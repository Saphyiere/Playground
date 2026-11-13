using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.OS;
using AndroidX.Core.App;
using Microsoft.Extensions.Logging;
using Prima.UI.Host;
using Prima.UI.Routines;
using System.Collections.Concurrent;

namespace Prima.Tablet;

public sealed class AndroidRoutineHost(RoutineService jobs, ILogger<AndroidRoutineHost> logger) : IRoutineHost
{
    public async Task StartAsync(string jobId)
    {
        try
        {
            var intent = new Intent(Android.App.Application.Context, typeof(RoutineServiceHost)).PutExtra(RoutineServiceHost.JobExtra, jobId);
            if (OperatingSystem.IsAndroidVersionAtLeast(26)) Android.App.Application.Context.StartForegroundService(intent);
            else Android.App.Application.Context.StartService(intent);
        }
        catch
        {
            await jobs.InterruptAsync(jobId, "Android could not start background planning. Your request is saved; reopen it to retry.");
            throw;
        }
        if (OperatingSystem.IsAndroidVersionAtLeast(33) && Platform.CurrentActivity?.HasWindowFocus == true && !Preferences.Default.Get("conversion.notificationsAsked", false))
        {
            try { await MainThread.InvokeOnMainThreadAsync(() => Permissions.RequestAsync<Permissions.PostNotifications>()); }
            catch (Exception error) { logger.LogWarning(error, "Notification permission could not be requested; routine planning continues."); }
            finally { Preferences.Default.Set("conversion.notificationsAsked", true); }
        }
    }
}

[Service(Name = "com.saphyiere.prima.RoutineService", Exported = false, ForegroundServiceType = ForegroundService.TypeDataSync)]
public sealed class RoutineServiceHost : Service
{
    internal const string JobExtra = "prima.routine.job";
    internal const string OpenExtra = "prima.routine.open";
    private const string CancelAction = "prima.routine.CANCEL";
    private const string ProgressChannel = "prima.routine.progress";
    private const string ResultChannel = "prima.routine.results";
    private const int ForegroundId = 31418;
    private readonly CancellationTokenSource stopped = new();
    private readonly ConcurrentDictionary<string, byte> running = new();
    private RoutineService jobs = null!;
    private NotificationManager notices = null!;
    private ILogger<RoutineServiceHost> logger = null!;
    private PowerManager.WakeLock? wakeLock;
    private bool destroyed;

    public override IBinder? OnBind(Intent? intent) => null;

    public override void OnCreate()
    {
        base.OnCreate();
        var services = IPlatformApplication.Current!.Services;
        jobs = services.GetRequiredService<RoutineService>();
        logger = services.GetRequiredService<ILogger<RoutineServiceHost>>();
        notices = (NotificationManager)GetSystemService(NotificationService)!;
        if (OperatingSystem.IsAndroidVersionAtLeast(26))
        {
            notices.CreateNotificationChannel(new NotificationChannel(ProgressChannel, "Routine planning", NotificationImportance.Low));
            notices.CreateNotificationChannel(new NotificationChannel(ResultChannel, "Routine results", NotificationImportance.Default));
        }
        wakeLock = ((PowerManager)GetSystemService(PowerService)!).NewWakeLock(WakeLockFlags.Partial, "Prima:Routine");
    }

    public override StartCommandResult OnStartCommand(Intent? intent, StartCommandFlags flags, int startId)
    {
        var id = intent?.GetStringExtra(JobExtra);
        if (id is null) { StopSelfResult(startId); return StartCommandResult.NotSticky; }
        if (intent?.Action == CancelAction)
        {
            _ = jobs.CancelAsync(id);
            return StartCommandResult.NotSticky;
        }
        try
        {
            var notice = Notification(id, "Preparing your routine", "You can leave this screen. Your request is saved.", true);
            if (OperatingSystem.IsAndroidVersionAtLeast(29)) StartForeground(ForegroundId, notice, ForegroundService.TypeDataSync);
            else StartForeground(ForegroundId, notice);
            if (wakeLock?.IsHeld != true) wakeLock?.Acquire(15 * 60 * 1000L);
            if (running.TryAdd(id, 0)) _ = ExecuteAsync(id);
        }
        catch (Exception error)
        {
            logger.LogError(error, "Android rejected background routine planning.");
            _ = jobs.InterruptAsync(id, "Android ended background planning. Your request is saved; reopen it to retry.");
            StopSelf();
        }
        return StartCommandResult.NotSticky;
    }

    private async Task ExecuteAsync(string id)
    {
        try
        {
            await jobs.RunAsync(id, stopped.Token);
            var result = await jobs.GetJobAsync(id);
            if (!destroyed && result is { State: not RoutineGenerationState.Cancelled })
            {
                var message = result.State == RoutineGenerationState.Ready ? "Your routine is ready to review." : result.Problem ?? "Open your saved request to continue.";
                notices.Notify(id, ForegroundId, Notification(id, "Prima routine", message, false));
            }
        }
        catch (Exception error) { logger.LogError(error, "Routine {JobId} ended before notification.", id); }
        finally
        {
            running.TryRemove(id, out _);
            if (!destroyed && running.IsEmpty)
            {
                if (wakeLock?.IsHeld == true) wakeLock.Release();
                StopForeground(StopForegroundFlags.Remove);
                StopSelf();
            }
        }
    }

    private Notification Notification(string id, string title, string message, bool ongoing)
    {
        var open = new Intent(this, typeof(MainActivity)).SetFlags(ActivityFlags.ClearTop | ActivityFlags.SingleTop);
        open.SetData(Android.Net.Uri.Parse("prima://routine/" + id));
        open.PutExtra(OpenExtra, id);
        var builder = new NotificationCompat.Builder(this, ongoing ? ProgressChannel : ResultChannel);
        builder.SetSmallIcon(Android.Resource.Drawable.IcMenuSave);
        builder.SetContentTitle(title);
        builder.SetContentText(message);
        builder.SetContentIntent(PendingIntent.GetActivity(this, 0, open, PendingIntentFlags.UpdateCurrent | PendingIntentFlags.Immutable));
        builder.SetOngoing(ongoing);
        builder.SetAutoCancel(!ongoing);
        if (ongoing)
        {
            var cancel = new Intent(this, typeof(RoutineServiceHost)).SetAction(CancelAction).PutExtra(JobExtra, id);
            cancel.SetData(Android.Net.Uri.Parse("prima://routine/cancel/" + id));
            builder.AddAction(Android.Resource.Drawable.IcMenuCloseClearCancel, "Cancel", PendingIntent.GetService(this, 0, cancel, PendingIntentFlags.UpdateCurrent | PendingIntentFlags.Immutable));
        }
        return builder.Build() ?? throw new InvalidOperationException("Android could not create the routine notification.");
    }

    public override void OnTimeout(int startId, ForegroundService foregroundServiceType)
    {
        stopped.Cancel();
        foreach (var id in running.Keys) _ = jobs.InterruptAsync(id, "Android ended the background execution window. Your request is saved; retry explicitly to send it again.");
        StopForeground(StopForegroundFlags.Remove);
        StopSelf();
    }

    public override void OnDestroy()
    {
        destroyed = true;
        stopped.Cancel();
        if (wakeLock?.IsHeld == true) wakeLock.Release();
        wakeLock?.Dispose();
        wakeLock = null;
        base.OnDestroy();
    }
}
