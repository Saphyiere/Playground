using System.Text.Json;
using AdaptiveWorkout.Core;
using AdaptiveWorkout.Core.Assist;
using Playground.AI.Assist;

namespace AdaptiveWorkout.Core.Tests;

public class AskTests
{
    private const long Rows = 1, Dips = 2, Walk = 3;

    // Sunday, Nov 8: the 8 weeks run from Monday, Sep 14.
    private static readonly DateTimeOffset Now = new(2026, 11, 8, 17, 0, 0, TimeSpan.FromHours(-8));

    private static readonly Dictionary<long, Exercise> Exercises = new()
    {
        [Rows] = new() { Id = Rows, Name = "Overhand rows", Measure = ExerciseMeasure.Weight },
        [Dips] = new() { Id = Dips, Name = "Dips", Measure = ExerciseMeasure.Weight },
        [Walk] = new() { Id = Walk, Name = "Walk", Measure = ExerciseMeasure.Time },
    };

    private static readonly Dictionary<long, MuscleTags> Tags = new() { [Rows] = new(Rows, [Muscle.Lats, Muscle.UpperBack], [Muscle.RearDelts, Muscle.Biceps], false) };

    private static (WorkoutSession Session, List<WorkoutSet> Sets) Workout(long id, int month, int day, params (long Exercise, double? Weight, int Reps, bool Goal)[] sets)
    {
        var start = new DateTimeOffset(2026, month, day, 18, 0, 0, TimeSpan.FromHours(-7));
        return (new WorkoutSession { Id = id, StartedAt = start, FinishedAt = start.AddHours(1) },
            [.. sets.Select((s, i) => new WorkoutSet { Id = id * 100 + i, SessionId = id, ExerciseId = s.Exercise, Order = i + 1, Weight = s.Weight, Reps = s.Reps,
                HitGoal = s.Goal, DurationSeconds = s.Exercise == Walk ? 1800 : null })]);
    }

    // Before the weeks; the Monday before last; this Monday, rows and dips; this Wednesday, a walk.
    private static readonly (WorkoutSession Session, List<WorkoutSet> Sets)[] Workouts =
    [
        Workout(1, 9, 7, (Rows, 90, 10, false), (Rows, 90, 10, false), (Rows, 90, 10, false)),
        Workout(2, 10, 26, (Rows, 100, 10, false), (Rows, 100, 10, false), (Rows, 100, 10, true)),
        Workout(3, 11, 2, (Rows, 105, 10, false), (Rows, 105, 10, false), (Rows, 105, 9, false), (Dips, null, 12, false), (Dips, null, 12, false), (Dips, null, 10, false)),
        Workout(4, 11, 4, (Walk, null, 0, false)),
    ];

    // Back day on Mondays, rest the other days, unless a Sunday session is given.
    private static AskRoutinePlan Routine(AskSession? sunday = null) =>
        new("Back and walk", [new AskSession("Back day", [new() { ExerciseId = Rows, PlannedSets = 3 }, new() { ExerciseId = Dips }]), null, null, null, null, null, sunday]);

    private static AskInput Input(AskRoutinePlan? routine = null, Dictionary<long, TrainerTarget>? trainer = null, IEnumerable<WorkoutSession>? extra = null)
    {
        // This week's food: 1,800 Monday to Wednesday, Thursday forgotten, 2,000 Friday and Saturday, and today's still going.
        var days = new List<CalorieDay>
        {
            new(new DateOnly(2026, 11, 2), 1800, 2400), new(new DateOnly(2026, 11, 3), 1800, 2400), new(new DateOnly(2026, 11, 4), 1800, 2400),
            new(new DateOnly(2026, 11, 5), null, 2400, Forgotten: true), new(new DateOnly(2026, 11, 6), 2000, 2400), new(new DateOnly(2026, 11, 7), 2000, 2400),
            new(new DateOnly(2026, 11, 8), 600, 2400),
        };
        BodyWeightEntry Weighed(int month, int day, double pounds) => new() { At = new DateTimeOffset(2026, month, day, 7, 0, 0, TimeSpan.FromHours(-7)), Weight = pounds };
        var weighIns = new List<BodyWeightEntry> { Weighed(10, 10, 203), Weighed(10, 20, 201), Weighed(11, 3, 200), Weighed(11, 6, 199) };
        var context = new AskContext(5, WeightUnit.Pounds, 1800, 2400, 2150, Tags, trainer ?? [], routine ?? Routine());
        return AskFacts.For("How's it going?", Now, [.. Workouts.Select(w => w.Session), .. extra ?? []], [.. Workouts.SelectMany(w => w.Sets)], Exercises, days, weighIns,
            context, [new AskEarlier(new DateOnly(2026, 11, 1), "What muscles am I missing?", "Calves.")]);
    }

    [Fact]
    public void For_SumsUpEachOfTheEightWeeks()
    {
        var input = Input();
        var thisWeek = input.Weeks[^1];

        Assert.Equal(8, input.Weeks.Count);
        Assert.Equal(new DateOnly(2026, 9, 14), input.Weeks[0].Monday);
        Assert.Equal((new DateOnly(2026, 11, 2), 2, 1), (thisWeek.Monday, thisWeek.Workouts, thisWeek.PlannedDays));
        Assert.Equal(1, input.Weeks[^2].Workouts);
        Assert.Equal(3, thisWeek.Muscles.Single(m => m.Muscle == Muscle.Lats).Sets);
        Assert.DoesNotContain(thisWeek.Muscles, m => m.Sets == 0);
        Assert.Contains(Muscle.Chest, thisWeek.Missing);
        Assert.Equal((5, 1, 1967, 433), (thisWeek.DaysLogged, thisWeek.DaysForgotten, thisWeek.AverageEaten, thisWeek.AverageUnderMaintenance));
        Assert.Equal(199.5, thisWeek.Weight);
        Assert.Null(input.Weeks[0].Weight);
    }

    [Fact]
    public void For_ListsTheExercisesDoneInTheWeeks_LatestFirst_WithTheirTimesAndWhatTheyTrain()
    {
        var input = Input(trainer: new() { [Rows] = new TrainerTarget(3, 105, WeightUnit.Pounds, 10, 3, 120) });
        var rows = input.Exercises.Single(e => e.Id == Rows);

        Assert.Equal("Walk", input.Exercises[0].Name);
        Assert.Equal([3L, 2L], rows.Times.Select(t => t.WorkoutId));
        Assert.Equal("10, 10, 9 at 105", rows.Times[0].Sets);
        Assert.Equal([105d], rows.Times[0].Weights);
        Assert.Equal([false, true], rows.Times.Select(t => t.HitGoal));
        Assert.Equal("Lats, upper back · helps rear delts, biceps", rows.Muscles);
        Assert.Equal("3 × 10 at 105 lb, rest 2:00", rows.TrainerSaid);
        Assert.Equal("Not tagged yet", input.Exercises.Single(e => e.Id == Dips).Muscles);
        Assert.Empty(input.Exercises.Single(e => e.Id == Dips).Times[0].Weights);
        // The line through Oct 20's 201, Nov 3's 200 and Nov 6's 199 (Oct 10's is over 4 weeks ago) falls about a pound every 10 days.
        Assert.Equal((199d, -2.9), (input.Body.Weight!.Value, input.Body.FourWeekChange!.Value));
        Assert.Equal(("What muscles am I missing?", 2150), (input.Earlier[0].Question, input.Body.MaintenanceByScale!.Value));
    }

    [Fact]
    public void For_TheNextWorkoutIsTheNextPlannedDay_WithWhatEachFirstSetStartsFrom()
    {
        var waiting = Input(trainer: new() { [Rows] = new TrainerTarget(3, 110, WeightUnit.Pounds, 10, 3, 120) }).Next!;
        var plain = Input().Next!;

        Assert.Equal((new DateOnly(2026, 11, 9), "Back day"), (waiting.Date, waiting.Session));
        Assert.Equal(["10 at 110 lb, from the plan waiting", "12 at bodyweight, as the first set last time"], waiting.Exercises.Select(e => e.FirstSet));
        Assert.Equal("10 at 105 lb, as the first set last time", plain.Exercises[0].FirstSet);
        Assert.Equal([3, null], waiting.Exercises.Select(e => e.PlannedSets));
        Assert.Equal(["Monday", "Back day"], new[] { Input().Routine!.Week[0].Day, Input().Routine!.Week[0].Session });
    }

    [Fact]
    public void For_TodaysSessionIsNext_UntilAWorkoutIsDoneToday()
    {
        var sunday = Routine(new AskSession("Walk day", [new() { ExerciseId = Walk }]));
        var doneToday = new WorkoutSession { Id = 9, StartedAt = Now.AddHours(-2), FinishedAt = Now.AddHours(-1) };

        Assert.Equal(new DateOnly(2026, 11, 8), Input(sunday).Next!.Date);
        Assert.Equal(new DateOnly(2026, 11, 9), Input(sunday, extra: [doneToday]).Next!.Date);
    }

    [Fact]
    public void Problems_HoldsEachLiftToTheMethod()
    {
        var input = Input();
        AskLift Lift(long exercise, double weight, int reps = 10) => new(exercise, 3, reps, weight, 120, "From its last time");

        Assert.Empty(new AskTask().Check(input, new AskAnswer(["Up a step."], [Lift(Rows, 110), Lift(Dips, 0, 13)])));
        var problems = new AskTask().Check(input, new AskAnswer([], [Lift(Rows, 107), Lift(Dips, 10), Lift(Walk, 0), Lift(99, 0), Lift(Rows, 105)]));

        Assert.Contains("Give one to five paragraphs, none empty, not 0.", problems);
        Assert.Contains("Overhand rows is in nextTime 2 times; give it once.", problems);
        Assert.Contains("Overhand rows: 107 isn't whole steps of 5 from a weight in its times (100, 105), by at most a tenth of it or one step.", problems);
        Assert.Contains("Dips is done at bodyweight: its weight should be 0, not 10.", problems);
        Assert.Contains("Walk is timed: leave it out of nextTime.", problems);
        Assert.Contains("nextTime has exercise 99, which isn't in the input's exercises.", problems);
    }

    [Fact]
    public void Targets_CountFromTheLatestTime_AndBodyweightStaysBodyweight()
    {
        var input = Input();
        var targets = AskPlan.Targets(input, new AskAnswer(["Up a step."], [new(Rows, 3, 10, 110, 150, "Up"), new(Dips, 3, 13, 0, 90, "More reps")]));

        Assert.Equal([(Rows, new TrainerTarget(3, 110, WeightUnit.Pounds, 10, 3, 150)), (Dips, new TrainerTarget(3, null, WeightUnit.Pounds, 13, 3, 90))], targets);
    }

    [Fact]
    public void Schema_IsOneTheApiTakes_AndTheInputTravelsAsJson()
    {
        var schema = JsonSerializer.SerializeToElement(AssistSchema.For(typeof(Reply<AskAnswer>)));
        var answer = schema.GetProperty("properties").GetProperty("answer").GetProperty("properties");
        var json = JsonSerializer.Serialize(Input(), AssistSchema.Json);

        Assert.Equal(["paragraphs", "nextTime"], answer.EnumerateObject().Select(p => p.Name));
        Assert.Equal(["exerciseId", "sets", "reps", "weight", "restSeconds", "why"], answer.GetProperty("nextTime").GetProperty("items").GetProperty("properties").EnumerateObject().Select(p => p.Name));
        Assert.Contains("\"question\":\"How's it going?\"", json.Replace("\\u0027", "'"));
        Assert.Contains("\"missing\":[\"chest\"", json);
    }
}
