using Prima.Core.Music;
using Prima.Core.Timing;

namespace Prima.Core.Practice;

/// <summary>
/// A piece as its practice is looked back on: its first and last bars by number (a pickup is bar 0), the tempo it goes at,
/// where its music marks one, in the beats a run's tempo counts, and whether it's for two hands, each on a staff of its own.
/// </summary>
public sealed record PieceShape(int FirstBar, int LastBar, double? Tempo, bool TwoHands = true)
{
    /// <summary>
    /// A piece's shape from its score: its bars by number, the tempo it marks in its first metre's beats (♩. in 6/8), whether
    /// its notes are on two staves, the notes in each bar and the order its repeats play them in.
    /// </summary>
    public static PieceShape Of(Score score)
    {
        var expected = score.Expected();
        var (first, last) = (score.Bars[0].Number, score.Bars[^1].Number);
        var opening = score.WithSignatures().First().Meter;
        var counts = expected.GroupBy(n => n.Bar).ToDictionary(g => g.Key, g => g.Count());
        return new(first, last, score.MainTempo() is { } quarters ? Math.Round(quarters * Ticks.Quarter / opening.BeatTicks) : null,
            expected.Select(n => n.Staff).Distinct().Order().SequenceEqual([1, 2]))
        {
            Notes = [.. Enumerable.Range(first, last - first + 1).Select(bar => counts.GetValueOrDefault(bar))],
            Order = [.. score.PlayOrder().Select(i => score.Bars[i].Number)],
        };
    }

    public int Bars => LastBar - FirstBar + 1;

    /// <summary>
    /// The notes written in each bar, from the first, both hands': what a wrong note in a bar is a share of. Where they aren't
    /// known, a bar counts as right only with no note wrong in it.
    /// </summary>
    public IReadOnlyList<int>? Notes { get; init; }

    /// <summary>
    /// The bars by number in the order a run from the first to the end plays them: a repeat's twice, the second time by its
    /// second ending. It says which bars a run played twice. Where it isn't known, a run's bars count as played twice only when
    /// its notes are twice theirs.
    /// </summary>
    public IReadOnlyList<int>? Order { get; init; }
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

    // Whether a run or pass played a bar right: nine in ten of its notes, each time it played them counted (a run through a
    // repeat plays its bars twice).
    private static bool Right(Played unit, int bar, PieceShape piece)
    {
        var wrong = unit.In(bar)?.NotRight ?? 0;
        if (wrong == 0) return true;
        if (piece.Notes is not { } notes) return false;

        return wrong <= 0.1 * notes.ElementAtOrDefault(bar - piece.FirstBar) * Times(unit, bar, piece, notes);
    }

    // How many times a run or pass played a bar. A pass round a loop plays each of its bars once. A run plays the bars in the
    // music's order from the one it started from, as far as its notes went, so only the bars a repeat took twice count twice.
    // Where the music's order isn't known, a run's bars count twice only when its notes are twice theirs, as when a repeat takes
    // all of them: which bars a repeat of some of them took can't be told, and a bar played once mustn't be let off a wrong note.
    private static int Times(Played unit, int bar, PieceShape piece, IReadOnlyList<int> notes)
    {
        if (unit.Run.To is not null) return 1;

        int Written(int b) => notes.ElementAtOrDefault(b - piece.FirstBar);
        if (piece.Order is { } order)
        {
            var at = 0;
            while (at < order.Count && order[at] != unit.Run.From) at++;
            var (reached, times) = (0, 0);
            for (; at < order.Count && reached < unit.Notes; at++)
            {
                reached += Written(order[at]);
                if (order[at] == bar) times++;
            }
            return Math.Max(1, times);
        }

        var bars = Enumerable.Range(unit.Run.From, Math.Max(0, unit.Last - unit.Run.From + 1)).ToList();
        var written = bars.Sum(Written);
        return written > 0 && unit.Notes >= 2 * written && bars.Contains(bar) ? 2 : 1;
    }
}
