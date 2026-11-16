using AngleSharp.Dom;
using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Prima.UI.Host;
using Prima.UI.Pages;

namespace Prima.UI.Tests;

// A new Prima's first steps: the welcome, the timing, and on to Today.
public class WelcomeTests
{
    // The owner, 15 November: "it should go into the routine page once the tuning is done and they click done". From the welcome,
    // the timing screen leads on: once the offset is kept, Done (its main key then) goes to Today. Not before.
    [Fact]
    public async Task FromTheWelcomeTheTimingLeadsOnOnceTheOffsetIsKeptDoneGoesToToday()
    {
        var (piano, clock) = (new TestPiano(plugged: true), new SetClock());
        await using var host = new ComponentHost(keyboard: piano, clock: clock);
        var nav = host.Context.Services.GetRequiredService<NavigationManager>();
        var welcome = host.Context.Render<Welcome>();
        await welcome.InvokeAsync(() => Key(welcome, "Plug in the piano and set the timing")!.ClickAsync(new MouseEventArgs()));
        Assert.Equal("http://localhost/tools/timing?from=welcome", nav.Uri);

        var timing = host.Context.Render<Timing>();
        Assert.Null(Key(timing, "Done"));
        await Calibrate(host, timing, piano, clock);
        Assert.Null(Key(timing, "Done"));

        await timing.InvokeAsync(() => Key(timing, "Keep this offset")!.ClickAsync(new MouseEventArgs()));
        Assert.StartsWith("Kept.", timing.Find(".control-row-play .control-note").TextContent);
        Assert.Equal("Done", timing.Find(".control-row-play .mu-primary").TextContent);
        await timing.InvokeAsync(() => Key(timing, "Done")!.ClickAsync(new MouseEventArgs()));
        Assert.Equal("http://localhost/", nav.Uri);
    }

    // From Tools it's the tool it was: an offset kept there leads nowhere, and the screen is left by the rail.
    [Fact]
    public async Task FromToolsTheTimingKeepsItsOffsetWithNoDone()
    {
        var (piano, clock) = (new TestPiano(plugged: true), new SetClock());
        await using var host = new ComponentHost(keyboard: piano, clock: clock);
        host.Context.Services.GetRequiredService<NavigationManager>().NavigateTo("tools/timing");
        var timing = host.Context.Render<Timing>();
        await Calibrate(host, timing, piano, clock);

        await timing.InvokeAsync(() => Key(timing, "Keep this offset")!.ClickAsync(new MouseEventArgs()));
        Assert.StartsWith("Kept.", timing.Find(".control-row-play .control-note").TextContent);
        Assert.Null(Key(timing, "Done"));
        Assert.Equal("Again", timing.Find(".control-row-play .mu-primary").TextContent);
    }

    // With the timing set already, as after Start over, the welcome skips that step: its main key, the only one, goes straight to
    // Today, keeping what the welcome asked.
    [Fact]
    public async Task WithTheTimingAlreadySetTheWelcomesMainKeyGoesStraightToToday()
    {
        await using var host = new ComponentHost();
        var settings = host.Context.Services.GetRequiredService<PrimaSettings>();
        settings.CalibratedOn = new DateOnly(2026, 10, 26);
        var nav = host.Context.Services.GetRequiredService<NavigationManager>();
        nav.NavigateTo("welcome");
        var welcome = host.Context.Render<Welcome>();

        var keys = welcome.FindAll(".control-row-play button");
        var main = Assert.Single(keys);
        Assert.Equal("Go to Today", main.TextContent);
        Assert.Contains("mu-primary", main.ClassList);
        await welcome.InvokeAsync(() => main.ClickAsync(new MouseEventArgs()));

        Assert.Equal("http://localhost/", nav.Uri);
        var saved = new PrimaSettings();
        await host.Store.LoadAsync(saved);
        Assert.True(saved.Welcomed);
        Assert.Equal(20, saved.DailyMinutes);
    }

    // Start, a key on every click of the 16 a little after it, then the clock past the last: what was played is measured.
    private static async Task Calibrate(ComponentHost host, IRenderedComponent<Timing> timing, TestPiano piano, SetClock clock)
    {
        await timing.InvokeAsync(() => Key(timing, "Start")!.ClickAsync(new MouseEventArgs()));
        var click = host.Context.Services.GetRequiredService<Click>();
        var first = click.Plan!.FirstMusicBeat;
        for (var beat = first; beat < first + 16; beat++) piano.Play(60, click.BeatTime(beat) + 20_000_000);
        clock.NowNs = click.BeatTime(first + 17);
        timing.WaitForAssertion(() => Assert.NotNull(Key(timing, "Keep this offset")));
    }

    private static IElement? Key<T>(IRenderedComponent<T> view, string words) where T : IComponent =>
        view.FindAll("button").FirstOrDefault(b => b.TextContent.Trim() == words);
}
