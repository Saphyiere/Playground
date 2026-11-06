using Prima.Core.Music;
using Prima.Core.Scoring;

namespace Prima.UI.Practice;

/// <summary>How a run follows you: in time with the click, or waiting at each note until its keys are played.</summary>
public enum PlayMode
{
    InTime,
    WaitForMe,
}

/// <summary>
/// What a run plays: its score (the metre, the key and the ornaments come from it), the notes to play (all of them, or some: one
/// hand's, or from a bar on), and the tick the run counts from, which is its beat 0 (bar 1's downbeat, or the bar it starts at).
/// </summary>
public sealed record PlayMusic(Score Score, IReadOnlyList<ExpectedNote> Notes, long FromTick = 0)
{
    public int BeatTicks => Score.Meter.BeatTicks;

    /// <summary>The opening pickup's length: Verovio's times count from the very start, a pickup's notes included.</summary>
    public long PickupTicks => Score.Bars.Count > 0 && Score.Bars[0].Number == 0 ? Score.Bars[0].Length ?? 0 : 0;

    /// <summary>Notes with a trill or mordent, whose extra notes aren't slips.</summary>
    public IReadOnlySet<string> Ornamented => Score.Marks.Where(m => m.Kind is MarkKind.Trill or MarkKind.Mordent).Select(m => m.StartId).ToHashSet();

    /// <summary>Where a tick is on the sheet, in quarter notes from the start: the cursor's measure.</summary>
    public double QuarterOf(long tick) => (tick + PickupTicks) / (double)Ticks.Quarter;
}

/// <summary>
/// How a run went: In time's passage result (timing, notes a minute, kept going) or Wait for me's (right first time, wrong keys),
/// what was played, whether it was stopped before the end, and the bar it got to.
/// </summary>
public sealed record PlayOutcome(PlayMode Mode, PassageResult? InTime, WaitResult? Wait, IReadOnlyList<PlayedNote> Played, bool Stopped, int Bar);
