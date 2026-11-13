#if DEBUG
using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.OS;
using Android.Util;
using Playground.AI.Assist;
using Prima.Core.Transcription;
using Prima.UI.Host;
using Prima.UI.Storage;
using Prima.UI.Transcription;
using System.Security.Cryptography;
using System.Text.Json;

namespace Prima.Tablet;

/// <summary>ADB-only regression entry point. Its fixed test-copy piece and fixed input/output paths isolate user music.</summary>
[Service(Name = "com.saphyiere.prima.ConversionDiagnosticService", Exported = true, Permission = "android.permission.DUMP", ForegroundServiceType = ForegroundService.TypeDataSync)]
public sealed class ConversionDiagnosticService : Service
{
    private const string PieceId = "conversion-diagnostic-sweden";
    private const string Title = "Sweden — conversion test";
    private readonly SemaphoreSlim commands = new(1, 1);
    private int pending;
    private int latestStart;

    public override IBinder? OnBind(Intent? intent) => null;

    public override StartCommandResult OnStartCommand(Intent? intent, StartCommandFlags flags, int startId)
    {
        latestStart = startId;
        var command = intent?.GetStringExtra("command") ?? "status";
        try
        {
            var notifications = (NotificationManager)GetSystemService(NotificationService)!;
            if (OperatingSystem.IsAndroidVersionAtLeast(26)) notifications.CreateNotificationChannel(new NotificationChannel("prima.diagnostics", "Conversion diagnostics", NotificationImportance.Low));
            var builder = new AndroidX.Core.App.NotificationCompat.Builder(this, "prima.diagnostics");
            builder.SetSmallIcon(Android.Resource.Drawable.IcMenuInfoDetails);
            builder.SetContentTitle("Prima conversion diagnostics");
            builder.SetContentText(command);
            builder.SetOngoing(true);
            var notice = builder.Build() ?? throw new InvalidOperationException("Android did not create the diagnostic notification.");
            if (OperatingSystem.IsAndroidVersionAtLeast(29)) StartForeground(31416, notice, ForegroundService.TypeDataSync);
            else StartForeground(31416, notice);
        }
        catch (Exception error)
        {
            Log.Error("PrimaConversionDiagnostic", "Foreground start failed: " + error);
            StopSelfResult(latestStart);
            return StartCommandResult.NotSticky;
        }
        pending++;
        _ = Task.Run(() => RunCommandAsync(command));
        return StartCommandResult.NotSticky;
    }

    private async Task RunCommandAsync(string command)
    {
        await commands.WaitAsync();
        var folder = Path.Combine(GetExternalFilesDir(null)!.AbsolutePath, "diagnostics");
        Directory.CreateDirectory(folder);
        var report = new Dictionary<string, object?> { ["command"] = command, ["started"] = DateTimeOffset.UtcNow, ["status"] = "running" };
        var power = (PowerManager)GetSystemService(PowerService)!;
        using var awake = power.NewWakeLock(WakeLockFlags.Partial, "Prima:conversion-diagnostics")!;
        awake.Acquire(120_000);
        try
        {
            await File.WriteAllTextAsync(Path.Combine(folder, "command.json"), JsonSerializer.Serialize(report));
            var services = IPlatformApplication.Current!.Services;
            var store = services.GetRequiredService<PrimaStore>();
            var jobs = services.GetRequiredService<ConversionJobs>();
            var host = services.GetRequiredService<IConversionHost>();
            var provider = services.GetRequiredService<IInferenceProvider>();
            // Await startup recovery before touching a test checkpoint or handing off a new run.
            var latest = await jobs.LatestAsync(PieceId);
            if (command == "cancel")
            {
                if (latest is not null) await jobs.CancelAsync(latest.Id);
                await File.WriteAllTextAsync(Path.Combine(folder, "status.json"), JsonSerializer.Serialize(await jobs.LatestAsync(PieceId), AssistSchema.Json));
                report["job"] = latest?.Id;
            }
            else if (command == "status")
            {
                await File.WriteAllTextAsync(Path.Combine(folder, "status.json"), JsonSerializer.Serialize(latest, AssistSchema.Json));
                report["job"] = latest?.Id;
            }
            else if (command is "prepare" or "offline" or "live")
            {
                var pdf = await File.ReadAllBytesAsync(Path.Combine(folder, "source.pdf"));
                var source = JsonSerializer.Deserialize<ConversionSource>(await File.ReadAllTextAsync(Path.Combine(folder, "source.json")), AssistSchema.Json)
                    ?? throw new InvalidDataException("The diagnostic source map is missing.");
                if (source.Problems().Count != 0) throw new InvalidDataException(string.Join(" ", source.Problems()));
                if (!source.Sha256.Equals(Convert.ToHexStringLower(SHA256.HashData(pdf)), StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("The diagnostic source map does not match its PDF.");
                if (command == "prepare")
                {
                    var existing = await store.GetPieceAsync(PieceId);
                    if (existing is not null && (existing.Title != Title || existing.Origin != "import" || existing.Format != "pdf")) throw new InvalidOperationException("The diagnostic piece ID is already used by another piece.");
                    if (latest is { Active: true }) throw new InvalidOperationException("The diagnostic conversion is running; its source cannot be replaced.");
                    Directory.CreateDirectory(store.FilesPath);
                    await File.WriteAllBytesAsync(Path.Combine(store.FilesPath, PieceId + ".pdf"), pdf);
                    if (existing is null)
                    {
                        var piece = new PieceRecord(PieceId, "import", Title, "", null, "pdf", PieceId + ".pdf", null, null, null,
                            null, source.Bars.Count, "Local diagnostic fixture", null, null, null, null, null, null, null, null, DateTimeOffset.UtcNow, null);
                        await store.SavePieceAsync(piece, null, []);
                    }
                    await store.SaveConversionSourceAsync(PieceId, source);
                    report["piece"] = PieceId;
                }
                else
                {
                    if (await store.GetPieceAsync(PieceId) is not { Title: Title, Format: "pdf" }) throw new InvalidOperationException("Run the prepare command before starting a diagnostic conversion.");
                    var execution = new ReadSystem([]).Execution;
                    var options = ConversionOptions.ForSource(provider.ProviderId,
                        new InferenceSettings(execution.Model, execution.Effort, 12_000, 0, false), source);
                    ConversionJob job;
                    if (command == "offline" && latest is { State: ConversionState.Interrupted or ConversionState.Paused })
                    {
                        if (latest.Attempts.Count != 0 || latest.Chunks.Any(c => c.Phase is not (ConversionPhase.Render or ConversionPhase.Complete) || c.Reading is null))
                            throw new InvalidOperationException("Offline resume requires a rendering-only checkpoint with no inference requests.");
                        if (latest.Source.Sha256 != source.Sha256 || latest.Source.Revision != source.Revision) throw new InvalidOperationException("The saved offline job has different source inputs.");
                        job = await jobs.ResumeAsync(latest.Id);
                    }
                    else
                    {
                        if (latest is { State: ConversionState.Queued or ConversionState.Running or ConversionState.Paused or ConversionState.Interrupted })
                            throw new InvalidOperationException("The test piece already has unfinished work. Finish or cancel that test job before starting another.");
                        SystemReading? reading = null;
                        if (command == "offline")
                        {
                            reading = JsonSerializer.Deserialize<SystemReading>(await File.ReadAllTextAsync(Path.Combine(folder, "reading.json")), AssistSchema.Json)
                                ?? throw new InvalidDataException("The offline diagnostic reading is missing.");
                            if (reading.Bars.Count != source.Bars.Count || reading.Bars.Where((bar, index) => bar.Index != index + 1).Any())
                                throw new InvalidDataException("The offline reading must contain each source bar exactly once, indexed from one in written order.");
                        }
                        job = await jobs.CreateAsync(PieceId, Title, pdf, source, options);
                        if (reading is not null)
                        {
                            var completeReading = reading;
                            var chunks = job.Chunks.Select(chunk =>
                            {
                                var orders = chunk.Chunk.BarIds.Select(id => source.Bars.Single(bar => bar.Id == id).Order).ToArray();
                                var bars = orders.Select((order, local) => completeReading.Bars[order] with { Index = local + 1 }).ToArray();
                                var doubts = completeReading.Doubts.Where(doubt => orders.Contains(doubt.Bar - 1))
                                    .Select(doubt => doubt with { Bar = Array.IndexOf(orders, doubt.Bar - 1) + 1 }).ToArray();
                                return chunk with { Phase = ConversionPhase.Render, Reading = new SystemReading(bars, doubts) };
                            }).ToArray();
                            var seeded = job with { Revision = job.Revision + 1, Updated = DateTimeOffset.UtcNow, Chunks = chunks };
                            if (!await store.SaveConversionJobAsync(seeded, job.Revision)) throw new InvalidOperationException("The diagnostic checkpoint changed before it could be seeded.");
                            job = seeded;
                        }
                    }
                    report["job"] = job.Id;
                    report["model"] = job.Options.Reading.Model;
                    report["provider"] = job.Options.Provider;
                    report["inferenceEnabled"] = command == "live";
                    await File.WriteAllTextAsync(Path.Combine(folder, "start.json"), JsonSerializer.Serialize(job, AssistSchema.Json));
                    await host.StartAsync(job.Id);
                    await File.WriteAllTextAsync(Path.Combine(folder, "status.json"), JsonSerializer.Serialize(await store.GetConversionJobAsync(job.Id), AssistSchema.Json));
                }
            }
            else throw new ArgumentException("Unknown diagnostic command.");
            report["status"] = "completed";
        }
        catch (Exception error)
        {
            report["status"] = "failed";
            report["error"] = error.ToString();
            Log.Error("PrimaConversionDiagnostic", error.ToString());
        }
        finally
        {
            if (awake.IsHeld) awake.Release();
            report["finished"] = DateTimeOffset.UtcNow;
            try { await File.WriteAllTextAsync(Path.Combine(folder, "command.json"), JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true })); }
            catch (Exception error) { Log.Error("PrimaConversionDiagnostic", "Could not write command result: " + error); }
            commands.Release();
            MainThread.BeginInvokeOnMainThread(() =>
            {
                pending--;
                if (pending != 0) return;
                StopForeground(StopForegroundFlags.Remove);
                Log.Info("PrimaConversionDiagnostic", "Service stopped: " + StopSelfResult(latestStart));
            });
        }
    }
}
#endif
