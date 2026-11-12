namespace Prima.Core.Practice;

/// <summary>
/// A stretch of a piece practised in a session: its bars (from a bar to the end, or round a loop), the hand and the mode, and each
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
}

/// <summary>
/// A run, or a pass round a loop, as a point of its stretch: when, how it went (In time the share right in time, Wait for me the
/// share right first time), the notes right of its notes, and In time its tempo.
/// </summary>
public sealed record StretchPoint(DateTimeOffset At, double Figure, int Right, int Notes, double? Tempo);

/// <summary>
/// The bar with most still to fix at the end: in how many of the runs and passes that played it something went wrong, what went
/// wrong most in its last times played, and all that went wrong then.
/// </summary>
public sealed record StillHard(int Bar, int Times, int Of, TroubleKind Mostly, BarTrouble Last);

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
/// A session's runs summed up, for its look back and its in-depth review: its stretches, the bar with most still to fix at the end
/// and the bar put right, what went wrong in each bar over every run and pass, the notes right of those played, the share right in
/// time of those played In time, and the notes let go early. Round a loop each pass counts, not the run.
/// </summary>
public sealed record SessionReview(IReadOnlyList<Stretch> Stretches, StillHard? Hard, FixedBar? Fixed, IReadOnlyDictionary<int, BarTrouble> Bars, int Right, int Notes, double? RightInTime, int Early)
{
    /// <summary>Its runs summed up, the piece's last bar being <paramref name="lastBar"/>; a run or pass with no notes reached isn't a point.</summary>
    public static SessionReview Of(IEnumerable<PieceRun> runs, int lastBar)
    {
        var ordered = runs.OrderBy(r => r.At).ToList();
        // Each run, or round a loop each pass: how it went, what went wrong where, and the bars it played.
        var units = ordered.SelectMany(r => r.Passes.Count > 0
            ? r.Passes.Select(p => (Run: r, p.Right, p.Notes, p.RightInTime, p.Trouble, Last: r.To ?? r.From))
            : [(Run: r, r.Right, r.Notes, r.RightInTime, r.Trouble, Last: r.To ?? (r.Stopped ? r.Bar ?? r.From : lastBar))]).Where(u => u.Notes > 0).ToList();

        var stretches = units.GroupBy(u => (u.Run.From, u.Run.To, u.Run.Hands, u.Run.InTime)).Select(g =>
        {
            var points = g.Select(u => new StretchPoint(u.Run.At, u.Run.InTime ? u.RightInTime ?? 0 : u.Right / (double)u.Notes, u.Right, u.Notes, u.Run.InTime ? u.Run.Tempo : null)).ToList();
            var played = g.Select(u => u.Run).Distinct().ToList();
            return new Stretch(g.Key.From, g.Key.To, g.Key.Hands, g.Key.InTime, points, played.Count, played.Count(r => r.Through));
        }).ToList();

        var bars = new Dictionary<int, BarTrouble>();
        foreach (var t in units.SelectMany(u => u.Trouble)) bars[t.Bar] = bars.TryGetValue(t.Bar, out var had) ? had + t : t;

        // Each bar that had something wrong, with every run and pass that played it, in order: what went wrong its first third
        // of those times is how it started, its last third (the last time, at least) how it stands now. A wrong or missed note
        // weighs three times anything else, the same rule the in-depth review shades its bars by.
        var standing = bars.Keys.Select(bar =>
        {
            var plays = units.Where(u => (u.Run.From <= bar && bar <= u.Last) || u.Trouble.Any(t => t.Bar == bar)).ToList();
            var third = Math.Max(1, plays.Count / 3);
            var none = new BarTrouble(bar, 0, 0, 0, 0);
            var first = plays.Take(third).SelectMany(u => u.Trouble.Where(t => t.Bar == bar)).Aggregate(none, (a, b) => a + b);
            var last = plays.Skip(plays.Count - third).SelectMany(u => u.Trouble.Where(t => t.Bar == bar)).Aggregate(none, (a, b) => a + b);
            var times = plays.Count(u => u.Trouble.Any(t => t.Bar == bar && t.Weight > 0));
            return (Bar: bar, Plays: plays.Count, Times: times, Third: third, First: first, Last: last);
        }).ToList();

        // Still hard: the bar with most to fix in its last times, wrong and missed notes first, then the earlier bar. Fixed: the
        // bar with most to fix at first, played three times or more, that was clean in its last times.
        var stillWrong = standing.Where(s => s.Last.Weight > 0);
        var still = stillWrong.OrderByDescending(s => s.Last.Weight).ThenByDescending(s => s.Last.Wrong + s.Last.Missed).ThenBy(s => s.Bar).ToList();
        StillHard? hard = null;
        if (still is [var top, ..])
        {
            TroubleKind[] kinds = [TroubleKind.Wrong, TroubleKind.Missed, TroubleKind.Off, TroubleKind.Early, TroubleKind.Slips, TroubleKind.Apart];
            int[] counts = [top.Last.Wrong, top.Last.Missed, top.Last.Off, top.Last.Early, top.Last.Slips, top.Last.Apart];
            hard = new StillHard(top.Bar, top.Times, top.Plays, kinds[Array.IndexOf(counts, counts.Max())], top.Last);
        }
        var putRight = standing.Where(s => s.Plays >= 3 && s.Last.Weight == 0 && s.First.Weight >= 3).OrderByDescending(s => s.First.Weight).ThenBy(s => s.Bar).ToList();
        FixedBar? fixedBar = putRight is [var best, ..] ? new FixedBar(best.Bar, best.First, best.Third) : null;

        var timed = units.Where(u => u.Run.InTime && u.RightInTime is not null).ToList();
        double? inTime = timed.Sum(u => u.Notes) is > 0 and var played ? timed.Sum(u => u.RightInTime!.Value * u.Notes) / played : null;
        return new(stretches, hard, fixedBar, bars, units.Sum(u => u.Right), units.Sum(u => u.Notes), inTime, bars.Values.Sum(b => b.Early));
    }
}
