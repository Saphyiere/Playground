namespace Prima.Core.Practice;

/// <summary>
/// A stretch of a piece practiced in a session: its bars (from a bar to the end, or round a loop), the hand and the mode, and each
/// of its runs, or round a loop each pass, as a point. Its runs, and how many of them went through the whole piece.
/// </summary>
public sealed record Stretch(int From, int? To, string Hands, bool InTime, IReadOnlyList<StretchPoint> Points, int Runs, int Throughs)
{
    public StretchPoint First => Points[0];

    public StretchPoint Last => Points[^1];

    /// <summary>The best point: the most right (in time, or first time); of two as good, the later.</summary>
    public StretchPoint Best => Points.OrderByDescending(p => p.Figure).ThenByDescending(p => p.At).First();

    /// <summary>Whether a stretch of another session is the same bars, hand and mode.</summary>
    public bool Same(Stretch other) => (From, To, Hands, InTime) == (other.From, other.To, other.Hands, other.InTime);

    /// <summary>The time spent playing it, where every run of it kept how long it took.</summary>
    public TimeSpan? Time { get; init; }
}

/// <summary>
/// A run, or a pass round a loop, as a point of its stretch: when, how it went (In time the share right in time, Wait for me the
/// share right first time), the notes right of its notes, and In time its tempo.
/// </summary>
public sealed record StretchPoint(DateTimeOffset At, double Figure, int Right, int Notes, double? Tempo)
{
    /// <summary>A run from a bar that was stopped on the way: part of its stretch, so not to be set against the runs that got to the end.</summary>
    public bool Partial { get; init; }
}

/// <summary>
/// A bar with something still to fix at the end: in how many of the runs and passes that played it something went wrong, what
/// went wrong most in its last times played and all that went wrong then, and how many those last times were (the last third of
/// them, the last at least) and in how many of them something went wrong.
/// </summary>
public sealed record StillHard(int Bar, int Times, int Of, TroubleKind Mostly, BarTrouble Last, int Recent = 1, int Bad = 1);

/// <summary>A bar that went wrong at first and not in its last times played: what went wrong its first times, and how many of the last were clean.</summary>
public sealed record FixedBar(int Bar, BarTrouble First, int Clean);

public enum TroubleKind
{
    Wrong,
    Missed,
    Off,
    Early,
    Slips,
    Apart,
}

/// <summary>
/// A run, or round a loop one pass, as it was played: its run, which pass, how it went, what went wrong in which bars, the last
/// bar it played, and the seconds it took where that was kept. What a session's review, a piece's standing and a session's story
/// are all worked out from.
/// </summary>
internal sealed record Played(PieceRun Run, int Pass, int Right, int Notes, double? RightInTime, IReadOnlyList<BarTrouble> Trouble, int Last, double? Seconds)
{
    public bool InTime => Run.InTime;

    /// <summary>Both hands (or all there is, on a piece for one).</summary>
    public bool Both => Run.Hands == "both";

    /// <summary>How it went: In time the share right in time, Wait for me the share right first time.</summary>
    public double Figure => InTime ? RightInTime ?? 0 : Notes == 0 ? 0 : Right / (double)Notes;

    /// <summary>Whether it played a bar: one of its bars, or one that something went wrong in.</summary>
    public bool Covers(int bar) => (Run.From <= bar && bar <= Last) || Trouble.Any(t => t.Bar == bar);

    /// <summary>What went wrong in a bar, or nothing.</summary>
    public BarTrouble? In(int bar) => Trouble.FirstOrDefault(t => t.Bar == bar);

    /// <summary>The share of its right notes that were in time, In time.</summary>
    public double Steady => Right == 0 ? 0 : 1 - Math.Min(Right, Trouble.Sum(t => t.Off)) / (double)Right;

    /// <summary>
    /// Runs as they were played, in order, a loop's passes each by itself. A run stopped on the way played up to the bar before
    /// the one it stopped in (that one too, if it's where it started); one with no notes reached isn't one.
    /// </summary>
    public static List<Played> Of(IEnumerable<PieceRun> runs, int lastBar) => [.. runs.OrderBy(r => r.At).SelectMany(r => r.Passes.Count > 0
        ? r.Passes.Select((p, i) => new Played(r, i, p.Right, p.Notes, p.RightInTime, p.Trouble, r.To ?? r.From, p.Seconds ?? r.Seconds / r.Passes.Count))
        : [new Played(r, 0, r.Right, r.Notes, r.RightInTime, r.Trouble, r.To ?? (r.Stopped ? Math.Max(r.From, (r.Bar ?? r.From) - 1) : lastBar), r.Seconds)]).Where(u => u.Notes > 0)];
}

/// <summary>
/// A session's runs summed up, for its look back and its in-depth review: its stretches, the bars with most still to fix at the end
/// (the first of them the bar still hard) and the bar put right, what went wrong in each bar over every run and pass, the notes
/// right of those played, the share right in time of those played In time, and the notes let go early. Round a loop each pass
/// counts, not the run.
/// </summary>
public sealed record SessionReview(IReadOnlyList<Stretch> Stretches, StillHard? Hard, FixedBar? Fixed, IReadOnlyDictionary<int, BarTrouble> Bars, int Right, int Notes, double? RightInTime, int Early)
{
    /// <summary>The bars with something still to fix at the end, the most first: up to three.</summary>
    public IReadOnlyList<StillHard> Hards { get; init; } = [];

    /// <summary>Its runs summed up, the piece's last bar being <paramref name="lastBar"/>; a run or pass with no notes reached isn't a point.</summary>
    public static SessionReview Of(IEnumerable<PieceRun> runs, int lastBar)
    {
        // Each run, or round a loop each pass: how it went, what went wrong where, and the bars it played.
        var units = Played.Of(runs, lastBar);

        var stretches = units.GroupBy(u => (u.Run.From, u.Run.To, u.Run.Hands, u.Run.InTime)).Select(g =>
        {
            var points = g.Select(u => new StretchPoint(u.Run.At, u.Figure, u.Right, u.Notes, u.InTime ? u.Run.Tempo : null) { Partial = u.Run is { To: null, Stopped: true } }).ToList();
            var played = g.Select(u => u.Run).Distinct().ToList();
            return new Stretch(g.Key.From, g.Key.To, g.Key.Hands, g.Key.InTime, points, played.Count, played.Count(r => r.Through))
            {
                Time = g.All(u => u.Seconds is not null) ? TimeSpan.FromSeconds(g.Sum(u => u.Seconds!.Value)) : null,
            };
        }).ToList();

        var bars = new Dictionary<int, BarTrouble>();
        foreach (var t in units.SelectMany(u => u.Trouble)) bars[t.Bar] = bars.TryGetValue(t.Bar, out var had) ? had + t : t;

        // Each bar that had something wrong, with every run and pass that played it, in order: what went wrong its first third
        // of those times is how it started, its last third (the last time, at least) how it stands now. A wrong or missed note
        // weighs three times anything else, the same rule the in-depth review shades its bars by.
        var standing = bars.Keys.Select(bar =>
        {
            var plays = units.Where(u => u.Covers(bar)).ToList();
            var third = Math.Max(1, plays.Count / 3);
            var none = new BarTrouble(bar, 0, 0, 0, 0);
            var first = plays.Take(third).Select(u => u.In(bar) ?? none).Aggregate(none, (a, b) => a + b);
            var recent = plays.Skip(plays.Count - third).Select(u => u.In(bar) ?? none).ToList();
            var last = recent.Aggregate(none, (a, b) => a + b);
            var times = plays.Count(u => u.In(bar) is { Weight: > 0 });
            return (Bar: bar, Plays: plays.Count, Times: times, Third: third, First: first, Last: last, Bad: recent.Count(t => t.Weight > 0));
        }).ToList();

        // Still hard: the bars with most to fix in their last times, wrong and missed notes first, then the earlier bar. Fixed: the
        // bar with most to fix at first, played three times or more, that was clean in its last times.
        TroubleKind[] kinds = [TroubleKind.Wrong, TroubleKind.Missed, TroubleKind.Off, TroubleKind.Early, TroubleKind.Slips, TroubleKind.Apart];
        List<StillHard> hards = [.. standing.Where(s => s.Last.Weight > 0).OrderByDescending(s => s.Last.Weight).ThenByDescending(s => s.Last.Wrong + s.Last.Missed).ThenBy(s => s.Bar).Take(3).Select(s =>
        {
            int[] counts = [s.Last.Wrong, s.Last.Missed, s.Last.Off, s.Last.Early, s.Last.Slips, s.Last.Apart];
            return new StillHard(s.Bar, s.Times, s.Plays, kinds[Array.IndexOf(counts, counts.Max())], s.Last, s.Third, s.Bad);
        })];
        var putRight = standing.Where(s => s.Plays >= 3 && s.Last.Weight == 0 && s.First.Weight >= 3).OrderByDescending(s => s.First.Weight).ThenBy(s => s.Bar).ToList();
        FixedBar? fixedBar = putRight is [var best, ..] ? new FixedBar(best.Bar, best.First, best.Third) : null;

        var timed = units.Where(u => u.InTime && u.RightInTime is not null).ToList();
        double? inTime = timed.Sum(u => u.Notes) is > 0 and var played ? timed.Sum(u => u.RightInTime!.Value * u.Notes) / played : null;
        return new(stretches, hards.FirstOrDefault(), fixedBar, bars, units.Sum(u => u.Right), units.Sum(u => u.Notes), inTime, bars.Values.Sum(b => b.Early)) { Hards = hards };
    }
}
