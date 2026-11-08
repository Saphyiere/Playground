using System.Globalization;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Playground.Updates;

/// <summary>
/// GitHub and the device stood in for, for an app's preview host, so its update screens can be checked without either: a made-up
/// newest release, and installs that only pretend. <see cref="Show"/> sets what's installed and what GitHub has; the app's
/// screen then checks again.
/// </summary>
public sealed class StandInUpdates(UpdateOptions options, string name) : HttpMessageHandler, IInstaller
{
    private static readonly byte[] App = Encoding.UTF8.GetBytes("Not an app: a preview's stand-in for one.");
    private int newest;
    private string notes = "";

    public string InstalledVersion { get; private set; } = "1.0";

    public string DownloadFolder { get; } = Path.Combine(Path.GetTempPath(), "playground-stand-in-updates", options.Tag);

    public event Action<string>? Stopped;

    /// <summary>What's installed (1.0.N from a release, 1.0 from the PC), and the newest release's build and notes; 0 for none yet.</summary>
    public void Show(string installed, int newestBuild, string releaseNotes = "")
    {
        InstalledVersion = installed;
        newest = newestBuild;
        notes = releaseNotes;
    }

    /// <summary>As if the person cancelled Android's installer.</summary>
    public void Cancel() => Stopped?.Invoke("The update was cancelled: nothing was installed.");

    public Task<bool> SignedLikeInstalledAsync(string file) => Task.FromResult(true);

    public Task<string> KeepCopiesAsync(IReadOnlyList<string> files, string folder, CancellationToken ct) => Task.FromResult($"Download/{folder}");

    public Task<InstallOutcome> InstallAsync(string file, CancellationToken ct) => Task.FromResult(new InstallOutcome(InstallResult.Handed, ""));

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        if (request.RequestUri!.Host != "api.github.com") return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(App) });

        var tag = $"{options.Tag}-1.0.{newest.ToString(CultureInfo.InvariantCulture)}";
        object[] releases = newest <= 0 ? [] :
        [
            new
            {
                tag_name = tag,
                name = $"{name} 1.0.{newest.ToString(CultureInfo.InvariantCulture)}",
                body = notes,
                draft = false,
                prerelease = false,
                published_at = DateTimeOffset.UtcNow.AddDays(-1).ToString("o", CultureInfo.InvariantCulture),
                assets = new[]
                {
                    new
                    {
                        name = options.Asset,
                        size = App.Length,
                        browser_download_url = $"https://github.com/{options.Repository}/releases/download/{tag}/{options.Asset}",
                        digest = "sha256:" + Convert.ToHexStringLower(SHA256.HashData(App)),
                    },
                },
            },
        ];
        return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(JsonSerializer.Serialize(releases), Encoding.UTF8, "application/json") });
    }
}

public static class StandInUpdatesExtensions
{
    /// <summary>The app's <see cref="Updater"/> for a preview host, with <see cref="StandInUpdates"/> for GitHub and the device.</summary>
    public static IServiceCollection AddStandInUpdates(this IServiceCollection services, UpdateOptions options, string name)
    {
        var standIn = new StandInUpdates(options, name);
        services.AddSingleton(standIn);
        services.AddSingleton<IInstaller>(standIn);
        services.TryAddSingleton(TimeProvider.System);
        services.AddSingleton(provider => new Updater(options, standIn, provider.GetRequiredService<TimeProvider>(), standIn));
        return services;
    }
}
