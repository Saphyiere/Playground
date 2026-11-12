using Prima.Core.Keyboard;
using Prima.Core.Timing;

namespace Prima.Core.Tests;

public class TimingTests
{
    private const long Ms = 1_000_000;

    [Fact]
    public void AFrameSoundsItsDistanceFromTheTimestampLater()
    {
        // The timestamp says frame 48,000 sounded at 5 s; at 48 kHz, frame 72,000 sounds half a second later.
        Assert.Equal(5_500 * Ms, AudioClock.TimeOf(72_000, 48_000, 5_000 * Ms, 48_000));
        Assert.Equal(4_750 * Ms, AudioClock.TimeOf(36_000, 48_000, 5_000 * Ms, 48_000));
        Assert.Equal(72_000, AudioClock.FrameAt(5_500 * Ms, 48_000, 5_000 * Ms, 48_000));
    }

    [Fact]
    public void SteadyActiveSensingHasNoJitter()
    {
        var jitter = new SensingJitter();
        for (var i = 0; i < SensingJitter.Enough; i++) jitter.Add(i * 300 * Ms);

        Assert.Equal(0, jitter.JitterMs!.Value, 6);
    }

    [Fact]
    public void JitterIsTheSpreadOfTheGaps()
    {
        var jitter = new SensingJitter();
        var random = new Random(7);
        var arrivals = Enumerable.Range(0, 400).Select(i => i * 300.0 + random.NextDouble() * 4 - 2).ToArray();
        foreach (var at in arrivals) jitter.Add((long)(at * Ms));

        var gaps = arrivals.Zip(arrivals.Skip(1), (a, b) => b - a).ToArray();
        Assert.Equal(Stats.StandardDeviation(gaps), jitter.JitterMs!.Value, 2);
    }

    [Fact]
    public void ALostMessageIsNotJitter()
    {
        var jitter = new SensingJitter();
        var at = 0L;
        for (var i = 0; i < SensingJitter.Enough + 1; i++)
        {
            jitter.Add(at);
            at += i == 50 ? 600 * Ms : 300 * Ms;
        }

        Assert.Equal(0, jitter.JitterMs!.Value, 6);
    }

    [Fact]
    public void JitterWaitsForAboutAMinuteOfArrivals()
    {
        var jitter = new SensingJitter();
        for (var i = 0; i < SensingJitter.Enough - 1; i++) jitter.Add(i * 300 * Ms);

        Assert.Null(jitter.JitterMs);
    }

    [Fact]
    public void CalibrationTakesTheMedianOffsetAndTheSpread()
    {
        var beats = Enumerable.Range(0, 17).Select(i => i * 500 * Ms).ToArray();
        // 16 taps: offsets -30..+30 ms in steps of 4, so the median is -2 and the middle half spans 32 ms.
        var offsets = Enumerable.Range(0, 16).Select(i => -30 + i * 4).ToArray();
        var taps = offsets.Select((o, i) => beats[i + 1] + o * Ms).ToArray();

        var result = Calibration.Measure(beats, taps)!;

        Assert.Equal(16, result.Taps);
        Assert.Equal(0, result.OffsetMs, 6);
        Assert.Equal(Stats.InterquartileRange(offsets.Select(o => (double)o).ToArray()), result.SpreadMs, 6);
    }

    [Fact]
    public void CalibrationIgnoresStrayTapsAndSecondTapsOnABeat()
    {
        var beats = Enumerable.Range(0, 12).Select(i => i * 500 * Ms).ToArray();
        var taps = beats.Skip(1).Select(b => b + 20 * Ms).Concat([beats[3] + 250 * Ms, beats[5] + 30 * Ms]).Order().ToArray();

        var result = Calibration.Measure(beats, taps)!;

        Assert.Equal(11, result.Taps);
        Assert.Equal(20, result.OffsetMs, 6);
    }

    [Fact]
    public void CalibrationNeedsEnoughTaps()
    {
        var beats = Enumerable.Range(0, 17).Select(i => i * 500 * Ms).ToArray();

        Assert.Null(Calibration.Measure(beats, beats.Take(Calibration.Needed - 1).ToArray()));
    }

    [Fact]
    public void TheClickSoundsByStageAndAccentsEachBar()
    {
        var throughout = new ClickPlan(120, 3);
        var countIn = throughout with { Stage = ClickStage.CountInOnly };
        var silent = throughout with { Stage = ClickStage.Silent };

        Assert.Equal(500 * Ms, throughout.BeatNs);
        Assert.Equal(3, throughout.FirstMusicBeat);
        Assert.True(throughout.Sounds(10));
        Assert.True(countIn.Sounds(2));
        Assert.False(countIn.Sounds(3));
        Assert.False(silent.Sounds(0));
        Assert.True(throughout.Accent(6));
        Assert.False(throughout.Accent(7));
    }

    [Fact]
    public void AClickWithTheMusicsOwnBeatsCountsInEvenlyThenKeepsToThem()
    {
        // A count-in of three 1 s beats, then the music's: a bar of 1 s, 2 s and 1 s beats, the last of them on into the run's end.
        var plan = new ClickPlan(60, 3) { Music = [new(0, 1, 1e9, true), new(1, 1, 2e9, false), new(2, 1, 1e9, false)] };

        Assert.Equal([0, 1e9, 2e9, 3e9, 4e9, 6e9, 7e9, 8e9], Enumerable.Range(0, 8).Select(plan.Offset));
        Assert.Equal(4.5, plan.BeatAt(5e9));
        Assert.Equal(1.5, plan.BeatAt(1.5e9));
        // A moment of the music in the beat it falls in: half way through its second beat, or half a beat into the count-in's last.
        Assert.Equal(4.5, plan.BeatOf(1.5));
        Assert.Equal(2.5, plan.BeatOf(-0.5));
        Assert.Equal([true, false, false, true, false, false], Enumerable.Range(0, 6).Select(plan.Accent));
    }

    [Theory]
    [InlineData(21, "A0")]
    [InlineData(60, "C4")]
    [InlineData(61, "C♯4")]
    [InlineData(108, "C8")]
    public void KeysAreNamedWithTheirOctave(int midi, string name)
    {
        Assert.Equal(name, NoteName.Of(midi));
    }

    [Fact]
    public void StatsAgreeWithHandWorkedValues()
    {
        double[] values = [1, 2, 3, 4, 100];

        Assert.Equal(3, Stats.Median(values));
        Assert.Equal(2, Stats.InterquartileRange(values));
        Assert.Equal(Math.Sqrt((1 + 4 + 9 + 16 + 10000) / 5.0), Stats.RootMeanSquare(values), 9);
        var (intercept, slope) = Stats.Line([0, 1, 2, 3], [1, 3, 5, 7]);
        Assert.Equal(1, intercept, 9);
        Assert.Equal(2, slope, 9);
    }
}
