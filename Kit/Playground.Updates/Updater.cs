using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;

namespace Playground.Updates;

/// <summary>
/// An app's updates from its GitHub releases: one per app, holding the <see cref="Status"/> its screens show.
/// <list type="bullet">
/// <item><see cref="CheckAsync"/> asks GitHub for the newest release, at most once in 20 hours unless asked again, so an app can
/// call it each time it starts.</item>
/// <item><see cref="InstallAsync"/> downloads it, checks it against the size and SHA-256 GitHub gives, checks it's signed with
/// the installed app's key, copies the app's data files somewhere an uninstall wouldn't reach, and hands it to the device's
/// installer, which asks the person to confirm. A step that fails stops it there, with nothing installed.</item>
/// </list>
/// A release's build is N in its version 1.0.N; a copy built on the PC is 1.0, older than any release.
/// </summary>
public sealed class Updater
{
    private static readonly TimeSpan Daily = TimeSpan.FromHours(20);

    private readonly UpdateOptions options;
    private readonly IInstaller installer;
    private readonly TimeProvider time;
    private readonly HttpClient http;
    private readonly SemaphoreSlim busy = new(1, 1);

    public Updater(UpdateOptions options, IInstaller installer, TimeProvider? time = null, HttpMessageHandler? handler = null)
    {
        this.options = options;
        this.installer = installer;
        this.time = time ?? TimeProvider.System;
        http = handler is null ? new HttpClient() : new HttpClient(handler, disposeHandler: false);
        http.Timeout = TimeSpan.FromMinutes(15);
        Status = new UpdateStatus(UpdateStep.Unchecked, installer.InstalledVersion, BuildOf(installer.InstalledVersion), null, 0, "", null);
        // The person cancelled the device's installer, or it failed: the update can be tried again.
        installer.Stopped += why => Set(Status with { Step = Status.Latest is null ? UpdateStep.Failed : UpdateStep.Available, Message = why });
    }

    public UpdateStatus Status { get; private set; }

    /// <summary>Raised on each change of <see cref="Status"/>, from whatever thread the work is on.</summary>
    public event Action<UpdateStatus>? Changed;

    /// <summary>The build in a release's version, 1.0.N; null for 1.0, a copy built on the PC.</summary>
    public static int? BuildOf(string version)
    {
        var parts = version.Split('.');
        return parts.Length == 3 && int.TryParse(parts[2], NumberStyles.None, CultureInfo.InvariantCulture, out var build) ? build : null;
    }

    /// <summary>Asks GitHub for the app's newest release: skipped when it asked less than 20 hours ago, unless <paramref name="evenIfRecent"/>.</summary>
    public async Task CheckAsync(bool evenIfRecent = false, CancellationToken ct = default)
    {
        if (!evenIfRecent && Status.CheckedAt is { } last && time.GetUtcNow() - last < Daily) return;
        if (!await busy.WaitAsync(0, ct)) return;
        try
        {
            var installedVersion = installer.InstalledVersion;
            Set(Status with { Step = UpdateStep.Checking, Installed = installedVersion, InstalledBuild = BuildOf(installedVersion), Message = "" });
            var newest = await GitHubReleases.NewestAsync(http, options, ct);
            var newer = newest is not null && (Status.InstalledBuild is not { } installed || newest.Build > installed);
            var message = newest is null ? "There's no release yet."
                : !newer ? $"Up to date: {newest.Title} is the newest."
                : Status.FromPc ? $"This copy was built on the PC. The newest release is {newest.Title}."
                : $"{newest.Title} is ready to install.";
            Set(Status with { Step = newer ? UpdateStep.Available : UpdateStep.UpToDate, Latest = newest, Message = message, CheckedAt = time.GetUtcNow() });
            if (!newer) TryEmpty(installer.DownloadFolder);
        }
        catch (Exception e) when (Trouble(e, ct) is { } why)
        {
            Set(Status with { Step = UpdateStep.Failed, Message = why });
        }
        catch (OperationCanceledException)
        {
            Set(Status with { Step = Status.Latest is null ? UpdateStep.Unchecked : UpdateStep.Available, Message = "" });
            throw;
        }
        finally
        {
            busy.Release();
        }
    }

    /// <summary>Downloads the newest release, checks it, keeps copies of the app's data, and hands it to the device's installer.</summary>
    public async Task InstallAsync(CancellationToken ct = default)
    {
        if (Status.Latest is not { } release) return;
        if (!await busy.WaitAsync(0, ct)) return;
        try
        {
            TryEmpty(installer.DownloadFolder);
            Directory.CreateDirectory(installer.DownloadFolder);
            var file = Path.Combine(installer.DownloadFolder, options.Asset);
            Set(Status with { Step = UpdateStep.Downloading, Progress = 0, Message = $"Downloading {release.Title}…" });
            await DownloadAsync(release, file, ct);

            if (!await installer.SignedLikeInstalledAsync(file))
            {
                throw new UpdateException($"{release.Title} isn't signed with the same key as the app you have, so it can't install as an update. Nothing was installed.");
            }

            var kept = "";
            if (options.DataFiles.Count > 0)
            {
                try
                {
                    kept = $" A copy of your data is in {await installer.KeepCopiesAsync(options.DataFiles, options.BackupFolder, ct)}.";
                }
                catch (Exception e) when (e is not OperationCanceledException)
                {
                    throw new UpdateException($"A copy of your data couldn't be kept first ({e.Message}), so nothing was installed.");
                }
            }

            var outcome = await installer.InstallAsync(file, ct);
            Set(outcome.Result switch
            {
                InstallResult.Handed => Status with { Step = UpdateStep.Installing, Message = $"Confirm the update to {release.Title}.{kept}" },
                InstallResult.NeedsPermission => Status with { Step = UpdateStep.Available, Message = outcome.Message },
                _ => Status with { Step = UpdateStep.Failed, Message = outcome.Message },
            });
        }
        catch (Exception e) when (Trouble(e, ct) is { } why)
        {
            Set(Status with { Step = UpdateStep.Failed, Message = why });
        }
        catch (OperationCanceledException)
        {
            Set(Status with { Step = UpdateStep.Available, Message = "Stopped: nothing was installed." });
            throw;
        }
        finally
        {
            busy.Release();
        }
    }

    private async Task DownloadAsync(Release release, string file, CancellationToken ct)
    {
        var part = file + ".part";
        using var response = await http.GetAsync(release.Download, HttpCompletionOption.ResponseHeadersRead, ct);
        if (!response.IsSuccessStatusCode) throw new UpdateException($"GitHub answered {(int)response.StatusCode} {response.ReasonPhrase} for the file; try again later.");

        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        long done = 0;
        await using (var input = await response.Content.ReadAsStreamAsync(ct))
        await using (var output = File.Create(part))
        {
            var buffer = new byte[81920];
            var reported = 0.0;
            int read;
            while ((read = await input.ReadAsync(buffer, ct)) > 0)
            {
                await output.WriteAsync(buffer.AsMemory(0, read), ct);
                hash.AppendData(buffer, 0, read);
                done += read;
                var progress = release.Size > 0 ? Math.Min(1, (double)done / release.Size) : 0;
                if (progress - reported < 0.01) continue;
                reported = progress;
                Set(Status with { Progress = progress });
            }
        }

        if (done != release.Size) throw new UpdateException($"The download stopped at {Megabytes(done)} of {Megabytes(release.Size)}, so nothing was installed; try again.");
        if (release.Sha256 is { } expected && Convert.ToHexStringLower(hash.GetHashAndReset()) != expected)
        {
            throw new UpdateException("The download doesn't match the fingerprint GitHub gives for it, so nothing was installed; try again.");
        }
        File.Move(part, file, overwrite: true);
        Set(Status with { Progress = 1 });
    }

    /// <summary>A sentence for the screen on what went wrong, or null for a cancellation the caller asked for.</summary>
    private static string? Trouble(Exception e, CancellationToken ct) => e switch
    {
        OperationCanceledException when ct.IsCancellationRequested => null,
        UpdateException => e.Message,
        HttpRequestException => "GitHub couldn't be reached: check the connection.",
        OperationCanceledException => "GitHub took too long to answer; try again.",
        JsonException => "GitHub's answer couldn't be read; try again later.",
        IOException => $"The download couldn't be saved: {e.Message}",
        UnauthorizedAccessException => $"The download couldn't be saved: {e.Message}",
        _ => $"Something went wrong, and nothing was installed: {e.Message}",
    };

    private void Set(UpdateStatus status)
    {
        Status = status;
        Changed?.Invoke(status);
    }

    private static string Megabytes(long bytes) => (bytes / 1_000_000.0).ToString("0.#", CultureInfo.InvariantCulture) + " MB";

    private static void TryEmpty(string folder)
    {
        try
        {
            if (Directory.Exists(folder)) Directory.Delete(folder, recursive: true);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // A file held open by the installer: it goes next time.
        }
    }
}
