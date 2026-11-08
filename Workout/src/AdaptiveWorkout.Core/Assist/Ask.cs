using System.ComponentModel;
using Playground.AI.Assist;

namespace AdaptiveWorkout.Core.Assist;

/// <summary>
/// A question about my training, and what Claude is told with it (see <see cref="AskTask"/>): the last 8 weeks summed up week by week,
/// the exercises done in them with their recent times, my body and food, the routine and its next workout, and my last few questions.
/// The app does the arithmetic; Claude reads it.
/// </summary>
/// <param name="HitGoalStep">How much a weight moves after a hit goal, in the set's own unit.</param>
public sealed record AskInput(string Question, DateOnly Today, double HitGoalStep, AskBody Body, IReadOnlyList<AskWeek> Weeks, IReadOnlyList<AskExercise> Exercises,
    AskRoutine? Routine, AskNext? Next, IReadOnlyList<AskEarlier> Earlier);

/// <summary>
/// My latest weigh-in (null without one), and how my weight moved over the last 4 weeks by the line through the weigh-ins, in my unit
/// (null without enough); the calorie goal, maintenance by the formula (null until it can be worked out), and maintenance by the scale
/// over the last 4 weeks (null without enough logging and weigh-ins, see <see cref="Calories.FromScale"/>).
/// </summary>
public sealed record AskBody(double? Weight, WeightUnit Unit, double? FourWeekChange, int CalorieGoal, int? Maintenance, int? MaintenanceByScale);

/// <summary>
/// One week, Monday to Sunday (this one to today): its finished workouts against the routine's gym days (null without a routine); each
/// muscle's hard sets, only those with some, and the muscles with none; its days over that I logged food or forgot (counted at
/// maintenance), what they averaged eaten and under maintenance; and my average weigh-in, in my unit.
/// </summary>
public sealed record AskWeek(DateOnly Monday, int Workouts, int? PlannedDays, IReadOnlyList<MuscleSets> Muscles, IReadOnlyList<Muscle> Missing,
    int DaysLogged, int DaysForgotten, int? AverageEaten, int? AverageUnderMaintenance, double? Weight);

/// <summary>
/// An exercise done in the 8 weeks: what it trains, its times in them (newest first, at most 8), whether it's gone flat counting every
/// time (see <see cref="Plateaus"/>; 0 and empty otherwise), and the plan for its next time that's waiting from a look back or an answer
/// (empty when there's none).
/// </summary>
public sealed record AskExercise(long Id, string Name, ExerciseMeasure Measure, string Muscles, IReadOnlyList<AskTime> Times, int FlatWorkouts, string Best,
    string TrainerSaid);

/// <summary>One workout's sets of an exercise: compactly ("3 × 10 at 100"), whether a goal was hit, and the weights used (none at bodyweight) in their unit.</summary>
public sealed record AskTime(long WorkoutId, DateOnly Date, string Sets, bool HitGoal, IReadOnlyList<double> Weights, WeightUnit Unit);

/// <summary>The routine in use: its name, and its week from Monday, each day's session and its exercises, or "Rest".</summary>
public sealed record AskRoutine(string Name, IReadOnlyList<AskDay> Week);

public sealed record AskDay(string Day, string Session, IReadOnlyList<string> Exercises);

/// <summary>
/// The next workout the routine plans: today's while there's no finished workout today, else the next day with a session. Each of its
/// exercises with its planned sets (null: as many as last time) and what its first set will start from.
/// </summary>
public sealed record AskNext(DateOnly Date, string Session, IReadOnlyList<AskNextExercise> Exercises);

public sealed record AskNextExercise(long ExerciseId, string Name, int? PlannedSets, string FirstSet);

/// <summary>A question asked before, and the answer's words.</summary>
public sealed record AskEarlier(DateOnly Date, string Question, string Answer);

/// <summary>The answer to a question: its words, and, when it says what to lift, the next time of each exercise it covers.</summary>
public sealed record AskAnswer(
    [property: Description("The answer, in one to five short paragraphs of plain text, the most useful first, with the exercises' names and the owner's numbers. No headings or lists.")]
    IReadOnlyList<string> Paragraphs,
    [property: Description("Only when the answer says what to lift next: the next time of each weight exercise it covers, from the input's exercises. Empty otherwise.")]
    IReadOnlyList<AskLift> NextTime);

/// <summary>What to do the next time of one exercise.</summary>
public sealed record AskLift(
    [property: Description("The exercise's id, from the input's exercises.")]
    long ExerciseId,
    [property: Description("Sets to do: 1 to 10.")]
    int Sets,
    [property: Description("Reps to aim for in each set: 1 to 50.")]
    int Reps,
    [property: Description("The weight for every set, in the unit of its latest time: 0 for bodyweight, negative for assistance (−35 is 35 of help). Whole hit-goal steps away from a weight in its times, by at most a tenth of it or one step.")]
    double Weight,
    [property: Description("The rest between its sets, in seconds: a multiple of 15 from 30 to 300.")]
    int RestSeconds,
    [property: Description("Why, in one short line for the screen, from the data: \"10, 10, 10 at 100 last time: up a step\".")]
    string Why);

/// <summary>
/// What else Claude is told with a question: my step, unit, calorie goal and maintenance (the formula's, and the scale's when it can be
/// told), what each exercise trains, the plans waiting for exercises' next times, and the routine in use (null without one).
/// </summary>
public sealed record AskContext(double HitGoalStep, WeightUnit Unit, int CalorieGoal, int? Maintenance, int? MaintenanceByScale,
    IReadOnlyDictionary<long, MuscleTags> Tags, IReadOnlyDictionary<long, TrainerTarget> Trainer, AskRoutinePlan? Routine);

/// <summary>The routine in use as the app keeps it: its name, and each day's session from Monday (null for a rest day).</summary>
public sealed record AskRoutinePlan(string Name, IReadOnlyList<AskSession?> Week);

/// <summary>A session in the routine: its name and its exercises in order.</summary>
public sealed record AskSession(string Name, IReadOnlyList<TemplateExercise> Exercises);

/// <summary>
/// Answers a question about my training from my own data, by my method. Asked from Ask Claude on Today; where the answer plans lifts,
/// I take them with a tap, as with the look back (see <see cref="AskPlan"/>).
/// </summary>
public sealed class AskTask : AssistTask<AskInput, AskAnswer>
{
    public override string Name => "workout.ask";

    public override int Version => 1;

    // The owner waits on the screen, and reading 8 weeks of summed-up numbers doesn't need the deepest thinking.
    public override EffortLevel Effort => EffortLevel.Medium;

    public override int MaxTokens => 16_000;

    public override string Instructions => """
        Answer the owner's question about their training, from their own data. The answer goes on a screen on their phone.

        The input:
        - question: what they asked. earlier: their last few questions and your answers, for a follow-up.
        - today, and weeks: the last 8 weeks, Monday to Sunday, oldest first; the last is this week so far. Each has its finished
          workouts against the gym days their routine plans; each muscle's hard sets, counting 1 for each set of an exercise it's
          the main muscle of and half for each it helps with (only muscles with some), and missing, the muscles with none; the days
          over that food was logged and the days forgotten, which count at maintenance, with what they averaged eaten and under
          maintenance; and the average weigh-in.
        - exercises: each one done in those weeks, with what it trains and its times in them, newest first: its sets compactly,
          whether a goal was hit, and the weights used (none at bodyweight, negative for assistance). flatWorkouts is how many
          workouts it's gone without beating its best (4 or more is flat), with best that workout; trainerSaid is a plan for its next
          time already waiting from a look back or an earlier answer.
        - body: the latest weigh-in, how weight moved over the last 4 weeks by the line through the weigh-ins, the daily calorie goal,
          and maintenance by the formula and by the scale (what the logs and the scale's change say they burn; null without enough).
        - routine: the routine in use, day by day. next: the next workout it plans, with what each exercise's first set starts from.
        - hitGoalStep: how much a weight moves, in the set's unit.

        Their method is the rule. They pick a weight and aim for three sets of 10, or the sets planned. When every set hits its reps
        the next time goes up one step; when reps fall short, the weight stays until they hit it. Never suggest swapping, adding or
        dropping an exercise: they keep their routine as it is until they're lighter and training steadily. A muscle the weeks miss
        can be pointed out, never fixed with a new exercise. While they eat below maintenance, slow progress and flat lifts are
        expected, and aren't a reason to go down.

        - Answer what was asked, briefly, from the data: the numbers that matter, the exercises by name. Say plainly when the data
          can't tell: too few weeks, no weigh-ins, food not logged.
        - Weight: read the line through the weigh-ins, not one morning's number. A pound is about 3,500 calories. When the logs say
          more should have gone than the scale shows, some food probably isn't logged, or maintenance is lower than the formula's;
          the scale's maintenance says by how much.
        - nextTime: only when the answer says what to lift next, because they asked what to do next time or how much to lift. One
          entry for each weight exercise it covers, by their method, from its latest time. Empty otherwise.
        - The summary: one sentence that answers the question, for the top of the screen.

        Write plainly, like a coach's note in a training log: warm but not gushing, no exclamation marks, no emoji, no headings or
        lists in the paragraphs.
        """;

    public override IReadOnlyList<string> Check(AskInput input, AskAnswer answer) => AskPlan.Problems(input, answer);
}

/// <summary>What an answer must be, and what taking its plan sets for the next time.</summary>
public static class AskPlan
{
    /// <summary>
    /// What's wrong with an answer, as sentences for Claude to fix; none when it's fine. One to five paragraphs; each planned exercise
    /// once, a weight exercise from the input, its sets, reps, rest and weight within <see cref="Lifts.Problems"/>'s bounds from the
    /// weights in its times.
    /// </summary>
    public static IReadOnlyList<string> Problems(AskInput input, AskAnswer answer)
    {
        var problems = new List<string>();
        if (answer.Paragraphs.Count is < 1 or > 5 || answer.Paragraphs.Any(string.IsNullOrWhiteSpace)) problems.Add($"Give one to five paragraphs, none empty, not {answer.Paragraphs.Count}.");

        var exercises = input.Exercises.ToDictionary(e => e.Id);
        foreach (var group in answer.NextTime.GroupBy(l => l.ExerciseId).Where(g => g.Count() > 1))
            problems.Add($"{(exercises.TryGetValue(group.Key, out var e) ? e.Name : $"Exercise {group.Key}")} is in nextTime {group.Count()} times; give it once.");
        foreach (var lift in answer.NextTime)
        {
            if (!exercises.TryGetValue(lift.ExerciseId, out var exercise))
            {
                problems.Add($"nextTime has exercise {lift.ExerciseId}, which isn't in the input's exercises.");
                continue;
            }

            if (exercise.Measure == ExerciseMeasure.Time)
            {
                problems.Add($"{exercise.Name} is timed: leave it out of nextTime.");
                continue;
            }

            var bases = exercise.Times.SelectMany(t => t.Weights).Distinct().ToList();
            problems.AddRange(Lifts.Problems(exercise.Name, lift.Sets, lift.Reps, lift.Weight, lift.RestSeconds, lift.Why, bases, bases.Count == 0, input.HitGoalStep, "in its times"));
        }

        return problems;
    }

    /// <summary>
    /// What taking the answer's plan sets for each exercise's next time (see <see cref="TrainerTarget"/>): it counts until the exercise
    /// is done again after its latest time, with the weight in that time's unit (bodyweight stays bodyweight).
    /// </summary>
    public static IReadOnlyList<(long ExerciseId, TrainerTarget Target)> Targets(AskInput input, AskAnswer answer)
    {
        var exercises = input.Exercises.ToDictionary(e => e.Id);
        return [.. answer.NextTime.Where(l => exercises.TryGetValue(l.ExerciseId, out var e) && e.Times.Count > 0).Select(lift =>
        {
            var exercise = exercises[lift.ExerciseId];
            var latest = exercise.Times[0];
            double? weight = exercise.Times.All(t => t.Weights.Count == 0) && lift.Weight == 0 ? null : lift.Weight;
            return (lift.ExerciseId, new TrainerTarget(latest.WorkoutId, weight, latest.Unit, lift.Reps, lift.Sets, lift.RestSeconds));
        })];
    }
}

public static class AskFacts
{
    /// <summary>How many weeks a question is sent with, this one included.</summary>
    public const int Weeks = 8;

    /// <summary>
    /// What Claude is sent with a question asked at <paramref name="now"/>: the last <see cref="Weeks"/> weeks from <paramref name="sessions"/>
    /// (finished workouts) and their <paramref name="sets"/>, <paramref name="days"/> of food (from <see cref="Calories.Days"/>, covering
    /// them) and the <paramref name="weighIns"/>, with <paramref name="context"/> and the <paramref name="earlier"/> questions.
    /// </summary>
    public static AskInput For(string question, DateTimeOffset now, IReadOnlyList<WorkoutSession> sessions, IReadOnlyList<WorkoutSet> sets,
        IReadOnlyDictionary<long, Exercise> exercises, IReadOnlyList<CalorieDay> days, IReadOnlyList<BodyWeightEntry> weighIns, AskContext context,
        IReadOnlyList<AskEarlier> earlier)
    {
        var today = DateOnly.FromDateTime(now.DateTime);
        var first = Consistency.WeekStart(today).AddDays(-7 * (Weeks - 1));
        var weeks = Enumerable.Range(0, Weeks).Select(i => Week(first.AddDays(7 * i), today, sessions, sets, days, weighIns, context)).ToList();
        return new AskInput(question, today, context.HitGoalStep, Body(now, weighIns, context), weeks, Exercises(first, today, sessions, sets, exercises, weighIns, context),
            Routine(context.Routine, exercises), Next(today, sessions, sets, exercises, context), earlier);
    }

    /// <summary>
    /// One week from <paramref name="monday"/>: its workouts against the routine's gym days, each muscle's hard sets and the muscles
    /// with none, its food on the days over by <paramref name="today"/> (today's is still being eaten), and its average weigh-in.
    /// </summary>
    public static AskWeek Week(DateOnly monday, DateOnly today, IReadOnlyList<WorkoutSession> sessions, IReadOnlyList<WorkoutSet> sets, IReadOnlyList<CalorieDay> days,
        IReadOnlyList<BodyWeightEntry> weighIns, AskContext context)
    {
        var next = monday.AddDays(7);
        var counts = Muscles.Count(Muscles.InWeek(sessions, sets, monday), context.Tags);
        var food = Calories.Span([.. days.Where(d => d.Day >= monday && d.Day < next && d.Day < today)]);
        var weighed = weighIns.Where(w => DateOnly.FromDateTime(w.At.DateTime) is var day && day >= monday && day < next)
            .Select(w => context.Unit == WeightUnit.Kilograms ? WorkoutStats.Kilograms(w.Weight, w.WeightUnit) : WorkoutStats.Pounds(w.Weight, w.WeightUnit)).ToList();
        return new AskWeek(monday, sessions.Count(s => Day(s) >= monday && Day(s) < next), context.Routine?.Week.Count(d => d is not null),
            [.. counts.Where(c => c.Sets > 0)], Muscles.Missing(counts), food.Logged, food.AtMaintenance, Round(food.AverageEaten), Round(food.AverageDeficit),
            weighed.Count == 0 ? null : Math.Round(weighed.Average(), 1));
    }

    /// <summary>
    /// The exercises done from <paramref name="from"/> through <paramref name="to"/>, the latest done first, each with its times there (at
    /// most 8), whether it's gone flat counting every time, and the plan waiting for its next time.
    /// </summary>
    public static IReadOnlyList<AskExercise> Exercises(DateOnly from, DateOnly to, IReadOnlyList<WorkoutSession> sessions, IReadOnlyList<WorkoutSet> sets,
        IReadOnlyDictionary<long, Exercise> exercises, IReadOnlyList<BodyWeightEntry> weighIns, AskContext context)
    {
        var inRange = sessions.Where(s => Day(s) >= from && Day(s) <= to).ToList();
        var startedAt = inRange.ToDictionary(s => s.Id, s => s.StartedAt);
        var latest = weighIns.MaxBy(w => w.At);
        double? bodyPounds = latest is null ? null : WorkoutStats.Pounds(latest.Weight, latest.WeightUnit);
        var list = new List<AskExercise>();
        foreach (var id in sets.Where(s => startedAt.ContainsKey(s.SessionId)).GroupBy(s => s.ExerciseId).OrderByDescending(g => g.Max(s => startedAt[s.SessionId])).Select(g => g.Key))
        {
            if (!exercises.TryGetValue(id, out var exercise)) continue;

            var times = ExerciseHistory.Recent(id, inRange, sets, 0, 8);
            var flat = Plateaus.Of(exercise, ExerciseHistory.Recent(id, sessions.Where(s => Day(s) <= to), sets, 0, int.MaxValue), bodyPounds);
            var said = context.Trainer.TryGetValue(id, out var target) && target.SessionId == times[0].Session.Id
                ? $"{target.Sets} × {target.Reps} at {(target.Weight is double w ? $"{SetSummary.WeightText(w)} {Unit(target.Unit)}" : "bodyweight")}, rest {TimeText.Format(TimeSpan.FromSeconds(target.RestSeconds))}"
                : "";
            list.Add(new AskExercise(id, exercise.Name, exercise.Measure, context.Tags.TryGetValue(id, out var tags) ? Muscles.Text(tags) : "Not tagged yet",
                [.. times.Select(t => new AskTime(t.Session.Id, Day(t.Session), SetSummary.Format(t.Sets, exercise.Measure), t.Sets.Any(s => s.HitGoal),
                    [.. t.Sets.Where(s => s.Weight is not null).Select(s => s.Weight!.Value).Distinct()], t.Sets[^1].WeightUnit))],
                flat?.Workouts ?? 0, flat is null ? "" : $"{SetSummary.Format(flat.Best.Sets, exercise.Measure)} on {Day(flat.Best.Session):yyyy-MM-dd}", said));
        }

        return list;
    }

    /// <summary>My body as of <paramref name="at"/>: the latest weigh-in by then, how my weight moved over the 4 weeks to then, and the goal and maintenance.</summary>
    public static AskBody Body(DateTimeOffset at, IReadOnlyList<BodyWeightEntry> weighIns, AskContext context)
    {
        var latest = weighIns.Where(w => w.At <= at).MaxBy(w => w.At);
        var change = BodyWeight.Trend(weighIns, at.AddDays(-28), at, context.Unit);
        return new AskBody(latest?.Weight, latest?.WeightUnit ?? context.Unit, change is double c ? Math.Round(c, 1) : null, context.CalorieGoal, context.Maintenance,
            context.MaintenanceByScale);
    }

    /// <summary>The routine day by day, Monday first, with its sessions' exercises by name; null without one.</summary>
    public static AskRoutine? Routine(AskRoutinePlan? plan, IReadOnlyDictionary<long, Exercise> exercises) => plan is null ? null
        : new AskRoutine(plan.Name, [.. plan.Week.Select((session, i) => new AskDay(((DayOfWeek)((i + 1) % 7)).ToString(), session?.Name ?? "Rest",
            session is null ? [] : [.. session.Exercises.Select(e => exercises.TryGetValue(e.ExerciseId, out var x) ? x.Name : "")]))]);

    /// <summary>
    /// The routine's next workout: <paramref name="today"/>'s session while there's no finished workout that day, else the next day
    /// with one. Each exercise says what its first set starts from: the plan waiting for it, a step up after a hit goal, or the first
    /// set last time. Null without a routine.
    /// </summary>
    public static AskNext? Next(DateOnly today, IReadOnlyList<WorkoutSession> sessions, IReadOnlyList<WorkoutSet> sets, IReadOnlyDictionary<long, Exercise> exercises,
        AskContext context)
    {
        if (context.Routine is not { } plan) return null;

        var doneToday = sessions.Any(s => Day(s) == today);
        var day = Enumerable.Range(doneToday ? 1 : 0, 7).Select(today.AddDays).FirstOrDefault(d => plan.Week[Schedule.DayIndex(d)] is not null);
        if (day == default || plan.Week[Schedule.DayIndex(day)] is not { } session) return null;

        return new AskNext(day, session.Name, [.. session.Exercises.Where(e => exercises.ContainsKey(e.ExerciseId)).Select(e =>
        {
            var last = ExerciseHistory.Recent(e.ExerciseId, sessions, sets, 0, 1).FirstOrDefault();
            var waiting = context.Trainer.TryGetValue(e.ExerciseId, out var t) && last is not null && t.SessionId == last.Session.Id ? t : null;
            var first = SetSuggestion.For([], last?.Sets ?? [], context.HitGoalStep, waiting);
            var text = first is null ? "nothing yet: not done before"
                : $"{first.Reps ?? 0} at {(first.Weight is double w ? $"{SetSummary.WeightText(w)} {Unit(first.Unit)}" : "bodyweight")}, " +
                  (first.FromTrainer ? "from the plan waiting" : first.RaisedForGoal ? "a step up after a hit goal" : "as the first set last time");
            return new AskNextExercise(e.ExerciseId, exercises[e.ExerciseId].Name, e.PlannedSets, text);
        })]);
    }

    private static DateOnly Day(WorkoutSession session) => DateOnly.FromDateTime(session.StartedAt.DateTime);

    private static int? Round(double? value) => value is double v ? (int)Math.Round(v) : null;

    private static string Unit(WeightUnit unit) => unit == WeightUnit.Kilograms ? "kg" : "lb";
}
