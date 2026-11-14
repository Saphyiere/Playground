using System.Diagnostics;
using Prima.Core.Routines;
using Prima.UI.Routines;
using Prima.UI.Storage;

namespace Prima.UI.Practice;

/// <summary>
/// One player's routine context. Attempt identities are allocated before playing and survive duplicate callbacks. Its clock is the
/// time on the activity: it counts from the activity opening, played or idle (a run stopped to read the music counts too), and
/// stops only while the activity is left or Prima is put away.
/// </summary>
public sealed class RoutineSession(RoutineService service)
{
    private readonly Stopwatch clock = new();
    private string submission = Guid.NewGuid().ToString("N");
    // A run under way, with its submission, from Start until it's recorded: the next Start after that is a new run.
    private bool started;
    private double baseline;
    public RoutineExecution? Execution { get; private set; }
    public RoutineItem? Item => Execution?.Item.Recipe;
    public string? Problem { get; private set; }
    public DateOnly Day => Execution?.Day ?? DateOnly.FromDateTime(DateTime.Now);
    public bool IsRoutine { get; private set; }

    /// <summary>The time on the activity today, in seconds: what was kept before, and the clock's since.</summary>
    public double Seconds => baseline + clock.Elapsed.TotalSeconds;

    /// <summary>Whether the clock is counting: the activity open and Prima in front.</summary>
    public bool Counting => clock.IsRunning;

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
        clock.Restart();
        return true;
    }

    /// <summary>A run starting: a submission of its own, and the clock counting if it had stopped.</summary>
    public void Start()
    {
        if (Execution is null) return;
        Resume();
        if (started) return;
        (submission, started) = (Guid.NewGuid().ToString("N"), true);
    }

    /// <summary>The clock counting again: the activity in front again after a pause.</summary>
    public void Resume()
    {
        if (Execution is not null && !clock.IsRunning) clock.Start();
    }

    public async Task<long> RecordAsync(AttemptRecord attempt, int passes = 0)
    {
        if (Execution is null) throw new InvalidOperationException("No routine activity is open.");
        // The time so far is kept with the run, and the clock counts on.
        var seconds = Seconds;
        (baseline, started) = (seconds, false);
        if (clock.IsRunning) clock.Restart();
        else clock.Reset();
        var id = await service.RecordAttemptAsync(Execution.Id, submission, attempt with { Day = Day }, seconds, passes);
        Execution = await service.GetExecutionAsync(Execution.Id) ?? Execution;
        return id;
    }

    public async Task PauseAsync()
    {
        if (Execution is null) return;
        clock.Stop();
        await service.CheckpointAsync(Execution.Id, Seconds, pause: true);
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
