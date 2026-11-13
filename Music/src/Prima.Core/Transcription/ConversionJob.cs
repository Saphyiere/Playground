using Playground.AI.Assist;

namespace Prima.Core.Transcription;

public enum ConversionState { Queued, Running, Paused, Interrupted, Cancelled, Ready }
public enum ConversionPhase { Read, Validate, Render, Complete }
public enum ConversionAttemptState { Dispatched, Received, Uncertain }

public sealed record ConversionOptions(string Provider, InferenceSettings Reading, ChunkLimits Chunks, int MaxRequests = 100, long OutputTokenBudget = 96_000)
{
    /// <summary>Reserves one bounded binary split tree per planned range, with no automatic retry allowance.</summary>
    public static ConversionOptions ForSource(string provider, InferenceSettings reading, ConversionSource source,
        ChunkLimits? limits = null, IReadOnlyList<string>? targetBarIds = null)
    {
        var chunks = limits ?? new ChunkLimits();
        if (targetBarIds is not null)
        {
            if (targetBarIds.Count == 0 || targetBarIds.Distinct(StringComparer.Ordinal).Count() != targetBarIds.Count ||
                targetBarIds.Any(id => !source.Bars.Any(b => b.Id == id))) throw new ArgumentException("Choose distinct source bars.", nameof(targetBarIds));
            chunks = chunks with { MaxBars = 1 };
        }
        var planned = ConversionChunks.Plan(source, chunks).Where(c => targetBarIds is null || targetBarIds.Contains(c.BarIds[0])).ToArray();
        var bars = planned.Sum(c => c.BarIds.Count);
        var requests = checked(2 * bars - planned.Length);
        return new(provider, reading, chunks, requests, checked((long)requests * chunks.TargetOutputTokens));
    }
}

public sealed record ConversionAttempt(string Id, string Chunk, DateTimeOffset Started, ConversionAttemptState State,
    Usage Usage, int ReservedOutputTokens, AnswerKind? Outcome = null, InferenceResponse? Response = null);

public sealed record ConversionChunkProgress(ConversionChunk Chunk, ConversionPhase Phase, SystemReading? Reading = null,
    string? Crop = null, string? Engraving = null, string? Problem = null);

/// <summary>A versioned local checkpoint. Only an explicit owner action may produce Cancelled.</summary>
public sealed record ConversionJob(int Version, string Id, string Piece, string Title, ConversionSource Source, string Pdf,
    ConversionOptions Options, ConversionState State, int Revision, DateTimeOffset Updated,
    IReadOnlyList<ConversionChunkProgress> Chunks, IReadOnlyList<ConversionAttempt> Attempts, string? Problem = null, string? Mei = null,
    NotationSnapshot? BaseSnapshot = null, IReadOnlyList<string>? TargetBarIds = null, string? UserHint = null, BarCorrectionPatch? ProposedPatch = null)
{
    public bool IsRecheck => BaseSnapshot is not null;
    public decimal KnownCost => Attempts.Sum(a => a.Usage.Cost);
    public bool HasUnknownCost => Attempts.Any(a => a.State != ConversionAttemptState.Received || !a.Usage.CostKnown);
    public long UsedOrReservedOutputTokens => Attempts.Sum(a => a.State == ConversionAttemptState.Received ? (long)a.Usage.OutputTokens : a.ReservedOutputTokens);
    public bool NeedsRequestAllowance => Chunks.Any(c => c.Phase == ConversionPhase.Read) &&
        (Attempts.Count >= Options.MaxRequests || UsedOrReservedOutputTokens + Options.Chunks.TargetOutputTokens > Options.OutputTokenBudget);
    public bool HasUncertainRequest => Attempts.Any(a => a.State == ConversionAttemptState.Uncertain);
    public bool Active => State is ConversionState.Queued or ConversionState.Running;
}

public sealed record ChunkToRead(int Page, int System, bool First, IReadOnlyList<string> OwnedSourceBars, ReadBar? PreviousBar,
    ReadBar? CurrentBar = null, string? UserHint = null);

/// <summary>A bounded reading. Context is read-only and never owns any of this response's bars.</summary>
public sealed class ReadChunk(byte[] png, InferenceSettings settings) : AssistTask<ChunkToRead, SystemReading>(settings)
{
    public override string Name => "prima.read-chunk";
    public override int Version => 2;
    public override int MaxTokens => 12_000;
    public override string Instructions => new ReadSystem([]).Instructions + """

        This image is a bounded range of complete source bars, not necessarily a whole printed system.
        Return exactly one bar for each OwnedSourceBars entry, in that order, with local indexes 1 through the owned count.
        PreviousBar is read-only context from immediately before the image: use its ending clefs, key and meter where the
        image does not print a replacement. Do not emit PreviousBar. First is true only at the start of the selected source.
        Inspect low ledger-line chords note by note; preserve the written letter and octave rather than guessing a chord.
        When CurrentBar is supplied, recheck that single owned bar against its source image. It is a prior reading, not
        ground truth. UserHint identifies the user's concern; verify it against the image and preserve correct notation.
        Return the complete replacement bar, and describe any remaining uncertainty in Doubts without guessing certainty.
        """;
    public override IReadOnlyList<Attachment> Attachments(ChunkToRead input) => [Attachment.Png(png, "Complete source bars to read")];
    public override IReadOnlyList<string> Check(ChunkToRead input, SystemReading answer)
    {
        var problems = ReadingChecks.Problems(answer, input.First).ToList();
        if (answer.Bars?.Count != input.OwnedSourceBars.Count) problems.Add("The returned bars do not cover the exact owned source range.");
        return problems;
    }
}
