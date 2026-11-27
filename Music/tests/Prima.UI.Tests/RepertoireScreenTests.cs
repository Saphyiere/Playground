using System.Text.Json;
using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Prima.Core.Music;
using Prima.Core.Timing;
using Prima.UI.Library;
using Prima.UI.Storage;

namespace Prima.UI.Tests;

// The owner, 17 November, on the Library's mock-ups: "We have a repertoire tab ... we don't need to put the repertoire in the
// library", "when you star it, that adds to your repertoire", with "binders or collections of repertoires ... Learning Pieces 5
// ... My Favorite Violet Collection"; and "repertoire would be way better to have that and then show like recent pieces
// underneath it. And then you can sort by like learned pieces".
public class RepertoireScreenTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.Now;

    // A piece in notation: four bars of crotchets.
    private static async Task Add(ComponentHost host, string id, string composer, string title, string? work = null, string origin = "collection", string? source = null)
    {
        var piece = new PieceRecord(id, origin, title, composer, work, "mei", null, null, 7, 7, null, 4, source, null, null, null, null, null, null, null, null, Now, null);
        var bars = Enumerable.Range(1, 4).Select(b => new Bar(b, [new Voice(1, [.. Enumerable.Range(0, 4).Select(q =>
            new Notes($"n{b}-{q}", q * Ticks.Quarter, Ticks.Quarter, [new Tone($"t{b}-{q}", new Spelled(Step.C, 0, 4))]))])])).ToArray();
        await host.Store.SavePieceAsync(piece, MeiWriter.Write(new Score(new Prima.Core.Music.Key(0), new Meter(4, 4), bars)), []);
    }

    // A run kept on a piece some days ago: In time at 72, through, most of it right.
    private static Task Played(ComponentHost host, string piece, int daysAgo)
    {
        var at = Now.AddDays(-daysAgo);
        return host.Store.AddAttemptAsync(new AttemptRecord(0, at, DateOnly.FromDateTime(at.LocalDateTime), "piece", "in time", piece, 7, 72, null, null, "[]", null,
            JsonSerializer.Serialize(new { inTime = 0.9, right = 14, wrong = 2, missed = 0, from = 1, hands = "both" }), 0.9, false, null));
    }

    private static async Task AddThree(ComponentHost host)
    {
        await Add(host, "dcml-beethoven-piano-sonatas-14-1", "Ludwig van Beethoven", "Sonata No. 'quasi una fantasia' ('Moonlight'), Op. 27/2: I. Adagio sostenuto", "Piano sonatas", source: "DCML");
        await Add(host, "dcml-beethoven-piano-sonatas-14-2", "Ludwig van Beethoven", "Sonata No. 'quasi una fantasia' ('Moonlight'), Op. 27/2: II. Allegretto", "Piano sonatas", source: "DCML");
        await Add(host, "import-hallelujah", "Leonard Cohen", "Hallelujah", origin: "import");
        await Add(host, "sweden", "C418", "Sweden");
    }

    [Fact]
    public async Task WithNothingStarredItSaysHowAPieceGetsHere()
    {
        await using var host = new ComponentHost();
        await AddThree(host);

        var view = host.Context.Render<Pages.Repertoire>();

        Assert.Equal("library", view.WaitForElement(".page-note a").GetAttribute("href"));
        Assert.Empty(view.FindAll(".rep-card"));
    }

    [Fact]
    public async Task KeptPiecesAreSortedByLastPracticeAndLinkToTheirHistory()
    {
        await using var host = new ComponentHost();
        await AddThree(host);
        var book = host.Context.Services.GetRequiredService<RepertoireBook>();
        foreach (var id in new[] { "dcml-beethoven-piano-sonatas-14-1", "import-hallelujah", "sweden" }) await book.StarAsync(id);
        await Played(host, "sweden", 0);
        await Played(host, "import-hallelujah", 2);
        var nav = host.Context.Services.GetRequiredService<NavigationManager>();
        nav.NavigateTo("repertoire");

        var view = host.Context.Render<Pages.Repertoire>();

        view.WaitForAssertion(() => Assert.Equal(["Sweden", "Hallelujah", "Sonata No. 14 “quasi una fantasia” “Moonlight”"], view.FindAll(".rep-title").Select(t => t.TextContent)));
        var cards = view.FindAll(".rep-card");
        Assert.All(cards, card => Assert.Equal(24, card.QuerySelectorAll(".rep-meter > span").Length));
        Assert.Equal(24, cards[0].QuerySelectorAll(".rep-meter > span.s2").Length);
        Assert.Equal("library/sweden/history", cards[0].QuerySelector(".rep-foot .band-link")!.GetAttribute("href"));
        Assert.Equal("library/sweden", cards[0].QuerySelector(".rep-foot .mu-btn")!.GetAttribute("href"));

        // By title, in the address's place.
        await view.InvokeAsync(() => view.FindAll(".rep-tools .mu-toggle").Single(b => b.TextContent == "A–Z").ClickAsync(new MouseEventArgs()));

        view.WaitForAssertion(() => Assert.Equal(["Hallelujah", "Sonata No. 14 “quasi una fantasia” “Moonlight”", "Sweden"], view.FindAll(".rep-title").Select(t => t.TextContent)));
        Assert.EndsWith("repertoire?sort=az", nav.Uri, StringComparison.Ordinal);
        Assert.True(((Bunit.TestDoubles.BunitNavigationManager)nav).History.First().Options.ReplaceHistoryEntry);
    }

    [Fact]
    public async Task ACollectionIsMadeShownRenamedAndRemovedAndItsPiecesStayKept()
    {
        await using var host = new ComponentHost();
        await AddThree(host);
        var book = host.Context.Services.GetRequiredService<RepertoireBook>();
        await book.StarAsync("import-hallelujah");
        await book.StarAsync("sweden");
        var nav = host.Context.Services.GetRequiredService<NavigationManager>();
        nav.NavigateTo("repertoire");
        var view = host.Context.Render<Pages.Repertoire>();
        view.WaitForAssertion(() => Assert.Equal(2, view.FindAll(".rep-card").Count));

        await view.InvokeAsync(() => view.Find(".page-head .mu-btn").ClickAsync(new MouseEventArgs()));
        await view.InvokeAsync(() => view.Find(".rep-name input").InputAsync(new ChangeEventArgs { Value = "Learning pieces 5" }));
        await view.InvokeAsync(() => view.Find(".rep-name .mu-btn").ClickAsync(new MouseEventArgs()));

        // The new collection is the one shown, empty.
        var made = Assert.Single(book.Current.Collections);
        view.WaitForAssertion(() => Assert.EndsWith($"repertoire?in={made.Id}", nav.Uri, StringComparison.Ordinal));
        view.WaitForAssertion(() => Assert.Equal(["false", "true"], view.FindAll(".settings-tab").Select(t => t.GetAttribute("aria-selected"))));
        Assert.Equal(["Everything·2", "Learning pieces 5·0"], view.FindAll(".settings-tab").Select(t => t.TextContent));
        Assert.Equal("Nothing in “Learning pieces 5” yet. A tap on a starred piece's star puts it in.", view.Find(".page-note").TextContent);

        // A piece put in it (from its star's menu) is its card.
        await view.InvokeAsync(() => book.PutAsync(made.Id, "sweden", true));
        view.WaitForAssertion(() => Assert.Equal(["Sweden"], view.FindAll(".rep-title").Select(t => t.TextContent)));

        await view.InvokeAsync(() => view.FindAll(".rep-own button").Single(b => b.TextContent == "Rename…").ClickAsync(new MouseEventArgs()));
        Assert.Equal("Learning pieces 5", view.Find(".rep-name input").GetAttribute("value"));
        await view.InvokeAsync(() => view.Find(".rep-name input").InputAsync(new ChangeEventArgs { Value = "Recital" }));
        await view.InvokeAsync(() => view.Find(".rep-name .mu-btn").ClickAsync(new MouseEventArgs()));

        view.WaitForAssertion(() => Assert.Equal(["Everything·2", "Recital·1"], view.FindAll(".settings-tab").Select(t => t.TextContent)));

        await view.InvokeAsync(() => view.FindAll(".rep-own button").Single(b => b.TextContent == "Remove this collection…").ClickAsync(new MouseEventArgs()));
        Assert.Equal("“Recital” goes; its pieces stay in your repertoire.", view.Find(".section-note").TextContent);
        await view.InvokeAsync(() => view.FindAll(".rep-own button").Single(b => b.TextContent == "Remove the collection").ClickAsync(new MouseEventArgs()));

        view.WaitForAssertion(() => Assert.Equal(["Everything·2"], view.FindAll(".settings-tab").Select(t => t.TextContent)));
        Assert.Equal("“Recital” is gone; its pieces are still in your repertoire.", view.Find(".control-note").TextContent);
        Assert.Empty(book.Current.Collections);
        Assert.True(book.Current.Has("sweden"));
        Assert.Equal(2, view.FindAll(".rep-card").Count);
    }

    [Fact]
    public async Task WhatWasPlayedLatelyWithoutBeingKeptIsUnderTheCardsAndAStarKeepsIt()
    {
        await using var host = new ComponentHost();
        await AddThree(host);
        var book = host.Context.Services.GetRequiredService<RepertoireBook>();
        await book.StarAsync("sweden");
        await Played(host, "import-hallelujah", 1);
        // Played too long ago to be lately.
        await Played(host, "dcml-beethoven-piano-sonatas-14-2", 45);

        var view = host.Context.Render<Pages.Repertoire>();

        view.WaitForAssertion(() => Assert.Equal(["Hallelujah"], view.FindAll(".rep-lately .up-title").Select(t => t.TextContent)));
        Assert.Single(view.FindAll(".rep-card"));

        await view.InvokeAsync(() => view.Find(".rep-lately .lb-star").ClickAsync(new MouseEventArgs()));

        view.WaitForAssertion(() => Assert.Equal(2, view.FindAll(".rep-card").Count));
        Assert.Empty(view.FindAll(".rep-lately"));
        Assert.True(book.Current.Has("import-hallelujah"));
    }

    // A card's star is lit: a tap opens where the piece is kept, and out of the repertoire its card goes.
    [Fact]
    public async Task ACardsStarOpensWhereThePieceIsKeptAndTakesItOut()
    {
        await using var host = new ComponentHost();
        await AddThree(host);
        var book = host.Context.Services.GetRequiredService<RepertoireBook>();
        await book.StarAsync("sweden");
        var view = host.Context.Render<Pages.Repertoire>();
        view.WaitForAssertion(() => Assert.Single(view.FindAll(".rep-card")));

        await view.InvokeAsync(() => view.Find(".rep-card .lb-star").ClickAsync(new MouseEventArgs()));

        Assert.Equal(["Repertoire", "New collection…"], view.FindAll(".lb-menu > button").Select(b => b.TextContent));

        await view.InvokeAsync(() => view.FindAll(".lb-menu > button").First().ClickAsync(new MouseEventArgs()));

        view.WaitForAssertion(() => Assert.Empty(view.FindAll(".rep-card")));
        Assert.Empty(view.FindAll(".lb-menu"));
        Assert.False(book.Current.Has("sweden"));
    }

    // Today: after the day's plan, the pieces kept that were played last, each with what comes next.
    [Fact]
    public async Task TodayHasThePiecesKeptAfterItsPlan()
    {
        await using var host = new ComponentHost();
        await AddThree(host);
        host.Context.Services.GetRequiredService<PrimaSettings>().Welcomed = true;
        var book = host.Context.Services.GetRequiredService<RepertoireBook>();
        foreach (var id in new[] { "dcml-beethoven-piano-sonatas-14-1", "dcml-beethoven-piano-sonatas-14-2", "import-hallelujah", "sweden" }) await book.StarAsync(id);
        await Played(host, "sweden", 0);
        await Played(host, "import-hallelujah", 3);

        var view = host.Context.Render<Pages.Today>();

        // Three of the four: the two played, the latest first, then the one kept last.
        view.WaitForAssertion(() => Assert.Equal(["Sweden", "Hallelujah"], view.FindAll(".today-rep-item b").Select(b => b.TextContent).Take(2)));
        Assert.Equal(3, view.FindAll(".today-rep-item").Count);
        Assert.Equal("repertoire", view.Find(".band-link").GetAttribute("href"));
        Assert.Equal("library/sweden", view.Find(".today-rep-item").GetAttribute("href"));
    }

    [Fact]
    public async Task TodayWithNothingKeptHasNoBandForIt()
    {
        await using var host = new ComponentHost();
        await AddThree(host);
        host.Context.Services.GetRequiredService<PrimaSettings>().Welcomed = true;

        var view = host.Context.Render<Pages.Today>();

        view.WaitForAssertion(() => Assert.NotEmpty(view.FindAll(".today-steps .mu-step")));
        Assert.Empty(view.FindAll(".today-rep-item"));
    }
}
