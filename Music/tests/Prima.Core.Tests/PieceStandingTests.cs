using Prima.Core.Practice;
using Prima.Core.Timing;

namespace Prima.Core.Tests;

/// <summary>Where a piece stands, bar by bar, from the runs kept on it: 8 bars for two hands, eight notes a bar, that goes at 100.</summary>
public class PieceStandingTests
{
    private static readonly DateTimeOffset Start = new(2026, 11, 15, 18, 0, 0, TimeSpan.Zero);
    private static readonly PieceShape Piece = new(1, 8, 100) { Notes = [.. Enumerable.Repeat(8, 8)] };

    private static PieceRun Run(int minute, double? tempo, int right, int notes, double? share, string hands = "both", int from = 1, int? to = null, params BarTrouble[] trouble) =>
        new(Start.AddMinutes(minute), DateOnly.FromDateTime(Start.Date), tempo is not null, tempo, hands, from, to is not null, to, right, notes, share) { To = to, Trouble = trouble };

    private static BarState[] States(PieceStanding standing) => [.. standing.Bars.Select(b => b.State)];

    [Fact]
    public void ABarIsNotPlayedThenBeingReadThenInTimeThenAtTempo()
    {
        var wrong = new BarTrouble(3, 1, 0, 0, 0);
        PieceRun[] runs =
        [
            // Bars 1-2 at the piece's tempo, 3-4 at 60, bar 3 with a wrong note every time; 5-6 only in Wait for me, and the right hand alone.
            Run(1, 100, 16, 16, 1.0, to: 2),
            Run(2, 100, 16, 16, 1.0, to: 2),
            Run(3, 60, 15, 16, 0.9, from: 3, to: 4, trouble: wrong),
            Run(4, 60, 15, 16, 0.9, from: 3, to: 4, trouble: wrong),
            Run(5, null, 14, 16, null, from: 5, to: 6),
            Run(6, 60, 8, 8, 1.0, hands: "right", from: 5, to: 6),
        ];

        var standing = PieceStanding.Of(runs, Piece);

        Assert.Equal([BarState.AtTempo, BarState.AtTempo, BarState.Reading, BarState.InTime, BarState.Reading, BarState.Reading, BarState.NotPlayed, BarState.NotPlayed], States(standing));
        Assert.Equal([100, 100, null, 60, null, null, null, null], standing.Bars.Select(b => b.Tempo));
        Assert.Equal(2, standing.Count(BarState.AtTempo));
        Assert.Equal(3, standing.Count(BarState.Reading));
        // Nothing's been played through.
        Assert.False(standing.Through);
        Assert.Null(standing.Held);
        Assert.Equal(100, standing.Own);
    }

    [Fact]
    public void ABarsNotesAreThereOnceTwoOfItsLastFourTimesWereRightAtTheSlowerOfThoseTempos()
    {
        var wrong = new BarTrouble(1, 2, 0, 0, 0);
        // Bar 1: right at 60, right at 72, then wrong twice at 80: still there, at 60. Then wrong again: three of the last four.
        PieceRun[] runs = [Run(1, 60, 8, 8, 1, to: 1), Run(2, 72, 8, 8, 1, to: 1), Run(3, 80, 6, 8, 0.7, to: 1, trouble: wrong), Run(4, 80, 6, 8, 0.7, to: 1, trouble: wrong)];

        Assert.Equal(new BarStanding(1, BarState.InTime, 60), PieceStanding.Of(runs, Piece).Bars[0]);
        Assert.Equal(new BarStanding(1, BarState.Reading, null), PieceStanding.Of([.. runs, Run(5, 80, 6, 8, 0.7, to: 1, trouble: wrong)], Piece).Bars[0]);
        // One right time of one or two is enough to start with; a lone wrong one isn't.
        Assert.Equal(BarState.InTime, PieceStanding.Of([Run(1, 60, 6, 8, 0.7, to: 1, trouble: wrong), Run(2, 60, 8, 8, 1, to: 1)], Piece).Bars[0].State);
        Assert.Equal(BarState.Reading, PieceStanding.Of([Run(1, 60, 6, 8, 0.7, to: 1, trouble: wrong)], Piece).Bars[0].State);
    }

    [Fact]
    public void ANoteWrongInABarPlayedTwiceThroughARepeatIsStillNineInTenRight()
    {
        // The whole piece through its repeat: 128 notes for 64 written, so each bar was played twice. One wrong of 16 is right
        // enough; three aren't. Without the bars' notes, a bar is right only with none wrong.
        var once = new BarTrouble(2, 1, 0, 0, 0);
        var thrice = new BarTrouble(5, 2, 1, 0, 0);
        PieceRun[] runs = [Run(1, 60, 124, 128, 0.9, trouble: [once, thrice])];

        var standing = PieceStanding.Of(runs, Piece);

        Assert.Equal(BarState.InTime, standing.Bars[1].State);
        Assert.Equal(BarState.Reading, standing.Bars[4].State);
        Assert.Equal(BarState.Reading, PieceStanding.Of(runs, Piece with { Notes = null }).Bars[1].State);
        // It held together, played through: 90% right in time at 60.
        Assert.True(standing.Through);
        Assert.Equal(60, standing.Held);
    }

    [Fact]
    public void WithTheMusicsOrderKnownOnlyTheBarsARepeatTookTwiceCountTheirNotesTwice()
    {
        // Bars 1-4 repeated, then 5-8: 96 notes through. A wrong note in bar 2, played twice, is one of 16, right enough; in bar
        // 6, played once, it's one of 8.
        var piece = Piece with { Order = [1, 2, 3, 4, 1, 2, 3, 4, 5, 6, 7, 8] };
        var wrong2 = new BarTrouble(2, 1, 0, 0, 0);
        PieceRun[] runs = [Run(1, 60, 94, 96, 0.9, trouble: [wrong2, new BarTrouble(6, 1, 0, 0, 0)])];

        var standing = PieceStanding.Of(runs, piece);

        Assert.Equal(BarState.InTime, standing.Bars[1].State);
        Assert.Equal(BarState.Reading, standing.Bars[5].State);
        // Stopped in bar 3 the first time round, bar 2 was played once; stopped there the second time, twice.
        Assert.Equal(BarState.Reading, PieceStanding.Of([Run(1, 60, 19, 20, 0.9, trouble: wrong2) with { Stopped = true, Bar = 3 }], piece).Bars[1].State);
        Assert.Equal(BarState.InTime, PieceStanding.Of([Run(1, 60, 51, 52, 0.9, trouble: wrong2) with { Stopped = true, Bar = 3 }], piece).Bars[1].State);
    }

    [Fact]
    public void ItHasHeldTogetherAtTheFastestTempoPlayedThroughWith85PercentRightInTime()
    {
        PieceRun[] runs = [Run(1, 60, 60, 64, 0.90), Run(2, 72, 58, 64, 0.86), Run(3, 80, 50, 64, 0.70), Run(4, 92, 62, 64, 0.95, from: 5)];

        var standing = PieceStanding.Of(runs, Piece);

        Assert.Equal(72, standing.Held);
        Assert.True(standing.Through);
    }

    [Fact]
    public void ARunStoppedOnTheWayPlayedTheBarsBeforeTheOneItStoppedIn()
    {
        PieceRun[] runs = [Run(1, 60, 30, 32, 0.9) with { Stopped = true, Bar = 5 }];

        Assert.Equal([BarState.InTime, BarState.InTime, BarState.InTime, BarState.InTime, BarState.NotPlayed, BarState.NotPlayed, BarState.NotPlayed, BarState.NotPlayed], States(PieceStanding.Of(runs, Piece)));
    }

    [Fact]
    public void BarsAreNamedByTheirRuns()
    {
        Assert.Equal("bar 21", BarWords.Runs([21]));
        Assert.Equal("bars 5–7", BarWords.Runs([7, 5, 6]));
        Assert.Equal("bars 5–7, 13–14 and 21", BarWords.Runs([5, 6, 7, 13, 14, 21]));
        Assert.Equal("bars 1, 3, 5, 7 and 2 more", BarWords.Runs([1, 3, 5, 7, 9, 11]));
        Assert.Equal("the upbeat and bars 2–3", BarWords.Runs([0, 2, 3]));
        Assert.Equal("the upbeat to bar 2", BarWords.Runs([0, 1, 2]));
        Assert.Equal("the upbeat and bar 4", BarWords.Runs([0, 4]));
        Assert.Equal("no bars", BarWords.Runs([]));
        Assert.Equal("a, b and c", BarWords.And(["a", "b", "c"]));
    }

    [Fact]
    public void ATempoMovesByTheMetronomesMarks()
    {
        Assert.Equal(76, TempoMarks.Up(72));
        Assert.Equal(69, TempoMarks.Down(72));
        Assert.Equal(72, TempoMarks.Up(70));
        Assert.Equal(52, TempoMarks.Near(51.75));
        Assert.Equal(30, TempoMarks.Down(30));
        Assert.Equal(208, TempoMarks.Up(208));
    }
}
