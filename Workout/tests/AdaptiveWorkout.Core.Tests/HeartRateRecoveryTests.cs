using AdaptiveWorkout.Core;

namespace AdaptiveWorkout.Core.Tests;

public class HeartRateRecoveryTests
{
    private static readonly DateTimeOffset Stop = new(2026, 11, 8, 17, 0, 0, TimeSpan.FromHours(-8));

    // Readings at seconds from the set's stop.
    private static List<HeartRateSample> Readings(params (int Second, int Bpm)[] readings) =>
        [.. readings.Select(r => new HeartRateSample { At = Stop.AddSeconds(r.Second), Bpm = r.Bpm })];

    [Fact]
    public void Since_FallsFromThePeakAroundTheStop_ToTheLatestReading()
    {
        // Still climbing for a few seconds after the stop, then falling.
        var readings = Readings((-30, 128), (-5, 146), (4, 152), (30, 135), (70, 118), (95, 112));

        Assert.Equal(new HeartRateFall(152, 118), HeartRateRecovery.Since(readings, Stop, Stop.AddSeconds(75)));
        Assert.Equal(34, HeartRateRecovery.Since(readings, Stop, Stop.AddSeconds(75))!.Drop);
    }

    [Fact]
    public void Since_IsEmpty_WithoutAReadingAroundTheStop_OrAfterIt()
    {
        Assert.Null(HeartRateRecovery.Since(Readings((-40, 140), (60, 120)), Stop, Stop.AddSeconds(70)));
        Assert.Null(HeartRateRecovery.Since(Readings((-8, 140), (-2, 145)), Stop, Stop.AddSeconds(70)));
    }

    [Fact]
    public void Since_CountsOnlyReadingsUpToNow()
    {
        var readings = Readings((2, 150), (20, 140), (40, 125));

        Assert.Equal(new HeartRateFall(150, 140), HeartRateRecovery.Since(readings, Stop, Stop.AddSeconds(25)));
    }

    [Fact]
    public void FirstMinute_IsThePeakLessTheReadingNearestAMinuteLater()
    {
        var readings = Readings((3, 150), (55, 121), (63, 118), (90, 110));

        Assert.Equal(32, HeartRateRecovery.FirstMinute(readings, Stop));
    }

    [Fact]
    public void FirstMinute_IsEmpty_WhenTheNextSetStartedInsideIt_OrWithoutAReadingThen()
    {
        var readings = Readings((3, 150), (60, 120));

        Assert.Null(HeartRateRecovery.FirstMinute(readings, Stop, Stop.AddSeconds(45)));
        Assert.Equal(30, HeartRateRecovery.FirstMinute(readings, Stop, Stop.AddSeconds(90)));
        Assert.Null(HeartRateRecovery.FirstMinute(Readings((3, 150), (90, 110)), Stop));
    }
}
