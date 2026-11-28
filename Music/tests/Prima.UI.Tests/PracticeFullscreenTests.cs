using System.Text.Json;
using Bunit;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
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
        view.WaitForElement(".piece-front-bar .mu-primary:not([disabled])").Click();

        view.WaitForAssertion(() => Assert.Equal([true], host.Browser.Fullscreen));
        Assert.Equal([false], host.Bars.Shown);
        view.Find(".play-bar [data-action=fullscreen]").Click();
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
            view.WaitForElement(".piece-front-bar .mu-primary:not([disabled])");
            view.WaitForAssertion(() => Assert.Equal(1, host.Browser.ListenKeysCalls));
            view.Find(".piece-front-bar .mu-primary:not([disabled])").Click();
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
            // Practice is off until there's music on the paper: its sheet says there is, while the page's own scripts (the one
            // that takes it full screen among them) are still loading.
            view.WaitForElement(".piece-front-bar .mu-primary");
            await view.InvokeAsync(() => view.FindComponent<Sheet>().Instance.Progress(1, 1, "done"));
            view.WaitForElement(".piece-front-bar .mu-primary:not([disabled])").Click();
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
        view.WaitForElement(".piece-front-bar .mu-primary:not([disabled])");
        Assert.Empty(view.FindAll(".piece-finish"));

        await view.InvokeAsync(() => view.Find(".piece-front-bar .mu-primary:not([disabled])").ClickAsync(new MouseEventArgs()));
        view.WaitForAssertion(() => Assert.Equal([true], host.Browser.Fullscreen));

        // First in the bar, before More and the run's keys.
        var finish = view.WaitForElement(".play-bar > .piece-finish");
        Assert.Contains("piece-finish", view.Find(".play-bar").QuerySelector("button")!.ClassList);
        Assert.Equal("Finish practice", finish.TextContent);
        Assert.Equal("off", finish.GetAttribute("data-piano"));
        Assert.DoesNotContain("mu-primary", finish.ClassList);

        await view.InvokeAsync(() => view.Find("[aria-label='Leave full screen']").ClickAsync(new MouseEventArgs()));
        view.WaitForAssertion(() => Assert.Equal([true, false], host.Browser.Fullscreen));
        Assert.Empty(view.FindAll(".piece-finish"));
        Assert.Equal("Finish practice", view.Find(".play-foot .practice-strip button").TextContent);

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

        view.WaitForElement(".look-headline");
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
        view.WaitForElement(".piece-front-bar .mu-primary:not([disabled])");
        await view.InvokeAsync(() => view.Find(".piece-front-bar .mu-primary:not([disabled])").ClickAsync(new MouseEventArgs()));
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

    // Pressed while Listen plays (the piano playing the piece, nothing kept): Listen stops and the session finishes then, with its
    // look back. It used to stop Listen only, and leave the finish waiting to end the session after the next run.
    [Fact]
    public async Task FinishPracticeWhileListenPlaysFinishesTheSessionThenNotAfterTheNextRun()
    {
        await using var host = new ComponentHost(keyboard: new TestPiano(plugged: true, takesNotes: true));
        host.Context.Services.GetRequiredService<PrimaSettings>().SendToPiano = true;
        var piece = await host.AddPiece(bars: 4);
        var view = host.Context.Render<Piece>(p => p.Add(c => c.Id, piece.Id));
        view.WaitForElement(".piece-front-bar .mu-primary:not([disabled])");
        await view.InvokeAsync(() => view.Find(".piece-front-bar .mu-primary:not([disabled])").ClickAsync(new MouseEventArgs()));
        view.WaitForAssertion(() => Assert.Equal([true], host.Browser.Fullscreen));

        view.WaitForElement(".play-listen button");
        await view.InvokeAsync(() => view.Find(".play-listen button").ClickAsync(new MouseEventArgs()));
        view.WaitForAssertion(() => Assert.Contains(view.FindAll(".play-bar .mu-readout-label"), l => l.TextContent == "Listening"));
        await view.InvokeAsync(() => view.Find(".piece-finish").ClickAsync(new MouseEventArgs()));

        view.WaitForElement(".look-headline");
        Assert.Equal([true, false], host.Browser.Fullscreen);
        Assert.NotNull(Assert.Single(await host.Store.GetSessionsAsync(piece.Id)).Finished);
        Assert.Empty(await host.Store.GetPieceRunsAsync(piece.Id));
    }

    // A PDF read into notes, practiced with Print showing its print: the session keeps its clocks and Finish practice (the head's,
    // and in full screen the print's bar's), and finishing brings the notation back for the look back, which the print would hide.
    [Fact]
    public async Task FinishPracticeWhilePrintShowsAPdfsPrintOpensTheLookBack()
    {
        await using var host = new ComponentHost();
        var piece = await host.AddPiece(pdf: true, read: true);
        var view = host.Context.Render<Piece>(p => p.Add(c => c.Id, piece.Id));
        view.WaitForElement(".piece-front-bar .mu-primary:not([disabled])");
        await view.InvokeAsync(() => view.Find(".piece-front-bar .mu-primary:not([disabled])").ClickAsync(new MouseEventArgs()));
        view.WaitForAssertion(() => Assert.Equal([true], host.Browser.Fullscreen));

        // The print, from More's group for the music.
        view.WaitForElement("[data-action=more]");
        await view.InvokeAsync(() => view.Find("[data-action=more]").ClickAsync(new MouseEventArgs()));
        await view.InvokeAsync(() => view.WaitForElement(".play-more [data-show=print]").ClickAsync(new MouseEventArgs()));
        view.WaitForElement(".piece-print");
        // The print's bar is the notation's, without what follows the playing: no mode, and no Play.
        Assert.Single(view.FindAll(".play-bar"));
        Assert.Empty(view.FindAll(".play-bar .play-mode"));
        Assert.Empty(view.FindAll(".play-bar [data-piano='go']"));
        Assert.Single(view.FindAll(".play-foot .practice-strip"));
        var finish = view.Find(".play-bar > .piece-finish");
        Assert.Equal("off", finish.GetAttribute("data-piano"));

        await view.InvokeAsync(() => view.Find(".piece-bar .piece-finish").ClickAsync(new MouseEventArgs()));

        // The look back only takes the page with the notation back: the print's bar comes first.
        view.WaitForElement(".look-headline");
        Assert.Equal([true, false], host.Browser.Fullscreen);
        Assert.NotNull(Assert.Single(await host.Store.GetSessionsAsync(piece.Id)).Finished);
    }

    // Hands-on gives each run its review, taking the screen, full screen as Practice is: Finish practice is first in its keys, and
    // does what Back to practicing then Finish practice would. The run is kept with the session.
    [Fact]
    public async Task FinishPracticeFromARunsReviewOpensTheLookBack()
    {
        var piano = new TestPiano(plugged: true);
        await using var host = new ComponentHost(keyboard: piano);
        host.Context.Services.GetRequiredService<PrimaSettings>().UseWay("hands-on");
        await host.Store.SetSettingAsync(PlayAlong.ModeKeyOf("library"), "wait");
        var piece = await host.AddPiece();
        var view = host.Context.Render<Piece>(p => p.Add(c => c.Id, piece.Id));
        view.WaitForElement(".piece-front-bar .mu-primary:not([disabled])");
        await view.InvokeAsync(() => view.Find(".piece-front-bar .mu-primary:not([disabled])").ClickAsync(new MouseEventArgs()));
        view.WaitForAssertion(() => Assert.Equal([true], host.Browser.Fullscreen));

        // Wait for me, its one note played: the run ends, and its review takes the screen.
        view.WaitForElement(".play-bar [data-piano=go]");
        await view.InvokeAsync(() => view.Find(".play-bar [data-piano=go]").ClickAsync(new MouseEventArgs()));
        view.WaitForAssertion(() => Assert.Contains(view.FindAll(".play-bar button"), b => b.TextContent == "Stop"));
        piano.Play(60, 1_000_000_000);
        var keys = view.WaitForElement(".review-keys");
        Assert.Equal(["Finish practice", "Again"], keys.QuerySelectorAll("button").Take(2).Select(b => b.TextContent));
        Assert.Equal("off", view.Find(".review-keys .review-finish").GetAttribute("data-piano"));

        await view.InvokeAsync(() => view.Find(".review-keys .review-finish").ClickAsync(new MouseEventArgs()));

        view.WaitForElement(".look-headline");
        Assert.Equal([true, false], host.Browser.Fullscreen);
        var kept = Assert.Single(await host.Store.GetSessionsAsync(piece.Id));
        Assert.NotNull(kept.Finished);
        Assert.Equal(kept.Id, Assert.Single(await host.Store.GetPieceRunsAsync(piece.Id)).Run.Session);
    }
}
