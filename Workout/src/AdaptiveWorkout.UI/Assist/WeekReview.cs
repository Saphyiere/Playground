using AdaptiveWorkout.Core;
using AdaptiveWorkout.Core.Assist;
using AdaptiveWorkout.UI.Storage;
using AdaptiveWorkout.UI.Workouts;
using Playground.AI.Assist;

namespace AdaptiveWorkout.UI.Assist;

/// <summary>
/// Claude's look back at each week (see <see cref="WeekTask"/>): asked when Today opens and a week is due, the Monday after or once its last
/// planned workout is done (see <see cref="WeekFacts.Due"/>), never by anything running in the background; and kept by its Monday. Today
/// shows the latest, and its page shows it in full with the weeks before. A look back still being asked keeps going when its screen
/// closes; the screens hear when it's done.
/// </summary>
public sealed class WeekReview(WorkoutStore store, WorkoutFlow flow, IClaude claude, IAssistKeyStore keys, TrainingFacts facts)
{
    /// <summary>The setting that turns the week's look back off ("off"); it's on otherwise.</summary>
    public const string SettingKey = "WeekLookBack";

    private readonly Dictionary<DateOnly, Task<WeekLookRecord>> asking = [];
    private readonly Dictionary<DateOnly, DateTimeOffset> askedAt = [];

    /// <summary>Raised with a week's Monday when its look back starts being asked, and when it's kept.</summary>
    public event Action<DateOnly>? Changed;

    /// <summary>Whether a week's look back is being asked now.</summary>
    public bool IsAsking(DateOnly monday) => asking.TryGetValue(monday, out var task) && !task.IsCompleted;

    /// <summary>When the asking of a week's look back began; null when it hasn't been asked here.</summary>
    public DateTimeOffset? AskedAt(DateOnly monday) => askedAt.TryGetValue(monday, out var at) ? at : null;

    /// <summary>The weeks looked back at, newest first.</summary>
    public Task<List<WeekLookRecord>> KeptAsync() => store.GetWeekLooksAsync();

    /// <summary>The week due a look back now, when it's on in Settings and there's a key on this phone; null otherwise.</summary>
    public async Task<DateOnly?> DueAsync()
    {
        if ((await store.GetSettingsAsync()).GetValueOrDefault(SettingKey) == "off" || string.IsNullOrWhiteSpace(await keys.GetAsync())) return null;

        var (sessions, _) = await flow.LoadFinishedAsync();
        var routine = await store.GetActiveRoutineAsync();
        var week = routine is null ? null : await store.GetRoutineWeekAsync(routine.Id);
        var kept = (await store.GetWeekLooksAsync()).Select(w => w.Monday).ToHashSet();
        return WeekFacts.Due(DateOnly.FromDateTime(DateTime.Now), sessions, week, kept);
    }

    /// <summary>Asks for a week's look back, or hands back the asking under way, and keeps the answer (or what went wrong) in place of any before.</summary>
    public Task<WeekLookRecord> AskAsync(DateOnly monday)
    {
        if (asking.TryGetValue(monday, out var running) && !running.IsCompleted) return running;

        askedAt[monday] = DateTimeOffset.Now;
        var task = AskNowAsync(monday);
        asking[monday] = task;
        Changed?.Invoke(monday);
        return task;
    }

    private async Task<WeekLookRecord> AskNowAsync(DateOnly monday)
    {
        WeekInput? input = null;
        WeekLookRecord record;
        try
        {
            // The week, the four before it, and the four weeks before now for the scale; see TrainingFacts.
            var now = DateTimeOffset.Now;
            var data = await facts.LoadAsync(monday.AddDays(-7 * WeekFacts.Before), now);
            input = WeekFacts.For(monday, now, data.Sessions, data.Sets, data.Exercises, data.Days, data.WeighIns, data.Context);
            var answer = await claude.AskAsync(new WeekTask(), input);
            record = new WeekLookRecord(monday, DateTimeOffset.Now, answer.Kind, answer.Summary, answer.Notes, input, answer.Value, answer.Usage.Cost);
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            // A bug, not Claude's or the network's trouble (those come back as kinds): said on the screen, so it can be asked again.
            record = new WeekLookRecord(monday, DateTimeOffset.Now, AnswerKind.Failed, $"The week's look back couldn't be asked: {e.Message}", [], input, null, 0);
        }

        try
        {
            await store.SaveWeekLookAsync(record);
        }
        finally
        {
            asking.Remove(monday);
            Changed?.Invoke(monday);
        }

        return record;
    }
}
