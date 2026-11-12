using Prima.Core.Music;

namespace Prima.Core.Scoring;

/// <summary>How a Wait for me run went: the notes reached, how many were right first time, the wrong keys, and the pace.</summary>
public sealed record WaitResult(int Notes, int Total, int RightFirstTime, int WrongKeys, double Seconds)
{
    /// <summary>The chords (keys that go down together, in one hand or both) played a key at a time before they were played together.</summary>
    public int Apart { get; init; }

    /// <summary>
    /// When each step of the music was played: the moment the last of its keys went down. The pace from one moment of the music
    /// to the next, however many keys a moment takes (two, hands together).
    /// </summary>
    public IReadOnlyList<long> StepNs { get; init; } = [];

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
/// was. Keys within a tone of an ornamented note (a trill's or a mordent's), in its step or just after it, aren't slips. A
/// chord the music marks to be rolled is played as a roll: its keys one after another, none more than <see cref="RollGapNs"/>
/// after the one before; with a longer wait between two of them it's marked apart, and waits to be rolled in one go. No other
/// time is judged: the pace is your own.
/// </summary>
public sealed class WaitJudge
{
    /// <summary>
    /// The longest from a chord's first key to its last for them to be together: 0.15 s. Hands that mean to land together are
    /// well inside it, and keys played one after another well outside.
    /// </summary>
    public const long TogetherNs = 150_000_000;

    /// <summary>
    /// The longest from one key of a rolled chord to the next: 0.3 s. A roll's keys follow each other a few hundredths of a
    /// second apart, a slow one's a tenth or two; keys being looked for one at a time come further apart than that.
    /// </summary>
    public const long RollGapNs = 300_000_000;

    private readonly List<IReadOnlyList<ExpectedNote>> steps;
    private readonly HashSet<string> ornamented;
    private readonly HashSet<string> rolled;
    private readonly HashSet<string> played = [];
    private readonly HashSet<string> slipped = [];
    // The step's keys that are down together so far, each with when it went down, and the steps played a key at a time.
    private readonly Dictionary<int, long> struck = [];
    private readonly HashSet<int> apart = [];
    private readonly List<long> stepNs = [];
    // When keys went down while each step was waited for, the first and the last, and the step each wrong key came at.
    private readonly Dictionary<int, (long First, long Last)> keyed = [];
    private readonly List<int> wrongAt = [];

    public WaitJudge(IEnumerable<ExpectedNote> notes, IReadOnlySet<string>? ornamented = null, IReadOnlySet<string>? rolled = null)
    {
        steps = [.. notes.GroupBy(n => n.Tick).OrderBy(g => g.Key).Select(g => (IReadOnlyList<ExpectedNote>)[.. g.OrderBy(n => n.Midi)])];
        this.ornamented = ornamented is null ? [] : [.. ornamented];
        this.rolled = rolled is null ? [] : [.. rolled];
    }

    /// <summary>The step waited for, from 0.</summary>
    public int Step { get; private set; }

    public bool Done => Step >= steps.Count;

    /// <summary>The notes waited for: the cursor's place.</summary>
    public IReadOnlyList<ExpectedNote> Current => Done ? [] : steps[Step];

    /// <summary>Whether the chord waited for has had its keys played, but one after another: it waits for them together.</summary>
    public bool Apart => apart.Contains(Step);

    /// <summary>Whether the chord waited for is one the music marks to be rolled.</summary>
    public bool Rolling => !Done && steps[Step].Any(n => rolled.Contains(n.Id));

    /// <summary>A key going down: the marks it makes (none for a key that's nothing), and whether it moved the cursor on.</summary>
    public (IReadOnlyList<LiveMark> Marks, bool Moved) Play(int midi, long timeNs)
    {
        if (Done) return ([], false);

        keyed[Step] = keyed.TryGetValue(Step, out var was) ? (was.First, timeNs) : (timeNs, timeNs);
        var step = steps[Step];
        if (step.Any(n => n.Midi == midi))
        {
            var found = step.Where(n => n.Midi == midi && !played.Contains(n.Id)).ToList();
            played.UnionWith(found.Select(n => n.Id));
            // The chord's keys that went down just before this one are down with it; those from longer ago are let go of. A
            // rolled chord's follow one another: after a longer wait than a roll has, the roll starts again from this key.
            if (!Rolling) foreach (var old in struck.Where(s => timeNs - s.Value > TogetherNs).Select(s => s.Key).ToList()) struck.Remove(old);
            else if (struck.Count > 0 && timeNs - struck.Values.Max() > RollGapNs) struck.Clear();
            struck[midi] = timeNs;
            var together = step.All(n => struck.ContainsKey(n.Midi));
            // Every key found, but not in one go: its notes are marked apart, all of them, and it waits to be played together.
            var parted = !together && step.All(n => played.Contains(n.Id)) && apart.Add(Step);
            if (together)
            {
                Step++;
                struck.Clear();
                stepNs.Add(timeNs);
            }
            // A note that had a slip keeps its wrong mark.
            var marked = (parted ? step : found).Where(n => !slipped.Contains(n.Id));
            var kind = parted ? LiveKind.Apart : LiveKind.Right;
            return ([.. marked.Select(n => new LiveMark(n.Id, kind, n.Midi, 0))], together);
        }

        var ornament = step.Concat(Step > 0 && !step.Any(n => played.Contains(n.Id)) ? steps[Step - 1] : []).Any(n => ornamented.Contains(n.Id) && Math.Abs(n.Midi - midi) <= 2);
        if (ornament) return ([], false);

        wrongAt.Add(Step);
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
    public WaitResult Result() => Stretch(long.MinValue, long.MaxValue);

    /// <summary>
    /// How a stretch of the music went, from a tick up to another (one time round a loop), counted as <see cref="Result"/> counts
    /// the whole run: its notes, the keys played while its steps were waited for, and the time from the first of those keys to
    /// the last.
    /// </summary>
    public WaitResult Stretch(long fromTick, long toTick)
    {
        var inside = Enumerable.Range(0, steps.Count).Where(s => steps[s][0].Tick >= fromTick && steps[s][0].Tick < toTick).ToHashSet();
        var notes = inside.SelectMany(s => steps[s]).ToList();
        var parted = apart.Where(inside.Contains).SelectMany(s => steps[s]).Select(n => n.Id).ToHashSet();
        var reached = notes.Count(n => played.Contains(n.Id) || slipped.Contains(n.Id));
        var right = notes.Count(n => played.Contains(n.Id) && !slipped.Contains(n.Id) && !parted.Contains(n.Id));
        var keys = keyed.Where(k => inside.Contains(k.Key)).Select(k => k.Value).ToList();
        var seconds = keys.Count == 0 ? 0 : (keys.Max(k => k.Last) - keys.Min(k => k.First)) / 1e9;
        var (wrong, chords) = (wrongAt.Count(inside.Contains), apart.Count(inside.Contains));
        return new(reached, notes.Count, right, wrong, seconds) { Apart = chords, StepNs = [.. stepNs.Where((_, s) => inside.Contains(s))] };
    }
}
