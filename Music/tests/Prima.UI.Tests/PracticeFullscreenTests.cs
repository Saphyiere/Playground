using System.Text.Json;
using Bunit;
using Microsoft.AspNetCore.Components.Web;
using Prima.UI.Components;
using Prima.UI.Pages;
using Prima.UI.Storage;

namespace Prima.UI.Tests;

public class PracticeFullscreenTests
{
    [Fact]
    public async Task EnteringPracticeUsesFullscreenAndManualToggleStillWorks()
    {
        await using var host = new ComponentHost();
        var piece = await host.AddPiece();
        var view = host.Context.Render<Piece>(p => p.Add(c => c.Id, piece.Id));
        view.WaitForElement(".piece-front-bar .mu-primary").Click();

        view.WaitForAssertion(() => Assert.Equal([true], host.Browser.Fullscreen));
        Assert.Equal([false], host.Bars.Shown);
        view.Find(".piece-actions .piece-tool[aria-pressed]:last-child").Click();
        view.WaitForAssertion(() => Assert.Equal([true, false], host.Browser.Fullscreen));
        Assert.Equal([false, true], host.Bars.Shown);
    }

    // On the tablet, Practice tapped as the piece was still opening could reach the page's drawing of the bars before Practice was
    // on the page: bars.js was handed a strip that wasn't there yet, threw, and the whole page stopped answering taps.
    [Fact]
    public async Task PracticeTappedAsThePieceOpensDrawsTheBarsOnlyOnceTheStripIsOnThePage()
    {
        await using var host = new ComponentHost();
        var piece = await host.AddPiece();
        var (keys, full) = (new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously), new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously));
        (host.Browser.ListenKeysReady, host.Browser.FullScreenReady) = (keys.Task, full.Task);
        try
        {
            var view = host.Context.Render<Piece>(p => p.Add(c => c.Id, piece.Id));

            // Practice tapped while the page is still setting itself up (listening for keys, its last step); it's going full screen
            // when the setting up ends.
            view.WaitForElement(".piece-front-bar .mu-primary");
            view.WaitForAssertion(() => Assert.Equal(1, host.Browser.ListenKeysCalls));
            view.Find(".piece-front-bar .mu-primary").Click();
            // Nothing's drawn as it goes full screen, to wake a wait for it (WaitForAssertion's): so it's looked for until it's there.
            await Eventually(() => Assert.Equal([true], host.Browser.Fullscreen));
            keys.SetResult();
            await Eventually(() => Assert.NotEmpty(host.Browser.BarStrips));
            full.SetResult();

            Assert.All(host.Browser.BarStrips, strip => Assert.NotNull(strip.Id));
            await Eventually(() => Assert.Equal([false], host.Bars.Shown));
        }
        finally
        {
            keys.TrySetResult();
            full.TrySetResult();
        }
    }

    private static async Task Eventually(Action assertion)
    {
        for (var waited = 0; ; waited += 50)
        {
            try
            {
                assertion();
                return;
            }
            catch (Xunit.Sdk.XunitException) when (waited < 5000)
            {
                await Task.Delay(50);
            }
        }
    }

    // Practice tapped before the page could go full screen at all: it does as soon as it can, not never.
    [Fact]
    public async Task PracticeTappedBeforeThePageCanGoFullScreenGoesFullScreenOnceItCan()
    {
        await using var host = new ComponentHost();
        var piece = await host.AddPiece();
        var scripts = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        host.Browser.ImportsReady = scripts.Task;
        try
        {
            var view = host.Context.Render<Piece>(p => p.Add(c => c.Id, piece.Id));
            view.WaitForElement(".piece-front-bar .mu-primary").Click();
            view.WaitForElement(".bar-strip");
            Assert.Empty(host.Browser.Fullscreen);

            scripts.SetResult();
            view.WaitForAssertion(() => Assert.Equal([true], host.Browser.Fullscreen), TimeSpan.FromSeconds(5));
            Assert.Equal([false], host.Bars.Shown);
        }
        finally { scripts.TrySetResult(); }
    }

    [Fact]
    public async Task RestoringPracticeAndReturningFromLookBackUsesFullscreen()
    {
        await using var host = new ComponentHost();
        var piece = await host.AddPiece();
        await host.Store.StartSessionAsync(piece.Id, "custom", DateTimeOffset.Now);
        var view = host.Context.Render<Piece>(p => p.Add(c => c.Id, piece.Id));

        view.WaitForAssertion(() => Assert.Equal([true], host.Browser.Fullscreen));
        await view.InvokeAsync(() => view.FindComponent<PracticeStrip>().Instance.OnFinish.InvokeAsync());
        view.WaitForAssertion(() => Assert.Equal([true, false], host.Browser.Fullscreen));
        await view.InvokeAsync(() => view.FindComponent<LookBack>().Instance.OnKeep.InvokeAsync());
        view.WaitForAssertion(() => Assert.Equal([true, false, true], host.Browser.Fullscreen));
        Assert.Equal([false, true, false], host.Bars.Shown);
    }

    // The owner, 17 November: done, in full screen, there was no easy way to finish ("all the way to the left ... left of those
    // buttons in the bar", "no piano button, no nothing with this", and no timer). Full screen has Finish practice first in the
    // bar, a plain key only a tap presses; outside full screen the strip's is the only one, as before.
    [Fact]
    public async Task FinishPracticeIsFirstInThePlayBarOnlyInFullScreenAndNoPianoKeyPressesIt()
    {
        await using var host = new ComponentHost();
        var piece = await host.AddPiece();
        var view = host.Context.Render<Piece>(p => p.Add(c => c.Id, piece.Id));
        view.WaitForElement(".piece-front-bar .mu-primary");
        Assert.Empty(view.FindAll(".piece-finish"));

        await view.InvokeAsync(() => view.Find(".piece-front-bar .mu-primary").ClickAsync(new MouseEventArgs()));
        view.WaitForAssertion(() => Assert.Equal([true], host.Browser.Fullscreen));

        // After the title's row (portrait's row of its own; landscape has no title) and before the level and the piece's tools.
        var finish = view.WaitForElement(".play-bar > .piece-title-row + .piece-finish + .piece-level").PreviousElementSibling!;
        Assert.Equal("Finish practice", finish.TextContent);
        Assert.Equal("off", finish.GetAttribute("data-piano"));
        Assert.DoesNotContain("mu-primary", finish.ClassList);

        await view.InvokeAsync(() => view.Find("[aria-label='Leave full screen']").ClickAsync(new MouseEventArgs()));
        view.WaitForAssertion(() => Assert.Equal([true, false], host.Browser.Fullscreen));
        Assert.Empty(view.FindAll(".piece-finish"));
        Assert.Equal("Finish practice", view.Find(".reading-head .practice-strip button").TextContent);

        await view.InvokeAsync(() => view.Find("[aria-label='Full screen']").ClickAsync(new MouseEventArgs()));
        view.WaitForAssertion(() => Assert.Equal([true, false, true], host.Browser.Fullscreen));
        view.WaitForElement(".play-bar .piece-finish");
    }

    // Pressed, it's the strip's Finish practice: out of full screen (the tablet's bars back), the session finished, and its look
    // back, saying what LookBackTests' does of the same session finished from the strip.
    [Fact]
    public async Task FinishPracticeInFullScreenLeavesItAndOpensTheLookBackAsTheStripsDoes()
    {
        await using var host = new ComponentHost();
        var piece = await host.AddPiece();
        var session = await host.Store.StartSessionAsync(piece.Id, "standard", DateTimeOffset.Now.AddMinutes(-5));
        var at = DateTimeOffset.Now.AddMinutes(-2);
        await host.Store.AddAttemptAsync(new AttemptRecord(0, at, DateOnly.FromDateTime(at.LocalDateTime), "piece", "in time", piece.Id, 1, 72, null, null, "[]", null,
            JsonSerializer.Serialize(new { inTime = 0.0, right = 0, wrong = 1, missed = 0, from = 1, stopped = false, bar = 1, hands = "both", session, took = 12.5, timing = new[] { -8.0, 3.5 }, trouble = new[] { new[] { 1, 1, 0, 0, 0, 0, 0, 0 } } }), 0, false, null));
        var view = host.Context.Render<Piece>(p => p.Add(c => c.Id, piece.Id));

        // The session left going opens paused, full screen, under its cover: carried on, then finished from the bar.
        view.WaitForAssertion(() => Assert.Equal([true], host.Browser.Fullscreen));
        view.WaitForElement(".pause-keys .mu-primary");
        await view.InvokeAsync(() => view.Find(".pause-keys .mu-primary").ClickAsync(new MouseEventArgs()));
        view.WaitForElement(".piece-finish");
        await view.InvokeAsync(() => view.Find(".piece-finish").ClickAsync(new MouseEventArgs()));

        Assert.Equal("A first look: bar 1 read. With the click at 72, the whole piece got to 0% right in time.", view.WaitForElement(".look-headline").TextContent);
        Assert.Equal([true, false], host.Browser.Fullscreen);
        Assert.Equal([false, true], host.Bars.Shown);
        Assert.Empty(view.FindAll(".piece-finish"));
        Assert.NotNull(Assert.Single(await host.Store.GetSessionsAsync(piece.Id)).Finished);
    }

    // Pressed while a run plays, as the strip's is: the run stops and is kept with the session, then the session finishes and
    // its look back takes the page, out of full screen.
    [Fact]
    public async Task FinishPracticeInFullScreenWhileARunPlaysKeepsTheRunWithTheSessionThenLooksBack()
    {
        var piano = new TestPiano(plugged: true);
        await using var host = new ComponentHost(keyboard: piano);
        var piece = await host.AddPiece(bars: 4);
        await host.Store.SetSettingAsync(PlayAlong.ModeKeyOf("library"), "wait");
        var view = host.Context.Render<Piece>(p => p.Add(c => c.Id, piece.Id));
        view.WaitForElement(".piece-front-bar .mu-primary");
        await view.InvokeAsync(() => view.Find(".piece-front-bar .mu-primary").ClickAsync(new MouseEventArgs()));
        view.WaitForAssertion(() => Assert.Equal([true], host.Browser.Fullscreen));

        // Wait for me, its first note played: the run waits at bar 2's.
        view.WaitForElement(".play-bar [data-piano=go]");
        await view.InvokeAsync(() => view.Find(".play-bar [data-piano=go]").ClickAsync(new MouseEventArgs()));
        view.WaitForAssertion(() => Assert.Contains(view.FindAll(".play-bar button"), b => b.TextContent == "Stop"));
        piano.Play(60, 1_000_000_000);
        view.WaitForAssertion(() => Assert.Equal("2", view.FindAll(".play-bar .mu-readout").Single(r => r.QuerySelector(".mu-readout-label")?.TextContent == "Bar").QuerySelector(".mu-readout-value")!.TextContent));

        await view.InvokeAsync(() => view.Find(".piece-finish").ClickAsync(new MouseEventArgs()));

        view.WaitForElement(".look-headline");
        Assert.Equal([true, false], host.Browser.Fullscreen);
        var kept = Assert.Single(await host.Store.GetSessionsAsync(piece.Id));
        Assert.NotNull(kept.Finished);
        var (_, run) = Assert.Single(await host.Store.GetPieceRunsAsync(piece.Id));
        Assert.Equal(kept.Id, run.Session);
        Assert.True(run.Stopped);
        Assert.Equal(1, run.Notes);
    }
}
