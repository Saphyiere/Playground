using AdaptiveWorkout.Core;

namespace AdaptiveWorkout.Core.Tests;

public class WorkoutPlanTests
{
    private const long Rows = 1, PullUps = 2, Dips = 3, Pulldown = 4, Skulls = 5, Curls = 6;

    private static readonly Dictionary<long, int> NoHistory = [];

    private static TemplateExercise Entry(long exerciseId, int order, int? sets = null, int? superset = null) =>
        new() { ExerciseId = exerciseId, Order = order, PlannedSets = sets, SupersetGroup = superset };

    private static List<WorkoutSet> Done(params long[] exerciseIds) => [.. exerciseIds.Select((id, i) => new WorkoutSet { ExerciseId = id, Order = i + 1 })];

    [Fact]
    public void Next_MovesOnAfterThePlannedSets()
    {
        TemplateExercise[] plan = [Entry(Rows, 0, sets: 3), Entry(Pulldown, 1, sets: 2)];

        Assert.Equal(new PlanStep([Rows], 3, 3), WorkoutPlan.Next(plan, Done(Rows, Rows), NoHistory, []));
        Assert.Equal(new PlanStep([Pulldown], 1, 2), WorkoutPlan.Next(plan, Done(Rows, Rows, Rows), NoHistory, []));
        Assert.Null(WorkoutPlan.Next(plan, Done(Rows, Rows, Rows, Pulldown, Pulldown), NoHistory, []));
    }

    [Fact]
    public void Next_UsesLastTimesSetCountWhenNothingIsPlanned()
    {
        TemplateExercise[] plan = [Entry(Pulldown, 0), Entry(Rows, 1, sets: 3)];
        var lastTime = new Dictionary<long, int> { [Pulldown] = 4 };

        Assert.Equal(new PlanStep([Pulldown], 4, 4), WorkoutPlan.Next(plan, Done(Pulldown, Pulldown, Pulldown), lastTime, []));
        Assert.Equal(Rows, WorkoutPlan.Next(plan, Done(Pulldown, Pulldown, Pulldown, Pulldown), lastTime, [])?.ExerciseId);
    }

    [Fact]
    public void Next_WithoutAnyCount_OffersAnotherSetUntilIMoveOn()
    {
        TemplateExercise[] plan = [Entry(Pulldown, 0), Entry(Rows, 1, sets: 3)];

        Assert.Equal(new PlanStep([Pulldown], 6, null), WorkoutPlan.Next(plan, Done(Pulldown, Pulldown, Pulldown, Pulldown, Pulldown), NoHistory, []));
        Assert.Equal(new PlanStep([Rows], 2, 3), WorkoutPlan.Next(plan, Done(Pulldown, Pulldown, Rows), NoHistory, []));
    }

    [Fact]
    public void Next_KeepsGoingWithTheExerciseImOn_AndNeverGoesBackToOnesIWentPast()
    {
        TemplateExercise[] counted = [Entry(Rows, 0, sets: 3), Entry(Pulldown, 1, sets: 3), Entry(Skulls, 2, sets: 3), Entry(Dips, 3, sets: 3)];
        TemplateExercise[] uncounted = [Entry(Pulldown, 0), Entry(Rows, 1, sets: 3)];

        Assert.Equal(new PlanStep([Skulls], 2, 3), WorkoutPlan.Next(counted, Done(Rows, Rows, Rows, Skulls), NoHistory, []));
        Assert.Equal(new PlanStep([Dips], 1, 3), WorkoutPlan.Next(counted, Done(Rows, Rows, Rows, Skulls, Skulls, Skulls), NoHistory, []));
        Assert.Null(WorkoutPlan.Next(counted, Done(Rows, Rows, Rows, Skulls, Skulls, Skulls, Dips, Dips, Dips), NoHistory, []));
        Assert.Equal(new PlanStep([Rows], 2, 3), WorkoutPlan.Next(uncounted, Done(Rows), NoHistory, []));
        Assert.Null(WorkoutPlan.Next(uncounted, Done(Rows, Rows, Rows), NoHistory, []));
    }

    [Fact]
    public void Next_ASwappedInExerciseAlreadyDoneTodayCountsAfresh()
    {
        TemplateExercise[] plan = [Entry(Skulls, 0, sets: 3, superset: 1), Entry(Rows, 1, superset: 1), Entry(PullUps, 2, sets: 3, superset: 2), Entry(Dips, 3, superset: 2), Entry(Pulldown, 4, sets: 3)];
        var done = Done(Skulls, Rows, Skulls, Rows, Skulls, Rows, PullUps, Dips, PullUps, Dips, PullUps, Dips);

        var swapped = PlanEdits.Swap(plan, Pulldown, Skulls);

        Assert.Equal(new PlanStep([Skulls], 1, 3), WorkoutPlan.Next(swapped, done, NoHistory, []));
        Assert.Equal(new PlanStep([Skulls], 2, 3), WorkoutPlan.Next(swapped, [.. done, .. Done(Skulls)], NoHistory, []));
        Assert.Null(WorkoutPlan.Next(swapped, [.. done, .. Done(Skulls, Skulls, Skulls)], NoHistory, []));
    }

    [Fact]
    public void Next_SetsOfAnExerciseNoLongerPlannedDontCarryOn()
    {
        TemplateExercise[] plan = [Entry(Rows, 0, sets: 3), Entry(Pulldown, 1, sets: 3)];

        Assert.Equal(new PlanStep([Pulldown], 1, 3), WorkoutPlan.Next(plan, Done(Rows, Rows, Rows, Curls), NoHistory, []));
    }

    [Fact]
    public void Next_ASupersetRoundIsOneStep()
    {
        TemplateExercise[] plan = [Entry(Rows, 0, sets: 3), Entry(PullUps, 1, sets: 2, superset: 1), Entry(Dips, 2, superset: 1), Entry(Pulldown, 3, sets: 3)];

        Assert.Equal(new PlanStep([PullUps, Dips], 1, 2), WorkoutPlan.Next(plan, Done(Rows, Rows, Rows), NoHistory, []));
        Assert.Equal(new PlanStep([PullUps, Dips], 2, 2), WorkoutPlan.Next(plan, Done(Rows, Rows, Rows, PullUps, Dips), NoHistory, []));
        Assert.Equal(new PlanStep([Pulldown], 1, 3), WorkoutPlan.Next(plan, Done(Rows, Rows, Rows, PullUps, Dips, PullUps, Dips), NoHistory, []));
    }

    [Fact]
    public void Next_OneOfASupersetSwappedPartWay_JoinsTheNextRound()
    {
        TemplateExercise[] plan = [Entry(PullUps, 0, sets: 3, superset: 1), Entry(Dips, 1, superset: 1), Entry(Rows, 2, sets: 3)];

        var swapped = PlanEdits.Swap(plan, Dips, Skulls);

        Assert.Equal(new PlanStep([PullUps, Skulls], 2, 3), WorkoutPlan.Next(swapped, Done(PullUps, Dips), NoHistory, []));
        Assert.Equal(new PlanStep([Rows], 1, 3), WorkoutPlan.Next(swapped, Done(PullUps, Dips, PullUps, Skulls, PullUps, Skulls), NoHistory, []));
    }

    [Fact]
    public void Split_PartWay_EachExerciseGoesOnAloneForTheRoundsLeft_ThePickedOneFirst()
    {
        TemplateExercise[] plan = [Entry(Rows, 0, sets: 3), Entry(PullUps, 1, sets: 3, superset: 1), Entry(Dips, 2, superset: 1), Entry(Pulldown, 3, sets: 3)];
        var done = Done(Rows, Rows, Rows, PullUps, Dips);

        var split = PlanEdits.Split(plan, Dips, block: 1, roundsDone: 1, roundsLeft: 2);

        Assert.Equal([(Rows, 3, null), (PullUps, 1, 1), (Dips, null, 1), (Dips, 2, null), (PullUps, 2, null), (Pulldown, 3, null)],
            split.Select(e => (e.ExerciseId, e.PlannedSets, e.SupersetGroup is null ? (int?)null : 1)));
        Assert.Equal(new PlanStep([Dips], 1, 2), WorkoutPlan.Next(split, done, NoHistory, []));
        Assert.Equal(new PlanStep([Dips], 2, 2), WorkoutPlan.Next(split, [.. done, .. Done(Dips)], NoHistory, []));
        Assert.Equal(new PlanStep([PullUps], 1, 2), WorkoutPlan.Next(split, [.. done, .. Done(Dips, Dips)], NoHistory, []));
        Assert.Equal(new PlanStep([Pulldown], 1, 3), WorkoutPlan.Next(split, [.. done, .. Done(Dips, Dips, PullUps, PullUps)], NoHistory, []));
    }

    [Fact]
    public void Split_BeforeAnyRound_TakesTheSupersetApartWhereItIs()
    {
        TemplateExercise[] plan = [Entry(PullUps, 0, sets: 3, superset: 1), Entry(Dips, 1, superset: 1), Entry(Rows, 2, sets: 3)];

        var split = PlanEdits.Split(plan, PullUps, block: 0, roundsDone: 0, roundsLeft: 3);

        Assert.Equal([(PullUps, 3), (Dips, 3), (Rows, 3)], split.Select(e => (e.ExerciseId, e.PlannedSets!.Value)));
        Assert.All(split, e => Assert.Null(e.SupersetGroup));
        Assert.Equal(new PlanStep([PullUps], 1, 3), WorkoutPlan.Next(split, [], NoHistory, []));
        Assert.Equal(new PlanStep([Dips], 1, 3), WorkoutPlan.Next(split, Done(PullUps, PullUps, PullUps), NoHistory, []));
    }

    [Fact]
    public void Split_LeavesThePlanAlone_WhenItsNotASuperset_OrNoRoundsAreLeft()
    {
        TemplateExercise[] plan = [Entry(PullUps, 0, sets: 3, superset: 1), Entry(Dips, 1, superset: 1), Entry(Rows, 2, sets: 3)];

        Assert.Equal([1, 1, null], PlanEdits.Split(plan, Rows, block: 2, roundsDone: 0, roundsLeft: 3).Select(e => e.SupersetGroup));
        Assert.Equal([1, 1, null], PlanEdits.Split(plan, Rows, block: 0, roundsDone: 0, roundsLeft: 3).Select(e => e.SupersetGroup));
        Assert.Equal([1, 1, null], PlanEdits.Split(plan, Dips, block: 0, roundsDone: 3, roundsLeft: 0).Select(e => e.SupersetGroup));
    }

    [Fact]
    public void Split_WithOneSkippedAfterwards_StillCountsTheOthersSets()
    {
        TemplateExercise[] plan = [Entry(PullUps, 0, sets: 3, superset: 1), Entry(Dips, 1, superset: 1)];
        var split = PlanEdits.Split(plan, PullUps, block: 0, roundsDone: 1, roundsLeft: 2);

        Assert.Equal(new PlanStep([Dips], 1, 2), WorkoutPlan.Next(split, Done(PullUps, Dips), NoHistory, [PullUps]));
        Assert.Equal(new PlanStep([Dips], 2, 2), WorkoutPlan.Next(split, Done(PullUps, Dips, Dips), NoHistory, [PullUps]));
        Assert.Null(WorkoutPlan.Next(split, Done(PullUps, Dips, Dips, Dips), NoHistory, [PullUps]));
    }

    [Fact]
    public void Next_AnExerciseRightAfterItself_CountsForTheNextOneOnceTheFirstHasItsSets()
    {
        TemplateExercise[] plan = [Entry(Rows, 0, sets: 2), Entry(Rows, 1, sets: 2), Entry(Pulldown, 2, sets: 3)];

        Assert.Equal(new PlanStep([Rows], 1, 2), WorkoutPlan.Next(plan, Done(Rows, Rows), NoHistory, []));
        Assert.Equal(new PlanStep([Rows], 2, 2), WorkoutPlan.Next(plan, Done(Rows, Rows, Rows), NoHistory, []));
        Assert.Equal(new PlanStep([Pulldown], 1, 3), WorkoutPlan.Next(plan, Done(Rows, Rows, Rows, Rows), NoHistory, []));
    }

    [Fact]
    public void Next_AnotherSetOfOnePutOff_StaysExtra_AndTheOneDoneFirstIsStillNext()
    {
        // Rows had a set, then Pulldown goes first: one more set of rows anyway is extra, and Pulldown is still next.
        TemplateExercise[] plan = [Entry(Rows, 0, sets: 3), Entry(Pulldown, 1, sets: 3)];
        var first = PlanEdits.DoFirst(plan, Pulldown, block: 0, setsDone: 1, setsLeft: 2);

        Assert.Equal(new PlanStep([Pulldown], 1, 3), WorkoutPlan.Next(first, Done(Rows), NoHistory, []));
        Assert.Equal(new PlanStep([Pulldown], 1, 3), WorkoutPlan.Next(first, Done(Rows, Rows), NoHistory, []));
    }

    [Fact]
    public void Next_AWarmUpSetIsntOneOfTheExercisesSets_ButSaysWhereIAm()
    {
        TemplateExercise[] counted = [Entry(Rows, 0, sets: 3), Entry(Curls, 1, sets: 3)];
        TemplateExercise[] uncounted = [Entry(Pulldown, 0), Entry(Curls, 1, sets: 3)];
        WorkoutSet WarmUp(long exerciseId) => new() { ExerciseId = exerciseId, WarmUp = true };

        Assert.Equal(new PlanStep([Curls], 1, 3), WorkoutPlan.Next(counted, [.. Done(Rows, Rows, Rows), WarmUp(Curls)], NoHistory, []));
        Assert.Equal(new PlanStep([Curls], 2, 3), WorkoutPlan.Next(counted, [.. Done(Rows, Rows, Rows), WarmUp(Curls), .. Done(Curls)], NoHistory, []));
        Assert.Equal(new PlanStep([Rows], 1, 3), WorkoutPlan.Next(counted, [WarmUp(Rows)], NoHistory, []));
        // With no count, a warm-up of the next exercise is moving on.
        Assert.Equal(new PlanStep([Curls], 1, 3), WorkoutPlan.Next(uncounted, [.. Done(Pulldown, Pulldown), WarmUp(Curls)], NoHistory, []));
        Assert.Equal(new PlanStep([Dips], 2, null), WorkoutPlan.Next([], [WarmUp(Dips), .. Done(Dips)], NoHistory, []));
    }

    [Fact]
    public void Next_PassesOverSkippedExercises()
    {
        TemplateExercise[] plan = [Entry(Rows, 0, sets: 3), Entry(Pulldown, 1, sets: 3), Entry(Dips, 2, sets: 3)];
        TemplateExercise[] superset = [Entry(PullUps, 0, sets: 3, superset: 1), Entry(Dips, 1, superset: 1)];

        Assert.Equal(Dips, WorkoutPlan.Next(plan, Done(Rows, Rows, Rows), NoHistory, [Pulldown])?.ExerciseId);
        Assert.Null(WorkoutPlan.Next(plan, Done(Rows, Rows, Rows), NoHistory, [Pulldown, Dips]));
        Assert.Equal(new PlanStep([PullUps], 1, 3), WorkoutPlan.Next(superset, [], NoHistory, [Dips]));
    }

    [Fact]
    public void Next_WhenJustTracking_OffersAnotherSetOfTheLastExercise()
    {
        var lastTime = new Dictionary<long, int> { [Dips] = 3 };

        Assert.Null(WorkoutPlan.Next([], [], lastTime, []));
        Assert.Equal(new PlanStep([Dips], 2, 3), WorkoutPlan.Next([], Done(Rows, Dips), lastTime, []));
        Assert.Equal(new PlanStep([Dips], 4, null), WorkoutPlan.Next([], Done(Dips, Dips, Dips), lastTime, []));
    }
}
