using System.Text.Json;
using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Prima.Core.Practice;
using Prima.UI.Components;
using Prima.UI.Pages;
using Prima.UI.Storage;

namespace Prima.UI.Tests;

/// <summary>Finished sessions, partial runs and the read-only history path.</summary>
public class LookBackTests
{
    // A run kept on the test piece (one bar, one note) in a practice session: In time at 72, its note wrong.
    private static Task<long> Run(ComponentHost host, PieceRecord piece, long? session, DateTimeOffset at) => host.Store.AddAttemptAsync(new AttemptRecord(0, at, DateOnly.FromDateTime(at.LocalDateTime), "piece", "in time", piece.Id, 1, 72, null, null, "[]", null,
        JsonSerializer.Serialize(new { inTime = 0.0, right = 0, wrong = 1, missed = 0, from = 1, stopped = false, bar = 1, hands = "both", session, took = 12.5, timing = new[] { -8.0, 3.5 }, trouble = new[] { new[] { 1, 1, 0, 0, 0, 0, 0, 0 } } }), 0, false, null));

    [Fact]
    public async Task FinishingPracticeOpensASummaryAndContinuingReopensTheSameSession()
    {
        await using var host = new ComponentHost();
        var piece = await host.AddPiece();
        var session = await host.Store.StartSessionAsync(piece.Id, "standard", DateTimeOffset.Now.AddMinutes(-5));
        var run = await Run(host, piece, session, DateTimeOffset.Now.AddMinutes(-2));
        var view = host.Context.Render<Piece>(p => p.Add(c => c.Id, piece.Id));
        view.WaitForAssertion(() => Assert.Equal([true], host.Browser.Fullscreen));

        await view.InvokeAsync(() => view.FindComponent<PracticeStrip>().Instance.OnFinish.InvokeAsync());
        view.WaitForAssertion(() => Assert.Equal(run, Assert.Single(view.FindComponent<LookBack>().Instance.Runs).Id));
        Assert.NotNull(Assert.Single(await host.Store.GetSessionsAsync(piece.Id)).Finished);
        Assert.Empty(view.FindAll(".look-steps"));
        await view.Find("[data-action=continue-practice]").ClickAsync(new MouseEventArgs());
        view.WaitForAssertion(() => Assert.Equal([true, false, true], host.Browser.Fullscreen));
        Assert.Null(Assert.Single(await host.Store.GetSessionsAsync(piece.Id)).Finished);
    }

    [Fact]
    public async Task RunsStoppedOnTheWayAreNamedByTheBarsTheyReachedInWhatWasPlayedAndTheInDepthReview()
    {
        await using var host = new ComponentHost();
        var piece = await host.AddPiece(bars: 4);
        var session = await host.Store.StartSessionAsync(piece.Id, "standard", DateTimeOffset.Now.AddMinutes(-9));
        // Twice from the start of the four bars, stopped in bar 3: bars 1-2, the second of them with its note wrong.
        foreach (var minutes in new[] { -6, -3 })
        {
            var at = DateTimeOffset.Now.AddMinutes(minutes);
            await host.Store.AddAttemptAsync(new AttemptRecord(0, at, DateOnly.FromDateTime(at.LocalDateTime), "piece", "in time", piece.Id, 1, 72, null, null, "[]", null,
                JsonSerializer.Serialize(new { inTime = 0.5, right = 1, wrong = 1, missed = 0, from = 1, stopped = true, bar = 3, hands = "both", session, took = 9.5, timing = new[] { -8.0, 3.5 }, trouble = new[] { new[] { 2, 1, 0, 0, 0, 0, 0, 0 } } }), 0, false, null));
        }
        var view = host.Context.Render<Piece>(p => p.Add(c => c.Id, piece.Id));
        view.WaitForAssertion(() => Assert.Equal([true], host.Browser.Fullscreen));

        await view.InvokeAsync(() => view.FindComponent<PracticeStrip>().Instance.OnFinish.InvokeAsync());

        var summary = view.FindComponent<LookBack>().Instance.Review;
        Assert.Equal(2, Assert.Single(summary.Stretches).Reached);
        await view.Find("[data-action=review-details]").ClickAsync(new MouseEventArgs());
        view.WaitForAssertion(() => Assert.Same(summary, view.FindComponent<DeepReview>().Instance.Review));
        Assert.Equal(2, view.FindComponent<DeepReview>().Instance.Picked);
    }

    [Fact]
    public async Task HistoryReopensFinishedSessionsAndShortStandaloneRunsWithoutChangingActivePractice()
    {
        await using var host = new ComponentHost();
        var piece = await host.AddPiece();
        var at = DateTimeOffset.Now.AddDays(-1);
        var id = await host.Store.StartSessionAsync(piece.Id, "standard", at);
        var first = await Run(host, piece, id, at.AddMinutes(1));
        var second = await Run(host, piece, id, at.AddMinutes(2));
        var closed = new PracticeSession(id, piece.Id, at, TimeSpan.FromMinutes(2), TimeSpan.Zero, true, at.AddMinutes(2), "standard", at.AddMinutes(2));
        await host.Store.SaveSessionAsync(closed);
        var solo = await Run(host, piece, null, at.AddHours(1));
        var active = await host.Store.StartSessionAsync(piece.Id, "simple", DateTimeOffset.Now);
        await Run(host, piece, active, DateTimeOffset.Now);
        var before = await host.Store.GetSessionsAsync(piece.Id);
        var nav = host.Context.Services.GetRequiredService<NavigationManager>();
        nav.NavigateTo($"library/{piece.Id}/history");
        var view = host.Context.Render<Pages.PracticeHistory>(p => p.Add(c => c.Id, piece.Id));
        view.WaitForElement(".history-entries .piece-row");
        var links = view.FindAll(".history-entries .piece-row");
        Assert.Equal([ $"library/{piece.Id}/history?run={solo}", $"library/{piece.Id}/history?session={id}" ], links.Select(e => e.GetAttribute("href")));

        nav.NavigateTo(links[1].GetAttribute("href")!);
        view.WaitForAssertion(() => Assert.Equal(new[] { first, second }, view.FindComponent<LookBack>().Instance.Runs.Select(r => r.Id)));
        Assert.Empty(view.FindAll("[data-action=continue-practice]"));
        await view.Find("[data-action=review-details]").ClickAsync(new MouseEventArgs());
        view.WaitForAssertion(() => Assert.Equal(2, view.FindComponent<DeepReview>().Instance.Review.Notes));
        Assert.Equal("1", view.FindComponent<Sheet>().Instance.Measures);

        nav.NavigateTo($"library/{piece.Id}/history?run={solo}");
        view.WaitForAssertion(() => Assert.Equal(solo, Assert.Single(view.FindComponent<LookBack>().Instance.Runs).Id));
        nav.NavigateTo($"library/{piece.Id}/history?session={active}");
        view.WaitForAssertion(() => Assert.Empty(view.FindComponents<LookBack>()));
        Assert.Equal(before, await host.Store.GetSessionsAsync(piece.Id));
        Assert.Equal(4, (await host.Store.GetPieceRunsAsync(piece.Id)).Count);
        Assert.Empty(host.Browser.Fullscreen);
    }
    [Fact]
    public async Task GlobalHistoryIncludesEmptySessionsAndRemovedPiecesAndHonorsThePieceFilter()
    {
        await using var host = new ComponentHost();
        var piece = await host.AddPiece();
        var at = DateTimeOffset.Now.AddDays(-2);
        var id = await host.Store.StartSessionAsync(piece.Id, "simple", at);
        await host.Store.SaveSessionAsync(new(id, piece.Id, at, TimeSpan.Zero, TimeSpan.FromSeconds(4), true, at, "simple", at.AddSeconds(4)));
        var older = await Run(host, piece with { Id = "removed" }, null, at.AddDays(-1));
        var nav = host.Context.Services.GetRequiredService<NavigationManager>();
        nav.NavigateTo("history");
        var history = host.Context.Render<Pages.PracticeHistory>();
        Assert.Equal($"history?session={id}", history.WaitForElement("[data-session]").GetAttribute("href"));
        Assert.Equal($"history?run={older}", history.Find("[data-run]").GetAttribute("href"));
        nav.NavigateTo($"history?session={id}");
        history.WaitForAssertion(() => Assert.Empty(history.FindComponent<LookBack>().Instance.Runs));
        Assert.Empty(history.FindAll("[data-action=review-details]"));

        nav.NavigateTo($"history?run={older}");
        await history.WaitForElement("[data-action=review-details]").ClickAsync(new MouseEventArgs());
        Assert.Equal(1, history.FindComponent<DeepReview>().Instance.Review.Notes);
        Assert.Empty(history.FindComponents<Sheet>());
        nav.NavigateTo($"library/{piece.Id}/history?run={older}");
        var scoped = host.Context.Render<Pages.PracticeHistory>(p => p.Add(c => c.Id, piece.Id));
        scoped.WaitForElement(".page-note");
        Assert.Empty(scoped.FindComponents<LookBack>());
    }

    [Fact]
    public async Task ArchivedBarUsesTheLatestAvailableMarksFromThatSession()
    {
        await using var host = new ComponentHost();
        var piece = await host.AddPiece(bars: 2);
        var at = DateTimeOffset.Now.AddDays(-1);
        var id = await host.Store.StartSessionAsync(piece.Id, "standard", at);
        var expected = Prima.Core.Music.MeiReader.Read((await host.Store.GetMeiAsync(piece.Id))!).Expected();
        var firstBar = expected.First(n => n.Bar == 1).Id;
        var secondBar = expected.First(n => n.Bar == 2).Id;
        foreach (var (noteId, bar, minute) in new[] { (firstBar, 1, 1), (secondBar, 2, 2) })
        {
            var marks = new { right = Array.Empty<string>(), other = new object[][] { [noteId, "wrong", "D4", 1] } };
            var data = JsonSerializer.Serialize(new { session = id, from = bar, bar, stopped = true, right = 0, wrong = 1, missed = 0, marks });
            await host.Store.AddAttemptAsync(new(0, at.AddMinutes(minute), DateOnly.FromDateTime(at.LocalDateTime), "piece", "in time", piece.Id, 1, 60, null, null, "[]", null, data, 0, false, null));
        }
        await host.Store.SaveSessionAsync(new(id, piece.Id, at, TimeSpan.FromMinutes(2), TimeSpan.Zero, true, at.AddMinutes(2), "standard", at.AddMinutes(2)));
        host.Context.Services.GetRequiredService<NavigationManager>().NavigateTo($"library/{piece.Id}/history?session={id}");
        var history = host.Context.Render<Pages.PracticeHistory>(p => p.Add(c => c.Id, piece.Id));
        history.WaitForElement("[data-action=review-details]");
        Assert.Empty(history.FindAll(".look-line"));
        await history.Find("[data-action=review-details]").ClickAsync(new MouseEventArgs());
        var panel = history.FindComponent<DeepReview>();
        foreach (var bar in new[] { 1, 2 })
        {
            await history.InvokeAsync(() => panel.Instance.OnPick.InvokeAsync(bar));
            history.WaitForAssertion(() => Assert.Equal(bar.ToString(), history.FindComponent<Sheet>().Instance.Measures));
            Assert.Empty(history.FindAll(".sheet-frame + .section-note"));
        }
        var runs = panel.Instance.Runs;
        var stored = await host.Store.GetRunMarksAsync([.. runs.Select(r => r.Id)]);
        var mark = Assert.Single(Prima.UI.Practice.KeptRun.MarksForBar(1, expected, runs, stored));
        Assert.Equal((firstBar, "wrong", "D4"), (mark.Id, mark.Kind, mark.Played));
        Assert.Null(mark.Written);
    }

}
