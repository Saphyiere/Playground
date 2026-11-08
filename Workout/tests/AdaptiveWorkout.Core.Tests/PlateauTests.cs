using AdaptiveWorkout.Core;

namespace AdaptiveWorkout.Core.Tests;

public class PlateauTests
{
    private static readonly Exercise Rows = new() { Id = 1, Name = "Overhand rows", Measure = ExerciseMeasure.Weight };
    private static readonly Exercise PullUps = new() { Id = 2, Name = "Pull-ups", Measure = ExerciseMeasure.Weight };
    private static readonly DateTimeOffset Start = new(2026, 9, 1, 18, 0, 0, TimeSpan.FromHours(-7));

    // Workouts a few days apart, oldest first, each with its sets as (weight, reps).
    private static List<PastSession> History(params (double? Weight, int Reps)[][] workouts) =>
        [.. workouts.Select((sets, i) => new PastSession(new WorkoutSession { Id = i + 1, StartedAt = Start.AddDays(3 * i) },
            [.. sets.Select((s, o) => new WorkoutSet { SessionId = i + 1, Order = o + 1, Weight = s.Weight, Reps = s.Reps })]))];

    private static (double?, int)[] Sets(double? weight, params int[] reps) => [.. reps.Select(r => (weight, r))];

    [Fact]
    public void Of_IsFlat_WhenTheBestCameFourWorkoutsAgoAndNoneBeatIt()
    {
        var history = History(Sets(100, 10, 10, 8), Sets(105, 10, 10, 10), Sets(105, 10, 9, 8), Sets(105, 10, 10, 9), Sets(100, 10, 10, 10), Sets(105, 10, 10, 10));

        var plateau = Plateaus.Of(Rows, history, null);

        Assert.Equal(4, plateau!.Workouts);
        Assert.Equal(2, plateau.Best.Session.Id);
    }

    [Fact]
    public void Of_IsNotFlat_WhenTheBestIsInTheLastFour_OrThereAreTooFewWorkouts()
    {
        Assert.Null(Plateaus.Of(Rows, History(Sets(100, 10), Sets(100, 10), Sets(105, 10), Sets(100, 10), Sets(100, 10), Sets(100, 10)), null));
        Assert.Null(Plateaus.Of(Rows, History(Sets(105, 10), Sets(100, 10), Sets(100, 10), Sets(100, 10)), null));
    }

    [Fact]
    public void Of_MoreRepsAtTheSameWeightIsANewBest()
    {
        var history = History(Sets(100, 10, 10, 8), Sets(100, 10, 9, 8), Sets(100, 10, 9, 8), Sets(100, 10, 9, 8), Sets(100, 10, 10, 9));

        Assert.Null(Plateaus.Of(Rows, history, null));
    }

    [Fact]
    public void Of_BodyweightComparesByReps_WithoutAWeighIn_AndAssistanceNeedsOne()
    {
        var reps = History(Sets(null, 12), Sets(null, 10), Sets(null, 11), Sets(null, 12), Sets(null, 11));
        var assisted = History(Sets(-40, 8), Sets(-40, 8), Sets(-40, 7), Sets(-40, 8), Sets(-40, 7));

        Assert.Equal(4, Plateaus.Of(PullUps, reps, null)!.Workouts);
        Assert.Null(Plateaus.Of(PullUps, assisted, null));
        Assert.Equal(4, Plateaus.Of(PullUps, assisted, 200)!.Workouts);
    }
}
