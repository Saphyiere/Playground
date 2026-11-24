using Microsoft.JSInterop;
using Prima.UI.Host;

namespace Prima.UI.Practice;

/// <summary>
/// Full screen, the same on every screen that plays music: the bar and the music alone, the rail, the screen's head and whatever
/// else it marks <c>full-hide</c> gone (data-full on &lt;html&gt;), and the tablet's own bars hidden too. One for the app: the play
/// bar's key turns it on and off, and a screen that goes full screen by itself (a piece's Practice) asks it here, so there's one
/// way it's done. Whoever turns it on turns it off when its screen goes.
/// </summary>
public sealed class FullScreen(IJSRuntime js, ISystemBars bars) : IAsyncDisposable
{
    private IJSObjectReference? module;

    /// <summary>Whether the page is full screen now.</summary>
    public bool On { get; private set; }

    /// <summary>When it goes on or off, for a bar showing its key.</summary>
    public event Action? Changed;

    /// <summary>The page full screen or not: the page changes first, then the tablet's bars, so the rail goes as the key is pressed.</summary>
    public async Task SetAsync(bool on)
    {
        if (On == on) return;

        On = on;
        try
        {
            module ??= await js.InvokeAsync<IJSObjectReference>("import", "./_content/Prima.UI/turns.js");
            await module.InvokeVoidAsync("fullScreen", on);
        }
        catch (Exception e) when (e is JSDisconnectedException or JSException or InvalidOperationException or TaskCanceledException)
        {
        }
        bars.Show(!on);
        Changed?.Invoke();
    }

    public async ValueTask DisposeAsync()
    {
        try
        {
            if (module is not null) await module.DisposeAsync();
        }
        catch (Exception e) when (e is JSDisconnectedException or JSException)
        {
        }
    }
}
