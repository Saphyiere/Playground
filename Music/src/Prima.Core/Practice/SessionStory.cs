using Prima.Core.Timing;
using static Prima.Core.Practice.BarWords;

namespace Prima.Core.Practice;

/// <summary>The kind of work a session was, which decides what's worth saying about it.</summary>
public enum Work
{
    /// <summary>Finding the notes: a hand at a time, at your own pace, or In time with many of them still wrong.</summary>
    Reading,
    /// <summary>The notes mostly there In time with both hands, not all of them: getting them right and the piece to hold together.</summary>
    Securing,
    /// <summary>The notes there: the timing, the touch, loud and soft, and the tempo are what's left.</summary>
    Shaping,
    /// <summary>The notes there at the piece's own tempo: playing it through, and keeping it.</summary>
    Performing,
}

/// <summary>How a line of a session's story is keyed.</summary>
public enum Tint
{
    /// <summary>Something that went well.</summary>
    Good,
    /// <summary>Something to work on.</summary>
    Bad,
    /// <summary>Just so.</summary>
    Plain,
}

/// <summary>A line of a session's story: what it's about, whether it's good news or something to work on, and its words.</summary>
public sealed record StoryLine(string About, Tint Tint, string Words);

/// <summary>
/// Something worth doing next on a piece, to start with a tap: its bars (round a loop to a last bar, or from a bar on), the hand,
/// In time at a tempo or Wait for me, what it is in words, and why, from the session's own figures.
/// </summary>
public sealed record NextStep(int From, int? To, string Hands, bool InTime, double? Tempo, string What, string Why);

/// <summary>
/// A practice session in words, for its look back: the kind of work it was, what it came to in a sentence, a few lines on what
/// moved, what's still hard and how it compares with the time before (which lines depends on the kind of work: wrong notes come
/// before timing, and timing before touch), what to do next, whether the session's end went worse than what came before it, as
/// tiredness does, where the piece stands now bar by bar, and the bars played today.
/// </summary>
public sealed record SessionStory(Work Work, string Headline, IReadOnlyList<StoryLine> Lines, IReadOnlyList<NextStep> Steps, bool Tired, PieceStanding Standing, IReadOnlySet<int> Today)
{
    /// <summary>
    /// The story of a session's runs on a piece, with the runs kept on it before (every session's, and Play's) and the day it is.
    /// </summary>
    public static SessionStory Of(IEnumerable<PieceRun> session, IEnumerable<PieceRun> earlier, PieceShape piece, DateOnly today) => new Teller([.. session.OrderBy(r => r.At)], [.. earlier.OrderBy(r => r.At)], piece, today).Tell();

    /// <summary>
    /// What to do next on a piece from the runs kept on it, for its front: the steps of its last practice session's story, and
    /// the day it was. None for a piece with no session that had a run, or whose last one left nothing to do.
    /// </summary>
    public static (DateOnly Day, IReadOnlyList<NextStep> Steps)? Next(IEnumerable<PieceRun> runs, PieceShape piece, DateOnly today)
    {
        var all = runs.OrderBy(r => r.At).ToList();
        if (all.LastOrDefault(r => r.Session is not null) is not { } last) return null;

        var session = all.Where(r => r.Session == last.Session).ToList();
        var story = Of(session, all.Where(r => r.At < session[0].At), piece, today);
        return story.Steps.Count == 0 ? null : (last.Day, story.Steps);
    }

    // A stretch at one tempo (or, in Wait for me, at your own pace): its points in a row.
    private sealed record Segment(Stretch Stretch, double? Tempo, List<StretchPoint> Points)
    {
        public StretchPoint First => Points[0];

        public StretchPoint Last => Points[^1];

        private int Third => Math.Max(1, Points.Count / 3);

        /// <summary>How it started: its first third, the first at least.</summary>
        public double Start => Points.Take(Third).Average(p => p.Figure);

        /// <summary>How it ended: its last third.</summary>
        public double End => Points.Skip(Points.Count - Third).Average(p => p.Figure);

        public double Gain => End - Start;
    }

    private sealed class Teller
    {
        private readonly List<PieceRun> runs;
        private readonly List<PieceRun> before;
        private readonly PieceShape piece;
        private readonly DateOnly today;
        private readonly SessionReview review;
        private readonly List<Played> units;
        private readonly List<Played> timed;
        private readonly List<Played> throughs;
        private readonly List<Segment> segments = [];
        private readonly PieceStanding standing;
        private readonly HashSet<int> played;
        private readonly HashSet<int> fresh;
        private readonly bool sayHands;
        private readonly double accuracy;
        private readonly double? tempo;
        // The kind of work it was; whether its end went worse than what came before, and from when; and the time through it's
        // judged by: the last, or then the best.
        private Work work;
        private bool tired;
        private DateTimeOffset? lateFrom;

        private Played? Reference => tired ? throughs.OrderByDescending(u => u.Figure).ThenByDescending(u => u.Run.At).FirstOrDefault() : throughs.LastOrDefault();

        public Teller(List<PieceRun> runs, List<PieceRun> before, PieceShape piece, DateOnly today)
        {
            (this.runs, this.before, this.piece, this.today) = (runs, before, piece, today);
            review = SessionReview.Of(runs, piece.LastBar);
            units = Played.Of(runs, piece.LastBar);
            // In time with both hands, which is what the notes, the timing and the tempo are judged on; a piece practiced only a
            // hand at a time In time is judged on that.
            timed = units.Where(u => u.InTime && u.Both).ToList() is { Count: > 0 } both ? both : [.. units.Where(u => u.InTime)];
            throughs = [.. units.Where(u => u is { InTime: true, Both: true, Run.Through: true })];
            foreach (var stretch in review.Stretches)
            {
                // A stretch's runs that got to its end are what's set against each other; ones stopped on the way only when
                // they all were.
                foreach (var point in stretch.Points.Any(p => !p.Partial) ? stretch.Points.Where(p => !p.Partial) : stretch.Points)
                {
                    if (segments.Count > 0 && ReferenceEquals(segments[^1].Stretch, stretch) && segments[^1].Tempo == point.Tempo) segments[^1].Points.Add(point);
                    else segments.Add(new Segment(stretch, point.Tempo, [point]));
                }
            }
            standing = PieceStanding.Of([.. before, .. runs], piece);
            var was = PieceStanding.Of(before, piece);
            played = [.. Enumerable.Range(piece.FirstBar, piece.Bars).Where(bar => units.Any(u => u.Covers(bar)))];
            fresh = before.Count == 0 ? [] : [.. played.Where(bar => was.Bars[bar - piece.FirstBar].State == BarState.NotPlayed)];
            sayHands = piece.TwoHands && units.Any(u => !u.Both);
            accuracy = timed.Sum(u => u.Notes) is > 0 and var notes ? timed.Sum(u => u.Right) / (double)notes : 0;
            // The tempo most of it was played at.
            tempo = timed.Where(u => u.Run.Tempo is not null).GroupBy(u => u.Run.Tempo!.Value).OrderByDescending(g => g.Sum(u => u.Notes)).Select(g => (double?)g.Key).FirstOrDefault();
        }

        public SessionStory Tell()
        {
            // Nothing played to the end of a run or a pass: there's no story, only where the piece stands.
            if (units.Count == 0) return new(Practice.Work.Reading, "Nothing was played to the end of a run or a pass this time.", [], [], false, standing, played);

            work = Sort();
            (tired, var tiredLine) = Tired();
            var lines = new List<StoryLine?>();
            string headline;
            switch (work)
            {
                case Practice.Work.Reading:
                {
                    headline = Read();
                    var moved = Moved();
                    lines.AddRange([moved?.Line, Hard(notesOnly: true), Since(), Clicked(moved?.Segment), Fast()?.Line, Fixed()]);
                    break;
                }
                case Practice.Work.Securing:
                {
                    var moved = Moved();
                    headline = Held() ?? moved?.Line.Words ?? $"{Capital(Runs(played))} worked on; not played through today.";
                    lines.AddRange([headline == moved?.Line.Words ? null : moved?.Line, Hard(notesOnly: true), Fast()?.Line ?? Stuck(moved?.Segment)?.Line, Since(), Fixed()]);
                    break;
                }
                case Practice.Work.Shaping:
                {
                    var (timing, held, marked) = (Timing(), Let(), Expression().ToList());
                    var left = new List<string>();
                    if (timing is { Tint: Tint.Bad }) left.Add("the timing");
                    if (held is not null || marked.Any(m => m is { About: "Touch", Tint: Tint.Bad })) left.Add("the touch");
                    if (marked.Any(m => m is { About: "Loud and soft", Tint: Tint.Bad })) left.Add("the loud and soft");
                    var at = tempo is { } now ? piece.Tempo is { } own && now < PieceStanding.Near * own ? $" at {Tempo(now)} of the piece's {Tempo(own)}" : $" at {Tempo(now)}" : "";
                    headline = left.Count > 0 ? $"The notes are there{at} ({Percent(accuracy)} right). What's left is {And(left)}."
                        : piece.Tempo is { } goes && tempo < PieceStanding.Near * goes ? $"The notes and the timing are there at {Tempo(tempo)} ({Percent(accuracy)} right). What's left is the tempo: the piece goes at {Tempo(goes)}."
                        : $"The notes and the timing are there{at} ({Percent(accuracy)} right).";
                    // A bar with a wrong note still in it is worth a line here; that none has one isn't.
                    lines.AddRange([timing, .. marked, held, Since(), Hard(notesOnly: true) is { Tint: Tint.Bad } wrong ? wrong : null, Fast()?.Line]);
                    break;
                }
                default:
                {
                    headline = Performed();
                    var timing = Timing();
                    lines.AddRange([Cold(), Slips(), Faster(), Since(), timing is { Tint: Tint.Bad } ? timing : null, .. Expression().Where(m => m.Tint == Tint.Bad)]);
                    break;
                }
            }
            // Five lines at most, the ones that matter most to this kind of work first; then how the end went, or with room
            // for it, where the time went.
            var said = lines.OfType<StoryLine>().Take(5).ToList();
            if ((tiredLine ?? (said.Count < 5 ? Minutes() : null)) is { } last) said.Add(last);
            return new(work, headline, said, Steps(), tired, standing, played);
        }

        // ——— The kind of work ———

        // Reading while half the notes played were a hand at a time or at your own pace, or In time with a quarter of them wrong;
        // securing until nineteen in twenty are right; performing at the piece's own tempo (or near it) with the notes all but
        // all right, or where it marks none, with the timing as tight; shaping between.
        private Work Sort()
        {
            var all = units.Sum(u => u.Notes);
            var slow = units.Where(u => !u.InTime || !u.Both).Sum(u => u.Notes);
            if (timed.Count == 0 || (piece.TwoHands ? slow : units.Where(u => !u.InTime).Sum(u => u.Notes)) * 2 >= all || accuracy < 0.75) return Practice.Work.Reading;
            if (accuracy < 0.95) return Practice.Work.Securing;

            var right = timed.Sum(u => u.Right);
            var steady = right > 0 && timed.Sum(u => u.Trouble.Sum(t => t.Off)) <= 0.04 * right;
            var atTempo = piece.Tempo is { } own ? tempo >= PieceStanding.Near * own : steady;
            return accuracy >= 0.975 && atTempo && throughs.Count > 0 ? Practice.Work.Performing : Practice.Work.Shaping;
        }

        // ——— Names and figures ———

        // The last bar a stretch got to: a loop's last, the piece's, or where its runs were stopped, when every one of them was
        // (the session's review works that out, so What was played and the in-depth review name a stretch as this does).
        private int Reached(Stretch s) => s.To ?? s.Reached ?? piece.LastBar;

        // A stretch's bars: a loop's, "the whole piece", "from bar 17", or the bars its runs got through when they were all stopped.
        private string Name(Stretch s) => s.To is null && Reached(s) == piece.LastBar ? s.From <= piece.FirstBar ? "the whole piece" : $"from bar {s.From}" : Bars(s.From, Reached(s));

        // The hand, when it's one of them: "bars 5–8 (right hand)"; and while the notes are being found a hand at a time, that
        // it's both.
        private string Hand(string hands) => hands switch
        {
            "right" => " (right hand)",
            "left" => " (left hand)",
            _ => sayHands && work == Practice.Work.Reading ? " (hands together)" : "",
        };

        private static string At(Segment s) => s.Tempo is { } tempo ? $" at {Tempo(tempo)}" : "";

        private static string Went(Stretch s, StretchPoint p) => s.InTime ? Percent(p.Figure) : $"{p.Right} of {p.Notes}";

        private static string Kind(Stretch s) => s.InTime ? "right in time" : "right first time";

        private static string Rounds(Stretch s, int n) => s.To is null ? Count(n, "run") : Count(n, "pass", "passes");

        // What a step is, in words: "Bars 20–22, In time at 60"; with its hand when it's one of them, or, while the notes are
        // being found, that it's both: "Bars 1–4, hands together, Wait for me".
        private string What(int from, int? to, string hands, bool inTime, double? at)
        {
            var bars = to is { } last ? Bars(from, last) : from <= piece.FirstBar ? "the whole piece" : $"from bar {from}";
            var hand = !piece.TwoHands ? "" : hands switch { "right" => ", right hand", "left" => ", left hand", _ => work == Practice.Work.Reading ? ", hands together" : "" };
            return $"{Capital(bars)}{hand}, {(inTime ? $"In time at {Tempo(at)}" : "Wait for me")}";
        }

        // A bar with a bar either side, as Practice bar N loops it.
        private (int From, int To) Around(int bar) => (Math.Max(piece.FirstBar, bar - 1), Math.Min(piece.LastBar, bar + 1));

        // The bars where something happens most of the times they're played (three times in four), the most often first, then
        // the most notes: up to six. It's the times it happened in that count, not its notes, so three notes out in one time
        // through of four is once.
        private List<int> Where(Func<BarTrouble, int> count) => [.. Enumerable.Range(piece.FirstBar, piece.Bars)
            .Select(bar => (Bar: bar, Plays: timed.Count(u => u.Covers(bar)), Times: timed.Count(u => u.In(bar) is { } t && count(t) > 0), Sum: timed.Sum(u => u.In(bar) is { } t ? count(t) : 0)))
            .Where(b => b.Plays > 0 && b.Times >= 0.75 * b.Plays).OrderByDescending(b => b.Times / (double)b.Plays).ThenByDescending(b => b.Sum / (double)b.Plays).Take(6).Select(b => b.Bar).Order()];

        // ——— What moved, and what didn't ———

        // The stretch that came up most at one tempo (or at your own pace), and whether it held a step faster.
        private (Segment Segment, StoryLine Line)? Moved()
        {
            var best = segments.Where(s => s.Points.Count >= 3 && s.Gain >= 0.08 && s.Last.Figure > s.First.Figure).OrderByDescending(s => s.Gain * Math.Sqrt(s.Points.Count)).FirstOrDefault();
            if (best is null) return null;

            var words = $"{Capital(Name(best.Stretch))}{Hand(best.Stretch.Hands)}{At(best)}: {Went(best.Stretch, best.First)} → {Went(best.Stretch, best.Last)} {Kind(best.Stretch)} over {Rounds(best.Stretch, best.Points.Count)}.";
            var next = segments.IndexOf(best) + 1;
            if (next < segments.Count && segments[next] is { } faster && ReferenceEquals(faster.Stretch, best.Stretch) && faster.Tempo > best.Tempo && Fast()?.Faster != faster)
            {
                words += faster.End >= PieceStanding.Holds ? $" It held at {Tempo(faster.Tempo)} ({Went(faster.Stretch, faster.Last)})." : $" At {Tempo(faster.Tempo)} it's {Went(faster.Stretch, faster.Last)}: not there yet.";
            }
            return (best, new("Moved", Tint.Good, words));
        }

        // A stretch gone round four times or more In time that ended where it started, under the 85% that holds.
        private (Segment Segment, StoryLine Line)? Stuck(Segment? moved)
        {
            var stuck = segments.Where(s => s.Stretch.InTime && s.Points.Count >= 4 && Math.Abs(s.Gain) < 0.03 && s.End < PieceStanding.Holds && s != moved).OrderByDescending(s => s.Points.Count).FirstOrDefault();
            return stuck is null ? null : (stuck, new("Stuck", Tint.Bad,
                $"{Capital(Name(stuck.Stretch))}{Hand(stuck.Stretch.Hands)}{At(stuck)} stayed around {Percent(stuck.Points.Average(p => p.Figure))} {Kind(stuck.Stretch)} over {Rounds(stuck.Stretch, stuck.Points.Count)}."));
        }

        // A stretch taken a step faster than it holds: it ended well under where it ended at the slower tempo, and under 85%.
        private (Segment Slower, Segment Faster, StoryLine Line)? Fast()
        {
            var pairs = segments.Zip(segments.Skip(1)).Where(p => ReferenceEquals(p.First.Stretch, p.Second.Stretch) && p.First.Stretch.InTime && p.Second.Tempo > p.First.Tempo
                && p.Second.End <= p.First.End - 0.08 && p.Second.End < PieceStanding.Holds).OrderByDescending(p => p.First.End - p.Second.End).ToList();
            if (pairs is not [var (slower, faster), ..]) return null;

            // The slower tempo is where it holds only if it held there.
            return (slower, faster, new("Too fast", Tint.Plain,
                $"At {Tempo(faster.Tempo)}, {Name(faster.Stretch)}{Hand(faster.Stretch.Hands)} dropped to {Percent(faster.End)}, from {Percent(slower.End)} at {Tempo(slower.Tempo)}{(slower.End >= PieceStanding.Holds ? $": {Tempo(slower.Tempo)} is where it holds for now" : "")}."));
        }

        // The bar with most still to fix at the end (its wrong, missed and slipped notes only, while those are what's being
        // worked on: looked for among every bar left with something to fix, as bars with more off the beat can outweigh it),
        // and up to two others nearly as bad.
        private StoryLine? Hard(bool notesOnly)
        {
            var hards = review.Hards.Where(h => !notesOnly || h.Last.NotRight > 0).ToList();
            if (hards is not [var hard, ..]) return notesOnly && review.Bars.Values.Any(b => b.NotRight > 0) ? new("Still hard", Tint.Good, "No bar had a wrong note left in it by the end.") : null;

            var last = hard.Last;
            (int Count, string Words)[] kinds = [(last.Wrong, "wrong notes"), (last.Missed, "missed notes"), (last.Slips, "slips put right"), (last.Apart, "chords a key at a time"), .. notesOnly ? [] : new[] { (last.Off, "notes off the beat"), (last.Early, "notes let go early") }];
            var mostly = kinds.MaxBy(k => k.Count).Words;
            var words = hard.Recent == 1 ? $"{Capital(Bar(hard.Bar))}: {mostly} the last time it was played." : $"{Capital(Bar(hard.Bar))}: still going wrong in {hard.Bad} of its last {hard.Recent} times, mostly {mostly}.";
            var also = hards.Skip(1).Where(o => o.Last.Weight * 2 >= last.Weight).Take(2).Select(o => o.Bar).ToList();
            return new("Still hard", Tint.Bad, also.Count > 0 ? $"{words} {Capital(Runs(also))} too." : words);
        }

        private StoryLine? Fixed() => review.Fixed is { } put
            ? new("Fixed", Tint.Good, $"{Capital(Bar(put.Bar))} came right: {Trouble(put.First)} at first, clean its last {(put.Clean == 1 ? "time" : $"{put.Clean} times")}.")
            : null;

        // ——— Reading ———

        // What was read and how: the bars, a hand at a time or together, and how far the hands together got.
        private string Read()
        {
            var both = played.Where(bar => units.Any(u => u.Both && u.Covers(bar))).ToList();
            var single = played.Where(bar => units.Any(u => !u.Both && u.Covers(bar))).ToList();
            var how = !piece.TwoHands ? $"{Runs(played)} read"
                : both.Count == 0 ? $"{Runs(played)}, a hand at a time"
                : single.Count == 0 ? $"{Runs(played)}, hands together"
                : both.Count == played.Count ? $"{Runs(played)}, a hand at a time and then together"
                : $"{Runs(played)} a hand at a time, {Runs(both)} together";
            var opening = before.Count == 0 ? $"A first look: {how}." : $"{Capital(how)}.";
            // How far along the furthest of it is: with the click, if it's been tried; else the most bars read hands together.
            if (Click() is { } clicked)
            {
                return $"{opening} With the click at {Tempo(clicked.Tempo)}, {Name(clicked.Stretch)} got to {Percent(clicked.End)} right in time.";
            }
            var together = review.Stretches.Where(s => s is { InTime: false, Hands: "both" }).OrderByDescending(s => (s.To ?? piece.LastBar) - s.From).ThenByDescending(s => s.Last.At).FirstOrDefault();
            return together is null || !piece.TwoHands ? opening : $"{opening} Together, {Name(together)} ended at {together.Last.Right} of {together.Last.Notes} notes right first time.";
        }

        // What was tried with the click, hands together, that says most: the stretch of the most bars, at the tempo it went best at.
        private Segment? Click() => segments.Where(s => s.Stretch is { InTime: true, Hands: "both" }).OrderByDescending(s => Reached(s.Stretch) - s.Stretch.From).ThenByDescending(s => s.End).FirstOrDefault();

        // Its first go to its last, when it isn't what moved most.
        private StoryLine? Clicked(Segment? moved)
        {
            if (Click() is not { Points.Count: > 1 } clicked || clicked == moved) return null;

            return new("With the click", clicked.End >= PieceStanding.Holds ? Tint.Good : Tint.Plain,
                $"{Capital(Name(clicked.Stretch))}{Hand(clicked.Stretch.Hands)}{At(clicked)}: {Percent(clicked.First.Figure)} → {Percent(clicked.Last.Figure)} right in time over {Rounds(clicked.Stretch, clicked.Points.Count)}.");
        }

        // ——— Securing ———

        // Whether it holds together, from the last time through; from the best, when the end of the session went worse.
        private string? Held()
        {
            if (Reference is not { } shown) return null;

            var last = throughs[^1];
            var first = throughs.FirstOrDefault(u => u.Run.Tempo == shown.Run.Tempo && u.Run.At < shown.Run.At);
            var went = shown == last
                ? $"{Percent(shown.Figure)} right in time the last time through{(first is not null && Math.Abs(shown.Figure - first.Figure) >= 0.03 ? $", from {Percent(first.Figure)} the first" : "")}"
                : $"{Percent(shown.Figure)} right in time at best today ({Percent(last.Figure)} the last time through, at the end)";
            return shown.Figure >= PieceStanding.Holds ? $"It holds together at {Tempo(shown.Run.Tempo)}: {went}."
                : shown.Figure >= 0.7 ? $"Nearly holding together at {Tempo(shown.Run.Tempo)}: {went}."
                : $"Not holding together at {Tempo(shown.Run.Tempo)} yet: {went}.";
        }

        // ——— Shaping ———

        // The timing of the right notes: how many were in time, which way the others were out and where, and from the first time
        // through to the last.
        private StoryLine? Timing()
        {
            var right = timed.Sum(u => u.Right);
            if (right == 0) return null;

            var (off, ahead) = (timed.Sum(u => u.Trouble.Sum(t => t.Off)), timed.Sum(u => u.Trouble.Sum(t => t.Ahead)));
            var share = 1 - off / (double)right;
            if (off <= 0.03 * right) return new("Timing", Tint.Good, $"Steady: {Percent(share)} of the right notes within a tenth of a beat of the click.");

            var where = Where(t => t.Off);
            // Which way they were out is kept with a run's timing: a run kept before it has none.
            var known = timed.Any(u => u.Run.Timing is not null);
            var way = !known ? "" : ahead >= 0.65 * off ? "early" : ahead <= 0.35 * off ? "late" : "";
            var said = (way, where.Count) switch
            {
                ("early", > 0) => $"What's out is early: it runs ahead in {Runs(where)}.",
                ("late", > 0) => $"What's out is late: it falls behind in {Runs(where)}.",
                ("early", _) => "What's out is early, a note here and there.",
                ("late", _) => "What's out is late, a note here and there.",
                (_, > 0) => $"It goes out most in {Runs(where)}.",
                _ => "What's out is a note here and there.",
            };
            var same = Rested(throughs).GroupBy(u => u.Run.Tempo).Where(g => g.Count() > 1).Select(g => g.ToList()).LastOrDefault();
            var trend = same is [var first, .., var last] && Math.Abs(last.Steady - first.Steady) >= 0.03 ? $" Through at {Tempo(first.Run.Tempo)}: {Percent(first.Steady)} the first time, {Percent(last.Steady)} the last." : "";
            return new("Timing", Tint.Bad, $"{Percent(share)} of the right notes in time. {said}{trend}");
        }

        // Notes let go before their time, when it's more than one in twenty of the right ones.
        private StoryLine? Let()
        {
            var (right, early) = (timed.Sum(u => u.Right), timed.Sum(u => u.Trouble.Sum(t => t.Early)));
            if (right == 0 || early < 0.05 * right) return null;

            var where = Where(t => t.Early);
            var each = throughs.Count > 0 ? $"about {throughs.Average(u => u.Trouble.Sum(t => t.Early)):0} each time through" : $"{early} over the session";
            return new("Held", Tint.Bad, $"Notes let go before their time: {each}{(where.Count > 0 ? $", most in {Runs(where)}" : "")}.");
        }

        // Each marking as it stands: as the last time through played it, the whole piece's; and what was played after that, of
        // the ones it played again. With no time through, each as it was last played.
        private Dictionary<(Marking, int, int, string?), MarkPlayed> Marks()
        {
            static (Marking, int, int, string?) Key(MarkPlayed m) => (m.What, m.From, m.To, m.Name);
            var judged = runs.Where(r => r.Marked.Count > 0).ToList();
            var whole = judged.LastOrDefault(r => r is { Through: true, Hands: "both" });
            var latest = new Dictionary<(Marking, int, int, string?), MarkPlayed>();
            foreach (var mark in whole?.Marked ?? []) latest[Key(mark)] = mark;
            foreach (var mark in judged.Where(r => whole is null || r.At > whole.At).SelectMany(r => r.Marked))
            {
                if (whole is null || latest.ContainsKey(Key(mark))) latest[Key(mark)] = mark;
            }
            return latest;
        }

        // Loud and soft, the touch, and the pedal, as marked or not.
        private IEnumerable<StoryLine> Expression()
        {
            var latest = Marks();
            if (latest.Count == 0) yield break;

            var loud = latest.Values.Where(m => m.What is Marking.Dynamic or Marking.Swell).OrderBy(m => m.From).ToList();
            if (loud.Count > 0)
            {
                var unmet = loud.Where(m => m.Off > 0).ToList();
                string Named(MarkPlayed m) => m.What == Marking.Swell ? $"the {m.Name} in {Bars(m.From, m.To)}" : $"the {m.Name} at {Bar(m.From)}";
                yield return unmet.Count == 0
                    ? new("Loud and soft", Tint.Good, $"As marked, all {Count(loud.Count, "dynamic and hairpin", "dynamics and hairpins")}.")
                    : new("Loud and soft", Tint.Bad, $"{loud.Count - unmet.Count} of the {loud.Count} dynamics and hairpins came out as marked. Not yet: {And([.. unmet.Take(3).Select(Named)])}{(unmet.Count > 3 ? $", and {unmet.Count - 3} more" : "")}.");
            }

            var touch = new List<string>();
            foreach (var kind in latest.Values.Where(m => m.What is Marking.Staccato or Marking.Tenuto or Marking.Slur or Marking.Accent).GroupBy(m => m.What).OrderBy(g => g.Key))
            {
                var (of, off) = (kind.Sum(m => m.Of), kind.Sum(m => m.Off));
                if (off == 0) continue;

                // Where, unless it's most of the piece.
                var bars = kind.Where(m => m.Off > 0).Select(m => m.From).Distinct().ToList();
                var where = bars.Count > 6 ? ", throughout" : $" ({Runs(bars)})";
                touch.Add(kind.Key switch
                {
                    Marking.Staccato => $"{off} of {of} staccato notes held long{where}",
                    Marking.Tenuto => $"{off} of {of} tenuto notes cut short{where}",
                    Marking.Accent => $"{off} of {of} accents no stronger than the notes around them{where}",
                    _ => $"{Count(off, "gap")} under slurs{where}",
                });
            }
            if (touch.Count > 0) yield return new("Touch", Tint.Bad, $"{Capital(string.Join("; ", touch))}.");

            var pedal = latest.Values.Where(m => m.What == Marking.Pedal).ToList();
            if (pedal.Any(m => m.Off > 0)) yield return new("Pedal", Tint.Bad, $"{pedal.Count(m => m.Off == 0)} of the {pedal.Count} pedal marks as marked; not in {Runs(pedal.Where(m => m.Off > 0).Select(m => m.From))}.");
        }

        // ——— Performing ———

        // From the lowest to the highest of some times through: "96% to 99%".
        private static string Range(List<Played> of)
        {
            var (low, high) = (Percent(of.Min(u => u.Figure)), Percent(of.Max(u => u.Figure)));
            return low == high ? high : $"{low} to {high}";
        }

        // Times through before the end went worse, when it did (all of them, when none was before it).
        private List<Played> Rested(List<Played> of) => tired && lateFrom is { } from && of.Where(u => u.Run.At <= from).ToList() is { Count: > 0 } before ? before : of;

        // The times through at the tempo most of it was played at, or with none at it, at the tempo nearest it (of two as near,
        // the slower: a step over it has a line of its own), never two tempos as one; the ones at the end apart, when the end
        // went worse.
        private string Performed()
        {
            var at = throughs.GroupBy(u => u.Run.Tempo).OrderBy(g => Math.Abs((g.Key ?? 0) - (tempo ?? 0))).ThenBy(g => g.Key).First().ToList();
            var rested = Rested(at);
            var worn = at.Except(rested).ToList();
            var own = piece.Tempo is { } goes && at[0].Run.Tempo >= PieceStanding.Near * goes ? at[0].Run.Tempo >= goes ? ", the piece's own tempo" : $", near the piece's own {Tempo(goes)}" : "";
            var clean = at.Count(u => u.Right == u.Notes);
            return $"{Capital(Times(at.Count))} through at {Tempo(at[0].Run.Tempo)}{own}: {Range(rested)} right in time{(clean > 0 ? $", {Times(clean)} with every note right" : "")}{(worn.Count > 0 && worn.Min(u => u.Figure) < rested.Min(u => u.Figure) ? $"; down to {Percent(worn.Min(u => u.Figure))} by the end" : "")}.";
        }

        // The first time through, cold, against the ones after at the same tempo (before the end went worse, if it did).
        private StoryLine? Cold()
        {
            var same = Rested([.. throughs.Where(u => u.Run.Tempo == throughs[0].Run.Tempo)]);
            if (same.Count < 2) return null;

            var (first, rest) = (same[0], same.Skip(1).ToList());
            return first.Figure >= rest.Average(u => u.Figure) - 0.015
                ? new("First try", Tint.Good, $"Cold, the first time through was {Percent(first.Figure)}: as good as the ones after ({Range(rest)}).")
                : new("First try", Tint.Plain, $"Cold, the first time through was {Percent(first.Figure)}; after that, {Range(rest)}. The cold one is the one that tells.");
        }

        // The bars that kept having a note not right: in half the times through, twice at least.
        private StoryLine? Slips()
        {
            if (throughs.Count < 2) return null;

            var again = Again();
            if (again.Count == 0) return throughs.Any(u => u.Right < u.Notes) ? new("Slips", Tint.Good, "No bar kept going wrong: the slips moved around.") : null;

            var most = again[0].Times;
            var bars = again.Where(a => a.Times == most).Select(a => a.Bar).ToList();
            var others = again.Where(a => a.Times < most).Take(3).Select(a => a.Bar).ToList();
            return new("Slips", Tint.Bad, bars.Count == 1
                ? $"{Capital(Bar(bars[0]))} had a note wrong {Often(most)}{(others.Count > 0 ? $"; {Runs(others)} often too" : "")}."
                : $"{Capital(Runs(bars))} each had a note wrong {Often(most)}.");
        }

        // How many of the times through: "in 3 of the 4 times through", or when it was every one of them, "both times through"
        // or "all 3 times through".
        private string Often(int times) => times < throughs.Count ? $"in {times} of the {throughs.Count} times through" : throughs.Count == 2 ? "both times through" : $"all {times} times through";

        // The bars that had a note not right in half the times through, twice at least, the most often first. Three bars or
        // more that each went wrong just twice are slips moving around, not a bar that keeps going wrong; but not when that's
        // every time through: a bar that went wrong every time it was played is named, however few times through there were.
        private List<(int Bar, int Times)> Again()
        {
            List<(int Bar, int Times)> again = [.. Enumerable.Range(piece.FirstBar, piece.Bars)
                .Select(bar => (Bar: bar, Times: throughs.Count(u => u.In(bar) is { NotRight: > 0 }))).Where(b => b.Times >= Math.Max(2, (throughs.Count + 1) / 2)).OrderByDescending(b => b.Times).ThenBy(b => b.Bar)];
            return again is [(_, 2), _, _, ..] && throughs.Count > 2 ? [] : again;
        }

        // A step over the tempo most of it was played at: how it went there.
        private StoryLine? Faster()
        {
            var over = timed.Where(u => u.Run.Tempo > tempo).ToList();
            if (over.Count == 0) return null;

            var top = over.Max(u => u.Run.Tempo);
            var at = over.Where(u => u.Run.Tempo == top).ToList();
            var whole = at.LastOrDefault(u => u.Run.Through);
            return whole is not null
                ? new("Faster", whole.Figure >= PieceStanding.Holds ? Tint.Good : Tint.Plain, $"Through at {Tempo(top)}: {Percent(whole.Figure)} right in time.")
                : new("Faster", Tint.Plain, $"At {Tempo(top)}, {Bars(at[^1].Run.From, at[^1].Last)}: {Percent(at[^1].Figure)} right in time by the end.");
        }

        // ——— Against the time before ———

        // What's new, the whole piece now against the last time it was played through, and what the last session left against
        // the first go at it today.
        private StoryLine? Since()
        {
            if (before.Count == 0) return null;

            var last = before[^1];
            var parts = new List<string>();
            var better = false;
            if (fresh.Count > 0) parts.Add($"{Runs(fresh)} {(fresh.Count == 1 ? "is" : "are")} new");

            // The last time through before today against today's at the same tempo, if there was one (the last of them, before
            // the end went worse); else against the time through today is judged by.
            var then = Played.Of(before, piece.LastBar).LastOrDefault(u => u is { InTime: true, Both: true, Run.Through: true });
            if (then is not null && (Rested(throughs).LastOrDefault(u => u.Run.Tempo == then.Run.Tempo) ?? Reference) is { } now)
            {
                better = now.Run.Tempo > then.Run.Tempo ? now.Figure >= PieceStanding.Holds : now.Run.Tempo == then.Run.Tempo && now.Figure >= then.Figure + 0.02;
                parts.Add(now.Run.Tempo > then.Run.Tempo ? $"the whole piece at {Tempo(now.Run.Tempo)} ({Percent(now.Figure)} right in time), from {Tempo(then.Run.Tempo)} ({Percent(then.Figure)})"
                    : now.Run.Tempo < then.Run.Tempo ? $"the whole piece at {Tempo(now.Run.Tempo)} ({Percent(now.Figure)} right in time); it was {Percent(then.Figure)} at {Tempo(then.Run.Tempo)}"
                    : Percent(now.Figure) == Percent(then.Figure) ? $"the whole piece at {Tempo(now.Run.Tempo)}: {Percent(now.Figure)} right in time, as it was"
                    : $"the whole piece at {Tempo(now.Run.Tempo)}: {Percent(now.Figure)} right in time, from {Percent(then.Figure)}");
            }

            // The last session's stretch played most that was played again, apart from the whole piece: where it was left, and
            // the first go at it today, which is what stayed.
            var left = SessionReview.Of(last.Session is { } id ? before.Where(r => r.Session == id) : before.Where(r => r.Day == last.Day), piece.LastBar).Stretches;
            var again = left.Where(l => !(l.To is null && l.From <= piece.FirstBar && l is { InTime: true, Hands: "both" })).Select(l => (Then: l, Now: review.Stretches.FirstOrDefault(n => n.Same(l))))
                .Where(p => p.Now is not null).OrderByDescending(p => p.Then.Points.Count).FirstOrDefault();
            if (again.Now is { } stretch)
            {
                // What stayed can be told only at the same tempo (or at your own pace).
                var (was, first) = (again.Then.Last, stretch.First);
                if (was.Tempo == first.Tempo)
                {
                    var names = $"{Name(stretch)}{(piece.TwoHands ? stretch.Hands switch { "right" => " (right hand)", "left" => " (left hand)", _ => " (hands together)" } : "")}";
                    var stayed = first.Figure >= was.Figure - 0.05;
                    var back = !stayed && stretch.Points.Skip(1).Any(p => p.Tempo == was.Tempo && p.Figure >= was.Figure - 0.03);
                    parts.Add($"{names} left at {Went(stretch, was)}{(was.Tempo is { } t ? $" at {Tempo(t)}" : "")}, and {Went(stretch, first)} the first go today: {(stayed ? "it stayed" : back ? "some of it went, and came back" : "some of it went")}");
                    better |= stayed;
                }
            }
            return parts.Count == 0 ? null : new(BarWords.Since(last.Day, today), better ? Tint.Good : Tint.Plain, $"{Capital(string.Join("; ", parts.Take(2)))}.");
        }

        // ——— The time ———

        // Where the playing went, by the bars, in a session of four stretches or more whose runs kept how long they took.
        private StoryLine? Minutes()
        {
            if (review.Stretches.Count < 4 || review.Stretches.Any(s => s.Time is null)) return null;

            var by = review.Stretches.GroupBy(Name).Select(g => (Name: g.Key, Minutes: g.Sum(s => s.Time!.Value.TotalMinutes))).Where(g => g.Minutes >= 0.75).OrderByDescending(g => g.Minutes).Take(3).ToList();
            return by.Count == 0 ? null : new("Time", Tint.Plain, $"Most of the playing went on {And([.. by.Select(g => $"{g.Name} ({Math.Max(1, Math.Round(g.Minutes)):0} min)")])}.");
        }

        // Whether the end went worse than what came before, in a session of 25 minutes or more: what was played again in its last
        // three tenths at the same bars, hand, mode and tempo against the last of before, and stretches that got worse as they
        // were gone round late on (not ones just taken a step faster, where the tempo may be why). More of those than of things
        // played again that went better says tired; new bars coming on late doesn't say otherwise.
        private (bool Tired, StoryLine? Line) Tired()
        {
            if (units.Count < 6) return (false, null);

            var (start, end) = (units[0].Run.At, units[^1].Run.At);
            if (end - start < TimeSpan.FromMinutes(25)) return (false, null);

            var cut = start + (end - start) * 0.7;
            var (early, late) = (units.Where(u => u.Run.At <= cut).ToList(), units.Where(u => u.Run.At > cut).ToList());
            static (int, int?, string, bool, double?) Key(Played u) => (u.Run.From, u.Run.To, u.Run.Hands, u.InTime, u.Run.Tempo);
            var again = late.GroupBy(Key).Select(g => (Unit: g.First(), After: g.Average(u => u.Figure), Was: early.Where(e => Key(e) == g.Key).TakeLast(3).Select(e => e.Figure).ToList()))
                .Where(a => a.Was.Count > 0).Select(a => (a.Unit, Was: a.Was.Average(), a.After)).ToList();
            var sinking = segments.Where((s, i) => s.Points.Count >= 3 && s.First.At > cut && s.Gain <= -0.06
                && !(i > 0 && ReferenceEquals(segments[i - 1].Stretch, s.Stretch) && segments[i - 1].Tempo < s.Tempo)).ToList();
            var worse = again.Count(a => a.After <= a.Was - 0.05) + sinking.Count;
            if (worse == 0 || worse <= again.Count(a => a.After >= a.Was + 0.05)) return (false, null);

            lateFrom = cut;
            string Named(PieceRun r) => $"{Name(review.Stretches.First(s => (s.From, s.To, s.Hands, s.InTime) == (r.From, r.To, r.Hands, r.InTime)))}{Hand(r.Hands)}{(r is { InTime: true, Tempo: { } t } ? $" at {Tempo(t)}" : "")}";
            // Each thing once, the biggest drop first: what was played again before what sank as it went.
            List<(string Name, double Drop, string Went)> examples =
            [
                .. again.Where(a => a.After <= a.Was - 0.05).Select(a => (Named(a.Unit.Run), a.Was - a.After, $"{Percent(a.Was)} → {Percent(a.After)}{(a.Unit.InTime ? "" : " right first time")}")),
                .. sinking.Select(s => ($"{Name(s.Stretch)}{Hand(s.Stretch.Hands)}{At(s)}", -s.Gain, $"{Went(s.Stretch, s.First)} → {Went(s.Stretch, s.Last)}{(s.Stretch.InTime ? "" : " right first time")}")),
            ];
            var said = examples.GroupBy(e => e.Name).Select(g => g.First()).OrderByDescending(e => e.Drop).Take(2).Select(e => $"{e.Name}, {e.Went}");
            var minutes = Math.Max(5, Math.Round((end - cut).TotalMinutes / 5) * 5);
            return (true, new("The end", Tint.Plain, $"The last {minutes:0} minutes or so went worse than what came before: {string.Join("; ", said)}. That's more likely tiredness than the bars."));
        }

        // ——— What to do next ———

        private List<NextStep> Steps()
        {
            var steps = new List<NextStep>();
            // Three at most, and no two on the same bars: a loop that overlaps one already there, in the same hand and mode, is it.
            void Add(int from, int? to, string hands, bool inTime, double? at, string why)
            {
                bool Same(NextStep s) => (s.Hands, s.InTime) == (hands, inTime) && (s.To is { } last && to is { } mine ? s.From <= mine && from <= last : (s.From, s.To) == (from, to));
                if (steps.Count < 3 && !steps.Any(Same)) steps.Add(new(from, to, hands, inTime, at, What(from, to, hands, inTime, at), why));
            }
            // The tempo something was last played at In time, hands together, in a bar.
            double? LastAt(int bar) => units.LastOrDefault(u => u.InTime && u.Both && u.Covers(bar))?.Run.Tempo;
            // The bar still going wrong: the one with most to fix of every bar left with a wrong note, as Still hard names it.
            var hard = review.Hards.FirstOrDefault(h => h.Last.NotRight > 0);
            // The last time through; the best of them, when the end of the session went worse.
            var through = Reference;

            switch (work)
            {
                case Practice.Work.Reading:
                {
                    // The bar still going wrong, at your own pace.
                    if (hard is not null)
                    {
                        var (from, to) = Around(hard.Bar);
                        Add(from, to, "both", false, null, $"{Capital(Bar(hard.Bar))} was still going wrong at the end. Slowly enough to get every note the first time.");
                    }
                    // What's been read only a hand at a time, together.
                    var apart = Enumerable.Range(piece.FirstBar, piece.Bars).Where(bar => standing.Bars[bar - piece.FirstBar].State == BarState.Reading
                        && !before.Concat(runs).Any(r => r.Hands == "both" && r.From <= bar && bar <= (r.To ?? (r.Stopped ? r.Bar ?? r.From : piece.LastBar)))).ToList();
                    if (piece.TwoHands && apart.Count > 0)
                    {
                        var to = apart.TakeWhile((bar, i) => bar == apart[0] + i).Take(4).Last();
                        Add(apart[0], to, "both", false, null, $"{Capital(Bars(apart[0], to))} {(to == apart[0] ? "has" : "have")} been read a hand at a time so far.");
                    }
                    // With the click, slowly: what was tried with it today, or the most bars that are right first time together.
                    var slow = piece.Tempo is { } own ? TempoMarks.Near(Math.Max(40, own * 0.45)) : 60;
                    if (Click() is { Stretch.To: not null } clicked)
                    {
                        var at = clicked.Tempo ?? slow;
                        var went = $"{Percent(clicked.End)} right in time at {Tempo(at)}";
                        if (clicked.End >= PieceStanding.Holds) Add(clicked.Stretch.From, clicked.Stretch.To, "both", true, TempoMarks.Up(at), $"{went}: a notch up.");
                        else if (clicked.End >= 0.6) Add(clicked.Stretch.From, clicked.Stretch.To, "both", true, at, $"{went} so far. Over 85%, it goes up a notch.");
                        else Add(clicked.Stretch.From, clicked.Stretch.To, "both", true, TempoMarks.Down(at), $"{went}: a notch slower, or a little longer in Wait for me first.");
                    }
                    else if (review.Stretches.Where(s => s is { InTime: false, Hands: "both", To: not null } && s.Last.Figure >= PieceStanding.Holds).OrderByDescending(s => s.To - s.From).FirstOrDefault() is { } ready)
                    {
                        Add(ready.From, ready.To, "both", true, slow, $"{ready.Last.Right} of {ready.Last.Notes} right first time at your own pace: ready to try with the click, slowly.");
                    }
                    // New ground: the four bars after the furthest read.
                    var furthest = standing.Bars.Where(b => b.State != BarState.NotPlayed).Select(b => b.Bar).DefaultIfEmpty(piece.FirstBar - 1).Max();
                    if (furthest < piece.LastBar)
                    {
                        Add(furthest + 1, Math.Min(piece.LastBar, furthest + 4), piece.TwoHands ? "right" : "both", false, null, $"New: nothing past {Bar(furthest)} has been read yet.{(piece.TwoHands ? " A hand at a time first." : "")}");
                    }
                    break;
                }
                case Practice.Work.Securing:
                {
                    // The bar still going wrong, a notch slower than it went wrong at.
                    if (hard is not null)
                    {
                        var (from, to) = Around(hard.Bar);
                        if (LastAt(hard.Bar) is { } at) Add(from, to, "both", true, TempoMarks.Down(at), $"{Capital(Bar(hard.Bar))} was still going wrong at the end, at {Tempo(at)}. A notch slower, until it's clean three times running.");
                        else Add(from, to, "both", false, null, $"{Capital(Bar(hard.Bar))} was still going wrong at the end.");
                    }
                    // What went too fast, back where it held; or what didn't move, a notch slower.
                    if (Fast() is { } fast && fast.Faster.Stretch.To is not null)
                    {
                        var held = fast.Slower.End >= PieceStanding.Holds;
                        Add(fast.Faster.Stretch.From, fast.Faster.Stretch.To, fast.Faster.Stretch.Hands, true, held ? fast.Slower.Tempo : TempoMarks.Down(fast.Slower.Tempo ?? 60),
                            held ? $"It dropped to {Percent(fast.Faster.End)} at {Tempo(fast.Faster.Tempo)}: {Tempo(fast.Slower.Tempo)} is where it holds for now."
                                : $"It dropped to {Percent(fast.Faster.End)} at {Tempo(fast.Faster.Tempo)}, and was {Percent(fast.Slower.End)} at {Tempo(fast.Slower.Tempo)}: a notch under both.");
                    }
                    else if (Stuck(null) is { } stuck && stuck.Segment.Stretch.To is not null)
                    {
                        Add(stuck.Segment.Stretch.From, stuck.Segment.Stretch.To, stuck.Segment.Stretch.Hands, true, TempoMarks.Down(stuck.Segment.Tempo ?? 60),
                            $"It stayed around {Percent(stuck.Segment.Points.Average(p => p.Figure))} at {Tempo(stuck.Segment.Tempo)}. Slower moves it more than more passes do.");
                    }
                    Whole(over: 0.9);
                    break;
                }
                case Practice.Work.Shaping:
                {
                    // Where the timing goes out: two notches slower, then back up.
                    var off = Where(t => t.Off);
                    if (off.Count > 0 && tempo is { } now && timed.Sum(u => u.Trouble.Sum(t => t.Off)) > 0.03 * timed.Sum(u => u.Right))
                    {
                        // The worst of those bars, with the ones beside it that go out too (a bar either side, if it's alone).
                        var worst = Enumerable.Range(piece.FirstBar, piece.Bars).Where(off.Contains).MaxBy(bar => timed.Sum(u => u.In(bar)?.Off ?? 0) / (double)Math.Max(1, timed.Count(u => u.Covers(bar))));
                        var (from, to) = (worst, worst);
                        while (off.Contains(from - 1)) from--;
                        while (off.Contains(to + 1)) to++;
                        if (from == to) (from, to) = Around(worst);
                        var ahead = timed.Sum(u => u.Trouble.Sum(t => t.Ahead)) >= 0.65 * timed.Sum(u => u.Trouble.Sum(t => t.Off));
                        Add(from, to, "both", true, TempoMarks.Down(TempoMarks.Down(now)), $"{(ahead ? "It runs ahead of the click here" : "The timing goes out here")}. In time three passes running, then back up a notch at a time.");
                    }
                    // A marking that hasn't come out yet: its bars.
                    if (Marks().Values.Where(m => m.Off > 0 && m.What is Marking.Dynamic or Marking.Swell).OrderBy(m => m.What == Marking.Swell ? 0 : 1).ThenBy(m => m.From).FirstOrDefault() is { } unmet && tempo is { } going)
                    {
                        var (from, to) = unmet.From == unmet.To || unmet.What == Marking.Dynamic ? Around(unmet.From) : (unmet.From, unmet.To);
                        Add(from, to, "both", true, going, unmet.What == Marking.Swell ? $"The {unmet.Name} in {Bars(unmet.From, unmet.To)} hasn't come out yet." : $"The {unmet.Name} at {Bar(unmet.From)} hasn't come out yet: no different from what's before it.");
                    }
                    if (hard is not null && LastAt(hard.Bar) is { } slipped)
                    {
                        var (from, to) = Around(hard.Bar);
                        Add(from, to, "both", true, TempoMarks.Down(slipped), $"{Capital(Bar(hard.Bar))} still had a wrong note at the end.");
                    }
                    Whole(over: 0.9);
                    break;
                }
                default:
                {
                    // The bar that slips more than once.
                    if (Again() is [var (bar, times), ..] && tempo is { } now)
                    {
                        var (from, to) = Around(bar);
                        Add(from, to, "both", true, now, $"{Capital(Bar(bar))} had a note wrong {Often(times)}. Clean five passes running.");
                    }
                    if (through is not null)
                    {
                        Add(piece.FirstBar, null, "both", true, tempo ?? through.Run.Tempo, "Once through, first thing next time, before anything else: the cold one is the one that tells. Play has nothing on the music.");
                    }
                    break;
                }
            }
            return steps;

            // The whole piece next: a notch up over nine in ten right in time, a notch down under eight, else the same again, first
            // thing; or, not played through today, once through at the tempo most of it was played at.
            void Whole(double over)
            {
                if (through is null)
                {
                    if (tempo is { } now) Add(piece.FirstBar, null, "both", true, now, "Not played through today: once through shows which bars to work on next.");
                    return;
                }
                var at = through.Run.Tempo ?? 60;
                if (through.Figure >= over) Add(piece.FirstBar, null, "both", true, TempoMarks.Up(at), $"{Percent(through.Figure)} right in time at {Tempo(at)}: over 90%, a notch up.");
                else if (through.Figure >= 0.8) Add(piece.FirstBar, null, "both", true, at, $"{Percent(through.Figure)} right in time at {Tempo(at)} today. First thing next time, before anything else: the first try of the day is the honest one.");
                else Add(piece.FirstBar, null, "both", true, TempoMarks.Down(at), $"{Percent(through.Figure)} right in time at {Tempo(at)}: under 80%, a notch down.");
            }
        }
    }
}
