using AdaptiveWorkout.Core;
using AdaptiveWorkout.Core.Assist;

namespace AdaptiveWorkout.Core.Tests;

public class WarmUpTests
{
    private const long Curls = 1;
    private static readonly Dictionary<long, Exercise> Exercises = new() { [Curls] = new() { Id = Curls, Name = "Curls", Measure = ExerciseMeasure.Weight } };
    private static readonly DateTimeOffset Start = new(2026, 11, 2, 18, 0, 0, TimeSpan.FromHours(-8));

    private static WorkoutSession Session(long id) => new() { Id = id, StartedAt = Start.AddDays(id), FinishedAt = Start.AddDays(id).AddHours(1) };

    // A set of curls in workout `session`, started `fromSecond` seconds in and run for 40.
    private static WorkoutSet Set(long session, int order, double weight, int reps, bool warmUp = false, bool goal = false, int fromSecond = 0) => new()
    {
        Id = session * 100 + order, SessionId = session, ExerciseId = Curls, Order = order, Weight = weight, Reps = reps, WarmUp = warmUp, HitGoal = goal,
        StartedAt = Start.AddDays(session).AddSeconds(fromSecond), StoppedAt = Start.AddDays(session).AddSeconds(fromSecond + 40),
    };

    [Fact]
    public void Recent_LeavesOutWarmUpSets_AndAWorkoutWithNothingElseOfIt()
    {
        WorkoutSet[] sets = [Set(1, 1, 15, 12, warmUp: true), Set(1, 2, 30, 10), Set(1, 3, 30, 9), Set(2, 1, 15, 12, warmUp: true)];

        var history = ExerciseHistory.Recent(Curls, [Session(1), Session(2)], sets, 0, 5);

        Assert.Equal(1, Assert.Single(history).Session.Id);
        Assert.Equal([30d, 30d], history[0].Sets.Select(s => s.Weight!.Value));
    }

    [Fact]
    public void Suggestion_PassesOverWarmUps_ThisSessionAndLastTime()
    {
        WorkoutSet[] lastTime = [Set(1, 1, 15, 12, warmUp: true), Set(1, 2, 30, 10), Set(1, 3, 30, 10, goal: true)];
        WorkoutSet[] noGoal = [Set(1, 1, 15, 12, warmUp: true), Set(1, 2, 30, 10), Set(1, 3, 30, 9)];
        WorkoutSet[] warmedUp = [Set(2, 1, 15, 12, warmUp: true)];

        Assert.Equal(new SetValues(35, WeightUnit.Pounds, 10, true), SetSuggestion.For(warmedUp, lastTime, 5));
        Assert.Equal(new SetValues(30, WeightUnit.Pounds, 10, false), SetSuggestion.For(warmedUp, noGoal, 5));
        Assert.Equal(new SetValues(30, WeightUnit.Pounds, 8, false), SetSuggestion.For([.. warmedUp, Set(2, 2, 30, 8)], noGoal, 5));
        Assert.Null(SetSuggestion.For(warmedUp, warmedUp, 5));
    }

    [Fact]
    public void Improvement_IsntDraggedDownByAWarmUp()
    {
        WorkoutSet[] sets = [Set(1, 1, 30, 10), Set(1, 2, 30, 10), Set(2, 1, 15, 12, warmUp: true), Set(2, 2, 30, 10), Set(2, 3, 30, 10)];

        var (change, compared) = Progress.Improvement(Session(2), [Session(1), Session(2)], sets, Exercises, []);

        Assert.Equal((0d, 1), (Math.Round(change!.Value, 6), compared));
    }

    [Fact]
    public void MusclesVolumeAndTheUsualRest_LeaveWarmUpsOut()
    {
        var tags = new Dictionary<long, MuscleTags> { [Curls] = new(Curls, [Muscle.Biceps], [Muscle.Forearms], false) };
        // A warm-up, 20 seconds' rest, then one working set.
        WorkoutSet[] sets = [Set(1, 1, 15, 12, warmUp: true), Set(1, 2, 30, 10, fromSecond: 60)];

        Assert.Equal(1, Muscles.Count(sets, tags).Single(m => m.Muscle == Muscle.Biceps).Sets);
        Assert.Equal(300, WorkoutStats.Volume(sets, WeightUnit.Pounds));
        Assert.Null(Rest.Typical(sets, Curls));
        Assert.Equal(TimeSpan.FromSeconds(30), Rest.Typical([Set(1, 1, 15, 12), Set(1, 2, 30, 10, fromSecond: 60)], Curls));
    }

    [Fact]
    public void AnExerciseOnlyWarmedUpWith_IsntOneClaudeIsToldWasDone()
    {
        // Tuesday, Nov 3: curls, with a warm-up. Wednesday: nothing but a warm-up of them.
        WorkoutSet[] sets = [Set(1, 1, 15, 12, warmUp: true), Set(1, 2, 30, 10), Set(2, 1, 15, 12, warmUp: true)];
        WorkoutSession[] sessions = [Session(1), Session(2)];
        var context = new AskContext(5, WeightUnit.Pounds, 1800, 2400, null, new Dictionary<long, MuscleTags>(), new Dictionary<long, TrainerTarget>(), null);
        var monday = new DateOnly(2026, 11, 2);

        var done = Assert.Single(AskFacts.Exercises(monday, monday.AddDays(6), sessions, sets, Exercises, [], context));
        var onlyWarmedUp = AskFacts.Exercises(monday.AddDays(2), monday.AddDays(6), sessions, sets, Exercises, [], context);
        var week = WeekFacts.For(monday, Start.AddDays(7), sessions, sets, Exercises, [], [], context);

        Assert.Equal("10 at 30", Assert.Single(done.Times).Sets);
        Assert.Empty(onlyWarmedUp);
        Assert.Equal([["Curls"], []], week.Days.Skip(1).Take(2).Select(d => d.Exercises));
    }
}
