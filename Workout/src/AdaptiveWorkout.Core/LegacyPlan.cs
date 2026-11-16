namespace AdaptiveWorkout.Core;

/// <summary>
/// A workout from before its lines owned their sets, brought over once, as it's opened: back then a set only named its exercise,
/// and where it counted in the plan was worked out by walking the sets in order. This walks them that way one last time and
/// writes the answer down, as each set's line.
/// </summary>
public static class LegacyPlan
{
    /// <summary>
    /// Numbers the plan's lines and gives every set its line. A set the plan no longer has (its exercise was swapped out or
    /// dropped), and every set when nothing was planned, gets a line for its exercise ahead of the rest, done with the sets it has;
    /// when nothing was planned, the exercise I'm on stays open for more. A planned exercise I went past, which used to come back
    /// only when picked, waits right after the one I'm on; one with no count that I'd started and moved on from stays done.
    /// </summary>
    /// <param name="sets">The workout's sets in order, running ones too; each one's <see cref="WorkoutSet.Line"/> is set.</param>
    /// <returns>The plan, its lines numbered.</returns>
    public static List<TemplateExercise> Number(IReadOnlyList<TemplateExercise> plan, IReadOnlyList<WorkoutSet> sets, IReadOnlyDictionary<long, int> setsLastTime)
    {
        var numbered = PlanEdits.Copy(plan);
        var blocks = WorkoutPlan.Blocks(numbered);
        var tally = new Tally(blocks, sets, setsLastTime);
        for (var block = 0; block < blocks.Count; block++)
        {
            foreach (var set in tally.Sets[block]) set.Line = blocks[block].First(e => e.ExerciseId == set.ExerciseId).Line;
        }

        // Went past: ahead of where I am, with sets still to do. They wait right after the block I'm on, in their order. One
        // without a count that I'd started was over once I went on to something else: it's done with the sets it has.
        if (tally.Position > 0)
        {
            var after = tally.Position + 1 < blocks.Count ? blocks[tally.Position + 1][0].Line : 0;
            foreach (var passed in blocks.Take(tally.Position))
            {
                var progress = WorkoutPlan.Progress(passed, sets, setsLastTime);
                if (progress.Complete) continue;

                numbered = progress is { Target: null, Started: true }
                    ? PlanEdits.Count(numbered, passed[0].Line, progress.Done)
                    : PlanEdits.MoveBefore(numbered, passed[0].Line, after);
            }
        }

        var first = numbered.Count > 0 ? numbered[0].Line : 0;
        var line = numbered.Count;
        foreach (var loose in sets.Where(s => s.Line == 0).GroupBy(s => s.ExerciseId).ToList())
        {
            line++;
            foreach (var set in loose) set.Line = line;
            numbered = PlanEdits.Add(numbered, loose.Key, line, first, loose.Count(s => !s.WarmUp && s.StoppedAt is not null));
        }

        return plan.Count == 0 && sets.Count > 0 ? PlanEdits.Count(numbered, sets[^1].Line, null) : numbered;
    }

    // Where the sets went in the plan, by the old rules. Walking them in order, a set counts for the block I'm on while I stay with
    // its exercises; else for the first block with its exercise still to do from where I am, else anywhere, else its last block.
    // So an exercise done twice (in a superset, then in another one's place) counts afresh the second time. Once the block I'm on
    // has had its sets, a set of what the plan offers next counts there instead. A set the plan doesn't have counts for nothing in
    // it and leaves me where I was. A warm-up set puts me on its block without counting as one of its sets.
    private sealed class Tally
    {
        private readonly List<List<TemplateExercise>> blocks;
        private readonly int?[] targets;
        private readonly int[][] counts;
        private readonly int[] lastSet;

        public Tally(List<List<TemplateExercise>> blocks, IReadOnlyList<WorkoutSet> sets, IReadOnlyDictionary<long, int> setsLastTime)
        {
            this.blocks = blocks;
            targets = [.. blocks.Select(b => b[0].PlannedSets ?? (b.Max(e => setsLastTime.GetValueOrDefault(e.ExerciseId)) is var most && most > 0 ? most : null))];
            counts = [.. blocks.Select(b => new int[b.Count])];
            lastSet = [.. blocks.Select(_ => -1)];
            Sets = [.. blocks.Select(_ => new List<WorkoutSet>())];
            for (var k = 0; k < sets.Count; k++)
            {
                var exerciseId = sets[k].ExerciseId;
                var holding = Enumerable.Range(0, blocks.Count).Where(b => blocks[b].Any(e => e.ExerciseId == exerciseId)).ToList();
                if (holding.Count == 0) continue;

                // Where this exercise is in the block I'm on, when it's in it: it has had its sets there once its own count is the block's.
                var here = holding.Contains(Position) ? blocks[Position].FindIndex(e => e.ExerciseId == exerciseId) : -1;
                var at = here < 0
                    ? holding.Where(b => b > Position && !Full(b)).Cast<int?>().FirstOrDefault() ?? holding.Where(b => !Full(b)).Cast<int?>().FirstOrDefault() ?? holding[^1]
                    : targets[Position] is int target && counts[Position][here] >= target && NextBlock() is int offered && holding.Contains(offered) ? offered : Position;
                if (!sets[k].WarmUp) counts[at][blocks[at].FindIndex(e => e.ExerciseId == exerciseId)]++;
                lastSet[at] = k;
                Sets[at].Add(sets[k]);
                Position = at;
            }
        }

        public List<List<WorkoutSet>> Sets { get; }

        /// <summary>Where I am in the plan: the block of the last set the plan has, or -1 before any.</summary>
        public int Position { get; private set; } = -1;

        // The block I'm on while it's still to do, else the first one after it that is.
        private int? NextBlock()
        {
            for (var block = Math.Max(Position, 0); block < blocks.Count; block++)
            {
                if (Offers(block)) return block;
            }

            return null;
        }

        // Whether a block is still to do: short of its sets, or without a count, not yet moved on from.
        private bool Offers(int block) =>
            targets[block] is int target ? counts[block].Max() < target : lastSet[block] < 0 || !lastSet.Skip(block + 1).Any(k => k > lastSet[block]);

        private bool Full(int block) => targets[block] is int target && counts[block].Max() >= target;
    }
}
