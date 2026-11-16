using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Playground.AI.Assist;
using Playground.Sync;
using Prima.UI.Host;
using Prima.UI.Storage;

namespace Prima.UI.Tests;

/// <summary>
/// From the review of main at a7ab94d: Prima's copies at their edges. Each fails on that code; the place in it is given with each.
/// </summary>
public class PrimaSyncEdgeTests
{
    private const string WaitingKey = "sk-ant-api03-PrimaTestKeyWaitingInItsFile-qrstuvwxyz012345";

    private static CancellationToken Ct => Xunit.TestContext.Current.CancellationToken;

    // Kit/Playground.Sync/Syncer.cs:515-539 (Missing) holds the copy from before an update when ANY table of prima.db has fewer
    // rows than it had, and PrimaSync.cs:49 hands it the whole database. But Prima itself takes out the bundled pieces a new
    // release's bundle no longer has (PrimaStore.Library.cs:166-181, as the Library opens): with the Library opened before the
    // check runs (it waits fifteen seconds), Settings says the update lost data, and offers to restore the copy from before it,
    // which would lose the practice since.
    [Fact]
    public async Task AnUpdateWhoseBundleDropsAPiece_DoesNotHoldTheCopyFromBeforeIt()
    {
        await using var tablet = new Tablet();
        await tablet.Store.SaveAsync(new PrimaSettings { Name = "Ada" });
        await tablet.Store.SavePieceAsync(new PieceRecord("dropped", "bundle", "A piece the next release leaves out", "Anon", null, "pdf", "dropped.pdf", null, 3, 3, null, 12,
            null, null, null, null, null, null, 72, null, null, DateTimeOffset.Now, null), null, []);
        var store = new FolderStore(tablet.Copies, "Documents/Prima backups");
        await new Syncer(tablet.Sync.Options with { Version = "1.0.11" }, [store]).CopyAsync(SyncReason.BeforeUpdate, "Prima 1.0.12", ct: Ct);

        // 1.0.12 starts, and its Library opens before the check: the bundled piece its bundle no longer has is taken out.
        await tablet.Store.RefreshBundleAsync([]);
        var syncer = new Syncer(tablet.Sync.Options with { Version = "1.0.12", StartDelay = TimeSpan.Zero }, [store]);
        await syncer.StartAsync(Ct);

        Assert.True(syncer.Status.Held is null, syncer.Status.Held?.Why);
    }

    // PrimaSync.cs:56 and :77-87 search every file copied for the key waiting in its file, taking the file's whole text as the
    // key. Kit/Playground.AI.Assist/AssistKey.cs:61-66 also takes a file holding {"apiKey":"sk-ant-…"}: then the text searched
    // for is the JSON, and the key itself, anywhere else, goes into the copy. ("never copied, wherever it might turn up")
    [Fact]
    public async Task AKeyWaitingInItsFileAsJson_IsStillNeverCopied()
    {
        await using var tablet = new Tablet();
        await tablet.Store.SaveAsync(new PrimaSettings { Name = "Ada" });
        File.WriteAllText(tablet.KeyFile, $$"""{"apiKey":"{{WaitingKey}}"}""");
        Directory.CreateDirectory(tablet.Store.FilesPath);
        File.WriteAllText(Path.Combine(tablet.Store.FilesPath, "notes.txt"), $"The key is {WaitingKey}");

        await Assert.ThrowsAsync<SyncSecretException>(() => tablet.Sync.CopyAsync(ct: Ct));
    }

    // A tablet's Prima, as PrimaSyncTests sets one up: its data in a folder of its own, its copies in another.
    private sealed class Tablet : IAsyncDisposable
    {
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

        public string KeyFile { get; }

        public PrimaStore Store => services.GetRequiredService<PrimaStore>();

        public Syncer Sync => services.GetRequiredService<Syncer>();

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
