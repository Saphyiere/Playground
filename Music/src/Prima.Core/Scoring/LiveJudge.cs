using Prima.Core.Music;

namespace Prima.Core.Scoring;

/// <summary>What a note turned out to be while it was being played.</summary>
public enum LiveKind
{
    /// <summary>The right key within 10% of a beat of its time.</summary>
    Right,
    /// <summary>The right key, more than 10% of a beat early.</summary>
    Early,
    /// <summary>The right key, more than 10% of a beat late.</summary>
    Late,
    /// <summary>Another key at its time.</summary>
    Wrong,
    /// <summary>Its time passed with nothing played for it.</summary>
    Missed,
    /// <summary>The right key of a chord whose keys were played one after another, not together (Wait for me).</summary>
    Apart,
}

/// <summary>A note judged during a run: its id on the sheet, what it was, the key played (if any) and when, in % of a beat from its time.</summary>
public sealed record LiveMark(string Id, LiveKind Kind, int? Played, double ErrorPercent);

/// <summary>
/// Judges each note as it's played, for the marks that appear on the sheet during a run. It's greedy where PassageScorer's
/// summary afterwards lines up the whole take, so the two can differ here and there; the summary's marks replace these.
/// A key goes to the nearest note of that key within the window (the scorer's: 40% of a beat, 0.12 to 0.30 s). With none of that
/// key there, the nearest note not yet played is marked wrong, and stays wrong: the right key a moment later is taken as putting
/// it right, so it isn't held against the next note, but the slip counts. A key around an ornament (a trill's, a mordent's, grace
/// notes before a note) is part of it, and a key of a chord's note struck again is only extra. A note whose window passes with
/// nothing played is missed. Times are the click's, asked for as each key comes (they settle once its audio is running), with the
/// timing calibration's offset taken off what's played. As the summary does, a right note's timing is judged with the run's lean
/// taken off: its average so far, over the right notes before it, so a steady habit of playing a little early isn't marked
/// early as you play and then in time after. A chord marked to be rolled is timed by its first key: the others follow it, each
/// no more than a roll's gap (<see cref="WaitJudge.RollGapNs"/>) after the one before.
/// </summary>
public sealed class LiveJudge
{
    private readonly List<Slot> open;
    private readonly List<ExpectedNote> ornaments;
    private readonly HashSet<(long Tick, int Midi)> together;
    private readonly Func<double, long> timeAt;
    private readonly double beatTicks;
    private readonly double windowNs;
    private readonly double beatNs;
    private readonly long offsetNs;
    private readonly HashSet<string> rolled;
    // Each rolled chord under way, by its tick: when its last key went down, and the error of its first.
    private readonly Dictionary<long, (double At, double Error)> rolls = [];
    // The right notes' timing so far, for the lean.
    private double errorSum;
    private int errorCount;

    private sealed class Slot(ExpectedNote note)
    {
        public ExpectedNote Note { get; } = note;
        public int? WrongKey { get; set; }
    }

    /// <param name="expected">The notes to be played.</param>
    /// <param name="timeAt">When a beat of the music sounds (beat 0 is bar 1's downbeat), on the system clock: the click's.</param>
    /// <param name="ornamented">Ids of notes with a trill or mordent, or grace notes before them.</param>
    /// <param name="rolled">Ids of the notes of chords marked to be rolled.</param>
    public LiveJudge(IEnumerable<ExpectedNote> expected, Func<double, long> timeAt, int beatTicks, double beatNs, double offsetMs, IReadOnlySet<string>? ornamented = null,
        IReadOnlySet<string>? rolled = null)
    {
        this.rolled = rolled is null ? [] : [.. rolled];
        open = [.. expected.OrderBy(n => n.Tick).Select(n => new Slot(n))];
        ornaments = [.. open.Select(s => s.Note).Where(n => ornamented?.Contains(n.Id) == true)];
        together = [.. open.Select(s => (s.Note.Tick, s.Note.Midi))];
        (this.timeAt, this.beatTicks, this.beatNs) = (timeAt, beatTicks, beatNs);
        windowNs = Grading.WindowNs(beatNs);
        offsetNs = (long)(offsetMs * 1e6);
    }

    private long TimeOf(Slot slot) => timeAt(slot.Note.Tick / beatTicks);

    // A rolled chord whose roll is under way at a time: a key in it may come later than its window.
    private bool Rolling(Slot slot, double t) => rolled.Contains(slot.Note.Id) && rolls.TryGetValue(slot.Note.Tick, out var roll) && t - roll.At <= WaitJudge.RollGapNs;

    /// <summary>A key going down: the mark it makes, or null when it's nowhere near a note (an extra one).</summary>
    public LiveMark? Play(int midi, long timeNs)
    {
        var t = timeNs - offsetNs;
        var near = open.Select(s => (Slot: s, Error: t - TimeOf(s))).Where(n => Math.Abs(n.Error) <= windowNs || Rolling(n.Slot, t)).ToList();
        if (near.Count == 0) return null;

        var right = near.Where(n => n.Slot.Note.Midi == midi).OrderBy(n => Math.Abs(n.Error)).FirstOrDefault();
        if (right.Slot is not null)
        {
            open.Remove(right.Slot);
            // A rolled chord's keys after its first take that one's timing.
            double raw = right.Error;
            if (rolled.Contains(right.Slot.Note.Id))
            {
                if (Rolling(right.Slot, t)) raw = rolls[right.Slot.Note.Tick].Error;
                rolls[right.Slot.Note.Tick] = (t, raw);
            }
            // A note with a slip stays wrong: this is the key that put it right.
            if (right.Slot.WrongKey is not null) return null;

            var lean = errorCount == 0 ? 0 : errorSum / errorCount;
            (errorSum, errorCount) = (errorSum + raw, errorCount + 1);
            var error = (raw - lean) / beatNs * 100;
            var kind = Math.Abs(error) <= Grading.AudiblePercent ? LiveKind.Right : error < 0 ? LiveKind.Early : LiveKind.Late;
            return new LiveMark(right.Slot.Note.Id, kind, midi, error);
        }

        // A key within a tone of an ornamented note, from just before it to its end, is the ornament's.
        if (ornaments.Any(o => Math.Abs(o.Midi - midi) <= 2 && t >= timeAt(o.Tick / beatTicks) - windowNs && t <= timeAt((o.Tick + o.Length) / beatTicks))) return null;

        // Another key: of the notes not yet played, the nearest in time (a chord's notes count as one time), then in pitch. One of
        // the chord's own keys, struck again, isn't a slip.
        var meant = near.Where(n => n.Slot.WrongKey is null && !together.Contains((n.Slot.Note.Tick, midi)))
            .OrderBy(n => Math.Round(Math.Abs(n.Error) / (beatNs / 8))).ThenBy(n => Math.Abs(n.Slot.Note.Midi - midi)).FirstOrDefault();
        if (meant.Slot is null) return null;

        meant.Slot.WrongKey = midi;
        return new LiveMark(meant.Slot.Note.Id, LiveKind.Wrong, midi, meant.Error / beatNs * 100);
    }

    /// <summary>The notes whose window has closed by <paramref name="nowNs"/> with nothing played for them (wrong ones are already marked).</summary>
    public IReadOnlyList<LiveMark> Due(long nowNs)
    {
        var closed = open.Where(s => nowNs - offsetNs > TimeOf(s) + windowNs && !Rolling(s, nowNs - offsetNs)).ToList();
        foreach (var slot in closed) open.Remove(slot);
        return [.. closed.Where(s => s.WrongKey is null).Select(s => new LiveMark(s.Note.Id, LiveKind.Missed, null, 0))];
    }
}
