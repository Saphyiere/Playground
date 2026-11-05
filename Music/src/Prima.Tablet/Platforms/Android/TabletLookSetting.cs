using AndroidX.Core.View;
using Prima.UI.Looks;
using Prima.UI.Storage;

namespace Prima.Tablet;

/// <summary>
/// Dresses the tablet around the page for the look: the status and navigation bar icons (dark on Studio, light on Vinyl) and the
/// background shown while the page loads. The look's id is also kept in Preferences, which the next start can read before the
/// app's own settings are open.
/// </summary>
public sealed class TabletLookSetting(PrimaStore store) : LookSetting(store)
{
    private const string PreferenceKey = "look";

    /// <summary>The look the tablet wore last, for the first moments of the next start.</summary>
    public static Look Last => Look.FromId(Preferences.Default.Get<string?>(PreferenceKey, null));

    /// <summary>Dark bar icons on a light page, light ones on a dark page.</summary>
    public static void DressBars(Android.Views.Window window, Look look)
    {
        var bars = WindowCompat.GetInsetsController(window, window.DecorView)!;
        bars.AppearanceLightStatusBars = !look.Dark;
        bars.AppearanceLightNavigationBars = !look.Dark;
    }

    protected override void ApplyToShell(Look look)
    {
        Preferences.Default.Set(PreferenceKey, look.Id);
        MainThread.BeginInvokeOnMainThread(() =>
        {
            if (Platform.CurrentActivity?.Window is { } window) DressBars(window, look);
            if (Application.Current?.Windows.FirstOrDefault()?.Page is MainPage page) page.ShowLook(look);
        });
    }
}
