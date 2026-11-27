using AngleSharp.Dom;
using Bunit;
using Bunit.TestDoubles;
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
        await welcome.InvokeAsync(() => Key(welcome, "calibrate")!.ClickAsync(new MouseEventArgs()));
        Assert.Equal("http://localhost/tools/timing?from=welcome", nav.Uri);

        var timing = host.Context.Render<Timing>();
        Assert.Null(Key(timing, "finish"));
        await Calibrate(host, timing, piano, clock);
        Assert.Null(Key(timing, "finish"));

        await timing.InvokeAsync(() => Key(timing, "save")!.ClickAsync(new MouseEventArgs()));
        Assert.NotNull(host.Context.Services.GetRequiredService<PrimaSettings>().CalibratedOn);
        Assert.Equal("finish", timing.Find(".control-row-play .mu-primary").GetAttribute("data-action"));
        await timing.InvokeAsync(() => Key(timing, "finish")!.ClickAsync(new MouseEventArgs()));
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

        await timing.InvokeAsync(() => Key(timing, "save")!.ClickAsync(new MouseEventArgs()));
        Assert.NotNull(host.Context.Services.GetRequiredService<PrimaSettings>().CalibratedOn);
        Assert.Null(Key(timing, "finish"));
        Assert.Equal("start", timing.Find(".control-row-play .mu-primary").GetAttribute("data-action"));
    }

    // The owner, 15 November: no tour or guide yet, only these steps leading on cleanly. A new Prima opening on Today goes to the
    // welcome, the welcome's main key to the timing, the timing's Done to Today, and Today then stays: nothing else comes between.
    [Fact]
    public async Task ANewPrimasFirstStepsAreOnlyTheWelcomeTheTimingAndToday()
    {
        var (piano, clock) = (new TestPiano(plugged: true), new SetClock());
        await using var host = new ComponentHost(keyboard: piano, clock: clock);
        var nav = (BunitNavigationManager)host.Context.Services.GetRequiredService<NavigationManager>();

        host.Context.Render<Today>();
        Assert.Equal("http://localhost/welcome", nav.Uri);
        var welcome = host.Context.Render<Welcome>();
        await welcome.InvokeAsync(() => welcome.Find(".control-row-play .mu-primary").ClickAsync(new MouseEventArgs()));
        var timing = host.Context.Render<Timing>();
        await Calibrate(host, timing, piano, clock);
        await timing.InvokeAsync(() => Key(timing, "save")!.ClickAsync(new MouseEventArgs()));
        await timing.InvokeAsync(() => timing.Find(".control-row-play .mu-primary").ClickAsync(new MouseEventArgs()));
        var today = host.Context.Render<Today>();
        today.WaitForAssertion(() => Assert.NotEmpty(today.FindAll(".today-steps .mu-step")));

        Assert.Equal("http://localhost/", nav.Uri);
        Assert.Equal(["welcome", "tools/timing?from=welcome", ""], nav.History.Reverse().Select(h => h.Uri));
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
        Assert.Equal("go-today", main.GetAttribute("data-action"));
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
        await timing.InvokeAsync(() => Key(timing, "start")!.ClickAsync(new MouseEventArgs()));
        var click = host.Context.Services.GetRequiredService<Click>();
        var first = click.Plan!.FirstMusicBeat;
        for (var beat = first; beat < first + 16; beat++) piano.Play(60, click.BeatTime(beat) + 20_000_000);
        clock.NowNs = click.BeatTime(first + 17);
        timing.WaitForAssertion(() => Assert.NotNull(Key(timing, "save")));
    }

    private static IElement? Key<T>(IRenderedComponent<T> view, string action) where T : IComponent =>
        view.FindAll($"button[data-action={action}]").SingleOrDefault();
}
