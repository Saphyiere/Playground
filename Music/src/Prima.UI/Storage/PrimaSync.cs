using System.Globalization;
using Microsoft.Extensions.DependencyInjection;
using Playground.AI.Assist;
using Playground.Sync;
using Playground.Updates;
using Prima.UI.Host;

namespace Prima.UI.Storage;

/// <summary>
/// What Prima keeps a copy of, through the Kit's sync (Playground.Sync): everything it keeps but Claude's key.
/// <list type="bullet">
/// <item><c>prima.db</c>: settings, levels, notes, every run, the pieces with their notation, readings, routines and Claude's calls.
/// It's restored only into Prima (it has Prima's tables).</item>
/// <item><c>library</c>: the music imported, and a local collection's PDFs and MIDI, beside the data file.</item>
/// <item><c>prima.conversion</c>: what reading a PDF's notes made (the systems' pictures and engravings), beside it too.</item>
/// <item><c>errors.log</c> and <c>errors.old.log</c>: the problems Prima kept.</item>
/// </list>
/// Claude's key lives in the tablet's secure storage, apart from all of these, and <c>claude-key.txt</c> is deleted once taken in;
/// every file a copy reads is searched for the key all the same, and a copy that would hold it stops before writing anything. The
/// look's id in the tablet's preferences is the database's Look setting again, and whether Prima has asked to show notifications
/// is the install's own, so neither is copied.
/// </summary>
public static class PrimaSync
{
    /// <summary>The folder copies go in, in the tablet's Documents: Documents/Prima backups.</summary>
    public const string Folder = "Prima backups";

    /// <summary>Where an export is written before it's handed to the share sheet (the cache, on the tablet).</summary>
    public static string ExportFolder => Path.Combine(Path.GetTempPath(), "prima-export");

    /// <summary>Where a file picked to restore from is copied, to be read.</summary>
    public static string PickedFolder => Path.Combine(Path.GetTempPath(), "prima-picked");

    /// <summary>The tables that tell a database is Prima's, which a restore checks for.</summary>
    public static readonly string[] Tables = ["Setting", "Track", "Attempt", "PracticeDay"];

    /// <summary>
    /// What in the data file is the owner's own, which the first start after an update finds none of with fewer rows than the copy
    /// from before had: the settings, the tracks, every run, the practice days and sessions, the routines, and the pieces imported.
    /// The bundled and collection pieces, and their excerpts, aren't: Prima takes out the bundled pieces a new bundle no longer has.
    /// </summary>
    public static readonly SyncCount[] Counted =
    [
        SyncCount.Rows("Setting"), SyncCount.Rows("Track"), SyncCount.Rows("Attempt"), SyncCount.Rows("PracticeDay"), SyncCount.Rows("PracticeSession"),
        SyncCount.Rows("RoutineGeneration"), SyncCount.Rows("RoutineDefinition"), SyncCount.Rows("RoutineDay"), SyncCount.Rows("RoutineExecution"), SyncCount.Rows("RoutineAttempt"),
        new("Piece (imported)", "Piece", "Origin = 'import'"),
    ];

    /// <summary>What Prima syncs, from its services (the store, the problem log, the key and its file, the version installed).</summary>
    public static SyncOptions Options(IServiceProvider services, string databasePath)
    {
        var store = services.GetRequiredService<PrimaStore>();
        var problems = services.GetRequiredService<ProblemLog>();
        var keyFile = services.GetRequiredService<ClaudeKeyFile>();
        var keys = services.GetRequiredService<IAssistKeyStore>();
        return new SyncOptions("Prima")
        {
            Items =
            [
                SyncItem.Database("prima.db", databasePath) with { RequiredTables = Tables, Counted = Counted },
                SyncItem.Folder("library", store.FilesPath),
                SyncItem.Folder("prima.conversion", Path.ChangeExtension(databasePath, ".conversion")),
                SyncItem.File(ProblemLog.FileName, problems.Path),
                SyncItem.File(Path.GetFileName(problems.OldPath), problems.OldPath),
            ],
            Exclude = [AssistKey.FileName],
            Secrets = async _ => [.. new[] { await keys.GetAsync(), await WaitingKeyAsync(keyFile.Path) }.OfType<string>()],
            Version = services.GetService<IInstaller>()?.InstalledVersion ?? "",
            WorkFolder = Path.Combine(Path.GetTempPath(), "prima-sync", Path.GetFileNameWithoutExtension(databasePath)),
            BeforeCopy = store.FlushAsync,
            WhileRestoring = store.WhileClosedAsync,
            Describe = Describe,
        };
    }

    /// <summary>What a copy holds, in a line: "1,204 runs over 41 days · 12 imported files".</summary>
    public static string Describe(SyncManifest copy)
    {
        var runs = copy.Rows("prima.db", "Attempt") ?? 0;
        var days = copy.Rows("prima.db", "PracticeDay") ?? 0;
        var files = copy.FilesOf("library").Count();
        var practiced = runs == 0 ? "Nothing played yet"
            : $"{runs.ToString("N0", CultureInfo.InvariantCulture)} {(runs == 1 ? "run" : "runs")} over {days.ToString("N0", CultureInfo.InvariantCulture)} {(days == 1 ? "day" : "days")}";
        return files == 0 ? practiced : $"{practiced} · {files} imported {(files == 1 ? "file" : "files")}";
    }

    // A key copied over USB and not taken in yet: never copied, wherever it might turn up.
    private static async Task<string?> WaitingKeyAsync(string path)
    {
        try
        {
            return File.Exists(path) ? (await File.ReadAllTextAsync(path)).Trim() : null;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }
}
