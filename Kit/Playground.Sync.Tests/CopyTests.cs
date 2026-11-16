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

        // SQLite's backup of a database that hasn't changed is the same, byte for byte, so nothing but the manifest is new; and the
        // copy before stays, one of the store's three newest.
        Assert.Equal([SyncPaths.Copy(second.Made)], store.Writes);
        Assert.Empty(store.Deletes);
        Assert.Equal([first.Made, second.Made], (await syncer.FindCopiesAsync(store, Ct)).Select(c => c.Made).Order());
        Assert.Equal("Copied: nothing had changed since the copy before.", syncer.Status.Message);
    }

    // Once no copy kept needs a file's content, it goes: here the first copy's, once a newer copy is a week older than the last.
    [Fact]
    public async Task CopyAsync_DeletesWhatNoCopyNeedsAnyMore()
    {
        using var app = new App();
        using var folder = new TempFolder();
        var clock = new Clock();
        var start = clock.Now;
        var syncer = app.Syncer(new FolderStore(folder.Path), time: clock);
        var first = await syncer.CopyAsync(ct: Ct);
        var gone = first.Manifest.Files.Single(f => f.Path == "library/minuet.mid");

        File.Delete(app["library/minuet.mid"]);
        var kept = new List<SyncCopy>();
        foreach (var day in new[] { 8, 9, 10, 15 })
        {
            clock.Now = start.AddDays(day);
            app.Execute($"INSERT INTO Attempt (Piece, Notes) VALUES ('aria', 'day {day}')");
            kept.Add(await syncer.CopyAsync(ct: Ct));
        }

        Assert.All(kept, k => Assert.DoesNotContain(k.Manifest.Files, f => f.Path == "library/minuet.mid"));
        Assert.False(File.Exists(folder["blobs/" + gone.Sha256]));
        Assert.Equal(kept.SelectMany(k => k.Manifest.Files).Select(f => f.Sha256).Distinct().Order(), Directory.GetFiles(folder["blobs"]).Select(Path.GetFileName).Order());
        Assert.Equal(kept.Select(k => k.Made).OrderDescending(), (await syncer.FindCopiesAsync(new FolderStore(folder.Path), Ct)).Select(c => c.Made));
    }

    // A store keeps its three newest copies and, besides them, the newest at least a week old, and the oldest younger than that,
    // which comes to be it; content any kept copy needs stays, and nothing else.
    [Fact]
    public async Task CopyAsync_KeepsTheThreeNewest_AndTheNewestAWeekOld()
    {
        using var app = new App();
        using var folder = new TempFolder();
        var clock = new Clock();
        var start = clock.Now;
        var syncer = app.Syncer(new FolderStore(folder.Path), time: clock);
        // A copy a day for eleven days; the first has a file the rest don't.
        var taken = new List<SyncCopy> { await syncer.CopyAsync(ct: Ct) };
        File.Delete(app["library/minuet.mid"]);
        for (var day = 1; day <= 10; day++)
        {
            clock.Now = start.AddDays(day);
            app.Execute($"INSERT INTO Attempt (Piece, Notes) VALUES ('aria', 'day {day}')");
            taken.Add(await syncer.CopyAsync(ct: Ct));
        }

        // Days 10, 9 and 8; day 0, the newest at least a week old (days 1 to 3 went before they were); and day 4, the oldest
        // younger than a week, which comes to be the week-old one in three days.
        var copies = await syncer.FindCopiesAsync(new FolderStore(folder.Path), Ct);
        Assert.Equal([10, 9, 8, 4, 0], copies.Select(c => (int)(c.Made - start).TotalDays));
        Assert.Equal(copies.SelectMany(c => c.Manifest.Files).Select(f => f.Sha256).Distinct().Order(), Directory.GetFiles(folder["blobs"]).Select(Path.GetFileName).Order());
        Assert.True(File.Exists(folder["blobs/" + taken[0].Manifest.Files.Single(f => f.Path == "library/minuet.mid").Sha256]));
        Assert.All(taken.Where(t => !copies.Any(c => c.Made == t.Made)), t => Assert.False(File.Exists(folder["blobs/" + t.Manifest.Files.Single(f => f.Path == "prima.db").Sha256])));
        // The status says how many there are, and what they take: each piece of content once.
        Assert.Equal(5, syncer.Status.Copies);
        Assert.Equal(Directory.GetFiles(folder["blobs"]).Sum(f => new FileInfo(f).Length), syncer.Status.Stored);

        // Three days on, day 4 is the week-old copy, and day 0 goes.
        foreach (var day in new[] { 11, 12, 13 })
        {
            clock.Now = start.AddDays(day);
            app.Execute($"INSERT INTO Attempt (Piece, Notes) VALUES ('aria', 'day {day}')");
            await syncer.CopyAsync(ct: Ct);
        }
        Assert.Equal([13, 12, 11, 8, 4], (await syncer.FindCopiesAsync(new FolderStore(folder.Path), Ct)).Select(c => (int)(c.Made - start).TotalDays));
    }

    // Once a week (the first copy seven days or more after the last full check), every piece of content the store holds is read
    // against its fingerprint: one that no longer matches is written again from the file on the device, and logged. Till then,
    // content already there is taken as it is.
    [Fact]
    public async Task CopyAsync_ReadsWhatTheStoreHoldsOnceAWeek_AndWritesWhatsDamagedAgain()
    {
        using var app = new App();
        using var folder = new TempFolder();
        var clock = new Clock();
        var start = clock.Now;
        var logs = new Logs();
        var store = new WatchedStore(new FolderStore(folder.Path));
        var syncer = new Syncer(app.Options(), [store], clock, logs);
        var first = await syncer.CopyAsync(ct: Ct);
        Assert.Equal(first.Made, first.Manifest.Checked);

        // The PDF's content goes bad in the store, its size the same.
        var pdf = first.Manifest.Files.Single(f => f.Path == "library/minuet.pdf");
        var bytes = File.ReadAllBytes(folder["blobs/" + pdf.Sha256]);
        bytes[100] ^= 0xFF;
        File.WriteAllBytes(folder["blobs/" + pdf.Sha256], bytes);

        // Six days on, it's taken as it is.
        clock.Now = start.AddDays(6);
        store.Clear();
        var second = await syncer.CopyAsync(ct: Ct);
        Assert.DoesNotContain("blobs/" + pdf.Sha256, store.Writes);
        Assert.Equal(first.Made, second.Manifest.Checked);

        // A week after the last check, everything is read, and the PDF is written again from the device's file.
        clock.Now = start.AddDays(7);
        store.Clear();
        var third = await syncer.CopyAsync(ct: Ct);
        Assert.Equal(["blobs/" + pdf.Sha256, SyncPaths.Copy(third.Made)], store.Writes);
        Assert.Equal(File.ReadAllBytes(app["library/minuet.pdf"]), File.ReadAllBytes(folder["blobs/" + pdf.Sha256]));
        Assert.Equal(third.Made, third.Manifest.Checked);
        Assert.Contains(logs.Lines, l => l.StartsWith($"blobs/{pdf.Sha256} in {folder.Path} no longer matches its fingerprint", StringComparison.Ordinal));
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

    // When the secrets searched for change, every file is read again: a file's size and time say nothing of whether it holds the new
    // one. What was last searched for is kept as a mark in the app's own data: never in a copy, and nothing a secret can be got
    // back from.
    [Fact]
    public async Task CopyAsync_WhenTheSecretsChange_ReadsEveryFileAgain()
    {
        using var app = new App();
        using var folder = new TempFolder();
        using var own = new TempFolder();
        var key = App.Key;
        var syncer = new Syncer(app.Options() with { Secrets = _ => Task.FromResult<IReadOnlyCollection<string>>([key]), SecretsMark = own["searched"] }, [new FolderStore(folder.Path)]);
        var first = await syncer.CopyAsync(ct: Ct);

        var mark = File.ReadAllText(own["searched"]);
        Assert.DoesNotContain(key, mark);
        Assert.DoesNotContain(Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(key))), mark, StringComparison.OrdinalIgnoreCase);
        Assert.All(Directory.EnumerateFiles(folder.Path, "*", SearchOption.AllDirectories), f => Assert.DoesNotContain(mark, File.ReadAllText(f), StringComparison.Ordinal));

        // A new key turns up in a file whose size and time stay as they were.
        const string NewKey = "sk-ant-api03-ANewKeyThatCameAfterTheLastCopy-abcdef0123";
        var pdf = app["library/minuet.pdf"];
        var time = File.GetLastWriteTimeUtc(pdf);
        var bytes = File.ReadAllBytes(pdf);
        Encoding.UTF8.GetBytes(NewKey).CopyTo(bytes, 1000);
        File.WriteAllBytes(pdf, bytes);
        File.SetLastWriteTimeUtc(pdf, time);

        // While the key is the one searched for, the file is taken by its size and time, unread.
        var second = await syncer.CopyAsync(ct: Ct);
        Assert.Equal(first.Manifest.Files.Single(f => f.Path == "library/minuet.pdf").Sha256, second.Manifest.Files.Single(f => f.Path == "library/minuet.pdf").Sha256);

        // With the new key, every file is read again, and the copy stops on it.
        key = NewKey;
        var error = await Assert.ThrowsAsync<SyncSecretException>(() => syncer.CopyAsync(ct: Ct));
        Assert.Equal("library/minuet.pdf", error.File);
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
