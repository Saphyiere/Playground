using Prima.Core.Generation;

namespace Prima.Core.Scoring;

/// <summary>
/// A rhythm's result: taps on any key against the written rhythm, in % of the beat. The opening bar of beat is graded on its own.
/// The headline is taps not off, out of the notes in the rhythm.
/// </summary>
public sealed record RhythmResult(IReadOnlyList<NoteResult> Notes, int Extra, double LeanPercent, double SteadinessPercent, double? DriftPercent, double PulseLeanPercent, double PulseSteadinessPercent, bool KeptGoing)
{
    public int Missed => Notes.Count(n => n.Outcome != Outcome.Right);

    public double Accuracy => Notes.Count == 0 ? 0 : Notes.Count(n => n.Band is Band.InTime or Band.Audible) / (double)Notes.Count;
}

/// <summary>A bar of a rhythm try: its taps' average against the click (% of a beat, late positive), those well early or late, and those missed.</summary>
public sealed record BarTiming(int Bar, double? Percent, int Early, int Late, int Missed)
{
    public bool Off => Early + Late > 0 || Percent is > Grading.AudiblePercent or < -Grading.AudiblePercent;
}

public static class RhythmScorer
{
    /// <summary>
    /// How each bar went, for the words after a try: where its taps landed on average against the click, in % of a beat (late is
    /// positive; none when nothing in it was tapped), how many landed more than 10% of a beat early or late, and how many of its
    /// notes were missed. Your lean isn't taken out: this is where the taps really were.
    /// </summary>
    /// <param name="bars">Each note's bar, in the order of <paramref name="notes"/>.</param>
    public static IReadOnlyList<BarTiming> ByBar(IReadOnlyList<NoteResult> notes, IReadOnlyList<int> bars, double beatNs) =>
    [
        .. notes.Zip(bars).GroupBy(p => p.Second).OrderBy(g => g.Key).Select(g =>
        {
            var tapped = g.Where(p => p.First.Outcome == Outcome.Right && p.First.ErrorMs is not null).Select(p => p.First.ErrorMs!.Value / (beatNs / 1e6) * 100).ToList();
            return new BarTiming(g.Key, tapped.Count == 0 ? null : tapped.Average(), tapped.Count(p => p < -Grading.AudiblePercent), tapped.Count(p => p > Grading.AudiblePercent), g.Count(p => p.First.Outcome != Outcome.Right));
        }),
    ];

    /// <param name="onsetsNs">The written rhythm's onsets, the opening bar of beat first, on the click's timeline.</param>
    /// <param name="ids">The sheet's id for each onset, for marks.</param>
    /// <param name="pulseNotes">How many of the onsets are the opening bar of beat.</param>
    public static RhythmResult Score(IReadOnlyList<long> onsetsNs, IReadOnlyList<string> ids, int pulseNotes, IReadOnlyList<long> tapsNs, double beatNs, double barNs, double offsetMs)
    {
        var window = Grading.WindowNs(beatNs);
        var expected = onsetsNs.Select(t => (double)t).ToList();
        var taps = tapsNs.Select(t => t - offsetMs * 1e6).Where(t => expected.Count > 0 && t >= expected[0] - window / 2 && t <= expected[^1] + window).Order().ToList();
        var pairs = Grading.Align(expected, taps, window);
        var errors = pairs.ToDictionary(p => p.Expected, p => (taps[p.Played] - expected[p.Expected]) / 1e6);
        var beatMs = beatNs / 1e6;
        var rhythmErrors = errors.Where(e => e.Key >= pulseNotes).Select(e => e.Value).ToArray();
        var pulseErrors = errors.Where(e => e.Key < pulseNotes).Select(e => e.Value).ToArray();
        var lean = rhythmErrors.Length == 0 ? 0 : rhythmErrors.Average();
        var pulseLean = pulseErrors.Length == 0 ? 0 : pulseErrors.Average();
        var notes = expected.Select((_, i) => errors.TryGetValue(i, out var ms)
            ? new NoteResult(ids[i], 0, 0, Outcome.Right, ms, (ms - (i < pulseNotes ? pulseLean : lean)) / beatMs * 100, Grading.BandOf((ms - (i < pulseNotes ? pulseLean : lean)) / beatMs * 100))
            : new NoteResult(ids[i], 0, 0, Outcome.Missed)).ToList();

        double? drift = null;
        if (pairs.Count >= 3)
        {
            var (_, slope) = Stats.Line([.. pairs.Select(p => expected[p.Expected])], [.. pairs.Select(p => taps[p.Played])]);
            if (slope > 0) drift = (1 / slope - 1) * 100;
        }

        var onsets = taps.Prepend(expected[0] - window).Append(expected[^1]).Order().ToArray();
        var keptGoing = !onsets.Zip(onsets.Skip(1)).Any(g => g.Second - g.First > barNs && expected.Any(t => t > g.First + window && t < g.Second - window));
        return new RhythmResult(notes, taps.Count - pairs.Count, lean / beatMs * 100, Stats.StandardDeviation([.. rhythmErrors.Select(e => (e - lean) / beatMs * 100)]), drift, pulseLean / beatMs * 100, Stats.StandardDeviation([.. pulseErrors.Select(e => (e - pulseLean) / beatMs * 100)]), keptGoing);
    }
}

/// <summary>A written note of a run and what became of it: right, wrong (the key played in its place) or missed.</summary>
public sealed record RunNote(string Id, int Midi, Outcome Outcome, int? Played = null);

/// <summary>A key the run didn't ask for, and the written note it came after.</summary>
public sealed record ExtraKey(string AfterId, int Played);

/// <summary>
/// One hand's run of a scale or arpeggio: each written note as played, and any extra keys. Evenness is only measured on a valid
/// run: every note right and in order, nothing extra.
/// </summary>
public sealed record HandRun(
    bool Valid,
    IReadOnlyList<RunNote> Notes,
    IReadOnlyList<ExtraKey> Extras,
    double IoiSdMs,
    double IoiSdPercent,
    double PlainIoiSdMs,
    double Bpm,
    double SlopeMsPerNote,
    double? CrossingMs,
    double? SeamMs,
    double VelocitySd,
    IReadOnlyDictionary<int, double> VelocityByFinger,
    double LegatoMs,
    double LegatoSdMs,
    IReadOnlyList<double> Deviations)
{
    public int Wrong => Notes.Count(n => n.Outcome == Outcome.Wrong);

    public int Missed => Notes.Count(n => n.Outcome == Outcome.Missed);
}

public sealed record TechniqueResult(HandRun? Right, HandRun? Left, double? SyncMeanMs, double? SyncSdMs)
{
    public bool Valid => (Right?.Valid ?? true) && (Left?.Valid ?? true);
}

/// <summary>
/// Technique measures from MIDI (plan, Technique): the gaps between onsets with a line fitted through gap against note index (its
/// mean is the run's own tempo, its slope a steady speed-up or slow-down), so the IOI SD is the spread around that line and drift
/// isn't counted as unevenness; the plain IOI SD too, for Jabusch's 8–9 ms; crossings and octave seams apart; touch; legato as
/// key overlap; and the hands' sync.
/// </summary>
public static class TechniqueScorer
{
    /// <summary>Timing differences under this are shown as below what anyone can hear (research §4, at a 125 ms gap; a guide).</summary>
    public const double HearingMs = 10;

    public static TechniqueResult Score(TechniqueItem item, IReadOnlyList<PlayedNote> played)
    {
        var keys = played.OrderBy(p => p.OnNs).ToList();
        var (rightKeys, leftKeys, extras) = Align(item.Right, item.Left, keys);
        var rightRun = item.Right.Count == 0 ? null : Run(item.Right, keys, rightKeys, [.. extras.Where(e => e.Right).Select(e => e.Key)], item.NotesPerBeat);
        var leftRun = item.Left.Count == 0 ? null : Run(item.Left, keys, leftKeys, [.. extras.Where(e => !e.Right).Select(e => e.Key)], item.NotesPerBeat);
        double? syncMean = null, syncSd = null;
        if (rightRun?.Valid == true && leftRun?.Valid == true)
        {
            var differences = rightKeys.Zip(leftKeys, (r, l) => (keys[r].OnNs - keys[l].OnNs) / 1e6).ToArray();
            (syncMean, syncSd) = (differences.Average(), Stats.StandardDeviation(differences));
        }
        return new TechniqueResult(rightRun, leftRun, syncMean, syncSd);
    }

    /// <summary>
    /// Lines the keys up with both hands' notes by the fewest slips, an edit distance: each key is a hand's next note, a wrong key
    /// in its place (given to the hand whose note is nearer) or an extra, and a written note can be missed. Order counts within a
    /// hand, not between them, so one slip stays one slip instead of throwing out every note after it. Keys before the first key
    /// lined up and after the last aren't part of the run (one pressed in the count-in, or the piano's button key after the last
    /// note), so they're left out. Returns the key each note was played with (−1 for missed), and the extras with their hand and
    /// the note they came after.
    /// </summary>
    private static (int[] Right, int[] Left, List<(bool Right, ExtraKey Key)> Extras) Align(IReadOnlyList<TechniqueNote> right, IReadOnlyList<TechniqueNote> left, IReadOnlyList<PlayedNote> keys)
    {
        const int Slip = 1000;
        // A missed note or an extra key costs a little more than a slip, and a wrong key a little more the further it is from the
        // written note: with as many slips either way, keys played in a note's place are read as wrong notes rather than as a
        // missed note and an extra, and a wrong key goes to the hand whose note is nearer.
        const int Gap = Slip + 64;
        var (a, b, n) = (right.Count, left.Count, keys.Count);
        int At(int i, int j, int k) => (i * (b + 1) + j) * (n + 1) + k;
        int Pair(int written, int key) => written == key ? 0 : Slip + Math.Abs(written - key);
        var cost = new int[(a + 1) * (b + 1) * (n + 1)];
        var how = new byte[cost.Length];
        for (var i = 0; i <= a; i++)
        {
            for (var j = 0; j <= b; j++)
            {
                for (var k = 0; k <= n; k++)
                {
                    if (i + j + k == 0) continue;

                    var (best, step) = (int.MaxValue, (byte)0);
                    if (i > 0 && k > 0 && cost[At(i - 1, j, k - 1)] + Pair(right[i - 1].Midi, keys[k - 1].Midi) is var r && r < best) (best, step) = (r, 1);
                    if (j > 0 && k > 0 && cost[At(i, j - 1, k - 1)] + Pair(left[j - 1].Midi, keys[k - 1].Midi) is var l && l < best) (best, step) = (l, 2);
                    if (i > 0 && cost[At(i - 1, j, k)] + Gap < best) (best, step) = (cost[At(i - 1, j, k)] + Gap, 3);
                    if (j > 0 && cost[At(i, j - 1, k)] + Gap < best) (best, step) = (cost[At(i, j - 1, k)] + Gap, 4);
                    if (k > 0 && cost[At(i, j, k - 1)] + Gap < best) (best, step) = (cost[At(i, j, k - 1)] + Gap, 5);
                    (cost[At(i, j, k)], how[At(i, j, k)]) = (best, step);
                }
            }
        }

        var rightKeys = Enumerable.Repeat(-1, a).ToArray();
        var leftKeys = Enumerable.Repeat(-1, b).ToArray();
        var extras = new List<(int Key, int I, int J)>();
        for (int i = a, j = b, k = n; i + j + k > 0;)
        {
            switch (how[At(i, j, k)])
            {
                case 1: rightKeys[--i] = --k; break;
                case 2: leftKeys[--j] = --k; break;
                case 3: i--; break;
                case 4: j--; break;
                default: extras.Add((--k, i, j)); break;
            }
        }

        var lined = rightKeys.Concat(leftKeys).Where(k => k >= 0).DefaultIfEmpty(-1).ToList();
        var (first, last) = (lined.Min(), lined.Max());
        var kept = extras.Where(e => e.Key > first && e.Key < last).OrderBy(e => e.Key).Select(e =>
        {
            // Beside the note before it in the hand it's nearer.
            var (r, l) = (right.Count > 0 ? right[Math.Max(e.I - 1, 0)] : null, left.Count > 0 ? left[Math.Max(e.J - 1, 0)] : null);
            var inRight = l is null || (r is not null && Math.Abs(keys[e.Key].Midi - r.Midi) <= Math.Abs(keys[e.Key].Midi - l.Midi));
            return (inRight, new ExtraKey((inRight ? r! : l!).Id, keys[e.Key].Midi));
        }).ToList();
        return (rightKeys, leftKeys, kept);
    }

    private static HandRun Run(IReadOnlyList<TechniqueNote> written, IReadOnlyList<PlayedNote> keys, int[] lined, IReadOnlyList<ExtraKey> extras, int perBeat)
    {
        var notes = written.Select((w, i) => lined[i] < 0 ? new RunNote(w.Id, w.Midi, Outcome.Missed)
            : keys[lined[i]].Midi == w.Midi ? new RunNote(w.Id, w.Midi, Outcome.Right)
            : new RunNote(w.Id, w.Midi, Outcome.Wrong, keys[lined[i]].Midi)).ToList();
        var played = lined.Where(k => k >= 0).Select(k => keys[k]).ToList();
        if (notes.Any(n => n.Outcome != Outcome.Right) || extras.Count > 0 || played.Count < 3) return new HandRun(false, notes, extras, 0, 0, 0, 0, 0, null, null, 0, new Dictionary<int, double>(), 0, 0, []);

        var gaps = played.Zip(played.Skip(1), (a, b) => (b.OnNs - a.OnNs) / 1e6).ToArray();
        var index = Enumerable.Range(0, gaps.Length).Select(i => (double)i).ToArray();
        var (intercept, slope) = Stats.Line(index, gaps);
        var deviations = gaps.Select((g, i) => g - (intercept + slope * i)).ToArray();
        var mean = gaps.Average();
        var crossings = deviations.Where((_, i) => written[i].CrossingNext).ToArray();
        var seams = deviations.Where((_, i) => written[i].SeamNext).ToArray();
        var overlaps = played.Zip(played.Skip(1), (a, b) => (a.OffNs - b.OnNs) / 1e6).ToArray();
        var byFinger = written.Zip(played).GroupBy(p => p.First.Finger).ToDictionary(g => g.Key, g => g.Average(p => (double)p.Second.Velocity));
        return new HandRun(
            true,
            notes,
            extras,
            Stats.StandardDeviation(deviations),
            Stats.StandardDeviation(deviations) / mean * 100,
            Stats.StandardDeviation(gaps),
            60_000 / (mean * perBeat),
            slope,
            crossings.Length == 0 ? null : crossings.Average(),
            seams.Length == 0 ? null : seams.Select(Math.Abs).Average(),
            Stats.StandardDeviation([.. played.Select(p => (double)p.Velocity)]),
            byFinger,
            overlaps.Average(),
            Stats.StandardDeviation(overlaps),
            deviations);
    }

    /// <summary>
    /// Over several valid runs of the same item: the consistent part (the same gap long every time: the size of each position's
    /// mean deviation) apart from the random part (each position's spread across runs), both in ms.
    /// </summary>
    public static (double ConsistentMs, double RandomMs) Consistency(IReadOnlyList<IReadOnlyList<double>> runs)
    {
        var length = runs.Min(r => r.Count);
        var means = Enumerable.Range(0, length).Select(i => runs.Average(r => r[i])).ToArray();
        var spreads = Enumerable.Range(0, length).Select(i => Stats.StandardDeviation([.. runs.Select(r => r[i])])).ToArray();
        return (Stats.RootMeanSquare(means), spreads.Average());
    }

    /// <summary>How a timing figure is described: below hearing under 10 ms, within the keyboard's noise under its floor, else as ms.</summary>
    public static string Describe(double ms, double? noiseFloorMs) =>
        noiseFloorMs is { } floor && Math.Abs(ms) < floor ? "within the keyboard's noise" : Math.Abs(ms) < HearingMs ? "below what anyone can hear" : $"{ms:0} ms";
}

/// <summary>Drill answers (plan, Drill scoring): the first note-on for a note; for a chord, every note-on within 150 ms of the first (ours).</summary>
public static class DrillScorer
{
    public const long ChordWindowNs = 150_000_000;

    public sealed record NoteAnswer(bool Right, double ResponseMs, int Played, bool OctaveOff, bool LostOnKeyboard);

    public sealed record ChordAnswer(bool Right, int Missing, int Extra, double SpreadMs, double ResponseMs);

    public sealed record FigureAnswer(bool Right, bool WrongFirstNote, int WrongIntervals, double ResponseMs, IReadOnlyList<bool> Steps);

    public static NoteAnswer? Note(int midi, IReadOnlyList<PlayedNote> played, long shownNs)
    {
        var first = played.Where(p => p.OnNs >= shownNs).OrderBy(p => p.OnNs).FirstOrDefault();
        if (first.OnNs == 0 && first.Midi == 0) return null;

        var apart = Math.Abs(first.Midi - midi);
        return new NoteAnswer(apart == 0, (first.OnNs - shownNs) / 1e6, first.Midi, apart != 0 && apart % 12 == 0, apart is 1 or 2);
    }

    public static ChordAnswer? Chord(IReadOnlyList<int> target, IReadOnlyList<PlayedNote> played, long shownNs)
    {
        var after = played.Where(p => p.OnNs >= shownNs).OrderBy(p => p.OnNs).ToList();
        if (after.Count == 0) return null;

        var chord = after.Where(p => p.OnNs - after[0].OnNs <= ChordWindowNs).ToList();
        var keys = chord.Select(p => p.Midi).Distinct().ToHashSet();
        var missing = target.Count(t => !keys.Contains(t));
        var extra = keys.Count(k => !target.Contains(k));
        return new ChordAnswer(missing == 0 && extra == 0, missing, extra, (chord.Max(p => p.OnNs) - chord.Min(p => p.OnNs)) / 1e6, (after[0].OnNs - shownNs) / 1e6);
    }

    /// <summary>A figure's notes in order: each step right or wrong, and a wrong first note kept apart from wrong intervals.</summary>
    public static FigureAnswer? Figure(IReadOnlyList<int> target, IReadOnlyList<PlayedNote> played, long shownNs)
    {
        var notes = played.Where(p => p.OnNs >= shownNs).OrderBy(p => p.OnNs).Take(target.Count).ToList();
        if (notes.Count < target.Count) return null;

        var steps = notes.Select((n, i) => n.Midi == target[i]).ToList();
        var intervalsRight = Enumerable.Range(1, target.Count - 1).Select(i => notes[i].Midi - notes[i - 1].Midi == target[i] - target[i - 1]).ToList();
        var wrongFirst = notes[0].Midi != target[0] && intervalsRight.All(r => r);
        return new FigureAnswer(steps.All(s => s), wrongFirst, intervalsRight.Count(r => !r), (notes[0].OnNs - shownNs) / 1e6, steps);
    }

    /// <summary>"Forgot the key": notes the signature sharpens or flattens that were played without it.</summary>
    public static int ForgotTheKey(PassageResult result, IReadOnlySet<string> affected, Music.Key key, IReadOnlyList<Music.ExpectedNote> expected) =>
        result.Notes.Count(n => n.Outcome == Outcome.Wrong && affected.Contains(n.Id) && n.Played is { } played && expected.First(e => e.Id == n.Id) is var written && played == written.Midi - SignatureAlter(written, key));

    private static int SignatureAlter(Music.ExpectedNote note, Music.Key key) => key.AlterOf(key.Spell(note.Midi).Step);
}
