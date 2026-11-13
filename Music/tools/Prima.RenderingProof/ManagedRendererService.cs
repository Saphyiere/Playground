using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.Graphics;
using Android.OS;
using Android.Util;
using Prima.Core.Transcription;
using Prima.Tablet;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using Path = System.IO.Path;

namespace Prima.RenderingProof;

/// <summary>Device regression checks using the production managed renderer and Java source, without an Activity or API calls.</summary>
[Service(Name = "com.saphyiere.prima.renderingproof.ManagedRendererService", Exported = true, Permission = "android.permission.DUMP", ForegroundServiceType = ForegroundService.TypeDataSync)]
public sealed class ManagedRendererService : Service
{
    private bool running;
    private int latestStart;

    public override IBinder? OnBind(Intent? intent) => null;

    public override StartCommandResult OnStartCommand(Intent? intent, StartCommandFlags flags, int startId)
    {
        latestStart = startId;
        if (running) return StartCommandResult.NotSticky;
        running = true;
        var notifications = (NotificationManager)GetSystemService(NotificationService)!;
        notifications.CreateNotificationChannel(new NotificationChannel("proof", "Rendering proof", NotificationImportance.Low));
        var notice = new Notification.Builder(this, "proof").SetSmallIcon(Android.Resource.Drawable.IcMenuInfoDetails)
            .SetContentTitle("Prima managed rendering proof").SetContentText("Testing the production bridge").SetOngoing(true).Build();
        if (OperatingSystem.IsAndroidVersionAtLeast(29)) StartForeground(2, notice, ForegroundService.TypeDataSync);
        else StartForeground(2, notice);
        _ = RunAsync();
        return StartCommandResult.NotSticky;
    }

    private async Task RunAsync()
    {
        var folder = Path.Combine(GetExternalFilesDir(null)!.AbsolutePath, "managed-proof");
        Directory.CreateDirectory(folder);
        var report = new Dictionary<string, object?> { ["status"] = "running", ["started"] = DateTimeOffset.UtcNow, ["sdk"] = (int)Build.VERSION.SdkInt };
        var power = (PowerManager)GetSystemService(PowerService)!;
        using var awake = power.NewWakeLock(WakeLockFlags.Partial, "Prima:managed-rendering-proof")!;
        awake.Acquire(180_000);
        try
        {
            report["screenOffAtStart"] = !power.IsInteractive;
            var renderer = new AndroidConversionRenderer();
            var unavailable = await renderer.UnavailableReasonAsync(default);
            if (unavailable is not null) throw new InvalidOperationException(unavailable);
            report["preflight"] = "passed";
            byte[] pdf;
            using (var input = Assets!.Open("input.pdf"))
            using (var memory = new MemoryStream())
            {
                await input.CopyToAsync(memory);
                pdf = memory.ToArray();
            }
            var full = await renderer.CropAsync(pdf, 1, new(0, 0, 1, 1), 900, default);
            await File.WriteAllBytesAsync(Path.Combine(folder, "full.png"), full);
            // The original synthetic page is 792 x 612 points. Choose integral output-pixel boundaries to compare crops.
            const double exactHeight = 900d * 612 / 792;
            var crop = await renderer.CropAsync(pdf, 1, new(.1, 100 / exactHeight, .8, 300 / exactHeight), 720, default);
            await File.WriteAllBytesAsync(Path.Combine(folder, "crop.png"), crop);
            using (var whole = await BitmapFactory.DecodeByteArrayAsync(full, 0, full.Length) ?? throw new InvalidDataException("Full-page PNG is invalid."))
            using (var part = await BitmapFactory.DecodeByteArrayAsync(crop, 0, crop.Length) ?? throw new InvalidDataException("Crop PNG is invalid."))
            {
                if (part.Width != 720 || part.Height != 300) throw new InvalidDataException("The normalized crop dimensions are wrong.");
                var changedPixels = 0;
                var inkPixels = 0;
                for (var y = 0; y < part.Height; y++)
                {
                    for (var x = 0; x < part.Width; x++)
                    {
                        var pixel = part.GetPixel(x, y);
                        if (((pixel >> 16) & 255) + ((pixel >> 8) & 255) + (pixel & 255) < 3 * 240) inkPixels++;
                        if (pixel != whole.GetPixel(x + 90, y + 100)) changedPixels++;
                    }
                }
                report["cropDifferingPixels"] = changedPixels;
                report["cropInkPixels"] = inkPixels;
                if (inkPixels < 100 || changedPixels > part.Width * part.Height / 100) throw new InvalidDataException("The normalized crop differs from the same full-page region.");
            }
            var rejectedPage = false;
            try { await renderer.CropAsync(pdf, 2, new(0, 0, 1, 1), 900, default); }
            catch (Exception) { rejectedPage = true; }
            if (!rejectedPage) throw new InvalidDataException("An absent PDF page was accepted.");
            report["invalidPageRejected"] = true;
            string mei;
            using (var input = Assets!.Open("fixture.mei"))
            using (var reader = new StreamReader(input)) mei = await reader.ReadToEndAsync();
            var first = await renderer.EngraveAsync(mei, 1600, default);
            await File.WriteAllBytesAsync(Path.Combine(folder, "notation.png"), first);
            using (var cancellation = new CancellationTokenSource())
            {
                var watch = Stopwatch.StartNew();
                var pending = renderer.EngraveAsync(mei, 1600, cancellation.Token);
                await Task.Delay(50);
                cancellation.Cancel();
                var cancelled = false;
                try { await pending.WaitAsync(TimeSpan.FromSeconds(5)); }
                catch (System.OperationCanceledException) { cancelled = true; }
                report["cancelElapsedMs"] = watch.ElapsedMilliseconds;
                if (!cancelled) throw new InvalidOperationException("An in-flight render did not report cancellation.");
            }
            var recovered = await renderer.EngraveAsync(mei, 1600, default);
            var expectedHash = Convert.ToHexStringLower(SHA256.HashData(first));
            var recoveredHash = Convert.ToHexStringLower(SHA256.HashData(recovered));
            if (expectedHash != recoveredHash) throw new InvalidDataException("The rendering changed after cancellation and recovery.");
            report["notationSha256"] = expectedHash;
            report["recoveredSha256"] = recoveredHash;
            report["screenOffAtEnd"] = !power.IsInteractive;
            report["status"] = "passed";
        }
        catch (Exception error)
        {
            report["status"] = "failed";
            report["error"] = error.ToString();
            Log.Error("PrimaManagedProof", error.ToString());
        }
        finally
        {
            if (awake.IsHeld) awake.Release();
            report["wakeLockHeldAfterCleanup"] = awake.IsHeld;
            report["finished"] = DateTimeOffset.UtcNow;
            new Handler(Looper.MainLooper!).Post(() =>
            {
                running = false;
                StopForeground(StopForegroundFlags.Remove);
                report["serviceStopAccepted"] = StopSelfResult(latestStart);
                File.WriteAllText(Path.Combine(folder, "result.json"), JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
                Log.Info("PrimaManagedProof", "MANAGED_PROOF_" + report["status"]);
            });
        }
    }
}
