using Prima.Core.Transcription;

namespace Prima.UI.Host;

/// <summary>Starts user-requested work in the platform's supported execution lifetime.</summary>
public interface IConversionHost
{
    Task StartAsync(string jobId);
}

internal sealed class NoConversionHost : IConversionHost, IConversionRenderer
{
    private const string Unavailable = "Background conversion is not available in this host.";
    public Task StartAsync(string jobId) => Task.FromException(new NotSupportedException(Unavailable));
    public Task<string?> UnavailableReasonAsync(CancellationToken ct) => Task.FromResult<string?>(Unavailable);
    public Task<byte[]> CropAsync(byte[] pdf, int page, SourceRectangle bounds, int width, CancellationToken ct) => Task.FromException<byte[]>(new NotSupportedException(Unavailable));
    public Task<byte[]> EngraveAsync(string mei, int widthPx, CancellationToken ct) => Task.FromException<byte[]>(new NotSupportedException(Unavailable));
}
