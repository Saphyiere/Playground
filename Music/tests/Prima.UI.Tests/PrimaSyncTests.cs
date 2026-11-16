using System.IO.Compression;
using System.Text;
using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Playground.AI.Assist;
using Playground.Sync;
using Prima.UI.Components;
using Prima.UI.Host;
using Prima.UI.Storage;

namespace Prima.UI.Tests;

/// <summary>Prima's copies (Storage/PrimaSync): everything it keeps but Claude's key, and all of it back on a fresh install.</summary>
public class PrimaSyncTests
{
    private const string Key = "sk-ant-api03-PrimaTestKeyKeptInSecureStorage-abcdefghijklmnop";
    private const string WaitingKey = "sk-ant-api03-PrimaTestKeyWaitingInItsFile-qrstuvwxyz012345";

    private static CancellationToken Ct => Xunit.TestContext.Current.CancellationToken;

    [Fact]
    public async Task ACopy_HoldsEverythingPrimaKeeps_AndNeverClaudesKey()
    {
        await using var tablet = new Tablet();
        await tablet.FillAsync();
        await tablet.Keys.SetAsync(Key);
        File.WriteAllText(tablet.KeyFile, WaitingKey);
        // Even a key file that turns up among the imported music is never taken.
        File.WriteAllText(Path.Combine(tablet.Store.FilesPath, AssistKey.FileName), WaitingKey);

        var copy = await tablet.Sync.CopyAsync(ct: Ct);
        var export = await tablet.Sync.ExportAsync(tablet.Exports, Ct);

        Assert.Equal(["prima.db", "library", "prima.conversion", "errors.log", "errors.old.log"], copy.Manifest.Items.Select(i => i.Name));
        Assert.Contains(copy.Manifest.Files, f => f.Path == "library/import-1.pdf");
        Assert.Contains(copy.Manifest.Files, f => f.Path == $"prima.conversion/{tablet.Artifact}.bin");
        Assert.Contains(copy.Manifest.Files, f => f.Path == "errors.log");
        Assert.DoesNotContain(copy.Manifest.Files, f => f.Path.Contains("claude-key", StringComparison.Ordinal));
        Assert.Equal(["Setting", "Track", "Attempt", "PracticeDay"], PrimaSync.Tables.Where(t => copy.Manifest.Rows("prima.db", t) is not null));
        // Not a byte of either key in anything kept, nor in the export's files.
        foreach (var file in Directory.EnumerateFiles(tablet.Copies, "*", SearchOption.AllDirectories)) AssertKeyless(File.ReadAllBytes(file), file);
        using var archive = ZipFile.OpenRead(export);
        foreach (var entry in archive.Entries)
        {
            using var content = new MemoryStream();
            using (var stream = entry.Open()) stream.CopyTo(content);
            AssertKeyless(content.ToArray(), entry.FullName);
        }
        Assert.DoesNotContain(archive.Entries, e => e.FullName.Contains("claude-key", StringComparison.Ordinal));
    }

    // The mark of the key searched for is kept beside the data file, in no item, so it's never in a copy; and it isn't the key.
    [Fact]
    public async Task TheMarkOfTheKeySearchedFor_IsKeptBesideTheData_NeverInACopy()
    {
        await using var tablet = new Tablet();
        await tablet.FillAsync();
        await tablet.Keys.SetAsync(Key);

        await tablet.Sync.CopyAsync(ct: Ct);

        var mark = tablet.Sync.Options.SecretsMark!;
        Assert.Equal(Path.GetDirectoryName(tablet.Store.DatabasePath), Path.GetDirectoryName(mark));
        Assert.DoesNotContain(Key, File.ReadAllText(mark));
        Assert.DoesNotContain(tablet.Sync.Status.Last!.Manifest.Files, f => f.Path.Contains(Path.GetFileName(mark), StringComparison.Ordinal));
    }

    [Fact]
    public async Task ACopy_ThatWouldHoldTheKey_StopsBeforeWritingAnything()
    {
        await using var tablet = new Tablet();
        await tablet.FillAsync();
        await tablet.Keys.SetAsync(Key);
        tablet.Problems.Keep("Error", "Prima.UI.Pages.Settings", $"The key {Key} was refused");

        var error = await Assert.ThrowsAsync<SyncSecretException>(() => tablet.Sync.CopyAsync(ct: Ct));

        Assert.Equal("errors.log", error.File);
        Assert.False(Directory.Exists(tablet.Copies) && Directory.EnumerateFiles(tablet.Copies, "*", SearchOption.AllDirectories).Any());
        Assert.Equal("No copy was made: errors.log holds a secret, which copies never take. The last one is as it was.", tablet.Sync.Status.Message);
    }

    [Fact]
    public async Task ARestore_OnAFreshInstall_BringsBackEverything_ButTheProblemLog()
    {
        await using var before = new Tablet();
        await before.FillAsync();
        await before.Sync.CopyAsync(ct: Ct);

        // Prima installed afresh: nothing of its own yet but a problem it kept, and the copy before found with the tablet's folder picker.
        await using var fresh = new Tablet();
        await fresh.Store.GetSettingsAsync();
        fresh.Problems.Keep("Warning", "Prima.UI.Pages.Library", "The Library took long to open");
        var copy = Assert.Single(await fresh.Sync.FindCopiesAsync(new FolderStore(before.Copies, "Documents/Prima backups/Installed 2026-10-01 090000"), Ct));
        Assert.Equal("Nothing played yet · 1 imported file", copy.Summary);
        await fresh.Sync.RestoreAsync(copy, Ct);

        var settings = new PrimaSettings();
        await fresh.Store.LoadAsync(settings);
        Assert.Equal("Ada", settings.Name);
        Assert.Equal("Gymnopédie No. 1", (await fresh.Store.GetPieceAsync("import-1"))?.Title);
        Assert.Equal(File.ReadAllBytes(Path.Combine(before.Store.FilesPath, "import-1.pdf")), File.ReadAllBytes(Path.Combine(fresh.Store.FilesPath, "import-1.pdf")));
        Assert.Equal(Tablet.Rendering, await fresh.Store.GetConversionArtifactAsync(before.Artifact!, Ct));
        // The problem log is in the copy, but a restore leaves this install's as it is.
        Assert.Contains(copy.Manifest.Files, f => f.Path == "errors.log");
        Assert.Equal(["The Library took long to open"], fresh.Problems.Recent().Select(p => p.Message));
        // The fresh install keeps a copy of its own of all of it, in the background.
        for (var i = 0; i < 200 && fresh.Sync.Status.Last?.Manifest.Reason != SyncReason.AfterRestore; i++) await Task.Delay(25, Ct);
        Assert.Contains(fresh.Sync.Status.Last!.Manifest.Files, f => f.Path == "library/import-1.pdf");
    }

    // After an update, only the owner's data is counted: the settings, tracks, runs, practice days and sessions, routines and the
    // pieces imported, and the library's files as before. The bundled and collection pieces, and their excerpts, aren't.
    [Fact]
    public async Task AfterAnUpdate_OnlyTheOwnersDataIsCounted()
    {
        Assert.Equal(["Setting", "Track", "Attempt", "PracticeDay", "PracticeSession", "RoutineGeneration", "RoutineDefinition", "RoutineDay", "RoutineExecution", "RoutineAttempt", "Piece"],
            PrimaSync.Counted.Select(c => c.Table));
        Assert.Equal(["Piece"], PrimaSync.Counted.Where(c => c.Where is not null).Select(c => c.Table));
        Assert.Equal("Origin = 'import'", PrimaSync.Counted.Single(c => c.Table == "Piece").Where);

        await using var tablet = new Tablet();
        await tablet.FillAsync();
        await tablet.Store.SavePieceAsync(new PieceRecord("collection-1", "collection", "A Minuet", "Bach", null, "pdf", "collection-1.pdf", null, 2, 2, null, 16,
            null, null, null, null, null, null, 72, null, null, DateTimeOffset.Now, null), null, [new Prima.Core.Library.BundledExcerpt(1, 4, 4, 0.5)]);
        var store = new FolderStore(tablet.Copies, "Documents/Prima backups");
        await new Syncer(tablet.Sync.Options with { Version = "1.0.11" }, [store]).CopyAsync(SyncReason.BeforeUpdate, "Prima 1.0.12", ct: Ct);

        // 1.0.12 takes the collection's piece out, and its excerpts with it: nothing is held.
        await tablet.Store.DeletePieceAsync("collection-1");
        var updated = new Syncer(tablet.Sync.Options with { Version = "1.0.12", StartDelay = TimeSpan.Zero }, [store]);
        await updated.StartAsync(Ct);
        Assert.True(updated.Status.Held is null, updated.Status.Held?.Why);

        // 1.0.13 loses a piece the owner imported: the copy from before it is held, saying so.
        await updated.CopyAsync(SyncReason.BeforeUpdate, "Prima 1.0.13", ct: Ct);
        await tablet.Store.DeletePieceAsync("import-1");
        var later = new Syncer(tablet.Sync.Options with { Version = "1.0.13", StartDelay = TimeSpan.Zero }, [store]);
        await later.StartAsync(Ct);
        Assert.Equal("prima.db's Piece (imported) had 1 rows and has 0", later.Status.Held?.Why);

        // And 1.0.14 loses a file of the library: held too.
        await later.ReleaseHeldAsync(Ct);
        await later.CopyAsync(SyncReason.BeforeUpdate, "Prima 1.0.14", ct: Ct);
        File.Delete(Path.Combine(tablet.Store.FilesPath, "import-1.pdf"));
        var last = new Syncer(tablet.Sync.Options with { Version = "1.0.14", StartDelay = TimeSpan.Zero }, [store]);
        await last.StartAsync(Ct);
        Assert.Equal("library/import-1.pdf is gone", last.Status.Held?.Why);
    }

    [Fact]
    public async Task YourData_ShowsTheLastCopy_BacksUpNow_AndRestoresTheCopyChosen()
    {
        await using var host = new ComponentHost();
        var sync = host.Context.Services.GetRequiredService<Syncer>();
        await host.Store.SaveAsync(new PrimaSettings { Name = "Ada" });
        var view = host.Context.Render<DataCopies>();
        view.WaitForAssertion(() => Assert.Contains("None yet", view.Find(".key-figure").TextContent));

        view.FindAll("button").Single(b => b.TextContent == "Back up now").Click();

        view.WaitForAssertion(() => Assert.Equal(SyncText.When(sync.Status.Last!.Made), view.Find(".key-figure").TextContent));
        Assert.Contains("Nothing played yet", view.Find(".control-note").TextContent);
        Assert.StartsWith("Copied: ", view.Find("[role=status]").TextContent);

        // Changed since, then the copy chosen and restored: the name is the copy's again, and every screen is drawn afresh.
        await host.Store.SaveAsync(new PrimaSettings { Name = "Someone else" });
        view.FindAll("button").Single(b => b.TextContent == "Restore from a backup…").Click();
        view.WaitForElement(".copy-choice").Click();
        view.WaitForAssertion(() => Assert.Contains("Everything in Prima now is replaced by the copy from", view.Markup));
        Assert.Equal("off", view.FindAll("button").Single(b => b.TextContent == "Restore this copy").GetAttribute("data-piano"));
        view.FindAll("button").Single(b => b.TextContent == "Restore this copy").Click();

        // It lands on App & data, where Your data says the copy was restored.
        var navigation = host.Context.Services.GetRequiredService<NavigationManager>();
        view.WaitForAssertion(() => Assert.EndsWith("/settings?tab=app&section=data", navigation.Uri, StringComparison.Ordinal), TimeSpan.FromSeconds(10));
        var settings = new PrimaSettings();
        await host.Store.LoadAsync(settings);
        Assert.Equal("Ada", settings.Name);
        Assert.Equal("Ada", host.Context.Services.GetRequiredService<PrimaSettings>().Name);
        var page = host.Context.Render<Prima.UI.Pages.Settings>();
        page.WaitForAssertion(() => Assert.Contains(page.FindAll("[role=status]"), s => s.TextContent.StartsWith("Restored the copy from ", StringComparison.Ordinal)));
        Assert.Equal("App & data", page.FindAll("[role=tab][aria-selected=true]").Single().TextContent);
        Assert.Equal("data", host.Browser.ShownSections.FirstOrDefault() ?? await host.Browser.SectionShown.Task.WaitAsync(TimeSpan.FromSeconds(10), Ct));

        // The copy of the data as restored, taken in the background, finishes before the test's data goes.
        for (var i = 0; i < 400 && (sync.Status.Busy || sync.Status.Last?.Manifest.Reason != SyncReason.AfterRestore); i++) await Task.Delay(25, Ct);
        Assert.Equal(SyncReason.AfterRestore, sync.Status.Last?.Manifest.Reason);
        Directory.Delete(sync.Options.WorkFolder, recursive: true);
    }

    // Each copy held says what it was kept from before (an update, a restore, Start over) and when, and has Restore and Let go of
    // its own: letting go of one leaves the others held.
    [Fact]
    public async Task YourData_ShowsEachCopyHeld_WhatItsFrom_AndWhen_EachWithRestoreAndLetGo()
    {
        await using var host = new ComponentHost();
        var sync = host.Context.Services.GetRequiredService<Syncer>();
        var piece = await host.AddPiece();
        await host.Store.AddMinutesAsync(DateOnly.FromDateTime(DateTime.Now), 5);
        // Kept from before an update that lost the piece; from before a restore; and from before Start over.
        await new Syncer(sync.Options with { Version = "1.0.11" }, [sync.Store]).CopyAsync(SyncReason.BeforeUpdate, "Prima 1.0.12", ct: Ct);
        await host.Store.DeletePieceAsync(piece.Id);
        // Its start clears a work folder of its own: the host's copies are in the host's (the store when none is registered).
        var start = Path.Combine(Path.GetTempPath(), $"prima-sync-start-{Guid.NewGuid():N}");
        await new Syncer(sync.Options with { Version = "1.0.12", StartDelay = TimeSpan.Zero, WorkFolder = start }, [sync.Store]).StartAsync(Ct);
        await sync.RestoreAsync((await sync.FindCopiesAsync(sync.Store, Ct)).First(c => c.Held is null), Ct);
        await sync.KeepAsync("Start over", Ct);

        var view = host.Context.Render<DataCopies>();

        var held = (await sync.FindCopiesAsync(sync.Store, Ct)).Where(c => c.Held is not null).ToList();
        view.WaitForAssertion(() => Assert.Equal(
            [$"Kept from before Start over · {SyncText.When(held[0].Made)}", $"Kept from before a restore · {SyncText.When(held[1].Made)}", $"Kept from before the update to Prima 1.0.12 · {SyncText.When(held[2].Made)}"],
            view.FindAll(".copies-held-head").Select(h => h.TextContent)));
        Assert.All(view.FindAll(".copies-held"), h => Assert.Equal(["Restore it…", "Let it go"], h.QuerySelectorAll("button").Select(b => b.TextContent)));
        Assert.StartsWith("After the update, Prima's data had less than the copy taken just before it: prima.db's Piece (imported) had 1 rows and has 0.", view.FindAll(".copies-held")[2].QuerySelector(".section-note")!.TextContent);

        // Let go, the restore's is held no more, and the others stay.
        await view.InvokeAsync(() => view.FindAll(".copies-held")[1].QuerySelectorAll("button").Single(b => b.TextContent == "Let it go").Click());
        view.WaitForAssertion(() => Assert.Equal(2, view.FindAll(".copies-held").Count));
        Assert.Equal([SyncHeldFrom.Change, SyncHeldFrom.Update], sync.Status.Holding.Select(c => c.Held!.From));

        // Restore it… chooses that very copy.
        await view.InvokeAsync(() => view.FindAll(".copies-held")[1].QuerySelectorAll("button").Single(b => b.TextContent == "Restore it…").Click());
        view.WaitForAssertion(() => Assert.Equal("true", view.FindAll(".copy-choice").Single(c => c.TextContent.Contains("from before the update to Prima 1.0.12", StringComparison.Ordinal)).GetAttribute("aria-pressed")));
        Directory.Delete(sync.Options.WorkFolder, recursive: true);
        if (Directory.Exists(start)) Directory.Delete(start, recursive: true);
    }

    // When what's here can't be kept first, nothing is restored, the note says why, and restoring without keeping it is offered apart.
    [Fact]
    public async Task YourData_WhenWhatsHereCantBeKept_OffersToRestoreWithoutIt()
    {
        await using var host = new ComponentHost();
        var sync = host.Context.Services.GetRequiredService<Syncer>();
        await host.Store.SaveAsync(new PrimaSettings { Name = "Ada" });
        await host.AddPiece();
        await sync.CopyAsync(ct: Ct);
        // Since then, a problem kept with the key in it: no copy can be taken now.
        await host.Store.SaveAsync(new PrimaSettings { Name = "Someone else" });
        host.Problems.Keep("Error", "Prima.UI.Pages.Settings", "The key test-key was refused");
        var view = host.Context.Render<DataCopies>();
        await view.InvokeAsync(() => view.FindAll("button").Single(b => b.TextContent == "Restore from a backup…").Click());
        await view.InvokeAsync(() => view.WaitForElement(".copy-choice").Click());

        await view.InvokeAsync(() => view.FindAll("button").Single(b => b.TextContent == "Restore this copy").ClickAsync(new MouseEventArgs()));

        view.WaitForAssertion(() => Assert.Equal("What's here couldn't be kept first (errors.log holds a secret, which copies never take), so nothing was restored.", view.Find("[role=status]").TextContent));
        var settings = new PrimaSettings();
        await host.Store.LoadAsync(settings);
        Assert.Equal("Someone else", settings.Name);

        await view.InvokeAsync(() => view.FindAll("button").Single(b => b.TextContent == "Restore without keeping what's here").ClickAsync(new MouseEventArgs()));
        await host.Store.LoadAsync(settings);
        Assert.Equal("Ada", settings.Name);
        Assert.Empty(sync.Status.Holding);
        for (var i = 0; i < 400 && sync.Status.Busy; i++) await Task.Delay(25, Ct);
        Directory.Delete(sync.Options.WorkFolder, recursive: true);
    }

    // A copy is taken after practice (a piece's session finished, a routine's activity done) when the last is a day old, and never
    // while a run is being played.
    [Fact]
    public async Task AfterPractice_ACopyIsTaken_WhenTheLastIsADayOld_NeverWhileARunIsPlayed()
    {
        await using var tablet = new Tablet();
        await tablet.FillAsync();
        var store = new FolderStore(tablet.Copies, "Documents/Prima backups");
        // The last copy was taken two days ago.
        await new Syncer(tablet.Sync.Options, [store], new Ago(TimeSpan.FromDays(2))).CopyAsync(ct: Ct);
        await tablet.Sync.RefreshAsync(Ct);
        var practiced = 0;
        tablet.Store.Practiced += () => practiced++;
        // A drill's item waits on the keys: a run is being played.
        tablet.Runs.DrillAnswer = _ => [];

        var started = DateTimeOffset.Now.AddMinutes(-20);
        var session = await tablet.Store.StartSessionAsync("import-1", "standard", started);
        await tablet.Store.SaveSessionAsync(new PracticeSession(session, "import-1", started, TimeSpan.FromMinutes(15), TimeSpan.FromMinutes(5), false, DateTimeOffset.Now, "standard", DateTimeOffset.Now));

        Assert.Equal(1, practiced);
        await Task.Delay(500, Ct);
        Assert.Single(await tablet.Sync.FindCopiesAsync(store, Ct));
        // Once the run is over, the copy is taken.
        tablet.Runs.DrillAnswer = null;
        for (var i = 0; i < 400 && tablet.Sync.Status.Last?.Manifest.Reason != SyncReason.AfterPractice; i++) await Task.Delay(25, Ct);
        Assert.Equal(SyncReason.AfterPractice, tablet.Sync.Status.Last?.Manifest.Reason);

        // A routine's activity done is practice too (one skipped isn't); with the last copy just taken, no other follows it.
        await tablet.Store.SaveTrackAsync("exercise-routine", new[] { "octave-jump" });
        var job = RoutineStoreTests.ReadyJob();
        await tablet.Store.CreateRoutineJobAsync(job);
        await tablet.Store.AcceptRoutineAsync(job.Id);
        var day = (await tablet.Store.GetOrCreateRoutineDayAsync(DateOnly.FromDateTime(DateTime.Now)))!;
        var execution = await tablet.Store.BeginRoutineExecutionAsync(day.Id, "jump");
        await tablet.Store.CompleteRoutineExecutionAsync(execution.Id, skipped: true);
        Assert.Equal(1, practiced);
        await tablet.Store.CompleteRoutineExecutionAsync(execution.Id);
        Assert.Equal(2, practiced);
        await Task.Delay(500, Ct);
        Assert.Equal(2, (await tablet.Sync.FindCopiesAsync(store, Ct)).Count);
    }

    // Your data says when copies are taken.
    [Fact]
    public async Task YourData_SaysWhenCopiesAreTaken()
    {
        var folder = Path.Combine(Path.GetTempPath(), $"prima-copies-{Guid.NewGuid():N}");
        await using var host = new ComponentHost(copies: new FolderStore(folder, "Documents/Prima backups"));

        var view = host.Context.Render<DataCopies>();

        view.WaitForAssertion(() => Assert.Equal(
            "Prima keeps a copy of everything but Claude's key in Documents/Prima backups on this tablet, where uninstalling Prima doesn't reach it: after practice and as Prima starts once the last is a day old (never while you're playing), and before each update, restore and Start over. Only what changed is written.",
            view.Find("[role=status]").TextContent));
    }

    // How many copies there are and what they take: the music imported is kept once for all of them, so each costs about a data file.
    [Fact]
    public async Task YourData_SaysHowManyCopiesThereAre_AndWhatTheyTake()
    {
        await using var host = new ComponentHost();
        var sync = host.Context.Services.GetRequiredService<Syncer>();
        await host.Store.SaveAsync(new PrimaSettings { Name = "Ada" });
        await sync.CopyAsync(ct: Ct);
        await host.Store.SaveAsync(new PrimaSettings { Name = "Ada Lovelace" });
        await sync.CopyAsync(ct: Ct);

        var view = host.Context.Render<DataCopies>();

        var data = sync.Status.Last!.Manifest.Files.Single(f => f.Path == "prima.db").Size;
        Assert.Equal(2, sync.Status.Copies);
        view.WaitForAssertion(() => Assert.Equal(
            $"2 copies, {SyncText.Size(sync.Status.Stored)} together: the music imported is kept once for all of them, so each copy adds about the size of Prima's data file, {SyncText.Size(data)}.",
            view.FindAll(".control-row").Single(r => r.QuerySelector(".control-label")?.TextContent == "Copies kept").QuerySelector(".control-note")!.TextContent));
        Directory.Delete(sync.Options.WorkFolder, recursive: true);
    }

    [Fact]
    public async Task YourData_ChoosingAFileThatIsntACopy_SaysSo()
    {
        await using var host = new ComponentHost();
        var view = host.Context.Render<DataCopies>();

        view.FindAll("button").Single(b => b.TextContent == "Restore from a backup…").Click();
        view.WaitForAssertion(() => Assert.Contains("This tablet has no copy of its own yet", view.Markup));
        view.FindAll("button").Single(b => b.TextContent == "Choose a file…").Click();

        view.WaitForAssertion(() => Assert.Equal("No file chosen.", view.Find("[role=status]").TextContent));
        Assert.Empty(view.FindAll(".copy-choice"));
    }

    /// <summary>The time some while ago: for a copy taken then.</summary>
    private sealed class Ago(TimeSpan by) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => System.GetUtcNow() - by;
    }

    private static void AssertKeyless(byte[] bytes, string where)
    {
        var text = Encoding.UTF8.GetString(bytes);
        Assert.False(text.Contains(Key, StringComparison.Ordinal) || text.Contains(WaitingKey, StringComparison.Ordinal), $"{where} holds a key");
    }

    /// <summary>
    /// Prima on a tablet, as its host puts it together: the data file in the app's own folder, the key file and problem log in the
    /// folder a PC reaches, and the copies in a folder of their own; each in a folder of the test's.
    /// </summary>
    private sealed class Tablet : IAsyncDisposable
    {
        public static readonly byte[] Rendering = [.. Enumerable.Range(0, 5_000).Select(i => (byte)(i % 199))];

        private readonly string root = Path.Combine(Path.GetTempPath(), $"prima-sync-{Guid.NewGuid():N}");
        private readonly ServiceProvider services;

        public Tablet()
        {
            var data = Directory.CreateDirectory(Path.Combine(root, "files")).FullName;
            var shared = Directory.CreateDirectory(Path.Combine(root, "external")).FullName;
            var collection = new ServiceCollection();
            collection.AddSingleton(new ClaudeKeyFile(Path.Combine(shared, AssistKey.FileName)));
            collection.AddSingleton(new ProblemLog(Path.Combine(shared, ProblemLog.FileName)));
            collection.AddSingleton<ISyncStore>(new FolderStore(Copies, "Documents/Prima backups"));
            collection.AddPrima(Path.Combine(data, "prima.db"));
            services = collection.BuildServiceProvider();
            KeyFile = Path.Combine(shared, AssistKey.FileName);
        }

        public string Copies => Path.Combine(root, "copies");

        public string Exports => Path.Combine(root, "exports");

        public string KeyFile { get; }

        public string? Artifact { get; private set; }

        public PrimaStore Store => services.GetRequiredService<PrimaStore>();

        public Syncer Sync => services.GetRequiredService<Syncer>();

        public IAssistKeyStore Keys => services.GetRequiredService<IAssistKeyStore>();

        public Prima.UI.Practice.SessionRegistry Runs => services.GetRequiredService<Prima.UI.Practice.SessionRegistry>();

        public ProblemLog Problems => services.GetRequiredService<ProblemLog>();

        /// <summary>What a tablet in use has: settings, an imported PDF, what reading it made, and a problem kept.</summary>
        public async Task FillAsync()
        {
            await Store.SaveAsync(new PrimaSettings { Name = "Ada" });
            await Store.SavePieceAsync(new PieceRecord("import-1", "import", "Gymnopédie No. 1", "Satie", null, "pdf", "import-1.pdf", null, 3, 3, null, 39,
                null, null, null, null, null, null, 72, null, null, DateTimeOffset.Now, null), null, []);
            Directory.CreateDirectory(Store.FilesPath);
            File.WriteAllBytes(Path.Combine(Store.FilesPath, "import-1.pdf"), [.. Enumerable.Range(0, 40_000).Select(i => (byte)(i % 251))]);
            Artifact = await Store.SaveConversionArtifactAsync(Rendering);
            Problems.Keep("Warning", "Prima.UI.Pages.Piece", "A page took long to draw");
        }

        public async ValueTask DisposeAsync()
        {
            await Store.FlushAsync();
            await services.DisposeAsync();
            SqliteConnection.ClearAllPools();
            try
            {
                Directory.Delete(root, recursive: true);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
            }
        }
    }
}
