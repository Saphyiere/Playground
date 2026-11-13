namespace Prima.UI.Host;

/// <summary>The host pauses interactive practice before it leaves the foreground; background jobs keep their own lifetime.</summary>
public sealed class PracticeLifecycle
{
    public event Func<Task>? Pausing;

    public async Task PauseAsync()
    {
        foreach (var handler in Pausing?.GetInvocationList().Cast<Func<Task>>() ?? []) await handler();
    }
}
