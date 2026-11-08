using System.Globalization;

namespace AdaptiveWorkout.Core.Assist;

/// <summary>
/// What the trainer reads about a finished workout (see <see cref="LookBackTask"/>): its sets with their times, heart rate and anything
/// odd about them, each exercise's last times, and the owner's step and body. The app does the arithmetic; Claude judges what it means.
/// </summary>
/// <param name="HitGoalStep">How much a weight moves after a hit goal, in the set's own unit.</param>
public sealed record LookBackInput(double HitGoalStep, LookBackBody Body, LookBackWorkout Workout, IReadOnlyList<LookBackExercise> Exercises);

/// <summary>The latest weigh-in before the workout (null without one), and the day's calorie goal against maintenance (null until it can be worked out).</summary>
public sealed record LookBackBody(double? Weight, WeightUnit Unit, int CalorieGoal, int? Maintenance);

public sealed record LookBackWorkout(long Id, string Name, DateOnly Date, TimeOnly Start, int Minutes, int? AverageBpm, bool WarmUp, bool CoolDown,
    IReadOnlyList<LookBackSet> Sets);

/// <summary>
/// One set as the app saw it. <paramref name="Number"/> counts the exercise's sets today; <paramref name="WithPrevious"/> marks a superset
/// round's later exercises, started with the one before. Times and heart rate are null when the set was logged afterward or the watch had no
/// readings: <paramref name="Seconds"/> is how long it ran, <paramref name="RestBeforeSeconds"/> the gap since the round before stopped.
/// <paramref name="RepsLeft"/> is how many more reps it had in it, when the owner said (0 to 3, 3 meaning three or more), and
/// <paramref name="BpmDropFirstMinute"/> how far heart rate fell in the minute after it stopped. Those two came with version 2, so a look
/// back kept before reads without them.
/// </summary>
public sealed record LookBackSet(long Id, long ExerciseId, string Exercise, int Number, bool WithPrevious, double? Weight, WeightUnit Unit, int? Reps,
    bool HitGoal, int? Seconds, int? RestBeforeSeconds, int? BpmAtStart, int? BpmAverage, int? BpmPeak, int? RestLowBpm, IReadOnlyList<SetFlag> Flags,
    int? RepsLeft = null, int? BpmDropFirstMinute = null);

/// <summary>
/// An exercise done today: its planned sets (null: as many as last time), the rest countdown it had, what the last look back said for today
/// (empty when none was used), and its last three times before today, newest first. When it's gone flat, counting today (see
/// <see cref="Plateaus"/>), <paramref name="FlatWorkouts"/> is how many workouts since its best, and <paramref name="Best"/> that workout; 0
/// and empty otherwise, and in a look back kept before version 2.
/// </summary>
public sealed record LookBackExercise(long Id, string Name, ExerciseMeasure Measure, int? PlannedSets, int RestCountdownSeconds, string TrainerSaid,
    IReadOnlyList<LookBackLastTime> LastTimes, int FlatWorkouts = 0, string Best = "");

/// <summary>One earlier workout's sets of an exercise: compactly ("3 × 10 at 100"), whether a goal was hit, the weights used, and the usual rest.</summary>
public sealed record LookBackLastTime(DateOnly Date, string Sets, bool HitGoal, IReadOnlyList<double> Weights, int? TypicalRestSeconds);

public enum SetFlagKind
{
    /// <summary>Over 3 minutes, or over 3 times the exercise's usual set: probably a late tap on Stop, or the walk to the next machine.</summary>
    LongSet,

    /// <summary>Under 30 seconds of rest, or under half the countdown: a rest cut short, or Start tapped late on the set before.</summary>
    ShortRest,

    /// <summary>Heart rate climbed during the rest and fell during the set: the set was done before Start was tapped.</summary>
    EffortDuringRest,

    /// <summary>A weight or reps far from the exercise's others: probably a typo.</summary>
    OddNumber,

    /// <summary>Hit goal ticked on fewer reps than before, or every set reached last time's goal with nothing ticked.</summary>
    GoalBox,
}

/// <summary>Something odd the app found about a set, and what it saw, in a few words.</summary>
public sealed record SetFlag(SetFlagKind Kind, string Detail);

/// <summary>
/// What else the trainer is told about the owner and this workout: each exercise's rest countdown in seconds, what the last look back said
/// for each exercise (<see cref="TrainerTarget"/>), the latest weigh-in, and the calorie goal against maintenance.
/// </summary>
public sealed record LookBackContext(double HitGoalStep, IReadOnlyDictionary<long, int> RestTargets, IReadOnlyDictionary<long, TrainerTarget> Trainer,
    BodyWeightEntry? WeighIn, WeightUnit Unit, int CalorieGoal, int? Maintenance);

public static class LookBackFacts
{
    // A heart rate reading counts for a moment when it's this close to it.
    private static readonly TimeSpan Near = TimeSpan.FromSeconds(15);

    /// <summary>
    /// The trainer's input for a finished workout. Only workouts before it count as last times, so looking back again later reads the same.
    /// </summary>
    /// <param name="sets">The workout's sets.</param>
    /// <param name="plan">The workout's own plan, for planned sets; empty when just tracking.</param>
    /// <param name="sessions">Every workout, this one included or not, for last times.</param>
    /// <param name="allSets">Their sets.</param>
    /// <param name="samples">The heart rate during the workout.</param>
    public static LookBackInput For(WorkoutSession session, string name, IReadOnlyList<WorkoutSet> sets, IReadOnlyList<TemplateExercise> plan,
        IReadOnlyDictionary<long, Exercise> exercises, IReadOnlyList<WorkoutSession> sessions, IReadOnlyList<WorkoutSet> allSets,
        IReadOnlyList<HeartRateSample> samples, LookBackContext context)
    {
        var ordered = sets.OrderBy(s => s.Order).ToList();
        var readings = samples.OrderBy(s => s.At).ToList();
        var earlier = sessions.Where(s => s.Id != session.Id && s.StartedAt < session.StartedAt).ToList();
        var earlierIds = earlier.Select(s => s.Id).ToHashSet();
        var earlierSets = allSets.Where(s => earlierIds.Contains(s.SessionId)).ToList();
        var setsBySession = earlierSets.ToLookup(s => s.SessionId);
        var exerciseIds = ordered.Select(s => s.ExerciseId).Distinct().ToList();
        var history = exerciseIds.ToDictionary(id => id, id => ExerciseHistory.Recent(id, earlier, earlierSets, session.Id, 3));

        // Rounds: sets started together (a superset's) rest together, from the round before's last stop.
        var rounds = new List<List<WorkoutSet>>();
        foreach (var set in ordered)
        {
            if (rounds.Count > 0 && set.StartedAt is not null && rounds[^1][0].StartedAt == set.StartedAt) rounds[^1].Add(set);
            else rounds.Add([set]);
        }

        var facts = new List<LookBackSet>();
        for (var r = 0; r < rounds.Count; r++)
        {
            var before = r > 0 ? rounds[r - 1] : null;
            var nextStart = r + 1 < rounds.Count ? rounds[r + 1][0].StartedAt : null;
            DateTimeOffset? restFrom = before is not null && before.All(s => s.StoppedAt is not null) ? before.Max(s => s.StoppedAt) : null;
            var restTo = rounds[r][0].StartedAt;
            int? rest = restFrom is { } from && restTo is { } to && to >= from ? (int)Math.Round((to - from).TotalSeconds) : null;
            for (var m = 0; m < rounds[r].Count; m++)
            {
                var set = rounds[r][m];
                var measure = exercises[set.ExerciseId].Measure;
                var today = ordered.Where(s => s.ExerciseId == set.ExerciseId).ToList();
                int? seconds = measure == ExerciseMeasure.Time && set.DurationSeconds is int typed ? typed
                    : set is { StartedAt: { } start, StoppedAt: { } stop } && stop >= start ? (int)Math.Round((stop - start).TotalSeconds) : null;
                var during = set is { StartedAt: { } s1, StoppedAt: { } s2 } ? WorkoutStats.HeartRate(readings, s1, s2) : null;
                var flags = new List<SetFlag>();
                if (measure == ExerciseMeasure.Weight)
                {
                    var past = history[set.ExerciseId].SelectMany(p => p.Sets).ToList();
                    var others = today.Where(s => s.Id != set.Id).Concat(past).ToList();
                    LongSet(set, seconds, others, flags);
                    if (m == 0 && rest is int gap) ShortRest(gap, before![^1].ExerciseId, exercises, context, flags);
                    if (m == 0 && restFrom is { } restStart) EffortDuringRest(set, restStart, readings, flags);
                    OddNumber(set, others, flags);
                    GoalBox(set, today, history[set.ExerciseId].FirstOrDefault()?.Sets, flags);
                }

                facts.Add(new LookBackSet(set.Id, set.ExerciseId, exercises[set.ExerciseId].Name, today.IndexOf(set) + 1, m > 0, set.Weight, set.WeightUnit,
                    set.Reps, set.HitGoal, seconds, rest, Bpm(readings, set.StartedAt), during?.Average, during?.Max,
                    restFrom is { } low && restTo is { } high ? Lowest(readings, low, high) : null, flags,
                    set.RepsLeft, set.StoppedAt is { } stopped ? HeartRateRecovery.FirstMinute(readings, stopped, nextStart) : null));
            }
        }

        double? bodyPounds = context.WeighIn is { } weighIn ? WorkoutStats.Pounds(weighIn.Weight, weighIn.WeightUnit) : null;
        var lookBackExercises = exerciseIds.Select(id =>
        {
            var exercise = exercises[id];
            var withToday = ExerciseHistory.Recent(id, [.. earlier, session], [.. earlierSets, .. ordered], 0, int.MaxValue);
            var flat = Plateaus.Of(exercise, withToday, bodyPounds);
            var lastTime = history[id].FirstOrDefault();
            var said = context.Trainer.TryGetValue(id, out var target) && lastTime is not null && target.SessionId == lastTime.Session.Id
                ? $"{target.Sets} × {target.Reps} at {Weight(target.Weight, target.Unit)}, rest {TimeText.Format(TimeSpan.FromSeconds(target.RestSeconds))}"
                : "";
            var lastTimes = history[id].Select(p => new LookBackLastTime(DateOnly.FromDateTime(p.Session.StartedAt.DateTime),
                SetSummary.Format(p.Sets, exercise.Measure), p.Sets.Any(s => s.HitGoal), [.. p.Sets.Where(s => s.Weight is not null).Select(s => s.Weight!.Value).Distinct()],
                Rest.Typical([.. setsBySession[p.Session.Id]], id) is { } typical ? (int)typical.TotalSeconds : null)).ToList();
            return new LookBackExercise(id, exercise.Name, exercise.Measure, PlannedSets(plan, id), context.RestTargets.GetValueOrDefault(id, (int)Rest.Default.TotalSeconds),
                said, lastTimes, flat?.Workouts ?? 0,
                flat is null ? "" : $"{SetSummary.Format(flat.Best.Sets, exercise.Measure)} on {DateOnly.FromDateTime(flat.Best.Session.StartedAt.DateTime):yyyy-MM-dd}");
        }).ToList();

        var (workoutFrom, workoutTo) = Calories.WorkoutTime(session, ordered);
        var average = WorkoutStats.HeartRate(readings, workoutFrom, workoutTo)?.Average;
        var started = session.StartedAt.DateTime;
        var minutes = (int)Math.Round(((session.FinishedAt ?? workoutTo) - session.StartedAt).TotalMinutes);
        var workout = new LookBackWorkout(session.Id, name, DateOnly.FromDateTime(started), new TimeOnly(started.Hour, started.Minute), minutes, average,
            session.WarmUp, session.CoolDown, facts);
        var body = new LookBackBody(context.WeighIn?.Weight, context.WeighIn?.WeightUnit ?? context.Unit, context.CalorieGoal, context.Maintenance);
        return new LookBackInput(context.HitGoalStep, body, workout, lookBackExercises);
    }

    // A set that ran over 3 minutes, or over 3 times (and over a minute past) the exercise's usual set.
    private static void LongSet(WorkoutSet set, int? seconds, List<WorkoutSet> others, List<SetFlag> flags)
    {
        if (seconds is not int ran) return;

        var usual = Median(others.Where(s => s is { StartedAt: not null, StoppedAt: not null } && s.StoppedAt >= s.StartedAt)
            .Select(s => (s.StoppedAt!.Value - s.StartedAt!.Value).TotalSeconds));
        if (ran > 180) flags.Add(new SetFlag(SetFlagKind.LongSet, usual is double u ? $"Ran {Time(ran)}; its sets usually run {Time(u)}" : $"Ran {Time(ran)}"));
        else if (usual is double typical && ran > 3 * typical && ran > 60) flags.Add(new SetFlag(SetFlagKind.LongSet, $"Ran {Time(ran)}; its sets usually run {Time(typical)}"));
    }

    // A rest under 30 seconds, or under half the countdown after the round before (its last exercise's, as the countdown ran).
    private static void ShortRest(int rest, long afterExerciseId, IReadOnlyDictionary<long, Exercise> exercises, LookBackContext context, List<SetFlag> flags)
    {
        var target = context.RestTargets.GetValueOrDefault(afterExerciseId);
        if (rest < 30 || (target > 0 && rest * 2 < target))
            flags.Add(new SetFlag(SetFlagKind.ShortRest, target > 0
                ? $"{Time(rest)} rest; the countdown after {exercises[afterExerciseId].Name} was {Time(target)}"
                : $"{Time(rest)} rest"));
    }

    // Heart rate climbed by 15 or more through the rest, then fell by 10 or more through the set: the effort was in the rest.
    private static void EffortDuringRest(WorkoutSet set, DateTimeOffset restFrom, List<HeartRateSample> readings, List<SetFlag> flags)
    {
        if (Bpm(readings, restFrom) is not int restStart || Bpm(readings, set.StartedAt) is not int restEnd || Bpm(readings, set.StoppedAt) is not int setEnd) return;

        if (restEnd - restStart >= 15 && setEnd - restEnd <= -10)
            flags.Add(new SetFlag(SetFlagKind.EffortDuringRest, $"Heart rate rose {restStart} → {restEnd} during the rest, then fell to {setEnd} during the set"));
    }

    // A weight 2.5 times (or under 0.4 times) the exercise's others, or 30 reps and more that are 3 times its others.
    private static void OddNumber(WorkoutSet set, List<WorkoutSet> others, List<SetFlag> flags)
    {
        if (set.Weight is double weight && weight != 0 && Median(others.Where(s => s.Weight is not null and not 0).Select(s => Math.Abs(s.Weight!.Value))) is double usual)
        {
            var ratio = Math.Abs(weight) / usual;
            if (ratio >= 2.5 || ratio <= 0.4) flags.Add(new SetFlag(SetFlagKind.OddNumber, $"{Number(weight)} {Unit(set.WeightUnit)}; its other sets are around {Number(usual)}"));
        }

        if (set.Reps is int reps && reps >= 30 && Median(others.Where(s => s.Reps > 0).Select(s => (double)s.Reps!.Value)) is double usualReps && reps >= 3 * usualReps)
            flags.Add(new SetFlag(SetFlagKind.OddNumber, $"{reps} reps; its other sets are around {Number(usualReps)}"));
    }

    // Hit goal ticked on fewer reps than an earlier set at the same weight today; or, on the exercise's last set, every set reached last
    // time's goal and none is ticked.
    private static void GoalBox(WorkoutSet set, List<WorkoutSet> today, IReadOnlyList<WorkoutSet>? lastTime, List<SetFlag> flags)
    {
        var earlier = today.TakeWhile(s => s.Id != set.Id).Where(s => s.Weight == set.Weight && s.Reps is not null).ToList();
        if (set is { HitGoal: true, Reps: int reps } && earlier.Count > 0 && earlier.Max(s => s.Reps!.Value) is var most && most > reps)
            flags.Add(new SetFlag(SetFlagKind.GoalBox, $"Hit goal ticked on {reps} reps, after {most} at the same weight"));

        if (today[^1].Id != set.Id || today.Any(s => s.HitGoal) || lastTime?.LastOrDefault(s => s.HitGoal) is not { Reps: int goalReps } goal) return;

        var reached = today.Count >= lastTime.Count && today.All(s => s.Reps >= goalReps && (goal.Weight is not double w || s.Weight >= w));
        if (reached) flags.Add(new SetFlag(SetFlagKind.GoalBox, $"Every set reached last time's goal ({goalReps} at {Weight(goal.Weight, goal.WeightUnit)}), but Hit goal isn't ticked"));
    }

    // The planned sets for an exercise: its entry's, or, in a superset, the first entry's (which counts for all).
    private static int? PlannedSets(IReadOnlyList<TemplateExercise> plan, long exerciseId)
    {
        var at = plan.ToList().FindIndex(e => e.ExerciseId == exerciseId);
        if (at < 0) return null;
        if (plan[at].PlannedSets is not null || plan[at].SupersetGroup is not int group) return plan[at].PlannedSets;

        var first = at;
        while (first > 0 && plan[first - 1].SupersetGroup == group) first--;
        return plan[first].PlannedSets;
    }

    // The reading nearest a moment, within 15 seconds of it.
    private static int? Bpm(List<HeartRateSample> readings, DateTimeOffset? at) =>
        at is not { } moment ? null : readings.Where(s => (s.At - moment).Duration() <= Near).OrderBy(s => (s.At - moment).Duration()).Select(s => (int?)s.Bpm).FirstOrDefault();

    private static int? Lowest(List<HeartRateSample> readings, DateTimeOffset from, DateTimeOffset to) =>
        readings.Where(s => s.At >= from && s.At <= to).Select(s => (int?)s.Bpm).Min();

    private static double? Median(IEnumerable<double> values)
    {
        var sorted = values.Order().ToList();
        return sorted.Count == 0 ? null : sorted.Count % 2 == 1 ? sorted[sorted.Count / 2] : (sorted[sorted.Count / 2 - 1] + sorted[sorted.Count / 2]) / 2;
    }

    private static string Time(double seconds) => TimeText.Format(TimeSpan.FromSeconds(Math.Round(seconds)));

    private static string Number(double value) => value.ToString("0.#", CultureInfo.InvariantCulture);

    private static string Unit(WeightUnit unit) => unit == WeightUnit.Kilograms ? "kg" : "lb";

    private static string Weight(double? weight, WeightUnit unit) => weight is double w ? $"{SetSummary.WeightText(w)} {Unit(unit)}" : "bodyweight";
}
