namespace Playground.AI.Assist;

/// <summary>How hard Claude thinks before it answers: more is slower and costs more, and pays off on hard questions.</summary>
public enum EffortLevel
{
    Low,
    Medium,
    High,
    XHigh,
    Max,
}

/// <summary>
/// One kind of question an app asks Claude: what it sends (<typeparamref name="TInput"/>), what comes back
/// (<typeparamref name="TAnswer"/>), what Claude is told, and the checks its answer must pass. The answer's JSON schema is
/// made from <typeparamref name="TAnswer"/> (see <see cref="AssistSchema"/>), and Claude's reply is held to it while it
/// writes. Describe the records' properties with <c>[Description]</c>: those are the field notes Claude reads.
/// </summary>
public abstract class AssistTask<TInput, TAnswer>
{
    /// <summary>Its name in requests and the log, like "prima.read-system".</summary>
    public abstract string Name { get; }

    /// <summary>Raised whenever the instructions or either record change, so the log tells old answers from new.</summary>
    public abstract int Version { get; }

    /// <summary>What Claude is told to do, after the shared preamble every task gets.</summary>
    public abstract string Instructions { get; }

    public virtual string Model => ClaudeModels.Opus;

    public virtual EffortLevel Effort => EffortLevel.High;

    /// <summary>The most Claude may write, thinking included. Running out is <see cref="AnswerKind.TooLong"/>.</summary>
    public virtual int MaxTokens => 32_000;

    /// <summary>Images or PDFs sent before the input, like a system cut from a printed page.</summary>
    public virtual IReadOnlyList<Attachment> Attachments(TInput input) => [];

    /// <summary>
    /// What the schema can't say, like an exercise that doesn't exist or a bar that doesn't add up: each problem as a sentence,
    /// none when the answer is fine. An answer with problems is asked for once more, with them listed.
    /// </summary>
    public virtual IReadOnlyList<string> Check(TInput input, TAnswer answer) => [];
}

public enum AttachmentKind
{
    Png,
    Jpeg,
    Pdf,
}

/// <summary>An image or PDF for Claude to look at. A caption, when there is one, is sent just before it, to say what it is.</summary>
public sealed record Attachment(AttachmentKind Kind, byte[] Data, string Caption = "")
{
    public static Attachment Png(byte[] data, string caption = "") => new(AttachmentKind.Png, data, caption);

    public static Attachment Jpeg(byte[] data, string caption = "") => new(AttachmentKind.Jpeg, data, caption);

    public static Attachment Pdf(byte[] data, string caption = "") => new(AttachmentKind.Pdf, data, caption);
}
