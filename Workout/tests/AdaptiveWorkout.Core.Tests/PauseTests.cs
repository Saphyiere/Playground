using AdaptiveWorkout.Core;
using AdaptiveWorkout.Core.Assist;

namespace AdaptiveWorkout.Core.Tests;

public class PauseTests
{
    private const long Rows = 1;
    private static readonly DateTimeOffset Start = new(2026, 11, 16, 18, 0, 0, TimeSpan.FromHours(-8));

    [Fact]
    public void Ran_LeavesOutTheTimeASetWasHeldWhilePaused()
    {
        var set = new WorkoutSet { ExerciseId = Rows, StartedAt = Start, StoppedAt = Start.AddSeconds(100), PausedSeconds = 60 };

        Assert.Equal(TimeSpan.FromSeconds(40), set.Ran());
    }

    [Fact]
    public void Ran_WhileASetRuns_GoesToNow()
    {
        var set = new WorkoutSet { ExerciseId = Rows, StartedAt = Start, PausedSeconds = 20 };

        Assert.Null(set.Ran());
        Assert.Equal(TimeSpan.FromSeconds(25), set.Ran(Start.AddSeconds(45)));
    }

    [Fact]
    public void Ran_IsEmptyForASetLoggedAfterward_AndNeverLessThanNothing()
    {
        Assert.Null(new WorkoutSet { ExerciseId = Rows, Reps = 10 }.Ran());
        Assert.Equal(TimeSpan.Zero, new WorkoutSet { ExerciseId = Rows, StartedAt = Start, StoppedAt = Start.AddSeconds(10), PausedSeconds = 30 }.Ran());
    }

    // A set held for three minutes while the workout was edited ran 40 seconds, not 3:40: the look back doesn't call it a late tap on Stop.
    [Fact]
    public void LookBack_DoesntFlagASetAsLongForTheTimeItWasHeld()
    {
        var exercises = new Dictionary<long, Exercise> { [Rows] = new() { Id = Rows, Name = "Rows", Measure = ExerciseMeasure.Weight } };
        var session = new WorkoutSession { Id = 1, StartedAt = Start, FinishedAt = Start.AddMinutes(20) };
        WorkoutSet Set(int order, int fromSecond, int seconds, int paused = 0) => new()
        {
            Id = order, SessionId = 1, ExerciseId = Rows, Order = order, Weight = 100, Reps = 10, PausedSeconds = paused,
            StartedAt = Start.AddSeconds(fromSecond), StoppedAt = Start.AddSeconds(fromSecond + seconds),
        };
        WorkoutSet[] sets = [Set(1, 0, 40), Set(2, 160, 220, paused: 180), Set(3, 500, 40)];

        var context = new LookBackContext(5, new Dictionary<long, int>(), new Dictionary<long, TrainerTarget>(), null, WeightUnit.Pounds, 1800, null);

        var input = LookBackFacts.For(session, "Back day", sets, [], exercises, [session], sets, [], context);

        var held = input.Workout.Sets[1];
        Assert.Equal(40, held.Seconds);
        Assert.DoesNotContain(held.Flags, f => f.Kind == SetFlagKind.LongSet);
    }
}
