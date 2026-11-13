using System.Diagnostics;
using Prima.Core.Routines;
using Prima.UI.Routines;
using Prima.UI.Storage;

namespace Prima.UI.Practice;

/// <summary>One player's routine context. Attempt identities are allocated before playing and survive duplicate callbacks.</summary>
public sealed class RoutineSession(RoutineService service)
{
    private readonly Stopwatch clock = new();
    private string submission = Guid.NewGuid().ToString("N");
    private double baseline;
    public RoutineExecution? Execution { get; private set; }
    public RoutineItem? Item => Execution?.Item.Recipe;
    public string? Problem { get; private set; }
    public DateOnly Day => Execution?.Day ?? DateOnly.FromDateTime(DateTime.Now);
    public bool IsRoutine { get; private set; }

    public async Task<bool> LoadAsync(string? id, string? kind = null, string? target = null)
    {
        IsRoutine = !string.IsNullOrWhiteSpace(id);
        if (!IsRoutine) return true;
        if (Execution?.Id == id) return true;
        Execution = await service.GetExecutionAsync(id!);
        if (Execution is null || kind is not null && Item?.Kind != kind || target is not null && Item?.TargetId != target)
        {
            Execution = null;
            Problem = "This routine activity is no longer available. Return to your routine to choose an activity.";
            return false;
        }
        baseline = Execution.ActiveSeconds;
        return true;
    }

    public void Start()
    {
        if (Execution is null || clock.IsRunning) return;
        submission = Guid.NewGuid().ToString("N");
        clock.Restart();
    }

    public async Task<long> RecordAsync(AttemptRecord attempt, int passes = 0)
    {
        if (Execution is null) throw new InvalidOperationException("No routine activity is open.");
        clock.Stop();
        var seconds = baseline + clock.Elapsed.TotalSeconds;
        var id = await service.RecordAttemptAsync(Execution.Id, submission, attempt with { Day = Day }, seconds, passes);
        Execution = await service.GetExecutionAsync(Execution.Id) ?? Execution;
        baseline = Execution.ActiveSeconds;
        clock.Reset();
        return id;
    }

    public async Task PauseAsync()
    {
        if (Execution is null) return;
        clock.Stop();
        await service.CheckpointAsync(Execution.Id, baseline + clock.Elapsed.TotalSeconds, pause: true);
        Execution = await service.GetExecutionAsync(Execution.Id) ?? Execution;
        baseline = Execution.ActiveSeconds;
        clock.Reset();
    }

    public async Task CompleteAsync(bool skipped)
    {
        if (Execution is null) return;
        await PauseAsync();
        await service.CompleteAsync(Execution.Id, skipped);
        Execution = await service.GetExecutionAsync(Execution.Id) ?? Execution;
    }

    public async Task FeedbackAsync(string feedback)
    {
        if (Execution is null) return;
        await service.FeedbackAsync(Execution.Id, feedback);
        Execution = await service.GetExecutionAsync(Execution.Id) ?? Execution;
    }
}
