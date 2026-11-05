namespace Prima.Core.Timing;

/// <summary>
/// The timing calibration: you play any key with the click, and each note's offset from its beat is measured. The median is the
/// timing offset, taken off every note from then on: the click's sound reaching you, the MIDI path, and your own habit of playing a
/// little early, together, since without a hardware loop they can't be told apart. The interquartile range is your tapping spread:
/// mostly you, not the keyboard.
/// </summary>
public static class Calibration
{
    /// <summary>Taps needed for a result.</summary>
    public const int Needed = 8;

    /// <summary>
    /// Measures taps against beats (both in ns on the same clock). Each tap counts against its nearest beat if it's within a third
    /// of a beat of it, and each beat takes only its first tap. Null when fewer than <see cref="Needed"/> taps count.
    /// </summary>
    public static CalibrationResult? Measure(IReadOnlyList<long> beatsNs, IReadOnlyList<long> tapsNs)
    {
        if (beatsNs.Count < 2) return null;

        var beat = (beatsNs[^1] - beatsNs[0]) / (double)(beatsNs.Count - 1);
        var used = new HashSet<int>();
        var offsets = new List<double>();
        foreach (var tap in tapsNs)
        {
            var nearest = Enumerable.Range(0, beatsNs.Count).MinBy(i => Math.Abs(tap - beatsNs[i]));
            var offset = tap - beatsNs[nearest];
            if (Math.Abs(offset) <= beat / 3 && used.Add(nearest)) offsets.Add(offset / 1e6);
        }

        return offsets.Count < Needed ? null : new CalibrationResult(Stats.Median(offsets), Stats.InterquartileRange(offsets), offsets.Count);
    }
}

/// <summary>A calibration's result: the offset to take off every note and the tapping spread, in ms, from this many taps.</summary>
public sealed record CalibrationResult(double OffsetMs, double SpreadMs, int Taps);
