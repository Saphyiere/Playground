namespace Playground.Sync;

public enum SyncStep
{
    Idle,
    /// <summary>Taking a copy: looking at what changed, then writing it.</summary>
    Copying,
    /// <summary>The first start after an update: checking the app holds all the copy from before it did.</summary>
    Checking,
    Restoring,
    Exporting,
}

/// <summary>
/// Where the <see cref="Syncer"/> has got to, for the screen: what it's doing and how far along, the newest copy in the app's own
/// store, the copies held there (<see cref="Held"/> is the newest one's hold), and a sentence on how it went.
/// </summary>
public sealed record SyncStatus(SyncStep Step, bool Known, SyncCopy? Last, SyncHeld? Held, SyncProgress? Progress, string Message, bool Failed)
{
    /// <summary>Doing something: a copy, a check, a restore or an export.</summary>
    public bool Busy => Step != SyncStep.Idle;

    /// <summary>The copies held in the app's own store, newest first: each kept, whatever copies come after, until it's restored or let go.</summary>
    public IReadOnlyList<SyncCopy> Holding { get; init; } = [];
}

/// <summary>How far a piece of work is, for a bar: a sentence on what it's doing, and how much of that is done (0 to 1).</summary>
public sealed record SyncProgress(string Doing, double Fraction);
