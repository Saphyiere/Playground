using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.OS;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Log = Android.Util.Log;

[assembly: UsesPermission(Android.Manifest.Permission.ForegroundService)]
[assembly: UsesPermission(Android.Manifest.Permission.ForegroundServiceDataSync)]
[assembly: UsesPermission(Android.Manifest.Permission.WakeLock)]

namespace Playground.AI.Assist;

public static class AndroidAskKeepAliveExtensions
{
    /// <summary>
    /// Keeps the app going while Claude answers (see <see cref="AndroidAskKeepAlive"/>), for every ask the app makes through
    /// <see cref="IClaude"/>. <paramref name="smallIcon"/> is the app's mark for the status bar, a drawable resource, white on clear.
    /// Before or after <c>AddAiAssist</c>, either works.
    /// </summary>
    public static IServiceCollection AddAndroidAskKeepAlive(this IServiceCollection services, int smallIcon)
    {
        services.Replace(ServiceDescriptor.Singleton<IAssistKeepAlive>(new AndroidAskKeepAlive(Application.Context, smallIcon)));
        return services;
    }
}

/// <summary>
/// Keeps the app going while Claude answers: the first ask under way starts <see cref="AskService"/>, a foreground service whose
/// quiet notification says what Claude is doing, and the last one to finish stops it. Android neither stops nor freezes an app
/// with a foreground service, so an ask goes on when the app is left: another app opened, the phone locked, or the app swiped off
/// the recent apps. Android 8 and later only; Android 12 and later start one only for an app on screen, so an ask begun in the
/// background goes on as it always did, unkept.
/// </summary>
public sealed class AndroidAskKeepAlive : IAssistKeepAlive
{
    readonly Context context;
    readonly Handler main = new(Looper.MainLooper!);
    readonly Lock gate = new();
    readonly List<Holding> holding = [];

    internal AndroidAskKeepAlive(Context context, int smallIcon)
    {
        this.context = context;
        SmallIcon = smallIcon;
        AskService.Keeper = this;
    }

    internal int SmallIcon { get; }

    /// <summary>What the notification says: the words of the latest ask still under way; null when none is.</summary>
    internal string? Doing
    {
        get
        {
            lock (gate) return holding.Count == 0 ? null : holding[^1].Doing;
        }
    }

    public IDisposable Hold(string doing)
    {
        var hold = new Holding(this, doing);
        lock (gate) holding.Add(hold);
        Changed();
        return hold;
    }

    void Release(Holding hold)
    {
        lock (gate)
        {
            if (!holding.Remove(hold)) return;
        }

        Changed();
    }

    // Android runs the service on the main thread, so it's started, reworded and stopped there, in the order the asks came and went.
    void Changed() => main.Post(() => AskService.Sync(context));

    sealed class Holding(AndroidAskKeepAlive owner, string doing) : IDisposable
    {
        public string Doing => doing;

        public void Dispose() => owner.Release(this);
    }
}

/// <summary>
/// The foreground service that keeps the app going while Claude answers, run by <see cref="AndroidAskKeepAlive"/>. It does no work
/// of its own: the asks go on where they began, and it only keeps Android from stopping or freezing the app until they're done.
/// Swiping the app off the recent apps leaves it running (a service stays unless it's told to stop with its task).
/// </summary>
[Service(Name = "playground.ai.assist.AskService", Exported = false, ForegroundServiceType = ForegroundService.TypeDataSync)]
public sealed class AskService : Service
{
    const string Tag = "Playground.AI";
    const string Channel = "claude";
    const int NoticeId = 2_417;

    // A partial wake lock keeps the phone's processor awake for a stream that's still coming; at most this long, if an ask hangs.
    static readonly long Awake = (long)TimeSpan.FromMinutes(30).TotalMilliseconds;

    internal static AndroidAskKeepAlive? Keeper;

    // Touched only on the main thread: the service once it's in the foreground, and whether Android has been asked to start it.
    static AskService? running;
    static bool starting;

    PowerManager.WakeLock? wakeLock;
    string? shown;

    /// <summary>Brings the service in line with the asks under way: started for the first, reworded for another, stopped after the last.</summary>
    internal static void Sync(Context context)
    {
        var doing = Keeper?.Doing;
        if (doing is null)
        {
            // While it's starting, it finds there's nothing left to keep going and stops itself.
            running?.Stop();
            return;
        }

        if (running is { } service)
        {
            service.Show(doing);
            return;
        }

        if (starting || !OperatingSystem.IsAndroidVersionAtLeast(26)) return;
        try
        {
            context.StartForegroundService(new Intent(context, typeof(AskService)));
            starting = true;
        }
        catch (Exception e)
        {
            // Android 12 and later refuse an app that isn't on screen: the ask goes on, unkept.
            Log.Warn(Tag, $"The app can't be kept going while Claude answers: {e.Message}");
        }
    }

    public override IBinder? OnBind(Intent? intent) => null;

    public override StartCommandResult OnStartCommand(Intent? intent, StartCommandFlags flags, int startId)
    {
        starting = false;
        try
        {
            // At once, whatever comes next: Android ends an app whose started service doesn't come to the foreground in seconds.
            var notice = Notice(Keeper?.Doing ?? "Asking Claude");
            if (OperatingSystem.IsAndroidVersionAtLeast(29)) StartForeground(NoticeId, notice, ForegroundService.TypeDataSync);
            else StartForeground(NoticeId, notice);
            running = this;
            wakeLock ??= ((PowerManager)GetSystemService(PowerService)!).NewWakeLock(WakeLockFlags.Partial, "Playground:claude");
            if (wakeLock is { IsHeld: false }) wakeLock.Acquire(Awake);
        }
        catch (Exception e)
        {
            Log.Warn(Tag, $"Android didn't let the app stay in the foreground while Claude answers: {e.Message}");
            StopSelf();
            return StartCommandResult.NotSticky;
        }

        // The asks may have ended, or changed, while it started.
        Sync(this);
        return StartCommandResult.NotSticky;
    }

    // Android 15 and later give a data sync service six hours a day; past that, the asks go on unkept.
    public override void OnTimeout(int startId, ForegroundService fgsType) => Stop();

    public override void OnDestroy()
    {
        if (running == this) running = null;
        if (wakeLock?.IsHeld == true) wakeLock.Release();
        base.OnDestroy();
    }

    void Stop()
    {
        if (running == this) running = null;
        if (wakeLock?.IsHeld == true) wakeLock.Release();
        StopForeground(StopForegroundFlags.Remove);
        StopSelf();
    }

    void Show(string doing)
    {
        if (doing == shown) return;
        ((NotificationManager)GetSystemService(NotificationService)!).Notify(NoticeId, Notice(doing));
    }

    // Quiet, and in the shade only: what Claude is doing, that the app can be left meanwhile, and a tap back into the app.
    Notification Notice(string doing)
    {
        shown = doing;
        if (!OperatingSystem.IsAndroidVersionAtLeast(26)) throw new PlatformNotSupportedException();
        var channel = new NotificationChannel(Channel, "Claude", NotificationImportance.Low)
        {
            Description = "While Claude answers, so its answer isn't lost if you leave the app.",
        };
        ((NotificationManager)GetSystemService(NotificationService)!).CreateNotificationChannel(channel);
        var builder = new Notification.Builder(this, Channel)
            .SetSmallIcon(Keeper?.SmallIcon is { } icon and not 0 ? icon : ApplicationInfo!.Icon)
            .SetContentTitle(doing)
            .SetContentText("Claude keeps going if you leave the app.")
            .SetProgress(0, 0, true)
            .SetOngoing(true)
            .SetOnlyAlertOnce(true)
            .SetShowWhen(false)
            .SetCategory(Notification.CategoryProgress);
        if (PackageManager!.GetLaunchIntentForPackage(PackageName!) is { } open)
            builder.SetContentIntent(PendingIntent.GetActivity(this, 0, open, PendingIntentFlags.Immutable | PendingIntentFlags.UpdateCurrent));
        return builder.Build();
    }
}
