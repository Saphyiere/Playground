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
    /// <summary>What the piano plays along In time (Duet): each key, its beat from the run's beat 0, and how many beats.</summary>
    public IReadOnlyList<(int Midi, double Beat, double Beats)>? PianoPart { get; init; }

    /// <summary>Read ahead: how much of the music shows ahead of the cursor, in quarters; the rest is covered In time.</summary>
    public double? ReadAheadQuarters { get; init; }

    public int BeatTicks => Score.Meter.BeatTicks;

    /// <summary>The opening pickup's length: Verovio's times count from the very start, a pickup's notes included.</summary>
    public long PickupTicks => Score.Bars.Count > 0 && Score.Bars[0].Number == 0 ? Score.Bars[0].Length ?? 0 : 0;

    /// <summary>
    /// Notes with a trill, mordent or turn (on the note or its chord), or grace notes before them, whose keys around them aren't
    /// slips: each time they're played. Grace notes aren't scored, so the keys a tone or two from the note they lead into are
    /// taken as theirs.
    /// </summary>
    public IReadOnlySet<string> Ornamented
    {
        get
        {
            var marked = Score.AllMarks.Where(m => m.Kind is MarkKind.Trill or MarkKind.Mordent or MarkKind.Turn).Select(m => m.StartId).ToHashSet();
            foreach (var events in Score.Bars.SelectMany(b => b.Voices).Select(v => v.Events.OfType<Notes>().ToList()))
            {
                for (var i = 1; i < events.Count; i++)
                {
                    if (events[i - 1].Grace && !events[i].Grace) marked.UnionWith(events[i].Tones.Select(t => t.Id));
                }
            }
            return Notes.Where(n => marked.Contains(n.WrittenId) || n.ChordId is { } chord && marked.Contains(SheetId(chord))).Select(n => n.Id).ToHashSet();
        }
    }

    /// <summary>A tick of the run in quarter notes from its start: the cursor's measure, which it's given the run's onsets in.</summary>
    public double QuarterOf(long tick) => (tick + PickupTicks) / (double)Ticks.Quarter;

    /// <summary>The note on the sheet a run's note is: one played again has its pass after the id ("n12~2").</summary>
    public static string SheetId(string id) => id.Split('~')[0];
}

/// <summary>
/// How a run went: In time's passage result (timing, notes a minute, kept going) or Wait for me's (right first time, wrong keys),
/// what was played, whether it was stopped before the end, and the bar it got to.
/// </summary>
public sealed record PlayOutcome(PlayMode Mode, PassageResult? InTime, WaitResult? Wait, IReadOnlyList<PlayedNote> Played, bool Stopped, int Bar)
{
    /// <summary>In time: when a beat of the music sounded on the click's clock (beat 0 is bar 1's downbeat), for keeping a take.</summary>
    public Func<double, long>? TimeAt { get; init; }
}
