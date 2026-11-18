using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Playground.Kit;
using Prima.Core.Music;
using Prima.UI.Components;
using Prima.UI.Storage;

namespace Prima.UI.Tests;

// The owner, 17 November: "The loading needs an animation ... I can't back out ... it's seemingly frozen, which is a very, very bad
// thing." A long piece was engraved twice as it opened (as pages, then as the scroll), on an empty page that said only "Opening…".
public class OpeningAPieceTests
{
    // A piece of many bars, its notes read away from the screen's thread for a while: long enough for the page to be drawn again
    // as it opens.
    private static async Task<PieceRecord> AddLong(ComponentHost host, int bars = 400)
    {
        var piece = new PieceRecord("long", "collection", "Sonata No. 21 'Waldstein', Op. 53: III. Rondo", "Ludwig van Beethoven", "Piano sonatas", "mei", null, null, 12, 9, null, bars,
            null, null, null, null, null, null, null, null, null, DateTimeOffset.Now, null);
        var music = Enumerable.Range(1, bars).Select(b => new Bar(b, [new Voice(1, [.. Enumerable.Range(0, 8).Select(q =>
            new Notes($"n{b}-{q}", q * Ticks.Quarter / 2, Ticks.Quarter / 2, [new Tone($"t{b}-{q}", new Spelled(Step.C, 0, 4 + q % 2))]))])])).ToArray();
        await host.Store.SavePieceAsync(piece, MeiWriter.Write(new Score(new Prima.Core.Music.Key(0), new Meter(4, 4), music)), []);
        return piece;
    }

    [Fact]
    public async Task APieceIsEngravedOnceInItsOwnView()
    {
        await using var host = new ComponentHost();
        await AddLong(host, bars: 2000);

        // The page drawn again and again as it opens, as the tablet's page is (a key, the piano, the page's own scripts loading).
        var page = await Opened(host);

        var render = Assert.Single(host.Browser.Renders);
        Assert.Equal("scroll", render.Options.GetProperty("layout").GetString());

        // Set to Pages, it opens as pages the next time, and only as pages.
        await host.Store.SetSettingAsync("Library.View.long", "pages");
        host.Browser.Renders.Clear();
        await Opened(host);
        Assert.Equal(["page"], host.Browser.Renders.Select(r => r.Options.GetProperty("layout").GetString()));
    }

    // The piece opened, drawn again every few milliseconds until its sheet is there, and a moment more.
    private static async Task<IRenderedComponent<Pages.Piece>> Opened(ComponentHost host)
    {
        var page = host.Context.Render<Pages.Piece>(p => p.Add(c => c.Id, "long"));
        for (var waited = 0; page.FindAll(".sheet").Count == 0 && waited < 10_000; waited += 5)
        {
            page.Render();
            await Task.Delay(5, Xunit.TestContext.Current.CancellationToken);
        }
        await Task.Delay(300, Xunit.TestContext.Current.CancellationToken);
        return page;
    }

    [Fact]
    public async Task WhileItOpensItsHeadAndBarShowWithTheWayBackUntilItsMusicIsDrawn()
    {
        await using var host = new ComponentHost();
        await AddLong(host);
        var nav = host.Context.Services.GetRequiredService<NavigationManager>();
        var navigation = host.Context.Services.GetRequiredService<AppNavigation>();
        nav.NavigateTo("library?by=composer&of=Ludwig%20van%20Beethoven");
        navigation.Name("Ludwig van Beethoven");
        nav.NavigateTo("library/long");

        var page = host.Context.Render<Pages.Piece>(p => p.Add(c => c.Id, "long"));

        // The head, the bar and the paper waiting, with what's happening and the way back, named for where it goes.
        page.WaitForElement(".piece-opening");
        Assert.Equal("Sonata No. 21 'Waldstein', Op. 53: III. Rondo", page.Find(".reading-head h1").TextContent);
        Assert.Equal("Ludwig van Beethoven · Piano sonatas", page.Find(".reading-head .page-kicker").TextContent);
        page.WaitForAssertion(() => Assert.Equal("Engraving its 400 bars…", page.Find(".piece-opening-words").TextContent));
        Assert.Equal("‹ Ludwig van Beethoven", page.Find(".piece-opening .mu-btn").TextContent);

        // Its music read, the sheet is engraving it, and it's still opening; drawn, it's gone.
        page.WaitForElement(".sheet");
        Assert.Single(page.FindAll(".piece-opening"));
        await page.InvokeAsync(() => page.FindComponent<Sheet>().Instance.Painted(1));
        Assert.Empty(page.FindAll(".piece-opening"));
    }

    [Fact]
    public async Task TheWayBackWhileItOpensGoesBackToTheList()
    {
        await using var host = new ComponentHost();
        await AddLong(host);
        var nav = host.Context.Services.GetRequiredService<NavigationManager>();
        host.Context.Services.GetRequiredService<AppNavigation>();
        nav.NavigateTo("library?by=all");
        nav.NavigateTo("library/long");
        var page = host.Context.Render<Pages.Piece>(p => p.Add(c => c.Id, "long"));
        page.WaitForElement(".piece-opening .mu-btn");

        await page.InvokeAsync(() => page.Find(".piece-opening .mu-btn").ClickAsync(new MouseEventArgs()));

        Assert.Equal([-1], host.Browser.WentBack);
    }
}
