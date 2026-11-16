using AdaptiveWorkout.Core;

namespace AdaptiveWorkout.UI.Workouts;

/// <summary>
/// What a picker of exercises offers: every exercise ever named, the ones done most recently first, and how each went the last
/// time, from the finished workouts.
/// </summary>
public sealed class ExerciseChoices(IReadOnlyList<Exercise> exercises, IReadOnlyList<WorkoutSession> sessions, IReadOnlyList<WorkoutSet> sets)
{
    private readonly Dictionary<long, PastSession?> lastTimes = [];
    private List<Exercise>? recent;

    /// <summary>Every exercise, by name.</summary>
    public List<Exercise> All { get; } = [.. exercises];

    /// <summary>The exercises done before, the latest first.</summary>
    public IReadOnlyList<Exercise> Recent
    {
        get
        {
            if (recent is not null) return recent;

            var starts = sessions.ToDictionary(s => s.Id, s => s.StartedAt);
            var byId = All.ToDictionary(e => e.Id);
            return recent = [.. sets.Where(s => starts.ContainsKey(s.SessionId) && byId.ContainsKey(s.ExerciseId)).GroupBy(s => s.ExerciseId)
                .OrderByDescending(g => g.Max(s => starts[s.SessionId])).Select(g => byId[g.Key])];
        }
    }

    /// <summary>How an exercise went the last time, in short ("3 × 10 at 95"); null when it wasn't done before.</summary>
    public string? LastTime(Exercise exercise) => Last(exercise) is { } last ? SetSummary.Format(last.Sets, exercise.Measure) : null;

    /// <summary>How many sets of an exercise I did the last time: what a line with no count goes by. 0 when it wasn't done before.</summary>
    public int SetsLastTime(Exercise exercise) => Last(exercise)?.Sets.Count ?? 0;

    /// <summary>An exercise just made is one of them from now on.</summary>
    public void Add(Exercise exercise)
    {
        if (All.TrueForAll(e => e.Id != exercise.Id)) All.Add(exercise);
    }

    private PastSession? Last(Exercise exercise)
    {
        if (!lastTimes.TryGetValue(exercise.Id, out var last)) lastTimes[exercise.Id] = last = ExerciseHistory.Recent(exercise.Id, sessions, sets, 0, 1).FirstOrDefault();
        return last;
    }
}
