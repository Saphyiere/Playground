using System.Text.RegularExpressions;
using AdaptiveWorkout.Core;
using AdaptiveWorkout.UI.Assist;
using Microsoft.Extensions.DependencyInjection;
using Playground.AI.Assist;
using Playground.Sync;
using Playground.Updates;

namespace AdaptiveWorkout.UI.Storage;

/// <summary>
/// What Workout keeps a copy of, through the Kit's sync (Playground.Sync): everything it keeps but Claude's key, which is all in its
/// data file, <c>adaptiveworkout.db</c>: every workout with its sets, plan and heart rate, the routines, sessions and exercises, food,
/// weigh-ins and what was said about a slip, the settings and the look, and Claude's look backs, answers and calls. It's restored only
/// into Workout (it has Workout's tables), and a data file on its own restores like a whole copy: one the updater kept in
/// Download/Workout backups before copies were kept whole, say.
/// <para>
/// Claude's key lives in the phone's secure storage, apart from the data file, and <c>claude-key.txt</c> is deleted once taken in;
/// every file a copy reads is searched for the key all the same, and a copy that would hold it stops before writing anything. The
/// look's id in the phone's preferences is the data file's Theme setting again, so it isn't copied.
/// </para>
/// </summary>
public static partial class WorkoutSync
{
    /// <summary>The folder copies go in, in the phone's Documents: Documents/Workout backups.</summary>
    public const string Folder = "Workout backups";

    /// <summary>The data file's name in a copy, the same on every device.</summary>
    public const string DataFile = "adaptiveworkout.db";

    /// <summary>Why a restore waits while a workout is open, as part of a sentence.</summary>
    public const string WorkoutOpen = "a workout is open, and a restore waits until it's finished";

    /// <summary>Where an export is written before it's handed to the share sheet (the cache, on the phone).</summary>
    public static string ExportFolder => Path.Combine(Path.GetTempPath(), "workout-export");

    /// <summary>Where a file picked to restore from is copied, to be read.</summary>
    public static string PickedFolder => Path.Combine(Path.GetTempPath(), "workout-picked");

    /// <summary>
    /// The tables that tell a data file is Workout's, which a restore checks for: those every one has had, from the first version on,
    /// so a data file kept long ago restores too (the app brings it up to date as it opens it).
    /// </summary>
    public static readonly string[] Tables = ["Exercise", "WorkoutSession", "WorkoutSet", "FoodEntry", "BodyWeightEntry"];

    /// <summary>
    /// What in the data file is the owner's own, which the first start after an update finds none of with fewer rows than the copy
    /// from before had: the workouts with their sets, plans and heart rate, food, weigh-ins and what was said about a slip, the
    /// routines, sessions and exercises, Claude's look backs at workouts and weeks and the questions asked, and the settings. The
    /// muscles Claude tagged exercises with aren't (it tags them again), nor the calls to Claude, a log of what each cost. A restore
    /// keeps what's here first unless none of it but the settings has a row (a fresh install).
    /// </summary>
    public static readonly SyncCount[] Counted =
    [
        SyncCount.Rows("Setting") with { Settings = true },
        SyncCount.Rows("WorkoutSession"), SyncCount.Rows("WorkoutSet"), SyncCount.Rows("SessionExercise"), SyncCount.Rows("HeartRateSample"),
        SyncCount.Rows("FoodEntry"), SyncCount.Rows("BodyWeightEntry"), SyncCount.Rows("DataCheck"),
        SyncCount.Rows("Routine"), SyncCount.Rows("RoutineDay"), SyncCount.Rows("SessionTemplate"), SyncCount.Rows("TemplateExercise"), SyncCount.Rows("Exercise"),
        SyncCount.Rows("LookBack"), SyncCount.Rows("WeekLook"), SyncCount.Rows("Question"),
    ];

    // How often a copy the app takes on its own looks again whether the workout it's waiting for is over.
    private static readonly TimeSpan QuietCheck = TimeSpan.FromSeconds(30);

    /// <summary>
    /// What Workout syncs, from its services (the store, the key and its file, the version installed). Copies the app takes on its
    /// own wait while a workout is open, and one is taken after Finish workout when the last is a day old. A restore waits while a
    /// workout is open too, and leaves the data file alone while it puts the copy in its place: everything read from it before is
    /// read again after.
    /// </summary>
    public static SyncOptions Options(IServiceProvider services, string databasePath)
    {
        var store = services.GetRequiredService<WorkoutStore>();
        var keyFile = services.GetRequiredService<ClaudeKeyFile>();
        var keys = services.GetRequiredService<IAssistKeyStore>();
        store.WorkoutFinished += () => _ = services.GetRequiredService<Syncer>().CopyIfDueAsync(SyncReason.AfterPractice);
        return new SyncOptions("Workout")
        {
            Items = [SyncItem.Database(DataFile, databasePath) with { RequiredTables = Tables, Counted = Counted }],
            Exclude = [AssistKey.FileName],
            Secrets = async _ => [.. new[] { await keys.GetAsync(), await WaitingKeyAsync(keyFile.Path) }.OfType<string>()],
            // Beside the data file and in no item: a new key has the data file read again, and the mark gives nothing of it back.
            SecretsMark = Path.ChangeExtension(databasePath, ".searched"),
            Version = services.GetService<IInstaller>()?.InstalledVersion ?? "",
            WorkFolder = Path.Combine(Path.GetTempPath(), "workout-sync", Path.GetFileNameWithoutExtension(databasePath)),
            BeforeCopy = store.FlushAsync,
            Quiet = ct => QuietAsync(store, ct),
            WhileRestoring = work => store.WhileClosedAsync(async () =>
            {
                // Asked again with the file held still: a workout's sets would otherwise be saved over the copy put in its place.
                if (WorkoutStore.HasOpenWorkout(databasePath)) throw new SyncException(WorkoutOpen);
                await work();
            }),
            Describe = Describe,
        };
    }

    /// <summary>
    /// What a copy holds, in a line: "212 workouts · 3,104 sets · 1,904 food entries · 480 weigh-ins". At the end of a line, a count
    /// stays with what it counts and a dot with what's before it (no-break spaces), and a weigh-in isn't broken at its hyphen (a word
    /// joiner, which shows as nothing).
    /// </summary>
    public static string Describe(SyncManifest copy)
    {
        var said = DataCopies.Describe(Rows("WorkoutSession"), Rows("WorkoutSet"), Rows("FoodEntry"), Rows("BodyWeightEntry"));
        return CountedWords().Replace(said, "$1\u00a0").Replace(" · ", "\u00a0· ", StringComparison.Ordinal).Replace("weigh-in", "weigh-\u2060in", StringComparison.Ordinal);

        long Rows(string table) => copy.Rows(DataFile, table) ?? 0;
    }

    [GeneratedRegex(@"(\d) ")]
    private static partial Regex CountedWords();

    // A copy the app takes on its own (as it starts, after a workout) waits while a workout is open: a set, a rest, the warm-up or the
    // cool-down being timed.
    private static async Task QuietAsync(WorkoutStore store, CancellationToken ct)
    {
        while (await store.HasOpenWorkoutAsync()) await Task.Delay(QuietCheck, ct);
    }

    // A key copied over USB and not taken in yet: never copied, wherever it might turn up. It's read as the app takes it in (the key
    // alone, or JSON with an apiKey), and a file that holds no key is searched for as it is.
    private static async Task<string?> WaitingKeyAsync(string path)
    {
        try
        {
            if (!File.Exists(path)) return null;
            var text = (await File.ReadAllTextAsync(path)).Trim();
            return AssistKey.Read(text) is { Length: > 0 } key ? key : text;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }
}
