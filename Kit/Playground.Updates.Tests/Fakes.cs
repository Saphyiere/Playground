using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Playground.Updates.Tests;

/// <summary>Stands in for GitHub: the releases list from its API, and the files they link to.</summary>
sealed class FakeGitHub : HttpMessageHandler
{
    public List<string> Requests { get; } = [];

    public List<object> Releases { get; } = [];

    public Dictionary<string, byte[]> Files { get; } = [];

    /// <summary>The next reply, once, in place of the usual one.</summary>
    public Func<HttpResponseMessage>? Next { get; set; }

    /// <summary>A release on the list, with its file served at its download address.</summary>
    public void Release(string tag, byte[] file, string asset = "prima.apk", bool draft = false, bool prerelease = false, string? digest = null, long? size = null)
    {
        var url = $"https://github.com/saphyiere/playground/releases/download/{tag}/{asset}";
        Files[url] = file;
        Releases.Add(new
        {
            tag_name = tag,
            name = $"Prima {tag[(tag.IndexOf('-') + 1)..]}",
            body = "Changes since the last one.",
            draft,
            prerelease,
            published_at = "2026-11-08T10:00:00Z",
            assets = new[]
            {
                new { name = asset, size = size ?? file.Length, browser_download_url = url, digest = digest ?? "sha256:" + Convert.ToHexStringLower(SHA256.HashData(file)) },
            },
        });
    }

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var url = request.RequestUri!.ToString();
        Requests.Add(url);
        if (Next is { } next)
        {
            Next = null;
            return Task.FromResult(next());
        }
        if (url.StartsWith("https://api.github.com/", StringComparison.Ordinal))
        {
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(JsonSerializer.Serialize(Releases), Encoding.UTF8, "application/json") });
        }
        return Task.FromResult(Files.TryGetValue(url, out var bytes) ? new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(bytes) } : new HttpResponseMessage(HttpStatusCode.NotFound));
    }
}

/// <summary>Stands in for the device: keeps each thing asked of it, in order.</summary>
sealed class FakeInstaller(string version) : IInstaller, IDisposable
{
    public string InstalledVersion { get; set; } = version;

    public string DownloadFolder { get; } = Path.Combine(Path.GetTempPath(), "playground-updates-tests", Guid.NewGuid().ToString("N"));

    public bool SameKey { get; set; } = true;

    public Exception? CopiesFail { get; set; }

    public InstallOutcome Outcome { get; set; } = new(InstallResult.Handed, "");

    public List<string> Calls { get; } = [];

    /// <summary>The file handed to the installer.</summary>
    public byte[]? Handed { get; private set; }

    public event Action<string>? Stopped;

    /// <summary>The device's installer stops without installing, as when the person cancels.</summary>
    public void Stop(string why) => Stopped?.Invoke(why);

    public Task<bool> SignedLikeInstalledAsync(string file)
    {
        Calls.Add("signature");
        return Task.FromResult(SameKey);
    }

    public Task<string> KeepCopiesAsync(IReadOnlyList<string> files, string folder, CancellationToken ct)
    {
        Calls.Add($"copies of {string.Join(", ", files)}");
        if (CopiesFail is { } e) throw e;
        return Task.FromResult($"Downloads/{folder}");
    }

    public Task<InstallOutcome> InstallAsync(string file, CancellationToken ct)
    {
        Calls.Add("install");
        Handed = File.ReadAllBytes(file);
        return Task.FromResult(Outcome);
    }

    public void Dispose()
    {
        if (Directory.Exists(DownloadFolder)) Directory.Delete(DownloadFolder, recursive: true);
    }
}

sealed class Clock : TimeProvider
{
    public DateTimeOffset Now { get; set; } = new(2026, 11, 8, 10, 0, 0, TimeSpan.Zero);

    public override DateTimeOffset GetUtcNow() => Now;
}
