using Com.Saphyiere.Prima.Conversion;
using Prima.Core.Transcription;

namespace Prima.Tablet;

/// <summary>Runs native PDF and notation rendering with application context, never the current Activity or a WebView.</summary>
public sealed class AndroidConversionRenderer : IConversionRenderer
{
    // Android permits one JavaScript sandbox per application. All callers share this gate, including preflight checks.
    private static readonly SemaphoreSlim gate = new(1, 1);

    public Task<string?> UnavailableReasonAsync(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (!OperatingSystem.IsAndroidVersionAtLeast(26)) return Task.FromResult<string?>("Background PDF conversion requires Android 8 or later.");
        return RunAsync<string?>(renderer => renderer.CheckSupport(), ct);
    }

    public Task<byte[]> EngraveAsync(string mei, int widthPx, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(mei);
        return RunAsync(renderer => renderer.Engrave(mei, widthPx) ?? throw new InvalidOperationException("The notation renderer returned no image."), ct);
    }

    public Task<byte[]> CropAsync(byte[] pdf, int page, SourceRectangle bounds, int width, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(pdf);
        ArgumentNullException.ThrowIfNull(bounds);
        if (pdf.Length == 0) throw new ArgumentException("The source PDF is empty.", nameof(pdf));
        if (page < 1) throw new ArgumentOutOfRangeException(nameof(page));
        if (!bounds.IsValid) throw new ArgumentOutOfRangeException(nameof(bounds));
        return RunAsync(renderer => renderer.Crop(pdf, page, bounds.X, bounds.Y, bounds.Width, bounds.Height, width) ?? throw new InvalidOperationException("The PDF renderer returned no image."), ct);
    }

    private static async Task<T> RunAsync<T>(Func<NativeRenderer, T> render, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (!OperatingSystem.IsAndroidVersionAtLeast(26)) throw new NotSupportedException("Background PDF conversion requires Android 8 or later.");
        await gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            using var renderer = new NativeRenderer(Android.App.Application.Context);
            using var cancellation = ct.Register(renderer.Cancel);
            try
            {
                var result = await Task.Run(() => render(renderer), CancellationToken.None).ConfigureAwait(false);
                ct.ThrowIfCancellationRequested();
                return result;
            }
            catch (Exception error) when (ct.IsCancellationRequested)
            {
                throw new OperationCanceledException("Conversion rendering was cancelled.", error, ct);
            }
        }
        finally { gate.Release(); }
    }
}
