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
/// What happened to one written note: right (with its timing, how long it was held as a share of its length when that's judged,
/// how hard it was struck, and its articulation as asked and as played), wrong (and what was played: the wrong key, though the
/// right one followed it at once), or missed.
/// </summary>
public sealed record NoteResult(string Id, int Midi, int Bar, Outcome Outcome, double? ErrorMs = null, double? ErrorPercent = null, Band? Band = null, int? Played = null, WrongKind? Wrong = null,
    double? HeldPercent = null, bool LetGoEarly = false, bool Corrected = false, int? Velocity = null, Touch? Touch = null, Articulation Asked = Articulation.None);

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

    /// <summary>Right notes whose articulation wasn't played as asked (staccato, tenuto, an accent, a slur).</summary>
    public int Touches => Notes.Count(n => n.Touch is not null);

    /// <summary>Each stretch marked with a dynamic, in order, and how loud it was played; none when the touch didn't vary.</summary>
    public IReadOnlyList<DynamicResult> Dynamics { get; init; } = [];

    public IReadOnlyList<HairpinResult> Hairpins { get; init; } = [];

    /// <summary>Each stretch marked for the damper pedal and how it was pedalled; none when the music marks no pedal.</summary>
    public IReadOnlyList<PedalResult> Pedal { get; init; } = [];

    /// <summary>The bars where the damper pedal went down where none is marked (judged only when the music marks the pedal).</summary>
    public IReadOnlyList<int> ExtraPedal { get; init; } = [];

    /// <summary>Whether how hard the keys were struck varied: a piano set to Fixed touch sends one velocity, and loudness can't be judged.</summary>
    public bool TouchVaries { get; init; }

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
    /// <param name="markings">What the music asks besides the notes (dynamics, crescendos, the pedal), in the notes' ticks.</param>
    /// <param name="range">How hard you strike a key soft and loud (velocity), for where in it a dynamic was played (ours: 25 to 105).</param>
    /// <param name="rolled">Ids of the notes of chords marked to be rolled: timed by the roll's first key.</param>
    public static PassageResult Score(IReadOnlyList<ExpectedNote> expected, Func<double, long> timeAt, int beatTicks, double beatNs, double barNs, IReadOnlyList<PlayedNote> played, double offsetMs,
        IReadOnlySet<string>? ornamented = null, IReadOnlyList<(long DownNs, long UpNs)>? damper = null, Markings? markings = null, (int Soft, int Loud)? range = null,
        IReadOnlySet<string>? rolled = null)
    {
        var window = Grading.WindowNs(beatNs);
        var times = expected.Select(e => (double)timeAt(e.Tick / (double)beatTicks)).ToArray();
        var first = times.Length == 0 ? 0 : times.Min();
        var last = expected.Count == 0 ? 0 : expected.Select((e, i) => times[i] + e.Length / (double)beatTicks * beatNs).Max();
        // Notes played during the look don't count: scoring starts half a window before the first note.
        var notes = played.Select(p => (Note: p, At: p.OnNs - offsetMs * 1e6)).Where(p => p.At >= first - window / 2 && p.At <= last + window).OrderBy(p => p.At).ToList();

        // A chord marked to be rolled is played as a roll: its first key near its time, then each other key of it no more than a
        // roll's gap after the one before. Its keys count as struck together when the roll began, so it's timed by its start, as
        // Wait for me takes it.
        foreach (var chord in Enumerable.Range(0, expected.Count).Where(i => rolled?.Contains(expected[i].Id) == true).GroupBy(i => expected[i].Tick))
        {
            var (left, at) = (chord.Select(i => expected[i].Midi).ToHashSet(), times[chord.First()]);
            var roll = new List<int>();
            foreach (var k in Enumerable.Range(0, notes.Count).Where(k => notes[k].At >= at - window && left.Contains(notes[k].Note.Midi)))
            {
                if (roll.Count == 0 ? notes[k].At > at + window : notes[k].At - notes[roll[^1]].At > WaitJudge.RollGapNs) break;
                if (left.Remove(notes[k].Note.Midi)) roll.Add(k);
            }
            foreach (var k in roll.Skip(1)) notes[k] = (notes[k].Note, notes[roll[0]].At);
        }
        if (rolled is { Count: > 0 }) notes = [.. notes.OrderBy(p => p.At)];

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
        // How each right note sounded: its key, and until when: its key's release, or the damper pedal's if it was down then (unless
        // the key was struck again).
        var sounded = matched.ToDictionary(m => m.Key, m =>
        {
            var key = notes[m.Value].Note;
            var pedal = damper?.Where(d => d.DownNs <= key.OffNs && d.UpNs > key.OffNs).Select(d => d.UpNs).DefaultIfEmpty(key.OffNs).Max() ?? key.OffNs;
            var again = notes.Where(n => n.Note.Midi == key.Midi && n.Note.OnNs > key.OnNs).Select(n => n.Note.OnNs).DefaultIfEmpty(long.MaxValue).Min();
            return (Key: key, Until: Math.Min(pedal, Math.Max(key.OffNs, again)));
        });
        var results = expected.Select((e, i) =>
        {
            if (sounded.TryGetValue(i, out var heard))
            {
                var percent = (errors[i] - lean) / beatMs * 100;
                var key = heard.Key;
                var held = (double)heard.Until - key.OnNs;
                var writtenNs = e.Length / (double)beatTicks * beatNs;
                var whole = !key.HeldToEnd || held >= writtenNs;
                // Its hold, unless it's staccato, a slur's lifted end, an ornament, or a note a stop cut off.
                var judged = writtenNs > 0 && !e.Lifted && ornamented?.Contains(e.Id) != true && whole;
                // A staccato key down longer than about half the note (at most 0.45 s, at least 0.12 s); a tenuto short of nine tenths.
                Touch? touch = e.Articulation == Articulation.Staccato && !key.HeldToEnd && key.OffNs - key.OnNs > Math.Max(0.12e9, Math.Min(0.6 * writtenNs, 0.45e9)) ? Touch.StaccatoHeldLong
                    : e.Articulation == Articulation.Tenuto && whole && writtenNs - held > Math.Max(0.1 * writtenNs, beatNs / 8) ? Touch.TenutoShort
                    : null;
                return new NoteResult(e.Id, e.Midi, e.Bar, Outcome.Right, errors[i], percent, Grading.BandOf(percent),
                    HeldPercent: judged ? held / writtenNs * 100 : null, LetGoEarly: judged && Grading.LetGoEarly(held, writtenNs, beatNs), Velocity: key.Velocity, Touch: touch, Asked: e.Articulation);
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

        // How hard keys were struck says something only when it varies: a piano set to Fixed touch sends one velocity for every key.
        var velocities = sounded.Values.Select(v => v.Key.Velocity).ToList();
        var touchVaries = velocities.Count >= 2 && velocities.Max() - velocities.Min() > 2;
        markings ??= Markings.None;

        // An accent (or a sforzando) at least 8 harder than the median of the notes within a beat of it, on its own staff if it can.
        if (touchVaries)
        {
            bool Stressed(int i) => expected[i].Articulation == Articulation.Accent || markings.Stressed.Contains(expected[i].Id);
            foreach (var i in sounded.Keys.Where(Stressed).ToList())
            {
                var around = sounded.Keys.Where(j => j != i && !Stressed(j) && Math.Abs(expected[j].Tick - expected[i].Tick) <= beatTicks).ToList();
                var staff = around.Where(j => expected[j].Staff == expected[i].Staff).ToList();
                var pool = staff.Count >= 2 ? staff : around;
                if (pool.Count >= 2 && results[i].Touch is null && sounded[i].Key.Velocity < Stats.Median([.. pool.Select(j => (double)sounded[j].Key.Velocity)]) + 8) results[i] = results[i] with { Touch = Touch.AccentNotStronger };
            }
        }

        // Under a slur each note sounds until the next comes: let go more than about a tenth of a beat (60 ms at least) before it, the
        // line is broken. Staccato under a slur (portato) is detached by design.
        foreach (var slur in expected.Select((e, i) => (e, i)).Where(x => x.e.Slur is not null).GroupBy(x => x.e.Slur))
        {
            var steps = slur.GroupBy(x => x.e.Tick).OrderBy(g => g.Key).Select(g => g.Select(x => x.i).ToList()).ToList();
            foreach (var (now, next) in steps.Zip(steps.Skip(1)))
            {
                if (!now.Concat(next).All(sounded.ContainsKey) || now.Concat(next).Any(i => expected[i].Articulation == Articulation.Staccato)) continue;
                if (next.Min(i => sounded[i].Key.OnNs) - now.Max(i => sounded[i].Until) <= Math.Max(60e6, 0.12 * beatNs)) continue;

                foreach (var i in now.Where(i => results[i].Touch is null)) results[i] = results[i] with { Touch = Touch.GapUnderSlur };
            }
        }

        // Loudness: each dynamic's stretch at the median of its right notes, as a share of your soft-to-loud range, against the share
        // its dynamic is played in; and marked louder or softer than the stretch before it, played so by 5 or more.
        var (soft, loud) = range ?? (25, 105);
        var dynamics = new List<DynamicResult>();
        var hairpins = new List<HairpinResult>();
        if (touchVaries)
        {
            (int Rank, double Median)? before = null;
            foreach (var stretch in markings.Dynamics)
            {
                var inside = sounded.Keys.Where(i => expected[i].Tick >= stretch.From && expected[i].Tick < stretch.To).ToList();
                if (inside.Count < 3) continue;

                var median = Stats.Median([.. inside.Select(i => (double)sounded[i].Key.Velocity)]);
                var share = Math.Clamp((median - soft) / Math.Max(1, loud - soft) * 100, 0, 100);
                var (rank, low, high) = Markings.Levels[stretch.Level];
                var outcome = before is { } was && (rank > was.Rank && median < was.Median + 5 || rank < was.Rank && median > was.Median - 5) ? Loudness.NoChange
                    : share > high ? Loudness.TooLoud : share < low ? Loudness.TooSoft : Loudness.Right;
                dynamics.Add(new DynamicResult(stretch.Level, inside.Min(i => expected[i].Bar), inside.Max(i => expected[i].Bar), share, outcome));
                before = (rank, median);
            }

            // A crescendo or diminuendo: the line through how hard its right notes were struck rises (or falls) by 6 or more across it.
            foreach (var swell in markings.Hairpins)
            {
                var inside = sounded.Keys.Where(i => expected[i].Tick >= swell.From && expected[i].Tick <= swell.To).ToList();
                if (inside.Count < 3 || inside.Select(i => expected[i].Tick).Distinct().Count() < 2) continue;

                var (_, slope) = Stats.Line([.. inside.Select(i => (double)expected[i].Tick)], [.. inside.Select(i => (double)sounded[i].Key.Velocity)]);
                var change = slope * (swell.To - swell.From);
                var grew = swell.Crescendo ? change : -change;
                hairpins.Add(new HairpinResult(swell.Crescendo, inside.Min(i => expected[i].Bar), inside.Max(i => expected[i].Bar), change, grew >= 6 ? Swell.Right : grew <= -6 ? Swell.WrongWay : Swell.Flat));
            }
        }

        // The damper pedal, where it's marked: pressed by half a beat after its mark, down for most of the stretch, and up (or
        // changed) by a quarter of a beat after its release. Down for over half a beat where none is marked is noted by its bar.
        var pedalled = new List<PedalResult>();
        var extraPedal = new List<int>();
        if (markings.Pedal.Count > 0 && damper is not null)
        {
            var downs = damper.Select(d => (Down: d.DownNs - offsetMs * 1e6, Up: d.UpNs - offsetMs * 1e6)).Where(d => d.Up > first - window && d.Down < last + window).ToList();
            var marked = markings.Pedal.Select(p => (Down: (double)timeAt(p.Down / (double)beatTicks), Up: (double)timeAt(p.Up / (double)beatTicks))).Where(p => p.Down <= last - beatNs / 2).ToList();
            int BarAt(double ns) => Enumerable.Range(0, expected.Count).Where(i => times[i] <= ns).Select(i => expected[i]).MaxBy(e => e.Tick)?.Bar ?? expected.MinBy(e => e.Tick)!.Bar;
            foreach (var (down, up) in marked)
            {
                var pressed = downs.Where(d => d.Up > down - beatNs / 4 && d.Down < up).ToList();
                var (from, to) = (down + beatNs / 2, Math.Min(up, last) - beatNs / 4);
                var covered = to <= from ? 1 : pressed.Sum(d => Math.Max(0, Math.Min(d.Up, to) - Math.Max(d.Down, from))) / (to - from);
                var outcome = pressed.Count == 0 ? Pedalling.NotPressed
                    : up - down > beatNs && pressed.Min(d => Math.Max(d.Down, down - beatNs / 4)) > down + beatNs / 2 ? Pedalling.Late
                    : covered < 0.7 ? Pedalling.LiftedEarly
                    : up + beatNs / 4 <= last + window && downs.Any(d => d.Down < up - beatNs / 4 && d.Up > up + beatNs / 4) ? Pedalling.NotLifted
                    : Pedalling.Right;
                pedalled.Add(new PedalResult(BarAt(down), BarAt(Math.Max(down, up - 1)), outcome));
            }
            extraPedal.AddRange(downs.Where(d => d.Up - d.Down > beatNs / 2 && !marked.Any(m => d.Up > m.Down - beatNs / 4 && d.Down < m.Up + beatNs / 2)).Select(d => BarAt(d.Down)).Distinct());
        }

        return new PassageResult(results, extra, lean, lean / beatMs * 100, Stats.StandardDeviation(afterLean), results.Count(r => r.Outcome == Outcome.Right) / minutes, keptGoing, drift, Stats.RootMeanSquare([.. errors.Values]), perBar, chordSpreads.Count == 0 ? null : chordSpreads.Average())
        {
            Dynamics = dynamics,
            Hairpins = hairpins,
            Pedal = pedalled,
            ExtraPedal = extraPedal,
            TouchVaries = touchVaries,
        };
    }
}
