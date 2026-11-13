using Bunit;
using Microsoft.AspNetCore.Components.Web;
using Prima.Core.Transcription;
using Prima.UI.Components;
using Prima.UI.Transcription;

namespace Prima.UI.Tests;

public class SourceBarsTests
{
    [Fact]
    public async Task CorrectedBoundariesKeepTheirExactPageTransformAndRequireExplicitConfirmation()
    {
        await using var host = new ComponentHost();
        (ConversionSource Source, bool MapReading)? confirmed = null;
        var cuts = new Dictionary<(int, int), PdfPages.SystemCut>
        {
            [(1, 1)] = new([1], "blob:source", [0.2, 0.4, 0.6, 0.8], new(0.2, 0.3, 0.6, 0.2)),
        };
        var view = host.Context.Render<SourceBars>(p => p.Add(c => c.SourceHash, new string('a', 64)).Add(c => c.Cuts, cuts)
            .Add(c => c.ReadingCounts, new Dictionary<(int, int), int> { [(1, 1)] = 3 })
            .Add(c => c.OnConfirmed, value => confirmed = value));

        Assert.All(view.FindAll("button, input"), control => Assert.Equal("off", control.GetAttribute("data-piano")));
        Assert.Null(confirmed);
        view.Find("[data-boundary='1']").Click();
        view.Find("[data-action=confirm-reading-map]").Click();
        Assert.Equal("true", view.Find("[data-action=confirm-reading-map]").GetAttribute("aria-pressed"));
        view.Find("input[type=range]").Input("0.4");
        Assert.Equal("false", view.Find("[data-action=confirm-reading-map]").GetAttribute("aria-pressed"));
        Assert.Null(confirmed);
        view.Find("[data-action=confirm-source]").Click();

        Assert.NotNull(confirmed);
        var source = confirmed.Value.Source;
        Assert.False(confirmed.Value.MapReading);
        Assert.Empty(source.Problems());
        Assert.Equal(3, source.Bars.Count);
        Assert.Equal(0.2, source.Bars[0].Bounds.X, 8);
        Assert.Equal(0.24, source.Bars[0].Bounds.Width, 8);
        Assert.Equal(0.44, source.Bars[1].Bounds.X, 8);
        Assert.All(source.Bars, b => Assert.Equal((0.3, 0.2), (b.Bounds.Y, b.Bounds.Height)));
        Assert.Equal(new[] { 0, 1, 2 }, source.Bars.Select(b => b.Order));
        Assert.Equal(3, source.Bars.Select(b => b.Id).Distinct().Count());
        Assert.Equal(0, host.Claude.Calls);

        var reopened = host.Context.Render<SourceBars>(p => p.Add(c => c.SourceHash, source.Sha256).Add(c => c.Cuts, cuts)
            .Add(c => c.Source, source).Add(c => c.OnConfirmed, value => confirmed = value));
        await reopened.Find("[data-action=confirm-source]").ClickAsync(new MouseEventArgs());
        Assert.Equal(source.Bars.Select(b => b.Id), confirmed.Value.Source.Bars.Select(b => b.Id));
        Assert.Equal(source.Bars.Select(b => b.Bounds), confirmed.Value.Source.Bars.Select(b => b.Bounds));
        Assert.NotEqual(source.Revision, confirmed.Value.Source.Revision);
    }

    [Fact]
    public async Task AddingAndRemovingDividersControlsWhetherTheExistingReadingCanBeMapped()
    {
        await using var host = new ComponentHost();
        (ConversionSource Source, bool MapReading)? confirmed = null;
        var cuts = new Dictionary<(int, int), PdfPages.SystemCut>
        {
            [(1, 1)] = new([1], "blob:source", [0.1, 0.5, 0.9], new(0.1, 0.2, 0.8, 0.3)),
        };
        var view = host.Context.Render<SourceBars>(p => p.Add(c => c.SourceHash, new string('a', 64)).Add(c => c.Cuts, cuts)
            .Add(c => c.ReadingCounts, new Dictionary<(int, int), int> { [(1, 1)] = 2 })
            .Add(c => c.OnConfirmed, value => confirmed = value));

        view.Find("[data-action=boundary-add]").Click();
        Assert.Equal(4, view.FindAll("[data-boundary]").Count);
        Assert.NotNull(view.Find("[data-action=confirm-reading-map][disabled]"));
        view.Find("[data-action=boundary-remove]").Click();
        Assert.Equal(3, view.FindAll("[data-boundary]").Count);
        Assert.NotNull(view.Find("[data-action=confirm-reading-map]:not([disabled])"));
        Assert.Equal("false", view.Find("[data-action=confirm-reading-map]").GetAttribute("aria-pressed"));
        view.Find("[data-action=confirm-reading-map]").Click();
        await view.Find("[data-action=confirm-source]").ClickAsync(new MouseEventArgs());

        Assert.True(confirmed!.Value.MapReading);
        Assert.Equal(2, confirmed.Value.Source.Bars.Count);
        Assert.Equal(0, host.Claude.Calls);
    }
}
