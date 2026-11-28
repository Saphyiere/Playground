using Prima.Core.Practice;

namespace Prima.Core.Tests;

public class CleanStreakTests
{
    private static PiecePass Pass(int right, int notes, int extra = 0, params BarTrouble[] trouble) => new(right, notes, 0.9) { Extra = extra, Trouble = trouble };

    [Fact]
    public void CleanTimesRoundCountInARowAndASpoiledOnePutsThemBackToNone()
    {
        var streak = new CleanStreak(3);

        streak.Add(true);
        streak.Add(true);
        Assert.Equal((2, false), (streak.InARow, streak.Done));
        streak.Add(false);
        Assert.Equal(0, streak.InARow);
        foreach (var _ in Enumerable.Range(0, 3)) streak.Add(true);

        Assert.True(streak.Done);
        Assert.Equal((5, 1, 6), (streak.Clean, streak.Spoiled, streak.Times));
    }

    [Fact]
    public void EverySecondSpoiledInARowAsksForANotchSlower()
    {
        var streak = new CleanStreak(7);

        // The first spoiled doesn't; the second does; then it counts afresh, and a clean one between starts it over.
        Assert.Equal([false, true, false, true], new[] { false, false, false, false }.Select(streak.Add));
        Assert.False(streak.Add(false));
        Assert.False(streak.Add(true));
        Assert.False(streak.Add(false));
        Assert.True(streak.Add(false));
    }

    [Fact]
    public void TenSpoiledInAllLetsTheSpotGo()
    {
        var streak = new CleanStreak(7);

        foreach (var clean in new[] { false, true, false, false, true, true, false, false, false, false, false, false })
        {
            Assert.False(streak.LetGo);
            streak.Add(clean);
        }

        Assert.Equal(9, streak.Spoiled);
        streak.Add(false);
        Assert.True(streak.LetGo);
        Assert.False(streak.Done);
    }

    [Fact]
    public void ATimeRoundIsCleanWithEveryNoteRightNothingExtraAndWithTimingTooNoneOffTheBeat()
    {
        Assert.True(CleanStreak.IsClean(Pass(12, 12), timing: true));
        // A slip put right a moment after still counts: the note wasn't right first time.
        Assert.False(CleanStreak.IsClean(Pass(11, 12, 0, new BarTrouble(14, 0, 0, 0, 0, Slips: 1)), timing: false));
        Assert.False(CleanStreak.IsClean(Pass(12, 12, extra: 1), timing: false));
        // A chord played a key at a time, in Wait for me.
        Assert.False(CleanStreak.IsClean(Pass(12, 12, 0, new BarTrouble(14, 0, 0, 0, 0, Apart: 1)), timing: false));
        // Off the beat spoils it only with Timing too.
        var late = Pass(12, 12, 0, new BarTrouble(14, 0, 0, 2, 0));
        Assert.True(CleanStreak.IsClean(late, timing: false));
        Assert.False(CleanStreak.IsClean(late, timing: true));
        Assert.False(CleanStreak.IsClean(Pass(0, 0), timing: false));
    }

    [Fact]
    public void ASlipLocksItsBarWithOneEitherSideNeverOutsideTheLoopOrThePiece()
    {
        Assert.Equal((13, 15), Slips.Spot(14, 1, 32));
        Assert.Equal((13, 14), Slips.Spot(13, 13, 16));
        Assert.Equal((15, 16), Slips.Spot(16, 13, 16));
        Assert.Equal((0, 2), Slips.Spot(1, 0, 32));
        Assert.Equal((5, 5), Slips.Spot(5, 5, 5));
        // Back a bar: the bar before, never before the loop's first.
        Assert.Equal(13, Slips.Back(14, 1));
        Assert.Equal(13, Slips.Back(13, 13));
    }

    [Fact]
    public void ALockedSpotGoesSlowerAfterTwoSpoiledAndIsDoneAtItsCleanTimesInARow()
    {
        var spot = new LockedSpot(13, 15, 7, 76);

        Assert.Equal(LockStep.Again, spot.Round(false));
        Assert.Equal(LockStep.Slower, spot.Round(false));
        Assert.Equal(72, spot.Tempo);
        foreach (var _ in Enumerable.Range(0, 6)) Assert.Equal(LockStep.Again, spot.Round(true));
        Assert.Equal(LockStep.Done, spot.Round(true));
        Assert.Equal((76, 72), (spot.StartTempo, spot.Tempo));
    }

    // A spot or a loop counted to its clean times round in a row, kept: its bars, tempo, times round (a pass of one note is a
    // slip that cut the time round short) and how it ended.
    private static PieceRun Locked(int minute, int from, int to, double tempo, RunLock locked, params PiecePass[] passes) =>
        new(new DateTimeOffset(2026, 11, 28, 18, minute, 0, TimeSpan.Zero), new DateOnly(2026, 11, 28), true, tempo, "both", from, true, to, passes[^1].Right, passes[^1].Notes, passes[^1].RightInTime)
        { To = to, Passes = passes, Lock = locked };

    private static PiecePass Slip(int bar) => new(0, 1, 0) { Trouble = [new BarTrouble(bar, 1, 0, 0, 0)] };

    private static PiecePass Clean() => new(12, 12, 1);

    [Fact]
    public void TheLookBackSaysHowEachSpotLockedInEnded()
    {
        var through = new PieceRun(new DateTimeOffset(2026, 11, 28, 18, 0, 0, TimeSpan.Zero), new DateOnly(2026, 11, 28), true, 76, "both", 1, false, null, 95, 100, 0.95)
        {
            Trouble = [new BarTrouble(9, 2, 0, 0, 0)],
        };
        var runs = new[]
        {
            through,
            Locked(2, 8, 10, 72, new RunLock(3, "done"), Slip(9), Slip(8), Clean(), Clean(), Clean()),
            Locked(5, 20, 22, 63, new RunLock(7, "let go"), [.. Enumerable.Range(0, 10).Select(_ => Slip(21))]),
            Locked(9, 13, 16, 76, new RunLock(7, "stopped"), Slip(14), Clean(), Clean()),
            Locked(12, 9, 10, 63, new RunLock(2, "done"), Clean(), Clean()),
        };

        var session = QuickReview.Session(runs, new PieceShape(1, 32, null));

        Assert.Equal(
        [
            "Bars 8–10: 3 clean in a row at ♩ 72, after 2 that went wrong.",
            "Bars 20–22 let go after 10 times round that went wrong at ♩ 63: still to fix.",
            "Bars 13–16: 2 clean in a row of 7 at ♩ 76 when you stopped.",
            "Bars 9–10: 2 clean in a row at ♩ 63, none going wrong.",
        ], session.Locks.Select(s => s.ToString()));
        Assert.Equal(Said.Place, session.Locks[0].Parts[0].As);
        Assert.Equal("Locked in: 7 clean in a row at ♩ 76.", QuickReview.Locked(7, 76).ToString());
        Assert.Equal("Done: 2 clean in a row at ♩ 63.", QuickReview.Locked(2, 63, "Done").ToString());
    }

    [Fact]
    public void ALockedSpotSpoiledTenTimesIsLetGo()
    {
        var spot = new LockedSpot(13, 15, 3, 76);

        var steps = Enumerable.Range(0, 10).Select(_ => spot.Round(false)).ToList();

        Assert.Equal(LockStep.LetGo, steps[^1]);
        Assert.Equal(4, steps.Count(s => s == LockStep.Slower));
        // Four notches under 76.
        Assert.Equal(63, spot.Tempo);
    }
}
