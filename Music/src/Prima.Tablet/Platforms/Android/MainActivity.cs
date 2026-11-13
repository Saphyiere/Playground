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
    protected override void OnCreate(Bundle? savedInstanceState)
    {
        if (Intent?.GetStringExtra(ConversionService.PieceExtra) is { } piece) ConversionNavigation.Request(piece);
        if (Intent?.GetStringExtra(RoutineServiceHost.OpenExtra) is { } routine) ConversionNavigation.RequestPath("/routine/jobs/" + Uri.EscapeDataString(routine));
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

    protected override void OnStop()
    {
        base.OnStop();
        _ = Task.Run(async () =>
        {
            try { await IPlatformApplication.Current!.Services.GetRequiredService<Prima.UI.Host.PracticeLifecycle>().PauseAsync(); }
            catch (Exception error) { Android.Util.Log.Error("Prima", "Practice checkpoint failed: " + error.Message); }
        });
    }
}
