using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;

namespace Playground.Updates;

/// <summary>
/// An app's updates from its GitHub releases: one per app, holding the <see cref="Status"/> its screens show.
/// <list type="bullet">
/// <item><see cref="CheckAsync"/> asks GitHub for the newest release, at most once in 20 hours unless asked again, so an app can
/// call it each time it starts.</item>
/// <item><see cref="InstallAsync"/> downloads it (once: tapped again, after Android asked to let the app install apps or after its
/// installer was cancelled, it hands over the file it has, checked again), checks it against the size and SHA-256 GitHub gives, checks it's signed with
/// the installed app's key, keeps a copy of the app's data somewhere an uninstall wouldn't reach (a full one through the app's
/// <see cref="IUpdateSafeguard"/>, or its data files), and hands it to the device's installer, which asks the person to confirm. A
/// step that fails stops it there, with nothing installed.</item>
/// </list>
/// A release's build is N in its version 1.0.N; a copy built on the PC is 1.0, older than any release.
/// </summary>
public sealed class Updater
{
    private static readonly TimeSpan Daily = TimeSpan.FromHours(20);

    private readonly UpdateOptions options;
    private readonly IInstaller installer;
    private readonly IUpdateSafeguard? safeguard;
    private readonly TimeProvider time;
    private readonly HttpClient http;
    private readonly SemaphoreSlim busy = new(1, 1);

    public Updater(UpdateOptions options, IInstaller installer, TimeProvider? time = null, HttpMessageHandler? handler = null, IUpdateSafeguard? safeguard = null)
    {
        this.options = options;
        this.installer = installer;
        this.safeguard = safeguard;
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
            Set(Status with { Step = UpdateStep.Checking, Installed = installedVersion, InstalledBuild = BuildOf(installedVersion), Message = "", CopyProblem = null });
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
    public Task InstallAsync(CancellationToken ct = default) => InstallAsync(withoutCopy: false, ct);

    /// <summary>
    /// The newest release without the copy of the app's data first: only when asked again, after that copy couldn't be made
    /// (<see cref="UpdateStatus.CopyProblem"/>), knowing there'll be nothing to go back to.
    /// </summary>
    public Task InstallWithoutCopyAsync(CancellationToken ct = default) => InstallAsync(withoutCopy: true, ct);

    private async Task InstallAsync(bool withoutCopy, CancellationToken ct)
    {
        if (Status.Latest is not { } release) return;
        if (!await busy.WaitAsync(0, ct)) return;
        try
        {
            var file = Path.Combine(installer.DownloadFolder, options.Asset);
            if (await DownloadedAsync(release, file, ct))
            {
                // Downloaded and checked a moment ago, before Android asked to let the app install apps or before its installer was
                // cancelled: handed over again as it is, not downloaded again.
                Set(Status with { Step = UpdateStep.Downloading, Progress = 1, Message = $"{release.Title} is downloaded already.", CopyProblem = null });
            }
            else
            {
                TryEmpty(installer.DownloadFolder);
                Directory.CreateDirectory(installer.DownloadFolder);
                Set(Status with { Step = UpdateStep.Downloading, Progress = 0, Message = $"Downloading {release.Title}…", CopyProblem = null });
                await DownloadAsync(release, file, ct);
            }

            if (!await installer.SignedLikeInstalledAsync(file))
            {
                throw new UpdateException($"{release.Title} isn't signed with the same key as the app you have, so it can't install as an update. Nothing was installed.");
            }

            var kept = withoutCopy ? " No copy of your data was kept first." : "";
            if (!withoutCopy && (safeguard is not null || options.DataFiles.Count > 0))
            {
                try
                {
                    // The download is done (its progress stays full): what's left before Android's installer is the copy, whose
                    // progress is told in the sentence, reported on the copy's own thread so it's all said before the next step.
                    Set(Status with { Message = "Keeping a copy of your data first…" });
                    var shown = -1;
                    var where = safeguard is not null
                        ? await safeguard.KeepBeforeUpdateAsync(release, new Inline<double>(p =>
                        {
                            var percent = (int)(Math.Clamp(p, 0, 1) * 100);
                            if (percent == shown) return;
                            shown = percent;
                            Set(Status with { Message = $"Keeping a copy of your data first… {percent}%" });
                        }), ct)
                        : await installer.KeepCopiesAsync(options.DataFiles, options.BackupFolder, ct);
                    kept = $" A copy of your data is in {where}.";
                }
                catch (Exception e) when (e is not OperationCanceledException)
                {
                    // Nothing installs without its copy unless asked again, knowing there'd be nothing to go back to: why is kept for
                    // the screen, which then offers that.
                    Set(Status with { Step = UpdateStep.Failed, Message = $"A copy of your data couldn't be kept first ({e.Message}), so nothing was installed.", CopyProblem = e.Message });
                    return;
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
        // Which release the file is, so Install tapped again knows it has it.
        await File.WriteAllTextAsync(file + ReleaseNote, release.Tag, ct);
        Set(Status with { Progress = 1 });
    }

    // Beside a finished download, the tag of the release it is.
    private const string ReleaseNote = ".release";

    /// <summary>
    /// Whether the file is this release's, downloaded and checked before: noted as that release's as its download finished, the
    /// size GitHub gives, and the SHA-256 it gives, read again in case the file changed since. Anything else is downloaded afresh.
    /// </summary>
    private static async Task<bool> DownloadedAsync(Release release, string file, CancellationToken ct)
    {
        try
        {
            if (!File.Exists(file) || !File.Exists(file + ReleaseNote)) return false;
            if (await File.ReadAllTextAsync(file + ReleaseNote, ct) != release.Tag || new FileInfo(file).Length != release.Size) return false;
            if (release.Sha256 is not { } expected) return true;
            await using var stream = File.OpenRead(file);
            return Convert.ToHexStringLower(await SHA256.HashDataAsync(stream, ct)) == expected;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return false;
        }
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

    /// <summary>Progress reported on the thread doing the work, unlike <see cref="Progress{T}"/>, which posts it for later.</summary>
    private sealed class Inline<T>(Action<T> report) : IProgress<T>
    {
        public void Report(T value) => report(value);
    }

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
