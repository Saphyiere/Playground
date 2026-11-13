using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.OS;
using AndroidX.Core.App;
using Microsoft.Extensions.Logging;
using Prima.Core.Transcription;
using Prima.UI.Host;
using Prima.UI.Storage;
using Prima.UI.Transcription;
using System.Collections.Concurrent;

namespace Prima.Tablet;

/// <summary>Starts a user-requested conversion while the app is visible. Notification permission is optional.</summary>
public sealed class AndroidConversionHost(ConversionJobs jobs, ILogger<AndroidConversionHost> logger) : IConversionHost
{
    public async Task StartAsync(string jobId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(jobId);
        try
        {
            var intent = new Intent(Android.App.Application.Context, typeof(ConversionService)).PutExtra(ConversionService.JobExtra, jobId);
            if (OperatingSystem.IsAndroidVersionAtLeast(26)) Android.App.Application.Context.StartForegroundService(intent);
            else Android.App.Application.Context.StartService(intent);
        }
        catch (Exception error)
        {
            logger.LogError(error, "Android could not start conversion {JobId}.", jobId);
            await jobs.InterruptAsync(jobId, "Android could not start background conversion. Reopen Prima and resume this conversion.");
            throw new InvalidOperationException("Android could not start background conversion. Your progress is saved; reopen Prima and resume.", error);
        }
        // Establish the execution lifetime before any permission dialog can move the user away from this Activity.
        if (OperatingSystem.IsAndroidVersionAtLeast(33) && Platform.CurrentActivity?.HasWindowFocus == true && !Preferences.Default.Get("conversion.notificationsAsked", false))
        {
            try { await MainThread.InvokeOnMainThreadAsync(() => Permissions.RequestAsync<Permissions.PostNotifications>()); }
            catch (Exception error) { logger.LogWarning(error, "Notification permission could not be requested; conversion can still run."); }
            finally { Preferences.Default.Set("conversion.notificationsAsked", true); }
        }
    }
}

/// <summary>Owns execution, CPU lifetime, and notifications. Closing a page or removing the app task does not cancel a job.</summary>
[Service(Name = "com.saphyiere.prima.ConversionService", Exported = false, ForegroundServiceType = ForegroundService.TypeDataSync)]
public sealed class ConversionService : Service
{
    internal const string JobExtra = "prima.conversion.job";
    internal const string PieceExtra = "prima.conversion.piece";
    private const string CancelAction = "prima.conversion.CANCEL";
    private const string ProgressChannel = "prima.conversion.progress";
    private const string ResultChannel = "prima.conversion.results";
    private const int ForegroundId = 31415;
    private readonly CancellationTokenSource stopped = new();
    private readonly ConcurrentDictionary<string, byte> activeJobs = new();
    private ConversionJobs? jobs;
    private PrimaStore? store;
    private ILogger<ConversionService>? logger;
    private NotificationManager? notifications;
    private PowerManager.WakeLock? wakeLock;
    private Timer? deadline;
    private int pendingWork;
    private int latestStartId;
    private bool foreground;
    private bool ending;
    private bool destroyed;
    private bool cancellationDisposed;

    public override IBinder? OnBind(Intent? intent) => null;

    public override void OnCreate()
    {
        base.OnCreate();
        var services = IPlatformApplication.Current!.Services;
        jobs = services.GetRequiredService<ConversionJobs>();
        store = services.GetRequiredService<PrimaStore>();
        logger = services.GetRequiredService<ILogger<ConversionService>>();
        notifications = (NotificationManager)GetSystemService(NotificationService)!;
        if (OperatingSystem.IsAndroidVersionAtLeast(26))
        {
            notifications.CreateNotificationChannel(new NotificationChannel(ProgressChannel, "Conversion progress", NotificationImportance.Low));
            notifications.CreateNotificationChannel(new NotificationChannel(ResultChannel, "Conversion results", NotificationImportance.Default));
        }
        jobs.Changed += ShowSavedState;
    }

    public override StartCommandResult OnStartCommand(Intent? intent, StartCommandFlags flags, int startId)
    {
        latestStartId = startId;
        var id = intent?.GetStringExtra(JobExtra);
        if (string.IsNullOrWhiteSpace(id))
        {
            StopIfIdle();
            return StartCommandResult.NotSticky;
        }
        if (intent?.Action == CancelAction)
        {
            pendingWork++;
            _ = Task.Run(async () =>
            {
                try { await jobs!.CancelAsync(id); }
                catch (Exception error) { logger!.LogError(error, "Could not cancel conversion {JobId}.", id); }
                finally { MainThread.BeginInvokeOnMainThread(() => { pendingWork--; StopIfIdle(); }); }
            });
            return StartCommandResult.NotSticky;
        }
        if (!activeJobs.TryAdd(id, 0)) return StartCommandResult.NotSticky;
        pendingWork++;
        if (ending)
        {
            _ = Task.Run(() => RunJobAsync(id, "Background execution is stopping. Your progress is saved; reopen Prima and resume."));
            return StartCommandResult.NotSticky;
        }
        try
        {
            if (!foreground)
            {
                var notice = BuildNotification(id, null, "Converting sheet music", "Progress is saved. You can leave this screen.", true);
                if (OperatingSystem.IsAndroidVersionAtLeast(29)) StartForeground(ForegroundId, notice, ForegroundService.TypeDataSync);
                else StartForeground(ForegroundId, notice);
                foreground = true;
                var power = (PowerManager)GetSystemService(PowerService)!;
                wakeLock = power.NewWakeLock(WakeLockFlags.Partial, "Prima:conversion");
                wakeLock!.Acquire((long)TimeSpan.FromHours(2).TotalMilliseconds + 10_000);
                deadline = new Timer(state => MainThread.BeginInvokeOnMainThread(() => _ = InterruptAndStopAsync("Background conversion reached its execution time allowance. Your progress is saved; resume when ready.")), null, TimeSpan.FromHours(2), Timeout.InfiniteTimeSpan);
            }
            _ = Task.Run(() => RunJobAsync(id));
        }
        catch (Exception error)
        {
            logger!.LogError(error, "Android rejected foreground conversion execution.");
            _ = Task.Run(() => RunJobAsync(id, "Android could not keep conversion running in the background. Your progress is saved; reopen Prima and resume."));
        }
        return StartCommandResult.NotSticky;
    }

    private async Task RunJobAsync(string id, string? startFailure = null)
    {
        try
        {
            if (startFailure is not null) await jobs!.InterruptAsync(id, startFailure);
            else await jobs!.RunAsync(id, stopped.Token);
            if (await store!.GetConversionJobAsync(id) is { } saved) ShowSavedState(saved);
        }
        catch (Exception error) { logger!.LogError(error, "Conversion host failed for job {JobId}; durable recovery will preserve its checkpoint.", id); }
        finally
        {
            MainThread.BeginInvokeOnMainThread(() =>
            {
                activeJobs.TryRemove(id, out _);
                pendingWork--;
                StopIfIdle();
            });
        }
    }

    private void ShowSavedState(ConversionJob job)
    {
        if (!activeJobs.ContainsKey(job.Id)) return;
        try
        {
            if (job.State == ConversionState.Cancelled)
            {
                notifications!.Cancel(job.Id, 0);
                return;
            }
            if (job.State is ConversionState.Ready or ConversionState.Paused or ConversionState.Interrupted)
            {
                var message = job.State == ConversionState.Ready ? "Conversion is ready to review." : job.Problem ?? "Conversion needs your attention. Your progress is saved.";
                notifications!.Notify(job.Id, 0, BuildNotification(job.Id, job.Piece, job.Title, message, false));
            }
            else if (foreground)
            {
                notifications!.Cancel(job.Id, 0);
                var done = job.Chunks.Count(c => c.Phase == ConversionPhase.Complete);
                notifications!.Notify(ForegroundId, BuildNotification(job.Id, job.Piece, job.Title, $"Converting: {done} of {job.Chunks.Count} sections saved. You can leave this screen.", true));
            }
        }
        catch (Exception error) { logger!.LogWarning(error, "Conversion {JobId} is saved but its notification could not be shown.", job.Id); }
    }

    private Notification BuildNotification(string id, string? piece, string title, string message, bool ongoing)
    {
        var open = new Intent(this, typeof(MainActivity)).SetFlags(ActivityFlags.ClearTop | ActivityFlags.SingleTop);
        open.SetData(Android.Net.Uri.Parse("prima://conversion/" + id));
        if (piece is not null) open.PutExtra(PieceExtra, piece);
        var content = PendingIntent.GetActivity(this, 0, open, PendingIntentFlags.UpdateCurrent | PendingIntentFlags.Immutable);
        var builder = new NotificationCompat.Builder(this, ongoing ? ProgressChannel : ResultChannel);
        builder.SetSmallIcon(Android.Resource.Drawable.IcMenuSave);
        builder.SetContentTitle(title);
        builder.SetContentText(message);
        var style = new NotificationCompat.BigTextStyle();
        style.BigText(message);
        builder.SetStyle(style);
        builder.SetContentIntent(content);
        builder.SetOngoing(ongoing);
        builder.SetAutoCancel(!ongoing);
        builder.SetOnlyAlertOnce(true);
        if (ongoing)
        {
            var cancel = new Intent(this, typeof(ConversionService)).SetAction(CancelAction).PutExtra(JobExtra, id);
            cancel.SetData(Android.Net.Uri.Parse("prima://conversion/cancel/" + id));
            var cancelIntent = PendingIntent.GetService(this, 0, cancel, PendingIntentFlags.UpdateCurrent | PendingIntentFlags.Immutable);
            builder.AddAction(Android.Resource.Drawable.IcMenuCloseClearCancel, "Cancel", cancelIntent);
        }
        return builder.Build() ?? throw new InvalidOperationException("Android did not create the conversion notification.");
    }

    // All start IDs and work counts are handled on Android's main looper, including completion callbacks.
    private void StopIfIdle()
    {
        if (pendingWork != 0) return;
        deadline?.Dispose();
        deadline = null;
        if (wakeLock?.IsHeld == true) wakeLock.Release();
        if (foreground) StopForeground(StopForegroundFlags.Remove);
        foreground = false;
        StopSelfResult(latestStartId);
        DisposeStoppedWhenIdle();
    }

    public override void OnTimeout(int startId, ForegroundService foregroundServiceType)
    {
        _ = InterruptAndStopAsync("Android ended this background execution window. Your progress is saved; reopen Prima and resume.");
    }

    private async Task InterruptAndStopAsync(string reason)
    {
        if (ending) return;
        ending = true;
        var cancellation = stopped.CancelAsync();
        var checkpoints = Task.WhenAll(activeJobs.Keys.Select(id => jobs!.InterruptAsync(id, reason)).Append(cancellation));
        try { await checkpoints.WaitAsync(TimeSpan.FromSeconds(2)); }
        catch (Exception error) { logger!.LogWarning(error, "Background execution ended before every interruption checkpoint finished; startup recovery remains available."); }
        if (wakeLock?.IsHeld == true) wakeLock.Release();
        if (foreground) StopForeground(StopForegroundFlags.Remove);
        foreground = false;
        StopSelf();
    }

    public override void OnDestroy()
    {
        ending = true;
        destroyed = true;
        jobs!.Changed -= ShowSavedState;
        deadline?.Dispose();
        try { stopped.Cancel(); }
        catch (Exception error) { logger!.LogWarning(error, "A conversion cancellation callback failed during service cleanup."); }
        if (wakeLock?.IsHeld == true) wakeLock.Release();
        wakeLock?.Dispose();
        wakeLock = null;
        DisposeStoppedWhenIdle();
        base.OnDestroy();
    }

    private void DisposeStoppedWhenIdle()
    {
        if (!destroyed || pendingWork != 0 || cancellationDisposed) return;
        stopped.Dispose();
        cancellationDisposed = true;
    }
}
