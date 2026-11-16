using AdaptiveWorkout.Core;

namespace AdaptiveWorkout.Core.Tests;

public class WorkoutPlanTests
{
    private const long Rows = 1, PullUps = 2, Dips = 3, Pulldown = 4, Skulls = 5, Curls = 6;

    private static readonly Dictionary<long, int> NoHistory = [];
    private static readonly DateTimeOffset Start = new(2026, 11, 16, 18, 0, 0, TimeSpan.FromHours(-8));

    // A plan's entry on line `line`; entries with one `superset` number are done together.
    private static TemplateExercise Entry(int line, long exerciseId, int? sets = null, int? superset = null) =>
        new() { Line = line, ExerciseId = exerciseId, Order = line, PlannedSets = sets, SupersetGroup = superset };

    // Finished sets on these lines, in order, a minute apart; a superset's round is two lines with the same `round` start.
    private static List<WorkoutSet> Done(IReadOnlyList<TemplateExercise> plan, params int[] lines) =>
        [.. lines.Select((line, i) => new WorkoutSet { Line = line, ExerciseId = plan.First(e => e.Line == line).ExerciseId, Order = i + 1, StartedAt = Start.AddMinutes(i), StoppedAt = Start.AddMinutes(i).AddSeconds(40) })];

    private static PlanStep Step(long exerciseId, int line, int set, int? of) => new([exerciseId], [line], set, of);

    [Fact]
    public void Next_MovesOnAfterThePlannedSets()
    {
        TemplateExercise[] plan = [Entry(1, Rows, sets: 3), Entry(2, Pulldown, sets: 2)];

        Assert.Equal(Step(Rows, 1, 3, 3), WorkoutPlan.Next(plan, Done(plan, 1, 1), NoHistory));
        Assert.Equal(Step(Pulldown, 2, 1, 2), WorkoutPlan.Next(plan, Done(plan, 1, 1, 1), NoHistory));
        Assert.Null(WorkoutPlan.Next(plan, Done(plan, 1, 1, 1, 2, 2), NoHistory));
    }

    [Fact]
    public void Next_UsesLastTimesSetCountWhenNothingIsPlanned()
    {
        TemplateExercise[] plan = [Entry(1, Pulldown), Entry(2, Rows, sets: 3)];
        var lastTime = new Dictionary<long, int> { [Pulldown] = 4 };

        Assert.Equal(Step(Pulldown, 1, 4, 4), WorkoutPlan.Next(plan, Done(plan, 1, 1, 1), lastTime));
        Assert.Equal(Step(Rows, 2, 1, 3), WorkoutPlan.Next(plan, Done(plan, 1, 1, 1, 1), lastTime));
    }

    [Fact]
    public void Next_WithoutAnyCount_OffersAnotherSetUntilItsCountIsSet()
    {
        TemplateExercise[] plan = [Entry(1, Pulldown), Entry(2, Rows, sets: 3)];
        var done = Done(plan, 1, 1, 1, 1, 1);

        Assert.Equal(Step(Pulldown, 1, 6, null), WorkoutPlan.Next(plan, done, NoHistory));
        Assert.Equal(Step(Rows, 2, 1, 3), WorkoutPlan.After(plan, done, NoHistory, block: 0));
        // Moving on gives it the sets it has as its count: then it's done, and rows are next.
        Assert.Equal(Step(Rows, 2, 1, 3), WorkoutPlan.Next(PlanEdits.Count(plan, 1, 5), done, NoHistory));
    }

    [Fact]
    public void Next_IsTheFirstLineInOrderWithSetsLeft_WhateverWasDoneAfterIt()
    {
        // Rows had a set, then all of pulldown was done: rows, still first with sets left, are next again.
        TemplateExercise[] plan = [Entry(1, Rows, sets: 3), Entry(2, Pulldown, sets: 3), Entry(3, Skulls, sets: 3)];

        Assert.Equal(Step(Rows, 1, 2, 3), WorkoutPlan.Next(plan, Done(plan, 1, 2, 2, 2), NoHistory));
        Assert.Equal(Step(Skulls, 3, 1, 3), WorkoutPlan.Next(plan, Done(plan, 1, 2, 2, 2, 1, 1), NoHistory));
    }

    [Fact]
    public void Next_TheSameExerciseOnTwoLines_EachLineCountsItsOwnSets()
    {
        TemplateExercise[] plan = [Entry(1, Rows, sets: 2), Entry(2, Rows, sets: 2), Entry(3, Pulldown, sets: 3)];

        Assert.Equal(Step(Rows, 1, 2, 2), WorkoutPlan.Next(plan, Done(plan, 1), NoHistory));
        Assert.Equal(Step(Rows, 2, 1, 2), WorkoutPlan.Next(plan, Done(plan, 1, 1), NoHistory));
        Assert.Equal(Step(Rows, 2, 2, 2), WorkoutPlan.Next(plan, Done(plan, 1, 1, 2), NoHistory));
        Assert.Equal(Step(Pulldown, 3, 1, 3), WorkoutPlan.Next(plan, Done(plan, 1, 1, 2, 2), NoHistory));
    }

    [Fact]
    public void Next_ASetOnALineThePlanNoLongerHas_CountsForNothingInIt()
    {
        TemplateExercise[] plan = [Entry(1, Rows, sets: 3), Entry(2, Pulldown, sets: 3)];
        var done = Done(plan, 1, 1, 1);
        done.Add(new WorkoutSet { Line = 9, ExerciseId = Curls, Order = 4, StartedAt = Start.AddMinutes(9), StoppedAt = Start.AddMinutes(10) });

        Assert.Equal(Step(Pulldown, 2, 1, 3), WorkoutPlan.Next(plan, done, NoHistory));
    }

    [Fact]
    public void Next_ASupersetRoundIsOneStep()
    {
        TemplateExercise[] plan = [Entry(1, Rows, sets: 3), Entry(2, PullUps, sets: 2, superset: 1), Entry(3, Dips, superset: 1), Entry(4, Pulldown, sets: 3)];

        Assert.Equal(new PlanStep([PullUps, Dips], [2, 3], 1, 2), WorkoutPlan.Next(plan, Done(plan, 1, 1, 1), NoHistory));
        Assert.Equal(new PlanStep([PullUps, Dips], [2, 3], 2, 2), WorkoutPlan.Next(plan, Done(plan, 1, 1, 1, 2, 3), NoHistory));
        Assert.Equal(Step(Pulldown, 4, 1, 3), WorkoutPlan.Next(plan, Done(plan, 1, 1, 1, 2, 3, 2, 3), NoHistory));
    }

    [Fact]
    public void Next_OneOfASupersetSwappedPartWay_JoinsTheNextRound()
    {
        TemplateExercise[] plan = [Entry(1, PullUps, sets: 3, superset: 1), Entry(2, Dips, superset: 1), Entry(3, Rows, sets: 3)];
        var done = Done(plan, 1, 2);

        // Dips' round stays dips, on a line of its own; skull crushers take the rounds left.
        var swapped = PlanEdits.SwapRest(plan, 2, Skulls, doneLine: 4, done: 1, left: null);
        done[1].Line = 4;

        Assert.Equal(new PlanStep([PullUps, Skulls], [1, 2], 2, 3), WorkoutPlan.Next(swapped, done, NoHistory));
    }

    [Fact]
    public void Next_AWarmUpSetIsntOneOfTheExercisesSets()
    {
        TemplateExercise[] plan = [Entry(1, Rows, sets: 3), Entry(2, Curls, sets: 3)];
        var done = Done(plan, 1, 1, 1, 2);
        done[3].WarmUp = true;

        Assert.Equal(Step(Curls, 2, 1, 3), WorkoutPlan.Next(plan, done, NoHistory));
        Assert.True(WorkoutPlan.Progress([plan[1]], done, NoHistory).Started);
        Assert.Equal(Step(Curls, 2, 2, 3), WorkoutPlan.Next(plan, [.. done, .. Done(plan, 2).Select(s => { s.Order = 5; return s; })], NoHistory));
    }

    [Fact]
    public void Next_WhenJustTracking_AnExerciseGoesOnUntilIMoveOn()
    {
        TemplateExercise[] plan = [Entry(1, Dips)];
        var lastTime = new Dictionary<long, int> { [Dips] = 3 };

        Assert.Null(WorkoutPlan.Next([], [], lastTime, openEnded: true));
        // Last time's count shows until it's passed, and passing it doesn't end the exercise.
        Assert.Equal(Step(Dips, 1, 2, 3), WorkoutPlan.Next(plan, Done(plan, 1), lastTime, openEnded: true));
        Assert.Equal(Step(Dips, 1, 4, null), WorkoutPlan.Next(plan, Done(plan, 1, 1, 1), lastTime, openEnded: true));
        // With a plan, as many as last time is the count.
        Assert.Null(WorkoutPlan.Next(plan, Done(plan, 1, 1, 1), lastTime));
    }

    [Fact]
    public void Progress_SaysWhatsDoneRunningAndStarted()
    {
        TemplateExercise[] plan = [Entry(1, PullUps, sets: 3, superset: 1), Entry(2, Dips, superset: 1), Entry(3, Rows, sets: 3)];
        var sets = Done(plan, 1, 2, 1);
        sets.Add(new WorkoutSet { Line = 2, ExerciseId = Dips, Order = 4, StartedAt = Start.AddMinutes(5) });
        var blocks = WorkoutPlan.Blocks(plan);

        Assert.Equal(new BlockProgress(2, 3, true, true), WorkoutPlan.Progress(blocks[0], sets, NoHistory));
        Assert.False(WorkoutPlan.Progress(blocks[0], sets, NoHistory).Complete);
        Assert.Equal(new BlockProgress(0, 3, false, false), WorkoutPlan.Progress(blocks[1], sets, NoHistory));
        Assert.True(WorkoutPlan.Progress(blocks[0], Done(plan, 1, 2, 1, 2, 1, 2), NoHistory).Complete);
    }

    [Fact]
    public void StepOf_SaysWhereASetSits_CountingOnlyTheSetsBeforeItsRound()
    {
        TemplateExercise[] plan = [Entry(1, PullUps, sets: 3, superset: 1), Entry(2, Dips, superset: 1)];
        var sets = Done(plan, 1, 2, 1, 2);
        // Each round's sets start together.
        (sets[1].StartedAt, sets[3].StartedAt) = (sets[0].StartedAt, sets[2].StartedAt);

        Assert.Equal(new PlanStep([PullUps, Dips], [1, 2], 1, 3), WorkoutPlan.StepOf(plan, sets, sets[1], NoHistory));
        Assert.Equal(new PlanStep([PullUps, Dips], [1, 2], 2, 3), WorkoutPlan.StepOf(plan, sets, sets[2], NoHistory));
        Assert.Equal(new PlanStep([PullUps, Dips], [1, 2], 2, 3), WorkoutPlan.StepOf(plan, sets, sets[3], NoHistory));
    }
}
