namespace Prima.Core.Timing;

/// <summary>
/// The jitter of the USB and MIDI path, from Active Sensing: the piano's firmware sends it at a fixed interval, so how much the gaps
/// between arrivals vary is roughly how much the path adds to any note's timing (the plan's idea, untested; it can't see the
/// keyboard's own scanning). Kept as the noise floor, under which timing differences are shown as the keyboard's noise.
/// </summary>
public sealed class SensingJitter
{
    /// <summary>Arrivals needed before the figure is shown: about a minute at Yamaha's 300 ms.</summary>
    public const int Enough = 200;

    private const int Kept = 600;
    private readonly Queue<long> arrivals = new();
    private readonly Lock gate = new();

    /// <summary>Arrivals so far, up to the number kept.</summary>
    public int Count
    {
        get { lock (gate) return arrivals.Count; }
    }

    public void Add(long timeNs)
    {
        lock (gate)
        {
            arrivals.Enqueue(timeNs);
            if (arrivals.Count > Kept) arrivals.Dequeue();
        }
    }

    public void Clear()
    {
        lock (gate) arrivals.Clear();
    }

    /// <summary>
    /// The spread (standard deviation) of the gaps, in ms, once there are enough; null before. A gap far from the usual one (a
    /// message lost, or the piano unplugged a moment) isn't jitter and is left out.
    /// </summary>
    public double? JitterMs
    {
        get
        {
            long[] times;
            lock (gate) times = [.. arrivals];
            if (times.Length < Enough) return null;

            var gaps = times.Zip(times.Skip(1), (a, b) => (b - a) / 1e6).ToArray();
            var usual = Stats.Median(gaps);
            var kept = gaps.Where(g => g > usual * 0.5 && g < usual * 1.5).ToArray();
            return kept.Length < Enough / 2 ? null : Stats.StandardDeviation(kept);
        }
    }
}
