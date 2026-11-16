using System.Security.Cryptography;
using System.Text;
using Microsoft.Data.Sqlite;

namespace Playground.Sync.Tests;

public class CopyTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task CopyAsync_KeepsEveryItem_WithEachFilesSizeTimeAndFingerprint()
    {
        using var app = new App();
        using var folder = new TempFolder();
        var syncer = app.Syncer(new FolderStore(folder.Path));

        var copy = await syncer.CopyAsync(ct: Ct);

        var manifest = copy.Manifest;
        Assert.Equal(["errors.log", "library/collection/aria.pdf", "library/minuet.mid", "library/minuet.pdf", "prima.db"], manifest.Files.Select(f => f.Path).Order(StringComparer.Ordinal));
        Assert.Equal(["prima.db", "library", "errors.log"], manifest.Items.Select(i => i.Name));
        Assert.Equal(40, manifest.Rows("prima.db", "Attempt"));
        Assert.Equal(2, manifest.Rows("prima.db", "Piece"));
        Assert.Equal("1.0.5", manifest.Version);
        Assert.Equal(SyncReason.Asked, manifest.Reason);
        foreach (var file in manifest.Files.Where(f => f.Path != "prima.db"))
        {
            var bytes = File.ReadAllBytes(app[file.Path]);
            Assert.Equal(bytes.Length, file.Size);
            Assert.Equal(Convert.ToHexStringLower(SHA256.HashData(bytes)), file.Sha256);
            Assert.Equal(File.GetLastWriteTimeUtc(app[file.Path]), file.Modified.UtcDateTime);
            Assert.Equal(bytes, File.ReadAllBytes(folder["blobs/" + file.Sha256]));
        }
        // The database's copy is a database of its own, with every row.
        var database = manifest.Files.Single(f => f.Path == "prima.db");
        Assert.Equal(40, App.Count(folder["blobs/" + database.Sha256], "Attempt"));
        Assert.Single(Directory.GetFiles(folder["copies"]));
        Assert.Equal(copy.Made, syncer.Status.Last!.Made);
        Assert.Equal("40 runs", copy.Summary);
    }

    [Fact]
    public async Task CopyAsync_WritesOnlyWhatChanged()
    {
        using var app = new App();
        using var folder = new TempFolder();
        var store = new WatchedStore(new FolderStore(folder.Path));
        var syncer = app.Syncer(store);
        await syncer.CopyAsync(ct: Ct);
        store.Clear();

        app.Write("library/minuet.pdf", App.Bytes(31_000, 4));
        app.Execute("INSERT INTO Attempt (Piece, Notes) VALUES ('aria', 'new')");
        var copy = await syncer.CopyAsync(ct: Ct);

        var pdf = copy.Manifest.Files.Single(f => f.Path == "library/minuet.pdf");
        var database = copy.Manifest.Files.Single(f => f.Path == "prima.db");
        Assert.Equal(new[] { "blobs/" + pdf.Sha256, "blobs/" + database.Sha256, SyncPaths.Copy(copy.Made) }.Order(StringComparer.Ordinal), store.Writes.Order(StringComparer.Ordinal));
        Assert.Equal(41, copy.Manifest.Rows("prima.db", "Attempt"));
        Assert.Equal($"Copied: 2 changed files, {SyncText.Size(pdf.Size + database.Size)} written.", syncer.Status.Message);
    }

    [Fact]
    public async Task CopyAsync_WithNothingChanged_WritesTheManifestAlone()
    {
        using var app = new App();
        using var folder = new TempFolder();
        var store = new WatchedStore(new FolderStore(folder.Path));
        var syncer = app.Syncer(store);
        var first = await syncer.CopyAsync(ct: Ct);
        store.Clear();

        var second = await syncer.CopyAsync(ct: Ct);

        // SQLite's backup of a database that hasn't changed is the same, byte for byte, so nothing but the manifest is new.
        Assert.Equal([SyncPaths.Copy(second.Made)], store.Writes);
        Assert.Equal([SyncPaths.Copy(first.Made)], store.Deletes);
        Assert.Equal("Copied: nothing had changed since the copy before.", syncer.Status.Message);
    }

    [Fact]
    public async Task CopyAsync_DeletesWhatNoCopyNeedsAnyMore()
    {
        using var app = new App();
        using var folder = new TempFolder();
        var syncer = app.Syncer(new FolderStore(folder.Path));
        var first = await syncer.CopyAsync(ct: Ct);
        var gone = first.Manifest.Files.Single(f => f.Path == "library/minuet.mid");

        File.Delete(app["library/minuet.mid"]);
        var second = await syncer.CopyAsync(ct: Ct);

        Assert.DoesNotContain(second.Manifest.Files, f => f.Path == "library/minuet.mid");
        Assert.False(File.Exists(folder["blobs/" + gone.Sha256]));
        Assert.Equal(second.Manifest.Files.Select(f => f.Sha256).Distinct().Order(), Directory.GetFiles(folder["blobs"]).Select(Path.GetFileName).Order());
        Assert.Equal([Path.GetFileName(SyncPaths.Copy(second.Made))], Directory.GetFiles(folder["copies"]).Select(Path.GetFileName));
    }

    [Fact]
    public async Task CopyAsync_ThatStopsPartWay_LeavesTheCopyBeforeWhole()
    {
        using var app = new App();
        using var folder = new TempFolder();
        var store = new WatchedStore(new FolderStore(folder.Path));
        var syncer = app.Syncer(store);
        var first = await syncer.CopyAsync(ct: Ct);

        app.Write("library/minuet.pdf", App.Bytes(31_000, 5));
        app.Write("library/new.pdf", App.Bytes(12_000, 6));
        store.Clear();
        store.FailAt = 2;
        await Assert.ThrowsAsync<IOException>(() => syncer.CopyAsync(ct: Ct));

        Assert.True(syncer.Status.Failed);
        Assert.Equal("No copy was made: the store went away. The last one is as it was.", syncer.Status.Message);
        var copies = await syncer.FindCopiesAsync(store, Ct);
        Assert.Equal(first.Made, Assert.Single(copies).Made);
        Assert.All(first.Manifest.Files, f => Assert.True(File.Exists(folder["blobs/" + f.Sha256])));
        Assert.Empty(Directory.GetFiles(folder.Path, "*.partial", SearchOption.AllDirectories));

        // The next copy finds what the one that stopped had written already.
        store.FailAt = null;
        store.Clear();
        var next = await syncer.CopyAsync(ct: Ct);
        Assert.DoesNotContain("blobs/" + next.Manifest.Files.Single(f => f.Path == "library/minuet.pdf").Sha256, store.Writes);
    }

    [Fact]
    public async Task CopyAsync_FileChangedWhileItWasCopied_IsKeptAsItIsNow()
    {
        using var app = new App();
        using var folder = new TempFolder();
        var store = new WatchedStore(new FolderStore(folder.Path));
        var syncer = app.Syncer(store);
        var log = File.ReadAllBytes(app.Log);
        var once = false;
        store.BeforeWrite = key =>
        {
            // The problem log is written to as the copy writes it.
            if (once || key != "blobs/" + Convert.ToHexStringLower(SHA256.HashData(log))) return;
            once = true;
            File.AppendAllText(app.Log, "=== 2026-11-15 10:01:00 +00:00 · Error · Prima.Sheet\nThe sheet stopped.\n\n");
        };

        var copy = await syncer.CopyAsync(ct: Ct);

        Assert.True(once);
        var kept = copy.Manifest.Files.Single(f => f.Path == "errors.log");
        var now = File.ReadAllBytes(app.Log);
        Assert.Equal(Convert.ToHexStringLower(SHA256.HashData(now)), kept.Sha256);
        Assert.Equal(now, File.ReadAllBytes(folder["blobs/" + kept.Sha256]));
    }

    [Fact]
    public async Task CopyAsync_NeverTakesAnExcludedFile_OrAnythingHalfWritten()
    {
        using var app = new App();
        using var folder = new TempFolder();
        File.WriteAllText(app["library/claude-key.txt"], "no key here, but excluded by its name all the same");
        app.Write("library/download.tmp", App.Bytes(100, 7));
        var syncer = new Syncer(app.Options() with { Items = [.. app.Options().Items, SyncItem.File("key", app.KeyFile)] }, [new FolderStore(folder.Path)]);

        var copy = await syncer.CopyAsync(ct: Ct);

        Assert.DoesNotContain(copy.Manifest.Files, f => f.Path.EndsWith("claude-key.txt", StringComparison.Ordinal) || f.Path == "key" || f.Path.EndsWith(".tmp", StringComparison.Ordinal));
    }

    [Fact]
    public async Task CopyAsync_StopsBeforeWritingAnything_WhenAFileHoldsASecret()
    {
        using var app = new App();
        using var folder = new TempFolder();
        var store = new WatchedStore(new FolderStore(folder.Path));
        var syncer = app.Syncer(store);
        File.AppendAllText(app.Log, $"=== 2026-11-15 10:02:00 +00:00 · Error · Prima.Claude\nThe key {App.Key} was refused.\n\n");

        var error = await Assert.ThrowsAsync<SyncSecretException>(() => syncer.CopyAsync(ct: Ct));

        Assert.Equal("errors.log", error.File);
        Assert.Empty(store.Writes);
        Assert.False(Directory.Exists(folder["blobs"]));
        Assert.Equal("No copy was made: errors.log holds a secret, which copies never take. The last one is as it was.", syncer.Status.Message);
        Assert.Empty(Directory.GetFileSystemEntries(app.WorkFolder));
    }

    [Fact]
    public async Task CopyAsync_FindsASecret_AcrossTheJoinBetweenTwoReads()
    {
        using var app = new App();
        using var folder = new TempFolder();
        var syncer = app.Syncer(new FolderStore(folder.Path));
        var bytes = App.Bytes(200_000, 8);
        Encoding.UTF8.GetBytes(App.Key).CopyTo(bytes, 81_920 - 20);
        app.Write("library/notes.pdf", bytes);

        var error = await Assert.ThrowsAsync<SyncSecretException>(() => syncer.CopyAsync(ct: Ct));

        Assert.Equal("library/notes.pdf", error.File);
    }

    [Fact]
    public async Task CopyAsync_TakesTheDatabaseWhole_WhileTheAppWritesToIt()
    {
        using var app = new App();
        using var folder = new TempFolder();
        var syncer = app.Syncer(new FolderStore(folder.Path));
        using var stop = new CancellationTokenSource();
        var writing = Task.Run(async () =>
        {
            await using var connection = new SqliteConnection($"Data Source={app.Database};Pooling=False;Default Timeout=30");
            await connection.OpenAsync(Ct);
            while (!stop.IsCancellationRequested)
            {
                await using var transaction = connection.BeginTransaction();
                for (var i = 0; i < 20; i++)
                {
                    await using var insert = connection.CreateCommand();
                    insert.CommandText = "INSERT INTO Attempt (Piece, Notes) VALUES ('aria', 'written during the copy')";
                    await insert.ExecuteNonQueryAsync(Ct);
                }
                await transaction.CommitAsync(Ct);
            }
        }, Ct);

        await Task.Delay(50, Ct);
        var copy = await syncer.CopyAsync(ct: Ct);
        await stop.CancelAsync();
        await writing;

        var database = folder["blobs/" + copy.Manifest.Files.Single(f => f.Path == "prima.db").Sha256];
        var rows = App.Count(database, "Attempt");
        Assert.Equal(copy.Manifest.Rows("prima.db", "Attempt"), rows);
        Assert.Equal(0, (rows - 40) % 20);
        Assert.True(App.Count(app.Database, "Attempt") >= rows);
    }

    [Fact]
    public async Task CopyAsync_SaysHowFarItIs_AndRunsOneAtATime()
    {
        using var app = new App();
        using var folder = new TempFolder();
        var syncer = app.Syncer(new FolderStore(folder.Path));
        var seen = new List<SyncStatus>();
        syncer.Changed += seen.Add;
        var parts = new List<double>();

        var copies = await Task.WhenAll(syncer.CopyAsync(progress: new Inline(parts.Add), ct: Ct), syncer.CopyAsync(ct: Ct));

        Assert.NotEqual(copies[0].Made, copies[1].Made);
        Assert.Contains(seen, s => s.Step == SyncStep.Copying && s.Progress is { Doing: var doing } && doing.StartsWith("Looking at what changed", StringComparison.Ordinal));
        Assert.Equal(1, parts[^1]);
        Assert.Equal(parts.Order(), parts);
        Assert.Equal(SyncStep.Idle, syncer.Status.Step);
        Assert.Null(syncer.Status.Progress);
    }

    [Fact]
    public async Task CopyAsync_AnotherStoreThatCantTakeIt_LeavesTheAppsOwnCopy()
    {
        using var app = new App();
        using var own = new TempFolder();
        using var other = new TempFolder();
        var away = new WatchedStore(new FolderStore(other.Path)) { Ready = false };
        var syncer = new Syncer(app.Options(), [new FolderStore(own.Path), away]);

        var copy = await syncer.CopyAsync(ct: Ct);

        Assert.Single(await syncer.FindCopiesAsync(new FolderStore(own.Path), Ct));
        Assert.Empty(away.Writes);
        Assert.False(syncer.Status.Failed);
        Assert.Equal(copy.Made, syncer.Status.Last!.Made);
    }

    [Fact]
    public void Items_EachNeedANameOfTheirOwn()
    {
        using var app = new App();
        Assert.Throws<ArgumentException>(() => new Syncer(app.Options() with { Items = [SyncItem.Folder("library", app.Library), SyncItem.File("library/x.pdf", app.Log)] }, [new FolderStore(app.WorkFolder)]));
        Assert.Throws<ArgumentException>(() => SyncItem.Folder("../library", app.Library));
        Assert.Throws<ArgumentException>(() => SyncItem.File("manifest.json", app.Log));
    }

    [Fact]
    public async Task FolderStore_KeepsItsKeysInside()
    {
        using var folder = new TempFolder();
        var store = new FolderStore(folder.Path);
        using var content = new MemoryStream([1, 2, 3]);

        await Assert.ThrowsAsync<ArgumentException>(() => store.WriteAsync("../outside", content, Ct));
        await Assert.ThrowsAsync<ArgumentException>(() => store.OpenReadAsync("blobs/../../x", Ct));
    }

    private sealed class Inline(Action<double> report) : IProgress<double>
    {
        public void Report(double value) => report(value);
    }
}
