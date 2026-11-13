using Playground.AI.Assist;
using Prima.Core.Generation;
using Prima.Core.Music;
using Prima.Core.Routines;
using Prima.UI.Host;
using Prima.UI.Routines;
using Prima.UI.Storage;

namespace Prima.UI.Tests;

public class RoutineServiceTests
{
    [Fact]
    public async Task CreatingRequestIsLocalAndRunUsesOnlyExplicitModelWithoutRetryOrFallback()
    {
        await using var host = new ComponentHost();
        var provider = new RoutineProvider();
        var service = new RoutineService(host.Store, provider, new(), new NoBundle());
        var job = await service.CreateGenerationAsync(new(10, "Improve jumps"), null, "chosen-model");
        Assert.Equal(0, provider.Calls);
        Assert.Null(await service.ActiveAsync());
        await service.RunAsync(job.Id, TestContext.Current.CancellationToken);
        var saved = (await service.GetJobAsync(job.Id))!;
        Assert.Equal(1, provider.Calls);
        Assert.Equal("chosen-model", provider.Execution!.Model);
        Assert.Equal(0, provider.Execution.ValidationRetries);
        Assert.False(provider.Execution.AllowModelFallback);
        Assert.Equal(RoutineGenerationState.Ready, saved.State);
        Assert.NotEmpty(saved.Prepared!);
        Assert.Null(await service.ActiveAsync());
        await service.AcceptAsync(job.Id);
        Assert.NotNull(await service.ActiveAsync());
    }

    [Fact]
    public async Task CancellationFencesALateProviderResponse()
    {
        await using var host = new ComponentHost();
        var provider = new RoutineProvider { Hold = new(TaskCreationOptions.RunContinuationsAsynchronously) };
        var service = new RoutineService(host.Store, provider, new(), new NoBundle());
        var job = await service.CreateGenerationAsync(new(10, "Improve jumps"), null, "chosen-model");
        var run = service.RunAsync(job.Id, TestContext.Current.CancellationToken);
        await provider.Called.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        await service.CancelAsync(job.Id);
        provider.Hold.SetResult();
        await run;
        Assert.Equal(RoutineGenerationState.Cancelled, (await service.GetJobAsync(job.Id))!.State);
        Assert.Null(await service.ActiveAsync());
    }

    [Fact]
    public async Task UncertainSourceAndInvalidRecipesNeverBecomeAcceptedExercises()
    {
        await using var host = new ComponentHost();
        var piece = await host.AddPiece();
        var provider = new RoutineProvider();
        var service = new RoutineService(host.Store, provider, new(), new NoBundle());
        var source = await service.PrepareSourceAsync(piece.Id);
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.CreateGenerationAsync(new(10, "Jump", piece.Id), source with { HandsConfirmed = false }, "chosen-model"));
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.CreateGenerationAsync(new(10, "Jump", piece.Id), source with { HasUnresolvedErrors = true }, "chosen-model"));
        Assert.Equal(0, provider.Calls);
        provider.Draft = provider.Draft with { Items = [provider.Draft.Items[0] with { TargetId = "invented-exercise" }] };
        var job = await service.CreateGenerationAsync(new(10, "Jump"), null, "chosen-model");
        await service.RunAsync(job.Id, TestContext.Current.CancellationToken);
        Assert.Equal(RoutineGenerationState.Failed, (await service.GetJobAsync(job.Id))!.State);
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.AcceptAsync(job.Id));
    }

    [Fact]
    public async Task SourceSnapshotsAndAcceptedNotationRemainStableAfterLibraryEdits()
    {
        await using var host = new ComponentHost();
        var piece = await host.AddPiece();
        var service = new RoutineService(host.Store, new RoutineProvider(), new(), new NoBundle());
        var source = await service.PrepareSourceAsync(piece.Id);
        var job = await service.CreateGenerationAsync(new(10, "Jump", piece.Id), source, "chosen-model");
        await service.RunAsync(job.Id, TestContext.Current.CancellationToken);
        var accepted = await service.AcceptAsync(job.Id);
        await host.Store.SavePieceAsync(piece, source.Mei.Replace("pname=\"c\"", "pname=\"d\""), []);
        var changed = await service.PrepareSourceAsync(piece.Id);
        Assert.NotEqual(source.Revision, changed.Revision);
        Assert.Equal(source.Mei, (await service.GetDefinitionAsync(accepted.Revision))!.Source!.Mei);
    }

    [Fact]
    public async Task MidiImportedAsMeiStillRequiresHandConfirmation()
    {
        await using var host = new ComponentHost();
        var piece = await host.AddPiece();
        await host.Store.SavePieceAsync(piece with { File = "piece.mid", Format = "mei" }, await host.Store.GetMeiAsync(piece.Id), []);
        var service = new RoutineService(host.Store, new RoutineProvider(), new(), new NoBundle());
        Assert.False((await service.PrepareSourceAsync(piece.Id)).HandsConfirmed);
        Assert.True((await service.PrepareSourceAsync(piece.Id, confirmHands: true)).HandsConfirmed);
    }

    [Fact]
    public async Task EvidenceSeparatesHandsTemposAndRecordedSteps()
    {
        await using var host = new ComponentHost();
        var service = new RoutineService(host.Store, new RoutineProvider(), new(), new NoBundle());
        var day = DateOnly.FromDateTime(DateTime.Now);
        var first = new AttemptRecord(0, DateTimeOffset.UtcNow, day, "exercise", "in time", "octave-jump", 1, 60, null,
            "{\"key\":\"C major\",\"hands\":\"Right\"}", "[]", null, "{\"inTime\":1,\"stopped\":false}", 1, true, null);
        await host.Store.AddAttemptAsync(first);
        await host.Store.AddAttemptAsync(first with { Settings = "{\"key\":\"C major\",\"hands\":\"Left\"}", Headline = 0.2 });
        await host.Store.AddAttemptAsync(first with { Tempo = 100, Headline = 0.2 });
        await host.Store.AddAttemptAsync(first with { Level = 2, Headline = 0.2 });
        var evidence = await service.EvidenceAsync();
        Assert.Equal(4, evidence.Skills.Count);
        Assert.All(evidence.Skills, s => Assert.Equal(1, s.Attempts));
    }

    [Fact]
    public async Task PieceEvidenceKeepsHandsSeparateAndCountsTroubleAcrossDistinctRuns()
    {
        await using var host = new ComponentHost();
        var service = new RoutineService(host.Store, new RoutineProvider(), new(), new NoBundle());
        var day = DateOnly.FromDateTime(DateTime.Now);
        var loop = System.Text.Json.JsonSerializer.Serialize(new
        {
            hands = "left", notes = 10, rightFirstTime = 8, stopped = true, from = 1, to = 7,
            trouble = new[] { new[] { 7, 3, 0, 0, 0, 0, 0 } },
            passes = Enumerable.Range(0, 10).Select(i => new
            {
                right = 8, notes = 10, inTime = (double?)null,
                trouble = i == 0 ? new[] { new[] { 2, 1, 0, 0, 0, 0, 0 }, new[] { 7, 3, 0, 0, 0, 0, 0 } } : [new[] { 7, 3, 0, 0, 0, 0, 0 }],
            }).ToArray(),
        });
        var first = new AttemptRecord(0, DateTimeOffset.UtcNow, day, "piece", "wait", "sweden", null, null, null, null,
            "[]", null, loop, 0.8, false, null);
        await host.Store.AddAttemptAsync(first);
        await host.Store.AddAttemptAsync(first with
        {
            Measures = System.Text.Json.JsonSerializer.Serialize(new { hands = "left", notes = 10, rightFirstTime = 8, stopped = false, trouble = new[] { new[] { 2, 1, 0, 0, 0, 0, 0 } } }),
        });
        var right = first with
        {
            Measures = System.Text.Json.JsonSerializer.Serialize(new { hands = "right", notes = 10, rightFirstTime = 8, stopped = false, trouble = new[] { new[] { 9, 1, 0, 0, 0, 0, 0 } } }),
        };
        await host.Store.AddAttemptAsync(right);
        await host.Store.AddAttemptAsync(right);

        var evidence = (await service.EvidenceAsync()).Skills;
        Assert.Equal(2, evidence.Count);
        var leftEvidence = Assert.Single(evidence, s => s.Hands == "left");
        var rightEvidence = Assert.Single(evidence, s => s.Hands == "right");
        Assert.Equal("sweden", leftEvidence.PieceId);
        Assert.Equal("sweden", rightEvidence.PieceId);
        Assert.Equal(2, leftEvidence.Attempts);
        Assert.Equal(2, rightEvidence.Attempts);
        Assert.Equal([2], leftEvidence.TroubleBars);
        Assert.Equal([9], rightEvidence.TroubleBars);
        Assert.DoesNotContain(7, leftEvidence.TroubleBars);
    }

    [Fact]
    public async Task PieceTroubleEvidenceIsBoundedAndExcludesZeroWeightBars()
    {
        await using var host = new ComponentHost();
        var service = new RoutineService(host.Store, new RoutineProvider(), new(), new NoBundle());
        var day = DateOnly.FromDateTime(DateTime.Now);
        var measures = System.Text.Json.JsonSerializer.Serialize(new
        {
            hands = "both", notes = 20, rightFirstTime = 10, stopped = false,
            trouble = Enumerable.Range(1, 9).Select(bar => new[] { bar, bar == 9 ? 0 : 1, 0, 0, 0, 0, 0 }).ToArray(),
        });
        var attempt = new AttemptRecord(0, DateTimeOffset.UtcNow, day, "piece", "wait", "sweden", null, null, null, null, "[]", null, measures, 0.5, false, null);
        await host.Store.AddAttemptAsync(attempt);
        await host.Store.AddAttemptAsync(attempt);
        var evidence = Assert.Single((await service.EvidenceAsync()).Skills);
        Assert.Equal([1, 2, 3, 4, 5, 6], evidence.TroubleBars);
        Assert.Equal("both", evidence.Hands);
    }

    [Fact]
    public async Task AdjustmentsExcludeChangedHandsKeysAndTempos()
    {
        await using var host = new ComponentHost();
        var service = new RoutineService(host.Store, new RoutineProvider(), new(), new NoBundle());
        await service.ActiveAsync();
        var job = RoutineStoreTests.ReadyJob();
        await host.Store.CreateRoutineJobAsync(job);
        await host.Store.AcceptRoutineAsync(job.Id);
        var today = DateOnly.FromDateTime(DateTime.Now);
        var key = new Key(0).Name;
        for (var i = 0; i < 3; i++)
        {
            var day = (await service.TodayAsync(today.AddDays(i - 2)))!;
            var execution = await service.BeginExecutionAsync(day.Id, "jump");
            var chosen = System.Text.Json.JsonSerializer.Serialize(new { key, hands = i == 2 ? "Left" : "Right" });
            var attempt = new AttemptRecord(0, DateTimeOffset.UtcNow.AddDays(i - 2), day.Day, "exercise", "in time", "octave-jump", null, 60, null,
                chosen, "[]", null, "{\"inTime\":0.96,\"stopped\":false}", 0.96, true, null);
            await service.RecordAttemptAsync(execution.Id, $"run-{i}", attempt, 30, 1);
        }
        Assert.Empty(await service.AdjustmentsAsync());
        var current = (await service.TodayAsync())!;
        var run = await service.BeginExecutionAsync(current.Id, "jump");
        var wrongKey = new AttemptRecord(0, DateTimeOffset.UtcNow, current.Day, "exercise", "in time", "octave-jump", null, 60, null,
            System.Text.Json.JsonSerializer.Serialize(new { key = new Key(1).Name, hands = "Right" }), "[]", null, "{\"inTime\":0.96}", 0.96, true, null);
        await service.RecordAttemptAsync(run.Id, "different-key", wrongKey, 50, 1);
        await service.RecordAttemptAsync(run.Id, "different-tempo", wrongKey with { Tempo = 90, Settings = System.Text.Json.JsonSerializer.Serialize(new { key, hands = "Right" }) }, 60, 1);
        Assert.Empty(await service.AdjustmentsAsync());
        await service.RecordAttemptAsync(run.Id, "matching", wrongKey with { Settings = System.Text.Json.JsonSerializer.Serialize(new { key, hands = "Right" }) }, 70, 1);
        Assert.True(Assert.Single(await service.AdjustmentsAsync()).SuggestedTempo > 60);
    }

    [Fact]
    public async Task WaitJumpFeedbackCreatesPreparationProposalWithoutChangingAcceptedMusic()
    {
        await using var host = new ComponentHost();
        var service = new RoutineService(host.Store, new RoutineProvider(), new(), new NoBundle());
        await service.ActiveAsync();
        var score = new Score(new Key(0), new Meter(4, 4), [new Bar(1, [new Voice(1,
            [new Notes("a", 0, 480, [new Tone("a1", new Spelled(Step.C, 0, 4))]), new Notes("b", 480, 480, [new Tone("b1", new Spelled(Step.C, 0, 5))]), new Rest("rest", 960, 960)])])]);
        var mei = MeiWriter.Write(score);
        var source = new RoutineSource("source", "Piece", RoutineCompiler.SourceRevision(mei), mei, true);
        var recipe = RoutineStoreTests.ReadyJob().Draft!.Items[0] with { Kind = "generated", TargetId = "jump", Mode = "wait", SourceEventIds = RoutineCompiler.SourceEvents(score).Select(e => e.Id).ToArray() };
        var draft = new RoutineDraft("Routine", "Practice", [recipe]);
        var request = new RoutineRequest(10, "Jumps", source.PieceId);
        var job = RoutineStoreTests.ReadyJob() with { Request = request, Source = source, Draft = draft, Prepared = RoutineCompiler.Prepare(draft, request, source) };
        await host.Store.CreateRoutineJobAsync(job);
        var original = await service.AcceptAsync(job.Id);
        var day = (await service.TodayAsync())!;
        var execution = await service.BeginExecutionAsync(day.Id, recipe.Id);
        await service.FeedbackAsync(execution.Id, "hard");
        var adjustment = Assert.Single(await service.AdjustmentsAsync());
        Assert.Equal(recipe.PreparationBeats + 1, adjustment.SuggestedPreparationBeats);
        var proposal = await service.CreateAdjustmentDraftAsync();
        Assert.NotEqual(original.Items[0].Mei, proposal.Prepared![0].Mei);
        Assert.Equal(original.Items[0].Mei, (await service.ActiveAsync())!.Items[0].Mei);
    }

    private sealed class RoutineProvider : IInferenceProvider
    {
        public string ProviderId => "test";
        public int Calls { get; private set; }
        public InferenceExecution? Execution { get; private set; }
        public TaskCompletionSource Called { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource? Hold { get; set; }
        public RoutineDraft Draft { get; set; } = RoutineStoreTests.ReadyJob().Draft!;

        public async Task<Answer<TAnswer>> AskAsync<TInput, TAnswer>(AssistTask<TInput, TAnswer> task, TInput input, CancellationToken ct = default)
        {
            Calls++;
            Execution = task.Execution;
            Called.TrySetResult();
            if (Hold is not null) await Hold.Task;
            return new(AnswerKind.Answered, (TAnswer)(object)Draft, "", [], Usage.None);
        }
    }
}
