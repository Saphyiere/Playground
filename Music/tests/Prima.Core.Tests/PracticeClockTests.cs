using Prima.Core.Practice;

namespace Prima.Core.Tests;

public class PracticeClockTests
{
    private static readonly DateTimeOffset Start = new(2026, 11, 12, 18, 0, 0, TimeSpan.Zero);

    private static DateTimeOffset At(double seconds) => Start.AddSeconds(seconds);

    [Fact]
    public void EachKeyKeepsYouPlayingTenSecondsAndTheRestIsIdle()
    {
        var clock = new PracticeClock(Start);

        clock.Key(At(0));
        clock.Key(At(5));
        clock.Key(At(30));
        clock.Advance(At(60));

        // Playing from 0 to 15 and from 30 to 40; looking at the music the rest of the minute.
        Assert.Equal(25, clock.Playing.TotalSeconds);
        Assert.Equal(35, clock.Idle.TotalSeconds);
        Assert.Equal(At(30), clock.LastPlayed);
    }

    [Fact]
    public void ARunIsPlayingHoweverLongItsGapsAre()
    {
        var clock = new PracticeClock(Start);

        clock.RunStarted(At(10));
        clock.Advance(At(70));
        clock.RunEnded(At(130));
        clock.Advance(At(150));

        Assert.Equal(130, clock.Playing.TotalSeconds);
        Assert.Equal(20, clock.Idle.TotalSeconds);
    }

    [Fact]
    public void FifteenMinutesWithNothingPlayedPausesItCountingUpToThen()
    {
        var clock = new PracticeClock(Start);
        clock.Key(At(0));

        Assert.False(clock.Advance(At(14 * 60)));
        Assert.True(clock.Advance(At(40 * 60)));
        clock.Advance(At(50 * 60));

        Assert.True(clock.Paused);
        Assert.Equal(10, clock.Playing.TotalSeconds);
        Assert.Equal(15 * 60 - 10, clock.Idle.TotalSeconds);
    }

    [Fact]
    public void CarryingOnOrPlayingAfterAPauseCountsAgainFromThen()
    {
        var clock = new PracticeClock(Start);
        clock.Pause(At(60));
        clock.Advance(At(600));

        clock.Resume(At(600));
        clock.Advance(At(630));
        Assert.Equal(90, clock.Idle.TotalSeconds);

        clock.Pause(At(640));
        clock.Key(At(700));
        clock.Advance(At(705));

        Assert.False(clock.Paused);
        Assert.Equal(5, clock.Playing.TotalSeconds);
        Assert.Equal(100, clock.Idle.TotalSeconds);
    }

    [Fact]
    public void AClockPutBackCarriesOnFromWhatItHad()
    {
        var clock = new PracticeClock(At(0), TimeSpan.FromMinutes(20), TimeSpan.FromMinutes(5), paused: true, lastPlayed: At(-60));

        clock.Advance(At(300));
        Assert.Equal(20, clock.Playing.TotalMinutes);
        Assert.Equal(At(-60), clock.LastPlayed);

        clock.Resume(At(300));
        clock.Key(At(310));
        clock.Advance(At(320));

        Assert.Equal(20 * 60 + 10, clock.Playing.TotalSeconds);
        Assert.Equal(5 * 60 + 10, clock.Idle.TotalSeconds);
    }
}
