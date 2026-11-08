using System.Collections.Concurrent;

namespace Playground.AI.Assist;

/// <summary>
/// One request to Claude: which task and model, how it went, what was asked (the input as JSON, without its images) and
/// what came back, what it used, and the API's id for the reply. A question whose answer failed its checks is asked again,
/// so it can be two calls.
/// </summary>
public sealed record AssistCall(
    DateTimeOffset At,
    string Task,
    int Version,
    string Model,
    int Attempt,
    AnswerKind Kind,
    string Summary,
    string Question,
    string Reply,
    int Attachments,
    Usage Usage,
    string MessageId,
    string Error);

/// <summary>Where a host keeps its calls to Claude: for seeing what was asked and what it cost, and for trying new instructions on old questions.</summary>
public interface IAssistLog
{
    Task WriteAsync(AssistCall call);
}

/// <summary>Keeps nothing.</summary>
public sealed class NoAssistLog : IAssistLog
{
    public static readonly NoAssistLog Instance = new();

    public Task WriteAsync(AssistCall call) => Task.CompletedTask;
}

/// <summary>Keeps the calls in memory, for tests and preview hosts.</summary>
public sealed class MemoryAssistLog : IAssistLog
{
    readonly ConcurrentQueue<AssistCall> calls = new();

    public IReadOnlyList<AssistCall> Calls => [.. calls];

    public Task WriteAsync(AssistCall call)
    {
        calls.Enqueue(call);
        return Task.CompletedTask;
    }
}
