using Playground.AI.Assist;
using Prima.Core.Routines;

namespace Prima.UI.Routines;

public enum RoutineGenerationState { Queued, Running, Ready, Failed, Interrupted, Cancelled, Accepted }
public enum RoutineItemState { NotStarted, InProgress, Paused, Done, Skipped }

/// <summary>A saved request and its response. Dispatch is recorded before calling the selected provider.</summary>
public sealed record RoutineGenerationJob(string Id, int Revision, RoutineGenerationState State, DateTimeOffset Updated,
    RoutineRequest Request, PracticeEvidenceSnapshot Evidence, RoutineSource? Source, string Provider, string Model,
    RoutineDraft? Draft = null, IReadOnlyList<PreparedRoutineItem>? Prepared = null, string? Problem = null,
    bool Dispatched = false, Usage? Usage = null, InferenceResponse? Response = null, string? AcceptedRevision = null);

/// <summary>An accepted immutable routine, including the exact generated music and source used to make it.</summary>
public sealed record RoutineDefinition(string Id, string Revision, DateTimeOffset Accepted, string Title, string Summary,
    RoutineRequest Request, PracticeEvidenceSnapshot Evidence, RoutineSource? Source, string Provider, string Model,
    IReadOnlyList<PreparedRoutineItem> Items);

public sealed record RoutineItemProgress(string ItemId, RoutineItemState State = RoutineItemState.NotStarted,
    double ActiveSeconds = 0, int CompletedPasses = 0, string? LastExecutionId = null);

/// <summary>A day's progress belongs to one immutable revision; tomorrow starts fresh using the same definition.</summary>
public sealed record RoutineDay(string Id, string Revision, DateOnly Day, IReadOnlyList<RoutineItemProgress> Items);

/// <summary>A run's identity is fixed before playing; its recipe is a snapshot, not the player's last-used settings.</summary>
public sealed record RoutineExecution(string Id, string DayId, string Revision, string ItemId, DateOnly Day,
    PreparedRoutineItem Item, RoutineItemState State, double ActiveSeconds = 0, int CompletedPasses = 0, string? Feedback = null);

public sealed record RoutineAdjustment(string ItemId, double CurrentTempo, double SuggestedTempo, int ComparableRuns, string Reason,
    int? SuggestedPreparationBeats = null, int? SuggestedStep = null, string? SuggestedMode = null);
