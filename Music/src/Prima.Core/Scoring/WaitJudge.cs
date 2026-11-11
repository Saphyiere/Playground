using Prima.Core.Music;

namespace Prima.Core.Scoring;

/// <summary>How a Wait for me run went: the notes reached, how many were right first time, the wrong keys, and the pace.</summary>
public sealed record WaitResult(int Notes, int Total, int RightFirstTime, int WrongKeys, double Seconds)
{
    /// <summary>The chords (keys that go down together, in one hand or both) played a key at a time before they were played together.</summary>
    public int Apart { get; init; }

    /// <summary>Whether every note was reached.</summary>
    public bool Finished => Notes == Total;

    /// <summary>Notes played a minute, from the first key to the last.</summary>
    public double NotesPerMinute => Seconds <= 0 ? 0 : Notes / Seconds * 60;
}

/// <summary>
/// Wait for me: the music as steps, each the notes that start together (a chord, or both hands at once), waiting at each until
/// its keys go down together: in any order, the first to the last within <see cref="TogetherNs"/>. A right key marks its note
/// right. Once a chord's keys have all been played but one after another, which is how it's found and not how it's played, its
/// notes are marked apart and the step waits for them together. Another key marks wrong the step's note not yet played that's
/// nearest to it, with the key, and the step waits on; that note stays marked wrong once it's played, so the slip shows where it
/// was. Keys within a tone of an ornamented note (a trill's or a mordent's), in its step or just after it, aren't slips. No
/// other time is judged: the pace is your own.
/// </summary>
public sealed class WaitJudge
{
    /// <summary>
    /// The longest from a chord's first key to its last for them to be together: 0.15 s. Hands that mean to land together are
    /// well inside it, and keys played one after another well outside.
    /// </summary>
    public const long TogetherNs = 150_000_000;

    private readonly List<IReadOnlyList<ExpectedNote>> steps;
    private readonly HashSet<string> ornamented;
    private readonly HashSet<string> played = [];
    private readonly HashSet<string> slipped = [];
    // The step's keys that are down together so far, each with when it went down, and the steps played a key at a time.
    private readonly Dictionary<int, long> struck = [];
    private readonly HashSet<int> apart = [];
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

    /// <summary>Whether the chord waited for has had its keys played, but one after another: it waits for them together.</summary>
    public bool Apart => apart.Contains(Step);

    /// <summary>A key going down: the marks it makes (none for a key that's nothing), and whether it moved the cursor on.</summary>
    public (IReadOnlyList<LiveMark> Marks, bool Moved) Play(int midi, long timeNs)
    {
        if (Done) return ([], false);

        firstNs ??= timeNs;
        lastNs = timeNs;
        var step = steps[Step];
        if (step.Any(n => n.Midi == midi))
        {
            var found = step.Where(n => n.Midi == midi && !played.Contains(n.Id)).ToList();
            played.UnionWith(found.Select(n => n.Id));
            // The chord's keys that went down just before this one are down with it; those from longer ago are let go of.
            foreach (var old in struck.Where(s => timeNs - s.Value > TogetherNs).Select(s => s.Key).ToList()) struck.Remove(old);
            struck[midi] = timeNs;
            var together = step.All(n => struck.ContainsKey(n.Midi));
            // Every key found, but not in one go: its notes are marked apart, all of them, and it waits to be played together.
            var parted = !together && step.All(n => played.Contains(n.Id)) && apart.Add(Step);
            if (together)
            {
                Step++;
                struck.Clear();
            }
            // A note that had a slip keeps its wrong mark.
            var marked = (parted ? step : found).Where(n => !slipped.Contains(n.Id));
            var kind = parted ? LiveKind.Apart : LiveKind.Right;
            return ([.. marked.Select(n => new LiveMark(n.Id, kind, n.Midi, 0))], together);
        }

        var ornament = step.Concat(Step > 0 && !step.Any(n => played.Contains(n.Id)) ? steps[Step - 1] : []).Any(n => ornamented.Contains(n.Id) && Math.Abs(n.Midi - midi) <= 2);
        if (ornament) return ([], false);

        wrongKeys++;
        // The note meant: the nearest still to be found, or of a chord all found and waiting to be played together, the nearest.
        var open = step.Where(n => !played.Contains(n.Id)).ToList();
        var meant = (open.Count > 0 ? open : step).MinBy(n => Math.Abs(n.Midi - midi))!;
        slipped.Add(meant.Id);
        return ([new LiveMark(meant.Id, LiveKind.Wrong, midi, 0)], false);
    }

    /// <summary>
    /// The notes reached: those played, and one with a slip that wasn't put right before the run stopped. Right first time are
    /// those with no slip, in no chord that was played a key at a time first.
    /// </summary>
    public WaitResult Result()
    {
        var parted = apart.SelectMany(at => steps[at]).Select(n => n.Id).ToHashSet();
        var rightFirstTime = played.Count(id => !slipped.Contains(id) && !parted.Contains(id));
        var seconds = firstNs is { } first ? (lastNs - first) / 1e9 : 0;
        return new(played.Union(slipped).Count(), steps.Sum(s => s.Count), rightFirstTime, wrongKeys, seconds) { Apart = apart.Count };
    }
}
