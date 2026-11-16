using System.Globalization;

namespace AdaptiveWorkout.Core;

/// <summary>The copies of everything the app keeps (Settings › Your data): what one holds, in a line for the screen.</summary>
public static class DataCopies
{
    /// <summary>
    /// What a copy holds, from its rows: "212 workouts · 3,104 sets · 1,904 food entries · 480 weigh-ins", leaving out what it has
    /// none of, and "Nothing logged yet" when it's none of them.
    /// </summary>
    public static string Describe(long workouts, long sets, long food, long weighIns)
    {
        var parts = new[]
        {
            Count(workouts, "workout", "workouts"),
            Count(sets, "set", "sets"),
            Count(food, "food entry", "food entries"),
            Count(weighIns, "weigh-in", "weigh-ins"),
        }.Where(p => p.Length > 0).ToList();
        return parts.Count == 0 ? "Nothing logged yet" : string.Join(" · ", parts);

        static string Count(long count, string one, string many) =>
            count <= 0 ? "" : $"{count.ToString("N0", CultureInfo.InvariantCulture)} {(count == 1 ? one : many)}";
    }
}
