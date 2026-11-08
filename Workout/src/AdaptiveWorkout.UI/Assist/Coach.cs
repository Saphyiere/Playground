using AdaptiveWorkout.Core;
using AdaptiveWorkout.Core.Assist;
using AdaptiveWorkout.UI.Storage;
using AdaptiveWorkout.UI.Workouts;
using Playground.AI.Assist;

namespace AdaptiveWorkout.UI.Assist;

/// <summary>
/// Ask Claude: one question at a time about my training, sent with the last 8 weeks summed up (see <see cref="AskTask"/>), kept with its
/// answer, and where the answer plans the next workout's lifts, taken for the next time with a tap, as a look back's plan is. A question
/// still being asked keeps going when its screen closes; the screen hears when it's done.
/// </summary>
public sealed class Coach(WorkoutStore store, WorkoutFlow flow, IClaude claude, AppSettings settings, MuscleTagger tagger)
{
    // How many earlier questions go with a new one, for a follow-up.
    private const int Earlier = 3;

    private Task<QuestionRecord>? asking;

    /// <summary>Raised when a question starts being asked, is answered and kept, or has its plan taken.</summary>
    public event Action? Changed;

    /// <summary>The question being asked now; null when none is.</summary>
    public string? Asking { get; private set; }

    /// <summary>When the question being asked, or the last one, was asked.</summary>
    public DateTimeOffset AskedAt { get; private set; } = DateTimeOffset.Now;

    /// <summary>The latest questions asked, newest first.</summary>
    public Task<List<QuestionRecord>> RecentAsync(int count) => store.GetQuestionsAsync(count);

    /// <summary>Asks a question, or hands back the one being asked, and keeps the answer (or what went wrong).</summary>
    public Task<QuestionRecord> AskAsync(string question)
    {
        if (asking is { IsCompleted: false }) return asking;

        Asking = question.Trim();
        AskedAt = DateTimeOffset.Now;
        asking = AskNowAsync(Asking, AskedAt);
        Changed?.Invoke();
        return asking;
    }

    /// <summary>Takes an answer's plan for each exercise's next time: its first set pre-fills from it, and its rest counts down from it.</summary>
    public async Task ApplyAsync(QuestionRecord record)
    {
        if (record is not { Input: { } input, Answer: { } answer }) return;

        flow.ApplyTrainer(AskPlan.Targets(input, answer));
        await store.SetQuestionAppliedAsync(record.Id, DateTimeOffset.Now);
        Changed?.Invoke();
    }

    private async Task<QuestionRecord> AskNowAsync(string question, DateTimeOffset at)
    {
        AskInput? input = null;
        QuestionRecord record;
        try
        {
            input = await InputAsync(question);
            var answer = await claude.AskAsync(new AskTask(), input);
            record = new QuestionRecord(0, at, question, answer.Kind, answer.Summary, answer.Notes, input, answer.Value, answer.Usage.Cost, null);
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            // A bug, not Claude's or the network's trouble (those come back as kinds): said on the screen, so it can be asked again.
            record = new QuestionRecord(0, at, question, AnswerKind.Failed, $"The question couldn't be asked: {e.Message}", [], input, null, 0, null);
        }

        try
        {
            return await store.AddQuestionAsync(record);
        }
        finally
        {
            Asking = null;
            Changed?.Invoke();
        }
    }

    // The last 8 weeks as Claude reads them: the workouts and their sets, food by day, the weigh-ins, maintenance by the formula and
    // by the scale, what each exercise trains, the plans waiting for exercises' next times, the routine, and the last few questions.
    private async Task<AskInput> InputAsync(string question)
    {
        // Exercises not tagged yet are tagged first, so the weeks' muscles count them; and workouts' heart rates are worked out, for what
        // they burned.
        await tagger.TagAsync();
        await flow.MeasureHeartRatesAsync();
        var now = DateTimeOffset.Now;
        var today = DateOnly.FromDateTime(now.DateTime);
        var (sessions, sets) = await flow.LoadFinishedAsync();
        var exercises = (await store.GetExercisesAsync()).ToDictionary(e => e.Id);
        var weighIns = await store.GetBodyWeightsAsync();
        var first = Consistency.WeekStart(today).AddDays(-7 * (AskFacts.Weeks - 1));
        var days = Calories.Days(await store.GetFoodAsync(), weighIns, settings, first, today, Calories.BurnedByDay(sessions, sets, exercises, weighIns, settings),
            (await store.GetCheckAnswersAsync()).AtMaintenance);

        // Maintenance by the scale over the last four weeks, as Calories works it out.
        DateTimeOffset Start(DateOnly day) => new(day.ToDateTime(TimeOnly.MinValue));
        var byScale = Calories.FromScale([.. days.Where(d => d.Day >= today.AddDays(-28) && d.Day < today)], weighIns, Start(today.AddDays(-28)), Start(today));

        var saved = await store.GetSettingsAsync();
        var trainer = new Dictionary<long, TrainerTarget>();
        foreach (var id in exercises.Keys)
            if (WorkoutFlow.ReadTrainerTarget(saved.GetValueOrDefault(WorkoutFlow.TrainerKey(id))) is { } target) trainer[id] = target;

        AskRoutinePlan? routine = null;
        if (await store.GetActiveRoutineAsync() is { } active)
        {
            var week = await store.GetRoutineWeekAsync(active.Id);
            var templates = (await store.GetTemplatesAsync()).ToDictionary(t => t.Id);
            var planned = new Dictionary<long, AskSession>();
            foreach (var templateId in week.OfType<long>().Distinct().Where(templates.ContainsKey))
                planned[templateId] = new AskSession(templates[templateId].Name, await store.GetTemplateExercisesAsync(templateId));
            routine = new AskRoutinePlan(active.Name, [.. week.Select(d => d is long t && planned.TryGetValue(t, out var session) ? session : null)]);
        }

        // The last few questions answered, oldest first, as a conversation reads.
        var earlier = (await store.GetQuestionsAsync(Earlier)).Where(q => q.Answer is not null).Reverse()
            .Select(q => new AskEarlier(DateOnly.FromDateTime(q.At.DateTime), q.Question, string.Join(" ", q.Answer!.Paragraphs))).ToList();
        var maintenance = Calories.Maintenance(settings, BodyWeight.Nearest(BodyWeight.InOrder(weighIns), now), today);
        var context = new AskContext(settings.HitGoalStep, settings.Unit, settings.CalorieGoal, maintenance, byScale?.ByScale, await store.GetMuscleTagsAsync(), trainer, routine);
        return AskFacts.For(question, now, sessions, sets, exercises, days, weighIns, context, earlier);
    }
}
