using System.Globalization;

namespace Playground.Sync.Tests;

/// <summary>
/// From the review of main at a7ab94d: each test says what a copy, a held copy or a restore should do, and fails on that code.
/// The place in Syncer.cs is given with each.
/// </summary>
public class CopySafetyTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    // Syncer.cs:490-503 writes held.json without looking for one already there, and 432-444 (TidyAsync) keeps only the copy it
    // names. So a second update that loses data lets go of the copy held from the first, and the copy taken right after deletes
    // it: the fullest copy there was. The README: a held copy is "kept whatever copies come after, until it's restored or let go".
    [Fact]
    public async Task StartAsync_AfterASecondUpdateThatLostData_StillKeepsTheCopyHeldFromTheFirst()
    {
        using var app = new App();
        using var store = new TempFolder();
        var clock = new Clock();
        await app.Syncer(new FolderStore(store.Path), time: clock).CopyAsync(SyncReason.BeforeUpdate, "Prima 1.0.6", ct: Ct);

        // The first update loses runs: the copy from before it, with all 40, is held.
        clock.Now = clock.Now.AddMinutes(2);
        app.Execute("DELETE FROM Attempt WHERE Id > 30");
        var first = app.Syncer(new FolderStore(store.Path), app.Options("1.0.6"), clock);
        await first.StartAsync(Ct);
        Assert.Equal("prima.db's Attempt had 40 rows and has 30", first.Status.Held?.Why);

        // Nothing is done about it. The next update takes its copy first, and loses runs too.
        clock.Now = clock.Now.AddDays(1);
        await first.CopyAsync(SyncReason.BeforeUpdate, "Prima 1.0.7", ct: Ct);
        clock.Now = clock.Now.AddMinutes(2);
        app.Execute("DELETE FROM Attempt WHERE Id > 20");
        var second = app.Syncer(new FolderStore(store.Path), app.Options("1.0.7"), clock);
        await second.StartAsync(Ct);

        var copies = await second.FindCopiesAsync(new FolderStore(store.Path), Ct);
        Assert.Contains(copies, c => c.Manifest.Rows("prima.db", "Attempt") == 40);
    }

    // Syncer.cs:417-418 dates a copy a millisecond after the newest one there when that one is later than now, and :163 takes a
    // scheduled copy only when now is more than two days after the last. A copy once taken with the clock ahead (a year, here)
    // therefore stops every scheduled copy until the real date passes it.
    [Fact]
    public async Task StartAsync_AfterACopyTakenWithTheClockAhead_StillTakesItsCopies()
    {
        using var app = new App();
        using var store = new TempFolder();
        var clock = new Clock();
        var now = clock.Now;
        clock.Now = now.AddDays(365);
        await app.Syncer(new FolderStore(store.Path), time: clock).CopyAsync(ct: Ct);

        // The clock is put right, and three days of practice go by.
        clock.Now = now.AddDays(3);
        app.Execute("INSERT INTO Attempt (Piece, Notes) VALUES ('aria', 'three days on')");
        var syncer = app.Syncer(new FolderStore(store.Path), time: clock);
        await syncer.StartAsync(Ct);

        Assert.Contains(await syncer.FindCopiesAsync(new FolderStore(store.Path), Ct), c => c.Manifest.Rows("prima.db", "Attempt") == 41);
    }

    // Syncer.cs:205-207: a copy's Held comes from the store it was found in (:659-664), but restoring a held copy always deletes
    // the app's OWN store's held.json. Restoring a held copy from another folder so lets go of this install's own held copy, which
    // the copy taken after the restore then deletes.
    [Fact]
    public async Task RestoreAsync_AHeldCopyFromAnotherFolder_LeavesThisInstallsOwnHeldCopyHeld()
    {
        // An earlier install's folder, with a copy held from before an update of its own.
        using var earlier = new App();
        using var theirs = new TempFolder();
        await earlier.Syncer(new FolderStore(theirs.Path)).CopyAsync(SyncReason.BeforeUpdate, "Prima 1.0.6", ct: Ct);
        earlier.Execute("DELETE FROM Attempt WHERE Id > 5");
        await earlier.Syncer(new FolderStore(theirs.Path), earlier.Options("1.0.6")).StartAsync(Ct);

        // This install holds one too, in its own folder.
        using var app = new App();
        using var own = new TempFolder();
        await app.Syncer(new FolderStore(own.Path)).CopyAsync(SyncReason.BeforeUpdate, "Prima 1.0.6", ct: Ct);
        app.Execute("DELETE FROM Attempt WHERE Id > 30");
        var syncer = app.Syncer(new FolderStore(own.Path), app.Options("1.0.6"));
        await syncer.StartAsync(Ct);
        Assert.True(File.Exists(own["held.json"]));

        await syncer.RestoreAsync((await syncer.FindCopiesAsync(new FolderStore(theirs.Path), Ct)).Single(c => c.Held is not null), Ct);
        for (var i = 0; i < 200 && syncer.Status.Last?.Manifest.Reason != SyncReason.AfterRestore; i++) await Task.Delay(25, Ct);

        Assert.True(File.Exists(own["held.json"]));
        Assert.Contains(await syncer.FindCopiesAsync(new FolderStore(own.Path), Ct), c => c.Held is not null);
    }

    // Syncer.cs:198-207: the held copy's mark is deleted after everything has been put in place, inside the same piece of work.
    // When that delete fails, :721-725 and :767 say "Nothing was restored: … Your data is as it was." of data that was replaced,
    // and DataCopies.razor's Restore() then leaves Prima running on what it had in memory.
    [Fact]
    public async Task RestoreAsync_WhenTheHeldCopysMarkCantBeDeleted_DoesntSayNothingWasRestored()
    {
        using var app = new App();
        using var store = new TempFolder();
        await app.Syncer(new FolderStore(store.Path)).CopyAsync(SyncReason.BeforeUpdate, "Prima 1.0.6", ct: Ct);
        app.Execute("DELETE FROM Piece");
        await app.Syncer(new FolderStore(store.Path), app.Options("1.0.6")).StartAsync(Ct);
        Assert.Equal(0, app.Count("Piece"));

        var stuck = new NoDeleteStore(new FolderStore(store.Path));
        var syncer = app.Syncer(stuck, app.Options("1.0.6"));
        var held = (await syncer.FindCopiesAsync(stuck, Ct)).Single(c => c.Held is not null);
        try
        {
            await syncer.RestoreAsync(held, Ct);
        }
        catch (IOException)
        {
        }

        Assert.Equal(2, app.Count("Piece"));
        Assert.DoesNotContain("Nothing was restored", syncer.Status.Message);
    }

    // Syncer.cs:557-558 (Valid) reads a manifest's files and items without asking whether it has any, and SyncManifest.cs:60-70
    // takes "files": null as a manifest. One such file among the copies throws out of listing them (FindCopiesAsync's summary:
    // "one that can't be read is left out"), and with it every copy, the one before an update included.
    [Fact]
    public async Task FindCopiesAsync_LeavesOutAManifestWithNoListOfFiles()
    {
        using var app = new App();
        using var store = new TempFolder();
        var syncer = app.Syncer(new FolderStore(store.Path));
        var good = await syncer.CopyAsync(ct: Ct);
        File.WriteAllText(store["copies/20251115T100000000Z.json"], """{"app":"Prima","files":null}""");

        var copies = await syncer.FindCopiesAsync(new FolderStore(store.Path), Ct);

        Assert.Equal(good.Made, Assert.Single(copies).Made);
    }

    // Syncer.cs:400 opens each file to write it, and :406-411 tries again only for one that changed. A file that has gone since
    // it was listed (the problem log turned over to errors.old.log, a piece taken out of the Library) throws
    // FileNotFoundException out of the whole copy. The README: a file that changes while copied is "copied as it is now".
    [Fact]
    public async Task CopyAsync_WhenAFileGoesWhileItsCopied_CopiesTheRest()
    {
        using var app = new App();
        using var store = new TempFolder();
        var gone = false;
        var watched = new WatchedStore(new FolderStore(store.Path))
        {
            BeforeWrite = _ =>
            {
                if (gone) return;
                File.Delete(app["library/minuet.mid"]);
                gone = true;
            },
        };

        var copy = await app.Syncer(watched).CopyAsync(ct: Ct);

        Assert.DoesNotContain(copy.Manifest.Files, f => f.Path == "library/minuet.mid");
        Assert.Contains(copy.Manifest.Files, f => f.Path == "library/minuet.pdf");
    }

    // Syncer.cs:490-509 clears the status's sentence, then lets a failure to write held.json go by without a sentence or a line in
    // the log, and :168-174 swallows it ("Said in the status, and logged where it happened"). The data lost by the update is so
    // never mentioned at that start.
    [Fact]
    public async Task StartAsync_WhenTheCopyFromBeforeAnUpdateCantBeHeld_SaysSo()
    {
        using var app = new App();
        using var store = new TempFolder();
        await app.Syncer(new FolderStore(store.Path)).CopyAsync(SyncReason.BeforeUpdate, "Prima 1.0.6", ct: Ct);
        app.Execute("DELETE FROM Attempt WHERE Id > 10");
        var syncer = app.Syncer(new WatchedStore(new FolderStore(store.Path)) { FailAt = 1 }, app.Options("1.0.6"));

        await syncer.StartAsync(Ct);

        Assert.True(syncer.Status.Failed);
        Assert.NotEqual("", syncer.Status.Message);
    }

    // Syncer.cs:231-248: an export fingerprints every file, then reads each again for the archive, and nothing catches the
    // SyncChangedException a file written to in between throws (a copy tries that file again, :406-411). A problem logged while a
    // long export runs so fails it: "Nothing was exported: errors.log changed while it was being copied."
    [Fact]
    public async Task ExportAsync_WhenTheLogIsWrittenToMeanwhile_StillExports()
    {
        using var app = new App();
        using var store = new TempFolder();
        using var exports = new TempFolder();
        // The export asks the time once every file has been looked at, to name its archive: a problem is logged just then.
        var logged = false;
        var clock = new AskedClock(() =>
        {
            if (logged) return;
            logged = true;
            File.AppendAllText(app.Log, "=== 2026-11-15 10:00:05 +00:00 · Warning · Prima.Piece\nAnother page took long.\n\n");
        });

        var path = await app.Syncer(new FolderStore(store.Path), time: clock).ExportAsync(exports.Path, Ct);

        Assert.True(logged);
        Assert.True(File.Exists(path));
    }

    // Syncer.cs:225 writes the archive's date with the device's own calendar ($"{…:yyyy-MM-dd HHmm}" takes the current culture),
    // where every other date the copies write is invariant: in Thai, the year is 2569.
    [Fact]
    public async Task ExportAsync_NamesTheArchiveTheSameWhateverTheDevicesCalendar()
    {
        using var app = new App();
        using var store = new TempFolder();
        using var exports = new TempFolder();
        var culture = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = new CultureInfo("th-TH");
        try
        {
            var path = await app.Syncer(new FolderStore(store.Path), time: new Clock()).ExportAsync(exports.Path, Ct);

            Assert.Equal("Prima 2026-11-15 1000.zip", Path.GetFileName(path));
        }
        finally
        {
            CultureInfo.CurrentCulture = culture;
        }
    }
}

/// <summary>A store nothing can be deleted from: a folder gone read-only, or its permission taken back.</summary>
sealed class NoDeleteStore(ISyncStore inner) : ISyncStore
{
    public string Name => inner.Name;

    public bool SurvivesUninstall => inner.SurvivesUninstall;

    public Task<bool> ConnectAsync(CancellationToken ct) => inner.ConnectAsync(ct);

    public Task<IReadOnlyList<StoredObject>> ListAsync(string prefix, CancellationToken ct) => inner.ListAsync(prefix, ct);

    public Task<Stream?> OpenReadAsync(string key, CancellationToken ct) => inner.OpenReadAsync(key, ct);

    public Task WriteAsync(string key, Stream content, CancellationToken ct) => inner.WriteAsync(key, content, ct);

    public Task DeleteAsync(string key, CancellationToken ct) => Task.FromException(new IOException("the folder is read-only"));
}

/// <summary>A clock at <see cref="Clock"/>'s time that does something each time it's asked the time.</summary>
sealed class AskedClock(Action asked) : TimeProvider
{
    public override DateTimeOffset GetUtcNow()
    {
        asked();
        return new(2026, 11, 15, 10, 0, 0, TimeSpan.Zero);
    }

    public override TimeZoneInfo LocalTimeZone => TimeZoneInfo.Utc;
}
