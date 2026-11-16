using System.IO.Compression;
using System.Text;
using Microsoft.Data.Sqlite;

namespace Playground.Sync.Tests;

public class RestoreTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task RestoreAsync_PutsEverythingBack_OnAFreshInstall()
    {
        using var before = new App();
        using var store = new TempFolder();
        await before.Syncer(new FolderStore(store.Path)).CopyAsync(ct: Ct);
        File.Delete(before.KeyFile);

        // A fresh install: its own empty data, and the copy found in the folder the person picked.
        using var fresh = new App(empty: true);
        using var own = new TempFolder();
        var syncer = fresh.Syncer(new FolderStore(own.Path));
        var copy = Assert.Single(await syncer.FindCopiesAsync(new FolderStore(store.Path), Ct));
        await syncer.RestoreAsync(copy, Ct);

        Assert.Equal(before.Files().Keys.Order(), fresh.Files().Keys.Order());
        foreach (var (path, bytes) in before.Files().Where(f => f.Key != "prima.db")) Assert.Equal(bytes, fresh.Files()[path]);
        Assert.Equal(40, fresh.Count("Attempt"));
        Assert.Equal(2, fresh.Count("Piece"));
        Assert.Equal(File.GetLastWriteTimeUtc(before["library/minuet.pdf"]), File.GetLastWriteTimeUtc(fresh["library/minuet.pdf"]));
        Assert.Empty(Directory.GetFileSystemEntries(fresh.Folder, "*.sync-*", SearchOption.AllDirectories));
        Assert.StartsWith("Restored the copy from ", syncer.Status.Message);

        // Then this install keeps a copy of its own, of the data as restored.
        await WaitAsync(() => syncer.Status.Last is { Manifest.Reason: SyncReason.AfterRestore });
        Assert.Equal(40, Assert.Single(await syncer.FindCopiesAsync(new FolderStore(own.Path), Ct)).Manifest.Rows("prima.db", "Attempt"));
        Assert.StartsWith("Restored the copy from ", syncer.Status.Message);
    }

    // A restore keeps what's there first, held until it's restored or let go: so a wrong restore can be undone.
    [Fact]
    public async Task RestoreAsync_KeepsWhatsHereFirst_SoItCanBeUndone()
    {
        using var before = new App();
        using var store = new TempFolder();
        var older = await before.Syncer(new FolderStore(store.Path)).CopyAsync(ct: Ct);

        // This install has more since: five more runs, on a piece of its own.
        using var app = new App();
        using var own = new TempFolder();
        app.Execute("INSERT INTO Piece VALUES ('waltz', 'Waltz')");
        for (var i = 0; i < 5; i++) app.Execute("INSERT INTO Attempt (Piece, Notes) VALUES ('waltz', 'mine')");
        var syncer = app.Syncer(new FolderStore(own.Path));
        await syncer.RestoreAsync(Assert.Single(await syncer.FindCopiesAsync(new FolderStore(store.Path), Ct)), Ct);

        Assert.Equal(40, app.Count("Attempt"));
        var kept = Assert.Single(syncer.Status.Holding);
        Assert.Equal(SyncHeldFrom.Restore, kept.Held!.From);
        Assert.Equal($"the copy from {SyncText.When(older.Made)} was restored in its place", kept.Held.Why);
        Assert.Equal(SyncReason.BeforeRestore, kept.Manifest.Reason);
        Assert.Equal(45, kept.Manifest.Rows("prima.db", "Attempt"));
        Assert.Equal($"Restored the copy from {SyncText.When(older.Made)}. What was here is kept, held until you restore it or let it go.", syncer.Status.Message);

        // Restoring it undoes the restore.
        await syncer.RestoreAsync(kept, Ct);
        Assert.Equal(45, app.Count("Attempt"));
        Assert.Equal(3, app.Count("Piece"));
    }

    // Not on a fresh install: nothing there is the person's yet, so nothing is kept first.
    [Fact]
    public async Task RestoreAsync_OnAFreshInstall_KeepsNothingFirst()
    {
        using var before = new App();
        using var store = new TempFolder();
        await before.Syncer(new FolderStore(store.Path)).CopyAsync(ct: Ct);

        using var fresh = new App(empty: true);
        using var own = new TempFolder();
        var syncer = fresh.Syncer(new FolderStore(own.Path));
        await syncer.RestoreAsync(Assert.Single(await syncer.FindCopiesAsync(new FolderStore(store.Path), Ct)), Ct);

        Assert.Equal(40, fresh.Count("Attempt"));
        Assert.Empty(syncer.Status.Holding);
        Assert.DoesNotContain("kept", syncer.Status.Message);
    }

    // When what's here can't be kept first, nothing is restored and the status says why; restoring without keeping it is asked for
    // apart.
    [Fact]
    public async Task RestoreAsync_WhenWhatsHereCantBeKept_RestoresNothing_UnlessAskedNotToKeepIt()
    {
        using var before = new App();
        using var store = new TempFolder();
        await before.Syncer(new FolderStore(store.Path)).CopyAsync(ct: Ct);
        using var app = new App();
        using var own = new TempFolder();
        app.Execute("DELETE FROM Attempt WHERE Id > 5");
        var failing = new WatchedStore(new FolderStore(own.Path)) { FailAt = 1 };
        var syncer = app.Syncer(failing);
        var copy = Assert.Single(await syncer.FindCopiesAsync(new FolderStore(store.Path), Ct));

        var error = await Assert.ThrowsAsync<SyncKeepException>(() => syncer.RestoreAsync(copy, Ct));

        Assert.Equal("What's here couldn't be kept first (the store went away), so nothing was restored.", error.Message);
        Assert.Equal(error.Message, syncer.Status.Message);
        Assert.Equal(5, app.Count("Attempt"));
        Assert.Empty(Directory.GetFileSystemEntries(app.Folder, "*.sync-*", SearchOption.AllDirectories));

        failing.FailAt = null;
        await syncer.RestoreAsync(copy, keepWhatsHere: false, Ct);
        Assert.Equal(40, app.Count("Attempt"));
        Assert.Empty(syncer.Status.Holding);
    }

    // A change the app makes keeps a copy first, held, named for it. With one kept from before an update and one from before a
    // restore, each is held on its own, says what it's from and when, and is let go on its own.
    [Fact]
    public async Task KeepAsync_HoldsACopyNamedForTheChange_BesideTheOthersHeld()
    {
        using var app = new App();
        using var store = new TempFolder();
        var clock = new Clock();
        var update = await app.Syncer(new FolderStore(store.Path), time: clock).CopyAsync(SyncReason.BeforeUpdate, "Prima 1.0.6", ct: Ct);
        clock.Now = clock.Now.AddMinutes(1);
        app.Execute("DELETE FROM Attempt WHERE Id > 30");
        var syncer = app.Syncer(new FolderStore(store.Path), app.Options("1.0.6"), clock);
        await syncer.StartAsync(Ct);
        clock.Now = clock.Now.AddMinutes(1);
        await syncer.RestoreAsync((await syncer.FindCopiesAsync(new FolderStore(store.Path), Ct)).First(c => c.Held is null), Ct);
        clock.Now = clock.Now.AddMinutes(1);

        var kept = await syncer.KeepAsync("Start over", Ct);

        Assert.Equal(SyncHeldFrom.Change, kept.Held!.From);
        Assert.Equal("Start over", kept.Held.Why);
        Assert.Equal(SyncReason.BeforeChange, kept.Manifest.Reason);
        Assert.Equal(clock.Now, kept.Made);
        Assert.Equal([SyncHeldFrom.Change, SyncHeldFrom.Restore, SyncHeldFrom.Update], syncer.Status.Holding.Select(c => c.Held!.From));
        Assert.Equal(update.Made, syncer.Status.Holding[^1].Made);
        Assert.Equal("Kept a copy from before Start over, held until you restore it or let it go.", syncer.Status.Message);

        // The restore's is let go alone, and a later start reads the others back.
        await syncer.ReleaseHeldAsync(syncer.Status.Holding[1], Ct);
        Assert.Equal([SyncHeldFrom.Change, SyncHeldFrom.Update], syncer.Status.Holding.Select(c => c.Held!.From));
        var later = app.Syncer(new FolderStore(store.Path), app.Options("1.0.6"), clock);
        await later.RefreshAsync(Ct);
        Assert.Equal(syncer.Status.Holding.Select(c => c.Held), later.Status.Holding.Select(c => c.Held));
    }

    [Fact]
    public async Task RestoreAsync_FromACopyWithDamagedContent_ChangesNothing()
    {
        using var before = new App();
        using var store = new TempFolder();
        var copy = await before.Syncer(new FolderStore(store.Path)).CopyAsync(ct: Ct);
        var pdf = copy.Manifest.Files.Single(f => f.Path == "library/collection/aria.pdf");
        var damaged = File.ReadAllBytes(store["blobs/" + pdf.Sha256]);
        damaged[1000] ^= 0xFF;
        File.WriteAllBytes(store["blobs/" + pdf.Sha256], damaged);

        using var other = new App();
        other.Execute("INSERT INTO Piece VALUES ('waltz', 'Waltz')");
        var had = other.Files();
        var syncer = other.Syncer(new FolderStore(other.WorkFolder));
        var error = await Assert.ThrowsAsync<SyncException>(async () => await syncer.RestoreAsync((await syncer.FindCopiesAsync(new FolderStore(store.Path), Ct))[0], Ct));

        Assert.Equal("library/collection/aria.pdf in that copy is damaged, so nothing was restored.", error.Message);
        Assert.Equal(error.Message, syncer.Status.Message);
        Assert.Equal(3, other.Count("Piece"));
        Assert.Equal(had.Keys.Order(), other.Files().Keys.Order());
        Assert.Empty(Directory.GetFileSystemEntries(other.Folder, "*.sync-*", SearchOption.AllDirectories));
    }

    [Fact]
    public async Task RestoreAsync_ReplacesWholeItems_AndOnlyTheOnesTheCopyHas()
    {
        using var before = new App();
        using var store = new TempFolder();
        // A copy from an app that kept the database and library only.
        var options = before.Options() with { Items = [.. before.Options().Items.Where(i => i.Name != "errors.log")] };
        await new Syncer(options, [new FolderStore(store.Path)]).CopyAsync(ct: Ct);

        using var other = new App();
        other.Write("library/only-here.pdf", App.Bytes(500, 11));
        File.WriteAllText(other.Log, "this install's own problems");
        var syncer = other.Syncer(new FolderStore(other.WorkFolder));
        await syncer.RestoreAsync((await syncer.FindCopiesAsync(new FolderStore(store.Path), Ct))[0], Ct);

        Assert.False(File.Exists(other["library/only-here.pdf"]));
        Assert.Equal("this install's own problems", File.ReadAllText(other.Log));
    }

    [Fact]
    public async Task RestoreAsync_PutsItInPlace_WhileTheAppLeavesItsDataAlone()
    {
        using var before = new App();
        using var store = new TempFolder();
        await before.Syncer(new FolderStore(store.Path)).CopyAsync(ct: Ct);

        using var fresh = new App(empty: true);
        var during = new List<long>();
        var options = fresh.Options() with
        {
            WhileRestoring = async work =>
            {
                during.Add(fresh.Count("Attempt"));
                await work();
                during.Add(fresh.Count("Attempt"));
            },
        };
        var syncer = new Syncer(options, [new FolderStore(fresh.WorkFolder)]);
        await syncer.RestoreAsync((await syncer.FindCopiesAsync(new FolderStore(store.Path), Ct))[0], Ct);

        Assert.Equal([0, 40], during);
    }

    [Fact]
    public async Task RestoreAsync_AnotherAppsDatabase_IsRefused()
    {
        using var app = new App();
        using var work = new TempFolder();
        var stranger = work["workout.db"];
        using (var connection = new SqliteConnection($"Data Source={stranger};Pooling=False"))
        {
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = "CREATE TABLE Exercise (Id INTEGER PRIMARY KEY); INSERT INTO Exercise DEFAULT VALUES";
            command.ExecuteNonQuery();
        }
        var syncer = app.Syncer(new FolderStore(app.WorkFolder));

        var copy = await syncer.OpenFileAsync(stranger, Ct);
        var error = await Assert.ThrowsAsync<SyncException>(() => syncer.RestoreAsync(copy!, Ct));

        Assert.Equal("prima.db in that copy isn't Prima's (it has no Piece table), so nothing was restored.", error.Message);
        Assert.Equal(40, app.Count("Attempt"));
    }

    [Fact]
    public async Task OpenFileAsync_ADatabaseOnItsOwn_RestoresJustTheDatabase()
    {
        using var before = new App();
        using var work = new TempFolder();
        before.Execute("INSERT INTO Piece VALUES ('waltz', 'Waltz')");
        File.Copy(before.Database, work["prima-2026-11-08.db"]);

        using var other = new App();
        other.Write("library/mine.pdf", App.Bytes(700, 12));
        var syncer = other.Syncer(new FolderStore(other.WorkFolder));
        var copy = await syncer.OpenFileAsync(work["prima-2026-11-08.db"], Ct);

        Assert.NotNull(copy);
        Assert.Equal("prima-2026-11-08.db", copy.Where);
        Assert.Equal(["prima.db"], copy.Manifest.Items.Select(i => i.Name));
        await syncer.RestoreAsync(copy, Ct);
        Assert.Equal(3, other.Count("Piece"));
        Assert.True(File.Exists(other["library/mine.pdf"]));
    }

    [Fact]
    public async Task ExportAsync_WritesEverythingIntoOneArchive_ThatRestores()
    {
        using var before = new App();
        using var exports = new TempFolder();
        var syncer = before.Syncer(new FolderStore(before.WorkFolder), time: new Clock());

        var path = await syncer.ExportAsync(exports.Path, Ct);

        Assert.Equal("Prima 2026-11-15 1000.zip", Path.GetFileName(path));
        using (var archive = ZipFile.OpenRead(path))
        {
            Assert.Equal(["errors.log", "library/collection/aria.pdf", "library/minuet.mid", "library/minuet.pdf", "manifest.json", "prima.db"], archive.Entries.Select(e => e.FullName).Order(StringComparer.Ordinal));
        }
        Assert.DoesNotContain(App.Key, Encoding.UTF8.GetString(File.ReadAllBytes(path)));
        Assert.StartsWith("Exported everything to Prima 2026-11-15 1000.zip, ", syncer.Status.Message);

        using var fresh = new App(empty: true);
        var restoring = fresh.Syncer(new FolderStore(fresh.WorkFolder));
        var copy = await restoring.OpenFileAsync(path, Ct);
        Assert.Equal("Prima 2026-11-15 1000.zip", copy!.Where);
        await restoring.RestoreAsync(copy, Ct);
        Assert.Equal(40, fresh.Count("Attempt"));
        Assert.Equal(File.ReadAllBytes(before["library/collection/aria.pdf"]), File.ReadAllBytes(fresh["library/collection/aria.pdf"]));
    }

    [Fact]
    public async Task OpenFileAsync_AnArchiveThatWouldReachOutside_IsNotACopy()
    {
        using var app = new App();
        using var work = new TempFolder();
        var path = work["sneaky.zip"];
        var manifest = new SyncManifest
        {
            App = "Prima",
            Items = [new SyncManifestItem("library", SyncItemKind.Folder)],
            Files = [new SyncFile("library/../../outside.txt", 3, DateTimeOffset.UtcNow, new string('a', 64))],
        };
        using (var archive = ZipFile.Open(path, ZipArchiveMode.Create))
        {
            using var json = archive.CreateEntry("manifest.json").Open();
            json.Write(manifest.ToJson());
        }

        Assert.Null(await app.Syncer(new FolderStore(app.WorkFolder)).OpenFileAsync(path, Ct));
    }

    [Fact]
    public async Task OpenFileAsync_AnotherAppsExport_IsNotACopy()
    {
        using var app = new App();
        using var exports = new TempFolder();
        var workout = new Syncer(new SyncOptions("Workout") { Items = [SyncItem.File("errors.log", app.Log)], WorkFolder = app.WorkFolder }, [new FolderStore(app.WorkFolder)]);
        var path = await workout.ExportAsync(exports.Path, Ct);

        Assert.Null(await app.Syncer(new FolderStore(app.WorkFolder)).OpenFileAsync(path, Ct));
    }

    [Fact]
    public async Task CustomItem_IsWrittenOutForACopy_AndReadBackOnRestore()
    {
        using var before = new App();
        using var store = new TempFolder();
        var preferences = new Preferences { Values = { ["look"] = "vinyl", ["asked"] = "yes" } };
        await new Syncer(before.Options() with { Items = [.. before.Options().Items, SyncItem.Custom("preferences", preferences)] }, [new FolderStore(store.Path)]).CopyAsync(ct: Ct);

        using var fresh = new App(empty: true);
        var restored = new Preferences();
        var syncer = new Syncer(fresh.Options() with { Items = [.. fresh.Options().Items, SyncItem.Custom("preferences", restored)] }, [new FolderStore(fresh.WorkFolder)]);
        await syncer.RestoreAsync((await syncer.FindCopiesAsync(new FolderStore(store.Path), Ct))[0], Ct);

        Assert.Equal(preferences.Values, restored.Values);
    }

    private static async Task WaitAsync(Func<bool> done)
    {
        for (var i = 0; i < 200 && !done(); i++) await Task.Delay(25, Ct);
        Assert.True(done());
    }

    /// <summary>Something kept outside files, written out as one file per value.</summary>
    private sealed class Preferences : ISyncExport
    {
        public Dictionary<string, string> Values { get; } = [];

        public async Task WriteAsync(string folder, CancellationToken ct)
        {
            foreach (var (key, value) in Values) await File.WriteAllTextAsync(Path.Combine(folder, key + ".txt"), value, ct);
        }

        public async Task ReadAsync(string folder, CancellationToken ct)
        {
            Values.Clear();
            foreach (var file in Directory.GetFiles(folder)) Values[Path.GetFileNameWithoutExtension(file)] = await File.ReadAllTextAsync(file, ct);
        }
    }
}
