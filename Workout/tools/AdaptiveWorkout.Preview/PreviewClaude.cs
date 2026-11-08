using System.Globalization;
using System.Text.Json;
using AdaptiveWorkout.Core;
using AdaptiveWorkout.Core.Assist;
using Playground.AI.Assist;

namespace AdaptiveWorkout.Preview;

/// <summary>
/// Claude for the preview: never calls the API, so the screens can be checked without a key. A look back is worked out from its input
/// the way the owner's method reads (up a step when every set reached 10 or a goal was hit, the same weight and a longer rest when reps
/// dropped), with a note on each flagged set and the week's missing muscles; exercises are tagged by the words in their names. Each
/// answer is held to the task's own checks, waits a moment and is logged with a price, as the real one is. /claude/script slows it
/// down, or makes it answer with a kind of trouble instead.
/// </summary>
public sealed class PreviewClaude(IAssistKeyStore keys, IAssistLog log) : IClaude
{
    public const int Delay = 2500;

    /// <summary>How long each answer takes, in ms.</summary>
    public int DelayMs { get; set; } = Delay;

    /// <summary>The kind of trouble to answer with; null to answer.</summary>
    public AnswerKind? Fail { get; set; }

    public void Reset() => (DelayMs, Fail) = (Delay, null);

    public async Task<Answer<TAnswer>> AskAsync<TInput, TAnswer>(AssistTask<TInput, TAnswer> task, TInput input, CancellationToken ct = default)
    {
        if (await keys.GetAsync() is null) return new(AnswerKind.NoKey, default, "There's no Claude key on this device yet.", [], Usage.None);
        await Task.Delay(DelayMs, ct);
        var took = TimeSpan.FromMilliseconds(DelayMs);
        var answer = (task, input) switch
        {
            (AssistTask<LookBackInput, LookBack> lookBack, LookBackInput workout) when Plan(workout) is var plan =>
                (object)Judge(lookBack, workout, plan, Summary(workout, plan), new Usage(7_400, 2_900, 0, 0, 0.09m, took)),
            (AssistTask<ExercisesToTag, ExerciseTags> tagging, ExercisesToTag exercises) =>
                Judge(tagging, exercises, Tag(exercises), $"Tagged {exercises.Exercises.Count} exercises by what they mainly train.", new Usage(1_900, 600, 0, 0, 0.02m, took)),
            (AssistTask<AskInput, AskAnswer> ask, AskInput question) when Reply(question) is var (reply, summary) =>
                Judge(ask, question, reply, summary, new Usage(9_800, 1_400, 0, 0, 0.14m, took)),
            (AssistTask<WeekInput, WeekLook> weekTask, WeekInput week) when Look(week) is var (look, summary) =>
                Judge(weekTask, week, look, summary, new Usage(8_600, 1_100, 0, 0, 0.12m, took)),
            _ => throw new InvalidOperationException($"The preview has no answers for {task.Name}."),
        };
        var typed = (Answer<TAnswer>)answer;
        await log.WriteAsync(new AssistCall(DateTimeOffset.Now, task.Name, task.Version, "preview", 1, typed.Kind, typed.Summary,
            JsonSerializer.Serialize(input, AssistSchema.Json), typed.Ok ? JsonSerializer.Serialize(typed.Value, AssistSchema.Json) : "", 0, typed.Usage, "preview", ""));
        return typed;
    }

    // The answer as the real one comes back: the trouble it was told to have, the task's checks failing, or answered.
    private Answer<T> Judge<TIn, T>(AssistTask<TIn, T> task, TIn input, T value, string summary, Usage usage) where T : class
    {
        var problems = task.Check(input, value);
        return Fail is { } kind ? new(kind, null, Trouble(kind), [], Usage.None)
            : problems.Count > 0 ? new(AnswerKind.CheckFailed, null, "Claude's answer didn't pass the task's checks.", problems, Usage.None)
            : new(AnswerKind.Answered, value, summary, [], usage);
    }

    // What an exercise trains, by the words in its name, the first that matches; cardio and anything unknown trains none.
    private static readonly (string[] Words, Muscle[] Primary, Muscle[] Secondary)[] ByName =
    [
        (["pulldown", "pull-up", "pull up", "chin"], [Muscle.Lats], [Muscle.Biceps, Muscle.UpperBack]),
        (["row"], [Muscle.Lats, Muscle.UpperBack], [Muscle.RearDelts, Muscle.Biceps]),
        (["dip"], [Muscle.Chest, Muscle.Triceps], [Muscle.FrontDelts]),
        (["pushdown", "skull", "tricep"], [Muscle.Triceps], []),
        (["chest", "bench"], [Muscle.Chest], [Muscle.FrontDelts, Muscle.Triceps]),
        (["shoulder press", "overhead"], [Muscle.FrontDelts], [Muscle.SideDelts, Muscle.Triceps]),
        (["lateral raise"], [Muscle.SideDelts], []),
        (["face pull", "rear delt"], [Muscle.RearDelts], [Muscle.UpperBack]),
        (["leg curl"], [Muscle.Hamstrings], []),
        (["curl"], [Muscle.Biceps], [Muscle.Forearms]),
        (["deadlift"], [Muscle.Hamstrings, Muscle.Glutes, Muscle.LowerBack], [Muscle.UpperBack, Muscle.Forearms]),
        (["squat", "leg press", "lunge"], [Muscle.Quads, Muscle.Glutes], [Muscle.Hamstrings, Muscle.LowerBack]),
        (["calf"], [Muscle.Calves], []),
        (["plank", "crunch", "leg raise", "sit-up"], [Muscle.Abs], []),
    ];

    // An answer from the question's input: the next workout's lifts by the owner's method, the weeks' missing muscles, or the weight's
    // line against the logs, by the words in the question; else the weeks' workouts.
    private static (AskAnswer Answer, string Summary) Reply(AskInput input)
    {
        var question = input.Question.ToLowerInvariant();
        var thisWeek = input.Weeks[^1];
        var lastWeek = input.Weeks[^2];
        if (question.Contains("next") && input.Next is { } next)
        {
            var lifts = new List<AskLift>();
            foreach (var planned in next.Exercises)
            {
                if (input.Exercises.FirstOrDefault(e => e.Id == planned.ExerciseId) is not { Measure: ExerciseMeasure.Weight, Times: [var latest, ..] }) continue;

                var weight = latest.Weights.Count == 0 ? 0 : latest.Weights.Max() + (latest.HitGoal ? input.HitGoalStep : 0);
                lifts.Add(new AskLift(planned.ExerciseId, planned.PlannedSets ?? 3, latest.Weights.Count == 0 ? 12 : 10, weight, 120,
                    $"{latest.Sets} last time: {(latest.HitGoal ? "up a step" : "the same, and tick Hit goal when every set reaches 10")}"));
            }

            return (new AskAnswer([
                $"Next is {next.Session} on {next.Date.DayOfWeek}. Start each lift from its last time: up a step where every set reached its reps, the same weight where they fell short.",
                "Keep the rests as they are unless reps drop from set to set; then give the big lifts a little longer."], lifts),
                $"{next.Session} on {next.Date.DayOfWeek}: {lifts.Count} {(lifts.Count == 1 ? "lift" : "lifts")}, each from its last time.");
        }

        if (question.Contains("muscle"))
        {
            var missing = thisWeek.Missing.Count == 0 ? "every muscle has had some sets" : $"nothing has trained {Muscles.List(thisWeek.Missing).ToLowerInvariant()} yet";
            return (new AskAnswer([
                $"This week {missing}. Last week, {(lastWeek.Missing.Count == 0 ? "every muscle got sets" : $"{Muscles.List(lastWeek.Missing).ToLowerInvariant()} got none")}.",
                "That's the routine as it stands: the gaps are worth knowing about, and changing it is your call."], []),
                thisWeek.Missing.Count == 0 ? "Every muscle has had sets this week." : $"{thisWeek.Missing.Count} muscles have had no sets this week.");
        }

        if (question.Contains("weight"))
        {
            var change = input.Body.FourWeekChange is double c ? $"{(c < 0 ? "down" : "up")} {Math.Abs(c):0.0} {(input.Body.Unit == WeightUnit.Kilograms ? "kg" : "lb")}" : "not clear yet";
            var eaten = input.Weeks.Select(w => w.AverageEaten).OfType<int>().DefaultIfEmpty().Average();
            return (new AskAnswer([
                $"Over the last four weeks your weight is {change} by the line through your weigh-ins.",
                $"You've eaten about {eaten:N0} a day on the days logged, against a goal of {input.Body.CalorieGoal:N0}." +
                (input.Body.MaintenanceByScale is int byScale ? $" By the scale, your maintenance is about {byScale:N0}." : "")], []),
                $"Your weight is {change} over four weeks.");
        }

        var workouts = input.Weeks.Sum(w => w.Workouts);
        return (new AskAnswer([$"You've done {workouts} workouts in the last eight weeks, {thisWeek.Workouts} of them this week."], []), $"{workouts} workouts in eight weeks.");
    }

    // A week's look back from its input: the planned days missed, each lift heavier than before the week, the muscles with no sets, the
    // food against maintenance and the scale's line, and the next workout.
    private static (WeekLook Look, string Summary) Look(WeekInput input)
    {
        var week = input.Week;
        var missedDays = input.Days.Where(d => d.Planned is not ("" or "Rest") && d.Workouts == 0 && d.Date < input.Today).Select(d => $"{d.Date.DayOfWeek}'s {d.Planned.ToLowerInvariant()}").ToList();
        var gymDays = week.PlannedDays is int planned
            ? $"{week.Workouts} of {planned} planned days{(missedDays.Count == 0 ? ", none missed." : $"; {string.Join(" and ", missedDays)} missed.")}"
            : $"{week.Workouts} gym days.";
        var wentUp = input.Exercises.Select(e =>
        {
            var heaviest = e.Times.Where(t => t.Date >= input.Monday).SelectMany(t => t.Weights).DefaultIfEmpty().Max();
            var before = e.Times.Where(t => t.Date < input.Monday).SelectMany(t => t.Weights).DefaultIfEmpty().Max();
            return heaviest > before && before > 0 ? $"{e.Name}: up to {heaviest:0.#} from {before:0.#}." : "";
        }).Where(l => l.Length > 0).Take(6).ToList();
        var missed = week.Missing.Count == 0 ? "" : $"Nothing trained {Muscles.List(week.Missing).ToLowerInvariant()}.";
        var calories = week.DaysLogged == 0 ? "No food was logged this week, so there's nothing to set against the scale."
            : $"{week.AverageEaten:N0} a day on {week.DaysLogged} days logged, {week.AverageUnderMaintenance:N0} under maintenance on average." +
              (input.Body.FourWeekChange is double c ? $" The scale's line is {(c < 0 ? "down" : "up")} {Math.Abs(c):0.0} over four weeks." : "");
        var next = input.Next is { } n ? $"{n.Session} on {n.Date.DayOfWeek} starts it: up a step wherever every set reached 10." : "Keep to the planned days.";
        var thisWeek = week.DaysLogged is > 0 and < 6 ? new[] { next, "Log every day's food, so the scale has something to answer." } : [next];
        return (new WeekLook(gymDays, wentUp, missed, calories, thisWeek),
            $"{week.Workouts} workouts{(missedDays.Count == 0 ? " and no planned day missed" : "")}, " +
            (wentUp.Count == 0 ? "with every lift holding its weight." : $"with {wentUp.Count} {(wentUp.Count == 1 ? "lift" : "lifts")} going up."));
    }

    /// <summary>The preview's tags for some exercises, by the words in their names; the muscles seed uses them too.</summary>
    public static ExerciseTags Tag(ExercisesToTag input) => new([.. input.Exercises.Select(e =>
        ByName.FirstOrDefault(r => r.Words.Any(w => e.Name.Contains(w, StringComparison.OrdinalIgnoreCase))) is { Words: not null } match
            ? new ExerciseTag(e.Id, match.Primary, match.Secondary)
            : new ExerciseTag(e.Id, [], []))]);

    public async Task<Answer<string>> TestAsync(CancellationToken ct = default)
    {
        if (await keys.GetAsync() is null) return new(AnswerKind.NoKey, null, "There's no Claude key on this device yet.", [], Usage.None);
        await Task.Delay(600, ct);
        return Fail is { } kind ? new(kind, null, Trouble(kind), [], Usage.None) : new(AnswerKind.Answered, "Claude Opus 5.5", "The key works, and Claude Opus 5.5 is ready.", [], Usage.None);
    }

    private static LookBack Plan(LookBackInput input)
    {
        var step = input.HitGoalStep;
        var next = new List<NextLift>();
        var highlights = new List<string>();
        foreach (var exercise in input.Exercises.Where(e => e.Measure == ExerciseMeasure.Weight))
        {
            var sets = input.Workout.Sets.Where(s => s.ExerciseId == exercise.Id).ToList();
            var reps = sets.Select(s => s.Reps ?? 0).ToList();
            var weight = sets.GroupBy(s => s.Weight).OrderByDescending(g => g.Count()).ThenByDescending(g => g.Key).First().Key;
            var reached = sets.Any(s => s.HitGoal) || reps.All(r => r >= 10);
            var dropped = reps[0] - reps[^1] >= 2;
            var rest = Math.Clamp((int)Math.Round((exercise.RestCountdownSeconds + (dropped ? 30 : 0)) / 15.0) * 15, 30, 300);
            var said = $"{string.Join(", ", reps)} at {Weight(weight)}";
            var (to, why) = weight is not double w ? (0d, reached ? $"{said}: two more reps" : $"{said}: the same again")
                : exercise.FlatWorkouts >= Plateaus.After ? (w, $"Flat for {exercise.FlatWorkouts} workouts: chase reps at {Weight(w)}")
                : reached ? (w + step, $"{said}: up a step")
                : (w, dropped ? $"{said}: stay, and rest longer" : $"{said}: stay until it's 10s");
            var aim = weight is null && reached ? reps.Max() + 2 : 10;
            next.Add(new NextLift(exercise.Id, exercise.PlannedSets ?? sets.Count, aim, to, rest, why));

            var last = exercise.LastTimes.FirstOrDefault();
            if (exercise.FlatWorkouts >= Plateaus.After) highlights.Add($"{exercise.Name} hasn't beaten {exercise.Best} in {exercise.FlatWorkouts} workouts: next time chases reps.");
            else if (weight is double up && last?.Weights is [_, ..] before && up > before.Max()) highlights.Add($"{exercise.Name} went up to {Weight(up)} and held {string.Join(", ", reps)}.");
            else if (reached && weight is not null) highlights.Add($"{exercise.Name}: every set at 10, so it goes up a step next time.");
            else if (dropped) highlights.Add($"{exercise.Name} slipped from {reps[0]} to {reps[^1]} reps across its sets.");
        }

        var notes = input.Workout.Sets.Where(s => s.Flags.Count > 0).Select(s => s.Flags[0].Kind switch
        {
            SetFlagKind.LongSet => new SetNote(s.Id, SetTreatment.TimeIgnored, $"Probably a late tap on Stop: its {s.Reps} reps count, its time doesn't."),
            SetFlagKind.ShortRest => new SetNote(s.Id, SetTreatment.Counted, "A short rest before it, but the reps held, so it counts as logged."),
            SetFlagKind.EffortDuringRest => new SetNote(s.Id, SetTreatment.TimeIgnored, "The effort shows in the rest before it: Start was tapped after the set."),
            SetFlagKind.OddNumber => new SetNote(s.Id, SetTreatment.LikelyTypo, $"Probably {Weight((s.Weight ?? 0) / 10)}: planned from that."),
            _ => new SetNote(s.Id, SetTreatment.Counted, "Every set reached the goal, so it's counted as a hit goal."),
        }).ToList();
        if (input.Muscles is { Missing: [_, ..] missing }) highlights = [.. highlights.Take(3), $"Nothing trained {Muscles.List(missing).ToLowerInvariant()} this week."];
        return new LookBack([.. highlights.Take(4)], next, notes);
    }

    private static string Summary(LookBackInput input, LookBack plan)
    {
        var ups = plan.NextTime.Count(l => input.Workout.Sets.Where(s => s.ExerciseId == l.ExerciseId).Max(s => s.Weight) is double w && l.Weight > w);
        return $"A steady {input.Workout.Name.ToLowerInvariant()}: {input.Workout.Sets.Count} sets in {input.Workout.Minutes} minutes. " +
            (ups > 0 ? $"{ups} of {plan.NextTime.Count} lifts go up a step next time; the rest hold their weight." : "Everything holds its weight next time.");
    }

    private static string Weight(double? weight) => weight is double w ? $"{w.ToString("0.#", CultureInfo.InvariantCulture)} lb" : "bodyweight";

    private static string Trouble(AnswerKind kind) => kind switch
    {
        AnswerKind.KeyRejected => "Claude didn't accept the key on this device: it may be wrong, disabled or expired.",
        AnswerKind.SpendLimit => "The spend limit for this key's workspace is reached.",
        AnswerKind.OutOfCredit => "This month's API credits are used up.",
        AnswerKind.Busy => "Claude is busy right now; try again in a minute.",
        AnswerKind.Offline => "Claude couldn't be reached: check the connection.",
        _ => "The request failed: the preview was told to fail.",
    };
}
