using System.IO.Compression;
using System.Text;
using Bunit;
using Microsoft.AspNetCore.Components;
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
    public async Task ARestore_OnAFreshInstall_BringsBackEverything()
    {
        await using var before = new Tablet();
        await before.FillAsync();
        await before.Sync.CopyAsync(ct: Ct);

        // Prima installed afresh: nothing of its own yet, and the copy before found with the tablet's folder picker.
        await using var fresh = new Tablet();
        await fresh.Store.GetSettingsAsync();
        var copy = Assert.Single(await fresh.Sync.FindCopiesAsync(new FolderStore(before.Copies, "Documents/Prima backups/Installed 2026-10-01 090000"), Ct));
        Assert.Equal("Nothing played yet · 1 imported file", copy.Summary);
        await fresh.Sync.RestoreAsync(copy, Ct);

        var settings = new PrimaSettings();
        await fresh.Store.LoadAsync(settings);
        Assert.Equal("Ada", settings.Name);
        Assert.Equal("Gymnopédie No. 1", (await fresh.Store.GetPieceAsync("import-1"))?.Title);
        Assert.Equal(File.ReadAllBytes(Path.Combine(before.Store.FilesPath, "import-1.pdf")), File.ReadAllBytes(Path.Combine(fresh.Store.FilesPath, "import-1.pdf")));
        Assert.Equal(Tablet.Rendering, await fresh.Store.GetConversionArtifactAsync(before.Artifact!, Ct));
        Assert.Contains(fresh.Problems.Recent(), p => p.Message == "A page took long to draw");
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

        var navigation = host.Context.Services.GetRequiredService<NavigationManager>();
        view.WaitForAssertion(() => Assert.EndsWith("/settings", navigation.Uri, StringComparison.Ordinal), TimeSpan.FromSeconds(10));
        var settings = new PrimaSettings();
        await host.Store.LoadAsync(settings);
        Assert.Equal("Ada", settings.Name);
        Assert.Equal("Ada", host.Context.Services.GetRequiredService<PrimaSettings>().Name);

        // The copy of the data as restored, taken in the background, finishes before the test's data goes.
        for (var i = 0; i < 400 && (sync.Status.Busy || sync.Status.Last?.Manifest.Reason != SyncReason.AfterRestore); i++) await Task.Delay(25, Ct);
        Assert.Equal(SyncReason.AfterRestore, sync.Status.Last?.Manifest.Reason);
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
