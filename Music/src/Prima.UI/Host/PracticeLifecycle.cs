namespace Prima.UI.Host;

/// <summary>
/// The host pauses interactive practice before it leaves the foreground, and says when it's back in front so what was paused can
/// go on; background jobs keep their own lifetime.
/// </summary>
public sealed class PracticeLifecycle
{
    public event Func<Task>? Pausing;
    public event Func<Task>? Resuming;

    public async Task PauseAsync()
    {
        foreach (var handler in Pausing?.GetInvocationList().Cast<Func<Task>>() ?? []) await handler();
    }

    public async Task ResumeAsync()
    {
        foreach (var handler in Resuming?.GetInvocationList().Cast<Func<Task>>() ?? []) await handler();
    }
}
