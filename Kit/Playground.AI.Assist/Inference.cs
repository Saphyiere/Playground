namespace Playground.AI.Assist;

/// <summary>The inference operations the adapter implements. Null means unknown, including model-specific token limits.</summary>
public sealed record InferenceCapabilities(bool? Images = null, bool? Pdf = null, bool? StructuredOutput = null,
    bool? Streaming = null, bool? Cancellation = null, bool? TokenCounting = null, bool? DurableRetrieval = null,
    int? MaxInputTokens = null, int? MaxOutputTokens = null)
{
    public static readonly InferenceCapabilities Unknown = new();
}

/// <summary>Overrides for one task execution. Model identifiers belong to the selected provider, not a shared model enum.</summary>
public sealed record InferenceSettings(string? Model = null, EffortLevel? Effort = null, int? MaxOutputTokens = null,
    int ValidationRetries = 1, bool AllowModelFallback = true)
{
    /// <summary>Reject invalid settings before dispatching any request in a multi-stage operation.</summary>
    public void Validate()
    {
        if (Model is not null && string.IsNullOrWhiteSpace(Model)) throw new ArgumentException("A model override cannot be empty.", nameof(Model));
        if (MaxOutputTokens is <= 0) throw new ArgumentOutOfRangeException(nameof(MaxOutputTokens), "The output token limit must be positive.");
        if (Effort is { } effort && !Enum.IsDefined(effort)) throw new ArgumentOutOfRangeException(nameof(Effort), "Unknown inference effort.");
        if (ValidationRetries is < 0 or > 1) throw new ArgumentOutOfRangeException(nameof(ValidationRetries), "Choose zero or one automatic validation retry.");
    }
}

/// <summary>Resolved task settings, suitable for recording before dispatch. Legacy task defaults fill unspecified overrides.</summary>
public sealed record InferenceExecution(string Model, EffortLevel Effort, int MaxOutputTokens, int ValidationRetries, bool AllowModelFallback);

public enum InferenceFinish
{
    Completed,
    OutputLimit,
    Refused,
    Other,
}

/// <summary>
/// The final received response, independent of task validation. Text can be incomplete when Finish is OutputLimit and must not
/// be accepted as a typed answer. The response ID identifies the returned message, not necessarily the HTTP request, and does
/// not imply the adapter can retrieve it later.
/// </summary>
public sealed record InferenceResponse(string ProviderId, string RequestedModel, string ServedModel, string ResponseId,
    InferenceFinish Finish, string? RawFinishReason, string Text);

/// <summary>
/// Typed inference without a provider-specific key-management API. Implementations honor task.Execution, return AnswerKind
/// failures and usage, and propagate caller cancellation. Unknown capabilities must not be interpreted as supported.
/// </summary>
public interface IInferenceProvider
{
    string ProviderId => "unspecified";

    InferenceCapabilities Capabilities => InferenceCapabilities.Unknown;

    Task<Answer<TAnswer>> AskAsync<TInput, TAnswer>(AssistTask<TInput, TAnswer> task, TInput input, CancellationToken ct = default);
}
