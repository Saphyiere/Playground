using Bunit;
using Prima.UI.Components;
using Prima.UI.Pages;

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
}
