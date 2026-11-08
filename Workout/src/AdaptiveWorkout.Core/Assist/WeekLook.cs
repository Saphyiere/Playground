using System.ComponentModel;
using Playground.AI.Assist;

namespace AdaptiveWorkout.Core.Assist;

/// <summary>
/// What Claude is told to look back at a week (see <see cref="WeekTask"/>): the week, Monday to Sunday (to today, while it's this week),
/// each of its days against the routine's plan, the 4 weeks before it to set it against, the exercises done in it with their times over
/// the 8 weeks to its end, my body at its end, the routine, and the routine's next workout.
/// </summary>
public sealed record WeekInput(DateOnly Monday, DateOnly Today, double HitGoalStep, AskWeek Week, IReadOnlyList<WeekDay> Days, IReadOnlyList<AskWeek> Before,
    IReadOnlyList<AskExercise> Exercises, AskBody Body, AskRoutine? Routine, AskNext? Next);

/// <summary>
/// One day of the week: its planned session ("Rest" on a rest day; empty without a routine), how many workouts were done on it, and their
/// exercises.
/// </summary>
public sealed record WeekDay(DateOnly Date, string Planned, int Workouts, IReadOnlyList<string> Exercises);

/// <summary>The week, looked back: its gym days against the plan, what went up, the muscles it missed, calories against the scale, and what's next.</summary>
public sealed record WeekLook(
    [property: Description("Gym days against the plan, in one line with the numbers: \"5 of 6 planned days; Wednesday's leg day was missed.\"")]
    string GymDays,
    [property: Description("What went up against the weeks before, one short line each with the numbers: \"Overhand rows: 3 × 10 at 105, up from 100.\" Empty when nothing did.")]
    IReadOnlyList<string> WentUp,
    [property: Description("The muscles the week missed, as a plain fact in one line. Empty when every muscle got sets.")]
    string Missed,
    [property: Description("Calories against the scale, in one or two plain sentences with the numbers.")]
    string Calories,
    [property: Description("One or two things for the coming week, by the owner's method: one short sentence each.")]
    IReadOnlyList<string> ThisWeek);

/// <summary>
/// Looks back at a training week, for a card on Today and the page it opens: asked as the app opens on the Monday after, or once the week's
/// last planned workout is done (see <see cref="WeekFacts.Due"/>), and kept.
/// </summary>
public sealed class WeekTask : AssistTask<WeekInput, WeekLook>
{
    public override string Name => "workout.week";

    public override int Version => 1;

    // It's asked as the app opens and read when it's ready, and a week of summed-up numbers doesn't need the deepest thinking.
    public override EffortLevel Effort => EffortLevel.Medium;

    public override int MaxTokens => 16_000;

    public override string Instructions => """
        Look back at the owner's training week, for a card on their phone's home screen and the page it opens.

        The input:
        - monday: the week looked back at, Monday to Sunday; today: when it's asked, which can be before the week is out.
        - week: its finished workouts against the gym days their routine plans; each muscle's hard sets, counting 1 for each set of an
          exercise it's the main muscle of and half for each it helps with (only muscles with some), and missing, the muscles with
          none; the days over that food was logged and the days forgotten, which count at maintenance, with what they averaged eaten
          and under maintenance; and the average weigh-in. before: the 4 weeks before it, the same way, oldest first.
        - days: each day of the week, with the session the routine planned ("Rest", or empty without a routine), how many workouts
          were done, and their exercises.
        - exercises: each one done in the week, with what it trains and its times over the 8 weeks to its end, newest first: its sets
          compactly, whether a goal was hit, and the weights used (none at bodyweight, negative for assistance). flatWorkouts is how
          many workouts it's gone without beating its best (4 or more is flat), with best that workout.
        - body: the latest weigh-in at the week's end, how weight moved over the 4 weeks to then by the line through the weigh-ins,
          the daily calorie goal, and maintenance by the formula and by the scale (null without enough).
        - routine: the routine in use, day by day. next: its next workout, with what each exercise's first set starts from.
        - hitGoalStep: how much a weight moves, in the set's unit.

        Their method is the rule. They pick a weight and aim for three sets of 10, or the sets planned. When every set hits its reps
        the next time goes up one step; when reps fall short, the weight stays until they hit it. They keep their routine as it is
        until they're lighter and training steadily: never suggest swapping, adding or dropping an exercise. While they eat below
        maintenance, slow progress and flat lifts are expected.

        - gymDays: the week's gym days against the planned ones, naming a planned day that was missed. Without a routine, how many
          days they trained.
        - wentUp: each lift that went up against its earlier times, more weight or more reps at the same weight, with its numbers.
          Empty when nothing did: never make progress up.
        - missed: the muscles with no sets this week, as a plain fact. Empty when every muscle got sets. Point it out only: never
          suggest an exercise for it.
        - calories: the week's food against maintenance, and what the scale did by the line through the weigh-ins. When the logs say
          more should have gone than the scale shows, say food is probably going unlogged or maintenance is lower than the formula's,
          with the scale's number. Say plainly when food wasn't logged.
        - thisWeek: one or two things for the coming week, by their method: a lift due to go up, reps to chase on a flat one, a planned
          day to keep, food to log.
        - The summary: one sentence on the week, for the card on the home screen.

        Write plainly, like a coach's note in a training log: warm but not gushing, no exclamation marks, no emoji.
        """;

    public override IReadOnlyList<string> Check(WeekInput input, WeekLook answer)
    {
        var problems = new List<string>();
        if (string.IsNullOrWhiteSpace(answer.GymDays)) problems.Add("gymDays is empty.");
        if (string.IsNullOrWhiteSpace(answer.Calories)) problems.Add("calories is empty.");
        if (answer.ThisWeek.Count is < 1 or > 2 || answer.ThisWeek.Any(string.IsNullOrWhiteSpace)) problems.Add($"Give one or two things for this week, none empty, not {answer.ThisWeek.Count}.");
        if (answer.WentUp.Count > 6) problems.Add($"wentUp has {answer.WentUp.Count} lines; keep it to six at most.");
        if (input.Week.Missing.Count == 0 && !string.IsNullOrWhiteSpace(answer.Missed)) problems.Add("missed should be empty: every muscle got sets this week.");
        return problems;
    }
}

public static class WeekFacts
{
    /// <summary>How many weeks before the one looked back at go with it.</summary>
    public const int Before = 4;

    /// <summary>
    /// What Claude is sent to look back at the week from <paramref name="monday"/>, asked at <paramref name="now"/>: the pieces Ask Claude
    /// sends (see <see cref="AskFacts"/>), for that week.
    /// </summary>
    public static WeekInput For(DateOnly monday, DateTimeOffset now, IReadOnlyList<WorkoutSession> sessions, IReadOnlyList<WorkoutSet> sets,
        IReadOnlyDictionary<long, Exercise> exercises, IReadOnlyList<CalorieDay> days, IReadOnlyList<BodyWeightEntry> weighIns, AskContext context)
    {
        var today = DateOnly.FromDateTime(now.DateTime);
        var sunday = monday.AddDays(6);
        var week = AskFacts.Week(monday, today, sessions, sets, days, weighIns, context);
        var before = Enumerable.Range(1, Before).Reverse().Select(i => AskFacts.Week(monday.AddDays(-7 * i), today, sessions, sets, days, weighIns, context)).ToList();
        var weekDays = Enumerable.Range(0, 7).Select(i =>
        {
            var date = monday.AddDays(i);
            var done = sessions.Where(s => DateOnly.FromDateTime(s.StartedAt.DateTime) == date).Select(s => s.Id).ToHashSet();
            var names = sets.Where(s => done.Contains(s.SessionId)).Select(s => s.ExerciseId).Distinct().Select(id => exercises.TryGetValue(id, out var e) ? e.Name : "").ToList();
            return new WeekDay(date, context.Routine is null ? "" : context.Routine.Week[i]?.Name ?? "Rest", done.Count, names);
        }).ToList();

        // The exercises done in the week, each with its times over the 8 weeks to its end; and my body at its end, or now while it's going.
        var inWeek = AskFacts.Exercises(monday.AddDays(-7 * (AskFacts.Weeks - 1)), sunday, sessions, sets, exercises, weighIns, context).Where(e => e.Times.Any(t => t.Date >= monday)).ToList();
        var end = sunday < today ? new DateTimeOffset(monday.AddDays(7).ToDateTime(TimeOnly.MinValue), now.Offset) : now;
        return new WeekInput(monday, today, context.HitGoalStep, week, weekDays, before, inWeek, AskFacts.Body(end, weighIns, context), AskFacts.Routine(context.Routine, exercises),
            AskFacts.Next(today, sessions, sets, exercises, context));
    }

    /// <summary>
    /// The week due a look back at <paramref name="today"/>: this week once its last planned workout is done (no planned day after today,
    /// and today's done when it's one), else last week; null when the one due had no workouts or is already <paramref name="kept"/>.
    /// Without a routine, only last week.
    /// </summary>
    /// <param name="sessions">The finished workouts.</param>
    /// <param name="routineWeek">The routine in use, Monday first: each day's session, or null for a rest day. Null without a routine.</param>
    /// <param name="kept">The weeks (their Mondays) already looked back at.</param>
    public static DateOnly? Due(DateOnly today, IReadOnlyList<WorkoutSession> sessions, IReadOnlyList<long?>? routineWeek, IReadOnlySet<DateOnly> kept)
    {
        var thisMonday = Consistency.WeekStart(today);
        var index = Schedule.DayIndex(today);
        bool Trained(DateOnly from, DateOnly to) => sessions.Any(s => DateOnly.FromDateTime(s.StartedAt.DateTime) is var day && day >= from && day <= to);

        if (routineWeek is not null && routineWeek.Any(d => d is not null) && !routineWeek.Skip(index + 1).Any(d => d is not null)
            && (routineWeek[index] is null || Trained(today, today)) && Trained(thisMonday, today))
            return kept.Contains(thisMonday) ? null : thisMonday;

        var lastMonday = thisMonday.AddDays(-7);
        return Trained(lastMonday, lastMonday.AddDays(6)) && !kept.Contains(lastMonday) ? lastMonday : null;
    }
}
