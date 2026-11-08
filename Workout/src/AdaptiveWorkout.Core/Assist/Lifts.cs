using System.Globalization;

namespace AdaptiveWorkout.Core.Assist;

/// <summary>
/// A weight exercise's next time as Claude plans it, wherever it's asked (the look back, a question): the checks it must pass, and how
/// it reads on the screen.
/// </summary>
public static class Lifts
{
    /// <summary>
    /// What's wrong with one exercise's planned next time, as sentences for Claude to fix: sets 1 to 10, reps 1 to 50, rest 30 to 300
    /// seconds in steps of 15, a why, and a weight whole <paramref name="step"/>s from one of <paramref name="bases"/>, by at most a
    /// tenth of it or one step. One done at bodyweight stays at 0; with no weights to go from, any weight goes.
    /// </summary>
    /// <param name="bases">The weights it was done at, that the plan moves from.</param>
    /// <param name="basesAre">Where those are from, for the sentence: "used today or in its last times".</param>
    public static IEnumerable<string> Problems(string name, int sets, int reps, double weight, int restSeconds, string why, IReadOnlyCollection<double> bases,
        bool bodyweight, double step, string basesAre)
    {
        if (sets is < 1 or > 10) yield return $"{name}: sets should be 1 to 10, not {sets}.";
        if (reps is < 1 or > 50) yield return $"{name}: reps should be 1 to 50, not {reps}.";
        if (restSeconds is < 30 or > 300 || restSeconds % 15 != 0) yield return $"{name}: rest should be 30 to 300 seconds in steps of 15, not {restSeconds}.";
        if (string.IsNullOrWhiteSpace(why)) yield return $"{name}: why is empty.";
        if (bodyweight)
        {
            if (weight != 0) yield return $"{name} is done at bodyweight: its weight should be 0, not {Number(weight)}.";
        }
        else if (bases.Count > 0 && !bases.Any(b => Near(weight, b, step)))
        {
            yield return $"{name}: {Number(weight)} isn't whole steps of {Number(step)} from a weight {basesAre} " +
                $"({string.Join(", ", bases.Order().Select(Number))}), by at most a tenth of it or one step.";
        }
    }

    /// <summary>"3 × 10 at 105 lb", "3 × 12 at bodyweight", "3 × 8 at -35 lb" (assisted), in the unit <paramref name="before"/>'s sets used.</summary>
    /// <param name="before">The exercise's sets the plan follows; <paramref name="unit"/> is the unit without any.</param>
    public static string Plan(IReadOnlyList<WorkoutSet> before, int sets, int reps, double weight, WeightUnit unit)
    {
        var bodyweight = before.All(s => s.Weight is null) && weight == 0;
        var unitText = (before.Count > 0 ? before[^1].WeightUnit : unit) == WeightUnit.Kilograms ? "kg" : "lb";
        return $"{sets} × {reps} at {(bodyweight ? "bodyweight" : $"{SetSummary.WeightText(weight)} {unitText}")}";
    }

    /// <summary>
    /// How the plan moves from <paramref name="before"/>'s working weight (the one with the most sets; the heavier on a tie): "Up 5",
    /// "Down 5", "Same weight"; at bodyweight, by its reps: "More reps", "Same again". Empty without sets to go from.
    /// </summary>
    public static string Change(IReadOnlyList<WorkoutSet> before, int reps, double weight)
    {
        if (before.Count == 0) return "";
        if (before.All(s => s.Weight is null)) return reps > before.Max(s => s.Reps ?? 0) ? "More reps" : "Same again";

        var working = before.GroupBy(s => s.Weight ?? 0).OrderByDescending(g => g.Count()).ThenByDescending(g => g.Key).First().Key;
        var change = Math.Round(weight - working, 2);
        return change > 0 ? $"Up {SetSummary.WeightText(change)}" : change < 0 ? $"Down {SetSummary.WeightText(-change)}" : "Same weight";
    }

    // Whole steps from the base, and no further than a tenth of it or one step, whichever is more.
    private static bool Near(double weight, double from, double step)
    {
        var change = weight - from;
        var steps = step > 0 ? change / step : 0;
        return Math.Abs(change) <= Math.Max(Math.Abs(from) * 0.1, step) + 1e-9 && Math.Abs(steps - Math.Round(steps)) < 1e-6;
    }

    private static string Number(double value) => value.ToString("0.##", CultureInfo.InvariantCulture);
}
