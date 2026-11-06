using Android.App;
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
        base.OnCreate(savedInstanceState);

        // The page draws behind the status and navigation bars, so their icons follow its look: dark on Studio, light on Vinyl.
        TabletLookSetting.DressBars(Window!, TabletLookSetting.Last);
        // The screen stays on while Prima is open, on every screen: the music, the results and the drills are all read from the
        // stand. Android only keeps it on while Prima is in front, so another app or the home screen sleeps as usual.
        Window!.AddFlags(WindowManagerFlags.KeepScreenOn);
    }
}
