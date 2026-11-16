using AngleSharp.Dom;
using Bunit;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Prima.Core.Practice;
using Prima.UI.Pages;

namespace Prima.UI.Tests;

// Sight-read's look and its run: the run starts only on a piano, and the page goes on to it only once the play bar has started it.
public class SightReadTests
{
    // The look's end starts the run by itself, but not with nothing to play on: the bar says why, and the look and its Start stay,
    // for once the piano's in. Plugged in, nothing starts until Start is pressed.
    [Fact]
    public async Task TheLookRunningOutWithNoPianoStartsNothingSaysWhyAndKeepsTheLookAndStart()
    {
        var piano = new TestPiano();
        await using var host = new ComponentHost(keyboard: piano);
        host.Context.Services.GetRequiredService<PrimaSettings>().LookSeconds = 1;
        var view = host.Context.Render<SightRead>();
        await OpenAPassage(view);

        view.WaitForAssertion(() => Assert.Contains(view.FindAll("[role=status]"), s => s.TextContent == "Plug in your keyboard first."), TimeSpan.FromSeconds(5));
        Assert.NotNull(Key(view, "Start"));
        Assert.Null(Key(view, "Stop"));
        Assert.Contains(view.FindAll(".mu-readout-label"), l => l.TextContent == "Look");

        piano.Plug();
        await Task.Delay(1000, Xunit.TestContext.Current.CancellationToken);
        Assert.NotNull(Key(view, "Start"));
        Assert.Null(Key(view, "Stop"));
    }

    // With the piano in, the look's end still starts the run by itself: its count-in, with Stop to end it.
    [Fact]
    public async Task TheLookRunningOutWithThePianoInStillStartsTheRunByItself()
    {
        await using var host = new ComponentHost(keyboard: new TestPiano(plugged: true));
        host.Context.Services.GetRequiredService<PrimaSettings>().LookSeconds = 1;
        var view = host.Context.Render<SightRead>();
        await OpenAPassage(view);

        view.WaitForAssertion(() =>
        {
            Assert.NotNull(Key(view, "Stop"));
            Assert.Null(Key(view, "Start"));
            Assert.DoesNotContain(view.FindAll(".mu-readout-label"), l => l.TextContent == "Look");
        }, TimeSpan.FromSeconds(5));
    }

    // The owner, 15 November: a new Prima assumes level 1 and opens Sight-read on its usual screen, the level with − and +, ▶ for a
    // new passage and Find my level beside it; no first-time screen, no Start at level 1. Opening it keeps nothing.
    [Fact]
    public async Task ANewPrimaOpensSightReadOnItsNormalScreenAtLevelOne()
    {
        await using var host = new ComponentHost();
        var view = host.Context.Render<SightRead>();

        view.WaitForAssertion(() => Assert.Equal(["Level 1", $"{ReadingState.Start.Tempo:0} bpm"], view.FindAll(".reading-start .mu-screen-head span").Select(s => s.TextContent)));
        Assert.Equal("1", Readout(view, "Level"));
        Assert.Equal("1", view.Find(".control-row .control-value").TextContent);
        Assert.True(view.Find("[aria-label='An easier level']").HasAttribute("disabled"));
        Assert.False(view.Find("[aria-label='A harder level']").HasAttribute("disabled"));
        var keys = view.FindAll(".control-row-play button");
        Assert.Equal(["New passage", "Find my level"], keys.Select(k => k.GetAttribute("aria-label") ?? k.TextContent.Trim()));
        Assert.Contains("mu-primary", keys[0].ClassList);
        Assert.DoesNotContain("First time", view.Markup);
        Assert.DoesNotContain("Start at level 1", view.Markup);
        Assert.Null(await host.Store.GetTrackAsync<ReadingState>("reading"));

        // ▶ reads a passage at level 1.
        await view.InvokeAsync(() => keys[0].ClickAsync(new MouseEventArgs()));
        view.WaitForAssertion(() => Assert.Equal("1", Readout(view, ".play-bar", "Level")));
    }

    // The first screen's main key: a passage, and its look with Start in the play bar.
    private static async Task OpenAPassage(IRenderedComponent<SightRead> view)
    {
        await view.InvokeAsync(() => view.WaitForElement(".control-row-play .mu-primary").ClickAsync(new MouseEventArgs()));
        view.WaitForAssertion(() => Assert.NotNull(Key(view, "Start")));
    }

    private static IElement? Key(IRenderedComponent<SightRead> view, string words) => view.FindAll("button").FirstOrDefault(b => b.TextContent.Trim() == words);

    // A readout's figure, by its label: the first screen's, or in a part of the page.
    private static string? Readout(IRenderedComponent<SightRead> view, string label) => Readout(view, ".reading-start", label);

    private static string? Readout(IRenderedComponent<SightRead> view, string within, string label) =>
        view.FindAll($"{within} .mu-readout").FirstOrDefault(r => r.QuerySelector(".mu-readout-label")?.TextContent == label)?.QuerySelector(".mu-readout-value")?.TextContent;
}
