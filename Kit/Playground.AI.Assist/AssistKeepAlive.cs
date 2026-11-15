namespace Playground.AI.Assist;

/// <summary>
/// What keeps the app going while Claude answers, so a question isn't lost when the app is left partway: another app opened, the
/// phone locked, or the app swiped off the recent apps. <see cref="Claude"/> holds it for each ask, from its request to its answer
/// (a second ask for failed checks included), and the app is let go when the last ask is done. On Android it's a foreground
/// service with a quiet notification (Kit/Playground.AI.Assist.Android); a host without one registered keeps nothing going.
/// </summary>
public interface IAssistKeepAlive
{
    /// <summary>
    /// Keeps the app going until the handle is disposed. <paramref name="doing"/> says what for, in the notification, like "Looking
    /// back at your workout" (the task's <see cref="AssistTask{TInput, TAnswer}.Doing"/>). Never throws: when the device won't keep
    /// the app going, the ask goes on as it would without.
    /// </summary>
    IDisposable Hold(string doing);
}

/// <summary>Keeps nothing going: the default, for a host that can't (the preview hosts) or needn't.</summary>
public sealed class NoKeepAlive : IAssistKeepAlive
{
    public static readonly NoKeepAlive Instance = new();

    public IDisposable Hold(string doing) => Nothing.Instance;

    sealed class Nothing : IDisposable
    {
        public static readonly Nothing Instance = new();

        public void Dispose()
        {
        }
    }
}
