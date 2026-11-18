using AdaptiveWorkout.Core;

namespace AdaptiveWorkout.UI.Workouts;

/// <summary>The open workout, kept in memory so every tap updates the screen at once. <see cref="WorkoutFlow"/> changes it and saves in the background.</summary>
public sealed class WorkoutState
{
    private readonly Dictionary<long, IReadOnlyList<PastSession>> historyCache = [];
    private readonly Dictionary<long, Plateau?> plateauCache = [];
    private Dictionary<long, int> setsLastTime = [];

    public required WorkoutSession Session { get; init; }
    public SessionTemplate? Template { get; init; }

    /// <summary>
    /// This workout's own plan: its lines in the order they're done, a copy of the session's exercises changed as it goes; when just
    /// tracking, a line for each exercise as it's picked. Each line owns its sets (<see cref="WorkoutSet.Line"/>). A change puts a
    /// new list here and leaves the old one as it was, so it can be gone back to.
    /// </summary>
    public required List<TemplateExercise> Plan { get; set; }

    public required Dictionary<long, Exercise> Exercises { get; init; }

    /// <summary>This session's sets, in order. The last ones may still be running.</summary>
    public required List<WorkoutSet> Sets { get; init; }

    /// <summary>Every other session and set, for history and suggestions.</summary>
    public required IReadOnlyList<WorkoutSession> PastSessions { get; init; }

    public required IReadOnlyList<WorkoutSet> PastSets { get; init; }
    public required double HitGoalStep { get; init; }

    /// <summary>The latest weigh-in, in pounds, for comparing bodyweight exercises over time; null without one.</summary>
    public double? BodyPounds { get; init; }

    /// <summary>The trainer's applied plans for exercises' next times, by exercise; each counts only for the time after the workout it looked back at.</summary>
    public Dictionary<long, TrainerTarget> TrainerTargets { get; init; } = [];

    /// <summary>Sets up to this order were entered at an earlier rest, or before the workout was reopened.</summary>
    public int LoggedThrough { get; set; }

    /// <summary>The last set's order; 0 before the first. Orders can skip numbers, after a set is deleted.</summary>
    public int LastOrder => Sets.Count == 0 ? 0 : Sets[^1].Order;

    /// <summary>
    /// What the rest screen asks for, until the next set starts: the weight sets stopped since the last rest. A superset round's
    /// sets all wait for the rest after the round. Time sets need nothing; the stopwatch was the entry.
    /// </summary>
    public List<WorkoutSet> ToConfirm => [.. Sets.Where(s => s.StoppedAt is not null && s.Order > LoggedThrough && Exercises[s.ExerciseId].Measure == ExerciseMeasure.Weight)];

    public string Name => Template?.Name ?? "Workout";

    /// <summary>Just tracking: nothing was planned, so no exercise is done until I move on from it.</summary>
    public bool OpenEnded => Session.TemplateId is null;

    /// <summary>Warming up: the workout started with a warm-up, and no exercise has started yet.</summary>
    public bool WarmingUp => Session is { WarmUp: true, WarmUpEndedAt: null };

    /// <summary>Cooling down, from Start cool-down until Finish.</summary>
    public bool CoolingDown => Session.CoolDownStartedAt is not null;

    /// <summary>A cool-down is planned and hasn't started, so finishing starts it instead.</summary>
    public bool CoolDownNext => Session is { CoolDown: true, CoolDownStartedAt: null };

    /// <summary>The sets running now, which stop together: one, or a superset's round. Empty between sets.</summary>
    public List<WorkoutSet> Running => [.. Sets.Where(s => s.StoppedAt is null)];

    /// <summary>The set the break screen logs: the last one, once stopped.</summary>
    public WorkoutSet? ToLog => Sets.Count > 0 && Sets[^1].StoppedAt is not null ? Sets[^1] : null;

    /// <summary>The last set and any started with it (a superset's round), once stopped. Empty before the first.</summary>
    public List<WorkoutSet> LastRound => ToLog is { } last ? Sets[(Sets.FindLastIndex(s => s.StartedAt != last.StartedAt) + 1)..] : [];

    /// <summary>The last exercise the rest screen may offer to extend. Explicitly finishing it removes that suggestion.</summary>
    public List<TemplateExercise>? RepeatableLastBlock => LastRound is [var last, ..] && BlockOf(last.Line) is { } block
        && !block.Exists(e => e.FinishedByUser) ? block : null;

    /// <summary>Per exercise, how many sets I did the last time (the trainer's count, when its plan is taken for this time).</summary>
    public IReadOnlyDictionary<long, int> SetsLastTime => setsLastTime;

    /// <summary>What the big button offers after the sets stopped so far: the first block, in the plan's order, with sets still to do.</summary>
    public PlanStep? Next { get; private set; }

    /// <summary>Where <see cref="Next"/> is in the plan: its block's index in <see cref="Blocks"/>; null when there's none.</summary>
    public int? NextBlock { get; private set; }

    /// <summary>The exercise being performed, including the rest between its sets. An upcoming exercise is separate.</summary>
    public List<TemplateExercise>? ActiveBlock { get; private set; }

    /// <summary>The next step available when finishing the current exercise early.</summary>
    public PlanStep? MoveOn { get; private set; }

    /// <summary>True between sets of the current exercise, when the screen can offer to move on.</summary>
    public bool CanMoveOn { get; private set; }

    /// <summary>The plan in order, one block per exercise or superset.</summary>
    public List<List<TemplateExercise>> Blocks() => WorkoutPlan.Blocks(Plan);

    /// <summary>The block a line is in; null when the plan no longer has the line.</summary>
    public List<TemplateExercise>? BlockOf(int line) => line == 0 ? null : Blocks().Find(b => b.Any(e => e.Line == line));

    /// <summary>How far a block is: its sets done, the count it's done after, and whether a set of it is running.</summary>
    public BlockProgress Progress(IReadOnlyList<TemplateExercise> block) => WorkoutPlan.Progress(block, Sets, setsLastTime, OpenEnded);

    /// <summary>The step a block is on: its exercises, on the set (or round) after those done.</summary>
    public PlanStep StepOf(IReadOnlyList<TemplateExercise> block) => WorkoutPlan.Step(block, Sets, setsLastTime);

    /// <summary>A line's sets, in order: done, running and warm-ups.</summary>
    public List<WorkoutSet> SetsOf(int line) => [.. Sets.Where(s => s.Line == line)];

    /// <summary>The blocks with sets still to do, in the order they'll be offered; the first may have a set running.</summary>
    public List<List<TemplateExercise>> StillToDo() => [.. Blocks().Where(b => !Progress(b).Complete)];

    /// <summary>A number for a new line.</summary>
    public int NewLine() => PlanEdits.NextLine(Plan, Sets);

    /// <summary>The exercises' names, joined: "Pull-ups + Dips".</summary>
    public string Names(IEnumerable<long> exerciseIds) => string.Join(" + ", exerciseIds.Select(id => Exercises[id].Name));

    /// <summary>A block's exercises' names, joined.</summary>
    public string Names(IEnumerable<TemplateExercise> block) => Names(block.Select(e => e.ExerciseId));

    /// <summary>Recomputes what comes next. Called after every change.</summary>
    public void Refresh()
    {
        var exerciseIds = Plan.Select(e => e.ExerciseId).Concat(Sets.Select(s => s.ExerciseId)).Distinct();
        setsLastTime = exerciseIds.ToDictionary(id => id, id => TrainerFor(id)?.Sets ?? History(id).FirstOrDefault()?.Sets.Count ?? 0);

        var blocks = Blocks();
        NextBlock = WorkoutPlan.NextBlock(Plan, Sets, setsLastTime, OpenEnded);
        Next = NextBlock is int at ? StepOf(blocks[at]) : null;
        ActiveBlock = WorkoutPlan.ActiveBlock(blocks, Sets, NextBlock);
        CanMoveOn = ToLog is not null && ActiveBlock is not null && !Progress(ActiveBlock).Running;
        MoveOn = CanMoveOn ? WorkoutPlan.After(Plan, Sets, setsLastTime, NextBlock!.Value, OpenEnded) : null;
    }

    /// <summary>The trainer's plan for this time of an exercise: applied from the look back at the last time I did it; null otherwise.</summary>
    public TrainerTarget? TrainerFor(long exerciseId) =>
        TrainerTargets.TryGetValue(exerciseId, out var target) && History(exerciseId).FirstOrDefault()?.Session.Id == target.SessionId ? target : null;

    /// <summary>Whether an exercise had gone flat before today: no new best in its last 4 workouts or more (see <see cref="Plateaus"/>).</summary>
    public Plateau? PlateauOf(long exerciseId)
    {
        if (!plateauCache.TryGetValue(exerciseId, out var plateau))
            plateauCache[exerciseId] = plateau = Plateaus.Of(Exercises[exerciseId], ExerciseHistory.Recent(exerciseId, PastSessions, PastSets, Session.Id, int.MaxValue), BodyPounds);
        return plateau;
    }

    /// <summary>The last 3 sessions that had this exercise, newest first.</summary>
    public IReadOnlyList<PastSession> History(long exerciseId)
    {
        if (!historyCache.TryGetValue(exerciseId, out var history)) historyCache[exerciseId] = history = ExerciseHistory.Recent(exerciseId, PastSessions, PastSets, Session.Id, 3);
        return history;
    }

    /// <summary>How an exercise went the last time, in short ("3 × 10 at 95"); null when it wasn't done before.</summary>
    public string? LastTime(long exerciseId) =>
        History(exerciseId).FirstOrDefault() is { } last && Exercises.TryGetValue(exerciseId, out var exercise) ? SetSummary.Format(last.Sets, exercise.Measure) : null;

    /// <summary>Where a set sits in the plan ("set 2 of 3", "round 1 of 3"), counting only the sets before its round.</summary>
    public PlanStep Describe(WorkoutSet set) => WorkoutPlan.StepOf(Plan, Sets, set, setsLastTime);

    /// <summary>The pre-filled values a set started with, to tell whether its weight went up for a hit goal.</summary>
    public SetValues? SuggestionFor(WorkoutSet set)
    {
        var before = Sets.TakeWhile(s => s.Order < set.Order).Where(s => s.ExerciseId == set.ExerciseId).ToList();
        return SetSuggestion.For(before, History(set.ExerciseId).FirstOrDefault()?.Sets ?? [], HitGoalStep, TrainerFor(set.ExerciseId));
    }

    /// <summary>
    /// What the next set of this exercise starts with, as Start fills it in (see <see cref="SetSuggestion"/>), so the weights can be
    /// ready during the rest: the trainer's plan for its first set when one is applied. Empty for a time exercise, or with nothing to go on.
    /// </summary>
    public SetValues? NextValues(long exerciseId)
    {
        if (Exercises[exerciseId].Measure != ExerciseMeasure.Weight) return null;

        var lastTime = History(exerciseId).FirstOrDefault()?.Sets ?? [];
        return SetSuggestion.For([.. Sets.Where(s => s.ExerciseId == exerciseId && s.StoppedAt is not null)], lastTime, HitGoalStep, TrainerFor(exerciseId));
    }

    /// <summary>"Set 2 of 3", "Set 4", or "Superset · round 1 of 3".</summary>
    public static string StepLabel(PlanStep step)
    {
        var of = step.TargetSets is int target ? $" of {target}" : "";
        return step.IsRound ? $"Superset · round {step.SetNumber}{of}" : $"Set {step.SetNumber}{of}";
    }
}
