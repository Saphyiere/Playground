using Prima.Core.Music;

namespace Prima.Core.Scoring;

/// <summary>How a Wait for me run went: the notes reached, how many were right first time, the wrong keys, and the pace.</summary>
public sealed record WaitResult(int Notes, int Total, int RightFirstTime, int WrongKeys, double Seconds)
{
    /// <summary>Whether every note was reached.</summary>
    public bool Finished => Notes == Total;

    /// <summary>Notes played a minute, from the first key to the last.</summary>
    public double NotesPerMinute => Seconds <= 0 ? 0 : Notes / Seconds * 60;
}

/// <summary>
/// Wait for me: the music as steps, each the notes that start together (a chord, or both hands at once), waiting at each until
/// all its keys have been played, in any order. A right key marks its note right. Another key marks wrong the step's note not yet
/// played that's nearest to it, with the key, and the step waits on; that note stays marked wrong once it's played, so the slip
/// shows where it was. A key of the step played again is nothing. Keys within a tone of an ornamented note (a trill's or a
/// mordent's), in its step or just after it, aren't slips. No time is judged: the pace is your own.
/// </summary>
public sealed class WaitJudge
{
    private readonly List<IReadOnlyList<ExpectedNote>> steps;
    private readonly HashSet<string> ornamented;
    private readonly HashSet<string> played = [];
    private readonly HashSet<string> slipped = [];
    private int wrongKeys;
    private long? firstNs;
    private long lastNs;

    public WaitJudge(IEnumerable<ExpectedNote> notes, IReadOnlySet<string>? ornamented = null)
    {
        steps = [.. notes.GroupBy(n => n.Tick).OrderBy(g => g.Key).Select(g => (IReadOnlyList<ExpectedNote>)[.. g.OrderBy(n => n.Midi)])];
        this.ornamented = ornamented is null ? [] : [.. ornamented];
    }

    /// <summary>The step waited for, from 0.</summary>
    public int Step { get; private set; }

    public bool Done => Step >= steps.Count;

    /// <summary>The notes waited for: the cursor's place.</summary>
    public IReadOnlyList<ExpectedNote> Current => Done ? [] : steps[Step];

    /// <summary>A key going down: the marks it makes (none for a key that's nothing), and whether it moved the cursor on.</summary>
    public (IReadOnlyList<LiveMark> Marks, bool Moved) Play(int midi, long timeNs)
    {
        if (Done) return ([], false);

        firstNs ??= timeNs;
        lastNs = timeNs;
        var step = steps[Step];
        var note = step.FirstOrDefault(n => n.Midi == midi && !played.Contains(n.Id));
        if (note is not null)
        {
            played.Add(note.Id);
            var moved = step.All(n => played.Contains(n.Id));
            if (moved) Step++;
            // A note that had a slip keeps its wrong mark.
            return (slipped.Contains(note.Id) ? [] : [new LiveMark(note.Id, LiveKind.Right, midi, 0)], moved);
        }

        var ornament = step.Concat(Step > 0 && !step.Any(n => played.Contains(n.Id)) ? steps[Step - 1] : []).Any(n => ornamented.Contains(n.Id) && Math.Abs(n.Midi - midi) <= 2);
        if (step.Any(n => n.Midi == midi) || ornament) return ([], false);

        wrongKeys++;
        var meant = step.Where(n => !played.Contains(n.Id)).OrderBy(n => Math.Abs(n.Midi - midi)).First();
        slipped.Add(meant.Id);
        return ([new LiveMark(meant.Id, LiveKind.Wrong, midi, 0)], false);
    }

    /// <summary>The notes reached: those played, and one with a slip that wasn't put right before the run stopped.</summary>
    public WaitResult Result() => new(played.Union(slipped).Count(), steps.Sum(s => s.Count), played.Count(id => !slipped.Contains(id)), wrongKeys, firstNs is { } first ? (lastNs - first) / 1e9 : 0);
}
