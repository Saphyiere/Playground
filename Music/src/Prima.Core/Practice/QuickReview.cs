using Prima.Core.Timing;

namespace Prima.Core.Practice;

/// <summary>
/// How a part of a quick review's sentence is shown: as it is, as the place it's about, or as a count of one of the two kinds of
/// mistake in that kind's colour, so the words point at the chart under them.
/// </summary>
public enum Said
{
    Plain,
    Place,
    Notes,
    Beat,
}

/// <summary>A part of a quick review's sentence and how it's shown.</summary>
public sealed record SentencePart(string Text, Said As);

/// <summary>A quick review's sentence, in parts.</summary>
public sealed record Sentence(IReadOnlyList<SentencePart> Parts)
{
    public override string ToString() => string.Concat(Parts.Select(p => p.Text));
}

/// <summary>
/// A bar's mistakes on a run (both times through a repeat together) or on a time round a loop, in the two kinds the quick review
/// shows: notes not right (wrong, missed, or a wrong key put right at once; in Wait for me, a chord a key at a time too) and, In
/// time, right notes off the beat, with how many of those were ahead of it.
/// </summary>
public sealed record BarCount(int Bar, int Notes, int Beat, int Ahead)
{
    public int Both => Notes + Beat;

    public static BarCount None(int bar) => new(bar, 0, 0, 0);

    public static BarCount Of(BarTrouble trouble, bool inTime) => new(trouble.Bar, trouble.NotRight, inTime ? trouble.Off : 0, inTime ? trouble.Ahead : 0);
}

/// <summary>What a run came to, which decides its sentence and its first key.</summary>
public enum RunFinding
{
    /// <summary>Every note right and, In time, on the beat.</summary>
    Clean,
    /// <summary>One place, four bars at most, with half the mistakes or more.</summary>
    Place,
    /// <summary>A longer stretch, six bars or more, with three quarters of them.</summary>
    Stretch,
    /// <summary>Mistakes in more than half the bars played, and no one place.</summary>
    AllThrough,
    /// <summary>A slip here and there, no one place.</summary>
    Here,
    /// <summary>Stopped on the way.</summary>
    Stopped,
}

/// <summary>
/// A run's quick review: what it came to and the bars that names; each bar's mistakes from <see cref="FirstBar"/> on, none where
/// the run didn't play the bar (before it started, or from the bar it stopped at); its totals; the sentence; and its first key, a
/// loop of the bars named or the run again at a tempo.
/// </summary>
public sealed record QuickRun(RunFinding Finding, int From, int To, int FirstBar, IReadOnlyList<BarCount?> Bars, int Notes, int Beat, Sentence Said)
{
    /// <summary>The bars to loop, when the first key loops them.</summary>
    public (int From, int To)? Loop { get; init; }

    /// <summary>The tempo to go again at, when the first key goes again: a notch faster after a clean run, two slower after one wrong all through.</summary>
    public double? Again { get; init; }

    /// <summary>The bars it names, to pick out on the chart.</summary>
    public IReadOnlyList<int> Named => Finding is RunFinding.Clean or RunFinding.AllThrough or RunFinding.Here ? [] : [.. Enumerable.Range(From, To - From + 1)];
}

/// <summary>
/// A loop's quick review, by time round: its bars and tempo, each time round's mistakes of each kind, each bar's mistakes each time
/// round (<see cref="Grid"/>[bar][round], both kinds), how it went, the sentence and the keys.
/// </summary>
public sealed record QuickLoop(int From, int To, double? Tempo, IReadOnlyList<int> Notes, IReadOnlyList<int> Beat, IReadOnlyList<IReadOnlyList<int>> Grid, LoopTrend Trend, Sentence Said)
{
    public int Rounds => Notes.Count;

    /// <summary>Each time round's mistakes, both kinds together: the number over its column.</summary>
    public IReadOnlyList<int> Totals => [.. Notes.Zip(Beat, (n, b) => n + b)];

    /// <summary>The bars that still went wrong at the end, to loop on their own.</summary>
    public (int From, int To)? Narrow { get; init; }

    /// <summary>The tempo to go again at: the same while it's getting better, a notch slower when it isn't, a notch faster once it's clean.</summary>
    public double? Again { get; init; }

    /// <summary>The bars still going wrong at the end, to pick out on the chart.</summary>
    public IReadOnlyList<int> Still { get; init; } = [];
}

public enum LoopTrend
{
    /// <summary>Played round once: nothing to set it against.</summary>
    Once,
    Better,
    NoBetter,
    Worse,
}

/// <summary>A place a session's review names, and what it needs: "wrong notes", "timing", or "wrong notes and timing".</summary>
public sealed record SessionPlace(int From, int To, string Needs)
{
    /// <summary>Which of the heat map's two kinds shows it best: the beat's for timing alone, the notes' otherwise.</summary>
    public bool Timing => Needs == "timing";
}

/// <summary>A run through the piece (not round a loop) as a row of a session's heat map: its number among them, its tempo and hand, and its bars.</summary>
public sealed record HeatRow(int Number, bool InTime, double? Tempo, string Hands, IReadOnlyList<BarCount?> Bars);

/// <summary>A group on a session's timeline: a run, or a loop's times round, named by what was practised ("Whole", "17–end", "21–24 LH").</summary>
public sealed record TimeGroup(string Label, bool Loop, IReadOnlyList<double> Figures);

/// <summary>
/// A session's quick review: how it went (the head), the places that still need help and the stretch that came right, both judged
/// on the heat map's rows (the last two runs through, the first two), the sentence and the line under it, the heat map's rows
/// (every run through, in order, from the piece's first bar), the timeline's groups (In time only: correct and on time), how many
/// runs and loops, which kind the heat map opens on, and Next with its loop and tempo.
/// </summary>
public sealed record QuickSession(string Head, IReadOnlyList<SessionPlace> Places, (int From, int To)? CameRight, Sentence Said, string? Sub,
    int FirstBar, IReadOnlyList<HeatRow> Rows, IReadOnlyList<TimeGroup> Groups, int Runs, int Loops, bool UntimedLeftOut, Sentence Next)
{
    /// <summary>The heat map opens on the kind the first place needs: the beat's for timing alone, the notes' otherwise.</summary>
    public bool OpensOnTiming => Places.FirstOrDefault()?.Timing == true;

    /// <summary>Next's loop, when it names a place.</summary>
    public (int From, int To)? NextLoop { get; init; }

    /// <summary>Next's tempo: a notch under the one you ended at with a place to loop, a notch over without.</summary>
    public double? NextTempo { get; init; }

    /// <summary>The bars it names, to pick out on the heat map.</summary>
    public IReadOnlyList<int> Named => [.. Places.SelectMany(p => Enumerable.Range(p.From, p.To - p.From + 1))];

    /// <summary>
    /// Each spot Lock in went round, and each loop counted to its clean times round in a row (your own with Lock in, Practice it),
    /// in the order played, in words: "Bars 8–10: 7 clean in a row at ♩ 72, after 3 that went wrong."
    /// </summary>
    public IReadOnlyList<Sentence> Locks { get; init; } = [];
}

/// <summary>
/// The quick review after practice: after a run, round a loop, and after a session. It says where the mistakes were, of which
/// kind, and what to play next, with every number it says on the chart it's drawn over. A mistake is a note not right (wrong,
/// missed, or put right after a wrong key) or, In time, a right note off the beat; notes let go early, touch and pedal stay in the
/// details. Loudness against the dynamics isn't judged: MIDI can't say how loud a marking is meant to be.
/// </summary>
public static class QuickReview
{
    /// <summary>The share of the worst four bars' mistakes the fewest bars need to hold to be the place: 85%.</summary>
    public const double PlaceHolds = 0.85;

    /// <summary>
    /// A run's quick review: its trouble by bar, In time or not, the piece's first and last bars, the bars it played (to the one
    /// before the bar it stopped at, if it was stopped) and its tempo.
    /// </summary>
    public static QuickRun Run(IReadOnlyList<BarTrouble> trouble, bool inTime, int firstBar, int lastBar, int from, int to, bool stopped, double? tempo)
    {
        (from, to) = (Math.Clamp(from, firstBar, lastBar), Math.Clamp(Math.Max(from, to), firstBar, lastBar));
        var byBar = trouble.ToDictionary(t => t.Bar);
        List<BarCount?> bars = [.. Enumerable.Range(firstBar, lastBar - firstBar + 1)
            .Select(bar => bar < from || bar > to ? null : byBar.TryGetValue(bar, out var t) ? BarCount.Of(t, inTime) : BarCount.None(bar))];
        var played = bars.OfType<BarCount>().ToList();
        var (notes, beat) = (played.Sum(b => b.Notes), played.Sum(b => b.Beat));
        var total = notes + beat;
        int In(int a, int z) => played.Where(b => b.Bar >= a && b.Bar <= z).Sum(b => b.Both);

        // The place: the fewest bars, four at most, that hold nearly all of the worst four bars' mistakes, trimmed of clean ends.
        (int From, int To, int Sum) Best(int length)
        {
            var best = (From: from, To: Math.Min(to, from + length - 1), Sum: -1);
            for (var a = from; a + length - 1 <= to; a++)
            {
                var sum = In(a, a + length - 1);
                if (sum > best.Sum) best = (a, a + length - 1, sum);
            }
            return best;
        }
        var four = Best(Math.Min(4, to - from + 1));
        var place = Enumerable.Range(1, Math.Min(4, to - from + 1)).Select(Best).First(x => x.Sum >= PlaceHolds * four.Sum);
        while (place.From < place.To && In(place.From, place.From) == 0) place = (place.From + 1, place.To, place.Sum);
        while (place.To > place.From && In(place.To, place.To) == 0) place = (place.From, place.To - 1, place.Sum);
        var inPlace = In(place.From, place.To);

        // A stretch: the shortest run of six bars or more holding three quarters of the mistakes (and less than three quarters of
        // the bars played), grown through the bars either side that went wrong too, and named as a half of the run when it is one.
        (int From, int To)? stretch = null;
        if (total >= 8)
        {
            for (var length = 6; length <= played.Count && stretch is null; length++)
            {
                if (length >= played.Count * 0.75) break;
                for (var a = from; a + length - 1 <= to; a++)
                {
                    if (In(a, a + length - 1) >= 0.75 * total) { stretch = (a, a + length - 1); break; }
                }
            }
            if (stretch is { } s)
            {
                var (a, z) = s;
                while (a > from && In(a - 1, a - 1) > 0) a--;
                while (z < to && In(z + 1, z + 1) > 0) z++;
                var middle = from + (to - from + 1) / 2;
                if (z >= to - 1 && Math.Abs(a - middle) <= 2) (a, z) = (middle, to);
                else if (a <= from + 1 && Math.Abs(z - (middle - 1)) <= 2) (a, z) = (from, middle - 1);
                stretch = (a, z);
            }
        }

        var hit = played.Count(b => b.Both > 0);
        var w = new Words();
        var pace = tempo is { } tt ? Math.Round(tt) : (double?)null;
        if (stopped)
        {
            // Stopped at a bar: what went wrong in the two bars before it, and before that.
            var at = to + 1;
            var a = Math.Max(from, at - 2);
            var (n2, b2) = (played.Where(b => b.Bar >= a).Sum(b => b.Notes), played.Where(b => b.Bar >= a).Sum(b => b.Beat));
            w.Place($"You stopped at {BarWords.Bar(at)}");
            if (n2 + b2 >= 3) w.Plain(", after ").Kinds(n2, b2).Plain($" in {BarWords.Bars(a, at - 1)}");
            var before = total - (n2 + b2 >= 3 ? n2 + b2 : 0);
            w.Plain($". Before that, {(before == 0 ? "clean" : BarWords.Count(before, "mistake"))}.");
            return new(RunFinding.Stopped, a, at, firstBar, bars, notes, beat, w.Done()) { Loop = (a, at), Again = pace };
        }
        if (total == 0)
        {
            var up = pace is { } now ? TempoMarks.Up(now) : (double?)null;
            w.Place("Clean:").Plain(inTime ? " every note right and on the beat." : " every note right.");
            if (up is { } faster && pace is { } was) w.Plain($" Try it a notch faster, {Mark(faster)} instead of {BarWords.Tempo(was)}.");
            return new(RunFinding.Clean, from, to, firstBar, bars, 0, 0, w.Done()) { Again = up };
        }
        if (inPlace >= 0.5 * total && inPlace >= 3) return PlaceSaid();
        if (hit >= 0.55 * played.Count)
        {
            var slower = pace is { } now ? TempoMarks.Down(TempoMarks.Down(now)) : (double?)null;
            w.Place("Mistakes all through:").Plain(" ").Kinds(notes, beat).Plain($", in {hit} of the {played.Count} bars. Try it slower");
            if (slower is { } s2 && pace is { } was) w.Plain($", {Mark(s2)} instead of {BarWords.Tempo(was)}");
            w.Plain(", or a hand at a time.");
            return new(RunFinding.AllThrough, from, to, firstBar, bars, notes, beat, w.Done()) { Again = slower };
        }
        if (stretch is { } st)
        {
            var held = In(st.From, st.To);
            var rest = total - held;
            var middle = from + (to - from + 1) / 2;
            var half = st.From == middle && st.To == to ? ", the second half" : st.From == from && st.To == middle - 1 ? ", the first half" : "";
            var other = half == ", the second half" ? "The first half" : half == ", the first half" ? "The second half" : "The rest";
            var inStretch = played.Where(b => b.Bar >= st.From && b.Bar <= st.To).ToList();
            w.Place($"{BarWords.Capital(BarWords.Bars(st.From, st.To))}{half}:").Plain(" ");
            if (rest == 0) w.Plain("all the run's mistakes, ").Kinds(inStretch.Sum(b => b.Notes), inStretch.Sum(b => b.Beat));
            else w.Plain($"{held} of the run's {total} mistakes");
            w.Plain($". {other} was {(rest == 0 ? "clean" : rest <= 2 ? "nearly clean" : "better")}.");
            return new(RunFinding.Stretch, st.From, st.To, firstBar, bars, notes, beat, w.Done()) { Loop = st };
        }
        if (inPlace >= 3) return PlaceSaid();
        w.Place("A slip here and there,").Plain(" no one place: ").Kinds(notes, beat).Plain(".");
        return new(RunFinding.Here, from, to, firstBar, bars, notes, beat, w.Done());

        QuickRun PlaceSaid()
        {
            var inBars = played.Where(b => b.Bar >= place.From && b.Bar <= place.To).ToList();
            var (pn, pb) = (inBars.Sum(b => b.Notes), inBars.Sum(b => b.Beat));
            var ahead = inBars.Sum(b => b.Ahead);
            var one = place.From == place.To;
            w.Place($"{BarWords.Capital(BarWords.Bars(place.From, place.To))} need{(one ? "s" : "")} help:").Plain(" ");
            if (pn == 0 && pb > 0)
            {
                w.Plain("every note right, but ").Kinds(0, pb);
                // Two thirds of them or more early is rushing, a third or fewer holding back.
                if (pb >= 4 && ahead >= pb * 2 / 3.0) w.Plain(": you rushed");
                else if (pb >= 4 && ahead <= pb / 3.0) w.Plain(": you held back");
                w.Plain(".");
            }
            else w.Kinds(pn, pb).Plain(".");
            var rest = total - pn - pb;
            if (rest > 0) w.Plain($" {BarWords.Count(rest, "more mistake")} elsewhere.");
            return new(RunFinding.Place, place.From, place.To, firstBar, bars, notes, beat, w.Done()) { Loop = (place.From, place.To) };
        }
    }

    /// <summary>
    /// A loop's quick review from its times round, in order: its bars, its tempo and whether it was In time. It's getting better when
    /// its last two times round (the last, under four) have at most 60% of the mistakes its first two had, worse at 125% or more. A
    /// bar came right with two mistakes or more its first times round and none its last; a bar still goes wrong with two or more its
    /// last, and a neighbour at least half as bad goes with it.
    /// </summary>
    public static QuickLoop Loop(IReadOnlyList<PiecePass> passes, int from, int to, double? tempo, bool inTime)
    {
        var bars = Enumerable.Range(from, to - from + 1).ToList();
        var rounds = passes.Select(p => p.Trouble.ToDictionary(t => t.Bar, t => BarCount.Of(t, inTime))).ToList();
        List<int> notes = [.. rounds.Select(r => r.Values.Where(b => b.Bar >= from && b.Bar <= to).Sum(b => b.Notes))];
        List<int> beat = [.. rounds.Select(r => r.Values.Where(b => b.Bar >= from && b.Bar <= to).Sum(b => b.Beat))];
        List<IReadOnlyList<int>> grid = [.. bars.Select(bar => (IReadOnlyList<int>)[.. rounds.Select(r => r.TryGetValue(bar, out var c) ? c.Both : 0)])];
        var totals = notes.Zip(beat, (n, b) => n + b).ToList();
        var pace = tempo is { } tt ? Math.Round(tt) : (double?)null;
        var w = new Words();
        if (totals.Count == 0)
        {
            w.Place("No times round").Plain(" played to the end.");
            return new(from, to, pace, notes, beat, grid, LoopTrend.Once, w.Done());
        }

        var k = totals.Count >= 4 ? 2 : 1;
        int Sum(IEnumerable<int> xs) => xs.Sum();
        var first = bars.Select((_, i) => Sum(grid[i].Take(k))).ToList();
        var last = bars.Select((_, i) => Sum(grid[i].Skip(grid[i].Count - k))).ToList();
        var (a, z) = (totals.Take(k).Average(), totals.Skip(totals.Count - k).Average());
        var trend = totals.Count < 2 ? LoopTrend.Once : z <= a * 0.6 ? LoopTrend.Better : z >= a * 1.25 && z > a ? LoopTrend.Worse : LoopTrend.NoBetter;
        var cameRight = bars.Where((_, i) => first[i] >= 2 && last[i] == 0).ToList();
        var still = bars.Select((bar, i) => (Bar: bar, V: last[i])).Where(x => x.V >= 2).OrderByDescending(x => x.V).ThenBy(x => x.Bar).ToList();
        var pair = still.Count >= 2 && Math.Abs(still[0].Bar - still[1].Bar) == 1 && still[1].V >= still[0].V / 2.0;
        (int From, int To)? narrow = still.Count == 0 ? null : pair ? (Math.Min(still[0].Bar, still[1].Bar), Math.Max(still[0].Bar, still[1].Bar)) : (still[0].Bar, still[0].Bar);

        var times = totals.Count;
        if (trend == LoopTrend.Once) w.Place("Once round:").Plain($" {BarWords.Count(totals[0], "mistake")}.");
        else
        {
            w.Place(trend switch { LoopTrend.Better => "Getting better:", LoopTrend.Worse => "Getting worse:", _ => "No better yet:" })
                .Plain($" {BarWords.Count(totals[0], "mistake")} the 1st time round, {totals[^1]} the {Nth(times)}.");
            // The first stretch of bars that came right, in a row.
            var rightTo = cameRight.Count == 0 ? 0 : cameRight.TakeWhile((bar, i) => bar == cameRight[0] + i).Last();
            if (cameRight.Count > 0) w.Plain($" {BarWords.Capital(BarWords.Bars(cameRight[0], rightTo))} came right{(narrow is null ? "." : ";")}");
            if (narrow is { } n)
            {
                var named = BarWords.Bars(n.From, n.To);
                if (cameRight.Count > 0) w.Plain($" {named} still go{(n.From == n.To ? "es" : "")} wrong.");
                else w.Plain(" ").Place(BarWords.Capital(named)).Plain($" still go{(n.From == n.To ? "es" : "")} wrong.");
            }
            else w.Plain(" Clean by the end.");
            if (trend != LoopTrend.Better && narrow is not null && pace is { } now) w.Plain($" Slow it down a notch, to {Mark(TempoMarks.Down(now))}.");
        }
        // Keys: clean by the end, a notch faster; getting better (or once round), the bars still wrong on their own, or the same again;
        // no better or worse, a notch slower.
        double? again = narrow is null ? (pace is { } up ? TempoMarks.Up(up) : null)
            : trend is LoopTrend.Better or LoopTrend.Once ? pace
            : pace is { } down ? TempoMarks.Down(down) : null;
        var stillBars = narrow is { } nb ? Enumerable.Range(nb.From, nb.To - nb.From + 1).ToList() : [];
        return new(from, to, pace, notes, beat, grid, trend, w.Done())
        {
            Narrow = narrow is { } x && x.To - x.From + 1 < bars.Count ? x : null,
            Again = again,
            Still = stillBars,
        };
    }

    /// <summary>
    /// A session's quick review from its runs, on a piece of <paramref name="shape"/>. The heat map's rows are its runs through the
    /// piece (from a bar to the end, or stopped on the way; not a loop's times round), and what it says is judged on them: the places
    /// still needing help are the worst bars of the last two runs, a mistake a run or more, grown through neighbours at least half as
    /// bad to four bars at most, and a second place apart if it's at least 70% as bad; a stretch came right with a mistake a run or
    /// more in the first two runs and none in the last two (four runs or more). It's getting better when the last run through the
    /// whole piece is 2.5 points up on the first at the same tempo or faster, or faster and within 3 points, or a stretch came right;
    /// slipping when 5 points or more down.
    /// </summary>
    public static QuickSession Session(IEnumerable<PieceRun> runs, PieceShape shape)
    {
        var all = runs.ToList();
        var units = Played.Of(all, shape.LastBar);
        var (firstBar, lastBar) = (shape.FirstBar, shape.LastBar);
        var throughs = units.Where(u => u.Run.Passes.Count == 0).ToList();
        var loops = units.Where(u => u.Run.Passes.Count > 0).Select(u => u.Run).Distinct().Count();

        // The rows: each run through, its bars from the piece's first to its last.
        var rows = throughs.Select((u, i) =>
        {
            var byBar = u.Trouble.ToDictionary(t => t.Bar);
            return new HeatRow(i + 1, u.InTime, u.Run.Tempo, u.Run.Hands, [.. Enumerable.Range(firstBar, lastBar - firstBar + 1)
                .Select(bar => bar < u.Run.From || bar > u.Last ? null : byBar.TryGetValue(bar, out var t) ? BarCount.Of(t, u.InTime) : BarCount.None(bar))]);
        }).ToList();
        double? Average(IEnumerable<HeatRow> over, int bar)
        {
            var counts = over.Select(r => r.Bars[bar - firstBar]).OfType<BarCount>().ToList();
            return counts.Count == 0 ? null : counts.Average(c => c.Both);
        }
        var lastTwo = rows.TakeLast(2).ToList();
        var firstTwo = rows.Take(2).ToList();
        double End(int bar) => bar < firstBar || bar > lastBar ? 0 : Average(lastTwo, bar) ?? 0;

        // The places, and what each needs in the last two runs: timing when two thirds of its mistakes or more are off the beat,
        // wrong notes when two thirds or more are notes, both otherwise.
        var ends = Enumerable.Range(firstBar, lastBar - firstBar + 1).Select(bar => (Bar: bar, V: End(bar))).OrderByDescending(x => x.V).ThenBy(x => x.Bar).ToList();
        (int From, int To) Grow(int bar)
        {
            var (a, z, top) = (bar, bar, End(bar));
            while (z - a < 3)
            {
                var (l, r) = (End(a - 1), End(z + 1));
                if (Math.Max(l, r) < Math.Max(1, top / 2)) break;
                if (l >= r) a--; else z++;
            }
            return (a, z);
        }
        string Needs((int From, int To) p)
        {
            var counts = lastTwo.SelectMany(r => r.Bars).OfType<BarCount>().Where(c => c.Bar >= p.From && c.Bar <= p.To).ToList();
            var (n, b) = (counts.Sum(c => c.Notes), counts.Sum(c => c.Beat));
            return b >= (n + b) * 2 / 3.0 ? "timing" : n >= (n + b) * 2 / 3.0 ? "wrong notes" : "wrong notes and timing";
        }
        var places = new List<(int From, int To)>();
        if (ends.Count > 0 && ends[0].V >= 1)
        {
            var p1 = Grow(ends[0].Bar);
            places.Add(p1);
            var next = ends.FirstOrDefault(x => (x.Bar < p1.From - 1 || x.Bar > p1.To + 1) && x.V >= Math.Max(1, 0.7 * ends[0].V));
            if (next.V > 0) places.Add(Grow(next.Bar));
        }
        List<SessionPlace> named = [.. places.OrderBy(p => p.From).Select(p => new SessionPlace(p.From, p.To, Needs(p)))];

        // Came right: wrong in the first two runs, clean in the last two, the stretch of them with the most mistakes at first.
        (int From, int To)? cameRight = null;
        if (rows.Count >= 4)
        {
            var right = Enumerable.Range(firstBar, lastBar - firstBar + 1).Where(bar => (Average(firstTwo, bar) ?? 0) >= 1 && Average(lastTwo, bar) == 0).ToList();
            var stretches = new List<(int From, int To)>();
            foreach (var bar in right)
            {
                if (stretches.Count > 0 && stretches[^1].To == bar - 1) stretches[^1] = (stretches[^1].From, bar);
                else stretches.Add((bar, bar));
            }
            double Weight((int From, int To) s) => Enumerable.Range(s.From, s.To - s.From + 1).Sum(bar => Average(firstTwo, bar) ?? 0);
            cameRight = stretches.Count == 0 ? null : stretches.OrderByDescending(Weight).ThenBy(s => s.From).First();
        }

        // How it went, from the runs through the whole piece In time.
        var whole = units.Where(u => u.Run.Passes.Count == 0 && u.Run.Through && u.InTime && u.RightInTime is not null).ToList();
        var head = cameRight is not null ? "Getting better" : "Holding steady";
        string? through = null;
        if (whole.Count >= 2)
        {
            var (a, z) = (whole[0], whole[^1]);
            var points = (z.RightInTime!.Value - a.RightInTime!.Value) * 100;
            var (ta, tz) = (a.Run.Tempo ?? 0, z.Run.Tempo ?? 0);
            head = (points >= 2.5 && tz >= ta) || (tz > ta && points > -3) || cameRight is not null ? "Getting better" : points <= -5 ? "Slipping by the end" : "Holding steady";
            through = $"Through the piece: {BarWords.Percent(a.RightInTime.Value)} correct and on time at {Mark(ta)} at first, {BarWords.Percent(z.RightInTime.Value)} at {Mark(tz)} at the end.";
        }

        var w = new Words().Plain($"{head}. ");
        var came = cameRight is { } cr ? $"{BarWords.Capital(BarWords.Bars(cr.From, cr.To))} came right." : null;
        if (named.Count == 0) w.Plain(came ?? "Nothing stands out.");
        else if (named.Count == 1)
        {
            var p = named[0];
            w.Place(BarWords.Capital(BarWords.Bars(p.From, p.To))).Plain($" still need{(p.From == p.To ? "s" : "")} help with {p.Needs}.");
        }
        else if (named[0].Needs == named[1].Needs)
        {
            var both = named.All(p => p.From == p.To) ? $"Bars {named[0].From} and {named[1].From}" : $"{BarWords.Capital(BarWords.Bars(named[0].From, named[0].To))} and {BarWords.Bars(named[1].From, named[1].To)}";
            w.Place(both).Plain($" still need help with {named[0].Needs}.");
        }
        else
        {
            w.Place(BarWords.Capital(BarWords.Bars(named[0].From, named[0].To))).Plain($" still need{(named[0].From == named[0].To ? "s" : "")} help with {named[0].Needs}, ")
                .Place(BarWords.Bars(named[1].From, named[1].To)).Plain($" with {named[1].Needs}.");
        }
        var sub = string.Join(" ", new[] { named.Count > 0 ? came : null, through }.OfType<string>());

        // The timeline: each run and each time round In time, in order, a loop's times round together.
        var groups = new List<TimeGroup>();
        PieceRun? inLoop = null;
        foreach (var u in units.Where(u => u.InTime && u.RightInTime is not null))
        {
            var loop = u.Run.Passes.Count > 0;
            if (loop && ReferenceEquals(inLoop, u.Run)) { groups[^1] = groups[^1] with { Figures = [.. groups[^1].Figures, u.RightInTime!.Value] }; continue; }
            var hand = u.Run.Hands switch { "left" => " LH", "right" => " RH", _ => "" };
            var label = loop ? $"{u.Run.From}–{u.Run.To}{hand}" : u.Run.Stopped ? $"{u.Run.From}–{u.Last}{hand}" : u.Run.From <= Math.Max(1, firstBar) ? $"Whole{hand}" : $"{u.Run.From}–end{hand}";
            groups.Add(new(label, loop, [u.RightInTime!.Value]));
            inLoop = loop ? u.Run : null;
        }

        // Next, from the tempo you ended at (the last run through's, or the last run's).
        var ended = throughs.LastOrDefault(u => u.InTime)?.Run.Tempo ?? units.LastOrDefault(u => u.InTime)?.Run.Tempo;
        var nw = new Words();
        (int From, int To)? nextLoop = null;
        double? nextTempo = null;
        if (named.Count > 0 && ended is { } e)
        {
            var p = named[0];
            nextLoop = (p.From, p.To);
            nextTempo = TempoMarks.Down(e);
            nw.Place($"Loop {BarWords.Bars(p.From, p.To)} at {Mark(nextTempo.Value)}").Plain($", a notch under the {Mark(e)} you ended at, until two times round in a row are clean.");
        }
        else if (ended is { } e2)
        {
            nextTempo = TempoMarks.Up(e2);
            nw.Place($"Play it at {Mark(nextTempo.Value)}").Plain($", a notch over the {Mark(e2)} you ended at.");
        }
        else if (named.Count > 0)
        {
            var p = named[0];
            nextLoop = (p.From, p.To);
            nw.Place($"Loop {BarWords.Bars(p.From, p.To)}").Plain(" until two times round in a row are clean.");
        }

        return new(head, named, cameRight, w.Done(), sub.Length == 0 ? null : sub, firstBar, rows, groups, throughs.Count, loops,
            units.Any(u => !u.InTime), nw.Done())
        {
            NextLoop = nextLoop, NextTempo = nextTempo,
            Locks = [.. all.Where(r => r.Lock is not null && r.To is not null).OrderBy(r => r.At).ThenBy(r => r.Id).Select(LockLine)],
        };
    }

    // A spot Lock in went round, or a loop counted to its clean times round in a row: its bars, how many in a row and at what
    // tempo, and how it ended: done (after how many that went wrong), let go, or stopped short of it.
    private static Sentence LockLine(PieceRun run)
    {
        var locked = run.Lock!;
        var wrong = run.Passes.Count(p => p.Right < p.Notes || p.Trouble.Any(t => t.NotRight > 0));
        var inARow = run.Passes.Reverse().TakeWhile(p => p.Right == p.Notes && p.Trouble.All(t => t.NotRight == 0)).Count();
        var at = run.Tempo is { } tempo ? $" at {Mark(tempo)}" : "";
        var w = new Words().Place(BarWords.Capital(BarWords.Bars(run.From, run.To!.Value)));
        if (locked.Done) w.Plain($": {locked.Needed} clean in a row{at}, {(wrong == 0 ? "none going wrong" : $"after {wrong} that went wrong")}.");
        else if (locked.LetGo) w.Plain($" let go after {wrong} times round that went wrong{at}: still to fix.");
        else w.Plain($": {Math.Min(inARow, locked.Needed)} clean in a row of {locked.Needed}{at} when you stopped.");
        return w.Done();
    }

    /// <summary>A tempo as a metronome mark, so it isn't taken for a score: "♩ 69".</summary>
    public static string Mark(double tempo) => $"♩ {BarWords.Tempo(tempo)}";

    /// <summary>
    /// A loop counted to its clean times round in a row, in words: Lock in's "Locked in: 7 clean in a row at ♩ 76.", Practice it's
    /// "Done: 2 clean in a row at ♩ 63.".
    /// </summary>
    public static Sentence Locked(int inARow, double? tempo, string lead = "Locked in") =>
        new Words().Place(lead).Plain($": {inARow} clean in a row{(tempo is { } at ? $" at {Mark(at)}" : "")}.").Done();

    private static string Nth(int n) => $"{n}{(n % 100 is >= 11 and <= 13 ? "th" : (n % 10) switch { 1 => "st", 2 => "nd", 3 => "rd", _ => "th" })}";

    // A sentence put together a part at a time, plain parts run together.
    private sealed class Words
    {
        private readonly List<SentencePart> parts = [];

        public Words Plain(string text) => Add(text, Said.Plain);

        public Words Place(string text) => Add(text, Said.Place);

        // Counts of the two kinds: "13 wrong notes", "4 off the beat", or both with "and" between.
        public Words Kinds(int notes, int beat)
        {
            if (notes > 0) Add(BarWords.Count(notes, "wrong note"), Said.Notes);
            if (notes > 0 && beat > 0) Plain(" and ");
            if (beat > 0) Add($"{beat} off the beat", Said.Beat);
            return this;
        }

        public Sentence Done() => new([.. parts]);

        private Words Add(string text, Said said)
        {
            if (text.Length == 0) return this;
            if (said == Said.Plain && parts.Count > 0 && parts[^1].As == Said.Plain) parts[^1] = parts[^1] with { Text = parts[^1].Text + text };
            else parts.Add(new(text, said));
            return this;
        }
    }
}
