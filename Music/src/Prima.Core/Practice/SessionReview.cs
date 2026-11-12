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

/// <summary>A bar that kept going wrong: in how many of the runs and passes that played it, and what went wrong most there.</summary>
public sealed record StillHard(int Bar, int Times, int Of, TroubleKind Mostly);

public enum TroubleKind
{
    Wrong,
    Missed,
    Off,
    Early,
}

/// <summary>
/// A session's runs summed up, for its look back and its in-depth review: its stretches, the bar that kept going wrong, what went
/// wrong in each bar over every run and pass, the notes right of those played, the share right in time of those played In time,
/// and the notes let go early. Round a loop each pass counts, not the run.
/// </summary>
public sealed record SessionReview(IReadOnlyList<Stretch> Stretches, StillHard? Hard, IReadOnlyDictionary<int, BarTrouble> Bars, int Right, int Notes, double? RightInTime, int Early)
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
        foreach (var t in units.SelectMany(u => u.Trouble))
        {
            var had = bars.GetValueOrDefault(t.Bar) ?? new BarTrouble(t.Bar, 0, 0, 0, 0);
            bars[t.Bar] = new BarTrouble(t.Bar, had.Wrong + t.Wrong, had.Missed + t.Missed, had.Off + t.Off, had.Early + t.Early);
        }

        // The bar that went wrong in the most runs and passes, at least twice: of as many, the one played least, then the earlier.
        var hard = bars.Keys.Select(bar =>
        {
            var times = units.Count(u => u.Trouble.Any(t => t.Bar == bar && t.Weight > 0));
            var of = units.Count(u => u.Run.From <= bar && bar <= u.Last);
            var sums = bars[bar];
            TroubleKind[] kinds = [TroubleKind.Wrong, TroubleKind.Missed, TroubleKind.Off, TroubleKind.Early];
            int[] counts = [sums.Wrong, sums.Missed, sums.Off, sums.Early];
            return new StillHard(bar, times, Math.Max(of, times), kinds[Array.IndexOf(counts, counts.Max())]);
        }).Where(h => h.Times >= 2).OrderByDescending(h => h.Times).ThenBy(h => h.Of).ThenBy(h => h.Bar).FirstOrDefault();

        var timed = units.Where(u => u.Run.InTime && u.RightInTime is not null).ToList();
        double? inTime = timed.Sum(u => u.Notes) is > 0 and var played ? timed.Sum(u => u.RightInTime!.Value * u.Notes) / played : null;
        return new(stretches, hard, bars, units.Sum(u => u.Right), units.Sum(u => u.Notes), inTime, bars.Values.Sum(b => b.Early));
    }
}
