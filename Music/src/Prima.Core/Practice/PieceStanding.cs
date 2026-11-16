namespace Prima.Core.Practice;

/// <summary>
/// A piece as its practice is looked back on: its first and last bars by number (a pickup is bar 0), the tempo it goes at,
/// where its music marks one, in the beats a run's tempo counts, and whether it's for two hands, each on a staff of its own.
/// </summary>
public sealed record PieceShape(int FirstBar, int LastBar, double? Tempo, bool TwoHands = true)
{
    public int Bars => LastBar - FirstBar + 1;

    /// <summary>
    /// The notes written in each bar, from the first, both hands': what a wrong note in a bar is a share of. Where they aren't
    /// known, a bar counts as right only with no note wrong in it.
    /// </summary>
    public IReadOnlyList<int>? Notes { get; init; }
}

/// <summary>How far along a bar is.</summary>
public enum BarState
{
    /// <summary>No run has played it.</summary>
    NotPlayed,
    /// <summary>Played, but not yet right In time with both hands: a hand at a time, in Wait for me, or with wrong notes still.</summary>
    Reading,
    /// <summary>Its notes right In time, both hands, under the piece's own tempo (or the piece marks none).</summary>
    InTime,
    /// <summary>Its notes right In time, both hands, at the piece's own tempo or near it.</summary>
    AtTempo,
}

/// <summary>A bar's standing: how far along it is, and the tempo its notes hold at In time, once they do.</summary>
public sealed record BarStanding(int Bar, BarState State, double? Tempo);

/// <summary>
/// Where a piece stands, from every run kept on it: each bar's standing, the fastest tempo the whole of it has held together at,
/// played through, and whether it's been played through at all. A bar's standing is how its last four times In time with both
/// hands went. A time is right with nine in ten of its notes right (none wrong, missed or slipped, in a bar of fewer than ten,
/// played once); the bar's notes are there once two of the four were (or one of its first two), at the slower of those two
/// tempos; and at tempo when that's within a tenth of the piece's own. So one bad time through at the end of a long session
/// doesn't undo a bar. Timing, touch and loud and soft aren't part of it: this is whether the notes are there.
/// </summary>
public sealed record PieceStanding(IReadOnlyList<BarStanding> Bars, double? Held, double? Own, bool Through = false)
{
    /// <summary>The share right in time at which a run through holds together: the 85% the app aims every level at.</summary>
    public const double Holds = 0.85;

    /// <summary>The share of the piece's own tempo that counts as at tempo.</summary>
    public const double Near = 0.9;

    public int Count(BarState state) => Bars.Count(b => b.State == state);

    public static PieceStanding Of(IEnumerable<PieceRun> runs, PieceShape piece)
    {
        var played = Played.Of(runs, piece.LastBar);
        List<BarStanding> bars = [.. Enumerable.Range(piece.FirstBar, piece.Bars).Select(bar =>
        {
            var plays = played.Where(u => u.Covers(bar)).ToList();
            if (plays.Count == 0) return new BarStanding(bar, BarState.NotPlayed, null);

            var timed = plays.Where(u => u is { InTime: true, Both: true, Run.Tempo: not null }).TakeLast(4).ToList();
            var right = timed.Where(u => Right(u, bar, piece)).Select(u => u.Run.Tempo!.Value).OrderDescending().ToList();
            if (right.Count == 0 || (right.Count == 1 && timed.Count > 2)) return new BarStanding(bar, BarState.Reading, null);

            var tempo = right.Count > 1 ? right[1] : right[0];
            return new BarStanding(bar, piece.Tempo is { } own && tempo >= Near * own ? BarState.AtTempo : BarState.InTime, tempo);
        })];
        var throughs = played.Where(u => u is { InTime: true, Both: true, Run: { Through: true, Tempo: not null } }).ToList();
        var held = throughs.Where(u => u.RightInTime >= Holds).Select(u => u.Run.Tempo!.Value).DefaultIfEmpty(0).Max();
        return new(bars, held > 0 ? held : null, piece.Tempo, throughs.Count > 0);
    }

    // Whether a run or pass played a bar right: nine in ten of its notes, each time through it counted (a run through a repeat
    // plays its bars twice: its notes against its bars' written ones say how often).
    private static bool Right(Played unit, int bar, PieceShape piece)
    {
        var wrong = unit.In(bar)?.NotRight ?? 0;
        if (wrong == 0) return true;
        if (piece.Notes is not { } notes) return false;

        var written = Enumerable.Range(unit.Run.From, Math.Max(0, unit.Last - unit.Run.From + 1)).Sum(b => notes.ElementAtOrDefault(b - piece.FirstBar));
        var times = written == 0 ? 1 : Math.Max(1, (int)Math.Round(unit.Notes / (double)written));
        return wrong <= 0.1 * notes.ElementAtOrDefault(bar - piece.FirstBar) * times;
    }
}
