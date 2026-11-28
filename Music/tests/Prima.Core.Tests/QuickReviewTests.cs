using Prima.Core.Practice;

namespace Prima.Core.Tests;

public class QuickReviewTests
{
    private static readonly DateTimeOffset Start = new(2026, 11, 28, 18, 0, 0, TimeSpan.Zero);

    // A bar's trouble: wrong notes, and right notes off the beat (with how many of those were early).
    private static BarTrouble Wrong(int bar, int wrong, int off = 0, int ahead = 0) => new(bar, wrong, 0, off, 0, Ahead: ahead);

    private static QuickRun Run(params BarTrouble[] trouble) => QuickReview.Run(trouble, true, 1, 32, 1, 32, false, 76);

    [Fact]
    public void OnePlaceWithMostOfTheMistakesIsNamedAndLooped()
    {
        var run = Run(Wrong(3, 1), Wrong(17, 1), Wrong(21, 7), Wrong(22, 6));

        Assert.Equal(RunFinding.Place, run.Finding);
        Assert.Equal("Bars 21–22 need help: 13 wrong notes. 2 more mistakes elsewhere.", run.Said.ToString());
        Assert.Equal((21, 22), run.Loop);
        Assert.Equal([21, 22], run.Named);
        Assert.Equal((15, 0), (run.Notes, run.Beat));
        // The place and the counts are told apart for drawing: the place, then the count of wrong notes in its colour.
        Assert.Equal([Said.Place, Said.Plain, Said.Notes, Said.Plain], run.Said.Parts.Select(p => p.As));
        Assert.Equal(32, run.Bars.Count);
        Assert.Equal(7, run.Bars[20]!.Notes);
    }

    [Fact]
    public void RightNotesOffTheBeatAreTimingAndMostlyEarlyIsRushing()
    {
        var run = Run(Wrong(25, 0, 3, 3), Wrong(26, 0, 5, 5), Wrong(27, 0, 5, 5), Wrong(28, 0, 5, 5));

        Assert.Equal("Bars 25–28 need help: every note right, but 18 off the beat: you rushed.", run.Said.ToString());
        Assert.Equal((0, 18), (run.Notes, run.Beat));
        Assert.Contains(run.Said.Parts, p => p is { As: Said.Beat, Text: "18 off the beat" });
    }

    [Fact]
    public void APlaceWithBothKindsSaysBoth()
    {
        var run = Run(Wrong(3, 1), Wrong(9, 6, 4), Wrong(10, 4, 1), Wrong(11, 3, 5), Wrong(12, 6, 3));

        // Bars 9 and 10 alone hold 15 of the four bars' 32 mistakes: the place is all four.
        Assert.Equal("Bars 9–12 need help: 19 wrong notes and 13 off the beat. 1 more mistake elsewhere.", run.Said.ToString());
        Assert.Equal((9, 12), run.Loop);
    }

    [Fact]
    public void AStretchOfHalfThePieceIsNamedAsThatHalf()
    {
        var run = Run([.. Enumerable.Range(17, 16).Select(bar => Wrong(bar, bar % 3 == 0 ? 0 : 3, bar % 2 == 0 ? 3 : 0))]);

        Assert.Equal(RunFinding.Stretch, run.Finding);
        Assert.StartsWith("Bars 17–32, the second half: all the run's mistakes, ", run.Said.ToString());
        Assert.EndsWith(". The first half was clean.", run.Said.ToString());
        Assert.Equal((17, 32), run.Loop);
    }

    [Fact]
    public void MistakesAllThroughGoAgainTwoNotchesSlower()
    {
        var run = Run([.. Enumerable.Range(1, 32).Select(bar => Wrong(bar, 1 + bar % 3, bar % 2))]);

        Assert.Equal(RunFinding.AllThrough, run.Finding);
        Assert.Equal($"Mistakes all through: {run.Notes} wrong notes and {run.Beat} off the beat, in 32 of the 32 bars. Try it slower, ♩ 69 instead of 76, or a hand at a time.", run.Said.ToString());
        Assert.Equal(69, run.Again);
        Assert.Null(run.Loop);
        Assert.Empty(run.Named);
    }

    [Fact]
    public void AStopNamesWhereAndWhatWentWrongBeforeIt()
    {
        var run = QuickReview.Run([Wrong(3, 1), Wrong(12, 6, 2, 2), Wrong(13, 5, 2, 1)], true, 1, 32, 1, 13, true, 76);

        Assert.Equal(RunFinding.Stopped, run.Finding);
        Assert.Equal("You stopped at bar 14, after 11 wrong notes and 4 off the beat in bars 12–13. Before that, 1 mistake.", run.Said.ToString());
        Assert.Equal((12, 14), run.Loop);
        Assert.Equal(76, run.Again);
        // Bars from the one it stopped at weren't played.
        Assert.Null(run.Bars[13]);
        Assert.NotNull(run.Bars[12]);
    }

    [Fact]
    public void ACleanRunGoesANotchFaster()
    {
        var run = Run();

        Assert.Equal("Clean: every note right and on the beat. Try it a notch faster, ♩ 80 instead of 76.", run.Said.ToString());
        Assert.Equal(80, run.Again);
    }

    [Fact]
    public void AFewSlipsAreNoOnePlace()
    {
        var run = Run(Wrong(4, 1), Wrong(20, 1));

        Assert.Equal(RunFinding.Here, run.Finding);
        Assert.Equal("A slip here and there, no one place: 2 wrong notes.", run.Said.ToString());
        Assert.Null(run.Loop);
    }

    [Fact]
    public void ARunFromABarDidntPlayTheBarsBefore()
    {
        var run = QuickReview.Run([Wrong(21, 4)], true, 1, 32, 17, 32, false, 76);

        Assert.All(run.Bars.Take(16), Assert.Null);
        Assert.Equal("Bar 21 needs help: 4 wrong notes.", run.Said.ToString());
    }

    private static PiecePass Round(params BarTrouble[] trouble) => new(20, 24, 0.8) { Trouble = trouble };

    [Fact]
    public void ALoopGettingBetterNamesTheBarThatCameRightAndTheOneStillWrong()
    {
        PiecePass[] rounds = [Round(Wrong(21, 5), Wrong(22, 5)), Round(Wrong(21, 7), Wrong(22, 3)), Round(Wrong(21, 6), Wrong(22, 5)),
            Round(Wrong(21, 4)), Round(Wrong(21, 6)), Round(Wrong(21, 5))];
        var loop = QuickReview.Loop(rounds, 21, 24, 76, true);

        Assert.Equal(LoopTrend.Better, loop.Trend);
        Assert.Equal([10, 10, 11, 4, 6, 5], loop.Totals);
        Assert.Equal("Getting better: 10 mistakes the 1st time round, 5 the 6th. Bar 22 came right; bar 21 still goes wrong.", loop.Said.ToString());
        // Each bar each time round, and each time round's counts add up to the number over its column.
        Assert.Equal([5, 3, 5, 0, 0, 0], loop.Grid[1]);
        Assert.Equal(loop.Totals, Enumerable.Range(0, 6).Select(r => loop.Grid.Sum(bar => bar[r])));
        Assert.Equal((21, 21), loop.Narrow);
        Assert.Equal(76, loop.Again);
    }

    [Fact]
    public void ALoopNoBetterGoesANotchSlower()
    {
        PiecePass[] rounds = [Round(Wrong(21, 3), Wrong(22, 3), Wrong(23, 1)), Round(Wrong(21, 4, 2), Wrong(22, 3, 2)), Round(Wrong(21, 5, 2), Wrong(22, 3, 1)),
            Round(Wrong(21, 5, 2), Wrong(22, 3, 1)), Round(Wrong(21, 3, 2), Wrong(22, 2, 2)), Round(Wrong(21, 4, 1), Wrong(22, 2, 1))];
        var loop = QuickReview.Loop(rounds, 21, 24, 76, true);

        Assert.Equal(LoopTrend.NoBetter, loop.Trend);
        Assert.Equal("No better yet: 7 mistakes the 1st time round, 8 the 6th. Bars 21–22 still go wrong. Slow it down a notch, to ♩ 72.", loop.Said.ToString());
        Assert.Equal([0, 4, 3, 3, 4, 2], loop.Beat);
        Assert.Equal(72, loop.Again);
        Assert.Equal((21, 22), loop.Narrow);
    }

    [Fact]
    public void ALoopCleanByTheEndGoesANotchFaster()
    {
        var loop = QuickReview.Loop([Round(Wrong(21, 3)), Round(Wrong(21, 2)), Round(), Round()], 21, 24, 76, true);

        Assert.Equal("Getting better: 3 mistakes the 1st time round, 0 the 4th. Bar 21 came right. Clean by the end.", loop.Said.ToString());
        Assert.Null(loop.Narrow);
        Assert.Equal(80, loop.Again);
    }

    [Fact]
    public void OnceRoundIsNothingToSetItAgainst()
    {
        var loop = QuickReview.Loop([Round(Wrong(22, 2))], 21, 24, 76, true);

        Assert.Equal(LoopTrend.Once, loop.Trend);
        Assert.Equal("Once round: 2 mistakes.", loop.Said.ToString());
    }

    private static PieceRun Through(int minute, double tempo, double share, int from = 1, params BarTrouble[] trouble) =>
        new(Start.AddMinutes(minute), DateOnly.FromDateTime(Start.Date), true, tempo, "both", from, false, null, 90, 100, share) { Trouble = trouble };

    [Fact]
    public void ASessionNamesItsPlacesFromItsLastRunsAndWhatEachNeeds()
    {
        var loop = new PieceRun(Start.AddMinutes(5), DateOnly.FromDateTime(Start.Date), true, 66, "both", 5, false, null, 23, 24, 1.0)
        {
            To = 8,
            Passes = [new(20, 24, 0.8) { Trouble = [Wrong(7, 2)] }, new(22, 24, 0.9) { Trouble = [Wrong(7, 1)] }, new(24, 24, 1.0)],
        };
        PieceRun[] runs =
        [
            Through(1, 72, 0.81, 1, Wrong(7, 3), Wrong(10, 3), Wrong(21, 4)),
            Through(3, 72, 0.84, 1, Wrong(7, 2), Wrong(10, 1), Wrong(21, 3)),
            loop,
            Through(10, 72, 0.85, 17, Wrong(21, 4), Wrong(22, 0, 1)),
            Through(12, 76, 0.81, 1, Wrong(7, 5, 3), Wrong(21, 6, 2)),
        ];

        var session = QuickReview.Session(runs, new PieceShape(1, 32, 115));

        Assert.Equal("Getting better. Bar 7 still needs help with wrong notes and timing, bar 21 with wrong notes.", session.Said.ToString());
        Assert.Equal([new SessionPlace(7, 7, "wrong notes and timing"), new SessionPlace(21, 21, "wrong notes")], session.Places);
        Assert.Equal("Bar 10 came right. Through the piece: 81% correct and on time at ♩ 72 at first, 81% at ♩ 76 at the end.", session.Sub);
        Assert.Equal((10, 10), session.CameRight);
        Assert.False(session.OpensOnTiming);
        // Next starts from the tempo the session ended at, which the line above says.
        Assert.Equal("Loop bar 7 at ♩ 72, a notch under the ♩ 76 you ended at, until two times round in a row are clean.", session.Next.ToString());
        Assert.Equal(((int, int)?)(7, 7), session.NextLoop);
        Assert.Equal(72, session.NextTempo);
        // The heat map's rows are the four runs through; the one from bar 17 didn't play the bars before it.
        Assert.Equal((4, 1), (session.Runs, session.Loops));
        Assert.Equal([1, 2, 3, 4], session.Rows.Select(r => r.Number));
        Assert.Null(session.Rows[2].Bars[9]);
        Assert.Equal(8, session.Rows[3].Bars[6]!.Both);
        // The timeline: each run, and the loop's times round together.
        Assert.Equal(["Whole", "Whole", "5–8", "17–end", "Whole"], session.Groups.Select(g => g.Label));
        Assert.Equal([0.8, 0.9, 1.0], session.Groups[2].Figures);
    }

    [Fact]
    public void ASessionWithNothingToFixGoesANotchFasterAndOpensOnWhatTheFirstPlaceNeeds()
    {
        var clean = QuickReview.Session([Through(1, 115, 0.98), Through(4, 115, 0.99)], new PieceShape(1, 32, 115));
        Assert.Empty(clean.Places);
        Assert.Equal("Play it at ♩ 116, a notch over the ♩ 115 you ended at.", clean.Next.ToString());
        Assert.Equal(116, clean.NextTempo);

        var timing = QuickReview.Session([Through(1, 115, 0.97, 1, Wrong(21, 0, 2)), Through(4, 115, 0.97, 1, Wrong(21, 0, 2))], new PieceShape(1, 32, 115));
        Assert.Equal("Holding steady. Bar 21 still needs help with timing.", timing.Said.ToString());
        Assert.True(timing.OpensOnTiming);
    }
}
