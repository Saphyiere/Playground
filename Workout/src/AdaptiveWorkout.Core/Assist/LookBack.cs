using System.ComponentModel;
using System.Globalization;
using Playground.AI.Assist;

namespace AdaptiveWorkout.Core.Assist;

/// <summary>The trainer's look back at a finished workout: a few lines on how it went, the next time of each weight exercise, and notes on odd sets.</summary>
public sealed record LookBack(
    [property: Description("Two to four short lines on today, the most notable first: weights that went up, reps that beat the last time, a goal hit, sets that slipped. One plain sentence each.")]
    IReadOnlyList<string> Highlights,
    [property: Description("The next time of each weight exercise done today, one entry each, in today's order. Timed exercises aren't in it.")]
    IReadOnlyList<NextLift> NextTime,
    [property: Description("A note on each flagged set, and on any other set whose numbers can't be right. Empty when nothing looked wrong.")]
    IReadOnlyList<SetNote> SetNotes);

/// <summary>What to do the next time of one exercise.</summary>
public sealed record NextLift(
    [property: Description("The exercise's id, from the input's exercises.")]
    long ExerciseId,
    [property: Description("Sets to do: 1 to 10.")]
    int Sets,
    [property: Description("Reps to aim for in each set: 1 to 50.")]
    int Reps,
    [property: Description("The weight for every set, in the unit the exercise's sets used today: 0 for bodyweight, negative for assistance (−35 is 35 of help). Whole hit-goal steps away from a weight used today or in its last times, by at most a tenth of it or one step.")]
    double Weight,
    [property: Description("The rest between its sets, in seconds: a multiple of 15 from 30 to 300.")]
    int RestSeconds,
    [property: Description("Why, in one short line for the screen, from the data: \"10, 10, 10 at 100: up a step\" or \"10, 8, 7 at 105: stay, and rest longer\".")]
    string Why);

/// <summary>What a set the trainer doubted probably was, and what it did with it.</summary>
public sealed record SetNote(
    [property: Description("The set's id, from the input's sets.")]
    long SetId,
    [property: Description("How you took the set: counted as logged; timeIgnored, its weight and reps counted but not its times; setIgnored, left out; likelyTypo, a number that's probably mistyped.")]
    SetTreatment TreatedAs,
    [property: Description("One short line for the screen: what the set probably was, and for a typo what it probably should be.")]
    string Note);

public enum SetTreatment
{
    Counted,
    TimeIgnored,
    SetIgnored,
    LikelyTypo,
}

/// <summary>
/// Looks back at a finished workout, and plans each weight exercise's next time by the owner's method. Asked when a workout finishes; the
/// owner applies the plan with a tap (see <see cref="LookBackPlan"/>).
/// </summary>
public sealed class LookBackTask : AssistTask<LookBackInput, LookBack>
{
    public override string Name => "workout.look-back";

    public override int Version => 1;

    // The owner waits on the screen for it, and judging a workout's sets doesn't need the deepest thinking.
    public override EffortLevel Effort => EffortLevel.Medium;

    public override int MaxTokens => 16_000;

    public override string Instructions => """
        Look back at the workout the owner just finished, and plan the next time of each of its weight exercises. The answer goes on
        the screen the phone shows after Finish, and its plan pre-fills their next workout when they apply it.

        The input:
        - workout: today's sets in order. Each has its exercise, its number for that exercise today, weight (null for bodyweight,
          negative for assistance: -40 is 40 of help), unit, reps and hitGoal (the owner ticked Hit goal on it). Times come from the
          app: seconds, how long the set ran; restBeforeSeconds, from the round before stopping to this one starting; withPrevious
          marks a superset's later exercises, started together with the one before. Heart rate comes from the watch on their wrist:
          bpmAtStart, bpmAverage and bpmPeak for the set, restLowBpm for the rest before it. Any of these is null when the set was
          logged afterward or the watch had no readings.
        - flags: what the app found odd about a set, with what it saw: longSet, shortRest, effortDuringRest, oddNumber, goalBox.
        - exercises: each exercise done today, with plannedSets (null: as many as last time), the rest countdown it had, what the
          last look back said for today (trainerSaid; empty when it wasn't used), and its last three times before today.
        - hitGoalStep: how much a weight moves, in the set's unit. body: the latest weigh-in, and the daily calorie goal against
          maintenance; a goal under maintenance means they're eating less to lose weight.

        Their method is the rule. They pick a weight and aim for three sets of 10, or the sets planned. When every set hits its reps
        they tick Hit goal, and the next time goes up one step; when reps fall short, the weight stays until they hit it.
        - Up one step after a hit goal, or when every set reached the reps they were aiming for even if Hit goal wasn't ticked.
        - The same weight when reps fell short, aiming at the reps they missed. More reps at the same weight is progress too.
        - Down a step only when reps fell well short two workouts running, or the numbers say the weight is wrong for them.
        - Keep the sets the plan has (plannedSets, else last time's count) unless the data gives a reason.
        - Never swap an exercise or add one: they're keeping their routine as it is until they're lighter and training steadily.
        - While they eat below maintenance, progress can be slow; a flat day isn't a failure, and it isn't a reason to go down.
        - Where today followed trainerSaid, judge today against it.

        Rest, for each exercise's next time: start from the countdown it had. Make it longer, up to 3 or 4 minutes for big lifts
        like squats, presses and rows, when reps dropped from set to set at the same weight; shorter, down to about 90 seconds,
        when reps held easily. Heart rate is a side note: a wrist watch reads it poorly while lifting, so weigh it with the reps,
        never on its own. A set started at a much higher heart rate than the others, whose reps then dropped, is a reason for more
        rest. A low heart rate isn't a reason to go heavier.

        Flags: decide what each flagged set probably was, and say so in setNotes. A long set, or effort during the rest, usually
        means a late or missed tap: its weight and reps still count, only its times don't (timeIgnored). An odd number is usually a
        typo (likelyTypo: say what it probably should be, and plan from that). A goal box ticked or left wrong: say which. Leave a
        set out (setIgnored) only when nothing about it can be trusted. Report only what the data shows.

        Highlights: two to four short lines on today, the most notable first, with the exercises' names and the owner's numbers.
        The summary: one or two sentences on the day as a whole, for the top of the screen.

        Write plainly and briefly, like a coach's note in a training log: warm but not gushing, no exclamation marks, no emoji.
        """;

    public override IReadOnlyList<string> Check(LookBackInput input, LookBack answer) => LookBackPlan.Problems(input, answer);
}

/// <summary>What a look back's answer must be, and what applying it sets for the next time.</summary>
public static class LookBackPlan
{
    /// <summary>
    /// What's wrong with an answer, as sentences for Claude to fix; none when it's fine. Each weight exercise of the workout has one
    /// next time and nothing else does; sets, reps and rest are in range; a weight is whole hit-goal steps from a weight used today or
    /// in the exercise's last times, by at most a tenth of it or one step (bodyweight stays 0); notes are about the workout's sets.
    /// </summary>
    public static IReadOnlyList<string> Problems(LookBackInput input, LookBack answer)
    {
        var problems = new List<string>();
        var exercises = input.Exercises.ToDictionary(e => e.Id);
        var step = input.HitGoalStep;
        foreach (var group in answer.NextTime.GroupBy(l => l.ExerciseId).Where(g => g.Count() > 1))
            problems.Add($"{Name(exercises, group.Key)} is in nextTime {group.Count()} times; give it once.");

        foreach (var lift in answer.NextTime)
        {
            if (!exercises.TryGetValue(lift.ExerciseId, out var exercise))
            {
                problems.Add($"nextTime has exercise {lift.ExerciseId}, which isn't in today's workout.");
                continue;
            }

            if (exercise.Measure == ExerciseMeasure.Time)
            {
                problems.Add($"{exercise.Name} is timed: leave it out of nextTime.");
                continue;
            }

            if (lift.Sets is < 1 or > 10) problems.Add($"{exercise.Name}: sets should be 1 to 10, not {lift.Sets}.");
            if (lift.Reps is < 1 or > 50) problems.Add($"{exercise.Name}: reps should be 1 to 50, not {lift.Reps}.");
            if (lift.RestSeconds is < 30 or > 300 || lift.RestSeconds % 15 != 0)
                problems.Add($"{exercise.Name}: rest should be 30 to 300 seconds in steps of 15, not {lift.RestSeconds}.");
            if (string.IsNullOrWhiteSpace(lift.Why)) problems.Add($"{exercise.Name}: why is empty.");

            var today = input.Workout.Sets.Where(s => s.ExerciseId == lift.ExerciseId).ToList();
            var bases = today.Select(s => s.Weight).Concat(exercise.LastTimes.SelectMany(t => t.Weights.Select(w => (double?)w))).OfType<double>().Distinct().ToList();
            if (bases.Count == 0 || today.All(s => s.Weight is null))
            {
                if (lift.Weight != 0 && today.All(s => s.Weight is null)) problems.Add($"{exercise.Name} is done at bodyweight: its weight should be 0, not {Number(lift.Weight)}.");
            }
            else if (!bases.Any(b => Near(lift.Weight, b, step)))
            {
                problems.Add($"{exercise.Name}: {Number(lift.Weight)} isn't whole steps of {Number(step)} from a weight used today or in its last times " +
                    $"({string.Join(", ", bases.Order().Select(Number))}), by at most a tenth of it or one step.");
            }
        }

        foreach (var missing in input.Exercises.Where(e => e.Measure == ExerciseMeasure.Weight && answer.NextTime.All(l => l.ExerciseId != e.Id)))
            problems.Add($"{missing.Name} is missing from nextTime.");

        var setIds = input.Workout.Sets.Select(s => s.Id).ToHashSet();
        foreach (var note in answer.SetNotes.Where(n => !setIds.Contains(n.SetId)))
            problems.Add($"setNotes has set {note.SetId}, which isn't one of today's sets.");
        foreach (var group in answer.SetNotes.GroupBy(n => n.SetId).Where(g => g.Count() > 1))
            problems.Add($"setNotes has set {group.Key} {group.Count()} times; give it one note.");

        if (answer.Highlights.Count > 5) problems.Add($"highlights has {answer.Highlights.Count} lines; keep it to five at most.");
        return problems;
    }

    /// <summary>
    /// What applying the answer sets for each exercise's next time, kept until the exercise is done again: the weight in the unit its
    /// sets used today (bodyweight stays bodyweight), the reps and sets to aim for, and the rest between them.
    /// </summary>
    public static IReadOnlyList<(long ExerciseId, TrainerTarget Target)> Targets(LookBackInput input, LookBack answer) =>
        [.. answer.NextTime.Select(lift =>
        {
            var today = input.Workout.Sets.Where(s => s.ExerciseId == lift.ExerciseId).ToList();
            var unit = today.Count > 0 ? today[^1].Unit : input.Body.Unit;
            double? weight = today.All(s => s.Weight is null) && lift.Weight == 0 ? null : lift.Weight;
            return (lift.ExerciseId, new TrainerTarget(input.Workout.Id, weight, unit, lift.Reps, lift.Sets, lift.RestSeconds));
        })];

    // Whole steps from the base, and no further than a tenth of it or one step, whichever is more.
    private static bool Near(double weight, double from, double step)
    {
        var change = weight - from;
        var steps = step > 0 ? change / step : 0;
        return Math.Abs(change) <= Math.Max(Math.Abs(from) * 0.1, step) + 1e-9 && Math.Abs(steps - Math.Round(steps)) < 1e-6;
    }

    private static string Name(Dictionary<long, LookBackExercise> exercises, long id) => exercises.TryGetValue(id, out var e) ? e.Name : $"Exercise {id}";

    private static string Number(double value) => value.ToString("0.##", CultureInfo.InvariantCulture);
}
