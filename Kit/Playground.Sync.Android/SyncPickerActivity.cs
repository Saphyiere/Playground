using Android.App;
using Android.Content;
using Android.OS;

namespace Playground.Sync;

/// <summary>
/// A see-through screen of the app's own that opens a system picker and passes on what was picked, so the Kit needs nothing from the
/// app's own activity. It goes over the app as the picker opens, and away as it closes; turned while the picker is open, it's made
/// again and still hears the answer.
/// </summary>
[Activity(Theme = "@android:style/Theme.Translucent.NoTitleBar", Exported = false)]
public sealed class SyncPickerActivity : Activity
{
    private const int Picking = 7;
    private static TaskCompletionSource<global::Android.Net.Uri?>? waiting;
    private static Intent? picker;

    /// <summary>Opens <paramref name="intent"/>, a system picker, over the app: what was picked, or null.</summary>
    internal static Task<global::Android.Net.Uri?> PickAsync(Context context, Intent intent)
    {
        waiting?.TrySetResult(null);
        var pick = new TaskCompletionSource<global::Android.Net.Uri?>(TaskCreationOptions.RunContinuationsAsynchronously);
        (waiting, picker) = (pick, intent);
        context.StartActivity(new Intent(context, typeof(SyncPickerActivity)).AddFlags(ActivityFlags.NewTask));
        return pick.Task;
    }

    protected override void OnCreate(Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);
        // Made again after turning: the picker is still open, and answers this one.
        if (savedInstanceState is not null) return;
        if (picker is not { } intent)
        {
            Finish();
            return;
        }

        picker = null;
        try
        {
            StartActivityForResult(intent, Picking);
        }
        catch (ActivityNotFoundException)
        {
            Answer(null);
        }
    }

    protected override void OnActivityResult(int requestCode, Result resultCode, Intent? data)
    {
        base.OnActivityResult(requestCode, resultCode, data);
        if (requestCode != Picking) return;

        var picked = resultCode == Result.Ok ? data?.Data : null;
        if (picked is not null)
        {
            // What the picker grants lasts only while this screen does, and it closes now; a folder's copies are read later, as the
            // person chooses one and restores it. So the app keeps its access to what was picked, as the picker allows.
            try
            {
                ContentResolver!.TakePersistableUriPermission(picked, ActivityFlags.GrantReadUriPermission);
            }
            catch (Java.Lang.SecurityException)
            {
            }
        }
        Answer(picked);
    }

    private void Answer(global::Android.Net.Uri? picked)
    {
        var pick = waiting;
        waiting = null;
        pick?.TrySetResult(picked);
        Finish();
    }
}
