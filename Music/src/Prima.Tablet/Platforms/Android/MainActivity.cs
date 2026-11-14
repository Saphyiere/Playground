using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.OS;
using Android.Views;

namespace Prima.Tablet;

// Rotation is handled here rather than by restarting the activity, so turning the tablet to portrait just resizes the page.
[Activity(Theme = "@style/Maui.SplashTheme", MainLauncher = true, ScreenOrientation = ScreenOrientation.FullUser, ConfigurationChanges = ConfigChanges.ScreenSize | ConfigChanges.Orientation | ConfigChanges.UiMode | ConfigChanges.ScreenLayout | ConfigChanges.SmallestScreenSize | ConfigChanges.Density | ConfigChanges.Keyboard | ConfigChanges.KeyboardHidden | ConfigChanges.Navigation)]
public class MainActivity : MauiAppCompatActivity
{
    // The screen showing, kept by Android with Prima while it's away (OnSaveInstanceState).
    private const string ScreenState = "prima.screen";

    protected override void OnCreate(Bundle? savedInstanceState)
    {
        if (Intent?.GetStringExtra(ConversionService.PieceExtra) is { } piece) ConversionNavigation.Request(piece);
        if (Intent?.GetStringExtra(RoutineServiceHost.OpenExtra) is { } routine) ConversionNavigation.RequestPath("/routine/jobs/" + Uri.EscapeDataString(routine));
        // Android closed Prima while it was out of sight and is bringing it back: to the screen it was on, not Today, nor again to a
        // notification that first opened it (that was seen to then).
        if (savedInstanceState?.GetString(ScreenState) is { } screen) ConversionNavigation.RequestPath(screen);
        base.OnCreate(savedInstanceState);

        // The page draws behind the status and navigation bars, so their icons follow its look: dark on Studio, light on Vinyl.
        TabletLookSetting.DressBars(Window!, TabletLookSetting.Last);
        // The screen stays on while Prima is open, on every screen: the music, the results and the drills are all read from the
        // stand. Android only keeps it on while Prima is in front, so another app or the home screen sleeps as usual.
        Window!.AddFlags(WindowManagerFlags.KeepScreenOn);
    }

    protected override void OnNewIntent(Intent? intent)
    {
        base.OnNewIntent(intent);
        if (intent?.GetStringExtra(ConversionService.PieceExtra) is { } piece) ConversionNavigation.Request(piece);
        if (intent?.GetStringExtra(RoutineServiceHost.OpenExtra) is { } routine) ConversionNavigation.RequestPath("/routine/jobs/" + Uri.EscapeDataString(routine));
    }

    // Android asks for this as Prima goes out of sight. Should it close Prima to free memory before Prima's come back to, it hands
    // this back to OnCreate. The screen is the web view's address within the app: its path, and its query (a routine's activity).
    protected override void OnSaveInstanceState(Bundle outState)
    {
        base.OnSaveInstanceState(outState);
        var page = Microsoft.Maui.Controls.Application.Current?.Windows.FirstOrDefault()?.Page as ContentPage;
        var web = page?.Content?.Handler?.PlatformView as Android.Webkit.WebView;
        // Only a screen of the app's own (https), not the blank page of a web view still starting.
        if (Uri.TryCreate(web?.Url, UriKind.Absolute, out var at) && at.Scheme == Uri.UriSchemeHttps) outState.PutString(ScreenState, at.PathAndQuery);
    }

    protected override void OnStop()
    {
        base.OnStop();
        _ = Task.Run(async () =>
        {
            try { await IPlatformApplication.Current!.Services.GetRequiredService<Prima.UI.Host.PracticeLifecycle>().PauseAsync(); }
            catch (Exception error) { Android.Util.Log.Error("Prima", "Practice checkpoint failed: " + error.Message); }
        });
    }

    // Back in front after OnStop: a routine's activity counts its time again, and goes on where it was.
    protected override void OnRestart()
    {
        base.OnRestart();
        _ = Task.Run(async () =>
        {
            try { await IPlatformApplication.Current!.Services.GetRequiredService<Prima.UI.Host.PracticeLifecycle>().ResumeAsync(); }
            catch (Exception error) { Android.Util.Log.Error("Prima", "Practice resume failed: " + error.Message); }
        });
    }
}
