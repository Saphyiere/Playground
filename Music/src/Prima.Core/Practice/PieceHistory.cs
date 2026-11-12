namespace Prima.Core.Practice;

/// <summary>
/// One run on a piece, as it was kept: when, In time (with its tempo) or Wait for me, the hand, the bar it started from, whether
/// it was stopped and the bar it got to, and how it went: the notes right (right first time, in Wait for me) of those it
/// reached, and In time the share of them right and in time, which is the run's headline.
/// </summary>
public sealed record PieceRun(DateTimeOffset At, DateOnly Day, bool InTime, double? Tempo, string Hands, int From, bool Stopped, int? Bar, int Right, int Notes, double? RightInTime)
{
    /// <summary>A loop's last bar, when the run went round a loop.</summary>
    public int? To { get; init; }

    /// <summary>
    /// A loop's passes, in order: each time round that was played to its end. The run's own figures are then the last one's,
    /// which is how the stretch stood when it was left.
    /// </summary>
    public IReadOnlyList<PiecePass> Passes { get; init; } = [];

    /// <summary>The piece from its start to its end: not from a later bar, round a loop, or stopped on the way.</summary>
    public bool Through => From <= 1 && !Stopped && To is null;

    /// <summary>The notes the run reached: round a loop, every pass's.</summary>
    public int Reached => Passes.Count > 0 ? Passes.Sum(p => p.Notes) : Notes;
}

/// <summary>One time round a loop: the notes right (right first time, in Wait for me) of its notes, and In time the share right and in time.</summary>
public sealed record PiecePass(int Right, int Notes, double? RightInTime);

/// <summary>
/// What's been done on a piece, from its runs: how many and on how many days, since when, the last, and of the times it was
/// played through In time how many, the last and the best. A run that reached fewer than <see cref="MinNotes"/> notes is a
/// false start, and isn't counted.
/// </summary>
public sealed record PieceHistory(int Runs, int Days, DateOnly Since, PieceRun Last, int Throughs, PieceRun? LastThrough, PieceRun? BestThrough)
{
    public const int MinNotes = 8;

    /// <summary>A piece's history from its runs, or null when none of them counts.</summary>
    public static PieceHistory? Of(IEnumerable<PieceRun> runs)
    {
        var counted = runs.Where(r => r.Reached >= MinNotes).OrderBy(r => r.At).ToList();
        if (counted.Count == 0) return null;

        var through = counted.Where(r => r.InTime && r.Through).ToList();
        // The best is the one with the most right in time: of two as good, the faster, and then the later.
        var best = through.OrderByDescending(r => r.RightInTime ?? 0).ThenByDescending(r => r.Tempo ?? 0).ThenByDescending(r => r.At).FirstOrDefault();
        return new(counted.Count, counted.Select(r => r.Day).Distinct().Count(), counted[0].Day, counted[^1], through.Count, through.LastOrDefault(), best);
    }
}
