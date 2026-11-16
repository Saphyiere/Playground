using System.Globalization;
using System.Text.Json;
using Bunit;
using Microsoft.AspNetCore.Components.Web;
using Prima.UI.Components;
using Prima.UI.Pages;
using Prima.UI.Storage;

namespace Prima.UI.Tests;

/// <summary>
/// From the review of main at a7ab94d: the look back after Finish practice, and what it leaves on the piece's front. Each fails on
/// that code; the place in it is given with each.
/// </summary>
public class LookBackEdgeUiTests
{
    // A run kept on the test piece (one bar, one note) in a practice session: In time at 72, its note right or wrong.
    private static Task<long> Timed(ComponentHost host, PieceRecord piece, long session, DateTimeOffset at, bool right) => host.Store.AddAttemptAsync(new AttemptRecord(0, at, DateOnly.FromDateTime(at.LocalDateTime), "piece", "in time", piece.Id, 1, 72, null, null, "[]", null,
        JsonSerializer.Serialize(new { inTime = right ? 1.0 : 0.0, right = right ? 1 : 0, wrong = right ? 0 : 1, missed = 0, from = 1, stopped = false, bar = 1, hands = "both", session, took = 12.5, timing = new[] { -8.0, 3.5 }, trouble = right ? [] : new[] { new[] { 1, 1, 0, 0, 0, 0, 0, 0 } } }), right ? 1 : 0, right, null));

    // The piece through once in Wait for me, hands together, its note right first time.
    private static Task<long> Waited(ComponentHost host, PieceRecord piece, long session, DateTimeOffset at) => host.Store.AddAttemptAsync(new AttemptRecord(0, at, DateOnly.FromDateTime(at.LocalDateTime), "piece", "wait", piece.Id, 1, null, null, null, "[]", null,
        JsonSerializer.Serialize(new { rightFirstTime = 1, notes = 1, from = 1, stopped = false, bar = 1, hands = "both", session, took = 20.0 }), null, true, null));

    // LookBack.razor:104 writes a stretch's line with $"{x:0.0} {ys[i]:0.0}", and :98 and :107 give its marks' places as numbers
    // for Razor to write: all in the tablet's own number format. Where that writes a decimal comma, "M6,0 41,3 L354,0 18,0" is
    // four points to SVG, not two, and the line is drawn wrong. (Not on a tablet set to English; the labels b2f2204 added are
    // written the invariant way already.)
    [Fact]
    public async Task AStretchsLineIsDrawnTheSameWhateverTheTabletsDecimalMark()
    {
        var (culture, ui) = (CultureInfo.CurrentCulture, CultureInfo.CurrentUICulture);
        CultureInfo.CurrentCulture = CultureInfo.CurrentUICulture = new CultureInfo("de-DE");
        try
        {
            await using var host = new ComponentHost();
            var piece = await host.AddPiece();
            var session = await host.Store.StartSessionAsync(piece.Id, "standard", DateTimeOffset.Now.AddMinutes(-9));
            await Timed(host, piece, session, DateTimeOffset.Now.AddMinutes(-6), right: false);
            await Timed(host, piece, session, DateTimeOffset.Now.AddMinutes(-4), right: true);
            await Timed(host, piece, session, DateTimeOffset.Now.AddMinutes(-2), right: true);
            var view = host.Context.Render<Piece>(p => p.Add(c => c.Id, piece.Id));
            view.WaitForAssertion(() => Assert.Equal([true], host.Browser.Fullscreen));

            await view.InvokeAsync(() => view.FindComponent<PracticeStrip>().Instance.OnFinish.InvokeAsync());

            Assert.Matches(@"^M\d+\.\d \d+\.\d( L\d+\.\d \d+\.\d)+$", view.WaitForElement(".look-line path").GetAttribute("d"));
            Assert.All(view.FindAll(".look-line circle"), c => Assert.DoesNotContain(",", c.GetAttribute("cx") + c.GetAttribute("cy")));
        }
        finally
        {
            (CultureInfo.CurrentCulture, CultureInfo.CurrentUICulture) = (culture, ui);
        }
    }

    // Piece.razor:1294 takes a session's steps for the front's Next only when it has some: after a session that leaves nothing to
    // do, the front goes on showing the session before's Next, with Start there. Opened again, the piece shows none
    // (SessionStory.Next: "None for a piece … whose last one left nothing to do").
    [Fact]
    public async Task ASessionThatLeavesNothingToDoClearsWhatsNextOnThePiecesFront()
    {
        await using var host = new ComponentHost();
        var piece = await host.AddPiece();
        var yesterday = DateTimeOffset.Now.AddDays(-1);
        var before = await host.Store.StartSessionAsync(piece.Id, "standard", yesterday);
        await Timed(host, piece, before, yesterday.AddMinutes(3), right: false);
        await host.Store.SaveSessionAsync(new PracticeSession(before, piece.Id, yesterday, TimeSpan.FromMinutes(4), TimeSpan.FromMinutes(1), true, yesterday.AddMinutes(3), "standard", yesterday.AddMinutes(5)));
        var session = await host.Store.StartSessionAsync(piece.Id, "standard", DateTimeOffset.Now.AddMinutes(-5));
        var view = host.Context.Render<Piece>(p => p.Add(c => c.Id, piece.Id));
        view.WaitForAssertion(() => Assert.Equal([true], host.Browser.Fullscreen));

        // Today's session, played with the piece open, leaves nothing to do next.
        await Waited(host, piece, session, DateTimeOffset.Now.AddSeconds(-30));
        await view.InvokeAsync(() => view.FindComponent<PracticeStrip>().Instance.OnFinish.InvokeAsync());
        view.WaitForElement(".look-headline");
        Assert.Empty(view.FindAll(".look-steps li"));

        await view.InvokeAsync(() => view.Find(".look-keys .mu-primary").ClickAsync(new MouseEventArgs()));

        view.WaitForAssertion(() => Assert.Empty(view.FindAll(".look-headline")));
        Assert.Empty(view.FindAll(".about-next"));
    }
}
