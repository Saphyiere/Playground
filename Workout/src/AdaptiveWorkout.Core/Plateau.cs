namespace AdaptiveWorkout.Core;

/// <summary>An exercise that's stopped moving: <paramref name="Workouts"/> of its workouts since its best (<paramref name="Best"/>), none of them better.</summary>
public sealed record Plateau(int Workouts, PastSession Best);

public static class Plateaus
{
    /// <summary>Workouts without a new best that make an exercise flat.</summary>
    public const int After = 4;

    /// <summary>
    /// Whether an exercise has gone flat: its best workout (by <see cref="Progress.Score"/>, so heavier or more reps both count) came 4
    /// or more of its workouts ago, and none since beat it. Matching the best doesn't beat it. A bodyweight or assisted exercise counts
    /// one body weight for every workout, the latest weigh-in's, so losing weight doesn't read as getting weaker; without a weigh-in,
    /// only plain bodyweight sets compare, by their reps. Null when it isn't flat.
    /// </summary>
    /// <param name="history">The exercise's workouts, in any order.</param>
    /// <param name="bodyPounds">The latest weigh-in, in pounds; null without one.</param>
    public static Plateau? Of(Exercise exercise, IEnumerable<PastSession> history, double? bodyPounds)
    {
        var ordered = history.OrderBy(p => p.Session.StartedAt).ToList();
        var bodyweight = exercise.Measure == ExerciseMeasure.Weight && ordered.Any(p => p.Sets.Any(s => s.Weight is null or < 0));
        if (bodyweight && bodyPounds is null && ordered.Any(p => p.Sets.Any(s => s.Weight is not null))) return null;

        var body = bodyweight ? bodyPounds ?? 1 : 0;
        var scored = ordered.Select(p => (Past: p, Score: Progress.Score(p.Sets, exercise.Measure, body))).Where(s => s.Score is not null).ToList();
        var at = -1;
        for (var i = 0; i < scored.Count; i++)
        {
            if (at < 0 || scored[i].Score > scored[at].Score) at = i;
        }

        var since = scored.Count - 1 - at;
        return at >= 0 && since >= After ? new Plateau(since, scored[at].Past) : null;
    }
}
