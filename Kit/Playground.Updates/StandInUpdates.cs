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
    // As big as a real release (Prima's is 53 MB), all zeros, sent in about three seconds so the download's progress shows.
    private const long Size = 53_000_000;
    private static readonly Lazy<string> Digest = new(() =>
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var block = new byte[1_000_000];
        for (var i = 0; i < Size / block.Length; i++) hash.AppendData(block);
        return Convert.ToHexStringLower(hash.GetHashAndReset());
    });

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
        if (request.RequestUri!.Host != "api.github.com") return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StreamContent(new Zeros()) });

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
                        size = Size,
                        browser_download_url = $"https://github.com/{options.Repository}/releases/download/{tag}/{options.Asset}",
                        digest = "sha256:" + Digest.Value,
                    },
                },
            },
        ];
        return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(JsonSerializer.Serialize(releases), Encoding.UTF8, "application/json") });
    }

    /// <summary>The release's file: <see cref="Size"/> zeros, a pause at each megabyte.</summary>
    private sealed class Zeros : Stream
    {
        private long position;

        public override bool CanRead => true;

        public override bool CanSeek => false;

        public override bool CanWrite => false;

        public override long Length => Size;

        public override long Position { get => position; set => throw new NotSupportedException(); }

        public override int Read(byte[] buffer, int offset, int count) => Fill(buffer.AsSpan(offset, count));

        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            if (position / 1_000_000 != (position + buffer.Length) / 1_000_000) await Task.Delay(50, cancellationToken);
            return Fill(buffer.Span);
        }

        private int Fill(Span<byte> buffer)
        {
            var count = (int)Math.Min(buffer.Length, Size - position);
            buffer[..count].Clear();
            position += count;
            return count;
        }

        public override void Flush()
        {
        }

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
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
