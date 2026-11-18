using AdaptiveWorkout.Core;

namespace AdaptiveWorkout.Core.Tests;

public class PlanEditsTests
{
    private const long Bench = 1, Incline = 2, Fly = 3, PullUps = 4, Dips = 5, Pulldown = 6, Skulls = 7, DumbbellPress = 8;

    private static readonly Dictionary<long, int> NoHistory = [];
    private static readonly DateTimeOffset Start = new(2026, 11, 16, 18, 0, 0, TimeSpan.FromHours(-8));

    private static TemplateExercise Entry(long exerciseId, int order, int? sets = null, int? superset = null) =>
        new() { ExerciseId = exerciseId, Order = order, PlannedSets = sets, SupersetGroup = superset };

    // The plan as (line, exercise, sets, in a superset) in order.
    private static IEnumerable<(int, long, int?, bool)> Lines(IEnumerable<TemplateExercise> plan) => plan.Select(e => (e.Line, e.ExerciseId, e.PlannedSets, e.SupersetGroup is not null));

    private static WorkoutSet Set(int line, long exerciseId, int order) =>
        new() { Line = line, ExerciseId = exerciseId, Order = order, StartedAt = Start.AddMinutes(order), StoppedAt = Start.AddMinutes(order).AddSeconds(40) };

    // Chest day as a workout: bench, incline, fly, then pull-ups with dips.
    private static List<TemplateExercise> ChestDay() =>
        PlanEdits.Copy([Entry(Bench, 0, sets: 3), Entry(Incline, 1, sets: 3), Entry(Fly, 2, sets: 3), Entry(PullUps, 3, sets: 3, superset: 1), Entry(Dips, 4, superset: 1)]);

    [Fact]
    public void Copy_NumbersTheLinesFromOne_AndAnEditLeavesThePlanItWasHandedAlone()
    {
        var plan = ChestDay();

        var changed = PlanEdits.Swap(plan, 2, DumbbellPress);

        Assert.Equal([1, 2, 3, 4, 5], plan.Select(e => e.Line));
        Assert.Equal(Incline, plan[1].ExerciseId);
        Assert.Equal(DumbbellPress, changed[1].ExerciseId);
        Assert.Equal(2, changed[1].Line);
    }

    [Fact]
    public void MoveBefore_BenchTaken_FlyNextAndInclineRightAfter()
    {
        var first = PlanEdits.MoveBefore(ChestDay(), line: 3, beforeLine: 2);

        Assert.Equal([Bench, Fly, Incline, PullUps, Dips], first.Select(e => e.ExerciseId));
        Assert.Equal([0, 1, 2, 3, 4], first.Select(e => e.Order));
    }

    [Fact]
    public void MoveBefore_MovesAWholeSuperset_AndWithNoLineAfterEverything()
    {
        var plan = ChestDay();

        Assert.Equal([Bench, PullUps, Dips, Incline, Fly], PlanEdits.MoveBefore(plan, line: 5, beforeLine: 2).Select(e => e.ExerciseId));
        Assert.Equal([Incline, Fly, PullUps, Dips, Bench], PlanEdits.MoveBefore(plan, line: 1, beforeLine: 0).Select(e => e.ExerciseId));
        // Before itself, or a line that isn't there: as it was.
        Assert.Equal(Lines(plan), Lines(PlanEdits.MoveBefore(plan, line: 4, beforeLine: 5)));
        Assert.Equal(Lines(plan), Lines(PlanEdits.MoveBefore(plan, line: 9, beforeLine: 1)));
    }

    [Fact]
    public void Exchange_TwoBlocksChangePlaces()
    {
        Assert.Equal([Bench, PullUps, Dips, Fly, Incline], PlanEdits.Exchange(ChestDay(), 2, 4).Select(e => e.ExerciseId));
    }

    [Fact]
    public void DoNext_LeavingPullUpsOnSetThree_KeepsTwoSetsAndTheRestOfTheQueue()
    {
        var plan = PlanEdits.Copy([Entry(PullUps, 0, 3), Entry(Dips, 1, 3), Entry(Skulls, 2, 4), Entry(PullUps, 3, 2)]);
        var held = Set(1, PullUps, 3);
        held.StoppedAt = null;
        List<WorkoutSet> sets = [Set(1, PullUps, 1), Set(1, PullUps, 2), held];

        var changed = PlanEdits.DoNext(plan, 3, [plan[0]], sets, NoHistory);

        Assert.Equal([1, 3, 2, 4], changed.Select(e => e.Line));
        Assert.Equal(2, changed[0].PlannedSets);
        Assert.True(changed[0].FinishedByUser);
        Assert.False(changed.Single(e => e.Line == 4).FinishedByUser);
        Assert.Equal([1, 2], sets.Select(s => s.Order));
        Assert.Equal(new PlanStep([Skulls], [3], 1, 4), WorkoutPlan.Next(changed, sets, NoHistory));
        for (var i = 0; i < 4; i++) sets.Add(Set(3, Skulls, 3 + i));
        Assert.Equal(new PlanStep([Dips], [2], 1, 3), WorkoutPlan.Next(changed, sets, NoHistory));
        for (var i = 0; i < 3; i++) sets.Add(Set(2, Dips, 7 + i));
        Assert.Equal(new PlanStep([PullUps], [4], 1, 2), WorkoutPlan.Next(changed, sets, NoHistory));
    }

    [Fact]
    public void DoNext_BetweenSetsFinishesOnlyTheExerciseBeingContinued()
    {
        var plan = ChestDay();
        var blocks = WorkoutPlan.Blocks(plan);
        List<WorkoutSet> sets = [Set(1, Bench, 1), Set(1, Bench, 2)];
        var active = WorkoutPlan.ActiveBlock(blocks, sets, WorkoutPlan.NextBlock(plan, sets, NoHistory));
        Assert.Equal(1, Assert.Single(active!).Line);

        var changed = PlanEdits.DoNext(plan, 2, active, sets, NoHistory);

        Assert.Equal(2, changed[0].PlannedSets);
        Assert.Null(WorkoutPlan.ActiveBlock(WorkoutPlan.Blocks(changed), sets, WorkoutPlan.NextBlock(changed, sets, NoHistory)));
        var reordered = PlanEdits.DoNext(changed, 3, null, sets, NoHistory);
        Assert.Equal([1, 3, 2, 4, 5], reordered.Select(e => e.Line));
        Assert.Equal(3, reordered[2].PlannedSets);
    }

    [Fact]
    public void AdjustCount_FewerUsesHistoryImmediatelyAndFinishesAHeldRoundAtTwo()
    {
        var plan = PlanEdits.Copy([Entry(PullUps, 0, superset: 1), Entry(Dips, 1, superset: 1), Entry(Skulls, 2, 3)]);
        var blocks = WorkoutPlan.Blocks(plan);
        Dictionary<long, int> history = new() { [PullUps] = 3, [Dips] = 2 };
        List<WorkoutSet> sets = [Set(1, PullUps, 1), Set(2, Dips, 2), Set(1, PullUps, 3), Set(2, Dips, 4)];
        var warmUp = Set(1, PullUps, 5);
        warmUp.WarmUp = true;
        sets.Add(warmUp);
        foreach (var entry in blocks[0])
        {
            var held = Set(entry.Line, entry.ExerciseId, sets.Count + 1);
            held.StoppedAt = null;
            sets.Add(held);
        }

        var changed = PlanEdits.AdjustCount(plan, blocks[0], sets, history, more: false);

        Assert.Equal(2, changed[0].PlannedSets);
        Assert.Equal([1, 2, 3, 4, 5], sets.Select(s => s.Order));
        Assert.True(WorkoutPlan.Progress(WorkoutPlan.Blocks(changed)[0], sets, history).Complete);
        Assert.Equal(new PlanStep([Skulls], [3], 1, 3), WorkoutPlan.Next(changed, sets, history));
        var again = PlanEdits.AdjustCount(changed, WorkoutPlan.Blocks(changed)[0], sets, history, more: false);
        Assert.Equal(2, again[0].PlannedSets);
        Assert.All(WorkoutPlan.Blocks(again)[0], e => Assert.True(e.FinishedByUser));
        var reopened = PlanEdits.AdjustCount(again, WorkoutPlan.Blocks(again)[0], sets, history, more: true);
        Assert.All(WorkoutPlan.Blocks(reopened)[0], e => Assert.False(e.FinishedByUser));
        Assert.All(PlanEdits.AsSession(again), e => Assert.False(e.FinishedByUser));
    }

    [Fact]
    public void FinishBlock_CorrectingALoggedSetToWarmUpDoesNotPutTheExerciseBackInTheQueue()
    {
        var plan = ChestDay();
        List<WorkoutSet> sets = [Set(1, Bench, 1), Set(1, Bench, 2)];
        var finished = PlanEdits.FinishBlock(plan, [plan[0]], sets, done: 2);

        sets[0].WarmUp = true;
        Assert.Equal(new BlockProgress(1, 1, false, true), WorkoutPlan.Progress([finished[0]], sets, NoHistory));
        Assert.Equal(new PlanStep([Incline], [2], 1, 3), WorkoutPlan.Next(finished, sets, NoHistory));
        var reopened = PlanEdits.AdjustCount(finished, [finished[0]], sets, NoHistory, more: true);
        Assert.Equal(new PlanStep([Bench], [1], 2, 2), WorkoutPlan.Next(reopened, sets, NoHistory));
    }

    [Fact]
    public void FinishBlock_LeavingTheFirstSetKeepsZeroLoggedSetsAndTheNextSlot()
    {
        var plan = ChestDay();
        var held = Set(1, Bench, 1);
        held.StoppedAt = null;
        List<WorkoutSet> sets = [held];

        var changed = PlanEdits.FinishBlock(plan, [plan[0]], sets, done: 0);

        Assert.Empty(sets);
        Assert.Equal(0, changed[0].PlannedSets);
        Assert.Equal(new PlanStep([Incline], [2], 1, 3), WorkoutPlan.Next(changed, sets, NoHistory));
    }

    [Fact]
    public void AdjustCount_OpenEndedExerciseCanEndAtTwoWithoutFirstAddingATarget()
    {
        var plan = PlanEdits.Copy([Entry(PullUps, 0), Entry(Dips, 1)]);
        var block = WorkoutPlan.Blocks(plan)[0];
        List<WorkoutSet> sets = [];
        Assert.Null(WorkoutPlan.Progress(block, sets, NoHistory).EditableCount);
        sets.AddRange([Set(1, PullUps, 1), Set(1, PullUps, 2)]);

        Assert.Null(WorkoutPlan.Progress(block, sets, NoHistory, openEnded: true).Target);
        Assert.Equal(3, WorkoutPlan.Progress(block, sets, NoHistory).EditableCount);
        var changed = PlanEdits.AdjustCount(plan, block, sets, NoHistory, more: false);

        Assert.Equal(2, changed[0].PlannedSets);
        Assert.Equal(new PlanStep([Dips], [2], 1, null), WorkoutPlan.Next(changed, sets, NoHistory, openEnded: true));
    }

    [Fact]
    public void ExchangeExercises_UpcomingSlotsTradeLinesWithoutChangingCurrentWorkOrSupersetPartners()
    {
        var plan = PlanEdits.Copy([Entry(PullUps, 0, 3), Entry(Dips, 1, 2), Entry(Fly, 2, 3), Entry(Skulls, 3, 4)]);
        var held = Set(1, PullUps, 3);
        held.StoppedAt = null;
        List<WorkoutSet> sets = [Set(1, PullUps, 1), Set(1, PullUps, 2), held];

        var exchanged = PlanEdits.ExchangeExercises(plan, 2, 4);

        Assert.Equal([(1, PullUps, 3, false), (4, Skulls, 4, false), (3, Fly, 3, false), (2, Dips, 2, false)], Lines(exchanged));
        Assert.True(WorkoutPlan.Progress([exchanged[0]], sets, NoHistory).Running);
        Assert.Equal(new PlanStep([Skulls], [4], 1, 4), WorkoutPlan.After(exchanged, sets, NoHistory, 0));

        var paired = PlanEdits.Join(plan, 2, 3);
        var memberChanged = PlanEdits.ExchangeExercises(paired, 2, 4);
        Assert.Equal([(1, PullUps, 3, false), (4, Skulls, 2, true), (3, Fly, null, true), (2, Dips, 4, false)], Lines(memberChanged));
    }

    [Fact]
    public void Count_SetsALinesSets_AndASupersetsRoundsFromAnyOfItsLines()
    {
        var plan = ChestDay();

        Assert.Equal(5, PlanEdits.Count(plan, 1, 5)[0].PlannedSets);
        Assert.Null(PlanEdits.Count(plan, 1, null)[0].PlannedSets);
        Assert.Equal([(4, PullUps, 2, true), (5, Dips, null, true)], Lines(PlanEdits.Count(plan, 5, 2)).Skip(3));
    }

    [Fact]
    public void Swap_KeepsTheSpotTheSetsAndTheSuperset()
    {
        Assert.Equal([(4, PullUps, 3, true), (5, Skulls, null, true)], Lines(PlanEdits.Swap(ChestDay(), 5, Skulls)).Skip(3));
    }

    // The owner's day: incline was swapped for the dumbbell press, which was done already. Each is a line of its own, and
    // removing the second leaves the first, with its sets, alone.
    [Fact]
    public void Swap_ForAnExerciseAlreadyThere_MakesASecondLineOfIt_AndRemovingThatOneLeavesTheFirst()
    {
        var plan = PlanEdits.Swap(ChestDay(), 1, DumbbellPress);
        List<WorkoutSet> sets = [Set(1, DumbbellPress, 1), Set(1, DumbbellPress, 2), Set(1, DumbbellPress, 3)];

        var doubled = PlanEdits.Swap(plan, 2, DumbbellPress);

        Assert.Equal([(1, DumbbellPress, 3, false), (2, DumbbellPress, 3, false)], Lines(doubled).Take(2));
        Assert.Equal(new PlanStep([DumbbellPress], [2], 1, 3), WorkoutPlan.Next(doubled, sets, NoHistory));

        var removed = PlanEdits.Remove(doubled, 2);

        Assert.Equal([(1, DumbbellPress, 3, false), (3, Fly, 3, false)], Lines(removed).Take(2));
        Assert.True(WorkoutPlan.Progress([removed[0]], sets, NoHistory).Complete);
        Assert.Equal(new PlanStep([Fly], [3], 1, 3), WorkoutPlan.Next(removed, sets, NoHistory));
    }

    [Fact]
    public void SwapRest_PartWay_WhatsDoneStaysWhatItWas_AndTheNewOneTakesTheSetsLeft()
    {
        var plan = ChestDay();
        List<WorkoutSet> sets = [Set(1, Bench, 1)];

        var swapped = PlanEdits.SwapRest(plan, 1, DumbbellPress, doneLine: 6, done: 1, left: 2);
        sets[0].Line = 6;

        Assert.Equal([(6, Bench, 1, false), (1, DumbbellPress, 2, false), (2, Incline, 3, false)], Lines(swapped).Take(3));
        Assert.True(WorkoutPlan.Progress([swapped[0]], sets, NoHistory).Complete);
        Assert.Equal(new PlanStep([DumbbellPress], [1], 1, 2), WorkoutPlan.Next(swapped, sets, NoHistory));
    }

    [Fact]
    public void SwapRest_InASuperset_TheOtherKeepsItsRounds()
    {
        var swapped = PlanEdits.SwapRest(ChestDay(), 5, Skulls, doneLine: 6, done: 1, left: 2);

        Assert.Equal([(6, Dips, 1, false), (4, PullUps, 3, true), (5, Skulls, null, true)], Lines(swapped).Skip(3));
    }

    [Fact]
    public void Join_TwoLinesGoTogether_ForTheFirstsCount()
    {
        var joined = PlanEdits.Join(ChestDay(), 3, 2);

        Assert.Equal([(1, Bench, 3, false), (3, Fly, 3, true), (2, Incline, null, true), (4, PullUps, 3, true), (5, Dips, null, true)], Lines(joined));
        Assert.NotEqual(joined[1].SupersetGroup, joined[3].SupersetGroup);
    }

    [Fact]
    public void Join_WithASuperset_TakesAllOfIt_AndWithoutACountTakesTheOthers()
    {
        var plan = PlanEdits.Count(ChestDay(), 3, null);

        var joined = PlanEdits.Join(plan, 3, 5);

        Assert.Equal([(3, Fly, 3, true), (4, PullUps, null, true), (5, Dips, null, true)], Lines(joined).Skip(2));
        // Already together, or with a line that isn't there: as it was.
        Assert.Equal(Lines(plan), Lines(PlanEdits.Join(plan, 4, 5)));
        Assert.Equal(Lines(plan), Lines(PlanEdits.Join(plan, 4, 9)));
    }

    [Fact]
    public void JoinNew_AnExerciseThePlanDoesntHave_GoesWithTheLineOnALineOfItsOwn()
    {
        var joined = PlanEdits.JoinNew(ChestDay(), 3, Skulls, newLine: 6);

        Assert.Equal([(3, Fly, 3, true), (6, Skulls, null, true)], Lines(joined).Skip(2).Take(2));
    }

    [Fact]
    public void Split_EachExerciseGoesOnAlone_WithTheSupersetsRounds()
    {
        var split = PlanEdits.Split(ChestDay(), 5);

        Assert.Equal([(4, PullUps, 3, false), (5, Dips, 3, false)], Lines(split).Skip(3));
        Assert.Equal(Lines(ChestDay()), Lines(PlanEdits.Split(ChestDay(), 1)));
    }

    [Fact]
    public void TakeOut_OneLeavesTheSuperset_AndComesRightAfter()
    {
        var plan = PlanEdits.JoinNew(ChestDay(), 4, Skulls, newLine: 6);

        var out1 = PlanEdits.TakeOut(plan, 4);

        // The rounds stay with the rest, though the one that carried them left.
        Assert.Equal([(5, Dips, 3, true), (6, Skulls, null, true), (4, PullUps, 3, false)], Lines(out1).Skip(3));
        Assert.Equal([(4, PullUps, 3, false), (5, Dips, 3, false)], Lines(PlanEdits.TakeOut(ChestDay(), 5)).Skip(3));
    }

    [Fact]
    public void Remove_NoDipStation_PullUpsKeepTheirRoundsAlone()
    {
        var plan = ChestDay();

        Assert.Equal([(3, Fly, 3, false), (5, Dips, 3, false)], Lines(PlanEdits.Remove(plan, 4)).Skip(2));
        Assert.Equal([Bench, Incline, Fly], PlanEdits.RemoveBlock(plan, 4).Select(e => e.ExerciseId));
        Assert.Equal([Bench, Fly, PullUps, Dips], PlanEdits.Remove(plan, 2).Select(e => e.ExerciseId));
    }

    [Fact]
    public void Add_AndAppend_GiveNewLinesTheirNumbers_AndKeepASessionsSupersetsItsOwn()
    {
        var plan = ChestDay();
        List<WorkoutSet> sets = [Set(1, Bench, 1)];
        TemplateExercise[] arms = [Entry(Skulls, 0, sets: 3, superset: 1), Entry(Pulldown, 1, superset: 1)];

        var line = PlanEdits.NextLine(plan, sets);
        var added = PlanEdits.Add(plan, Skulls, line);
        var first = PlanEdits.Add(plan, Skulls, line, beforeLine: 5, count: 2);
        var appended = PlanEdits.Append(plan, arms, line);

        Assert.Equal(6, line);
        Assert.Equal((6, Skulls, null, false), Lines(added).Last());
        Assert.Equal([(3, Fly, 3, false), (6, Skulls, 2, false), (4, PullUps, 3, true)], Lines(first).Skip(2).Take(3));
        Assert.Equal([(6, Skulls, 3, true), (7, Pulldown, null, true)], Lines(appended).Skip(5));
        Assert.NotEqual(appended[3].SupersetGroup, appended[5].SupersetGroup);
        // A set on a line the plan no longer has still keeps its number from being used again.
        Assert.Equal(10, PlanEdits.NextLine(plan, [Set(9, Skulls, 1)]));
    }

    [Fact]
    public void Keep_LeavesOnlyTheBlocksStarted_BeforeAnotherSessionTakesTheRestsPlace()
    {
        var plan = ChestDay();
        List<WorkoutSet> sets = [Set(1, Bench, 1), Set(4, PullUps, 2), Set(5, Dips, 3)];

        var kept = PlanEdits.Keep(plan, block => WorkoutPlan.Progress(block, sets, NoHistory).Started);

        Assert.Equal([Bench, PullUps, Dips], kept.Select(e => e.ExerciseId));
    }

    [Fact]
    public void StayDone_ALineThatWasDone_IsDoneWithTheSetsItHasLeft_RatherThanToDoAgain()
    {
        // Bench had its 3 sets; the third was deleted, or was really another exercise.
        var plan = ChestDay();
        List<WorkoutSet> left = [Set(1, Bench, 1), Set(1, Bench, 2)];

        var settled = PlanEdits.StayDone(plan, 1, left, NoHistory);

        Assert.Equal(2, settled[0].PlannedSets);
        Assert.True(WorkoutPlan.Progress([settled[0]], left, NoHistory).Complete);
        Assert.Equal(new PlanStep([Incline], [2], 1, 3), WorkoutPlan.Next(settled, left, NoHistory));
    }

    [Fact]
    public void StayDone_WithNoSetLeft_TheLineLeavesThePlan()
    {
        // A set on a line of its own turned out to be bench: nothing is left on that line, so it isn't offered as a set to do.
        var plan = PlanEdits.Add(ChestDay(), DumbbellPress, newLine: 6, beforeLine: 2, count: 1);
        List<WorkoutSet> left = [Set(1, Bench, 1), Set(1, Bench, 2), Set(1, Bench, 3), Set(1, Bench, 4)];

        var settled = PlanEdits.StayDone(plan, 6, left, NoHistory);

        Assert.Equal([Bench, Incline, Fly, PullUps, Dips], settled.Select(e => e.ExerciseId));
    }

    [Fact]
    public void StayDone_ASupersetStillAtItsRounds_OrALineWithARunningSet_IsLeftAsItIs()
    {
        var plan = ChestDay();
        // Three rounds of pull-ups, and dips short of one: the superset is at its rounds by the exercise that's furthest.
        List<WorkoutSet> rounds = [Set(4, PullUps, 1), Set(5, Dips, 2), Set(4, PullUps, 3), Set(5, Dips, 4), Set(4, PullUps, 5)];
        var running = new WorkoutSet { Line = 1, ExerciseId = Bench, Order = 1, StartedAt = Start };

        Assert.Equal(Lines(plan), Lines(PlanEdits.StayDone(plan, 5, rounds, NoHistory)));
        Assert.Equal(Lines(plan), Lines(PlanEdits.StayDone(plan, 1, [running], NoHistory)));
    }

    [Fact]
    public void StayDone_OnlyAWarmUpLeft_IsDoneWithNoSets()
    {
        var plan = ChestDay();
        var warmUp = Set(1, Bench, 1);
        warmUp.WarmUp = true;

        var settled = PlanEdits.StayDone(plan, 1, [warmUp], NoHistory);

        Assert.Equal(0, settled[0].PlannedSets);
        Assert.Equal(new PlanStep([Incline], [2], 1, 3), WorkoutPlan.Next(settled, [warmUp], NoHistory));
    }

    [Fact]
    public void AsSession_KeepsTheExercisesOrderSetsAndSupersets_WithoutLineNumbers()
    {
        var session = PlanEdits.AsSession(PlanEdits.MoveBefore(ChestDay(), 4, 1));

        Assert.All(session, e => Assert.Equal(0, e.Line));
        Assert.Equal([PullUps, Dips, Bench, Incline, Fly], session.Select(e => e.ExerciseId));
        Assert.Equal([3, null, 3, 3, 3], session.Select(e => e.PlannedSets));
        Assert.Equal(session[0].SupersetGroup, session[1].SupersetGroup);
        Assert.Null(session[2].SupersetGroup);
    }
}
