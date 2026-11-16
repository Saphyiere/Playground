using AngleSharp.Dom;
using Bunit;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
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

    // The first screen's main key: a passage, and its look with Start in the play bar.
    private static async Task OpenAPassage(IRenderedComponent<SightRead> view)
    {
        await view.InvokeAsync(() => view.WaitForElement(".control-row-play .mu-primary").ClickAsync(new MouseEventArgs()));
        view.WaitForAssertion(() => Assert.NotNull(Key(view, "Start")));
    }

    private static IElement? Key(IRenderedComponent<SightRead> view, string words) => view.FindAll("button").FirstOrDefault(b => b.TextContent.Trim() == words);
}
