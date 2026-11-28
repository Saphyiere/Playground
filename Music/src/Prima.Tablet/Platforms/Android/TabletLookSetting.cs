using AndroidX.Core.View;
using Prima.UI.Looks;
using Prima.UI.Storage;

namespace Prima.Tablet;

/// <summary>
/// Dresses the tablet around the page for the look: the status and navigation bar icons (dark on Porcelain, light on Night), the
/// background shown while the page loads, and Android's splash at the next start (Porcelain's or Night's card with a staff). The
/// look's id is also kept in Preferences, which the next start can read before the app's own settings are open.
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
            ChooseSplash(look);
        });
    }

    // Android keeps the splash chosen here for every start after this one (from Android 13); before one's chosen, the app's own.
    private static void ChooseSplash(Look look)
    {
        if (!OperatingSystem.IsAndroidVersionAtLeast(33) || Platform.CurrentActivity is not { } activity) return;
        var splash = look == Look.Night ? Resource.Style.Prima_Splash_Night : Resource.Style.Prima_Splash_Porcelain;
        try
        {
            activity.SplashScreen.SetSplashScreenTheme(splash);
        }
        catch (Java.Lang.Exception error)
        {
            Android.Util.Log.Warn("PrimaLook", "Could not choose the splash: " + error.Message);
        }
    }
}
