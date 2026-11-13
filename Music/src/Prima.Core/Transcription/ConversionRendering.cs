namespace Prima.Core.Transcription;

/// <summary>Rendering owned by a conversion job, independent of its screen and web view.</summary>
public interface IConversionRenderer : IEngraver
{
    /// <summary>Checks the complete rendering path before a paid request; null means it is available.</summary>
    Task<string?> UnavailableReasonAsync(CancellationToken ct);

    /// <summary>Renders a rectangle of the immutable source PDF. Page numbers start at one; bounds are normalized.</summary>
    Task<byte[]> CropAsync(byte[] pdf, int page, SourceRectangle bounds, int width, CancellationToken ct);
}
