using Android.Views;
using Java.Lang;
using Prima.UI.Host;

namespace Prima.Tablet;

/// <summary>The system's monotonic clock, the one MIDI timestamps and audio timestamps use.</summary>
public sealed class AndroidClock : IClock
{
    public long NowNs => JavaSystem.NanoTime();
}

/// <summary>Keeps the screen on while a practice screen is open.</summary>
public sealed class AndroidScreen : IScreen
{
    public void KeepOn(bool on) => MainThread.BeginInvokeOnMainThread(() =>
    {
        var window = Platform.CurrentActivity?.Window;
        if (on) window?.AddFlags(WindowManagerFlags.KeepScreenOn);
        else window?.ClearFlags(WindowManagerFlags.KeepScreenOn);
    });
}
