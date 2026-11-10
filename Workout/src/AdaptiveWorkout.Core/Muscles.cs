namespace AdaptiveWorkout.Core;

/// <summary>The muscles the muscle map counts sets for, top to bottom.</summary>
public enum Muscle
{
    Chest,
    FrontDelts,
    SideDelts,
    RearDelts,
    Biceps,
    Triceps,
    Forearms,
    Lats,
    UpperBack,
    LowerBack,
    Abs,
    Glutes,
    Quads,
    Hamstrings,
    Calves,
}

/// <summary>
/// What an exercise trains: the muscles it's done for (<paramref name="Main"/>) and the ones that help (<paramref name="Helps"/>). Both
/// empty for cardio, like a walk. <paramref name="ByOwner"/> when I set them, so Claude's tagging never changes them.
/// </summary>
public sealed record MuscleTags(long ExerciseId, IReadOnlyList<Muscle> Main, IReadOnlyList<Muscle> Helps, bool ByOwner);

/// <summary>Hard sets a muscle got: 1 for each set of an exercise it's the main muscle of, ½ for each it helps with.</summary>
public sealed record MuscleSets(Muscle Muscle, double Sets);

public static class Muscles
{
    /// <summary>Every muscle, top to bottom.</summary>
    public static readonly IReadOnlyList<Muscle> All = Enum.GetValues<Muscle>();

    /// <summary>Hard sets a week where growth is well served: about 10 to 20, with less to gain past 20.</summary>
    public const int Low = 10, High = 20;

    /// <summary>"Front delts", "Upper back".</summary>
    public static string Name(Muscle muscle) => muscle switch
    {
        Muscle.FrontDelts => "Front delts",
        Muscle.SideDelts => "Side delts",
        Muscle.RearDelts => "Rear delts",
        Muscle.UpperBack => "Upper back",
        Muscle.LowerBack => "Lower back",
        _ => muscle.ToString(),
    };

    /// <summary>"Lats, upper back": the first capitalized, as a line starts.</summary>
    public static string List(IEnumerable<Muscle> muscles) =>
        string.Join(", ", muscles.Select((m, i) => i == 0 ? Name(m) : Name(m).ToLowerInvariant()));

    /// <summary>"Lats, upper back · helps rear delts, biceps"; "No muscles: cardio" when it trains none.</summary>
    public static string Text(MuscleTags tags) =>
        tags.Main.Count == 0 ? "No muscles: cardio" : tags.Helps.Count == 0 ? List(tags.Main) : $"{List(tags.Main)} · helps {List(tags.Helps).ToLowerInvariant()}";

    /// <summary>
    /// The hard sets each muscle got from some sets: 1 for each set of an exercise it's the main muscle of, ½ for each of one it helps
    /// with. Sets of untagged exercises, warm-up sets, and empty ones (no reps, no time) count for nothing. Every muscle, top to bottom.
    /// </summary>
    public static IReadOnlyList<MuscleSets> Count(IEnumerable<WorkoutSet> sets, IReadOnlyDictionary<long, MuscleTags> tags)
    {
        var count = All.ToDictionary(m => m, _ => 0.0);
        foreach (var set in sets.Working().Where(s => s.Reps > 0 || s.DurationSeconds > 0))
        {
            if (!tags.TryGetValue(set.ExerciseId, out var tag)) continue;
            foreach (var muscle in tag.Main) count[muscle] += 1;
            foreach (var muscle in tag.Helps) count[muscle] += 0.5;
        }

        return [.. All.Select(m => new MuscleSets(m, count[m]))];
    }

    /// <summary>The muscles that got no sets, top to bottom.</summary>
    public static IReadOnlyList<Muscle> Missing(IEnumerable<MuscleSets> counts) => [.. counts.Where(c => c.Sets == 0).Select(c => c.Muscle)];

    /// <summary>The sets of the workouts started in the week from <paramref name="monday"/>, by the day they started on.</summary>
    public static IEnumerable<WorkoutSet> InWeek(IEnumerable<WorkoutSession> sessions, IEnumerable<WorkoutSet> sets, DateOnly monday)
    {
        var ids = sessions.Where(s => DateOnly.FromDateTime(s.StartedAt.DateTime) is var day && day >= monday && day < monday.AddDays(7)).Select(s => s.Id).ToHashSet();
        return sets.Where(s => ids.Contains(s.SessionId));
    }

    /// <summary>"12", "4½", "½", "0".</summary>
    public static string SetsText(double sets) => sets % 1 == 0 ? $"{sets:0}" : sets < 1 ? "½" : $"{Math.Floor(sets):0}½";
}
