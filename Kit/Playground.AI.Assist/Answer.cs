using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;

namespace Playground.AI.Assist;

public enum ReplyStatus
{
    Answered,
    NeedMore,
    Cannot,
}

/// <summary>
/// What Claude writes for every task: the same envelope, with the task's own answer inside. Its JSON schema is what the
/// API holds Claude's reply to, so it always parses.
/// </summary>
public sealed record Reply<TAnswer>(
    [property: Description("answered when you answered; needMore when the input isn't enough to answer well; cannot when the task can't be done with it.")]
    ReplyStatus Status,
    [property: Description("One or two plain sentences the app shows on screen.")]
    string Summary,
    [property: Description("What the owner should know: data you didn't trust and what you did with it, or what's missing for needMore. Empty when there's nothing.")]
    IReadOnlyList<string> Notes,
    [property: Description("The task's answer when status is answered; null otherwise.")]
    TAnswer? Answer);

/// <summary>How a question went.</summary>
public enum AnswerKind
{
    /// <summary>Claude answered, and the answer passed the task's checks.</summary>
    Answered,

    /// <summary>Claude said the input wasn't enough; the notes say what's missing.</summary>
    NeedMore,

    /// <summary>Claude said the task can't be done with this input.</summary>
    Cannot,

    /// <summary>The answer failed the task's checks and its permitted validation retries; the notes list the problems.</summary>
    CheckFailed,

    /// <summary>Claude declined, and no other model answered in its place.</summary>
    Refused,

    /// <summary>The answer ran past the task's token limit.</summary>
    TooLong,

    /// <summary>There's no API key on this device.</summary>
    NoKey,

    /// <summary>The API didn't accept the key: wrong, disabled, expired or deleted.</summary>
    KeyRejected,

    /// <summary>The month's API credits are used up; requests work again when the next month's arrive.</summary>
    OutOfCredit,

    /// <summary>The workspace's spend limit is reached.</summary>
    SpendLimit,

    /// <summary>Claude is busy or had a problem of its own; trying again later usually works.</summary>
    Busy,

    /// <summary>The API couldn't be reached.</summary>
    Offline,

    /// <summary>Anything else: the summary says what.</summary>
    Failed,
}

/// <summary>What a question used: tokens, what they cost at the API's prices, and how long it took.</summary>
public sealed record Usage(long InputTokens, long OutputTokens, long CacheReadTokens, long CacheWriteTokens, decimal Cost, TimeSpan Took)
{
    /// <summary>False when some received usage has no known price; Cost then contains only the priced subtotal.</summary>
    public bool CostKnown { get; init; } = true;
    public static readonly Usage None = new(0, 0, 0, 0, 0, TimeSpan.Zero);

    public Usage Plus(Usage other) => new(InputTokens + other.InputTokens, OutputTokens + other.OutputTokens,
        CacheReadTokens + other.CacheReadTokens, CacheWriteTokens + other.CacheWriteTokens, Cost + other.Cost, Took + other.Took)
        { CostKnown = CostKnown && other.CostKnown };
}

/// <summary>
/// What an app gets back from a question, always this shape. <see cref="Ok"/> is the one thing to check: when it's true,
/// <see cref="Value"/> holds the task's answer; when it's false, <see cref="Summary"/> says why in a sentence for the screen.
/// </summary>
public sealed record Answer<TAnswer>(AnswerKind Kind, TAnswer? Value, string Summary, IReadOnlyList<string> Notes, Usage Usage)
{
    /// <summary>Present after a final provider response was received; absent for failures before a complete response.</summary>
    public InferenceResponse? Response { get; init; }

    [MemberNotNullWhen(true, nameof(Value))]
    public bool Ok => Kind == AnswerKind.Answered && Value is not null;
}
