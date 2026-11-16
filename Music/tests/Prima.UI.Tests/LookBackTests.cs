using System.Text.Json;
using Bunit;
using Prima.UI.Components;
using Prima.UI.Pages;
using Prima.UI.Storage;

namespace Prima.UI.Tests;

/// <summary>The look back after Finish practice, and what it leaves on the piece's front: what the session came to, and what's next.</summary>
public class LookBackTests
{
    // A run kept on the test piece (one bar, one note) in a practice session: In time at 72, its note wrong.
    private static Task<long> Run(ComponentHost host, PieceRecord piece, long session, DateTimeOffset at) => host.Store.AddAttemptAsync(new AttemptRecord(0, at, DateOnly.FromDateTime(at.LocalDateTime), "piece", "in time", piece.Id, 1, 72, null, null, "[]", null,
        JsonSerializer.Serialize(new { inTime = 0.0, right = 0, wrong = 1, missed = 0, from = 1, stopped = false, bar = 1, hands = "both", session, took = 12.5, timing = new[] { -8.0, 3.5 }, trouble = new[] { new[] { 1, 1, 0, 0, 0, 0, 0, 0 } } }), 0, false, null));

    [Fact]
    public async Task FinishingPracticeSaysWhatItCameToAndAStepCarriesItOnSetUpForIt()
    {
        await using var host = new ComponentHost();
        var piece = await host.AddPiece();
        var session = await host.Store.StartSessionAsync(piece.Id, "standard", DateTimeOffset.Now.AddMinutes(-5));
        await Run(host, piece, session, DateTimeOffset.Now.AddMinutes(-2));
        var view = host.Context.Render<Piece>(p => p.Add(c => c.Id, piece.Id));
        view.WaitForAssertion(() => Assert.Equal([true], host.Browser.Fullscreen));

        await view.InvokeAsync(() => view.FindComponent<PracticeStrip>().Instance.OnFinish.InvokeAsync());

        // In words; the piece's one bar on the map, played today, its note not there yet; and what's next.
        Assert.Equal("A first look: bar 1 read. With the click at 72, the whole piece got to 0% right in time.", view.WaitForElement(".look-headline").TextContent);
        Assert.Equal(["Still hard"], view.FindAll(".look-story .sum-point .mu-readout-label").Select(l => l.TextContent));
        Assert.Single(view.FindAll(".look-map .look-bar.today .state-1"));
        var step = Assert.Single(view.FindAll(".look-steps li"));
        Assert.Equal("Bar 1, Wait for me", step.QuerySelector(".look-step-what")!.TextContent);

        step.QuerySelector("button")!.Click();

        // Practice again, looping the step's bar in the step's mode (which the play bar reads as it's made), full screen as
        // Practice is.
        view.WaitForAssertion(() => Assert.Equal("Loop bar 1", view.Find(".play-bar [data-piano=go]").TextContent.Trim()));
        view.WaitForAssertion(() => Assert.Equal("Wait for me", view.Find(".play-mode-option[aria-checked=true]").TextContent));
        Assert.Equal("wait", (await host.Store.GetSettingsAsync())[PlayAlong.ModeKeyOf("library")]);
        view.WaitForAssertion(() => Assert.Equal([true, false, true], host.Browser.Fullscreen));
    }

    [Fact]
    public async Task APiecesFrontKeepsWhatsNextFromItsLastSessionAndStartsPracticeThere()
    {
        await using var host = new ComponentHost();
        var piece = await host.AddPiece();
        var yesterday = DateTimeOffset.Now.AddDays(-1);
        var session = await host.Store.StartSessionAsync(piece.Id, "standard", yesterday);
        await Run(host, piece, session, yesterday.AddMinutes(3));
        await host.Store.SaveSessionAsync(new PracticeSession(session, piece.Id, yesterday, TimeSpan.FromMinutes(4), TimeSpan.FromMinutes(1), true, yesterday.AddMinutes(3), "standard", yesterday.AddMinutes(5)));
        var view = host.Context.Render<Piece>(p => p.Add(c => c.Id, piece.Id));

        var next = view.WaitForElement(".about-next");
        Assert.StartsWith("Next: Bar 1, Wait for me. Bar 1 was still going wrong at the end.", next.TextContent);

        next.QuerySelector("button")!.Click();

        view.WaitForAssertion(() => Assert.Equal("Loop bar 1", view.Find(".play-bar [data-piano=go]").TextContent.Trim()));
        view.WaitForAssertion(() => Assert.Equal("Wait for me", view.Find(".play-mode-option[aria-checked=true]").TextContent));
        view.WaitForAssertion(() => Assert.Equal([true], host.Browser.Fullscreen));
    }
}
