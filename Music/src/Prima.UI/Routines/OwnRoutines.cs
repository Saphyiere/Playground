using Prima.Core.Generation;
using Prima.Core.Routines;

namespace Prima.UI.Routines;

/// <summary>
/// What a routine of your own is made of: exercises and drills from the catalogue, each an activity for the minutes it's given,
/// played as you last left it (its key, hands, variations, tempo and Loop are the exercise's own, wherever it's played, and a
/// drill's step is its own ladder's), so practising it in a routine moves it on as practising it anywhere does.
/// </summary>
public static class OwnRoutines
{
    // The drills whose screens keep a routine's time and runs.
    private static readonly HashSet<string> Drills = ["notes", "ledger", "intervals", "chords", "keys", "geography", "scales", "arpeggios", "independence", "rhythm"];

    /// <summary>What an exercise is in a routine: "exercise" or "drill", and its id there (a drill's from its address, "scales").</summary>
    public static (string Kind, string Target) Of(Exercise exercise) =>
        exercise.Route is { } route ? ("drill", route[(route.LastIndexOf('/') + 1)..]) : ("exercise", exercise.Id);

    /// <summary>
    /// Whether it can be an activity of a routine: one played, one done away from the keys, or a drill whose screen keeps a
    /// routine's time. A way to practice is to read.
    /// </summary>
    public static bool CanHave(Exercise exercise) => exercise.Kind switch
    {
        ExerciseKind.Played or ExerciseKind.Away => exercise.Route is null,
        ExerciseKind.Drill => Drills.Contains(Of(exercise).Target),
        _ => false,
    };

    /// <summary>An exercise as an activity: a minute for one away from the keys, three to play, five for a drill.</summary>
    public static PreparedRoutineItem Activity(Exercise exercise) => Activity(exercise, exercise.Kind == ExerciseKind.Away ? 1 : Of(exercise).Kind == "drill" ? 5 : 3);

    /// <summary>An exercise as an activity, for these minutes.</summary>
    public static PreparedRoutineItem Activity(Exercise exercise, int minutes)
    {
        var (kind, target) = Of(exercise);
        return new(new RoutineItem
        {
            Id = Guid.NewGuid().ToString("N")[..12],
            Kind = kind,
            TargetId = target,
            Title = exercise.Name,
            Purpose = exercise.Does,
            Minutes = minutes,
            Hands = exercise.Hands.Count > 0 ? exercise.Hands[0] : HandUse.Together,
            Mode = "time",
            Tempo = exercise.Tempo,
        }, "", null);
    }

    /// <summary>The exercise an activity is in the catalogue; none for one Claude made from a piece.</summary>
    public static Exercise? ExerciseOf(RoutineItem item) => item.Kind switch
    {
        "exercise" => Exercises.Find(item.TargetId),
        "drill" => Exercises.All.FirstOrDefault(e => e.Route == $"drills/{item.TargetId}"),
        _ => null,
    };

    /// <summary>Whether a routine has an exercise among its activities.</summary>
    public static bool Holds(RoutineDefinition routine, Exercise exercise)
    {
        var (kind, target) = Of(exercise);
        return routine.Items.Any(i => i.Recipe.Kind == kind && i.Recipe.TargetId == target);
    }

    /// <summary>
    /// Whether an activity is played as you last left it: an exercise or a drill in a routine of your own. One of Claude's is played
    /// as its recipe says (a key, hands, a mode and a tempo), and leaves your own as they were; so is music Claude made from a
    /// piece, wherever it is; and so is one of RCM's technique (<see cref="RcmTechnique"/>), in the key and hands its day brings.
    /// </summary>
    public static bool AsLeft(RoutineDefinition routine, RoutineItem item) => routine.Own && item.Kind is "exercise" or "drill" && RcmTechnique.Find(item.Id) is null;

    /// <summary>One of RCM's technique in a routine of yours, as it is on a day; null for any other activity.</summary>
    public static RcmToday? Rcm(RoutineDefinition routine, RoutineItem item, DateOnly day) => routine.Own ? RcmTechnique.Find(item.Id)?.On(day) : null;
}
