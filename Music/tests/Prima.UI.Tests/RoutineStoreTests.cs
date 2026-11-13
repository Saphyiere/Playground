using Prima.Core.Generation;
using Prima.Core.Routines;
using Prima.UI.Routines;
using Prima.UI.Storage;

namespace Prima.UI.Tests;

public class RoutineStoreTests
{
    [Fact]
    public async Task AcceptanceIsIdempotentAndNextDayStartsFreshWithoutChangingPins()
    {
        await using var host = new ComponentHost();
        await host.Store.SaveTrackAsync("exercise-routine", new[] { "octave-jump" });
        var job = ReadyJob();
        await host.Store.CreateRoutineJobAsync(job);
        Assert.Null(await host.Store.GetActiveRoutineAsync());

        var accepted = await host.Store.AcceptRoutineAsync(job.Id);
        Assert.Equal(accepted.Revision, (await host.Store.AcceptRoutineAsync(job.Id)).Revision);
        var first = (await host.Store.GetOrCreateRoutineDayAsync(new(2026, 11, 13)))!;
        var execution = await host.Store.BeginRoutineExecutionAsync(first.Id, "jump");
        await host.Store.CompleteRoutineExecutionAsync(execution.Id);
        var again = (await host.Store.GetOrCreateRoutineDayAsync(first.Day))!;
        var next = (await host.Store.GetOrCreateRoutineDayAsync(first.Day.AddDays(1)))!;

        Assert.Equal(first.Id, again.Id);
        Assert.Equal(RoutineItemState.Done, again.Items[0].State);
        Assert.NotEqual(first.Id, next.Id);
        Assert.All(next.Items, i => Assert.Equal(RoutineItemState.NotStarted, i.State));
        Assert.Equal(new[] { "octave-jump" }, await host.Store.GetTrackAsync<string[]>("exercise-routine"));
    }

    [Fact]
    public async Task AttemptsAndTimeAreCreditedOnceWhileKeepingTheirMaterialAndStartingDay()
    {
        await using var host = new ComponentHost();
        var job = ReadyJob();
        await host.Store.CreateRoutineJobAsync(job);
        await host.Store.AcceptRoutineAsync(job.Id);
        var day = (await host.Store.GetOrCreateRoutineDayAsync(new(2026, 11, 13)))!;
        var execution = await host.Store.BeginRoutineExecutionAsync(day.Id, "jump");
        await host.Store.CheckpointRoutineExecutionAsync(execution.Id, 30);
        var attempt = new AttemptRecord(0, DateTimeOffset.UtcNow, day.Day.AddDays(1), "exercise", "in time", "octave-jump", null, 60, null, "{}", "[]", null, "{\"inTime\":0.95}", 0.95, true, null);
        var ids = await Task.WhenAll(host.Store.RecordRoutineAttemptAsync(execution.Id, "same-run", attempt, 60, 1), host.Store.RecordRoutineAttemptAsync(execution.Id, "same-run", attempt, 60, 1));
        await host.Store.CheckpointRoutineExecutionAsync(execution.Id, 55);

        Assert.Equal(ids[0], ids[1]);
        var kept = Assert.Single(await host.Store.GetAttemptsAsync(day.Day));
        Assert.Equal("exercise", kept.Kind);
        Assert.Equal("octave-jump", kept.SetId);
        Assert.Equal(day.Day, kept.Day);
        var saved = (await host.Store.GetRoutineExecutionAsync(execution.Id))!;
        Assert.Equal(60, saved.ActiveSeconds);
        Assert.Equal(1, saved.CompletedPasses);
        Assert.Equal(1, Assert.Single(await host.Store.GetDaysAsync(day.Day)).Minutes);
    }

    [Fact]
    public async Task RecoveryPausesPracticeAndNeverRequeuesAnUncertainPaidRequest()
    {
        await using var host = new ComponentHost();
        var job = ReadyJob();
        await host.Store.CreateRoutineJobAsync(job);
        await host.Store.AcceptRoutineAsync(job.Id);
        var day = (await host.Store.GetOrCreateRoutineDayAsync(new(2026, 11, 13)))!;
        var execution = await host.Store.BeginRoutineExecutionAsync(day.Id, "jump");
        await host.Store.CheckpointRoutineExecutionAsync(execution.Id, 19);
        var pending = job with { Id = "pending", State = RoutineGenerationState.Running, Dispatched = true, Draft = null, Prepared = null };
        await host.Store.CreateRoutineJobAsync(pending);
        await host.Store.RecoverRoutineJobsAsync();

        Assert.Equal(RoutineGenerationState.Interrupted, (await host.Store.GetRoutineJobAsync(pending.Id))!.State);
        Assert.True((await host.Store.GetRoutineJobAsync(pending.Id))!.Dispatched);
        var paused = (await host.Store.GetRoutineExecutionAsync(execution.Id))!;
        Assert.Equal(RoutineItemState.Paused, paused.State);
        Assert.Equal(19, paused.ActiveSeconds);
        Assert.Equal(execution.Id, (await host.Store.BeginRoutineExecutionAsync(day.Id, "jump")).Id);
    }

    [Fact]
    public async Task FeedbackIsMetadataAndReplacementDoesNotRewriteAcceptedHistory()
    {
        await using var host = new ComponentHost();
        var firstJob = ReadyJob();
        await host.Store.CreateRoutineJobAsync(firstJob);
        var original = await host.Store.AcceptRoutineAsync(firstJob.Id);
        var day = (await host.Store.GetOrCreateRoutineDayAsync(new(2026, 11, 13)))!;
        var execution = await host.Store.BeginRoutineExecutionAsync(day.Id, "jump");
        await host.Store.SaveRoutineFeedbackAsync(execution.Id, "hard");
        var changedItem = firstJob.Prepared![0].Recipe with { Tempo = 40 };
        var secondJob = ReadyJob() with { Id = "replacement", Draft = firstJob.Draft! with { Items = [changedItem] }, Prepared = [new(changedItem, "", null)] };
        await host.Store.CreateRoutineJobAsync(secondJob);
        var replacement = await host.Store.AcceptRoutineAsync(secondJob.Id);

        Assert.Empty(await host.Store.GetAttemptsAsync(day.Day));
        Assert.Equal("hard", (await host.Store.GetRoutineExecutionAsync(execution.Id))!.Feedback);
        Assert.Equal(60, (await host.Store.GetRoutineDefinitionAsync(original.Revision))!.Items[0].Recipe.Tempo);
        Assert.Equal(40, replacement.Items[0].Recipe.Tempo);
        Assert.Equal(replacement.Revision, (await host.Store.GetActiveRoutineAsync())!.Revision);
        await host.Store.DeactivateRoutineAsync();
        Assert.Null(await host.Store.GetActiveRoutineAsync());
        Assert.NotNull(await host.Store.GetRoutineDefinitionAsync(original.Revision));
    }

    [Fact]
    public async Task DurationCompletesAnItemOnlyAtACompletedRunBoundary()
    {
        await using var host = new ComponentHost();
        var job = ReadyJob();
        await host.Store.CreateRoutineJobAsync(job);
        await host.Store.AcceptRoutineAsync(job.Id);
        var day = (await host.Store.GetOrCreateRoutineDayAsync(new(2026, 11, 13)))!;
        var execution = await host.Store.BeginRoutineExecutionAsync(day.Id, "jump");
        await host.Store.CheckpointRoutineExecutionAsync(execution.Id, 125);
        Assert.Equal(RoutineItemState.InProgress, (await host.Store.GetRoutineExecutionAsync(execution.Id))!.State);
        var attempt = new AttemptRecord(0, DateTimeOffset.UtcNow, day.Day, "exercise", "wait", "octave-jump", null, null, null, null, "[]", null, "{}", null, false, null);
        await host.Store.RecordRoutineAttemptAsync(execution.Id, "interrupted", attempt, 126);
        Assert.Equal(RoutineItemState.InProgress, (await host.Store.GetRoutineExecutionAsync(execution.Id))!.State);
        await host.Store.RecordRoutineAttemptAsync(execution.Id, "complete", attempt, 130, 1);
        Assert.Equal(RoutineItemState.Done, (await host.Store.GetRoutineExecutionAsync(execution.Id))!.State);
        Assert.Equal(RoutineItemState.Done, (await host.Store.GetRoutineDayAsync(day.Id))!.Items[0].State);
    }

    [Fact]
    public async Task ReceivedDraftIsPreparedLocallyOnRecovery()
    {
        await using var host = new ComponentHost();
        var job = ReadyJob() with { State = RoutineGenerationState.Running, Dispatched = true, Prepared = null };
        await host.Store.CreateRoutineJobAsync(job);
        await host.Store.RecoverRoutineJobsAsync();
        var recovered = (await host.Store.GetRoutineJobAsync(job.Id))!;
        Assert.Equal(RoutineGenerationState.Ready, recovered.State);
        Assert.NotEmpty(recovered.Prepared!);
        Assert.Equal(job.Id, recovered.Id);
        Assert.Null(await host.Store.GetActiveRoutineAsync());
    }

    internal static RoutineGenerationJob ReadyJob()
    {
        var item = new RoutineItem { Id = "jump", Kind = "exercise", TargetId = "octave-jump", Title = "Jump", Purpose = "Land accurately", Hands = HandUse.Right, Mode = "time", Tempo = 60 };
        return new(Guid.NewGuid().ToString("N"), 0, RoutineGenerationState.Ready, DateTimeOffset.UtcNow, new(10, "Practise jumps"),
            new(DateTimeOffset.UtcNow, []), null, "test", "explicit-model", new("Routine", "Practice", [item]), [new(item, "", null)]);
    }
}
