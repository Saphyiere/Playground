using System.Text.Json;
using AdaptiveWorkout.Core;
using AdaptiveWorkout.Core.Assist;
using Playground.AI.Assist;

namespace AdaptiveWorkout.Core.Tests;

public class WeekLookTests
{
    private const long Rows = 1, Squats = 2;
    private static readonly TimeSpan Offset = TimeSpan.FromHours(-8);

    private static readonly Dictionary<long, Exercise> Exercises = new()
    {
        [Rows] = new() { Id = Rows, Name = "Overhand rows", Measure = ExerciseMeasure.Weight },
        [Squats] = new() { Id = Squats, Name = "Squats", Measure = ExerciseMeasure.Weight },
    };

    private static readonly Dictionary<long, MuscleTags> Tags = new()
    {
        [Rows] = new(Rows, [Muscle.Lats, Muscle.UpperBack], [Muscle.RearDelts, Muscle.Biceps], false),
        [Squats] = new(Squats, [Muscle.Quads, Muscle.Glutes], [Muscle.Hamstrings, Muscle.LowerBack], false),
    };

    // Back day on Mondays and Thursdays, leg day on Saturdays.
    private static readonly long?[] Plan = [11, null, null, 11, null, 12, null];

    private static readonly AskRoutinePlan Routine = new("Three days",
    [
        new AskSession("Back day", [new() { ExerciseId = Rows, PlannedSets = 3 }]), null, null, new AskSession("Back day", [new() { ExerciseId = Rows, PlannedSets = 3 }]), null,
        new AskSession("Leg day", [new() { ExerciseId = Squats, PlannedSets = 3 }]), null,
    ]);

    private static (WorkoutSession Session, List<WorkoutSet> Sets) Workout(long id, int month, int day, long exercise, double weight, params int[] reps)
    {
        var start = new DateTimeOffset(2026, month, day, 18, 0, 0, Offset);
        return (new WorkoutSession { Id = id, StartedAt = start, FinishedAt = start.AddHours(1) },
            [.. reps.Select((r, i) => new WorkoutSet { Id = id * 100 + i, SessionId = id, ExerciseId = exercise, Order = i + 1, Weight = weight, Reps = r })]);
    }

    // Two weeks of rows going up, and the week of Nov 2: rows Monday and Thursday, Saturday's legs missed.
    private static readonly (WorkoutSession Session, List<WorkoutSet> Sets)[] Workouts =
    [
        Workout(1, 10, 19, Rows, 95, 10, 10, 10),
        Workout(2, 10, 26, Rows, 100, 10, 10, 9),
        Workout(3, 11, 2, Rows, 100, 10, 10, 10),
        Workout(4, 11, 5, Rows, 105, 10, 10, 8),
    ];

    private static WeekInput Input(DateTimeOffset now)
    {
        var context = new AskContext(5, WeightUnit.Pounds, 1800, 2400, null, Tags, new Dictionary<long, TrainerTarget>(), Routine);
        var weighIns = new List<BodyWeightEntry> { new() { At = new DateTimeOffset(2026, 11, 3, 7, 0, 0, Offset), Weight = 200 }, new() { At = new DateTimeOffset(2026, 11, 10, 7, 0, 0, Offset), Weight = 199 } };
        return WeekFacts.For(new DateOnly(2026, 11, 2), now, [.. Workouts.Select(w => w.Session)], [.. Workouts.SelectMany(w => w.Sets)], Exercises, [], weighIns, context);
    }

    [Fact]
    public void For_SetsTheWeeksDaysAgainstThePlan_AndTheWeeksBefore()
    {
        var input = Input(new DateTimeOffset(2026, 11, 9, 8, 0, 0, Offset));

        Assert.Equal(new DateOnly(2026, 11, 2), input.Monday);
        Assert.Equal((2, 3), (input.Week.Workouts, input.Week.PlannedDays));
        Assert.Equal(["Back day", "Rest", "Rest", "Back day", "Rest", "Leg day", "Rest"], input.Days.Select(d => d.Planned));
        Assert.Equal([1, 0, 0, 1, 0, 0, 0], input.Days.Select(d => d.Workouts));
        Assert.Equal(["Overhand rows"], input.Days[0].Exercises);
        Assert.Equal([new DateOnly(2026, 10, 5), new DateOnly(2026, 10, 12), new DateOnly(2026, 10, 19), new DateOnly(2026, 10, 26)], input.Before.Select(w => w.Monday));
        Assert.Equal(6, input.Week.Muscles.Single(m => m.Muscle == Muscle.Lats).Sets);
        Assert.Contains(Muscle.Quads, input.Week.Missing);
    }

    [Fact]
    public void For_HasTheWeeksExercisesWithTheirEarlierTimes_AndTheBodyAtItsEnd()
    {
        var input = Input(new DateTimeOffset(2026, 11, 12, 8, 0, 0, Offset));

        Assert.Equal(["Overhand rows"], input.Exercises.Select(e => e.Name));
        Assert.Equal([4L, 3L, 2L, 1L], input.Exercises[0].Times.Select(t => t.WorkoutId));
        // Nov 10's weigh-in came after the week was out.
        Assert.Equal(200, input.Body.Weight);
        Assert.Equal((new DateOnly(2026, 11, 12), "Back day"), (input.Next!.Date, input.Next.Session));
    }

    [Fact]
    public void Due_IsLastWeekOnMonday_AndThisWeekOnceItsLastPlannedWorkoutIsDone()
    {
        var sessions = Workouts.Select(w => w.Session).ToList();
        var saturday = new DateOnly(2026, 11, 7);
        var legs = new WorkoutSession { Id = 9, StartedAt = new DateTimeOffset(2026, 11, 7, 10, 0, 0, Offset), FinishedAt = new DateTimeOffset(2026, 11, 7, 11, 0, 0, Offset) };

        Assert.Equal(new DateOnly(2026, 11, 2), WeekFacts.Due(new DateOnly(2026, 11, 9), sessions, Plan, new HashSet<DateOnly>()));
        // Saturday's legs not done yet: last week's, which is kept, so none.
        Assert.Null(WeekFacts.Due(saturday, sessions, Plan, new HashSet<DateOnly> { new(2026, 10, 26) }));
        Assert.Equal(new DateOnly(2026, 11, 2), WeekFacts.Due(saturday, [.. sessions, legs], Plan, new HashSet<DateOnly> { new(2026, 10, 26) }));
        Assert.Equal(new DateOnly(2026, 11, 2), WeekFacts.Due(new DateOnly(2026, 11, 8), sessions, Plan, new HashSet<DateOnly> { new(2026, 10, 26) }));
        Assert.Null(WeekFacts.Due(new DateOnly(2026, 11, 9), sessions, Plan, new HashSet<DateOnly> { new(2026, 11, 2) }));
        // Without a routine, only last week; with no workouts in it, none.
        Assert.Null(WeekFacts.Due(saturday, sessions, null, new HashSet<DateOnly> { new(2026, 10, 26) }));
        Assert.Null(WeekFacts.Due(new DateOnly(2026, 11, 23), sessions, Plan, new HashSet<DateOnly>()));
    }

    [Fact]
    public void Check_WantsTheGymDaysCaloriesAndOneOrTwoThings()
    {
        var input = Input(new DateTimeOffset(2026, 11, 9, 8, 0, 0, Offset));
        var good = new WeekLook("2 of 3 planned days; Saturday's leg day was missed.", ["Overhand rows: 105 for the first time."], "Nothing trained quads.",
            "No food was logged.", ["Keep Saturday's leg day."]);

        Assert.Empty(new WeekTask().Check(input, good));
        Assert.Equal(["gymDays is empty.", "calories is empty.", "Give one or two things for this week, none empty, not 3."],
            new WeekTask().Check(input, good with { GymDays = "", Calories = " ", ThisWeek = ["a", "b", "c"] }));
    }

    [Fact]
    public void Schema_IsOneTheApiTakes()
    {
        var schema = JsonSerializer.SerializeToElement(AssistSchema.For(typeof(Reply<WeekLook>)));
        var answer = schema.GetProperty("properties").GetProperty("answer").GetProperty("properties");

        Assert.Equal(["gymDays", "wentUp", "missed", "calories", "thisWeek"], answer.EnumerateObject().Select(p => p.Name));
    }
}
