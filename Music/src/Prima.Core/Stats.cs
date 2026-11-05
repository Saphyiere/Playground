namespace Prima.Core;

/// <summary>The few statistics the scoring and timing need, on plain lists.</summary>
public static class Stats
{
    public static double Mean(IReadOnlyList<double> values) => values.Count == 0 ? 0 : values.Sum() / values.Count;

    /// <summary>The middle value (the mean of the two middle ones for an even count); 0 for none.</summary>
    public static double Median(IReadOnlyList<double> values) => Quantile(values, 0.5);

    /// <summary>The value below which a share <paramref name="q"/> of the values fall, interpolating between neighbours; 0 for none.</summary>
    public static double Quantile(IReadOnlyList<double> values, double q)
    {
        if (values.Count == 0) return 0;

        var sorted = values.Order().ToArray();
        var at = q * (sorted.Length - 1);
        var below = (int)Math.Floor(at);
        var above = Math.Min(below + 1, sorted.Length - 1);
        return sorted[below] + (sorted[above] - sorted[below]) * (at - below);
    }

    /// <summary>The spread of the middle half: the 75th percentile minus the 25th.</summary>
    public static double InterquartileRange(IReadOnlyList<double> values) => Quantile(values, 0.75) - Quantile(values, 0.25);

    /// <summary>The sample standard deviation; 0 for fewer than two values.</summary>
    public static double StandardDeviation(IReadOnlyList<double> values)
    {
        if (values.Count < 2) return 0;

        var mean = Mean(values);
        return Math.Sqrt(values.Sum(v => (v - mean) * (v - mean)) / (values.Count - 1));
    }

    /// <summary>The root mean square: the typical size of the values, whatever their sign.</summary>
    public static double RootMeanSquare(IReadOnlyList<double> values) => values.Count == 0 ? 0 : Math.Sqrt(values.Sum(v => v * v) / values.Count);

    /// <summary>The least-squares line through the points: y = intercept + slope · x. A flat line through the mean for fewer than two distinct x.</summary>
    public static (double Intercept, double Slope) Line(IReadOnlyList<double> x, IReadOnlyList<double> y)
    {
        var (mx, my) = (Mean(x), Mean(y));
        var sxx = x.Sum(v => (v - mx) * (v - mx));
        if (sxx == 0) return (my, 0);

        var sxy = x.Select((v, i) => (v - mx) * (y[i] - my)).Sum();
        var slope = sxy / sxx;
        return (my - slope * mx, slope);
    }
}
