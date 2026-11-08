using AdaptiveWorkout.UI.Storage;
using Playground.AI.Assist;

namespace AdaptiveWorkout.UI.Assist;

/// <summary>The app's calls to Claude, kept in its data file with the workout each was for, so Settings can show what a month cost.</summary>
public sealed class ClaudeLog(WorkoutStore store) : IAssistLog
{
    /// <summary>The workout the calls being made are for: set around a look back, it goes with every call the look back makes.</summary>
    public static readonly AsyncLocal<long?> Session = new();

    public Task WriteAsync(AssistCall call) => store.AddClaudeCallAsync(call, Session.Value);
}
