using Prima.Core.Music;

namespace Prima.Core.Scoring;

/// <summary>A note the piano sent: its key, when it went down and came up (ns on the system clock), and how hard.</summary>
public readonly record struct PlayedNote(int Midi, long OnNs, long OffNs, int Velocity);

/// <summary>How far a right note was from its place, after the lean: the research's design inference from the perception studies.</summary>
public enum Band
{
    /// <summary>Under 5% of the beat: a shifted note starts to be noticed at about 2.5–5%.</summary>
    InTime,
    /// <summary>5 to 10% of the beat.</summary>
    Audible,
    /// <summary>Over 10%: a steady pulse starts to break at about 8.6%.</summary>
    Off,
}

public enum Outcome
{
    Right,
    Wrong,
    Missed,
}

/// <summary>A wrong note's kind: one or two semitones away (lost on the keyboard), an octave away, or anything else (a reading error).</summary>
public enum WrongKind
{
    LostOnKeyboard,
    Octave,
    Reading,
}

/// <summary>What happened to one written note: right (with its timing), wrong (and what was played), or missed.</summary>
public sealed record NoteResult(string Id, int Midi, int Bar, Outcome Outcome, double? ErrorMs = null, double? ErrorPercent = null, Band? Band = null, int? Played = null, WrongKind? Wrong = null);

/// <summary>
/// A passage's result (plan, Scoring a passage). In-time accuracy is the headline: right notes not graded off, out of the notes in
/// the passage (audible ones count; ours). The lean is the signed mean error of right notes, taken off before grading.
/// </summary>
public sealed record PassageResult(
    IReadOnlyList<NoteResult> Notes,
    int Extra,
    double LeanMs,
    double LeanPercent,
    double SteadinessPercent,
    double CorrectPerMinute,
    bool KeptGoing,
    double? TempoDriftPercent,
    double RmsMs,
    IReadOnlyDictionary<int, int> ErrorsPerBar,
    double? ChordSpreadMs)
{
    public int Expected => Notes.Count;

    public int Right => Notes.Count(n => n.Outcome == Outcome.Right);

    public int Wrong => Notes.Count(n => n.Outcome == Outcome.Wrong);

    public int Missed => Notes.Count(n => n.Outcome == Outcome.Missed);

    public int InTime => Notes.Count(n => n.Band == Band.InTime);

    public int Audible => Notes.Count(n => n.Band == Band.Audible);

    public int Off => Notes.Count(n => n.Band == Band.Off);

    public int LostOnKeyboard => Notes.Count(n => n.Wrong == WrongKind.LostOnKeyboard);

    /// <summary>Right notes not graded off, out of the notes in the passage: the headline the levels and the 85% aim use.</summary>
    public double InTimeAccuracy => Expected == 0 ? 0 : (InTime + Audible) / (double)Expected;

    /// <summary>Every note right and kept going (Duke: retention follows the share of correct tries).</summary>
    public bool Clean => Wrong == 0 && Missed == 0 && Extra == 0 && KeptGoing;

    /// <summary>Zhukov's counts: a wrong note counts as both a missed and an extra note (ours; the research doesn't say).</summary>
    public int ZhukovMissed => Missed + Wrong;

    public int ZhukovExtra => Extra + Wrong;

    /// <summary>The bar with the most errors (wrong, missed or off), for "one spot"; null for a clean passage.</summary>
    public int? WorstBar => ErrorsPerBar.Where(b => b.Value > 0).OrderByDescending(b => b.Value).ThenBy(b => b.Key).Select(b => (int?)b.Key).FirstOrDefault();
}

/// <summary>The timing rules every scorer shares.</summary>
public static class Grading
{
    public const double InTimePercent = 5;
    public const double AudiblePercent = 10;

    /// <summary>The matching window: 40% of a beat, held between 0.12 and 0.30 s (ours; Kopiez &amp; Lee used ±0.25 s).</summary>
    public static double WindowNs(double beatNs) => Math.Clamp(0.4 * beatNs, 0.12e9, 0.30e9);

    public static Band BandOf(double percentOfBeat) => Math.Abs(percentOfBeat) < InTimePercent ? Band.InTime : Math.Abs(percentOfBeat) <= AudiblePercent ? Band.Audible : Band.Off;

    /// <summary>
    /// Pairs two sorted lists of times, keeping their order, within ±<paramref name="window"/>: the dynamic-programming alignment
    /// that leaves the least total error, each unpaired item costing a window. Returns the pairs as indexes.
    /// </summary>
    public static List<(int Expected, int Played)> Align(IReadOnlyList<double> expected, IReadOnlyList<double> played, double window)
    {
        var (n, m) = (expected.Count, played.Count);
        var cost = new double[n + 1, m + 1];
        for (var i = 0; i <= n; i++) cost[i, 0] = i * window;
        for (var j = 0; j <= m; j++) cost[0, j] = j * window;
        for (var i = 1; i <= n; i++)
        {
            for (var j = 1; j <= m; j++)
            {
                var distance = Math.Abs(expected[i - 1] - played[j - 1]);
                var pair = distance <= window ? cost[i - 1, j - 1] + distance : double.PositiveInfinity;
                cost[i, j] = Math.Min(pair, Math.Min(cost[i - 1, j], cost[i, j - 1]) + window);
            }
        }

        var pairs = new List<(int, int)>();
        for (int i = n, j = m; i > 0 && j > 0;)
        {
            var distance = Math.Abs(expected[i - 1] - played[j - 1]);
            if (distance <= window && Math.Abs(cost[i, j] - (cost[i - 1, j - 1] + distance)) < 1e-6)
            {
                pairs.Add((i - 1, j - 1));
                (i, j) = (i - 1, j - 1);
            }
            else if (Math.Abs(cost[i, j] - (cost[i - 1, j] + window)) < 1e-6) i--;
            else j--;
        }
        pairs.Reverse();
        return pairs;
    }
}

/// <summary>Scores a passage against what was played (plan, Scoring a passage).</summary>
public static class PassageScorer
{
    /// <param name="expected">The notes to play, from <see cref="Score.Expected"/>.</param>
    /// <param name="timeAt">When a moment of the music sounded: beats from bar 1's downbeat to ns, from the click's real beat times.</param>
    /// <param name="beatTicks">Ticks in a beat.</param>
    /// <param name="beatNs">A beat's length, for timing as a share of it.</param>
    /// <param name="barNs">A bar's length, for "kept going".</param>
    /// <param name="played">The notes played.</param>
    /// <param name="offsetMs">The timing calibration's offset, taken off every note.</param>
    /// <param name="ornamented">Ids of notes with a trill or mordent: the extra notes around them aren't counted.</param>
    public static PassageResult Score(IReadOnlyList<ExpectedNote> expected, Func<double, long> timeAt, int beatTicks, double beatNs, double barNs, IReadOnlyList<PlayedNote> played, double offsetMs, IReadOnlySet<string>? ornamented = null)
    {
        var window = Grading.WindowNs(beatNs);
        var times = expected.Select(e => (double)timeAt(e.Tick / (double)beatTicks)).ToArray();
        var first = times.Length == 0 ? 0 : times.Min();
        var last = expected.Count == 0 ? 0 : expected.Select((e, i) => times[i] + e.Length / (double)beatTicks * beatNs).Max();
        // Notes played during the look don't count: scoring starts half a window before the first note.
        var notes = played.Select(p => (Note: p, At: p.OnNs - offsetMs * 1e6)).Where(p => p.At >= first - window / 2 && p.At <= last + window).OrderBy(p => p.At).ToList();

        var matched = new Dictionary<int, int>();
        foreach (var pitch in expected.Select(e => e.Midi).Distinct())
        {
            var e = Enumerable.Range(0, expected.Count).Where(i => expected[i].Midi == pitch).OrderBy(i => times[i]).ToList();
            var p = Enumerable.Range(0, notes.Count).Where(i => notes[i].Note.Midi == pitch).ToList();
            foreach (var (ei, pi) in Grading.Align([.. e.Select(i => times[i])], [.. p.Select(i => notes[i].At)], window)) matched[e[ei]] = p[pi];
        }

        // Left-over played notes near a missed note are wrong notes, closest in time first, then nearest in pitch.
        var usedPlayed = matched.Values.ToHashSet();
        var wrong = new Dictionary<int, int>();
        var candidates = from ei in Enumerable.Range(0, expected.Count).Where(i => !matched.ContainsKey(i))
                         from pi in Enumerable.Range(0, notes.Count).Where(i => !usedPlayed.Contains(i))
                         let distance = Math.Abs(times[ei] - notes[pi].At)
                         where distance <= window
                         orderby distance, Math.Abs(expected[ei].Midi - notes[pi].Note.Midi)
                         select (ei, pi);
        foreach (var (ei, pi) in candidates)
        {
            if (wrong.ContainsKey(ei) || usedPlayed.Contains(pi)) continue;

            wrong[ei] = pi;
            usedPlayed.Add(pi);
        }

        // Extra notes, apart from those around an ornament (a trill's notes, a mordent's).
        var ornaments = expected.Select((e, i) => (e, i)).Where(x => ornamented?.Contains(x.e.Id) == true).ToList();
        var extra = Enumerable.Range(0, notes.Count).Count(i => !usedPlayed.Contains(i) && !ornaments.Any(o => Math.Abs(notes[i].Note.Midi - o.e.Midi) <= 2 && notes[i].At >= times[o.i] - window && notes[i].At <= times[o.i] + o.e.Length / (double)beatTicks * beatNs));

        var errors = matched.ToDictionary(m => m.Key, m => (notes[m.Value].At - times[m.Key]) / 1e6);
        var lean = errors.Count == 0 ? 0 : errors.Values.Average();
        var beatMs = beatNs / 1e6;
        var results = expected.Select((e, i) =>
        {
            if (matched.TryGetValue(i, out _))
            {
                var percent = (errors[i] - lean) / beatMs * 100;
                return new NoteResult(e.Id, e.Midi, e.Bar, Outcome.Right, errors[i], percent, Grading.BandOf(percent));
            }
            if (wrong.TryGetValue(i, out var pi))
            {
                var played = notes[pi].Note.Midi;
                var apart = Math.Abs(played - e.Midi);
                return new NoteResult(e.Id, e.Midi, e.Bar, Outcome.Wrong, Played: played, Wrong: apart <= 2 ? WrongKind.LostOnKeyboard : apart % 12 == 0 ? WrongKind.Octave : WrongKind.Reading);
            }
            return new NoteResult(e.Id, e.Midi, e.Bar, Outcome.Missed);
        }).ToList();

        var afterLean = errors.Values.Select(v => (v - lean) / beatMs * 100).ToArray();
        var minutes = Math.Max(1e-9, (last - first) / 60e9);

        // Kept going: no stretch longer than a bar between notes played (or from the first due note) with a note due inside it.
        var onsets = notes.Select(n => n.At).Prepend(first - window).Append(last).Order().ToArray();
        var keptGoing = !onsets.Zip(onsets.Skip(1)).Any(g => g.Second - g.First > barNs && times.Any(t => t > g.First + window && t < g.Second - window));

        // Zhukov's tempo drift: a line through played against expected times; a slope over 1 means it was played slower.
        double? drift = null;
        if (matched.Count >= 3)
        {
            var (_, slope) = Stats.Line([.. matched.Keys.Select(i => times[i])], [.. matched.Select(m => notes[m.Value].At)]);
            if (slope > 0) drift = (1 / slope - 1) * 100;
        }

        var perBar = expected.Select(e => e.Bar).Distinct().ToDictionary(b => b, b => results.Count(r => r.Bar == b && (r.Outcome != Outcome.Right || r.Band == Band.Off)));
        var chordSpreads = expected.Select((e, i) => (e, i)).Where(x => x.e.ChordId is not null && matched.ContainsKey(x.i)).GroupBy(x => x.e.ChordId).Where(g => g.Count() >= 2).Select(g => (g.Max(x => notes[matched[x.i]].At) - g.Min(x => notes[matched[x.i]].At)) / 1e6).ToList();

        return new PassageResult(results, extra, lean, lean / beatMs * 100, Stats.StandardDeviation(afterLean), results.Count(r => r.Outcome == Outcome.Right) / minutes, keptGoing, drift, Stats.RootMeanSquare([.. errors.Values]), perBar, chordSpreads.Count == 0 ? null : chordSpreads.Average());
    }
}
