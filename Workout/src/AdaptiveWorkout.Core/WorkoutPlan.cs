namespace AdaptiveWorkout.Core;

/// <summary>What the big button offers next: a set of one line's exercise, or a superset's round, whose exercises start and stop together.</summary>
/// <param name="ExerciseIds">The exercise, or the superset's exercises in order.</param>
/// <param name="Lines">The lines they're on, in the same order: the sets it starts belong to them.</param>
/// <param name="SetNumber">The set's number on its line; for a superset, the round's.</param>
/// <param name="TargetSets">Sets (or rounds) planned for it, else as many as last time until that's passed; empty when unknown.</param>
public sealed record PlanStep(IReadOnlyList<long> ExerciseIds, IReadOnlyList<int> Lines, int SetNumber, int? TargetSets)
{
    /// <summary>The exercise, or the superset's first.</summary>
    public long ExerciseId => ExerciseIds[0];

    /// <summary>A superset's round: more than one exercise, back to back with no break between them.</summary>
    public bool IsRound => ExerciseIds.Count > 1;

    public bool Equals(PlanStep? other) =>
        other is not null && ExerciseIds.SequenceEqual(other.ExerciseIds) && Lines.SequenceEqual(other.Lines) && SetNumber == other.SetNumber && TargetSets == other.TargetSets;

    public override int GetHashCode() => HashCode.Combine(ExerciseId, SetNumber, TargetSets);
}

/// <summary>How far one block of a workout's plan is: an exercise's line, or a superset's lines together.</summary>
/// <param name="Done">Sets done, warm-ups aside; for a superset, rounds, by its furthest exercise.</param>
/// <param name="Target">The sets it's done after: planned, else as many as last time. Empty when it goes on until I move on.</param>
/// <param name="Running">A set of it is running.</param>
/// <param name="Started">It has a set of any kind: done, running or a warm-up.</param>
public readonly record struct BlockProgress(int Done, int? Target, bool Running, bool Started)
{
    /// <summary>Nothing of it is left to do.</summary>
    public bool Complete => !Running && Target is int target && Done >= target;

    /// <summary>The count shown while editing. With no target, a started exercise shows its current or next set.</summary>
    public int? EditableCount => Target is int count ? Math.Max(count, Running ? Done + 1 : Done) : Started ? Done + 1 : null;
}

/// <summary>
/// A workout's plan is its lines in the order they're done: each line is an exercise in its place, with its own sets (a set carries
/// its line's number), and neighbors with one superset group are done together. What's next is the first block, in that order,
/// with sets still to do. Nothing is matched by exercise: the same exercise can be on two lines, each with its own sets.
/// </summary>
public static class WorkoutPlan
{
    /// <summary>The plan in order, one block per exercise or superset.</summary>
    public static List<List<TemplateExercise>> Blocks(IReadOnlyList<TemplateExercise> plan)
    {
        var blocks = new List<List<TemplateExercise>>();
        foreach (var entry in plan.OrderBy(e => e.Order))
        {
            var joinsSuperset = entry.SupersetGroup is not null && blocks.Count > 0 && blocks[^1][0].SupersetGroup == entry.SupersetGroup;
            if (joinsSuperset) blocks[^1].Add(entry);
            else blocks.Add([entry]);
        }

        return blocks;
    }

    /// <summary>
    /// How far a block is, from the sets on its lines. A block is done after its planned sets, else as many as last time. Without
    /// either, or when <paramref name="openEnded"/> (just tracking: nothing is planned, so nothing is done until I say), it goes on
    /// until its count is set. A warm-up set isn't one of an exercise's sets.
    /// </summary>
    /// <param name="sets">The workout's sets, running ones too.</param>
    /// <param name="setsLastTime">Per exercise, how many sets I did the last time.</param>
    public static BlockProgress Progress(IReadOnlyList<TemplateExercise> block, IReadOnlyList<WorkoutSet> sets, IReadOnlyDictionary<long, int> setsLastTime, bool openEnded = false)
    {
        var (done, running, started) = (0, false, false);
        foreach (var entry in block)
        {
            var count = 0;
            foreach (var set in sets)
            {
                if (set.Line != entry.Line) continue;

                started = true;
                if (set.StoppedAt is null) running = true;
                else if (!set.WarmUp) count++;
            }

            done = Math.Max(done, count);
        }

        var target = block.All(e => e.FinishedByUser) ? done : block[0].PlannedSets ?? (openEnded ? null : LastTime(block, setsLastTime));
        return new BlockProgress(done, target, running, started);
    }

    /// <summary>Where <see cref="Next"/>'s step is in the plan: the first block of <see cref="Blocks"/> that isn't done; null when all are.</summary>
    public static int? NextBlock(IReadOnlyList<TemplateExercise> plan, IReadOnlyList<WorkoutSet> sets, IReadOnlyDictionary<long, int> setsLastTime, bool openEnded = false, int after = -1)
    {
        var blocks = Blocks(plan);
        for (var block = after + 1; block < blocks.Count; block++)
        {
            if (!Progress(blocks[block], sets, setsLastTime, openEnded).Complete) return block;
        }

        return null;
    }

    /// <summary>The running block, or the block being continued after its last stopped set. A fresh upcoming slot is separate.</summary>
    public static List<TemplateExercise>? ActiveBlock(List<List<TemplateExercise>> blocks, IReadOnlyList<WorkoutSet> sets, int? nextBlock)
    {
        if (sets.FirstOrDefault(s => s.StoppedAt is null) is { } running) return blocks.Find(b => b.Exists(e => e.Line == running.Line));
        if (sets.Count == 0 || nextBlock is not int next) return null;
        return blocks[next].Exists(e => e.Line == sets[^1].Line) ? blocks[next] : null;
    }

    /// <summary>
    /// The next step, or null when nothing in the plan is left: the first block in order with sets still to do, on the set (or
    /// round) after those done. A superset's round is one step and counts as one set of each of its exercises.
    /// </summary>
    public static PlanStep? Next(IReadOnlyList<TemplateExercise> plan, IReadOnlyList<WorkoutSet> sets, IReadOnlyDictionary<long, int> setsLastTime, bool openEnded = false) =>
        NextBlock(plan, sets, setsLastTime, openEnded) is int at ? Step(Blocks(plan)[at], sets, setsLastTime) : null;

    /// <summary>The first block after this one with sets still to do.</summary>
    public static PlanStep? After(IReadOnlyList<TemplateExercise> plan, IReadOnlyList<WorkoutSet> sets, IReadOnlyDictionary<long, int> setsLastTime, int block, bool openEnded = false) =>
        NextBlock(plan, sets, setsLastTime, openEnded, block) is int at ? Step(Blocks(plan)[at], sets, setsLastTime) : null;

    /// <summary>
    /// The step a block is on: all its exercises together, on the set (or round) after those done. So when one of a superset joins
    /// part way, it joins the next round rather than starting over. The count shown is the planned one, else last time's until
    /// it's passed.
    /// </summary>
    public static PlanStep Step(IReadOnlyList<TemplateExercise> block, IReadOnlyList<WorkoutSet> sets, IReadOnlyDictionary<long, int> setsLastTime)
    {
        var number = Progress(block, sets, setsLastTime).Done + 1;
        var shown = block[0].PlannedSets ?? (LastTime(block, setsLastTime) is int last && last >= number ? last : null);
        return new PlanStep([.. block.Select(e => e.ExerciseId)], [.. block.Select(e => e.Line)], number, shown);
    }

    /// <summary>
    /// Where a set sits in the plan ("set 2 of 3", "round 1 of 3"): its block's step with only the sets before its round counted.
    /// A set whose line is no longer in the plan counts among its own line's sets.
    /// </summary>
    public static PlanStep StepOf(IReadOnlyList<TemplateExercise> plan, IReadOnlyList<WorkoutSet> sets, WorkoutSet set, IReadOnlyDictionary<long, int> setsLastTime)
    {
        // Sets started with it are its round's, not before it.
        var before = sets.Where(s => s.Order < set.Order && s.StoppedAt is not null && (s.StartedAt is null || s.StartedAt != set.StartedAt)).ToList();
        if (Blocks(plan).Find(b => b.Any(e => e.Line == set.Line)) is { } block) return Step(block, before, setsLastTime);

        var number = before.Working().Count(s => s.Line == set.Line && s.ExerciseId == set.ExerciseId) + 1;
        return new PlanStep([set.ExerciseId], [set.Line], number, setsLastTime.GetValueOrDefault(set.ExerciseId) is var last && last >= number ? last : null);
    }

    // As many sets as last time: a superset's rounds go by the exercise that had the most. Empty when none was done before.
    private static int? LastTime(IReadOnlyList<TemplateExercise> block, IReadOnlyDictionary<long, int> setsLastTime) =>
        block.Max(e => setsLastTime.GetValueOrDefault(e.ExerciseId)) is var most && most > 0 ? most : null;
}
