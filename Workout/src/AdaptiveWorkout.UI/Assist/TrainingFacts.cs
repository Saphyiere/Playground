using AdaptiveWorkout.Core;
using AdaptiveWorkout.Core.Assist;
using AdaptiveWorkout.UI.Storage;
using AdaptiveWorkout.UI.Workouts;

namespace AdaptiveWorkout.UI.Assist;

/// <summary>What Claude is told about my training, gathered for a question or a week's look back (see <see cref="TrainingFacts"/>).</summary>
public sealed record TrainingData(List<WorkoutSession> Sessions, List<WorkoutSet> Sets, Dictionary<long, Exercise> Exercises, List<CalorieDay> Days,
    List<BodyWeightEntry> WeighIns, AskContext Context);

/// <summary>
/// Gathers what Claude is told about my training, the same for a question (see <see cref="Coach"/>) and a week's look back (see
/// <see cref="WeekReview"/>): the finished workouts and their sets, the exercises, food by day, the weigh-ins, and the context, my step,
/// unit, calorie goal, maintenance by the formula and by the scale, what each exercise trains, the plans waiting for exercises' next
/// times, and the routine. Exercises not tagged yet are tagged first, and workouts' heart rates are worked out, for what they burned.
/// </summary>
public sealed class TrainingFacts(WorkoutStore store, WorkoutFlow flow, AppSettings settings, MuscleTagger tagger)
{
    /// <summary>Everything, with food by day from <paramref name="from"/> (and the four weeks before <paramref name="now"/>, for the scale) to today.</summary>
    public async Task<TrainingData> LoadAsync(DateOnly from, DateTimeOffset now)
    {
        await tagger.TagAsync();
        await flow.MeasureHeartRatesAsync();
        var today = DateOnly.FromDateTime(now.DateTime);
        var (sessions, sets) = await flow.LoadFinishedAsync();
        var exercises = (await store.GetExercisesAsync()).ToDictionary(e => e.Id);
        var weighIns = await store.GetBodyWeightsAsync();
        var start = from < today.AddDays(-28) ? from : today.AddDays(-28);
        var days = Calories.Days(await store.GetFoodAsync(), weighIns, settings, start, today, Calories.BurnedByDay(sessions, sets, exercises, weighIns, settings),
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

        var maintenance = Calories.Maintenance(settings, BodyWeight.Nearest(BodyWeight.InOrder(weighIns), now), today);
        var context = new AskContext(settings.HitGoalStep, settings.Unit, settings.CalorieGoal, maintenance, byScale?.ByScale, await store.GetMuscleTagsAsync(), trainer, routine);
        return new TrainingData(sessions, sets, exercises, days, weighIns, context);
    }
}
