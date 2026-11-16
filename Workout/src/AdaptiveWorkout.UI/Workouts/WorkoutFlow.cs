using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using AdaptiveWorkout.Core;
using AdaptiveWorkout.UI.Storage;
using AdaptiveWorkout.UI.Watch;

namespace AdaptiveWorkout.UI.Workouts;

/// <summary>
/// Starting, stopping and finishing, and every change to the open workout. Every change updates the in-memory workout first, so
/// the screen moves on at once, then saves in the background; the store keeps those saves in order. The watch app opens with a new
/// workout and closes when it ends.
/// </summary>
public sealed class WorkoutFlow(WorkoutStore store, AppSettings settings, WatchLink watch, WorkoutNotice notice)
{
    private const string RestCountdownKey = "RestCountdown";

    // The changes made on the edit screen since the workout was paused, newest last, each with the workout as it was before it.
    private readonly List<Change> changes = [];
    private Task? loading;
    private Task? measuring;
    private List<WorkoutSession> pastSessions = [];
    private List<WorkoutSet> pastSets = [];
    private Dictionary<string, string> preferences = [];
    private List<TemplateExercise>? savedAsSession;

    /// <summary>The open workout, or null when there is none (or nothing has loaded yet).</summary>
    public WorkoutState? Current { get; private set; }

    /// <summary>Raised after every change to the open workout, e.g. one made from the watch, so a screen showing it can update.</summary>
    public event Action? Changed;

    /// <summary>What the open workout is doing now, for the watch and the notification; null when none is open.</summary>
    public WorkoutStatus? Status
    {
        get
        {
            if (Current is not { } state) return null;
            if (state.Session.PausedAt is { } pausedSince) return new WorkoutStatus(true, "Paused", pausedSince, null, null);
            if (state.Session.CoolDownStartedAt is { } coolingSince) return new WorkoutStatus(true, "Cool-down", coolingSince, null, null);
            // A set's clock leaves out any time it was held while the workout was paused, here as on the workout's screen.
            if (state.Running is [var first, ..] running)
                return new WorkoutStatus(false, state.Names(running.Select(s => s.ExerciseId)), first.StartedAt!.Value.AddSeconds(first.PausedSeconds), null, null);

            // The warm-up waits for the first exercise like a rest, counting up, so the watch can start it too. After the last
            // exercise, a planned cool-down is what's next.
            var next = state.Next is { } step ? state.Names(step.ExerciseIds) : state is { WarmingUp: false, CoolDownNext: true } ? "cool-down" : null;
            if (state.WarmingUp) return new WorkoutStatus(true, "Warm-up", state.Session.StartedAt, null, next);
            if (state.ToLog is not { } logged) return new WorkoutStatus(true, state.Name, state.Session.StartedAt, null, next);

            DateTimeOffset? endsAt = RestCountdown ? logged.StoppedAt!.Value + RestTarget(logged.ExerciseId) : null;
            return new WorkoutStatus(true, "Rest", logged.StoppedAt!.Value, endsAt, next);
        }
    }

    /// <summary>Loads history and any open workout once; later calls return what's in memory.</summary>
    public async Task<WorkoutState?> LoadAsync()
    {
        await (loading ??= LoadHistoryAsync());
        return Current;
    }

    /// <summary>Every finished workout and their sets, oldest first, from memory once history has loaded: for looking back, as Stats does.</summary>
    public async Task<(List<WorkoutSession> Sessions, List<WorkoutSet> Sets)> LoadFinishedAsync()
    {
        await LoadAsync();
        var finished = pastSessions.Where(s => s.FinishedAt is not null).OrderBy(s => s.StartedAt).ToList();
        var ids = finished.Select(s => s.Id).ToHashSet();
        return (finished, [.. pastSets.Where(s => ids.Contains(s.SessionId))]);
    }

    /// <summary>
    /// Starts a session now, or resumes the open one. A session with a warm-up starts with it, until the big button starts the first
    /// exercise; otherwise a planned first exercise's first set starts right away.
    /// </summary>
    public async Task StartSessionAsync(long? templateId)
    {
        if (await LoadAsync() is not null) return;

        var template = templateId is long id ? await store.GetTemplateAsync(id) : null;
        var session = new WorkoutSession
        {
            TemplateId = templateId,
            StartedAt = DateTimeOffset.Now,
            WarmUp = template?.WarmUp == true,
            CoolDown = template?.CoolDown == true,
        };
        Save(store.StartSessionAsync(session));
        var plan = template is not null ? PlanEdits.Copy(await store.GetTemplateExercisesAsync(template.Id)) : [];
        Save(store.SaveSessionPlanAsync(session, plan));
        Current = await CreateStateAsync(session, [], plan);
        watch.Open(session);
        if (!session.WarmUp && Current.Next is { } first) StartSets(first);
        else Announce();
    }

    /// <summary>
    /// Starts a step: a set of an exercise, or a superset's round, whose sets start and stop together and so share their time and
    /// the heart rate during it. Each set belongs to its line and starts from suggested values. Saves the entries of the rest it ends.
    /// </summary>
    public void StartSets(PlanStep step)
    {
        if (Current is not { } state || step.ExerciseIds.Count == 0) return;

        // A set I start myself, from a screen that paused the workout, ends the pause.
        Resume();

        // The first exercise ends the warm-up as it starts. A set started during the cool-down puts the cool-down off until the end again.
        var now = DateTimeOffset.Now;
        if (state.WarmingUp || state.CoolingDown)
        {
            if (state.WarmingUp) state.Session.WarmUpEndedAt = now;
            state.Session.CoolDownStartedAt = null;
            Save(store.SaveWarmUpAndCoolDownAsync(state.Session));
        }

        // Starting closes the rest's entries, as they stand in their fields.
        foreach (var entry in state.ToConfirm) Save(store.UpdateSetAsync(entry));
        state.LoggedThrough = state.LastOrder;

        for (var i = 0; i < step.ExerciseIds.Count; i++)
        {
            var values = state.NextValues(step.ExerciseIds[i]);
            var set = new WorkoutSet
            {
                ExerciseId = step.ExerciseIds[i],
                Line = step.Lines[i],
                Order = state.LastOrder + 1,
                StartedAt = now,
                Weight = values?.Weight,
                WeightUnit = values?.Unit ?? settings.Unit,
                Reps = values?.Reps,
            };
            state.Sets.Add(set);
            Save(store.InsertSetAsync(state.Session, set));
        }

        state.Refresh();
        Announce();
    }

    /// <summary>Starts whatever comes next, e.g. from the watch: the next set or round, else a planned cool-down. Does nothing while a set runs.</summary>
    public void StartNext()
    {
        if (Paused) return;

        if (Current is { Running: [], Next: { } next }) StartSets(next);
        else StartCoolDown();
    }

    /// <summary>
    /// The rest countdown ran out: starts the next set or round, from the rest screen's countdown or the watch's. A cool-down waits
    /// for a tap. The watch says when its countdown ended (<paramref name="endsAt"/>); when that's no longer this rest's (another
    /// set started meanwhile, or the countdown changed), nothing starts. Nothing starts while the workout is paused.
    /// </summary>
    public void RestIsUp(DateTimeOffset? endsAt = null)
    {
        if (Paused) return;

        if (endsAt is { } heard && (Status?.RestEndsAt is not { } end || (end - heard).Duration() > TimeSpan.FromSeconds(2))) return;

        if (Current is { Running: [], Next: { } next }) StartSets(next);
    }

    /// <summary>True while the workout is paused: a screen that changes it is open.</summary>
    public bool Paused => Current?.Session.PausedAt is not null;

    /// <summary>
    /// Pauses the workout, as a screen that changes it opens: until <see cref="Resume"/> nothing starts by itself, on the phone or
    /// from the watch, whose countdown is called off, and a running set's clock holds. Rest is the time since the last set whatever
    /// the app does, so it goes on counting.
    /// </summary>
    public void Pause()
    {
        if (Current is not { Session.PausedAt: null } state) return;

        changes.Clear();
        state.Session.PausedAt = DateTimeOffset.Now;
        Save(store.SavePauseAsync(state.Session));
        Announce();
    }

    /// <summary>
    /// Ends the pause. A set that was running carries on from where its clock held: the time it was held is kept with it, and
    /// isn't part of how long it ran. A rest countdown that ran out meanwhile starts nothing; the rest screen shows how far over it is.
    /// What was changed meanwhile stays changed: there's no going back on it from here.
    /// </summary>
    public void Resume()
    {
        if (Current is not { Session.PausedAt: { } pausedAt } state) return;

        var now = DateTimeOffset.Now;
        foreach (var set in state.Running)
        {
            var heldFrom = set.StartedAt is { } started && started > pausedAt ? started : pausedAt;
            set.PausedSeconds += Math.Max(0, (int)Math.Round((now - heldFrom).TotalSeconds));
            Save(store.UpdateSetAsync(set));
        }

        changes.Clear();
        state.Session.PausedAt = null;
        Save(store.SavePauseAsync(state.Session));
        Announce();
    }

    /// <summary>
    /// Another set of what I just did (another round of a superset), e.g. past its planned sets: a line that was done gets one
    /// more than it has.
    /// </summary>
    public void StartAnother()
    {
        if (Current is not { Running: [], LastRound: [var last, ..] } state || state.BlockOf(last.Line) is not { } block) return;

        if (state.Progress(block) is { Complete: true, Done: var done }) SavePlan(state, PlanEdits.Count(state.Plan, last.Line, done + 1));
        StartSets(state.StepOf(state.BlockOf(last.Line)!));
    }

    /// <summary>
    /// Moves on from an exercise that has no count (nothing planned, nothing to go on from last time): it's done with the sets
    /// it has, and what's after it starts.
    /// </summary>
    public void MoveOn()
    {
        if (Current is not { Running: [], CanMoveOn: true, NextBlock: int at } state) return;

        var block = state.Blocks()[at];
        SavePlan(state, PlanEdits.Count(state.Plan, block[0].Line, state.Progress(block).Done));
        if (state.Next is { } next) StartSets(next);
        else Announce();
    }

    /// <summary>
    /// Starts an exercise picked from the list now. A line of it still to do is the one done; with none, it gets a line. Either way
    /// it goes ahead of everything else still to do, so the plan carries on after it; an exercise I was on that has no count is done
    /// with the sets it has.
    /// </summary>
    public void StartPicked(Exercise exercise)
    {
        if (Current is not { Running: [] } state) return;

        state.Exercises.TryAdd(exercise.Id, exercise);
        var line = state.StillToDo().SelectMany(b => b).FirstOrDefault(e => e.ExerciseId == exercise.Id)?.Line ?? 0;
        var plan = state.Plan;
        if (line == 0) plan = PlanEdits.Add(plan, exercise.Id, line = state.NewLine());
        StartLine(state, plan, line);
    }

    /// <summary>Starts a line still to do now, ahead of everything else: with its superset, when it's in one.</summary>
    public void StartLine(int line)
    {
        if (Current is { Running: [] } state && state.BlockOf(line) is not null) StartLine(state, state.Plan, line);
    }

    /// <summary>Stops the running set, or a superset's round, all at once; for a time exercise the stopwatch becomes its duration.</summary>
    public void StopSet()
    {
        if (Paused || Current is not { Running: [_, ..] running } state) return;

        var now = DateTimeOffset.Now;
        foreach (var set in running)
        {
            set.StoppedAt = now;
            if (state.Exercises[set.ExerciseId].Measure == ExerciseMeasure.Time) set.DurationSeconds = (int)Math.Round(set.Ran()!.Value.TotalSeconds);
            Save(store.UpdateSetAsync(set));
        }

        state.Refresh();
        Announce();
    }

    /// <summary>
    /// Saves edits to a set of the open workout (weight, reps, hit goal, reps left, warm-up). What's next follows, since a warm-up
    /// isn't one of an exercise's sets and the next set starts from the last working one; a warm-up has no goal to hit.
    /// </summary>
    public void SaveSet(WorkoutSet set)
    {
        if (set.WarmUp) (set.HitGoal, set.RepsLeft) = (false, null);
        Save(store.UpdateSetAsync(set));
        if (Current is not { } state) return;

        state.Refresh();
        Announce();
    }

    /// <summary>Saves a change to a set of a finished workout, then works out again how it and every later one compared.</summary>
    public async Task SavePastSetAsync(WorkoutSet set)
    {
        await LoadAsync();
        var at = pastSets.FindIndex(s => s.Id == set.Id);
        if (at >= 0) pastSets[at] = set;
        if (set.WarmUp) (set.HitGoal, set.RepsLeft) = (false, null);
        Save(store.UpdateSetAsync(set));
        await RescoreFromAsync(set.SessionId);
    }

    /// <summary>Deletes a set of a finished workout, then works out again how it and every later one compared.</summary>
    public async Task DeletePastSetAsync(WorkoutSet set)
    {
        await LoadAsync();
        pastSets.RemoveAll(s => s.Id == set.Id);
        Save(store.DeleteSetAsync(set));
        await RescoreFromAsync(set.SessionId);

        // Its last set gone, the workout may have ended sooner (see Calories.WorkoutTime).
        if (pastSessions.Find(s => s.Id == set.SessionId) is { } session) await MeasureAsync(session, pastSets.Where(s => s.SessionId == session.Id));
    }

    /// <summary>
    /// Works out the average heart rate of each finished workout that doesn't have one yet (see WorkoutSession.AverageBpm), from
    /// before it was kept: once a run, after Today is up. Finish works out its own workout's.
    /// </summary>
    public Task MeasureHeartRatesAsync() => measuring ??= MeasureUnmeasuredAsync();

    /// <summary>
    /// Readings the watch held while out of the phone's reach can come in after their workout finished: works out the average heart
    /// rate again of each finished workout they fall in.
    /// </summary>
    public async Task TakeLateReadingsAsync(IReadOnlyList<HeartRateSample> readings)
    {
        if (readings.Count == 0) return;

        await LoadAsync();
        var (first, last) = (readings.Min(r => r.At), readings.Max(r => r.At));
        var during = pastSessions.Where(s => s.StartedAt <= last && s.FinishedAt >= first).ToList();
        foreach (var session in during) await MeasureAsync(session, pastSets.Where(s => s.SessionId == session.Id));
    }

    /// <summary>After the last exercise: saves the set being logged and starts the cool-down, which runs until Finish.</summary>
    public void StartCoolDown()
    {
        if (Paused || Current is not { Running: [], WarmingUp: false, CoolDownNext: true } state) return;

        foreach (var entry in state.ToConfirm) Save(store.UpdateSetAsync(entry));
        state.LoggedThrough = state.LastOrder;
        state.Session.CoolDownStartedAt = DateTimeOffset.Now;
        Save(store.SaveWarmUpAndCoolDownAsync(state.Session));
        Announce();
    }

    /// <summary>Stops a running set, saves the one being logged, and closes the session; a cool-down ends with it. Returns the workout finished.</summary>
    public WorkoutSession? Finish()
    {
        if (Current is not { } state) return null;

        Resume();
        StopSet();
        foreach (var entry in state.ToConfirm) Save(store.UpdateSetAsync(entry));
        state.Session.FinishedAt = DateTimeOffset.Now;

        // Finished during the warm-up: it was all warm-up.
        if (state.WarmingUp)
        {
            state.Session.WarmUpEndedAt = state.Session.FinishedAt;
            Save(store.SaveWarmUpAndCoolDownAsync(state.Session));
        }

        Save(store.FinishSessionAsync(state.Session));
        Save(MeasureAsync(state.Session, state.Sets));
        pastSessions.Add(state.Session);
        pastSets.AddRange(state.Sets);
        Save(ScoreSessionsAsync());
        Current = null;
        watch.Close();
        Announce();
        return state.Session;
    }

    /// <summary>Throws away the open session and its sets, e.g. one started by mistake.</summary>
    public void Cancel()
    {
        if (Current is not { } state) return;

        Save(store.DeleteSessionAsync(state.Session));
        Current = null;
        watch.Close();
        Announce();
    }

    /// <summary>
    /// Works out how each finished workout compared with the last time of its exercises (see <see cref="Progress"/>) and saves it
    /// with the workout, once: at Finish, and for workouts finished before improvements existed.
    /// </summary>
    public async Task ScoreSessionsAsync()
    {
        await LoadAsync();
        var unscored = pastSessions.Where(s => s is { FinishedAt: not null, ExercisesCompared: null }).OrderBy(s => s.StartedAt).ToList();
        if (unscored.Count == 0) return;

        var exercises = (await store.GetExercisesAsync()).ToDictionary(e => e.Id);
        var bodyWeights = await store.GetBodyWeightsAsync();
        foreach (var session in unscored)
        {
            (session.Improvement, session.ExercisesCompared) = Progress.Improvement(session, pastSessions, pastSets, exercises, bodyWeights);
            Save(store.SaveImprovementAsync(session));
        }
    }

    /// <summary>Deletes a finished session and its sets, so history and suggestions forget it.</summary>
    public async Task DeleteSessionAsync(WorkoutSession session)
    {
        await LoadAsync();
        pastSessions.RemoveAll(s => s.Id == session.Id);
        pastSets.RemoveAll(s => s.SessionId == session.Id);
        Save(store.DeleteSessionAsync(session));
    }

    // Changing today's workout, from its edit screen, which pauses it. Each change is made to the workout in memory, saved, and
    // kept so it can be undone until the workout resumes. A line is named by its number, never by its exercise.

    /// <summary>What the last change on the edit screen was, in words ("Removed Dips"), for its Undo; null when there's none to undo.</summary>
    public string? LastChange => changes.Count > 0 ? changes[^1].What : null;

    /// <summary>True once today's lines were saved as the session too, until they change again.</summary>
    public bool SavedAsSession => savedAsSession is not null && ReferenceEquals(savedAsSession, Current?.Plan);

    /// <summary>Takes back the last change made on the edit screen: the lines and the sets are as they were before it.</summary>
    public void Undo()
    {
        if (Current is not { } state || changes.Count == 0) return;

        var change = changes[^1];
        changes.RemoveAt(changes.Count - 1);
        var now = Snapshot(state, "");
        state.Plan = change.Plan;
        state.Sets.Clear();
        foreach (var (set, was) in change.Sets)
        {
            Copy(was, set);
            state.Sets.Add(set);
        }

        state.LoggedThrough = change.LoggedThrough;
        Persist(state, now);
        if (change.Revert is { } revert) Save(revert());
        state.Refresh();
        Announce();
    }

    /// <summary>Moves a line's block a step up or down among the ones still to do; one with a set running stays where it is.</summary>
    public void MoveLine(int line, int direction) => Edit(line, names => $"Moved {names} {(direction < 0 ? "up" : "down")}", (state, _) =>
    {
        var free = state.Blocks().Where(b => state.Progress(b) is { Complete: false, Running: false }).ToList();
        var at = free.FindIndex(b => b.Any(e => e.Line == line));
        if (at >= 0 && at + direction >= 0 && at + direction < free.Count) state.Plan = PlanEdits.Exchange(state.Plan, line, free[at + direction][0].Line);
    });

    /// <summary>
    /// Does a line's block next: ahead of everything else still to do, right after a set that's running. An exercise I was on that
    /// has no count is done with the sets it has.
    /// </summary>
    public void DoNext(int line) => Edit(line, names => $"{names} is next", (state, _) => state.Plan = ToFront(state, state.Plan, line));

    /// <summary>One more set (or round) for a line's block: one more than planned, or than it has when that's more.</summary>
    public void MoreSets(int line) => Edit(line, names => $"More sets of {names}", (state, block) =>
    {
        var auto = block.Max(e => state.SetsLastTime.GetValueOrDefault(e.ExerciseId));
        state.Plan = PlanEdits.Count(state.Plan, line, (block[0].PlannedSets ?? Math.Max(state.Progress(block).Done, auto)) + 1);
    });

    /// <summary>One set (or round) fewer for a line's block, never fewer than it has done; under one, as many as last time.</summary>
    public void FewerSets(int line) => Edit(line, names => $"Fewer sets of {names}", (state, block) =>
    {
        if (block[0].PlannedSets is not int count) return;

        var done = state.Progress(block).Done;
        state.Plan = PlanEdits.Count(state.Plan, line, count == 1 && done == 0 ? null : Math.Max(Math.Max(done, 1), count - 1));
    });

    /// <summary>
    /// Puts another exercise on a line, today only. With sets done on it: either they were the new exercise as well
    /// (<paramref name="doneToo"/>), or they stay what they were, on a line of their own just before, and the new exercise takes
    /// the sets (in a superset, the rounds) left. A running set carries on as the new exercise, from its numbers.
    /// </summary>
    public void SwapLine(int line, Exercise exercise, bool doneToo) => Edit(line, _ => $"Swapped {NameOn(line)} for {exercise.Name}", (state, block) =>
    {
        state.Exercises.TryAdd(exercise.Id, exercise);
        var done = state.SetsOf(line).Where(s => s.StoppedAt is not null).ToList();
        if (done.Count == 0 || doneToo)
        {
            state.Plan = PlanEdits.Swap(state.Plan, line, exercise.Id);
            foreach (var set in state.SetsOf(line))
            {
                set.ExerciseId = exercise.Id;
                if (done.Count == 0) Prefill(state, set);
            }

            return;
        }

        var working = done.Working().Count();
        var doneLine = state.NewLine();
        int? left = state.Progress(block).Target is int target ? Math.Max(1, target - working) : null;
        state.Plan = PlanEdits.SwapRest(state.Plan, line, exercise.Id, doneLine, working, left);
        foreach (var set in done) set.Line = doneLine;
        foreach (var set in state.SetsOf(line))
        {
            set.ExerciseId = exercise.Id;
            Prefill(state, set);
        }
    });

    /// <summary>
    /// Does another line's block together with a line's, as a superset: from the next round, and in a round that's running, on the
    /// same clock. <see cref="FillRounds"/> writes in what it did in rounds already done.
    /// </summary>
    public void SupersetWith(int line, int withLine)
    {
        if (Current?.BlockOf(withLine) is not { } other) return;

        var joining = other.Select(e => e.Line).ToList();
        var with = Current.Names(other);
        Edit(line, names => $"Paired {names} with {with}", (state, _) =>
        {
            state.Plan = PlanEdits.Join(state.Plan, line, withLine);
            JoinRound(state, line, joining);
        });
    }

    /// <summary>Does an exercise today's workout doesn't have together with a line's block, as a superset, on a line of its own. Returns its line.</summary>
    public int SupersetWithNew(int line, Exercise exercise)
    {
        var newLine = 0;
        Edit(line, names => $"Paired {names} with {exercise.Name}", (state, _) =>
        {
            state.Exercises.TryAdd(exercise.Id, exercise);
            newLine = state.NewLine();
            state.Plan = PlanEdits.JoinNew(state.Plan, line, exercise.Id, newLine);
            JoinRound(state, line, [newLine]);
        });
        return newLine;
    }

    /// <summary>
    /// A superset said afterwards: writes in a set for each of these lines in every round their superset has done without them,
    /// at the round's own times, from the numbers they'd have started with, to be put right.
    /// </summary>
    public void FillRounds(IReadOnlyList<int> lines)
    {
        if (lines.Count == 0 || Current is not { } current) return;

        var who = string.Join(" and ", current.Plan.Where(e => lines.Contains(e.Line)).Select(e => current.Exercises[e.ExerciseId].Name));
        Edit(lines[0], _ => $"Wrote in what {who} did", (state, block) =>
        {
            var rounds = state.Progress(block).Done;
            var lead = block.Select(e => state.SetsOf(e.Line).Where(s => s is { StoppedAt: not null, WarmUp: false }).ToList()).First(sets => sets.Count == rounds);
            foreach (var entry in block.Where(e => lines.Contains(e.Line)))
            {
                for (var round = state.SetsOf(entry.Line).Count(s => s is { StoppedAt: not null, WarmUp: false }); round < rounds; round++)
                {
                    var with = lead[round];
                    var set = new WorkoutSet
                    {
                        ExerciseId = entry.ExerciseId, Line = entry.Line, StartedAt = with.StartedAt, StoppedAt = with.StoppedAt,
                        PausedSeconds = with.PausedSeconds, WeightUnit = settings.Unit,
                    };
                    Insert(state, state.Sets.FindLast(s => s.StartedAt == with.StartedAt), set);
                    Prefill(state, set);
                }
            }
        });
    }

    /// <summary>Takes a line's superset apart, today only: each exercise alone, in their order, for the rounds it had. A running round goes on as a set of the first.</summary>
    public void SplitSuperset(int line) => Edit(line, names => $"Split {names}", (state, block) =>
    {
        state.Plan = PlanEdits.Split(state.Plan, line);
        var others = block.Skip(1).Select(e => e.Line).ToList();
        state.Sets.RemoveAll(s => s.StoppedAt is null && others.Contains(s.Line));
    });

    /// <summary>Takes a line out of its superset, today only: it comes right after, alone. Its part of a running round is dropped.</summary>
    public void TakeOut(int line) => Edit(line, _ => $"Took {NameOn(line)} out of the superset", (state, _) =>
    {
        state.Plan = PlanEdits.TakeOut(state.Plan, line);
        state.Sets.RemoveAll(s => s.StoppedAt is null && s.Line == line);
    });

    /// <summary>Leaves a line out of today's workout, or its whole superset, with every set on it.</summary>
    public void RemoveLine(int line, bool wholeBlock) => Edit(line, names => $"Removed {(wholeBlock ? names : NameOn(line))}", (state, block) =>
    {
        var gone = wholeBlock ? [.. block.Select(e => e.Line)] : new List<int> { line };
        state.Plan = wholeBlock ? PlanEdits.RemoveBlock(state.Plan, line) : PlanEdits.Remove(state.Plan, line);
        state.Sets.RemoveAll(s => gone.Contains(s.Line));
    });

    /// <summary>
    /// The sets a line had left go to another line instead, e.g. one that already has the exercise it was being swapped for: that
    /// one gets them on top of its own, and this one is done with the sets it has, or leaves when it has none.
    /// </summary>
    public void GiveSetsTo(int line, int toLine)
    {
        if (Current?.BlockOf(toLine) is not { } to) return;

        Edit(line, _ => $"{NameOn(line)} gave its sets to {NameOn(toLine)}", (state, block) =>
        {
            var progress = state.Progress(block);
            var left = progress.Target is int target ? Math.Max(1, target - progress.Done) : 1;

            // A line with no count already takes as many sets as I do.
            if (state.Progress(to) is { Target: int toTarget, Done: var toDone }) state.Plan = PlanEdits.Count(state.Plan, toLine, Math.Max(toTarget, toDone) + left);
            state.Plan = progress.Started ? PlanEdits.Count(state.Plan, line, progress.Done) : PlanEdits.RemoveBlock(state.Plan, line);
        });
    }

    /// <summary>Adds an exercise after everything else in today's workout. Returns its line.</summary>
    public int AddLine(Exercise exercise)
    {
        if (Current is not { } state) return 0;

        var line = state.NewLine();
        Edit($"Added {exercise.Name}", s =>
        {
            s.Exercises.TryAdd(exercise.Id, exercise);
            s.Plan = PlanEdits.Add(s.Plan, exercise.Id, line);
        });
        return line;
    }

    /// <summary>
    /// Adds a whole session's exercises after everything else in today's workout, or in place of what's left: then the lines not
    /// started go, and one part way is done with the sets it has. The workout keeps its name.
    /// </summary>
    public async Task AddSessionAsync(long templateId, bool inPlaceOfWhatsLeft)
    {
        if (Current is not { } state) return;

        var name = (await store.GetTemplateAsync(templateId))?.Name ?? "Session";
        var entries = await store.GetTemplateExercisesAsync(templateId);
        foreach (var exercise in await store.GetExercisesAsync()) state.Exercises.TryAdd(exercise.Id, exercise);
        Edit(inPlaceOfWhatsLeft ? $"{name} took the place of what was left" : $"Added {name}", s =>
        {
            if (inPlaceOfWhatsLeft)
            {
                foreach (var block in s.Blocks())
                {
                    if (s.Progress(block) is { Started: true, Complete: false, Running: false, Done: var done }) s.Plan = PlanEdits.Count(s.Plan, block[0].Line, done);
                }

                s.Plan = PlanEdits.Keep(s.Plan, block => s.Progress(block).Started);
            }

            s.Plan = PlanEdits.Append(s.Plan, entries, s.NewLine());
        });
    }

    /// <summary>
    /// A set was really another exercise: it goes to another line of today's with that exercise, or with none, to a line of its
    /// own right after the one it was on, done with it. The line it leaves, when it was done, stays done with the sets it has
    /// left, and goes when it has none.
    /// </summary>
    public void MoveSet(WorkoutSet set, Exercise exercise) => Edit($"Moved a set to {exercise.Name}", state =>
    {
        state.Exercises.TryAdd(exercise.Id, exercise);
        var (leaving, wasDone) = (set.Line, WasDone(state, set.Line));
        var to = state.Plan.FirstOrDefault(e => e.ExerciseId == exercise.Id && e.Line != set.Line)?.Line ?? 0;
        if (to == 0)
        {
            var blocks = state.Blocks();
            var from = blocks.FindIndex(b => b.Any(e => e.Line == set.Line));
            var after = from >= 0 && from + 1 < blocks.Count ? blocks[from + 1][0].Line : 0;
            state.Plan = PlanEdits.Add(state.Plan, exercise.Id, to = state.NewLine(), after, set.WarmUp ? 0 : 1);
        }

        (set.ExerciseId, set.Line) = (exercise.Id, to);
        CountAtLeastDone(state, to);
        if (wasDone) state.Plan = PlanEdits.StayDone(state.Plan, leaving, state.Sets, state.SetsLastTime, state.OpenEnded);
    });

    /// <summary>
    /// Writes in a set done off the clock, on a line: after the line's last set (with none, after the workout's last), from the
    /// numbers it would have started with. It has no time of its own. Returns it, to be put right.
    /// </summary>
    public WorkoutSet? LogSet(int line)
    {
        WorkoutSet? logged = null;
        Edit(line, _ => $"Logged a set of {NameOn(line)}", (state, block) =>
        {
            var after = state.SetsOf(line).FindLast(s => s.StoppedAt is not null) ?? state.Sets.FindLast(s => s.StoppedAt is not null);
            var at = after?.StoppedAt ?? state.Session.StartedAt;
            logged = new WorkoutSet { ExerciseId = block.First(e => e.Line == line).ExerciseId, Line = line, StartedAt = at, StoppedAt = at, WeightUnit = settings.Unit };
            Insert(state, after, logged);
            Prefill(state, logged);
            CountAtLeastDone(state, line);
        });
        return logged;
    }

    /// <summary>
    /// Deletes a finished set of the open workout, e.g. one done by mistake. A line that was done stays done with the sets it has
    /// left, and goes when it has none; one still to do keeps its count, so what's next follows from the sets left.
    /// </summary>
    public void DeleteSet(WorkoutSet set)
    {
        if (Current is not { } current || set.StoppedAt is null || !current.Sets.Contains(set)) return;

        Edit($"Deleted a set of {current.Exercises[set.ExerciseId].Name}", state =>
        {
            var wasDone = WasDone(state, set.Line);
            state.Sets.Remove(set);
            if (wasDone) state.Plan = PlanEdits.StayDone(state.Plan, set.Line, state.Sets, state.SetsLastTime, state.OpenEnded);
        });
    }

    /// <summary>Throws away the set (or round) that's running, e.g. one a countdown started that I'm not doing.</summary>
    public void DiscardRunning()
    {
        if (Current is not { Running: [_, ..] running } current) return;

        Edit($"Discarded the running {(running.Count > 1 ? "round" : "set")} of {current.Names(running.Select(s => s.ExerciseId))}", state => state.Sets.RemoveAll(s => s.StoppedAt is null));
    }

    /// <summary>
    /// Makes today's lines the session too, from the next time on: its exercises, their order, sets and supersets. Workouts
    /// already done stay as they are. Undone, the session is as it was.
    /// </summary>
    public async Task SaveAsSessionAsync()
    {
        if (Current is not { Template: { } template } state) return;

        var before = await store.GetTemplateExercisesAsync(template.Id);
        var change = Snapshot(state, $"{template.Name} matches today now") with
        {
            Revert = () =>
            {
                savedAsSession = null;
                return store.SaveTemplateAsync(template, before);
            },
        };
        Save(store.SaveTemplateAsync(template, PlanEdits.AsSession(state.Plan)));
        savedAsSession = state.Plan;
        changes.Add(change);
        Announce();
    }

    /// <summary>Whether a rest counts down to the next set, and starts it at zero, instead of counting up.</summary>
    public bool RestCountdown => preferences.GetValueOrDefault(RestCountdownKey) == "on";

    /// <summary>True until the rest countdown has been tried, so the break screen can say how.</summary>
    public bool RestCountdownUntried => !preferences.ContainsKey(RestCountdownKey);

    public void SetRestCountdown(bool on) => SetPreference(RestCountdownKey, on ? "on" : "off");

    /// <summary>The countdown for a rest after this exercise: the one I set last time, else my typical rest after it last time, else 1:30.</summary>
    public TimeSpan RestTarget(long exerciseId)
    {
        if (preferences.GetValueOrDefault(RestTargetKey(exerciseId)) is string saved) return TimeSpan.FromSeconds(int.Parse(saved, CultureInfo.InvariantCulture));

        var last = Current?.History(exerciseId).FirstOrDefault();
        return (last is null ? null : Rest.Typical([.. pastSets.Where(s => s.SessionId == last.Session.Id)], exerciseId)) ?? Rest.Default;
    }

    /// <summary>Moves this exercise's rest countdown by some seconds; it's remembered for next time.</summary>
    public void AdjustRestTarget(long exerciseId, int seconds) =>
        SetPreference(RestTargetKey(exerciseId), Math.Clamp((int)RestTarget(exerciseId).TotalSeconds + seconds, 15, 600).ToString(CultureInfo.InvariantCulture));

    /// <summary>
    /// Takes the trainer's plan for each exercise's next time (see <see cref="TrainerTarget"/>): its first set pre-fills from it, and
    /// its rest counts down from it. Both are remembered with the settings.
    /// </summary>
    public void ApplyTrainer(IReadOnlyList<(long ExerciseId, TrainerTarget Target)> targets)
    {
        foreach (var (exerciseId, target) in targets)
        {
            foreach (var (key, value) in new[] { (TrainerKey(exerciseId), JsonSerializer.Serialize(target)), (RestTargetKey(exerciseId), target.RestSeconds.ToString(CultureInfo.InvariantCulture)) })
            {
                preferences[key] = value;
                Save(store.SetSettingAsync(key, value));
            }

            Current?.TrainerTargets[exerciseId] = target;
        }

        Current?.Refresh();
        Announce();
    }

    /// <summary>The setting that keeps the trainer's plan for an exercise's next time.</summary>
    public static string TrainerKey(long exerciseId) => $"Trainer:{exerciseId}";

    /// <summary>The setting that keeps an exercise's rest countdown.</summary>
    public static string RestTargetKey(long exerciseId) => $"RestTarget:{exerciseId}";

    /// <summary>A trainer's plan as the settings keep it; null for none, or one that can't be read.</summary>
    public static TrainerTarget? ReadTrainerTarget(string? saved)
    {
        if (saved is null) return null;

        try
        {
            return JsonSerializer.Deserialize<TrainerTarget>(saved);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>What a picker of exercises offers: every exercise, the ones done lately first, and how each went the last time.</summary>
    public async Task<ExerciseChoices> ChoicesAsync()
    {
        await LoadAsync();
        return new ExerciseChoices(await store.GetExercisesAsync(), pastSessions, pastSets);
    }

    private async Task LoadHistoryAsync()
    {
        preferences = await store.GetSettingsAsync();
        var sessions = await store.GetSessionsAsync();
        var sets = await store.GetSetsAsync();
        var open = sessions.LastOrDefault(s => s.FinishedAt is null);
        pastSessions = [.. sessions.Where(s => s != open)];
        pastSets = [.. sets.Where(s => s.SessionId != open?.Id)];
        if (open is null) return;

        // A workout started before workouts kept their own plan follows its template.
        var plan = await store.GetSessionPlanAsync(open.Id);
        if (plan.Count == 0 && open.TemplateId is long templateId) plan = await store.GetTemplateExercisesAsync(templateId);
        var openSets = sets.Where(s => s.SessionId == open.Id).ToList();
        var state = Current = await CreateStateAsync(open, openSets, plan);

        // One from before its lines owned their sets gets its lines now, once: each set goes to the line it counted for.
        if (plan.Any(e => e.Line == 0) || openSets.Any(s => s.Line == 0))
        {
            state.Plan = LegacyPlan.Number(plan, openSets, state.SetsLastTime);
            Save(store.SaveSessionPlanAsync(open, state.Plan));
            foreach (var set in openSets) Save(store.UpdateSetAsync(set));
            state.Refresh();
        }

        // Left paused when the app closed: the screen that paused it is gone, so it carries on from here.
        if (Paused) Resume();
        else Announce();
    }

    // Starts a line's block now: it goes ahead of everything else still to do, and its next set (or round) starts.
    private void StartLine(WorkoutState state, List<TemplateExercise> plan, int line)
    {
        Resume();
        SavePlan(state, ToFront(state, plan, line));
        StartSets(state.StepOf(state.BlockOf(line)!));
    }

    // A plan with a line's block ahead of everything else still to do, right after a block with a set running. The exercise I was
    // on, when it has sets and no count, is done with the sets it has: I went on to something else.
    private static List<TemplateExercise> ToFront(WorkoutState state, List<TemplateExercise> plan, int line)
    {
        if (state.NextBlock is int at && state.Blocks()[at] is var on && !on.Any(e => e.Line == line) && state.Progress(on) is { Target: null, Running: false, Done: > 0 and var done })
            plan = PlanEdits.Count(plan, on[0].Line, done);

        var first = WorkoutPlan.Blocks(plan).Find(b => !b.Any(e => e.Line == line) && WorkoutPlan.Progress(b, state.Sets, state.SetsLastTime, state.OpenEnded) is { Complete: false, Running: false });
        return PlanEdits.MoveBefore(plan, line, first?[0].Line ?? 0);
    }

    // A round that's running takes the lines that joined its superset in, on the same clock.
    private void JoinRound(WorkoutState state, int line, IReadOnlyList<int> joining)
    {
        if (state.BlockOf(line) is not { } block || state.Sets.Find(s => s.StoppedAt is null && block.Any(e => e.Line == s.Line)) is not { } running) return;

        foreach (var entry in block.Where(e => joining.Contains(e.Line) && !state.Sets.Exists(s => s.StoppedAt is null && s.Line == e.Line)))
        {
            var set = new WorkoutSet
            {
                ExerciseId = entry.ExerciseId, Line = entry.Line, Order = state.LastOrder + 1, StartedAt = running.StartedAt,
                PausedSeconds = running.PausedSeconds, WeightUnit = settings.Unit,
            };
            state.Sets.Add(set);
            Prefill(state, set);
        }
    }

    // Whether a line's block has had its sets: before one of them leaves it.
    private static bool WasDone(WorkoutState state, int line) => state.BlockOf(line) is { } block && state.Progress(block).Complete;

    // A line done with more sets than it had planned has that many planned: it isn't shown as short of a count it's past.
    private static void CountAtLeastDone(WorkoutState state, int line)
    {
        if (state.BlockOf(line) is { } block && block[0].PlannedSets is int count && state.Progress(block).Done > count)
            state.Plan = PlanEdits.Count(state.Plan, line, state.Progress(block).Done);
    }

    // Puts a set into the workout right after another (first, with none), and renumbers the ones after it. A set that goes in
    // among those already entered at a rest is entered too.
    private static void Insert(WorkoutState state, WorkoutSet? after, WorkoutSet set)
    {
        var at = after is null ? 0 : state.Sets.IndexOf(after) + 1;
        set.Order = (after?.Order ?? 0) + 1;
        if (set.Order <= state.LoggedThrough + 1 && (after is not null || state.LoggedThrough > 0)) state.LoggedThrough++;
        for (var i = at; i < state.Sets.Count; i++) state.Sets[i].Order = Math.Max(state.Sets[i].Order, set.Order + 1 + i - at);
        state.Sets.Insert(at, set);
    }

    // The numbers a set starts from: the set before it of its exercise today, else last time's (see SetSuggestion).
    private void Prefill(WorkoutState state, WorkoutSet set)
    {
        var values = state.Exercises[set.ExerciseId].Measure == ExerciseMeasure.Weight ? state.SuggestionFor(set) : null;
        (set.Weight, set.WeightUnit, set.Reps) = (values?.Weight, values?.Unit ?? settings.Unit, values?.Reps);
    }

    private string NameOn(int line) => Current?.Plan.Find(e => e.Line == line) is { } entry ? Current.Exercises[entry.ExerciseId].Name : "it";

    // A change to a line's block: `what` gets the block's names for its words, `change` the block as it was.
    private void Edit(int line, Func<string, string> what, Action<WorkoutState, List<TemplateExercise>> change)
    {
        if (Current?.BlockOf(line) is not { } block) return;

        Edit(what(Current.Names(block)), state => change(state, block));
    }

    // Makes a change to the open workout: keeps how it was, for Undo, makes the change in memory, and saves what's different.
    private void Edit(string what, Action<WorkoutState> change)
    {
        if (Current is not { } state) return;

        var before = Snapshot(state, what);
        change(state);
        changes.Add(before);
        Persist(state, before);
        state.Refresh();
        Announce();
    }

    private static Change Snapshot(WorkoutState state, string what) => new(what, state.Plan, [.. state.Sets.Select(s => (s, Copy(s, new WorkoutSet())))], state.LoggedThrough);

    // Everything about a set but which row it is.
    private static WorkoutSet Copy(WorkoutSet from, WorkoutSet to)
    {
        (to.ExerciseId, to.Order, to.Line, to.StartedAt, to.StoppedAt, to.PausedSeconds) = (from.ExerciseId, from.Order, from.Line, from.StartedAt, from.StoppedAt, from.PausedSeconds);
        (to.Weight, to.WeightUnit, to.Reps, to.DurationSeconds, to.HitGoal, to.RepsLeft, to.WarmUp) = (from.Weight, from.WeightUnit, from.Reps, from.DurationSeconds, from.HitGoal, from.RepsLeft, from.WarmUp);
        return to;
    }

    private static bool Same(WorkoutSet a, WorkoutSet b) =>
        (a.ExerciseId, a.Order, a.Line, a.StartedAt, a.StoppedAt, a.PausedSeconds, a.Weight, a.WeightUnit, a.Reps, a.DurationSeconds, a.HitGoal, a.RepsLeft, a.WarmUp)
        == (b.ExerciseId, b.Order, b.Line, b.StartedAt, b.StoppedAt, b.PausedSeconds, b.Weight, b.WeightUnit, b.Reps, b.DurationSeconds, b.HitGoal, b.RepsLeft, b.WarmUp);

    // Saves what's different from how the workout was: its lines when they changed, and each set that's gone, new or changed.
    private void Persist(WorkoutState state, Change before)
    {
        if (!ReferenceEquals(before.Plan, state.Plan)) Save(store.SaveSessionPlanAsync(state.Session, state.Plan));
        var were = new HashSet<WorkoutSet>();
        foreach (var (set, was) in before.Sets)
        {
            were.Add(set);
            if (!state.Sets.Contains(set)) Save(store.DeleteSetAsync(set));
            else if (!Same(set, was)) Save(store.UpdateSetAsync(set));
        }

        foreach (var set in state.Sets.Where(s => !were.Contains(s))) Save(store.InsertSetAsync(state.Session, set));
    }

    // Replaces today's plan outside the edit screen (Another set, Move on, an exercise picked to start), and saves it.
    private void SavePlan(WorkoutState state, List<TemplateExercise> plan)
    {
        state.Plan = plan;
        state.Refresh();
        Save(store.SaveSessionPlanAsync(state.Session, plan));
    }

    private async Task MeasureUnmeasuredAsync()
    {
        await LoadAsync();
        var setsBySession = pastSets.ToLookup(s => s.SessionId);
        var unmeasured = pastSessions.Where(s => s is { FinishedAt: not null, AverageBpm: null }).ToList();
        foreach (var session in unmeasured) await MeasureAsync(session, setsBySession[session.Id]);
    }

    // A workout's average heart rate while it was on, kept with it. Asking the store is one piece of work, queued at once, so the
    // screens that read after it see it.
    private async Task MeasureAsync(WorkoutSession session, IEnumerable<WorkoutSet> sets)
    {
        var (from, to) = Calories.WorkoutTime(session, sets);
        session.AverageBpm = await store.SaveAverageBpmAsync(session, from, to);
    }

    // A finished workout's sets changed: it, and every one after it, compared against them, so each is worked out again.
    private async Task RescoreFromAsync(long sessionId)
    {
        if (pastSessions.Find(s => s.Id == sessionId) is not { } changed) return;

        foreach (var session in pastSessions.Where(s => s.StartedAt >= changed.StartedAt)) (session.Improvement, session.ExercisesCompared) = (null, null);
        await ScoreSessionsAsync();
    }

    private async Task<WorkoutState> CreateStateAsync(WorkoutSession session, List<WorkoutSet> sets, List<TemplateExercise> plan)
    {
        var template = session.TemplateId is long templateId ? await store.GetTemplateAsync(templateId) : null;
        var weighIn = (await store.GetBodyWeightsSinceAsync(DateTimeOffset.Now.AddYears(-1), 1)).MaxBy(w => w.At);
        var state = new WorkoutState
        {
            Session = session,
            Template = template,
            Plan = plan,
            Exercises = (await store.GetExercisesAsync()).ToDictionary(e => e.Id),
            Sets = sets,
            PastSessions = pastSessions,
            PastSets = pastSets,
            HitGoalStep = settings.HitGoalStep,
            BodyPounds = weighIn is null ? null : WorkoutStats.Pounds(weighIn.Weight, weighIn.WeightUnit),
            LoggedThrough = sets.Count == 0 ? 0 : sets[^1].Order,
            TrainerTargets = preferences.Where(p => p.Key.StartsWith("Trainer:", StringComparison.Ordinal))
                .Select(p => (Id: long.TryParse(p.Key["Trainer:".Length..], CultureInfo.InvariantCulture, out var id) ? id : 0, Target: ReadTrainerTarget(p.Value)))
                .Where(p => p is { Id: > 0, Target: not null }).ToDictionary(p => p.Id, p => p.Target!),
        };
        state.Refresh();
        return state;
    }

    private void SetPreference(string key, string value)
    {
        preferences[key] = value;
        Save(store.SetSettingAsync(key, value));
        Announce();
    }

    // Tells the watch, the notification and any screen showing the workout what it's doing now.
    private void Announce()
    {
        var status = Status;
        if (status is not null) watch.Show(status);
        notice.Show(status);
        Changed?.Invoke();
    }

    // Saves run in the background; a failure is logged rather than holding up the screen.
    private static void Save(Task write) => write.ContinueWith(t => Debug.WriteLine($"AdaptiveWorkout: saving failed: {t.Exception}"), TaskContinuationOptions.OnlyOnFaulted);

    // How the workout was before a change on the edit screen: its lines (a list that's never changed once it's the plan), each set
    // with what it held, and what a save to the session needs undone.
    private sealed record Change(string What, List<TemplateExercise> Plan, List<(WorkoutSet Set, WorkoutSet Was)> Sets, int LoggedThrough, Func<Task>? Revert = null);
}
