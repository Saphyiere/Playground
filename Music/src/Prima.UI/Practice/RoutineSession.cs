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

    /// <summary>The activity open, in whichever routine.</summary>
    public RoutineItem? Activity => Execution?.Item.Recipe;

    /// <summary>
    /// What the activity's recipe asks to be played (a key, hands, a mode, a tempo, a step), where that's what's played: in Claude's
    /// routines. An exercise or a drill in a routine of your own asks for nothing: it's played as you last left it, and what's
    /// changed playing it is kept as it would be anywhere.
    /// </summary>
    public RoutineItem? Item => Fixed ? Activity : null;

    /// <summary>Whether the recipe is what's played and your own settings and progress are left as they were: Claude's routines.</summary>
    public bool Fixed { get; private set; }

    /// <summary>
    /// One of RCM's technique, as the day has it (its key, form and hands, and RCM's tempo as the target), where that's the
    /// activity: played as RCM asks, its own tempo kept as a drill's is anywhere.
    /// </summary>
    public RcmToday? Rcm { get; private set; }
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
        if (Execution is null || kind is not null && Activity?.Kind != kind || target is not null && Activity?.TargetId != target)
        {
            Execution = null;
            Problem = "This routine activity is no longer available. Return to your routine to choose an activity.";
            return false;
        }
        var definition = await service.GetDefinitionAsync(Execution.Revision);
        Fixed = definition is null || !OwnRoutines.AsLeft(definition, Execution.Item.Recipe);
        Rcm = definition is null ? null : OwnRoutines.Rcm(definition, Execution.Item.Recipe, Execution.Day);
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
