namespace AdaptiveWorkout.Core;

/// <summary>How far heart rate has fallen since a set stopped: from its peak around the stop to the latest reading.</summary>
public sealed record HeartRateFall(int From, int Now)
{
    /// <summary>Beats a minute fallen since the peak; negative when it's still climbing.</summary>
    public int Drop => From - Now;
}

/// <summary>Heart rate after a set: how far it falls in the rest. Shown as it is, never as a rest to wait for.</summary>
public static class HeartRateRecovery
{
    // Heart rate keeps climbing for a few seconds after a set stops, so its peak is looked for on both sides of the stop.
    public static readonly TimeSpan AroundStop = TimeSpan.FromSeconds(10);

    /// <summary>
    /// The highest reading within 10 seconds of a set's stop, at or before <paramref name="now"/>; null without one.
    /// </summary>
    public static HeartRateSample? Peak(IEnumerable<HeartRateSample> readings, DateTimeOffset stoppedAt, DateTimeOffset now) =>
        readings.Where(r => (r.At - stoppedAt).Duration() <= AroundStop && r.At <= now).MaxBy(r => r.Bpm);

    /// <summary>
    /// The fall since a set stopped: from its peak around the stop (see <see cref="Peak"/>) to the latest reading after the stop, at
    /// or before <paramref name="now"/>. Null without either.
    /// </summary>
    public static HeartRateFall? Since(IEnumerable<HeartRateSample> readings, DateTimeOffset stoppedAt, DateTimeOffset now)
    {
        var all = readings as IReadOnlyCollection<HeartRateSample> ?? [.. readings];
        var latest = all.Where(r => r.At > stoppedAt && r.At <= now).MaxBy(r => r.At);
        return Peak(all, stoppedAt, now) is { } peak && latest is not null ? new HeartRateFall(peak.Bpm, latest.Bpm) : null;
    }

    /// <summary>
    /// How far heart rate fell in the first minute after a set stopped: its peak around the stop less the reading nearest a minute
    /// after it, within 10 seconds. Null without either, or when the next set started inside that minute.
    /// </summary>
    public static int? FirstMinute(IEnumerable<HeartRateSample> readings, DateTimeOffset stoppedAt, DateTimeOffset? nextStart = null)
    {
        var minute = stoppedAt.AddMinutes(1);
        if (nextStart is { } next && next < minute) return null;

        var all = readings as IReadOnlyCollection<HeartRateSample> ?? [.. readings];
        var later = all.Where(r => (r.At - minute).Duration() <= AroundStop).MinBy(r => (r.At - minute).Duration());
        return Peak(all, stoppedAt, stoppedAt + AroundStop) is { } peak && later is not null ? peak.Bpm - later.Bpm : null;
    }
}
