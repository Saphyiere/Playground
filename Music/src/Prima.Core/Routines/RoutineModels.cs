using System.ComponentModel;
using Prima.Core.Generation;
using Prima.Core.Music;

namespace Prima.Core.Routines;

public sealed record RoutineRequest(int Minutes, string Goal, string? PieceId = null, int? FirstBar = null, int? LastBar = null);

public sealed record SkillEvidence(string Skill, int Attempts, int Days, DateTimeOffset? Last, double? Accuracy, double? Tempo, string Detail)
{
    public string? Hands { get; init; }
    public string? PieceId { get; init; }
    public IReadOnlyList<int> TroubleBars { get; init; } = [];
}

public sealed record PracticeEvidenceSnapshot(DateTimeOffset At, IReadOnlyList<SkillEvidence> Skills);

/// <summary>The retained, exact edition used to construct exercises. Revision is the hash of Mei.</summary>
public sealed record RoutineSource(string PieceId, string Title, string Revision, string Mei, bool HandsConfirmed, bool HasUnresolvedErrors = false);

/// <summary>A simultaneous group in one hand, including voices that start together. IDs belong to this source revision.</summary>
public sealed record RoutineSourceEvent(string Id, int Bar, int Staff, int Start, int Length, IReadOnlyList<int> Pitches,
    IReadOnlyList<string> NoteIds, int Span, int MoveFromPrevious, string? PreviousId);

public sealed record RoutineCatalogEntry(string Kind, string Id, string Name, string Purpose, IReadOnlyList<HandUse> Hands,
    IReadOnlyList<int> Fifths, bool AllowsMinor, int MaxStep)
{
    public IReadOnlyList<string> Modes { get; init; } = ["wait", "time"];
}

public sealed record RoutinePlanningInput(RoutineRequest Request, PracticeEvidenceSnapshot Evidence, IReadOnlyList<RoutineCatalogEntry> Catalog,
    string? SourceTitle, string? SourceRevision, bool HandsConfirmed, bool HasUnresolvedErrors, IReadOnlyList<RoutineSourceEvent> SourceEvents)
{
    /// <summary>The key and metre in force at the start of the source, or the requested passage.</summary>
    public Key? SourceKey { get; init; }
    public Meter? SourceMeter { get; init; }
    public int TicksPerQuarter { get; init; } = Ticks.Quarter;
}

/// <summary>A bounded recipe. Notes are always constructed locally from catalogue material or the retained source.</summary>
public sealed record RoutineItem
{
    public string Id { get; init; } = "";
    [Description("exercise, drill, generated, or excerpt")]
    public string Kind { get; init; } = "exercise";
    [Description("A supplied catalogue ID, or jump/chord-shapes/chord-changes/accompaniment/rhythm/coordination; source for an excerpt")]
    public string TargetId { get; init; } = "";
    public string Title { get; init; } = "";
    public string Purpose { get; init; } = "";
    [Description("Whole minutes, 1 to 30, with all items together at most the requested time")]
    public int Minutes { get; init; } = 2;
    public HandUse Hands { get; init; } = HandUse.Right;
    [Description("wait or time; rhythm uses time")]
    public string Mode { get; init; } = "wait";
    [Description("Beats per minute, 20 to 160")]
    public double Tempo { get; init; } = 60;
    public int Fifths { get; init; }
    public bool Minor { get; init; }
    public int Step { get; init; } = 1;
    [Description("IDs from sourceEvents, in musical order. Two adjacent groups for jump/chord-changes; one chord for chord-shapes. Empty for catalogue material.")]
    public IReadOnlyList<string> SourceEventIds { get; init; } = [];
    [Description("Inclusive source bar range for excerpt/accompaniment/rhythm/coordination; otherwise null")]
    public int? FirstBar { get; init; }
    public int? LastBar { get; init; }
    [Description("Inserted quarter-note preparation rests for jumps/chord changes, 0 to 4")]
    public int PreparationBeats { get; init; } = 2;
    [Description("Repeats of the small exercise, 1 to 8")]
    public int Repetitions { get; init; } = 3;
}

public sealed record RoutineDraft(string Title, string Summary, IReadOnlyList<RoutineItem> Items);

/// <summary>The accepted recipe and validated notation. Catalogue/drill items dispatch to existing players and have empty Mei.</summary>
public sealed record PreparedRoutineItem(RoutineItem Recipe, string Mei, string? SourceRevision);
