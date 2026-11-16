namespace Playground.Sync;

/// <summary>
/// What an app syncs and how: its <see cref="Items"/>, what never goes in a copy (<see cref="Exclude"/>, <see cref="Secrets"/>),
/// its version (to know the first start after an update), and the app's side of a copy and a restore.
/// </summary>
public sealed record SyncOptions(string App)
{
    /// <summary>What the app keeps, each copied whole: its databases, folders and files, and anything else it writes out.</summary>
    public IReadOnlyList<SyncItem> Items { get; init; } = [];

    /// <summary>File names never copied, wherever they turn up in an item: a key file waiting to be taken in, say.</summary>
    public IReadOnlyList<string> Exclude { get; init; } = [];

    /// <summary>
    /// The secrets to keep out of every copy, such as an API key. Every file read for a copy is searched for each: a copy that
    /// would hold one stops before anything is written, and says which file held it.
    /// </summary>
    public Func<CancellationToken, Task<IReadOnlyCollection<string>>>? Secrets { get; init; }

    /// <summary>The app's version now (1.0.N from a release, 1.0 from the PC): kept with each copy, so the first start after an update is known.</summary>
    public string Version { get; init; } = "";

    /// <summary>Where the work is done: database snapshots, an export before it's handed on. Emptied as it goes.</summary>
    public string WorkFolder { get; init; } = Path.Combine(Path.GetTempPath(), "playground-sync", App);

    /// <summary>What the app does before a copy, such as finishing the writes it has queued, so the copy has them.</summary>
    public Func<Task>? BeforeCopy { get; init; }

    /// <summary>
    /// How the app leaves its data alone while a restore puts a copy's in place: it runs the work it's given once nothing of its own
    /// holds the data open (Prima: on its database's lane, with its connections closed). Without it, the work runs as it is.
    /// </summary>
    public Func<Func<Task>, Task>? WhileRestoring { get; init; }

    /// <summary>A copy older than this is replaced when the app starts: two days.</summary>
    public TimeSpan RefreshAfter { get; init; } = TimeSpan.FromDays(2);

    /// <summary>How long the work on start waits, so it doesn't slow the app's first screen.</summary>
    public TimeSpan StartDelay { get; init; } = TimeSpan.FromSeconds(15);

    /// <summary>A short line on what a copy holds, from its manifest (Prima: its runs, the days practiced and the music imported).</summary>
    public Func<SyncManifest, string>? Describe { get; init; }
}
