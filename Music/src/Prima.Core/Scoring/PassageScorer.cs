using Prima.Core.Music;

namespace Prima.Core.Scoring;

/// <summary>A note the piano sent: its key, when it went down and came up (ns on the system clock), and how hard.</summary>
public readonly record struct PlayedNote(int Midi, long OnNs, long OffNs, int Velocity)
{
    /// <summary>Still down when the run ended, so <see cref="OffNs"/> is the end, not when it was let go.</summary>
    public bool HeldToEnd { get; init; }
}

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

/// <summary>
/// What happened to one written note: right (with its timing, and how long it was held as a share of its length when that's
/// judged), wrong (and what was played: the wrong key, though the right one followed it at once), or missed.
/// </summary>
public sealed record NoteResult(string Id, int Midi, int Bar, Outcome Outcome, double? ErrorMs = null, double? ErrorPercent = null, Band? Band = null, int? Played = null, WrongKind? Wrong = null,
    double? HeldPercent = null, bool LetGoEarly = false, bool Corrected = false);

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

    /// <summary>Wrong notes whose right key came straight after the wrong one: a slip put right, which still counts.</summary>
    public int Corrected => Notes.Count(n => n.Corrected);

    /// <summary>Right notes whose hold was judged: not staccato, the lifted end of a slur, ornamented, or cut off by a stop.</summary>
    public int HoldsJudged => Notes.Count(n => n.HeldPercent is not null);

    /// <summary>Right notes let go before their time: see <see cref="Grading.LetGoEarly"/>.</summary>
    public int LetGoEarly => Notes.Count(n => n.LetGoEarly);

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
    /// A note let go early: sounding (its key down, or the damper pedal holding it) short of its written length by more than a fifth
    /// of it and more than a quarter of a beat, so a half note held a beat and a half is, and a quarter needs three quarters of a
    /// beat. Strict, with room for a pianist's lift between notes; notes a quarter of a beat or shorter aren't judged (ours).
    /// </summary>
    public static bool LetGoEarly(double heldNs, double writtenNs, double beatNs) => writtenNs - heldNs > Math.Max(0.2 * writtenNs, 0.25 * beatNs);

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
    /// <param name="ornamented">Ids of notes with a trill or mordent, or grace notes before them: the keys around them aren't counted.</param>
    /// <param name="damper">When the damper pedal was down, from and to: a key let go while it's down sounds on until it comes up.</param>
    public static PassageResult Score(IReadOnlyList<ExpectedNote> expected, Func<double, long> timeAt, int beatTicks, double beatNs, double barNs, IReadOnlyList<PlayedNote> played, double offsetMs,
        IReadOnlySet<string>? ornamented = null, IReadOnlyList<(long DownNs, long UpNs)>? damper = null)
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

        // Keys around an ornament (a trill's, a mordent's, grace notes before a note) are part of it: neither slips nor extra.
        var ornaments = expected.Select((e, i) => (e, i)).Where(x => ornamented?.Contains(x.e.Id) == true).ToList();
        bool Ornament(int pi) => ornaments.Any(o => Math.Abs(notes[pi].Note.Midi - o.e.Midi) <= 2 && notes[pi].At >= times[o.i] - window && notes[pi].At <= times[o.i] + o.e.Length / (double)beatTicks * beatNs);

        // A key in a right note's window before its right key is a slip: the note is wrong, though put right at once. Nearest in
        // time first, then in pitch. A key of a note written at that moment, struck twice, is only extra.
        var slips = from ei in matched.Keys
                    from pi in Enumerable.Range(0, notes.Count).Where(i => !usedPlayed.Contains(i) && !Ornament(i))
                    let distance = Math.Abs(times[ei] - notes[pi].At)
                    where distance <= window && notes[pi].At < notes[matched[ei]].At && !expected.Any(x => x.Tick == expected[ei].Tick && x.Midi == notes[pi].Note.Midi)
                    orderby distance, Math.Abs(expected[ei].Midi - notes[pi].Note.Midi)
                    select (ei, pi);
        var corrected = new HashSet<int>();
        foreach (var (ei, pi) in slips.ToList())
        {
            if (corrected.Contains(ei) || usedPlayed.Contains(pi)) continue;

            wrong[ei] = pi;
            corrected.Add(ei);
            usedPlayed.Add(pi);
        }
        // The key that put it right was played for it, so it isn't extra; its timing isn't graded.
        foreach (var ei in corrected) matched.Remove(ei);

        var extra = Enumerable.Range(0, notes.Count).Count(i => !usedPlayed.Contains(i) && !Ornament(i));

        var errors = matched.ToDictionary(m => m.Key, m => (notes[m.Value].At - times[m.Key]) / 1e6);
        var lean = errors.Count == 0 ? 0 : errors.Values.Average();
        var beatMs = beatNs / 1e6;
        var results = expected.Select((e, i) =>
        {
            if (matched.TryGetValue(i, out var right))
            {
                var percent = (errors[i] - lean) / beatMs * 100;
                // How long it sounded: until its key came up, or the damper pedal did if it was down then (unless the key was struck again).
                // Not judged for staccato, a slur's lifted end, an ornament, or a note a stop cut off.
                var key = notes[right].Note;
                var pedal = damper?.Where(d => d.DownNs <= key.OffNs && d.UpNs > key.OffNs).Select(d => d.UpNs).DefaultIfEmpty(key.OffNs).Max() ?? key.OffNs;
                var again = notes.Where(n => n.Note.Midi == key.Midi && n.Note.OnNs > key.OnNs).Select(n => n.Note.OnNs).DefaultIfEmpty(long.MaxValue).Min();
                var held = (double)Math.Min(pedal, Math.Max(key.OffNs, again)) - key.OnNs;
                var writtenNs = e.Length / (double)beatTicks * beatNs;
                var judged = writtenNs > 0 && !e.Lifted && ornamented?.Contains(e.Id) != true && (!key.HeldToEnd || held >= writtenNs);
                return new NoteResult(e.Id, e.Midi, e.Bar, Outcome.Right, errors[i], percent, Grading.BandOf(percent),
                    HeldPercent: judged ? held / writtenNs * 100 : null, LetGoEarly: judged && Grading.LetGoEarly(held, writtenNs, beatNs));
            }
            if (wrong.TryGetValue(i, out var pi))
            {
                var played = notes[pi].Note.Midi;
                var apart = Math.Abs(played - e.Midi);
                return new NoteResult(e.Id, e.Midi, e.Bar, Outcome.Wrong, Played: played, Wrong: apart <= 2 ? WrongKind.LostOnKeyboard : apart % 12 == 0 ? WrongKind.Octave : WrongKind.Reading, Corrected: corrected.Contains(i));
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
