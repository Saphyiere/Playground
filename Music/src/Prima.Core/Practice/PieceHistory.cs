namespace Prima.Core.Practice;

/// <summary>
/// One run on a piece, as it was kept: when, In time (with its tempo) or Wait for me, the hand, the bar it started from, whether
/// it was stopped and the bar it got to, and how it went: the notes right (right first time, in Wait for me) of those it
/// reached, and In time the share of them right and in time, which is the run's headline.
/// </summary>
public sealed record PieceRun(DateTimeOffset At, DateOnly Day, bool InTime, double? Tempo, string Hands, int From, bool Stopped, int? Bar, int Right, int Notes, double? RightInTime)
{
    /// <summary>Its place among the runs kept, to find what else was kept with it (its marks).</summary>
    public long Id { get; init; }

    /// <summary>The practice session it was played in; none for a play-through from Play, or a run from before there were sessions.</summary>
    public long? Session { get; init; }

    /// <summary>Played from Play: the piece through, In time, with nothing on the music.</summary>
    public bool Play { get; init; }

    /// <summary>What went wrong in each bar that had something wrong (round a loop, in its last pass).</summary>
    public IReadOnlyList<BarTrouble> Trouble { get; init; } = [];

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

    /// <summary>How long it took, its count-in too, in seconds; unknown for a run kept before 15 November.</summary>
    public double? Seconds { get; init; }

    /// <summary>In time, how it lay against the click (round a loop, its last pass); unknown for a run kept before 15 November.</summary>
    public RunTiming? Timing { get; init; }

    /// <summary>
    /// In time, what the music marks besides its notes (loud and soft, the touch, the pedal) and how each was played (round a
    /// loop, in its last pass); none where the music marks nothing, or for a run kept before 15 November.
    /// </summary>
    public IReadOnlyList<MarkPlayed> Marked { get; init; } = [];

    /// <summary>
    /// The key it was played in, from the key it's written in: none as written. The marks it left on the music are spelled in that
    /// key, so they're shown on the music in it.
    /// </summary>
    public Music.Transposition Transposed { get; init; }

    /// <summary>That key's name, as it was named when played ("D major"); none as written.</summary>
    public string? KeyName { get; init; }
}

/// <summary>One time round a loop: the notes right (right first time, in Wait for me) of its notes, and In time the share right and in time.</summary>
public sealed record PiecePass(int Right, int Notes, double? RightInTime)
{
    /// <summary>What went wrong in each bar of it that had something wrong.</summary>
    public IReadOnlyList<BarTrouble> Trouble { get; init; } = [];

    /// <summary>In Wait for me, the seconds it took at your own pace, from its first key to its last; unknown before 15 November.</summary>
    public double? Seconds { get; init; }

    /// <summary>In time, keys played that weren't any of its notes: they spoil a time round for Lock in. Not kept.</summary>
    public int Extra { get; init; }
}

/// <summary>
/// What went wrong in a bar on a run or a pass: notes played wrong (and not put right at once), missed, played off the beat (and
/// how many of those were early: ahead of it), let go early, wrong but put right with the next key (a slip), and in Wait for me
/// chords played a key at a time.
/// </summary>
public sealed record BarTrouble(int Bar, int Wrong, int Missed, int Off, int Early, int Slips = 0, int Apart = 0, int Ahead = 0)
{
    /// <summary>How much there is to fix, a wrong or missed note counting three times as much as anything else.</summary>
    public int Weight => 3 * (Wrong + Missed) + Off + Early + Slips + Apart;

    /// <summary>The notes that weren't played right: wrong, missed, or wrong and put right at once; in Wait for me, a chord's a key at a time too.</summary>
    public int NotRight => Wrong + Missed + Slips + Apart;

    /// <summary>What went wrong in the same bar on two runs or passes, together.</summary>
    public static BarTrouble operator +(BarTrouble a, BarTrouble b) =>
        new(a.Bar, a.Wrong + b.Wrong, a.Missed + b.Missed, a.Off + b.Off, a.Early + b.Early, a.Slips + b.Slips, a.Apart + b.Apart, a.Ahead + b.Ahead);
}

/// <summary>
/// How a run In time lay against the click: its lean in ms (ahead of it is negative), which is taken out before each note is
/// judged, and how steady it was after that: the spread of its right notes, as a share of the beat.
/// </summary>
public sealed record RunTiming(double LeanMs, double SteadyPercent);

/// <summary>What the music marks besides its notes, as a run is judged on it.</summary>
public enum Marking
{
    /// <summary>A dynamic (<i>p</i>, <i>mf</i>, <i>f</i>…): how loud its stretch is played.</summary>
    Dynamic,
    /// <summary>A crescendo or diminuendo.</summary>
    Swell,
    Staccato,
    Tenuto,
    /// <summary>A slur: its notes joined, none let go before the next.</summary>
    Slur,
    Accent,
    Pedal,
}

/// <summary>
/// A marking as a run played it: what it is, the bars it's over, how many of it were judged there and how many weren't played as
/// marked, and its name where it has one (a dynamic's level, "crescendo").
/// </summary>
public sealed record MarkPlayed(Marking What, int From, int To, int Of, int Off, string? Name = null);

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
