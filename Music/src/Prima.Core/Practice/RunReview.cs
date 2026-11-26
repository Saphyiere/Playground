using System.Globalization;
using Prima.Core.Generation;
using Prima.Core.Music;
using Prima.Core.Scoring;

namespace Prima.Core.Practice;

/// <summary>
/// What a run's review asks of the next run, in the order it's chosen: keeping going, then the notes (a sharp or flat forgotten,
/// one hand's, in one place, or all through), keeping with the click, what the music marks, evenness (a scale's thumb crossings
/// and new octaves first), the hands together, legato, and loudness. None: nothing to fix, so the next line is the step on.
/// </summary>
public enum ReviewFocus
{
    None,
    KeepGoing,
    Key,
    OneHand,
    Place,
    Notes,
    Ahead,
    Behind,
    Marked,
    Crossings,
    Octaves,
    Uneven,
    Together,
    Legato,
    Loudness,
}

/// <summary>
/// A run's review (concept, "A run's review"): what you did, in a line, and the one thing to work on next time, or the step on
/// when there's nothing to fix. A hand is named only when both played and one did what the other didn't; the figures it's drawn
/// from are the screen's details.
/// </summary>
public sealed record RunReview(string Did, string Next, ReviewFocus Focus)
{
    /// <summary>The bars its next line is about, for the screen's key to loop them; null when it's about the whole run.</summary>
    public (int From, int To)? Bars { get; init; }
}

/// <summary>Reviews of a run, from what its scorer measured: In time's passage result, a scale's or arpeggio's hands, Wait for me's.</summary>
public static class RunReviews
{
    /// <summary>Gaps even up to this spread, each gap's difference from the gap written in % of it: Prima's tightest evenness target.</summary>
    public const double EvenPercent = TechniqueState.FloorPercent;

    /// <summary>Uneven past this spread; between it and <see cref="EvenPercent"/>, a little uneven.</summary>
    public const double UnevenPercent = 12;

    /// <summary>Ahead of the click or behind it by the end, in % of a beat, before it's said: where a note starts to be heard apart from the click.</summary>
    public const double AheadPercent = Grading.AudiblePercent;

    /// <summary>A lean ahead of the click or behind it all the way, in % of a beat, before it's said.</summary>
    public const double LeanPercent = 12;

    /// <summary>Speeding up or slowing down within a scale, the gaps' change from first to last in % of a gap, before it's said.</summary>
    public const double SpeedChangePercent = 15;

    /// <summary>Legato up to this silence between a key coming up and the next going down, in % of the gap; detached past <see cref="DetachedPercent"/>.</summary>
    public const double LegatoPercent = 5;

    public const double DetachedPercent = 25;

    /// <summary>The hands together to within this on average, in ms, and to within <see cref="TogetherSdMs"/> in spread.</summary>
    public const double TogetherMs = 25;

    public const double TogetherSdMs = 30;

    /// <summary>
    /// A finger louder or softer than the hand's others by this much velocity, over four notes of it at least; a hand uneven in
    /// loudness past <see cref="LoudnessSd"/>.
    /// </summary>
    public const double FingerVelocity = 12;

    public const double LoudnessSd = 12;

    /// <summary>The next line when there's nothing to fix, unless the screen has its own step.</summary>
    public const string Faster = "A little faster.";

    // One thing to say: what it's about, whether it's worth the next run (a minor one is said only when there's nothing more), its
    // words in the line of what you did, and the next line.
    private sealed record Point(ReviewFocus Focus, bool Major, string Did, string Next)
    {
        public (int From, int To)? Bars { get; init; }

        /// <summary>About the timing (the click, the speed, evenness), so the run isn't called steady or even.</summary>
        public bool Timing { get; init; }

        /// <summary>About the notes, whose words lead the line of what you did.</summary>
        public bool Notes => Focus is ReviewFocus.Key or ReviewFocus.Place or ReviewFocus.Notes || Focus == ReviewFocus.OneHand && !Timing;

        /// <summary>A slip or two: where, after the count (", in bar 4", ", coming down"), where that's one place.</summary>
        public string Where { get; init; } = "";
    }

    // A wrong or missed note from either scorer, or an extra key: the key written (none for an extra) and the key played (none:
    // missed), its bar, its staff (a hand), and in a scale its place.
    private sealed record Slip(int? Written, int? Played, int Bar, int Staff, string? Place = null);

    /// <summary>
    /// An In time run's review, from its passage result. Round a loop its passes lead, and the rest is the last pass's; a run
    /// stopped early is of what was played.
    /// </summary>
    /// <param name="key">The music's key, to tell a sharp or flat of it forgotten; none where that's no help.</param>
    /// <param name="passes">Round a loop, each pass played to its end.</param>
    /// <param name="stoppedAt">Where a run stopped before its end was stopped, in words ("bar 5").</param>
    /// <param name="step">The next line when there's nothing to fix.</param>
    public static RunReview InTime(PassageResult result, Key? key = null, IReadOnlyList<PiecePass>? passes = null, string? stoppedAt = null, string? step = null)
    {
        if (result.Notes.Count(n => n.Outcome != Outcome.Missed) < 3 && (passes?.Count ?? 0) == 0)
            return new("Too little played to say much.", "Play it through.", ReviewFocus.KeepGoing);

        var both = result.Staves.Count >= 2;
        var upper = result.Staves.Count > 0 ? result.Staves[0].Staff : 1;
        string HandOf(int staff) => staff == upper ? "right" : "left";
        var points = new List<Point>();
        var strengths = new List<string>();
        if (!result.KeptGoing) points.Add(new(ReviewFocus.KeepGoing, true, "you stopped along the way", "Keep going through a slip: the click won't wait.") { Timing = true });

        // The notes: wrong ones (slips put right too, which count) and missed ones.
        List<Slip> slips = [.. result.Notes.Where(n => n.Outcome != Outcome.Right).Select(n => new Slip(n.Midi, n.Played, n.Bar, n.Staff))];
        Func<int, Spelled>? spell = key is { } k ? k.Spell : null;
        if (NotesPoint(slips, result.Expected, spell, both, HandOf, bars: true) is { } notes) points.Add(notes);

        // With the click: ahead or behind by the end, or leaning one way all through.
        var right = result.Right;
        if (result.AheadPercent >= AheadPercent) points.Add(new(ReviewFocus.Ahead, true, "you got ahead of the click as you went", "Stay with the click right to the end.") { Timing = true });
        else if (result.AheadPercent <= -AheadPercent) points.Add(new(ReviewFocus.Behind, true, "you fell behind the click as you went", "Keep up with the click; a little slower if you need.") { Timing = true });
        else if (right >= 4 && result.LeanPercent <= -LeanPercent) points.Add(new(ReviewFocus.Ahead, false, "a little ahead of the click all the way", "Land with the click, not before it.") { Timing = true });
        else if (right >= 4 && result.LeanPercent >= LeanPercent) points.Add(new(ReviewFocus.Behind, false, "a little behind the click all the way", "Land with the click, not after it.") { Timing = true });

        if (Marked(result) is { } marked) points.Add(marked);

        // Evenness: the gaps between notes against the gaps written, in the less even hand; or, where those say nothing, notes
        // well off the click.
        var uneven = Evenness([.. result.Staves.Where(s => s.GapSpreadPercent is not null).Select(s => (s.Staff, s.GapSpreadPercent!.Value))], EvenPercent, both, HandOf, null);
        if (uneven is not null) points.Add(uneven);
        else if (right > 0 && result.Off >= Math.Max(3, right * 0.15)) points.Add(new(ReviewFocus.Uneven, true, "some notes were well off the click", "Count the beats as you play; slower helps.") { Timing = true });

        // The hands together.
        var together = both ? Together(result.HandsApartMs, result.HandsApartSdMs) : null;
        if (together is not null) points.Add(together);
        else if (both && result.HandsApartMs is not null) strengths.Add("the hands together");

        // Legato: how joined the notes were where they're written to be, in the less joined hand; and long notes let go early.
        var joined = result.Staves.Where(s => s.SilencePercent is not null).Select(s => s.SilencePercent!.Value).DefaultIfEmpty(double.NaN).Max();
        var legato = Legato(joined);
        if (legato is not null) points.Add(legato);
        else if (!double.IsNaN(joined)) strengths.Insert(0, "legato");
        if (legato is null && result.HoldsJudged > 0 && result.LetGoEarly >= Math.Max(2, result.HoldsJudged * 0.2))
            points.Add(new(ReviewFocus.Legato, false, "long notes were let go early", "Hold each note its whole length."));

        // Loudness, where the touch varied and the music doesn't ask for loud and soft.
        if (result.TouchVaries && result.Dynamics.Count == 0 && result.Hairpins.Count == 0)
        {
            var loudness = result.Staves.Select(s => LoudnessPoint(s.VelocityByFinger, s.VelocitySd, both ? HandOf(s.Staff) : null)).FirstOrDefault(p => p is not null);
            if (loudness is not null) points.Add(loudness);
        }

        if (!points.Any(p => p.Timing)) strengths.Insert(0, "steady");
        var review = Compose(slips.Count, strengths, points, passes, step ?? Faster);
        return stoppedAt is null || (passes?.Count ?? 0) > 0 ? review : review with { Did = $"Up to {stoppedAt}: {Lower(review.Did)}" };
    }

    /// <summary>
    /// A scale's or arpeggio's run In time, from its hands' runs: the notes (a sharp or flat of the scale forgotten, and where in it
    /// they went wrong: going up, at the top, coming down, at the thumb crossings), keeping with the click and one speed, evenness
    /// (a bump at the thumb crossings or at each new octave first), the hands together and each alone, legato, and loudness.
    /// </summary>
    /// <param name="tempo">The click's beats a minute.</param>
    /// <param name="target">The evenness this item's runs are held to (its target), or none for Prima's tightest.</param>
    /// <param name="passes">Round a loop, each pass played to its end; the result is the last's.</param>
    /// <param name="step">The next line when there's nothing to fix.</param>
    public static RunReview Technique(TechniqueResult result, TechniqueItem item, double tempo, double? target = null, IReadOnlyList<PiecePass>? passes = null, string? step = null)
    {
        var hands = new[] { (Staff: 1, Run: result.Right, Written: item.Right), (Staff: 2, Run: result.Left, Written: item.Left) }
            .Where(h => h.Run is not null).Select(h => (h.Staff, Run: h.Run!, h.Written)).ToList();
        var both = hands.Count == 2;
        var points = new List<Point>();
        var strengths = new List<string>();

        // The notes, each slip with its place in the scale.
        var slips = new List<Slip>();
        foreach (var (staff, run, written) in hands)
        {
            var top = Top(written);
            string Place(int i) => i > 0 && written[i - 1].CrossingNext ? "at the thumb crossings" : Math.Abs(i - top) <= 1 ? "at the top" : i < top ? "going up" : "coming down";
            slips.AddRange(run.Notes.Select((n, i) => (n, i)).Where(x => x.n.Outcome != Outcome.Right).Select(x => new Slip(x.n.Midi, x.n.Played, 0, staff, Place(x.i))));
            slips.AddRange(run.Extras.Select(e => new Slip(null, e.Played, 0, staff)));
        }
        if (NotesPoint(slips, hands.Sum(h => h.Written.Count), item.Key.Spell, both, HandOf, bars: false) is { } notes) points.Add(notes);

        var valid = hands.Where(h => h.Run.Valid).ToList();
        if (valid.Count > 0)
        {
            // With the click: the run's own tempo against it, as how far ahead or behind it got by the last note; or within the run,
            // speeding up or slowing down.
            var bpm = valid.Average(h => h.Run.Bpm);
            var gap = 60_000 / (bpm * item.NotesPerBeat);
            var ahead = (bpm / tempo - 1) * (valid[0].Written.Count - 1) / item.NotesPerBeat * 100;
            var change = valid.Average(h => h.Run.SlopeMsPerNote) * (valid[0].Written.Count - 2) / gap * 100;
            if (ahead >= AheadPercent) points.Add(new(ReviewFocus.Ahead, true, "you got ahead of the click as you went", "Stay with the click right to the end.") { Timing = true });
            else if (ahead <= -AheadPercent) points.Add(new(ReviewFocus.Behind, true, "you fell behind the click as you went", "Keep up with the click; a little slower if you need.") { Timing = true });
            else if (change <= -SpeedChangePercent) points.Add(new(ReviewFocus.Ahead, true, "you sped up as you went", "Keep one speed all the way, with the click.") { Timing = true });
            else if (change >= SpeedChangePercent) points.Add(new(ReviewFocus.Behind, true, "you slowed down as you went", "Keep one speed all the way, with the click.") { Timing = true });

            // Evenness: a bump at the thumb crossings or at each new octave, standing out from the rest; else the less even hand,
            // and where in the scale.
            bool StandsOut(double ms, HandRun run) => Math.Abs(ms) >= Math.Max(TechniqueScorer.HearingMs, 1.5 * run.IoiSdMs);
            var crossings = valid.Where(h => h.Run.CrossingMs is { } ms && StandsOut(ms, h.Run)).ToList();
            var seams = valid.Where(h => h.Run.SeamMs is { } ms && StandsOut(ms, h.Run)).ToList();
            if (crossings.Count > 0)
            {
                var (staff, late) = (crossings[0].Staff, crossings[0].Run.CrossingMs > 0);
                points.Add(late
                    ? new(ReviewFocus.Crossings, true, $"a bump at each thumb crossing{In(staff)}", "Get the thumb under early, so it's ready.") { Timing = true }
                    : new(ReviewFocus.Crossings, true, $"a rush at each thumb crossing{In(staff)}", "Give the thumb crossings their full time.") { Timing = true });
            }
            else if (seams.Count > 0) points.Add(new(ReviewFocus.Octaves, true, $"a hitch at each new octave{In(seams[0].Staff)}", "Carry straight on into each new octave.") { Timing = true });
            else if (Evenness([.. valid.Select(v => (v.Staff, v.Run.IoiSdPercent))], Math.Max(EvenPercent, target ?? 0), both, HandOf, Where) is { } uneven) points.Add(uneven);

            // Legato: each key's overlap with the next, as silence in % of the gap, in the less joined hand.
            var legato = Legato(valid.Max(h => -h.Run.LegatoMs / gap * 100));
            if (legato is not null) points.Add(legato);
            else strengths.Add("legato");

            // Loudness: the fingers on four notes or more, as an exercise's are.
            var loudness = valid.Select(h => LoudnessPoint(h.Run.VelocityByFinger.Where(f => h.Written.Count(n => n.Finger == f.Key) >= 4).ToDictionary(), h.Run.VelocitySd > 0 ? h.Run.VelocitySd : null, both ? HandOf(h.Staff) : null))
                .FirstOrDefault(p => p is not null);
            if (loudness is not null) points.Add(loudness);
        }
        var together = both ? Together(result.SyncMeanMs, result.SyncSdMs) : null;
        if (together is not null) points.Add(together);
        else if (both && result.SyncMeanMs is not null) strengths.Add("the hands together");

        if (valid.Count > 0 && !points.Any(p => p.Timing)) strengths.Insert(0, "even");
        return Compose(slips.Count, strengths, points, passes, step ?? Faster);

        static string HandOf(int staff) => staff == 1 ? "right" : "left";

        string In(int staff) => both ? $" in the {HandOf(staff)} hand" : "";

        // Where in the scale a hand's gaps were less even, where one half is much less even than the other.
        string? Where(int staff)
        {
            var hand = valid.First(v => v.Staff == staff);
            var top = Top(hand.Written);
            var deviations = hand.Run.Deviations;
            if (deviations.Count < 6 || top < 3 || top > deviations.Count - 3) return null;

            var (up, down) = (Stats.StandardDeviation([.. deviations.Take(top)]), Stats.StandardDeviation([.. deviations.Skip(top)]));
            return down >= 1.6 * up ? "coming down" : up >= 1.6 * down ? "going up" : null;
        }
    }

    /// <summary>
    /// A Wait for me run's review: the notes right first time, the chords played a key at a time, and where the slips were; round
    /// a loop, its passes first.
    /// </summary>
    /// <param name="trouble">What went wrong in each bar, from the marks the run left.</param>
    public static RunReview Waited(WaitResult wait, IReadOnlyList<BarTrouble> trouble, IReadOnlyList<PiecePass>? passes = null, string? step = null)
    {
        var apart = wait.Apart;
        if (wait.Notes == 0) return new("Nothing played yet.", "Play it through.", ReviewFocus.KeepGoing);

        var points = new List<Point>();
        var slips = wait.Notes - wait.RightFirstTime;
        if (slips > 0)
        {
            List<int> bars = [.. trouble.SelectMany(t => Enumerable.Repeat(t.Bar, t.Wrong + t.Missed))];
            var stretch = slips >= 3 ? Stretch(bars) : null;
            // A slip or two, with where.
            var where = slips <= 2 && bars.Count > 0 ? $", in {BarWords.Runs(bars)}" : "";
            points.Add(stretch is { } place && place.Count >= bars.Count * 0.6
                ? new Point(ReviewFocus.Place, true, $"most of the slips were in {BarWords.Bars(place.From, place.To)}", Slowly(place.From, place.To)) { Bars = (place.From, place.To) }
                : new Point(ReviewFocus.Notes, slips >= 3, $"{(slips == 1 ? "one note wasn't" : $"{Number(slips)} notes weren't")} right first time{where}", "Once more in Wait for me, until every note's right first time."));
        }
        if (apart > 0) points.Add(new(ReviewFocus.Together, true, apart == 1 ? "a chord was played a key at a time" : $"{Number(apart)} chords were played a key at a time", "Put each chord's keys down together."));

        var point = points.Where(p => p.Major).MinBy(p => p.Focus) ?? points.FirstOrDefault();
        var lead = (passes?.Count ?? 0) > 1 ? Passes(passes!) : null;
        if (point is null) return new($"{lead ?? "Every note right first time, at your own pace"}.", step ?? "In time, with the click.", ReviewFocus.None);
        return new RunReview($"{(lead is null ? Capital(point.Did) : $"{lead}: {point.Did}")}.", point.Next, point.Focus) { Bars = point.Bars };
    }

    /// <summary>A scale's step on when there's nothing to fix: RCM's tempo for its level reached, or towards it; or a little faster.</summary>
    public static string StepOn(double ranAt, double? rcmTempo, string? rcmLevel) => rcmTempo is { } rcm
        ? ranAt >= rcm ? $"That's RCM {rcmLevel}'s tempo: keep it there, and on to the next key." : $"A little faster, towards RCM {rcmLevel}'s tempo."
        : Faster;

    // The notes' point: a sharp or flat forgotten, one hand's, in one place, or all through, each by how most went wrong. A slip or
    // two is a minor one, which the line of what you did counts.
    private static Point? NotesPoint(IReadOnlyList<Slip> slips, int notes, Func<int, Spelled>? spell, bool both, Func<int, string> handOf, bool bars)
    {
        var n = slips.Count;
        if (n == 0) return null;

        // A sharp or flat played as its plain letter.
        if (spell is not null)
        {
            List<Spelled> forgot = [.. slips.Where(s => s is { Written: { } w, Played: { } p } && spell(w) is { Alter: not 0 } written && p == w - written.Alter).Select(s => spell(s.Written!.Value))];
            if (forgot.Count >= Math.Max(1, (n + 1) / 2))
            {
                List<string> named = [.. forgot.Select(Name).Distinct()];
                List<string> plain = [.. forgot.Select(w => w.Step.ToString()).Distinct()];
                return new(ReviewFocus.Key, true, $"{BarWords.And(plain)} instead of {BarWords.And(named)}{Times(forgot.Count)}", $"Think of the {BarWords.And(named)} before you start.");
            }
        }
        if (n <= 2)
        {
            // Where, when that's one place: its bars, or its place in the scale.
            List<string> places = [.. slips.Where(s => s.Written is not null).Select(s => bars ? BarWords.Runs([s.Bar]) : s.Place ?? "").Distinct()];
            var where = bars && slips.All(s => s.Written is not null) ? $", in {BarWords.Runs([.. slips.Select(s => s.Bar)])}"
                : !bars && places is [{ Length: > 0 } one] && slips.All(s => s.Written is not null) ? $", {one}"
                : "";
            return new(ReviewFocus.Notes, false, "", "Once more at this tempo, for every note.") { Where = where };
        }

        // One hand's, where both played.
        if (both && slips.GroupBy(s => s.Staff).MaxBy(g => g.Count()) is { } most && most.Count() >= n * 0.8)
        {
            var hand = handOf(most.Key);
            return new(ReviewFocus.OneHand, true, $"{Number(n)} slips, {(most.Count() == n ? "all" : "most")} in the {hand} hand", $"The {hand} hand alone for a run or two, then together.");
        }

        // In one place: two bars in a row with most of them; in a scale, going up, at the top, coming down, at the thumb crossings.
        if (bars && Stretch([.. slips.Where(s => s.Written is not null).Select(s => s.Bar)]) is { } stretch && stretch.Count >= n * 0.6)
        {
            var where = BarWords.Bars(stretch.From, stretch.To);
            return new(ReviewFocus.Place, true, $"most of the slips were in {where}", Slowly(stretch.From, stretch.To)) { Bars = (stretch.From, stretch.To) };
        }
        if (!bars && slips.Where(s => s.Place is not null).GroupBy(s => s.Place!).MaxBy(g => g.Count()) is { } place && place.Count() >= n * 0.6)
        {
            return new(ReviewFocus.Place, true, $"most of the slips were {place.Key}", place.Key switch
            {
                "at the thumb crossings" => "Get the thumb under early, so it's ready.",
                "at the top" => "The turn at the top slowly a few times, then the whole.",
                "going up" => "The way up slowly on its own, then the whole.",
                _ => "The way down slowly on its own, then the whole.",
            });
        }

        // All through: many, or mostly a key or two away, or mostly left out.
        if (n > notes * 0.15) return new(ReviewFocus.Notes, true, $"{Number(n)} wrong notes", "Wait for me first, to learn the notes; then In time, slower.");
        if (slips.Count(s => s is { Written: { } w, Played: { } p } && Math.Abs(p - w) <= 2) >= n * 0.6) return new(ReviewFocus.Notes, true, $"{Number(n)} notes landed a key off", "A little slower, so each finger finds its own key.");
        if (slips.Count(s => s is { Written: not null, Played: null }) >= n * 0.6) return new(ReviewFocus.Notes, true, $"{Number(n)} notes were left out", "Slower, so every note gets played.");
        return new(ReviewFocus.Notes, true, $"{Number(n)} wrong notes", "A little slower, for every note right.");
    }

    // The bars of a run's slips: the two bars in a row with the most of them, and how many; one bar where the next has none.
    private static (int From, int To, int Count)? Stretch(IReadOnlyList<int> bars)
    {
        if (bars.Count == 0) return null;

        var best = bars.Distinct().Order().Select(b => (From: b, To: b + 1, Count: bars.Count(x => x == b || x == b + 1))).MaxBy(s => s.Count);
        return bars.Contains(best.To) ? best : (best.From, best.From, best.Count);
    }

    // The first of what the music marks (loud and soft, the touch, the pedal) that wasn't played as asked.
    private static Point? Marked(PassageResult result)
    {
        if (result.Dynamics.FirstOrDefault(d => d.Outcome is Scoring.Loudness.TooLoud or Scoring.Loudness.TooSoft) is { } far)
        {
            return far.Outcome == Scoring.Loudness.TooLoud
                ? new(ReviewFocus.Marked, true, $"too loud at the {far.Level}", $"Softer at the {far.Level}.")
                : new(ReviewFocus.Marked, true, $"too soft at the {far.Level}", $"Louder at the {far.Level}.");
        }
        if (result.Dynamics.FirstOrDefault(d => d.Outcome == Scoring.Loudness.NoChange) is { } same) return new(ReviewFocus.Marked, true, $"no change at the {same.Level}", $"Let the {same.Level} change the sound.");
        if (result.Hairpins.FirstOrDefault(h => h.Outcome != Swell.Right) is { } swell)
        {
            var (what, does) = swell.Crescendo ? ("crescendo", "grow") : ("diminuendo", "fade");
            return swell.Outcome == Swell.Flat
                ? new(ReviewFocus.Marked, true, $"the {what} didn't {does}", $"Let the {what} {does} a little with each note.")
                : new(ReviewFocus.Marked, true, $"the {what} went the other way", $"{Capital(does)} through the {what}.");
        }
        if (result.Notes.FirstOrDefault(n => n.Touch is not null)?.Touch is { } touch)
        {
            return touch switch
            {
                Touch.StaccatoHeldLong => new(ReviewFocus.Marked, true, "the staccato notes were held long", "Staccato short and light: let each key spring up."),
                Touch.TenutoShort => new(ReviewFocus.Marked, true, "the tenuto notes were cut short", "Hold each tenuto note its whole length."),
                Touch.GapUnderSlur => new(ReviewFocus.Marked, true, "gaps under the slurs", "Join the notes under each slur."),
                _ => new(ReviewFocus.Marked, true, "the accents were no stronger than the rest", "Lean into each accented note."),
            };
        }
        if (result.Pedal.FirstOrDefault(p => p.Outcome != Pedalling.Right) is { } pedal)
        {
            return pedal.Outcome switch
            {
                Pedalling.NotPressed => new(ReviewFocus.Marked, true, "no pedal where it's marked", "Press the pedal where it's marked."),
                Pedalling.Late => new(ReviewFocus.Marked, true, "the pedal came late", "Press the pedal as the chord sounds."),
                Pedalling.LiftedEarly => new(ReviewFocus.Marked, true, "the pedal came up early", "Hold the pedal to the end of its mark."),
                _ => new(ReviewFocus.Marked, true, "the pedal wasn't lifted", "Lift the pedal where it's marked, to clear the sound."),
            };
        }
        return result.ExtraPedal.Count > 0 ? new(ReviewFocus.Marked, true, "you pedalled where none is marked", "Keep the pedal off where none is marked.") : null;
    }

    // Evenness, from each hand's spread of gaps: one hand uneven where the other's even, or the less even hand, and where.
    private static Point? Evenness(IReadOnlyList<(int Staff, double Spread)> hands, double even, bool both, Func<int, string> handOf, Func<int, string?>? where)
    {
        if (hands.Count == 0) return null;

        var (staff, spread) = hands.MaxBy(h => h.Spread);
        var uneven = Math.Max(UnevenPercent, even * 1.5);
        if (spread <= even) return null;
        if (both && hands.Count == 2 && spread > uneven && hands.Min(h => h.Spread) <= even)
        {
            var (hand, other) = (handOf(staff), handOf(hands.First(h => h.Staff != staff).Staff));
            return new(ReviewFocus.OneHand, true, $"the {hand} hand was less even than the {other}", $"The {hand} hand alone for a run or two, for even gaps.") { Timing = true };
        }
        var place = where?.Invoke(staff);
        var at = place is null ? "" : $" {place}";
        return spread > uneven
            ? new(ReviewFocus.Uneven, true, $"the gaps between them were uneven{at}", place is null ? "Listen for even gaps; slower helps." : $"The way {(place == "coming down" ? "down" : "up")} slowly, listening for even gaps.") { Timing = true }
            : new(ReviewFocus.Uneven, false, $"a little uneven{at}", "Listen for even gaps between the notes.") { Timing = true };
    }

    // The hands together: one a little after the other on average (the upper's after the lower's, ms), or not together each time.
    private static Point? Together(double? meanMs, double? sdMs) => meanMs switch
    {
        >= TogetherMs => new(ReviewFocus.Together, true, "the right hand came in a little after the left", "Land the hands exactly together."),
        <= -TogetherMs => new(ReviewFocus.Together, true, "the left hand came in a little after the right", "Land the hands exactly together."),
        not null when sdMs >= TogetherSdMs => new(ReviewFocus.Together, true, "the hands weren't quite together", "Land the hands exactly together; slower helps."),
        _ => null,
    };

    // Legato, from the silence between keys in % of the gap: detached, a little short of legato, or legato (none said).
    private static Point? Legato(double silence) => double.IsNaN(silence) ? null
        : silence > DetachedPercent ? new(ReviewFocus.Legato, true, "detached: each key came up well before the next", "Hold each note until the next one sounds.")
        : silence > LegatoPercent ? new(ReviewFocus.Legato, false, "a little short of legato", "Let each key up only as the next goes down.")
        : null;

    // Loudness: a finger louder or softer than the hand's others, or the hand's notes uneven in loudness.
    private static Point? LoudnessPoint(IReadOnlyDictionary<int, double> byFinger, double? spread, string? hand)
    {
        if (byFinger.Count >= 3)
        {
            var (finger, by) = byFinger.Select(f => (f.Key, By: f.Value - byFinger.Where(o => o.Key != f.Key).Average(o => o.Value))).MaxBy(f => Math.Abs(f.By));
            if (Math.Abs(by) >= FingerVelocity)
            {
                var name = hand is null ? $"the {Finger(finger)}" : $"the {hand} hand's {Finger(finger)}";
                return by > 0
                    ? new(ReviewFocus.Loudness, false, $"{name} was louder than the other fingers", $"Keep {name} light.")
                    : new(ReviewFocus.Loudness, false, $"{name} was softer than the other fingers", $"Give {name} a little more weight.");
            }
        }
        return spread >= LoudnessSd ? new(ReviewFocus.Loudness, false, "the notes were uneven in loudness", "Every note at one loudness: listen for it.") : null;
    }

    // The two lines from what there is to say: the major point first in the review's order, else a minor one, else the step on.
    // The line of what you did leads with the notes (all right, a slip or two, or what went wrong with them), then what was good,
    // or the point; round a loop, with the passes.
    private static RunReview Compose(int slips, List<string> strengths, List<Point> points, IReadOnlyList<PiecePass>? passes, string step)
    {
        var point = points.Where(p => p.Major).MinBy(p => p.Focus) ?? points.Where(p => !p.Major).MinBy(p => p.Focus);
        var good = BarWords.And(strengths);
        string did;
        if ((passes?.Count ?? 0) > 1)
        {
            // Round a loop, the passes lead; the rest is the last pass's.
            var lead = Passes(passes!);
            did = point is null || point.Did.Length == 0 ? $"{lead}."
                : point.Notes ? $"{lead}: {point.Did}."
                : $"{lead}, {(point.Major ? "and" : "but")} {point.Did}.";
        }
        else if (point is { Notes: true })
        {
            // What went wrong with the notes (a slip or two counted, and where, when that's one place), and what was good.
            var timing = strengths.FirstOrDefault(s => s is "steady" or "even");
            did = point.Did.Length > 0 ? $"{Capital(point.Did)}{(timing is null ? "" : $"; {timing} otherwise")}."
                : $"{Capital(Number(slips))} slip{(slips == 1 ? "" : "s")}{point.Where}{(good.Length == 0 ? "" : point.Where.Length > 0 ? $"; {good} otherwise" : $", {good}")}.";
        }
        else
        {
            // Every note right (or a slip or two, said with what's to fix), what was good, and what wasn't.
            var notes = slips == 0 ? "Every note right" : $"{Capital(Number(slips))} slip{(slips == 1 ? "" : "s")}";
            did = point is null ? $"{notes}{(good.Length == 0 ? "" : $", {good}")}."
                : point.Major ? $"{notes}{(slips == 0 ? ", but " : ", and ")}{point.Did}."
                : $"{notes}{(good.Length == 0 ? "" : $", {good}")}{(slips == 0 ? ", but " : ", and ")}{point.Did}.";
        }
        return new RunReview(did, point?.Next ?? step, point?.Focus ?? ReviewFocus.None) { Bars = point?.Bars };
    }

    // A loop's passes: how many, and how clean they were, or how they went.
    private static string Passes(IReadOnlyList<PiecePass> passes)
    {
        static bool Clean(PiecePass p) => p.RightInTime is { } r ? r >= 0.995 : p.Right == p.Notes;
        static double Share(PiecePass p) => p.RightInTime ?? (p.Notes == 0 ? 0 : p.Right / (double)p.Notes);
        var n = passes.Count;
        var times = n == 2 ? "Twice round" : $"{Capital(Number(n))} times round";
        var clean = passes.Count(Clean);
        if (clean == n) return $"{times}, every one clean";
        if (n >= 3 && clean == n - 1) return $"{times}, every one clean but the {Ordinal(passes.ToList().FindIndex(p => !Clean(p)) + 1)}";
        if (Clean(passes[^1]) && !Clean(passes[0])) return $"{times}, clean by the last";
        if (Clean(passes[0]) && !Clean(passes[^1])) return $"{times}, slipping by the last";
        if (Share(passes[^1]) >= Share(passes[0]) + 0.1) return passes.Zip(passes.Skip(1)).All(p => Share(p.Second) >= Share(p.First)) ? $"{times}, better each time" : $"{times}, better by the last";
        if (Share(passes[^1]) <= Share(passes[0]) - 0.1) return $"{times}, slipping as you went";
        return clean == 0 ? $"{times}, with slips each time" : $"{times}, {Number(clean)} of them clean";
    }

    // A bar or two to practise by themselves: "Bars 9–10 slowly on their own, then the whole."
    private static string Slowly(int from, int to) => $"{Capital(BarWords.Bars(from, to))} slowly on {(from == to ? "its" : "their")} own, then the whole.";

    // The highest note's place in a hand's run: where a scale or arpeggio turns.
    private static int Top(IReadOnlyList<TechniqueNote> written) => written.Count == 0 ? 0 : written.Select((n, i) => (n.Midi, i)).MaxBy(x => x.Midi).i;

    private static string Name(Spelled pitch) => $"{pitch.Step}{pitch.Alter switch { 2 => "𝄪", 1 => "♯", -1 => "♭", -2 => "𝄫", _ => "" }}";

    private static string Finger(int finger) => finger switch { 1 => "thumb", 2 => "second finger", 3 => "third finger", 4 => "fourth finger", _ => "little finger" };

    private static string Times(int n) => n switch { 1 => "", 2 => " twice", _ => $" {Number(n)} times" };

    private static string Number(int n) => n switch
    {
        1 => "one", 2 => "two", 3 => "three", 4 => "four", 5 => "five", 6 => "six", 7 => "seven", 8 => "eight", 9 => "nine", 10 => "ten", 11 => "eleven", 12 => "twelve",
        _ => n.ToString(CultureInfo.InvariantCulture),
    };

    private static string Ordinal(int n) => n switch
    {
        1 => "first", 2 => "second", 3 => "third", 4 => "fourth", 5 => "fifth", 6 => "sixth", 7 => "seventh", 8 => "eighth", 9 => "ninth", 10 => "tenth",
        _ => $"{n}th",
    };

    private static string Capital(string words) => words.Length == 0 ? words : char.ToUpperInvariant(words[0]) + words[1..];

    private static string Lower(string words) => words.Length == 0 ? words : char.ToLowerInvariant(words[0]) + words[1..];
}
