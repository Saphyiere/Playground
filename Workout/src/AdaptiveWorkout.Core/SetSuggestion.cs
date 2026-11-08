namespace AdaptiveWorkout.Core;

/// <summary>Pre-filled values for a set.</summary>
/// <param name="RaisedForGoal">True when the weight went up because I hit my goal last time.</param>
/// <param name="FromTrainer">True when they're the trainer's, from the last workout's look back.</param>
public sealed record SetValues(double? Weight, WeightUnit Unit, int? Reps, bool RaisedForGoal, bool FromTrainer = false);

/// <summary>
/// What the trainer's look back at a workout said for an exercise's next time, once applied. It counts until the exercise is done again
/// after <paramref name="SessionId"/>, the workout it looked back at. An empty weight is bodyweight.
/// </summary>
public sealed record TrainerTarget(long SessionId, double? Weight, WeightUnit Unit, int Reps, int Sets, int RestSeconds);

public static class SetSuggestion
{
    /// <summary>
    /// The previous set this session; else the trainer's next time, when it's applied for this time; else, after a hit goal last time,
    /// that set's weight plus the step; else the first set last time. Bodyweight stays bodyweight. Null when there is nothing to go on.
    /// </summary>
    /// <param name="thisSession">This exercise's sets so far this session, in order.</param>
    /// <param name="lastTime">This exercise's sets the last time I did it, in order.</param>
    /// <param name="trainer">The trainer's applied next time for this exercise, when it's from the last time I did it.</param>
    public static SetValues? For(IReadOnlyList<WorkoutSet> thisSession, IReadOnlyList<WorkoutSet> lastTime, double hitGoalStep, TrainerTarget? trainer = null)
    {
        if (thisSession.Count > 0) return new SetValues(thisSession[^1].Weight, thisSession[^1].WeightUnit, thisSession[^1].Reps, false);
        if (trainer is not null) return new SetValues(trainer.Weight, trainer.Unit, trainer.Reps, false, FromTrainer: true);
        if (lastTime.Count == 0) return null;

        var goalSet = lastTime.LastOrDefault(s => s.HitGoal && s.Weight is not null);
        if (goalSet is not null) return new SetValues(goalSet.Weight + hitGoalStep, goalSet.WeightUnit, goalSet.Reps, true);

        return new SetValues(lastTime[0].Weight, lastTime[0].WeightUnit, lastTime[0].Reps, false);
    }
}
