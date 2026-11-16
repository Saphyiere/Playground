using AdaptiveWorkout.Core;

namespace AdaptiveWorkout.Core.Tests;

// A workout open when the app is updated was kept without lines: a set only named its exercise. Opening it gives it its lines once.
public class LegacyPlanTests
{
    private const long Rows = 1, PullUps = 2, Dips = 3, Pulldown = 4, Skulls = 5, Curls = 6;

    private static readonly Dictionary<long, int> NoHistory = [];
    private static readonly DateTimeOffset Start = new(2026, 11, 16, 18, 0, 0, TimeSpan.FromHours(-8));

    private static TemplateExercise Entry(long exerciseId, int order, int? sets = null, int? superset = null) =>
        new() { ExerciseId = exerciseId, Order = order, PlannedSets = sets, SupersetGroup = superset };

    // Sets as they were kept: in order, each with only its exercise. The last may be running.
    private static List<WorkoutSet> Sets(bool lastRuns, params long[] exerciseIds) =>
        [.. exerciseIds.Select((id, i) => new WorkoutSet { ExerciseId = id, Order = i + 1, StartedAt = Start.AddMinutes(i), StoppedAt = lastRuns && i == exerciseIds.Length - 1 ? null : Start.AddMinutes(i).AddSeconds(40) })];

    [Fact]
    public void Number_GivesEachSetTheLineItCountedFor()
    {
        TemplateExercise[] plan = [Entry(Rows, 0, sets: 3), Entry(PullUps, 1, sets: 3, superset: 1), Entry(Dips, 2, superset: 1), Entry(Pulldown, 3)];
        var sets = Sets(false, Rows, Rows, Rows, PullUps, Dips);

        var numbered = LegacyPlan.Number(plan, sets, NoHistory);

        Assert.Equal([1, 2, 3, 4], numbered.Select(e => e.Line));
        Assert.Equal([1, 1, 1, 2, 3], sets.Select(s => s.Line));
        Assert.Equal(new PlanStep([PullUps, Dips], [2, 3], 2, 3), WorkoutPlan.Next(numbered, sets, NoHistory));
    }

    [Fact]
    public void Number_KeepsARunningSetRunningOnItsLine()
    {
        TemplateExercise[] plan = [Entry(Rows, 0, sets: 3), Entry(Pulldown, 1, sets: 3)];
        var sets = Sets(true, Rows, Rows, Rows, Pulldown);

        var numbered = LegacyPlan.Number(plan, sets, NoHistory);

        Assert.Equal(2, sets[3].Line);
        Assert.True(WorkoutPlan.Progress([numbered[1]], sets, NoHistory).Running);
    }

    [Fact]
    public void Number_AnExerciseInThePlanTwice_FillsTheFirstLineThenTheSecond()
    {
        TemplateExercise[] plan = [Entry(Rows, 0, sets: 2), Entry(Rows, 1, sets: 2), Entry(Pulldown, 2, sets: 3)];
        var sets = Sets(false, Rows, Rows, Rows);

        var numbered = LegacyPlan.Number(plan, sets, NoHistory);

        Assert.Equal([1, 1, 2], sets.Select(s => s.Line));
        Assert.Equal(new PlanStep([Rows], [2], 2, 2), WorkoutPlan.Next(numbered, sets, NoHistory));
    }

    [Fact]
    public void Number_ASetThePlanNoLongerHas_GetsALineOfItsOwnAheadOfTheRest_DoneWithIt()
    {
        // Curls were swapped out after a set.
        TemplateExercise[] plan = [Entry(Rows, 0, sets: 3), Entry(Pulldown, 1, sets: 3)];
        var sets = Sets(false, Rows, Rows, Rows, Curls);

        var numbered = LegacyPlan.Number(plan, sets, NoHistory);

        Assert.Equal([(3, Curls, 1), (1, Rows, 3), (2, Pulldown, 3)], numbered.Select(e => (e.Line, e.ExerciseId, e.PlannedSets!.Value)));
        Assert.Equal(3, sets[3].Line);
        Assert.Equal(new PlanStep([Pulldown], [2], 1, 3), WorkoutPlan.Next(numbered, sets, NoHistory));
    }

    [Fact]
    public void Number_WhenJustTracking_EachExerciseGetsALine_AndTheOneImOnStaysOpen()
    {
        var sets = Sets(false, Rows, Rows, Dips);

        var numbered = LegacyPlan.Number([], sets, NoHistory);

        Assert.Equal([(1, Rows, 2), (2, Dips, null)], numbered.Select(e => (e.Line, e.ExerciseId, e.PlannedSets)));
        Assert.Equal([1, 1, 2], sets.Select(s => s.Line));
        Assert.Equal(new PlanStep([Dips], [2], 2, null), WorkoutPlan.Next(numbered, sets, NoHistory, openEnded: true));
    }

    [Fact]
    public void Number_AnExerciseIWentPast_WaitsRightAfterTheOneImOn()
    {
        // Skull crushers were picked ahead of the pulldown, which used to come back only when picked.
        TemplateExercise[] plan = [Entry(Rows, 0, sets: 3), Entry(Pulldown, 1, sets: 3), Entry(Skulls, 2, sets: 3), Entry(Dips, 3, sets: 3)];
        var sets = Sets(false, Rows, Rows, Rows, Skulls);

        var numbered = LegacyPlan.Number(plan, sets, NoHistory);

        Assert.Equal([Rows, Skulls, Pulldown, Dips], numbered.Select(e => e.ExerciseId));
        Assert.Equal(new PlanStep([Skulls], [3], 2, 3), WorkoutPlan.Next(numbered, sets, NoHistory));
    }

    [Fact]
    public void Number_AnExerciseWithNoCountIMovedOnFrom_StaysDoneWithTheSetsItHas()
    {
        // Neither has a count, or a last time to go by. A set of curls, then on to skull crushers: curls were over with that.
        TemplateExercise[] plan = [Entry(Curls, 0), Entry(Skulls, 1)];
        var sets = Sets(false, Curls, Skulls);

        var numbered = LegacyPlan.Number(plan, sets, NoHistory);

        Assert.Equal(new (long, int?)[] { (Curls, 1), (Skulls, null) }, numbered.Select(e => (e.ExerciseId, e.PlannedSets)));
        Assert.Equal(new PlanStep([Skulls], [2], 2, null), WorkoutPlan.Next(numbered, sets, NoHistory));
        Assert.Null(WorkoutPlan.After(numbered, sets, NoHistory, 1));
    }

    [Fact]
    public void Number_AnExerciseWithNoCountINeverStarted_StillWaitsAfterTheOneImOn()
    {
        // Skull crushers were picked first: curls haven't had a set, so they're still to come.
        TemplateExercise[] plan = [Entry(Curls, 0), Entry(Skulls, 1)];
        var sets = Sets(false, Skulls);

        var numbered = LegacyPlan.Number(plan, sets, NoHistory);

        Assert.Equal([Skulls, Curls], numbered.Select(e => e.ExerciseId));
        Assert.Equal(new PlanStep([Curls], [1], 1, null), WorkoutPlan.After(numbered, sets, NoHistory, 0));
    }

    [Fact]
    public void Number_ANewWorkoutsPlan_IsJustNumbered()
    {
        TemplateExercise[] plan = [Entry(Rows, 0, sets: 3), Entry(Pulldown, 1)];

        Assert.Equal([(1, Rows), (2, Pulldown)], LegacyPlan.Number(plan, [], NoHistory).Select(e => (e.Line, e.ExerciseId)));
        Assert.Empty(LegacyPlan.Number([], [], NoHistory));
    }
}
