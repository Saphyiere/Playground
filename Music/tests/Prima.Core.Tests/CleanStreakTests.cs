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
