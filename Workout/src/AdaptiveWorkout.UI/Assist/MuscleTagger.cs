using AdaptiveWorkout.Core;
using AdaptiveWorkout.Core.Assist;
using AdaptiveWorkout.UI.Storage;
using AdaptiveWorkout.UI.Workouts;
using Playground.AI.Assist;

namespace AdaptiveWorkout.UI.Assist;

/// <summary>
/// Claude's tagging of my exercises with the muscles they train (see <see cref="TagExercises"/>), for the muscle map: asked for every
/// exercise not tagged yet, in one question, when an exercise is made, when the muscle screens open, and before a look back. One asking
/// at a time: an exercise made during one is tagged right after it. When an asking goes wrong, the same exercises wait for Try again,
/// so trouble isn't paid for twice; a new exercise asks again for all of them.
/// </summary>
public sealed class MuscleTagger(WorkoutStore store, WorkoutFlow flow, IClaude claude, IAssistKeyStore keys)
{
    private Task running = Task.CompletedTask;
    private bool again;
    private HashSet<long> failedFor = [];

    /// <summary>Raised when an asking starts, and when it's done and its tags are kept.</summary>
    public event Action? Changed;

    /// <summary>How many exercises are being tagged now; 0 when none are.</summary>
    public int Tagging { get; private set; }

    /// <summary>What went wrong the last time, for the screen; null when it went well, or nothing has been asked.</summary>
    public string? Trouble { get; private set; }

    /// <summary>
    /// Tags the exercises not tagged yet, when there's a key. Hands back the asking under way, with another to follow for exercises made
    /// meanwhile. Without <paramref name="retry"/>, exercises whose last asking went wrong wait. Never throws: trouble is kept in
    /// <see cref="Trouble"/>.
    /// </summary>
    public Task TagAsync(bool retry = false)
    {
        if (!running.IsCompleted)
        {
            again = true;
            return running;
        }

        running = TagAllAsync();
        return running;

        // Asks for the exercises not tagged yet, and again while new ones were made during the asking.
        async Task TagAllAsync()
        {
            do
            {
                again = false;
                var retrying = retry;
                retry = false;
                var asked = new HashSet<long>();
                try
                {
                    if (string.IsNullOrWhiteSpace(await keys.GetAsync())) continue;

                    var exercises = await store.GetExercisesAsync();
                    var tags = await store.GetMuscleTagsAsync();
                    var untagged = exercises.Where(e => !tags.ContainsKey(e.Id)).ToList();
                    if (untagged.Count == 0 || (!retrying && untagged.All(e => failedFor.Contains(e.Id)))) continue;

                    // Each with how I did it last, which says weights, bodyweight, assisted or time; and the ones I tagged myself, to follow.
                    var (sessions, sets) = await flow.LoadFinishedAsync();
                    var names = exercises.ToDictionary(e => e.Id, e => e.Name);
                    var toTag = untagged.Select(e => new ExerciseToTag(e.Id, e.Name, e.Measure,
                        ExerciseHistory.Recent(e.Id, sessions, sets, 0, 1) is [var last] ? SetSummary.Format(last.Sets, e.Measure) : "")).ToList();
                    var mine = tags.Values.Where(t => t.ByOwner && names.ContainsKey(t.ExerciseId)).Select(t => new OwnerTag(names[t.ExerciseId], t.Main, t.Helps)).ToList();
                    asked = [.. untagged.Select(e => e.Id)];
                    Tagging = toTag.Count;
                    Trouble = null;
                    Changed?.Invoke();

                    var answer = await claude.AskAsync(new TagExercises(), new ExercisesToTag(toTag, mine));
                    if (answer is { Ok: true, Value: { } value })
                    {
                        await store.SaveMuscleTagsAsync(TagExercises.Tags(value));
                        failedFor = [];
                    }
                    else
                    {
                        Trouble = answer.Summary;
                        failedFor = asked;
                    }
                }
                catch (Exception e) when (e is not OperationCanceledException)
                {
                    // A bug, not Claude's or the network's trouble (those come back as kinds): said on the screen, and asked again on Try again.
                    Trouble = $"The tagging couldn't be asked: {e.Message}";
                    failedFor = asked;
                    Changed?.Invoke();
                }
                finally
                {
                    if (Tagging > 0)
                    {
                        Tagging = 0;
                        Changed?.Invoke();
                    }
                }
            }
            while (again);
        }
    }
}
