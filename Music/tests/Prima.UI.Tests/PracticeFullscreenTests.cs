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
