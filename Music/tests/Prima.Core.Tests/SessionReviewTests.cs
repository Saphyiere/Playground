using Prima.Core.Practice;

namespace Prima.Core.Tests;

public class SessionReviewTests
{
    private static readonly DateTimeOffset Start = new(2026, 11, 12, 18, 0, 0, TimeSpan.Zero);

    private static PieceRun Run(int minute, bool inTime, int from, int? to, int right, int notes, double? inTimeShare, params PiecePass[] passes) =>
        new(Start.AddMinutes(minute), DateOnly.FromDateTime(Start.Date), inTime, inTime ? 72 : null, "both", from, false, null, right, notes, inTimeShare) { To = to, Passes = passes };

    private static PiecePass Pass(int right, double inTime, params BarTrouble[] trouble) => new(right, 23, inTime) { Trouble = trouble };

    [Fact]
    public void ASessionsRunsAreItsStretchesEachPassOrRunAPoint()
    {
        var wrong15 = new BarTrouble(15, 1, 0, 0, 0);
        var runs = new[]
        {
            Run(1, true, 13, 16, 23, 23, 1.0, Pass(18, 0.70, wrong15), Pass(21, 0.85, wrong15, new BarTrouble(13, 0, 0, 1, 0)), Pass(23, 1.0)),
            Run(5, false, 9, 12, 15, 17, null) with { Hands = "right" },
            Run(8, true, 1, null, 80, 95, 0.79) with { Trouble = [new BarTrouble(15, 0, 1, 0, 2)] },
            Run(12, true, 1, null, 88, 95, 0.88),
        };

        var review = SessionReview.Of(runs, 16);

        Assert.Equal([(13, (int?)16), (9, 12), (1, null)], review.Stretches.Select(s => (s.From, s.To)));
        var loop = review.Stretches[0];
        Assert.Equal([0.70, 0.85, 1.0], loop.Points.Select(p => p.Figure));
        Assert.Equal(1.0, loop.Best.Figure);
        Assert.Equal((1, 0), (loop.Runs, loop.Throughs));
        Assert.Equal(15 / 17.0, review.Stretches[1].First.Figure);
        Assert.Equal((2, 2), (review.Stretches[2].Runs, review.Stretches[2].Throughs));
        Assert.Equal([0.79, 0.88], review.Stretches[2].Points.Select(p => p.Figure));

        // Bar 15 went wrong in two passes and a run through, of the three passes and two runs that played it, mostly wrong notes.
        Assert.Equal(new StillHard(15, 3, 5, TroubleKind.Wrong), review.Hard);
        Assert.Equal(new BarTrouble(15, 2, 1, 0, 2), review.Bars[15]);
        Assert.Equal((18 + 21 + 23 + 15 + 80 + 88, 23 * 3 + 17 + 95 * 2), (review.Right, review.Notes));
        Assert.Equal((0.70 + 0.85 + 1.0) * 23 + (0.79 + 0.88) * 95, review.RightInTime!.Value * (23 * 3 + 95 * 2), 6);
        Assert.Equal(2, review.Early);
    }

    [Fact]
    public void ABarWrongOnceIsntStillHard()
    {
        var review = SessionReview.Of([Run(1, true, 1, null, 90, 95, 0.9) with { Trouble = [new BarTrouble(4, 1, 0, 0, 0)] }], 16);

        Assert.Null(review.Hard);
        Assert.Single(review.Stretches);
    }
}
