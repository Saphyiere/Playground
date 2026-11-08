using Microsoft.JSInterop;
using Prima.Core.Transcription;

namespace Prima.UI.Transcription;

/// <summary>A system's reading engraved as one system: its picture, a URL to show it by, and each bar's left and right edge as shares of its width.</summary>
public sealed record Engraving(byte[] Png, string Url, double[][] Bars);

/// <summary>
/// Engraves readings with the sheet's Verovio, in the web view: as a PNG for Claude to set beside the print, and with its bars for the
/// review to show under the print. Only music goes to the web view, never the key.
/// </summary>
public sealed class VerovioEngraver(IJSRuntime js) : IEngraver
{
    private IJSObjectReference? module;
    private readonly List<string> urls = [];

    public async Task<Engraving> EngraveAsync(string mei, int widthPx)
    {
        module ??= await js.InvokeAsync<IJSObjectReference>("import", "./_content/Prima.UI/Components/Sheet.razor.js");
        var engraving = await module.InvokeAsync<Engraving>("engrave", mei, widthPx);
        lock (urls) urls.Add(engraving.Url);
        return engraving;
    }

    async Task<byte[]> IEngraver.EngraveAsync(string mei, int widthPx, CancellationToken ct) => (await EngraveAsync(mei, widthPx)).Png;

    /// <summary>The URLs of everything engraved, to let go of when the screen closes.</summary>
    public IReadOnlyList<string> Urls
    {
        get
        {
            lock (urls) return [.. urls];
        }
    }
}
