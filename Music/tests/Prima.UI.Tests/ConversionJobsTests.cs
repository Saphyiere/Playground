using System.Security.Cryptography;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;
using Playground.AI.Assist;
using Prima.Core.Music;
using Prima.Core.Transcription;
using Prima.UI.Storage;
using Prima.UI.Transcription;

namespace Prima.UI.Tests;

public class ConversionJobsTests
{
    private static readonly TimeSpan Deadline = TimeSpan.FromSeconds(10);

    private sealed class Provider : IInferenceProvider
    {
        public string ProviderId => "test-provider";
        public InferenceCapabilities Capabilities { get; set; } = InferenceCapabilities.Unknown;
        public List<ChunkToRead> Inputs { get; } = [];
        public TaskCompletionSource<ChunkToRead> Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Func<ChunkToRead, CancellationToken, Task<Answer<SystemReading>>> Handler { get; set; } = (input, _) => Task.FromResult(Good(input));

        public async Task<Answer<TAnswer>> AskAsync<TInput, TAnswer>(AssistTask<TInput, TAnswer> task, TInput input, CancellationToken ct = default)
        {
            var chunk = Assert.IsType<ChunkToRead>(input);
            Inputs.Add(chunk);
            Entered.TrySetResult(chunk);
            return (Answer<TAnswer>)(object)await Handler(chunk, ct);
        }
    }

    private sealed class Renderer : IConversionRenderer
    {
        public int Preflights { get; private set; }
        public string? Unavailable { get; set; }
        public int Crops { get; private set; }
        public int Engravings { get; private set; }
        public Func<CancellationToken, Task<byte[]>> Handler { get; set; } = _ => Task.FromResult(new byte[] { 9, 8, 7 });
        public Task<string?> UnavailableReasonAsync(CancellationToken ct)
        {
            Preflights++;
            return Task.FromResult(Unavailable);
        }
        public Task<byte[]> CropAsync(byte[] pdf, int page, SourceRectangle bounds, int width, CancellationToken ct)
        {
            Crops++;
            return Task.FromResult(new byte[] { 1, 2, 3 });
        }
        public Task<byte[]> EngraveAsync(string mei, int widthPx, CancellationToken ct)
        {
            Engravings++;
            return Handler(ct);
        }
    }

    private sealed class Rig : IAsyncDisposable
    {
        private readonly string root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "test-work", "conversion-jobs"));
        private readonly string folder;
        private readonly string database;
        public Provider Provider { get; } = new();
        public Renderer Renderer { get; } = new();
        public PrimaStore Store { get; }
        public ConversionJobs Jobs { get; }
        public byte[] Pdf { get; } = [7, 2, 5];

        private Rig()
        {
            folder = Path.Combine(root, Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(folder);
            database = Path.Combine(folder, "prima.db");
            Store = new PrimaStore(database);
            Jobs = new ConversionJobs(Store, Provider, Renderer, NullLogger<ConversionJobs>.Instance);
        }

        public static async Task<Rig> Create()
        {
            var rig = new Rig();
            var piece = new PieceRecord("piece", "import", "Synthetic piece", "", null, "pdf", null, null,
                null, null, null, null, null, null, null, null, null, null, 60, null, null, DateTimeOffset.UtcNow, null);
            await rig.Store.SavePieceAsync(piece, null, []);
            return rig;
        }

        public ConversionSource Source(int count = 2) => new(Convert.ToHexStringLower(SHA256.HashData(Pdf)), "source-r1",
            Enumerable.Range(0, count).Select(i => new SourceBar($"b{i}", 1, 1, i, (i + 1).ToString(), new(i / (double)count, 0, 1d / count, 0.5))).ToArray(), true);

        public ConversionOptions Options(int maxBars = 2, int maxRequests = 10, long tokenBudget = 1000) => new(Provider.ProviderId,
            new InferenceSettings(Model: "test-model"), new ChunkLimits(maxBars, 100, 30, 10), maxRequests, tokenBudget);

        public Task<ConversionJob> Job(int bars = 2, int maxBars = 2, int maxRequests = 10, long tokenBudget = 1000) =>
            Jobs.CreateAsync("piece", "Synthetic piece", Pdf, Source(bars), Options(maxBars, maxRequests, tokenBudget));

        public NotationReview Review(int count = 4)
        {
            var source = Source(count);
            var review = NotationCorrections.Create(source);
            var bars = Good(new(1, 1, true, source.Bars.Select(b => b.Id).ToArray(), null)).Value!.Bars;
            return NotationCorrections.Apply(review, new(source.Sha256, source.Revision, review.Current.Revision,
                source.Bars.Select(b => b.Id).ToArray(), source.Bars.Select((b, i) => new BarReplacement(b.Id, bars[i], [])).ToArray(), CorrectionOrigin.Model));
        }

        public async Task<ConversionJob> Saved(string id) => await Store.GetConversionJobAsync(id) ?? throw new InvalidOperationException("Missing job.");

        public async ValueTask DisposeAsync()
        {
            await Store.GetSettingsAsync(); // Drain this store's writes before clearing its exact connection pool.
            using var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = database, ForeignKeys = true }.ToString());
            SqliteConnection.ClearPool(connection);
            var resolved = Path.GetFullPath(folder);
            if (!resolved.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("Invalid test cleanup path.");
            if (Directory.Exists(resolved)) Directory.Delete(resolved, recursive: true);
        }
    }

    private static Answer<SystemReading> Good(ChunkToRead input)
    {
        var bars = input.OwnedSourceBars.Select((_, i) => new ReadBar(i + 1, 0, 4, 4, false, BarEnd.Normal, 0,
        [
            new(1, ReadClef.Treble, [new(1, [new(ReadKind.Note, [new(Step.C, Accidental.None, 4, false, 0)], NoteValue.Whole, 0, 0, false, false, Articulation.None, [], 0, ReadClef.None)])]),
            new(2, ReadClef.Bass, [new(1, [new(ReadKind.BarRest, [], NoteValue.Quarter, 0, 0, false, false, Articulation.None, [], 0, ReadClef.None)])]),
        ])).ToArray();
        return new(AnswerKind.Answered, new(bars, []), "", [], new Usage(5, 20, 0, 0, 0.01m, TimeSpan.Zero));
    }

    [Fact]
    public async Task CreationQueuesBeforeRendererPreflightAndAnUnavailableHostPausesWithoutDispatch()
    {
        await using var rig = await Rig.Create();
        rig.Renderer.Unavailable = "Synthetic renderer unavailable.";

        var job = await rig.Job();

        Assert.Equal(ConversionState.Queued, job.State);
        Assert.Equal(0, rig.Renderer.Preflights);
        await rig.Jobs.RunAsync(job.Id, TestContext.Current.CancellationToken);
        Assert.Equal(ConversionState.Paused, (await rig.Saved(job.Id)).State);
        Assert.Equal(1, rig.Renderer.Preflights);
        Assert.Empty(rig.Provider.Inputs);
    }

    [Theory]
    [InlineData(false, true)]
    [InlineData(true, false)]
    public async Task ExplicitlyUnsupportedProviderCapabilitiesPauseBeforeDispatch(bool images, bool structuredOutput)
    {
        await using var rig = await Rig.Create();
        rig.Provider.Capabilities = new(Images: images, StructuredOutput: structuredOutput);
        var job = await rig.Job();

        await rig.Jobs.RunAsync(job.Id, TestContext.Current.CancellationToken);

        var saved = await rig.Saved(job.Id);
        Assert.Equal(ConversionState.Paused, saved.State);
        Assert.Empty(saved.Attempts);
        Assert.Empty(rig.Provider.Inputs);
        Assert.Equal(0, rig.Renderer.Crops);
    }

    [Fact]
    public async Task UnpricedUsageIsRetainedAsUnknownCostAfterSavingTheCompletedJob()
    {
        await using var rig = await Rig.Create();
        rig.Provider.Handler = (input, _) => Task.FromResult(Good(input) with { Usage = new(1, 2, 0, 0, 0, TimeSpan.Zero) { CostKnown = false } });
        var job = await rig.Job();

        await rig.Jobs.RunAsync(job.Id, TestContext.Current.CancellationToken);

        var saved = await rig.Saved(job.Id);
        Assert.Equal(ConversionState.Ready, saved.State);
        Assert.True(saved.HasUnknownCost);
        Assert.False(saved.HasUncertainRequest);
        Assert.Equal(0, saved.KnownCost);
    }

    [Fact]
    public async Task RecheckOwnsOnlySelectedBarsAndProducesARevisionBoundProposalWithoutApplyingIt()
    {
        await using var rig = await Rig.Create();
        var review = rig.Review();
        review = NotationCorrections.Accept(review, review.Current.Revision, "b0");
        review = NotationCorrections.SetUserFlags(review, review.Current.Revision, "b1", [new("flag", "pitch", "Check this chord")]);
        var job = await rig.Jobs.CreateRecheckAsync("piece", "Synthetic piece", rig.Pdf, review, ["b3", "b1"], "Check the ledger lines", rig.Options());
        await rig.Jobs.RunAsync(job.Id, TestContext.Current.CancellationToken);

        var saved = await rig.Saved(job.Id);
        Assert.True(saved.IsRecheck);
        Assert.Equal(ConversionState.Ready, saved.State);
        Assert.Null(saved.Mei);
        Assert.Equal(["b1", "b3"], saved.Chunks.Select(c => Assert.Single(c.Chunk.BarIds)));
        Assert.Equal([1, 3], rig.Provider.Inputs.Select(i => i.PreviousBar!.Index));
        Assert.Equal([2, 4], rig.Provider.Inputs.Select(i => i.CurrentBar!.Index));
        Assert.All(rig.Provider.Inputs, i => { Assert.False(i.First); Assert.Equal("Check the ledger lines", i.UserHint); });
        var patch = Assert.IsType<BarCorrectionPatch>(saved.ProposedPatch);
        Assert.Equal(review.Current.Revision, patch.BaseNotationRevision);
        Assert.Equal(review.Source.Sha256, patch.SourceSha256);
        Assert.Equal(review.Source.Revision, patch.SourceRevision);
        Assert.Equal(["b1", "b3"], patch.TargetBarIds);
        Assert.Equal([2, 4], patch.Replacements.Select(r => r.Written.Index));
        Assert.Equal(review.Current.Revision, saved.BaseSnapshot!.Revision);
        Assert.True(review.Current.Bars[0].Accepted);
        Assert.Single(review.Current.Bars[1].UserFlags);
        var applied = NotationCorrections.Apply(review, patch);
        Assert.Equal(review.Current.Bars[0], applied.Current.Bars[0]);
        Assert.Equal(review.Current.Bars[2], applied.Current.Bars[2]);
        Assert.Equal(review.Current.Bars[1].UserFlags, applied.Current.Bars[1].UserFlags);
        var changed = NotationCorrections.Accept(review, review.Current.Revision, "b2");
        Assert.Equal(CorrectionRejection.StaleNotation, Assert.Throws<CorrectionRejectedException>(() => NotationCorrections.Apply(changed, patch)).Code);
    }

    [Theory]
    [InlineData("accepted")]
    [InlineData("duplicate")]
    [InlineData("foreign")]
    [InlineData("empty")]
    [InlineData("unread")]
    public async Task RecheckRejectsTargetsThatCannotBeReplaced(string scenario)
    {
        await using var rig = await Rig.Create();
        var review = scenario == "unread" ? NotationCorrections.Create(rig.Source(4)) : rig.Review();
        if (scenario == "accepted") review = NotationCorrections.Accept(review, review.Current.Revision, "b1");
        string[] targets = scenario switch { "duplicate" => ["b1", "b1"], "foreign" => ["foreign"], "empty" => [], _ => ["b1"] };

        var error = await Assert.ThrowsAsync<CorrectionRejectedException>(() =>
            rig.Jobs.CreateRecheckAsync("piece", "Synthetic piece", rig.Pdf, review, targets, "", rig.Options()));

        Assert.Equal(scenario == "accepted" ? CorrectionRejection.ProtectedBar : CorrectionRejection.InvalidTargets, error.Code);
        Assert.Null(await rig.Jobs.LatestAsync("piece"));
        Assert.Empty(rig.Provider.Inputs);
    }

    [Fact]
    public async Task DuplicateRecheckReusesOnlyTheSameTargetsBaseAndHint()
    {
        await using var rig = await Rig.Create();
        var review = rig.Review();
        var first = await rig.Jobs.CreateRecheckAsync("piece", "Synthetic piece", rig.Pdf, review, ["b1"], "ledger", rig.Options());
        var duplicate = await rig.Jobs.CreateRecheckAsync("piece", "Synthetic piece", rig.Pdf, review, ["b1"], "ledger", rig.Options());
        Assert.Equal(first.Id, duplicate.Id);
        await Assert.ThrowsAsync<InvalidOperationException>(() => rig.Jobs.CreateRecheckAsync("piece", "Synthetic piece", rig.Pdf, review, ["b2"], "ledger", rig.Options()));
        await Assert.ThrowsAsync<InvalidOperationException>(() => rig.Jobs.CreateRecheckAsync("piece", "Synthetic piece", rig.Pdf, review, ["b1"], "rhythm", rig.Options()));
        var changed = NotationCorrections.Accept(review, review.Current.Revision, "b0");
        await Assert.ThrowsAsync<InvalidOperationException>(() => rig.Jobs.CreateRecheckAsync("piece", "Synthetic piece", rig.Pdf, changed, ["b1"], "ledger", rig.Options()));
        await Assert.ThrowsAsync<InvalidOperationException>(() => rig.Jobs.CreateAsync("piece", "Synthetic piece", rig.Pdf, review.Source, rig.Options()));
        Assert.Empty(rig.Provider.Inputs);
    }

    [Theory]
    [InlineData(1, 1000)]
    [InlineData(10, 100)]
    public async Task RecheckRetainsTheSameRequestAndTokenGuards(int requests, long tokens)
    {
        await using var rig = await Rig.Create();
        var job = await rig.Jobs.CreateRecheckAsync("piece", "Synthetic piece", rig.Pdf, rig.Review(), ["b1", "b3"], "", rig.Options(maxRequests: requests, tokenBudget: tokens));

        await rig.Jobs.RunAsync(job.Id, TestContext.Current.CancellationToken);

        var saved = await rig.Saved(job.Id);
        Assert.Equal(ConversionState.Paused, saved.State);
        Assert.Null(saved.ProposedPatch);
        Assert.Single(rig.Provider.Inputs);
        Assert.Equal(ConversionPhase.Complete, saved.Chunks[0].Phase);
        Assert.Equal(ConversionPhase.Read, saved.Chunks[1].Phase);
    }

    [Fact]
    public async Task RecheckRestoresItsBaseAndReceivedReadingAfterHostInterruption()
    {
        await using var rig = await Rig.Create();
        var review = rig.Review();
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        rig.Provider.Handler = async (input, _) => { await release.Task; return Good(input); };
        var job = await rig.Jobs.CreateRecheckAsync("piece", "Synthetic piece", rig.Pdf, review, ["b1"], "ledger", rig.Options());
        using var host = new CancellationTokenSource();
        var running = rig.Jobs.RunAsync(job.Id, host.Token);
        await rig.Provider.Entered.Task.WaitAsync(Deadline, TestContext.Current.CancellationToken);
        host.Cancel();
        release.SetResult();
        await running.WaitAsync(Deadline, TestContext.Current.CancellationToken);

        Assert.Equal(ConversionState.Interrupted, (await rig.Saved(job.Id)).State);
        var restarted = new ConversionJobs(rig.Store, rig.Provider, rig.Renderer, NullLogger<ConversionJobs>.Instance);
        await restarted.ResumeAsync(job.Id);
        await restarted.RunAsync(job.Id, TestContext.Current.CancellationToken);

        var saved = await rig.Saved(job.Id);
        Assert.Equal(ConversionState.Ready, saved.State);
        Assert.Equal(review.Current.Revision, saved.ProposedPatch!.BaseNotationRevision);
        Assert.Equal(["b1"], saved.ProposedPatch.TargetBarIds);
        Assert.Single(rig.Provider.Inputs);
        Assert.Single(saved.Attempts);
    }

    [Fact]
    public async Task RecheckRejectsAReplacementThatBreaksATieToAnUntouchedBar()
    {
        await using var rig = await Rig.Create();
        rig.Provider.Handler = (input, _) =>
        {
            var answer = Good(input);
            var bar = answer.Value!.Bars[0];
            var note = bar.Staves[0].Voices[0].Events[0];
            var tied = note with { Pitches = [note.Pitches[0] with { Step = Step.D, Tie = true }] };
            bar = bar with { Staves = [bar.Staves[0] with { Voices = [new(1, [tied])] }, bar.Staves[1]] };
            return Task.FromResult(answer with { Value = new([bar], []) });
        };
        var job = await rig.Jobs.CreateRecheckAsync("piece", "Synthetic piece", rig.Pdf, rig.Review(), ["b1"], "", rig.Options());

        await rig.Jobs.RunAsync(job.Id, TestContext.Current.CancellationToken);

        var saved = await rig.Saved(job.Id);
        Assert.Equal(ConversionState.Paused, saved.State);
        Assert.Null(saved.ProposedPatch);
        Assert.Single(rig.Provider.Inputs);
        Assert.Equal(ConversionPhase.Complete, Assert.Single(saved.Chunks).Phase);
    }

    [Fact]
    public async Task ReadCheckpointSurvivesRenderFailureAndResumeDoesNotRepayTheProvider()
    {
        await using var rig = await Rig.Create();
        var job = await rig.Job();
        ConversionJob? atRender = null;
        rig.Renderer.Handler = async _ =>
        {
            atRender = await rig.Saved(job.Id);
            throw new InvalidOperationException("Synthetic render failure.");
        };

        await rig.Jobs.RunAsync(job.Id, TestContext.Current.CancellationToken);
        Assert.NotNull(atRender);
        Assert.NotNull(Assert.Single(atRender.Chunks).Reading);
        Assert.Equal(ConversionPhase.Render, atRender.Chunks[0].Phase);
        Assert.Equal(ConversionAttemptState.Received, Assert.Single(atRender.Attempts).State);
        Assert.Equal(ConversionState.Paused, (await rig.Saved(job.Id)).State);
        rig.Renderer.Handler = _ => Task.FromResult(new byte[] { 9, 8, 7 });
        await rig.Jobs.ResumeAsync(job.Id);
        await rig.Jobs.RunAsync(job.Id, TestContext.Current.CancellationToken);

        Assert.Equal(ConversionState.Ready, (await rig.Saved(job.Id)).State);
        Assert.Single(rig.Provider.Inputs);
        Assert.Equal(2, rig.Renderer.Engravings);
        Assert.Equal(1, rig.Renderer.Crops);
    }

    [Fact]
    public async Task ExplicitCancelFencesAnAnswerFromAProviderThatIgnoresCancellation()
    {
        await using var rig = await Rig.Create();
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        rig.Provider.Handler = async (input, _) => { await release.Task; return Good(input); };
        var job = await rig.Job();
        var running = rig.Jobs.RunAsync(job.Id, TestContext.Current.CancellationToken);
        await rig.Provider.Entered.Task.WaitAsync(Deadline, TestContext.Current.CancellationToken);
        await rig.Jobs.CancelAsync(job.Id);
        var cancelled = await rig.Saved(job.Id);
        release.SetResult();
        await running.WaitAsync(Deadline, TestContext.Current.CancellationToken);

        var saved = await rig.Saved(job.Id);
        Assert.Equal(ConversionState.Cancelled, saved.State);
        Assert.Equal(cancelled.Revision, saved.Revision);
        Assert.Equal(ConversionAttemptState.Uncertain, Assert.Single(saved.Attempts).State);
        Assert.Null(saved.Chunks[0].Reading);
        Assert.Equal(0, rig.Renderer.Engravings);
    }

    [Fact]
    public async Task HostCancellationIsInterruptedAndRetainsTheUnknownChargeReservation()
    {
        await using var rig = await Rig.Create();
        rig.Provider.Handler = async (input, ct) => { await Task.Delay(Timeout.Infinite, ct); return Good(input); };
        var job = await rig.Job(tokenBudget: 100);
        using var host = new CancellationTokenSource();
        var running = rig.Jobs.RunAsync(job.Id, host.Token);
        await rig.Provider.Entered.Task.WaitAsync(Deadline, TestContext.Current.CancellationToken);
        host.Cancel();
        await running.WaitAsync(Deadline, TestContext.Current.CancellationToken);

        var interrupted = await rig.Saved(job.Id);
        Assert.Equal(ConversionState.Interrupted, interrupted.State);
        Assert.True(interrupted.HasUncertainRequest);
        Assert.Equal(100, Assert.Single(interrupted.Attempts).ReservedOutputTokens);
        await Assert.ThrowsAsync<InvalidOperationException>(() => rig.Jobs.ResumeAsync(job.Id));
        rig.Provider.Handler = (input, _) => Task.FromResult(Good(input));
        await Assert.ThrowsAsync<InvalidOperationException>(() => rig.Jobs.ResumeAsync(job.Id, acknowledgeUncertainRequest: true));

        Assert.Equal(ConversionState.Interrupted, (await rig.Saved(job.Id)).State);
        Assert.Single(rig.Provider.Inputs); // The uncertain first request still owns the entire token allowance.
    }

    [Theory]
    [InlineData(1, 1000)]
    [InlineData(10, 100)]
    public async Task ExhaustedResumeRequiresExplicitPermissionAndAllowsOnlyOneMoreRequest(int requests, long tokens)
    {
        await using var rig = await Rig.Create();
        var job = await rig.Job(bars: 3, maxBars: 1, maxRequests: requests, tokenBudget: tokens);
        await rig.Jobs.RunAsync(job.Id, TestContext.Current.CancellationToken);
        var paused = await rig.Saved(job.Id);
        Assert.True(paused.NeedsRequestAllowance);
        await Assert.ThrowsAsync<InvalidOperationException>(() => rig.Jobs.ResumeAsync(job.Id));
        Assert.Equal(paused.Revision, (await rig.Saved(job.Id)).Revision);

        var resumed = await rig.Jobs.ResumeAsync(job.Id, allowAdditionalRequest: true);
        Assert.False(resumed.NeedsRequestAllowance);
        Assert.Equal(2, resumed.Options.MaxRequests);
        rig.Provider.Handler = (input, _) => Task.FromResult(Good(input) with { Usage = Usage.None });
        await rig.Jobs.RunAsync(job.Id, TestContext.Current.CancellationToken);

        var saved = await rig.Saved(job.Id);
        Assert.Equal(ConversionState.Paused, saved.State);
        Assert.True(saved.NeedsRequestAllowance);
        Assert.Equal(2, rig.Provider.Inputs.Count);
        Assert.Equal(ConversionPhase.Read, saved.Chunks[2].Phase);
    }

    [Fact]
    public async Task HostCancellationAfterAnIgnoredTokenStillCheckpointsTheReceivedReadingAndStops()
    {
        await using var rig = await Rig.Create();
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        rig.Provider.Handler = async (input, _) => { await release.Task; return Good(input); };
        var job = await rig.Job();
        using var host = new CancellationTokenSource();
        var running = rig.Jobs.RunAsync(job.Id, host.Token);
        await rig.Provider.Entered.Task.WaitAsync(Deadline, TestContext.Current.CancellationToken);
        host.Cancel();
        release.SetResult();
        await running.WaitAsync(Deadline, TestContext.Current.CancellationToken);

        var interrupted = await rig.Saved(job.Id);
        Assert.Equal(ConversionState.Interrupted, interrupted.State);
        Assert.NotNull(interrupted.Chunks[0].Reading);
        Assert.Equal(ConversionAttemptState.Received, interrupted.Attempts[0].State);
        Assert.Equal(0, rig.Renderer.Engravings);
        await rig.Jobs.ResumeAsync(job.Id);
        await rig.Jobs.RunAsync(job.Id, TestContext.Current.CancellationToken);
        Assert.Equal(ConversionState.Ready, (await rig.Saved(job.Id)).State);
        Assert.Single(rig.Provider.Inputs);
    }

    [Fact]
    public async Task DuplicateCreateAndRunShareOneSavedJobAndOneProviderExecution()
    {
        await using var rig = await Rig.Create();
        var created = await Task.WhenAll(rig.Job(), rig.Job());
        Assert.Equal(created[0].Id, created[1].Id);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        rig.Provider.Handler = async (input, _) => { await release.Task; return Good(input); };
        var first = rig.Jobs.RunAsync(created[0].Id, TestContext.Current.CancellationToken);
        await rig.Provider.Entered.Task.WaitAsync(Deadline, TestContext.Current.CancellationToken);
        var second = rig.Jobs.RunAsync(created[1].Id, TestContext.Current.CancellationToken);
        Assert.False(second.IsCompleted);
        release.SetResult();
        await Task.WhenAll(first, second).WaitAsync(Deadline, TestContext.Current.CancellationToken);

        Assert.Single(rig.Provider.Inputs);
        Assert.Equal(ConversionState.Ready, (await rig.Saved(created[0].Id)).State);
    }

    [Fact]
    public async Task ANewConfirmationCannotSilentlyReuseDifferentExecutionSettings()
    {
        await using var rig = await Rig.Create();
        var original = await rig.Job();
        var options = rig.Options() with { Reading = new(Model: "different-model") };

        await Assert.ThrowsAsync<InvalidOperationException>(() => rig.Jobs.CreateAsync("piece", "Synthetic piece", rig.Pdf, rig.Source(), options));
        Assert.Equal(original.Id, (await rig.Jobs.LatestAsync("piece"))!.Id);
        Assert.Empty(rig.Provider.Inputs);
    }

    [Fact]
    public async Task ANewSourceCannotSilentlyAttachToThePendingJob()
    {
        await using var rig = await Rig.Create();
        var original = await rig.Job();
        byte[] changedPdf = [4, 5, 6];
        var changedSource = rig.Source() with { Revision = "source-r2", Sha256 = Convert.ToHexStringLower(SHA256.HashData(changedPdf)) };

        await Assert.ThrowsAsync<InvalidOperationException>(() => rig.Jobs.CreateAsync("piece", "Synthetic piece", changedPdf, changedSource, rig.Options()));

        var saved = await rig.Jobs.LatestAsync("piece");
        Assert.Equal(original.Id, saved!.Id);
        Assert.Equal(original.Source.Sha256, saved.Source.Sha256);
        Assert.Empty(rig.Provider.Inputs);
    }

    [Fact]
    public async Task OutputLimitSplitsOnlyThePendingRangeAndPreservesCompletedNeighbours()
    {
        await using var rig = await Rig.Create();
        var job = await rig.Job(bars: 4);
        var completedId = job.Chunks[0].Chunk.Id;
        rig.Provider.Handler = (input, _) => Task.FromResult(input.OwnedSourceBars.SequenceEqual(["b2", "b3"])
            ? new Answer<SystemReading>(AnswerKind.TooLong, null, "Output limit.", [], new Usage(5, 100, 0, 0, 0.02m, TimeSpan.Zero)) : Good(input));

        await rig.Jobs.RunAsync(job.Id, TestContext.Current.CancellationToken);

        var saved = await rig.Saved(job.Id);
        Assert.Equal(ConversionState.Ready, saved.State);
        Assert.Equal(completedId, saved.Chunks[0].Chunk.Id);
        Assert.Equal(3, saved.Chunks.Count);
        Assert.Equal(["b0,b1", "b2,b3", "b2", "b3"], rig.Provider.Inputs.Select(i => string.Join(',', i.OwnedSourceBars)));
        Assert.Equal(["b0", "b1", "b2", "b3"], saved.Chunks.SelectMany(c => c.Chunk.BarIds));
        Assert.Equal(4, saved.Attempts.Count);
    }

    [Fact]
    public async Task DenseReceivedOutputSplitsLaterPendingRangesBeforePayingForAnOverlargeRequest()
    {
        await using var rig = await Rig.Create();
        var job = await rig.Job(bars: 4);
        var completedId = job.Chunks[0].Chunk.Id;
        rig.Provider.Handler = (input, _) => Task.FromResult(Good(input) with
        {
            Usage = new Usage(5, input.OwnedSourceBars.Count * 40, 0, 0, 0.01m, TimeSpan.Zero),
        });

        await rig.Jobs.RunAsync(job.Id, TestContext.Current.CancellationToken);

        var saved = await rig.Saved(job.Id);
        Assert.Equal(ConversionState.Ready, saved.State);
        Assert.Equal(completedId, saved.Chunks[0].Chunk.Id);
        Assert.Equal(["b0,b1", "b2", "b3"], rig.Provider.Inputs.Select(i => string.Join(',', i.OwnedSourceBars)));
        Assert.Equal(3, saved.Attempts.Count);
        Assert.Equal(3, rig.Renderer.Crops);
        Assert.Equal(["b0", "b1", "b2", "b3"], saved.Chunks.SelectMany(c => c.Chunk.BarIds));
    }

    [Theory]
    [InlineData(1, 1000)]
    [InlineData(10, 100)]
    public async Task RequestAndTokenBudgetsPauseBeforeAnotherDispatch(int requests, long tokens)
    {
        await using var rig = await Rig.Create();
        var job = await rig.Job(maxBars: 1, maxRequests: requests, tokenBudget: tokens);

        await rig.Jobs.RunAsync(job.Id, TestContext.Current.CancellationToken);

        var saved = await rig.Saved(job.Id);
        Assert.Equal(ConversionState.Paused, saved.State);
        Assert.Single(rig.Provider.Inputs);
        Assert.Equal(ConversionPhase.Complete, saved.Chunks[0].Phase);
        Assert.Equal(ConversionPhase.Read, saved.Chunks[1].Phase);
    }

    [Fact]
    public async Task ExplicitInterruptionPersistsBeforeFencingALateAnswer()
    {
        await using var rig = await Rig.Create();
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        rig.Provider.Handler = async (input, _) => { await release.Task; return Good(input); };
        var job = await rig.Job();
        var running = rig.Jobs.RunAsync(job.Id, TestContext.Current.CancellationToken);
        await rig.Provider.Entered.Task.WaitAsync(Deadline, TestContext.Current.CancellationToken);
        await rig.Jobs.InterruptAsync(job.Id, "Host deadline.");
        await Assert.ThrowsAsync<InvalidOperationException>(() => rig.Jobs.ResumeAsync(job.Id, acknowledgeUncertainRequest: true));
        release.SetResult();
        await running.WaitAsync(Deadline, TestContext.Current.CancellationToken);

        var saved = await rig.Saved(job.Id);
        Assert.Equal(ConversionState.Interrupted, saved.State);
        Assert.True(saved.HasUncertainRequest);
        Assert.Null(saved.Chunks[0].Reading);
        Assert.Equal(0, rig.Renderer.Engravings);
    }

    [Fact]
    public async Task InterruptionAfterCompletionDoesNotUndoTheReadyState()
    {
        await using var rig = await Rig.Create();
        var job = await rig.Job();
        await rig.Jobs.RunAsync(job.Id, TestContext.Current.CancellationToken);
        var completed = await rig.Saved(job.Id);

        await rig.Jobs.InterruptAsync(job.Id, "Late host stop.");

        var saved = await rig.Saved(job.Id);
        Assert.Equal(ConversionState.Ready, saved.State);
        Assert.Equal(completed.Revision, saved.Revision);
    }

    [Fact]
    public async Task RecentJobsReturnsOnlyTheNewestJobForEachPiece()
    {
        await using var rig = await Rig.Create();
        var first = await rig.Job();
        await rig.Jobs.CancelAsync(first.Id);
        var second = await rig.Job();

        var recent = await rig.Jobs.RecentAsync();

        Assert.Equal(second.Id, Assert.Single(recent).Id);
        Assert.Equal(ConversionState.Queued, recent[0].State);
    }

    [Fact]
    public async Task StartupRecoveryMarksAnUnfinishedDispatchUncertainWithoutRunningIt()
    {
        await using var rig = await Rig.Create();
        var job = await rig.Job();
        var running = job with
        {
            State = ConversionState.Running, Revision = job.Revision + 1,
            Attempts = [new("attempt", job.Chunks[0].Chunk.Id, DateTimeOffset.UtcNow, ConversionAttemptState.Dispatched, Usage.None, 100)],
        };
        Assert.True(await rig.Store.SaveConversionJobAsync(running, job.Revision));

        var recovered = new ConversionJobs(rig.Store, rig.Provider, rig.Renderer, NullLogger<ConversionJobs>.Instance);
        var saved = await recovered.LatestAsync(job.Piece);

        Assert.Equal(ConversionState.Interrupted, saved!.State);
        Assert.Equal(ConversionAttemptState.Uncertain, Assert.Single(saved.Attempts).State);
        Assert.Equal(100, saved.Attempts[0].ReservedOutputTokens);
        Assert.Empty(rig.Provider.Inputs);
    }

    [Fact]
    public async Task HostCancellationDuringAnIgnoringRendererCannotPublishReady()
    {
        await using var rig = await Rig.Create();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        rig.Renderer.Handler = async _ => { entered.SetResult(); await release.Task; return [9, 8, 7]; };
        var job = await rig.Job();
        using var host = new CancellationTokenSource();
        var running = rig.Jobs.RunAsync(job.Id, host.Token);
        await entered.Task.WaitAsync(Deadline, TestContext.Current.CancellationToken);
        host.Cancel();
        release.SetResult();
        await running.WaitAsync(Deadline, TestContext.Current.CancellationToken);

        var saved = await rig.Saved(job.Id);
        Assert.Equal(ConversionState.Interrupted, saved.State);
        Assert.Equal(ConversionPhase.Render, saved.Chunks[0].Phase);
        Assert.NotNull(saved.Chunks[0].Reading);
        Assert.Null(saved.Mei);
    }

    [Fact]
    public async Task AnObserverCannotMutateTheWorkersSourceOrAnotherObserversSnapshot()
    {
        await using var rig = await Rig.Create();
        var observed = new List<string>();
        rig.Jobs.Changed += state => ((IList<SourceBar>)state.Source.Bars)[0] = state.Source.Bars[0] with { Id = "mutated" };
        rig.Jobs.Changed += state => observed.Add(state.Source.Bars[0].Id);
        var job = await rig.Job();

        await rig.Jobs.RunAsync(job.Id, TestContext.Current.CancellationToken);

        Assert.Equal(ConversionState.Ready, (await rig.Saved(job.Id)).State);
        Assert.NotEmpty(observed);
        Assert.All(observed, id => Assert.Equal("b0", id));
    }

    [Fact]
    public async Task AContradictoryTieAcrossChunksCannotBecomeReady()
    {
        await using var rig = await Rig.Create();
        var job = await rig.Job(maxBars: 1);
        rig.Provider.Handler = (input, _) =>
        {
            var answer = Good(input);
            var bar = answer.Value!.Bars[0];
            var note = bar.Staves[0].Voices[0].Events[0];
            var pitch = note.Pitches[0] with { Step = input.First ? Step.C : Step.D, Tie = input.First };
            bar = bar with { Staves = [bar.Staves[0] with { Voices = [new(1, [note with { Pitches = [pitch] }])] }, bar.Staves[1]] };
            return Task.FromResult(answer with { Value = new([bar], []) });
        };

        await rig.Jobs.RunAsync(job.Id, TestContext.Current.CancellationToken);

        var saved = await rig.Saved(job.Id);
        Assert.Equal(ConversionState.Paused, saved.State);
        Assert.Null(saved.Mei);
        Assert.All(saved.Chunks, c => Assert.Equal(ConversionPhase.Complete, c.Phase));
        Assert.Equal(2, rig.Provider.Inputs.Count);
    }
}
