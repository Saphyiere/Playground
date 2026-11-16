using Microsoft.Extensions.DependencyInjection;
using Playground.Updates;

namespace Playground.Sync.Tests;

public class StartTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task StartAsync_TakesAFirstCopy()
    {
        using var app = new App();
        using var store = new TempFolder();
        var syncer = app.Syncer(new FolderStore(store.Path));

        await syncer.StartAsync(Ct);

        Assert.Equal(SyncReason.Scheduled, syncer.Status.Last!.Manifest.Reason);
        Assert.True(syncer.Status.Known);
    }

    [Fact]
    public async Task StartAsync_LeavesACopyUnderTwoDaysOld_AndReplacesAnOlderOne()
    {
        using var app = new App();
        using var store = new TempFolder();
        var clock = new Clock();
        var first = await app.Syncer(new FolderStore(store.Path), time: clock).CopyAsync(ct: Ct);

        clock.Now = clock.Now.AddDays(1.9);
        var syncer = app.Syncer(new FolderStore(store.Path), time: clock);
        await syncer.StartAsync(Ct);
        Assert.Equal(first.Made, Assert.Single(await syncer.FindCopiesAsync(new FolderStore(store.Path), Ct)).Made);

        clock.Now = clock.Now.AddDays(0.2);
        await app.Syncer(new FolderStore(store.Path), time: clock).StartAsync(Ct);
        var copy = Assert.Single(await syncer.FindCopiesAsync(new FolderStore(store.Path), Ct));
        Assert.Equal(clock.Now, copy.Made);
        Assert.Equal(SyncReason.Scheduled, copy.Manifest.Reason);
    }

    [Fact]
    public async Task StartAsync_AfterAnUpdate_ChecksTheData_ThenReplacesTheCopyFromBefore()
    {
        using var app = new App();
        using var store = new TempFolder();
        var clock = new Clock();
        await app.Syncer(new FolderStore(store.Path), time: clock).CopyAsync(SyncReason.BeforeUpdate, "Prima 1.0.6", ct: Ct);

        // The update's first start: the same data, a newer version, and the practice that goes on.
        clock.Now = clock.Now.AddMinutes(2);
        app.Execute("INSERT INTO Attempt (Piece, Notes) VALUES ('aria', 'after the update')");
        var syncer = app.Syncer(new FolderStore(store.Path), app.Options("1.0.6"), clock);
        await syncer.StartAsync(Ct);

        var copy = Assert.Single(await syncer.FindCopiesAsync(new FolderStore(store.Path), Ct));
        Assert.Equal(SyncReason.AfterUpdate, copy.Manifest.Reason);
        Assert.Equal("1.0.6", copy.Manifest.Version);
        Assert.Equal(41, copy.Manifest.Rows("prima.db", "Attempt"));
        Assert.Null(syncer.Status.Held);
    }

    [Fact]
    public async Task StartAsync_AfterAnUpdateThatLostData_HoldsTheCopyFromBefore_UntilItsLetGo()
    {
        using var app = new App();
        using var store = new TempFolder();
        var clock = new Clock();
        var before = await app.Syncer(new FolderStore(store.Path), time: clock).CopyAsync(SyncReason.BeforeUpdate, "Prima 1.0.6", ct: Ct);

        clock.Now = clock.Now.AddMinutes(2);
        app.Execute("DELETE FROM Attempt WHERE Id > 10");
        var syncer = app.Syncer(new FolderStore(store.Path), app.Options("1.0.6"), clock);
        await syncer.StartAsync(Ct);

        var held = syncer.Status.Held;
        Assert.NotNull(held);
        Assert.Equal("Prima 1.0.6", held.Update);
        Assert.Equal("prima.db's Attempt had 40 rows and has 10", held.Why);
        // The data as it is now is copied too, beside the held copy, which later copies keep.
        clock.Now = clock.Now.AddDays(3);
        await syncer.CopyAsync(ct: Ct);
        var copies = await syncer.FindCopiesAsync(new FolderStore(store.Path), Ct);
        Assert.Equal(2, copies.Count);
        Assert.Equal(before.Made, copies.Single(c => c.Held is not null).Made);
        Assert.Equal(40, copies.Single(c => c.Held is not null).Manifest.Rows("prima.db", "Attempt"));
        Assert.All(copies.SelectMany(c => c.Manifest.Files), f => Assert.True(File.Exists(store["blobs/" + f.Sha256])));
        // A later start says it still.
        var later = app.Syncer(new FolderStore(store.Path), app.Options("1.0.6"), clock);
        await later.StartAsync(Ct);
        Assert.Equal(held, later.Status.Held);

        await later.ReleaseHeldAsync(Ct);
        Assert.Null(later.Status.Held);
        await later.CopyAsync(ct: Ct);
        Assert.Single(await later.FindCopiesAsync(new FolderStore(store.Path), Ct));
    }

    [Fact]
    public async Task StartAsync_AfterAnUpdate_WithTheDatabaseGone_HoldsTheCopyFromBefore()
    {
        using var app = new App();
        using var store = new TempFolder();
        await app.Syncer(new FolderStore(store.Path)).CopyAsync(SyncReason.BeforeUpdate, "Prima 1.0.6", ct: Ct);

        File.Delete(app.Database);
        var syncer = app.Syncer(new FolderStore(store.Path), app.Options("1.0.6"));
        await syncer.StartAsync(Ct);

        Assert.Equal("prima.db is gone", syncer.Status.Held?.Why);
    }

    // Only what's the person's own is counted after an update: what the app put there itself and takes out (a bundled piece) can go.
    [Fact]
    public async Task StartAsync_AfterAnUpdate_CountsOnlyWhatsThePersonsOwn()
    {
        using var app = new App();
        using var store = new TempFolder();
        await new Syncer(Counted(app), [new FolderStore(store.Path)]).CopyAsync(SyncReason.BeforeUpdate, "Prima 1.0.6", ct: Ct);

        // The update takes out a piece that isn't the person's: nothing is held.
        app.Execute("DELETE FROM Piece WHERE Id = 'aria'");
        var syncer = new Syncer(Counted(app, "1.0.6"), [new FolderStore(store.Path)]);
        await syncer.StartAsync(Ct);
        Assert.Null(syncer.Status.Held);

        // The next takes out one of theirs: the copy from before it is held, and says which.
        await syncer.CopyAsync(SyncReason.BeforeUpdate, "Prima 1.0.7", ct: Ct);
        app.Execute("DELETE FROM Piece WHERE Id = 'minuet'");
        var later = new Syncer(Counted(app, "1.0.7"), [new FolderStore(store.Path)]);
        await later.StartAsync(Ct);
        Assert.Equal("prima.db's Piece (minuets) had 1 rows and has 0", later.Status.Held?.Why);
    }

    // A copy taken before counts were kept is checked by its tables' rows, for what's counted whole: the rows a condition picks can't
    // be told from it.
    [Fact]
    public async Task StartAsync_AfterAnUpdate_ChecksACopyTakenBeforeCountsByItsTables()
    {
        using var app = new App();
        using var store = new TempFolder();
        await app.Syncer(new FolderStore(store.Path)).CopyAsync(SyncReason.BeforeUpdate, "Prima 1.0.6", ct: Ct);

        app.Execute("DELETE FROM Piece WHERE Id = 'minuet'; DELETE FROM Attempt WHERE Id > 30");
        var syncer = new Syncer(Counted(app, "1.0.6"), [new FolderStore(store.Path)]);
        await syncer.StartAsync(Ct);

        Assert.Equal("prima.db's Attempt had 40 rows and has 30", syncer.Status.Held?.Why);
    }

    // The check after an update runs as the app starts, before its screens can change the data; the copy after it keeps its wait.
    [Fact]
    public async Task StartAsync_AfterAnUpdate_ChecksAtOnce_AndCopiesAfterTheWait()
    {
        using var app = new App();
        using var store = new TempFolder();
        var before = await app.Syncer(new FolderStore(store.Path)).CopyAsync(SyncReason.BeforeUpdate, "Prima 1.0.6", ct: Ct);
        app.Execute("DELETE FROM Attempt WHERE Id > 10");
        var syncer = app.Syncer(new FolderStore(store.Path), app.Options("1.0.6") with { StartDelay = TimeSpan.FromMinutes(10) });
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(Ct);

        var starting = syncer.StartAsync(stop.Token);
        for (var i = 0; i < 400 && syncer.Status.Held is null; i++) await Task.Delay(25, Ct);

        Assert.Equal("prima.db's Attempt had 40 rows and has 10", syncer.Status.Held?.Why);
        Assert.Equal(before.Made, Assert.Single(await syncer.FindCopiesAsync(new FolderStore(store.Path), Ct)).Made);
        await stop.CancelAsync();
        await starting;
    }

    [Fact]
    public async Task StartAsync_AfterAnUpdateThatDidntInstall_TakesItAsAnyCopy()
    {
        using var app = new App();
        using var store = new TempFolder();
        var before = await app.Syncer(new FolderStore(store.Path)).CopyAsync(SyncReason.BeforeUpdate, "Prima 1.0.6", ct: Ct);

        // The update was cancelled: the same version starts, with less in it, and nothing is held.
        app.Execute("DELETE FROM Attempt");
        var syncer = app.Syncer(new FolderStore(store.Path));
        await syncer.StartAsync(Ct);

        Assert.Null(syncer.Status.Held);
        Assert.Equal(before.Made, Assert.Single(await syncer.FindCopiesAsync(new FolderStore(store.Path), Ct)).Made);
    }

    [Fact]
    public async Task RestoreAsync_TheHeldCopy_LetsItGo()
    {
        using var app = new App();
        using var store = new TempFolder();
        await app.Syncer(new FolderStore(store.Path)).CopyAsync(SyncReason.BeforeUpdate, "Prima 1.0.6", ct: Ct);
        app.Execute("DELETE FROM Piece");
        var syncer = app.Syncer(new FolderStore(store.Path), app.Options("1.0.6"));
        await syncer.StartAsync(Ct);

        await syncer.RestoreAsync((await syncer.FindCopiesAsync(new FolderStore(store.Path), Ct)).Single(c => c.Held is not null), Ct);

        Assert.Equal(2, app.Count("Piece"));
        Assert.Null(syncer.Status.Held);
        Assert.False(File.Exists(store["held.json"]));
    }

    [Fact]
    public async Task RestoreAsync_AnotherCopy_LeavesTheHeldOneHeld()
    {
        using var app = new App();
        using var store = new TempFolder();
        using var exports = new TempFolder();
        await app.Syncer(new FolderStore(store.Path)).CopyAsync(SyncReason.BeforeUpdate, "Prima 1.0.6", ct: Ct);
        app.Execute("DELETE FROM Piece WHERE Id = 'aria'");
        var syncer = app.Syncer(new FolderStore(store.Path), app.Options("1.0.6"));
        await syncer.StartAsync(Ct);
        var export = await syncer.OpenFileAsync(await syncer.ExportAsync(exports.Path, Ct), Ct);

        await syncer.RestoreAsync(export!, Ct);

        Assert.NotNull(syncer.Status.Held);
        Assert.True(File.Exists(store["held.json"]));
        for (var i = 0; i < 200 && syncer.Status.Last?.Manifest.Reason != SyncReason.AfterRestore; i++) await Task.Delay(25, Ct);
        Assert.Contains(await syncer.FindCopiesAsync(new FolderStore(store.Path), Ct), c => c.Held is not null);
    }

    [Fact]
    public async Task Updater_TakesAFullCopy_BeforeHandingTheUpdateToTheDevice()
    {
        using var app = new App();
        using var store = new TempFolder();
        var calls = new List<string>();
        var watched = new WatchedStore(new FolderStore(store.Path)) { BeforeWrite = key => { if (key.StartsWith("copies/", StringComparison.Ordinal)) calls.Add("copy kept"); } };
        var syncer = app.Syncer(watched);
        var messages = new List<string>();
        var updater = new Updater(new UpdateOptions("prima", "prima.apk") { DataFiles = [app.Database] }, new FakeInstaller(calls), handler: new FakeGitHub(), safeguard: new SyncSafeguard(syncer));
        updater.Changed += s => messages.Add(s.Message);
        await updater.CheckAsync(ct: Ct);

        await updater.InstallAsync(Ct);

        Assert.Equal(["copy kept", "installed"], calls);
        Assert.Equal(UpdateStep.Installing, updater.Status.Step);
        Assert.Equal($"Confirm the update to Prima 1.0.7. A copy of your data is in {store.Path}.", updater.Status.Message);
        Assert.Contains("Keeping a copy of your data first… 100%", messages);
        var copy = Assert.Single(await syncer.FindCopiesAsync(watched, Ct));
        Assert.Equal(SyncReason.BeforeUpdate, copy.Manifest.Reason);
        Assert.Equal("Prima 1.0.7", copy.Manifest.Update);
    }

    [Fact]
    public async Task Updater_WhenTheCopyCantBeTaken_InstallsNothing()
    {
        using var app = new App();
        using var store = new TempFolder();
        var calls = new List<string>();
        var syncer = app.Syncer(new WatchedStore(new FolderStore(store.Path)) { FailAt = 1 });
        var updater = new Updater(new UpdateOptions("prima", "prima.apk"), new FakeInstaller(calls), handler: new FakeGitHub(), safeguard: new SyncSafeguard(syncer));
        await updater.CheckAsync(ct: Ct);

        await updater.InstallAsync(Ct);

        Assert.Empty(calls);
        Assert.Equal(UpdateStep.Failed, updater.Status.Step);
        Assert.Equal("A copy of your data couldn't be kept first (the store went away), so nothing was installed.", updater.Status.Message);
    }

    // The app's options, counting as the person's own the runs and the minuets (as Prima counts its imported pieces, not the bundled).
    private static SyncOptions Counted(App app, string version = "1.0.5") => app.Options(version) with
    {
        Items =
        [
            SyncItem.Database("prima.db", app.Database) with { RequiredTables = ["Piece", "Attempt"], Counted = [new("Piece (minuets)", "Piece", "Title LIKE 'Minuet%'"), SyncCount.Rows("Attempt")] },
            .. app.Options().Items.Skip(1),
        ],
    };

    [Fact]
    public void AddSync_GivesTheUpdaterItsSafeguard()
    {
        using var app = new App();
        using var store = new TempFolder();
        var services = new ServiceCollection();
        services.AddSingleton<ISyncStore>(new FolderStore(store.Path, "Documents/Prima backups"));
        services.AddSync(_ => app.Options());
        services.AddStandInUpdates(new UpdateOptions("prima", "prima.apk"), "Prima");
        using var provider = services.BuildServiceProvider();

        var syncer = provider.GetRequiredService<Syncer>();

        Assert.Equal("Documents/Prima backups", syncer.Store.Name);
        Assert.IsType<SyncSafeguard>(provider.GetRequiredService<IUpdateSafeguard>());
        Assert.IsType<NoPicker>(provider.GetRequiredService<ISyncPicker>());
        Assert.NotNull(provider.GetRequiredService<Updater>());
    }
}
