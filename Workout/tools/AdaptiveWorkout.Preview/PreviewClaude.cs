using System.Globalization;
using System.Text.Json;
using AdaptiveWorkout.Core;
using AdaptiveWorkout.Core.Assist;
using Playground.AI.Assist;

namespace AdaptiveWorkout.Preview;

/// <summary>
/// Claude for the preview: never calls the API, so the screens can be checked without a key. A look back is worked out from its input
/// the way the owner's method reads (up a step when every set reached 10 or a goal was hit, the same weight and a longer rest when reps
/// dropped), with a note on each flagged set, and held to the task's own checks. Each answer waits a moment and is logged with a
/// price, as the real one is. /claude/script slows it down, or makes it answer with a kind of trouble instead.
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
        if (input is not LookBackInput workout || task is not AssistTask<LookBackInput, LookBack> lookBack)
            throw new InvalidOperationException($"The preview has no answers for {task.Name}.");

        var plan = Plan(workout);
        var problems = lookBack.Check(workout, plan);
        Answer<LookBack> answer = Fail is { } kind ? new(kind, null, Trouble(kind), [], Usage.None)
            : problems.Count > 0 ? new(AnswerKind.CheckFailed, null, "Claude's answer didn't pass the task's checks.", problems, Usage.None)
            : new(AnswerKind.Answered, plan, Summary(workout, plan), [], new Usage(7_400, 2_900, 0, 0, 0.09m, TimeSpan.FromMilliseconds(DelayMs)));
        await log.WriteAsync(new AssistCall(DateTimeOffset.Now, task.Name, task.Version, "preview", 1, answer.Kind, answer.Summary,
            JsonSerializer.Serialize(input, AssistSchema.Json), answer.Ok ? JsonSerializer.Serialize(answer.Value, AssistSchema.Json) : "", 0, answer.Usage, "preview", ""));
        return (Answer<TAnswer>)(object)answer;
    }

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
