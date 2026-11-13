using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Playground.AI.Assist;
using Prima.Core.Transcription;
using Prima.UI.Storage;

namespace Prima.UI.Transcription;

/// <summary>Owns conversion work independently of any page. Every externally visible state has already been saved.</summary>
public sealed class ConversionJobs(PrimaStore store, IInferenceProvider provider, IConversionRenderer renderer, ILogger<ConversionJobs> logger)
{
    private readonly Task recovered = store.RecoverConversionJobsAsync();
    private readonly ConcurrentDictionary<string, ActiveRun> active = new();
    private readonly SemaphoreSlim runLane = new(1);
    public event Action<ConversionJob>? Changed;

    public async Task<ConversionJob?> LatestAsync(string piece)
    {
        await recovered;
        return await store.GetLatestConversionJobAsync(piece);
    }

    public async Task<IReadOnlyList<ConversionJob>> RecentAsync()
    {
        await recovered;
        return await store.GetRecentConversionJobsAsync();
    }

    public Task<ConversionJob> CreateAsync(string piece, string title, byte[] pdf, ConversionSource source, ConversionOptions options) =>
        CreateAsync(piece, title, pdf, source, options, null, null, null);

    public Task<ConversionJob> CreateRecheckAsync(string piece, string title, byte[] pdf, NotationReview review,
        IReadOnlyList<string> targets, string hint, ConversionOptions options)
    {
        var frozen = NotationCorrections.Restore(review);
        var selected = targets.ToArray();
        if (selected.Length == 0 || selected.Distinct(StringComparer.Ordinal).Count() != selected.Length ||
            selected.Any(id => !frozen.Current.Bars.Any(b => b.SourceBarId == id && b.Written is not null)))
            throw new CorrectionRejectedException(CorrectionRejection.InvalidTargets, "Choose distinct, already read bars from this source.");
        if (frozen.Current.Bars.Any(b => b.Accepted && selected.Contains(b.SourceBarId)))
            throw new CorrectionRejectedException(CorrectionRejection.ProtectedBar, "Reopen an accepted bar before requesting a recheck.");
        selected = frozen.Source.Bars.Where(b => selected.Contains(b.Id)).Select(b => b.Id).ToArray();
        return CreateAsync(piece, title, pdf, frozen.Source, options, frozen.Current, selected, hint.Trim());
    }

    private async Task<ConversionJob> CreateAsync(string piece, string title, byte[] pdf, ConversionSource source, ConversionOptions options,
        NotationSnapshot? snapshot, IReadOnlyList<string>? targets, string? hint)
    {
        pdf = pdf.ToArray();
        source = source with { Bars = source.Bars.ToArray() };
        await recovered;
        if (options.Provider != provider.ProviderId) throw new InvalidOperationException("The selected inference provider is unavailable.");
        if (!source.Sha256.Equals(Convert.ToHexStringLower(SHA256.HashData(pdf)), StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("The bar map belongs to a different PDF.");
        if (options.MaxRequests < 1 || options.OutputTokenBudget < options.Chunks.TargetOutputTokens) throw new ArgumentOutOfRangeException(nameof(options));
        if (string.IsNullOrWhiteSpace(options.Reading.Model)) throw new ArgumentException("Choose an explicit model before starting a conversion.", nameof(options));
        var settings = options.Reading with { ValidationRetries = 0, AllowModelFallback = false, MaxOutputTokens = options.Chunks.TargetOutputTokens };
        _ = new ReadChunk([], settings);
        var limits = snapshot is null ? options.Chunks : options.Chunks with { MaxBars = 1 };
        var chunks = ConversionChunks.Plan(source, limits).Where(c => targets is null || targets.Contains(c.BarIds[0]))
            .Select(c => new ConversionChunkProgress(c, ConversionPhase.Read)).ToArray();
        await store.SaveConversionSourceAsync(piece, source);
        var artifact = await store.SaveConversionArtifactAsync(pdf);
        var job = new ConversionJob(1, Guid.NewGuid().ToString("N"), piece, title, source, artifact, options with { Reading = settings },
            ConversionState.Queued, 0, DateTimeOffset.UtcNow, chunks, [], BaseSnapshot: snapshot, TargetBarIds: targets, UserHint: hint);
        var saved = await store.CreateConversionJobAsync(job);
        if (saved.Source.Revision != source.Revision || !saved.Source.Sha256.Equals(source.Sha256, StringComparison.OrdinalIgnoreCase) ||
            !saved.Source.Bars.SequenceEqual(source.Bars) || saved.Options != job.Options || saved.IsRecheck != job.IsRecheck ||
            saved.BaseSnapshot?.Revision != snapshot?.Revision || !(saved.TargetBarIds ?? []).SequenceEqual(targets ?? []) || saved.UserHint != hint)
            throw new InvalidOperationException("This piece already has an unfinished conversion with different source boundaries or execution settings.");
        Publish(saved);
        return saved;
    }

    public async Task<ConversionJob> ResumeAsync(string id, bool acknowledgeUncertainRequest = false, bool allowAdditionalRequest = false)
    {
        await recovered;
        var job = await store.GetConversionJobAsync(id) ?? throw new InvalidOperationException("The conversion was not found.");
        if (job.State is not (ConversionState.Interrupted or ConversionState.Paused)) return job;
        if (active.ContainsKey(id)) throw new InvalidOperationException("The previous execution is still stopping. Resume after it stops.");
        if (job.HasUncertainRequest && !acknowledgeUncertainRequest) throw new InvalidOperationException("The interrupted request may have incurred a charge. Confirm before sending it again.");
        if (job.NeedsRequestAllowance)
        {
            if (!allowAdditionalRequest) throw new InvalidOperationException("The request allowance is exhausted. Authorize one additional request to resume.");
            job = job with { Options = job.Options with
            {
                MaxRequests = checked(job.Attempts.Count + 1),
                OutputTokenBudget = Math.Max(job.Options.OutputTokenBudget, checked(job.UsedOrReservedOutputTokens + job.Options.Chunks.TargetOutputTokens)),
            } };
        }
        return await CommitAsync(job with { State = ConversionState.Queued, Problem = null });
    }

    public async Task CancelAsync(string id)
    {
        await recovered;
        var job = await store.CancelConversionJobAsync(id);
        await CancelActiveAsync(id);
        if (job is not null) Publish(job);
    }

    public async Task InterruptAsync(string id, string reason)
    {
        await recovered;
        var job = await store.InterruptConversionJobAsync(id, reason);
        await CancelActiveAsync(id);
        if (job is not null) Publish(job);
    }

    private async Task CancelActiveAsync(string id)
    {
        if (!active.TryGetValue(id, out var run)) return;
        try { await run.Cancellation.CancelAsync(); }
        catch (ObjectDisposedException) { } // The completed run can leave the dictionary immediately before cancellation.
    }

    public async Task RunAsync(string id, CancellationToken hostStopped = default)
    {
        await recovered;
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(hostStopped);
        var run = new ActiveRun(cancellation, new(TaskCreationOptions.RunContinuationsAsynchronously));
        var owner = active.GetOrAdd(id, run);
        if (!ReferenceEquals(owner, run)) { await owner.Finished.Task.WaitAsync(hostStopped); return; }
        var ct = cancellation.Token;
        var entered = false;
        ConversionJob? job = null;
        try
        {
            await runLane.WaitAsync(ct);
            entered = true;
            job = await store.GetConversionJobAsync(id) ?? throw new InvalidOperationException("The conversion was not found.");
            if (job.State != ConversionState.Queued) return;
            if (job.Options.Provider != provider.ProviderId) throw new InvalidOperationException("The saved inference provider is unavailable.");
            if (provider.Capabilities.Images is false) throw new InvalidOperationException("The selected provider adapter does not support music images.");
            if (provider.Capabilities.StructuredOutput is false) throw new InvalidOperationException("The selected provider adapter does not support structured notation output.");
            if (await renderer.UnavailableReasonAsync(ct) is { } unavailable) throw new InvalidOperationException(unavailable);
            job = await CommitAsync(job with { State = ConversionState.Running });
            ClaudeLog.Piece.Value = job.Piece;
            var pdf = await store.GetConversionArtifactAsync(job.Pdf, ct);
            while (job.Chunks.Any(c => c.Phase != ConversionPhase.Complete))
            {
                ct.ThrowIfCancellationRequested();
                var index = job.Chunks.ToList().FindIndex(c => c.Phase != ConversionPhase.Complete);
                var chunk = job.Chunks[index];
                var bars = chunk.Chunk.BarIds.Select(id => job.Source.Bars.Single(b => b.Id == id)).ToArray();
                if (chunk.Phase == ConversionPhase.Read)
                {
                    // Received output includes reasoning overhead. Use the densest completed range and reserve 25% plus
                    // the configured context allowance; only untouched ranges may shrink, and no new requests are added here.
                    var observedPerBar = job.Chunks.Where(c => c.Phase == ConversionPhase.Complete).SelectMany(c => job.Attempts
                        .Where(a => a.Chunk == c.Chunk.Id && a.State == ConversionAttemptState.Received && a.Outcome == AnswerKind.Answered)
                        .Select(a => a.Usage.OutputTokens / (decimal)c.Chunk.BarIds.Count)).DefaultIfEmpty(0).Max();
                    var projected = Math.Ceiling(observedPerBar * chunk.Chunk.BarIds.Count * 1.25m) + job.Options.Chunks.ContextTokens;
                    if (chunk.Chunk.BarIds.Count > 1 && projected > job.Options.Chunks.TargetOutputTokens)
                    {
                        var split = ConversionChunks.Split(chunk.Chunk).Select(c => new ConversionChunkProgress(c, ConversionPhase.Read));
                        job = await CommitAsync(job with { Chunks = [.. job.Chunks.Take(index), .. split, .. job.Chunks.Skip(index + 1)] });
                        continue;
                    }
                    if (job.Attempts.Count >= job.Options.MaxRequests) throw new InvalidOperationException("The conversion request allowance is reached. Completed bars are saved.");
                    if (job.UsedOrReservedOutputTokens + job.Options.Chunks.TargetOutputTokens > job.Options.OutputTokenBudget)
                        throw new InvalidOperationException("The conversion output-token allowance is reached. Completed bars are saved.");
                    if (chunk.Crop is null)
                    {
                        var left = bars.Min(b => b.Bounds.X);
                        var top = bars.Min(b => b.Bounds.Y);
                        var right = bars.Max(b => b.Bounds.X + b.Bounds.Width);
                        var bottom = bars.Max(b => b.Bounds.Y + b.Bounds.Height);
                        var png = await renderer.CropAsync(pdf, bars[0].Page, new(left, top, right - left, bottom - top), 2576, ct);
                        chunk = chunk with { Crop = await store.SaveConversionArtifactAsync(png, ct) };
                        job = await CommitAsync(job with { Chunks = job.Chunks.Select((c, i) => i == index ? chunk : c).ToArray() });
                    }
                    var image = await store.GetConversionArtifactAsync(chunk.Crop, ct);
                    var before = job.IsRecheck
                        ? job.BaseSnapshot!.Bars.FirstOrDefault(b => b.SourceBarId == chunk.Chunk.ContextBefore)?.Written
                        : index == 0 ? null : job.Chunks[index - 1].Reading?.Bars.LastOrDefault();
                    var current = job.BaseSnapshot?.Bars.Single(b => b.SourceBarId == bars[0].Id).Written;
                    var attempt = new ConversionAttempt(Guid.NewGuid().ToString("N"), chunk.Chunk.Id, DateTimeOffset.UtcNow,
                        ConversionAttemptState.Dispatched, Usage.None, job.Options.Reading.MaxOutputTokens!.Value);
                    job = await CommitAsync(job with { Attempts = [.. job.Attempts, attempt] });
                    var answer = await provider.AskAsync(new ReadChunk(image, job.Options.Reading),
                        new ChunkToRead(bars[0].Page, bars[0].System, bars[0].Order == 0, chunk.Chunk.BarIds, before, current, job.UserHint), ct);
                    attempt = attempt with { State = ConversionAttemptState.Received, Usage = answer.Usage, Outcome = answer.Kind, Response = answer.Response };
                    var attempts = job.Attempts.Select(a => a.Id == attempt.Id ? attempt : a).ToArray();
                    if (!answer.Ok)
                    {
                        job = await CommitAsync(job with { Attempts = attempts });
                        ct.ThrowIfCancellationRequested();
                        if (answer.Kind == AnswerKind.TooLong && chunk.Chunk.BarIds.Count > 1)
                        {
                            var split = ConversionChunks.Split(chunk.Chunk).Select(c => new ConversionChunkProgress(c, ConversionPhase.Read));
                            job = await CommitAsync(job with { Chunks = [.. job.Chunks.Take(index), .. split, .. job.Chunks.Skip(index + 1)] });
                            continue;
                        }
                        throw new InvalidOperationException(answer.Summary);
                    }
                    chunk = chunk with { Phase = ConversionPhase.Validate, Reading = answer.Value };
                    job = await CommitAsync(job with { Attempts = attempts, Chunks = job.Chunks.Select((c, i) => i == index ? chunk : c).ToArray() });
                    ct.ThrowIfCancellationRequested();
                }
                if (chunk.Phase == ConversionPhase.Validate)
                {
                    if (chunk.Reading is null || chunk.Reading.Bars.Count != chunk.Chunk.BarIds.Count) throw new InvalidOperationException("A reading does not cover its exact source bars.");
                    var problems = ReadingChecks.Problems(chunk.Reading, bars[0].Order == 0);
                    if (problems.Count > 0) throw new InvalidOperationException(string.Join(" ", problems));
                    chunk = chunk with { Phase = ConversionPhase.Render };
                    job = await CommitAsync(job with { Chunks = job.Chunks.Select((c, i) => i == index ? chunk : c).ToArray() });
                }
                if (chunk.Phase == ConversionPhase.Render)
                {
                    var score = ReadingScore.Build([chunk.Reading!], job.Title).Score;
                    var png = await renderer.EngraveAsync(ReadingScore.Mei(score), 1600, ct);
                    ct.ThrowIfCancellationRequested();
                    chunk = chunk with { Phase = ConversionPhase.Complete, Engraving = await store.SaveConversionArtifactAsync(png, ct) };
                    job = await CommitAsync(job with { Chunks = job.Chunks.Select((c, i) => i == index ? chunk : c).ToArray() });
                }
            }
            var owned = job.Chunks.SelectMany(c => c.Chunk.BarIds).ToArray();
            var expected = job.IsRecheck ? job.TargetBarIds! : job.Source.Bars.Select(b => b.Id);
            if (!owned.SequenceEqual(expected)) throw new InvalidOperationException("The completed chunks do not cover the requested source bars exactly once.");
            ct.ThrowIfCancellationRequested();
            if (job.IsRecheck)
            {
                var replacements = job.Chunks.SelectMany(c => c.Reading!.Bars.Select((bar, i) => new BarReplacement(c.Chunk.BarIds[i],
                    bar with { Index = job.BaseSnapshot!.Bars.Single(b => b.SourceBarId == c.Chunk.BarIds[i]).Written!.Index },
                    c.Reading.Doubts.Where(d => d.Bar == i + 1).Select(d => new ModelBarDoubt(d.Staff is 1 or 2 ? d.Staff : null, d.Note)).ToArray()))).ToArray();
                var patch = new BarCorrectionPatch(job.Source.Sha256, job.Source.Revision, job.BaseSnapshot!.Revision,
                    job.TargetBarIds!, replacements, CorrectionOrigin.Model);
                var baseline = NotationCorrections.Restore(new(job.Source, job.BaseSnapshot, [], []));
                _ = NotationCorrections.Apply(baseline, patch); // Validate the full written context without applying the proposal to saved notation.
                job = await CommitAsync(job with { State = ConversionState.Ready, ProposedPatch = patch, Problem = null });
            }
            else
            {
                var joined = job.Chunks.SelectMany(c => c.Reading!.Bars).Select((bar, i) => bar with { Index = i + 1 }).ToArray();
                var seamProblems = ReadingChecks.Problems(new SystemReading(joined, []), first: true);
                if (seamProblems.Count > 0) throw new InvalidOperationException(string.Join(" ", seamProblems));
                var complete = ReadingScore.Build(job.Chunks.Select(c => c.Reading!).ToArray(), job.Title).Score;
                job = await CommitAsync(job with { State = ConversionState.Ready, Mei = ReadingScore.Mei(complete), Problem = null });
            }
        }
        catch (ChangedCheckpointException) { }
        catch (Exception error)
        {
            var latest = await store.GetConversionJobAsync(id);
            if (latest is not null && latest.State is not (ConversionState.Cancelled or ConversionState.Ready))
            {
                var interrupted = error is OperationCanceledException;
                try
                {
                    await CommitAsync(latest with
                    {
                        State = interrupted ? ConversionState.Interrupted : ConversionState.Paused, Problem = error.Message,
                        Attempts = latest.Attempts.Select(a => a.State == ConversionAttemptState.Dispatched ? a with { State = ConversionAttemptState.Uncertain } : a).ToArray()
                    });
                }
                catch (ChangedCheckpointException) { }
            }
        }
        finally
        {
            ClaudeLog.Piece.Value = null;
            active.TryRemove(id, out _);
            if (entered) runLane.Release();
            run.Finished.TrySetResult();
        }
    }

    private async Task<ConversionJob> CommitAsync(ConversionJob job)
    {
        var updated = job with { Revision = job.Revision + 1, Updated = DateTimeOffset.UtcNow };
        if (!await store.SaveConversionJobAsync(updated, job.Revision)) throw new ChangedCheckpointException();
        Publish(updated);
        return updated;
    }

    private void Publish(ConversionJob job)
    {
        foreach (var subscriber in Changed?.GetInvocationList() ?? [])
        {
            try
            {
                var snapshot = JsonSerializer.Deserialize<ConversionJob>(JsonSerializer.Serialize(job, AssistSchema.Json), AssistSchema.Json)!;
                ((Action<ConversionJob>)subscriber)(snapshot);
            }
            catch (Exception error) { logger.LogError(error, "A conversion observer failed for job {JobId}", job.Id); }
        }
    }

    private sealed class ChangedCheckpointException : Exception;
    private sealed record ActiveRun(CancellationTokenSource Cancellation, TaskCompletionSource Finished);
}
