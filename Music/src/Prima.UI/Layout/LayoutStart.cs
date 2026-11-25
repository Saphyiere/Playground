using Playground.AI.Assist;
using Prima.UI.Host;
using Prima.UI.Looks;
using Prima.UI.Storage;

namespace Prima.UI.Layout;

/// <summary>
/// What the layout reads before its first screen: the look, the settings, and a Claude key copied over USB (Settings says what
/// became of it). Once while Prima is open: every screen builds the layout again, and reading them again before drawing left the
/// tablet a frame or more of bare ground between screens. A start that failed is tried again by the next screen.
/// </summary>
public sealed class LayoutStart(LookSetting look, PrimaStore store, PrimaSettings settings, ClaudeKeyFile keyFile, IAssistKeyStore keys)
{
    private Task? started;

    /// <summary>Done once the first screen can be drawn; done at once for every screen after it.</summary>
    public Task ReadyAsync() =>
        started is { IsFaulted: false, IsCanceled: false } ? started : started = Task.WhenAll(look.LoadAsync(), store.LoadAsync(settings), keyFile.ImportAsync(keys));
}
