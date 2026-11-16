namespace AdaptiveWorkout.Core;

/// <summary>
/// Changes to a plan: one workout's own lines while it's going, or a session's exercises in its editor. A workout's session never
/// changes with it. Each edit returns a new plan, numbered from 0, with one superset group number per superset and each line's
/// number kept; the plan handed in is left as it was, so it can be gone back to. A line is named by its number
/// (<see cref="TemplateExercise.Line"/>), never by its exercise, which can be on two lines.
/// </summary>
public static class PlanEdits
{
    /// <summary>A session's exercises as a plan to change: a workout's own, or the session editor's. Its lines are numbered from 1.</summary>
    public static List<TemplateExercise> Copy(IReadOnlyList<TemplateExercise> entries)
    {
        var plan = Build(Parse(entries));
        for (var i = 0; i < plan.Count; i++) plan[i].Line = i + 1;
        return plan;
    }

    /// <summary>A plan as a session's exercises: the same exercises, order, counts and supersets, without line numbers.</summary>
    public static List<TemplateExercise> AsSession(IReadOnlyList<TemplateExercise> plan)
    {
        var entries = Build(Parse(plan));
        foreach (var entry in entries) entry.Line = 0;
        return entries;
    }

    /// <summary>A number for a new line: one no line of the plan and no set has.</summary>
    public static int NextLine(IReadOnlyList<TemplateExercise> plan, IReadOnlyList<WorkoutSet> sets) =>
        Math.Max(plan.Count == 0 ? 0 : plan.Max(e => e.Line), sets.Count == 0 ? 0 : sets.Max(s => s.Line)) + 1;

    /// <summary>
    /// Moves a line's block (a superset moves whole) to just before another line's block, e.g. to do it next; with no other line
    /// (0), after everything else. Unknown lines leave the plan as it is.
    /// </summary>
    public static List<TemplateExercise> MoveBefore(IReadOnlyList<TemplateExercise> plan, int line, int beforeLine)
    {
        var blocks = Parse(plan);
        if (Find(blocks, line) is not { } moving || moving.Has(beforeLine)) return Build(blocks);

        blocks.Remove(moving);
        var before = beforeLine == 0 ? null : Find(blocks, beforeLine);
        blocks.Insert(before is null ? blocks.Count : blocks.IndexOf(before), moving);
        return Build(blocks);
    }

    /// <summary>Two lines' blocks change places, e.g. a step up or down the list. Unknown lines leave the plan as it is.</summary>
    public static List<TemplateExercise> Exchange(IReadOnlyList<TemplateExercise> plan, int line, int otherLine)
    {
        var blocks = Parse(plan);
        if (Find(blocks, line) is not { } one || Find(blocks, otherLine) is not { } other || one == other) return Build(blocks);

        var (i, j) = (blocks.IndexOf(one), blocks.IndexOf(other));
        (blocks[i], blocks[j]) = (other, one);
        return Build(blocks);
    }

    /// <summary>Sets a line's sets (its superset's rounds); empty means as many as last time.</summary>
    public static List<TemplateExercise> Count(IReadOnlyList<TemplateExercise> plan, int line, int? count)
    {
        var blocks = Parse(plan);
        if (Find(blocks, line) is { } block) block.Count = count;
        return Build(blocks);
    }

    /// <summary>Puts another exercise on a line: same spot, same sets, same superset.</summary>
    public static List<TemplateExercise> Swap(IReadOnlyList<TemplateExercise> plan, int line, long exerciseId)
    {
        var blocks = Parse(plan);
        if (Find(blocks, line)?.Entry(line) is { } entry) entry.ExerciseId = exerciseId;
        return Build(blocks);
    }

    /// <summary>
    /// Puts another exercise on a line part way: what's done stays the exercise it was, on a line of its own
    /// (<paramref name="doneLine"/>) just before, done with its <paramref name="done"/> sets; the line carries on as the new
    /// exercise for the sets <paramref name="left"/> (in a superset, for the rounds left). The caller gives the done sets the new line.
    /// </summary>
    public static List<TemplateExercise> SwapRest(IReadOnlyList<TemplateExercise> plan, int line, long exerciseId, int doneLine, int done, int? left)
    {
        var blocks = Parse(plan);
        if (Find(blocks, line) is not { } block) return Build(blocks);

        var entry = block.Entry(line)!;
        blocks.Insert(blocks.IndexOf(block), new Block(done, [new TemplateExercise { ExerciseId = entry.ExerciseId, Line = doneLine }]));
        entry.ExerciseId = exerciseId;
        if (block.Lines.Count == 1) block.Count = left;
        return Build(blocks);
    }

    /// <summary>
    /// Does one line's block together with another's, as one superset: the other's exercises come after, for the first's count, or
    /// else the other's. Already together, or with an unknown line, the plan stays as it is.
    /// </summary>
    public static List<TemplateExercise> Join(IReadOnlyList<TemplateExercise> plan, int line, int withLine)
    {
        var blocks = Parse(plan);
        if (Find(blocks, line) is not { } block || Find(blocks, withLine) is not { } other || block == other) return Build(blocks);

        blocks.Remove(other);
        block.Lines.AddRange(other.Lines);
        block.Count ??= other.Count;
        return Build(blocks);
    }

    /// <summary>Does an exercise the plan doesn't have together with a line's block, as a superset, on a new line after its exercises.</summary>
    public static List<TemplateExercise> JoinNew(IReadOnlyList<TemplateExercise> plan, int line, long exerciseId, int newLine)
    {
        var blocks = Parse(plan);
        Find(blocks, line)?.Lines.Add(new TemplateExercise { ExerciseId = exerciseId, Line = newLine });
        return Build(blocks);
    }

    /// <summary>Takes a line's superset apart, where it is: each exercise alone, in their order, each with the superset's count.</summary>
    public static List<TemplateExercise> Split(IReadOnlyList<TemplateExercise> plan, int line)
    {
        var blocks = Parse(plan);
        if (Find(blocks, line) is not { Lines.Count: > 1 } block) return Build(blocks);

        var at = blocks.IndexOf(block);
        blocks.RemoveAt(at);
        blocks.InsertRange(at, block.Lines.Select(e => new Block(block.Count, [e])));
        return Build(blocks);
    }

    /// <summary>Takes one line out of its superset: it comes right after, alone, with the superset's count. The rest keep the rounds.</summary>
    public static List<TemplateExercise> TakeOut(IReadOnlyList<TemplateExercise> plan, int line)
    {
        var blocks = Parse(plan);
        if (Find(blocks, line) is not { Lines.Count: > 1 } block) return Build(blocks);

        var entry = block.Entry(line)!;
        block.Lines.Remove(entry);
        blocks.Insert(blocks.IndexOf(block) + 1, new Block(block.Count, [entry]));
        return Build(blocks);
    }

    /// <summary>Leaves a line out. The rest of its superset keeps the rounds.</summary>
    public static List<TemplateExercise> Remove(IReadOnlyList<TemplateExercise> plan, int line)
    {
        var blocks = Parse(plan);
        if (Find(blocks, line) is { } block) block.Lines.Remove(block.Entry(line)!);
        return Build(blocks);
    }

    /// <summary>Leaves a line's whole block out: the exercise, or its superset with every exercise in it.</summary>
    public static List<TemplateExercise> RemoveBlock(IReadOnlyList<TemplateExercise> plan, int line)
    {
        var blocks = Parse(plan);
        if (Find(blocks, line) is { } block) blocks.Remove(block);
        return Build(blocks);
    }

    /// <summary>
    /// A line that was done stays done when a set leaves it (deleted, or put on another exercise's line): its block is done with
    /// the sets it has left, rather than coming back as something to do, and with none left it leaves the plan. A block still at
    /// its count is left as it is. For a line that was done before the set left; one that wasn't keeps its count.
    /// </summary>
    /// <param name="sets">The workout's sets, without the one that left.</param>
    public static List<TemplateExercise> StayDone(IReadOnlyList<TemplateExercise> plan, int line, IReadOnlyList<WorkoutSet> sets, IReadOnlyDictionary<long, int> setsLastTime, bool openEnded = false)
    {
        if (WorkoutPlan.Blocks(plan).Find(b => b.Exists(e => e.Line == line)) is not { } block) return Build(Parse(plan));

        var progress = WorkoutPlan.Progress(block, sets, setsLastTime, openEnded);
        if (progress.Complete || progress.Running) return Build(Parse(plan));

        return progress.Started ? Count(plan, line, progress.Done) : RemoveBlock(plan, line);
    }

    /// <summary>Keeps only the blocks <paramref name="keep"/> says to, e.g. the ones started, before another session takes the rest's place.</summary>
    public static List<TemplateExercise> Keep(IReadOnlyList<TemplateExercise> plan, Func<IReadOnlyList<TemplateExercise>, bool> keep) =>
        Build(Parse(plan).Where(b => keep(b.Lines)));

    /// <summary>Adds an exercise as a line of its own just before another line's block; with no other line (0), after everything else.</summary>
    public static List<TemplateExercise> Add(IReadOnlyList<TemplateExercise> plan, long exerciseId, int newLine, int beforeLine = 0, int? count = null)
    {
        var blocks = Parse(plan);
        var before = beforeLine == 0 ? null : Find(blocks, beforeLine);
        blocks.Insert(before is null ? blocks.Count : blocks.IndexOf(before), new Block(count, [new TemplateExercise { ExerciseId = exerciseId, Line = newLine }]));
        return Build(blocks);
    }

    /// <summary>Adds a session's exercises after everything else, on new lines from <paramref name="firstLine"/>. Its supersets stay its own.</summary>
    public static List<TemplateExercise> Append(IReadOnlyList<TemplateExercise> plan, IReadOnlyList<TemplateExercise> entries, int firstLine)
    {
        var added = Parse(entries);
        foreach (var entry in added.SelectMany(b => b.Lines)) entry.Line = firstLine++;
        return Build([.. Parse(plan), .. added]);
    }

    // A plan's blocks, each with its count and copies of its lines, so an edit never touches the plan it was handed.
    private static List<Block> Parse(IReadOnlyList<TemplateExercise> plan) =>
        [.. WorkoutPlan.Blocks(plan).Select(b => new Block(b[0].PlannedSets, [.. b.Select(e => new TemplateExercise { ExerciseId = e.ExerciseId, Line = e.Line })]))];

    private static Block? Find(List<Block> blocks, int line) => blocks.Find(b => b.Has(line));

    // Numbers the entries in order. A block's count lives on its first entry, and a superset's entries share a group number.
    private static List<TemplateExercise> Build(IEnumerable<Block> blocks)
    {
        var plan = new List<TemplateExercise>();
        var group = 0;
        foreach (var block in blocks.Where(b => b.Lines.Count > 0))
        {
            group++;
            for (var i = 0; i < block.Lines.Count; i++)
            {
                plan.Add(new TemplateExercise
                {
                    ExerciseId = block.Lines[i].ExerciseId, Line = block.Lines[i].Line, Order = plan.Count,
                    PlannedSets = i == 0 ? block.Count : null, SupersetGroup = block.Lines.Count > 1 ? group : null,
                });
            }
        }

        return plan;
    }

    // An exercise's line, or a superset's lines, with the sets (or rounds) planned for them.
    private sealed class Block(int? count, List<TemplateExercise> lines)
    {
        public int? Count { get; set; } = count;

        public List<TemplateExercise> Lines { get; } = lines;

        public bool Has(int line) => line != 0 && Lines.Exists(e => e.Line == line);

        public TemplateExercise? Entry(int line) => Lines.Find(e => e.Line == line);
    }
}
