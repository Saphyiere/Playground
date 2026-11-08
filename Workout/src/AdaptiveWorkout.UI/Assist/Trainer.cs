using System.Globalization;
using AdaptiveWorkout.Core;
using AdaptiveWorkout.Core.Assist;
using AdaptiveWorkout.UI.Storage;
using AdaptiveWorkout.UI.Workouts;
using Playground.AI.Assist;

namespace AdaptiveWorkout.UI.Assist;

/// <summary>
/// The trainer: Claude's look back at each finished workout (see <see cref="LookBackTask"/>), asked as the workout finishes, kept with
/// it, and applied with a tap so each exercise's next time starts from its plan. A look back still being asked keeps going when its
/// screen closes; the screen showing it hears when it's done.
/// </summary>
public sealed class Trainer(WorkoutStore store, WorkoutFlow flow, IClaude claude, IAssistKeyStore keys, AppSettings settings, MuscleTagger tagger)
{
    /// <summary>The setting that turns the look back after Finish off ("off"); it's on otherwise.</summary>
    public const string SettingKey = "LookBack";

    // The askings under way, and each one's task, so asking again while one runs hands it back; an asking leaves the first before it
    // says it's done, so a screen that hears it sees the answer kept.
    private readonly HashSet<long> asking = [];
    private readonly Dictionary<long, Task<LookBackRecord>> tasks = [];
    private readonly Dictionary<long, DateTimeOffset> askedAt = [];

    /// <summary>Raised with a workout's id when its look back starts being asked, is kept, or is applied.</summary>
    public event Action<long>? Changed;

    /// <summary>Whether finishing a workout opens its look back: there's a key on this phone, and it isn't turned off in Settings.</summary>
    public async Task<bool> LooksBackAsync() => (await store.GetSettingsAsync()).GetValueOrDefault(SettingKey) != "off" && await HasKeyAsync();

    /// <summary>Whether there's a Claude key on this phone, to ask with.</summary>
    public async Task<bool> HasKeyAsync() => !string.IsNullOrWhiteSpace(await keys.GetAsync());

    /// <summary>Whether a workout's look back is being asked now.</summary>
    public bool IsAsking(long sessionId) => asking.Contains(sessionId);

    /// <summary>When the asking of a workout's look back began; null when it hasn't been asked here.</summary>
    public DateTimeOffset? AskedAt(long sessionId) => askedAt.TryGetValue(sessionId, out var at) ? at : null;

    /// <summary>The look back kept for a workout; null when there's none yet.</summary>
    public Task<LookBackRecord?> GetAsync(long sessionId) => store.GetLookBackAsync(sessionId);

    /// <summary>
    /// Asks for a workout's look back, or hands back the asking already under way, and keeps the answer (or what went wrong) with
    /// the workout, in place of any before.
    /// </summary>
    public Task<LookBackRecord> AskAsync(long sessionId)
    {
        if (asking.Contains(sessionId) && tasks.TryGetValue(sessionId, out var running)) return running;

        asking.Add(sessionId);
        askedAt[sessionId] = DateTimeOffset.Now;
        var task = AskNowAsync(sessionId);
        tasks[sessionId] = task;
        Changed?.Invoke(sessionId);
        return task;
    }

    /// <summary>
    /// Takes a look back's plan for the next time of each exercise: its first set pre-fills from it, its sets follow it where the plan
    /// doesn't say, and its rest counts down from it.
    /// </summary>
    public async Task ApplyAsync(long sessionId)
    {
        if (await store.GetLookBackAsync(sessionId) is not { Input: { } input, Answer: { } answer }) return;

        flow.ApplyTrainer(LookBackPlan.Targets(input, answer));
        await store.SetLookBackAppliedAsync(sessionId, DateTimeOffset.Now);
        Changed?.Invoke(sessionId);
    }

    private async Task<LookBackRecord> AskNowAsync(long sessionId)
    {
        LookBackInput? input = null;
        LookBackRecord record;
        try
        {
            input = await InputAsync(sessionId);
            ClaudeLog.Session.Value = sessionId;
            var answer = await claude.AskAsync(new LookBackTask(), input);
            record = new LookBackRecord(sessionId, DateTimeOffset.Now, answer.Kind, answer.Summary, answer.Notes, input, answer.Value, answer.Usage.Cost, null);
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            // A bug, not Claude's or the network's trouble (those come back as kinds): said on the screen, so it can be asked again.
            record = new LookBackRecord(sessionId, DateTimeOffset.Now, AnswerKind.Failed, $"The look back couldn't be asked: {e.Message}", [], input, null, 0, null);
        }
        finally
        {
            ClaudeLog.Session.Value = null;
        }

        try
        {
            await store.SaveLookBackAsync(record);
        }
        finally
        {
            asking.Remove(sessionId);
            Changed?.Invoke(sessionId);
        }

        return record;
    }

    // The workout as the trainer reads it: its sets, plan and heart rate, the workouts before it, the rest countdowns and the trainer's
    // own last plans from the settings, the weigh-in before it, and on the week's last planned workout the week's muscles.
    private async Task<LookBackInput> InputAsync(long sessionId)
    {
        var session = await store.GetSessionAsync(sessionId) ?? throw new InvalidOperationException("That workout isn't kept any more.");
        var (sessions, allSets) = await flow.LoadFinishedAsync();
        var sets = allSets.Where(s => s.SessionId == sessionId).ToList();
        if (sets.Count == 0) sets = await store.GetSessionSetsAsync(sessionId);
        var plan = await store.GetSessionPlanAsync(sessionId);
        var exercises = (await store.GetExercisesAsync()).ToDictionary(e => e.Id);
        var name = session.TemplateId is long templateId ? (await store.GetTemplateAsync(templateId))?.Name ?? "Workout" : "Workout";
        var (from, to) = Calories.WorkoutTime(session, sets);
        var samples = await store.GetHeartRateAsync(from.AddMinutes(-1), (session.FinishedAt ?? to).AddMinutes(1));
        var saved = await store.GetSettingsAsync();
        var weighIn = BodyWeight.Nearest(BodyWeight.InOrder(await store.GetBodyWeightsAsync()), session.StartedAt);
        var earlier = sessions.Where(s => s.Id != sessionId && s.StartedAt < session.StartedAt).ToList();

        var restTargets = new Dictionary<long, int>();
        var trainer = new Dictionary<long, TrainerTarget>();
        foreach (var exerciseId in sets.Select(s => s.ExerciseId).Distinct())
        {
            // The countdown it had: the one I set, else my usual rest after it the time before, else 1:30 (as the workout worked it out).
            var lastTime = ExerciseHistory.Recent(exerciseId, earlier, allSets, sessionId, 1).FirstOrDefault();
            restTargets[exerciseId] = saved.GetValueOrDefault(WorkoutFlow.RestTargetKey(exerciseId)) is string set && int.TryParse(set, CultureInfo.InvariantCulture, out var seconds)
                ? seconds
                : (int)((lastTime is null ? null : Rest.Typical([.. allSets.Where(s => s.SessionId == lastTime.Session.Id)], exerciseId)) ?? Rest.Default).TotalSeconds;
            if (WorkoutFlow.ReadTrainerTarget(saved.GetValueOrDefault(WorkoutFlow.TrainerKey(exerciseId))) is { } target) trainer[exerciseId] = target;
        }

        // The week's muscles wait for exercises being tagged, only when they're needed; otherwise those are tagged meanwhile.
        var routine = await store.GetActiveRoutineAsync();
        var week = routine is null ? null : await store.GetRoutineWeekAsync(routine.Id);
        List<WorkoutSet> withToday = [.. allSets, .. sets];
        var muscles = LookBackFacts.WeekMuscles(session, sessions, withToday, exercises, await store.GetMuscleTagsAsync(), week);
        if (muscles is { Untagged.Count: > 0 })
        {
            await tagger.TagAsync();
            muscles = LookBackFacts.WeekMuscles(session, sessions, withToday, exercises, await store.GetMuscleTagsAsync(), week);
        }
        else
        {
            _ = tagger.TagAsync();
        }

        var maintenance = Calories.Maintenance(settings, weighIn, DateOnly.FromDateTime(session.StartedAt.DateTime));
        var context = new LookBackContext(settings.HitGoalStep, restTargets, trainer, weighIn, settings.Unit, settings.CalorieGoal, maintenance, muscles);
        return LookBackFacts.For(session, name, sets, plan, exercises, sessions, allSets, samples, context);
    }
}
