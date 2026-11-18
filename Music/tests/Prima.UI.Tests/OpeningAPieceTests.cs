using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Playground.Kit;
using Prima.Core.Library;
using Prima.Core.Music;
using Prima.UI.Components;
using Prima.UI.Host;
using Prima.UI.Storage;

namespace Prima.UI.Tests;

// The owner, 17 November: "The loading needs an animation ... I can't back out. I can't cancel the loading ... it's seemingly
// frozen, which is a very, very bad thing." A long piece was engraved twice as it opened (as pages, then as the scroll), on an
// empty page that said only "Opening…". Now its head, its bar and its paper are there at once, the paper waiting; its lines show
// as they're drawn; and Cancel, then Stop, are there all the while.
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

    // The piece opened from a composer's list, which named itself for the way back.
    private static IRenderedComponent<Pages.Piece> OpenFromItsList(ComponentHost host)
    {
        var nav = host.Context.Services.GetRequiredService<NavigationManager>();
        var navigation = host.Context.Services.GetRequiredService<AppNavigation>();
        nav.NavigateTo("library?by=composer&of=Ludwig%20van%20Beethoven");
        navigation.Name("Ludwig van Beethoven");
        nav.NavigateTo("library/long");
        return host.Context.Render<Pages.Piece>(p => p.Add(c => c.Id, "long"));
    }

    // Looked for until it's so: nothing on the page is drawn when its sheet is given its music, to wake a wait for it.
    private static async Task Eventually(Action assertion)
    {
        for (var waited = 0; ; waited += 50)
        {
            try
            {
                assertion();
                return;
            }
            catch (Xunit.Sdk.XunitException) when (waited < 10_000)
            {
                await Task.Delay(50, Xunit.TestContext.Current.CancellationToken);
            }
        }
    }

    private static Task Says(IRenderedComponent<Pages.Piece> page, int lines, int total, string how) => page.InvokeAsync(() => page.FindComponent<Sheet>().Instance.Progress(lines, total, how));

    [Fact]
    public async Task APieceIsEngravedOnceInItsOwnView()
    {
        await using var host = new ComponentHost();
        await AddLong(host, bars: 2000);

        // The page drawn again and again as it opens, as the tablet's page is (a key, the piano, the page's own scripts loading).
        await Opened(host);

        var render = Assert.Single(host.Browser.Renders);
        Assert.Equal("scroll", render.Options.GetProperty("layout").GetString());

        // Set to Pages, it opens as pages the next time, and only as pages.
        await host.Store.SetSettingAsync("Library.View.long", "pages");
        host.Browser.Renders.Clear();
        await Opened(host);
        Assert.Equal(["page"], host.Browser.Renders.Select(r => r.Options.GetProperty("layout").GetString()));
    }

    // The piece opened, drawn again every few milliseconds until its sheet has its music, and a moment more.
    private static async Task<IRenderedComponent<Pages.Piece>> Opened(ComponentHost host)
    {
        var page = host.Context.Render<Pages.Piece>(p => p.Add(c => c.Id, "long"));
        for (var waited = 0; host.Browser.Renders.Count == 0 && waited < 10_000; waited += 5)
        {
            page.Render();
            await Task.Delay(5, Xunit.TestContext.Current.CancellationToken);
        }
        await Task.Delay(300, Xunit.TestContext.Current.CancellationToken);
        return page;
    }

    [Fact]
    public async Task ItsHeadItsBarAndItsPaperAreThereAtOnceWithTheWayBackAndCancel()
    {
        await using var host = new ComponentHost();
        await AddLong(host);
        host.Browser.DrawsPieces = false;

        var page = OpenFromItsList(host);

        // The head, with the way back named for where it goes; the bar, its keys off; and the paper, waiting.
        Assert.Equal("Ludwig van Beethoven", page.WaitForElement(".reading-head .way-back").TextContent);
        Assert.Equal("Sonata No. 21 'Waldstein', Op. 53: III. Rondo", page.Find(".reading-head h1").TextContent);
        Assert.Equal("Ludwig van Beethoven · Piano sonatas", page.Find(".reading-head .page-kicker").TextContent);
        Assert.Equal(["Level", "Bars"], page.FindAll(".piece-front-bar .mu-readout-label").Select(l => l.TextContent));
        Assert.Equal("400", page.FindAll(".piece-front-bar .mu-readout-value")[1].TextContent);
        Assert.Equal("Getting its music ready", page.Find(".piece-front-bar .bar-note").TextContent);
        Assert.All(page.FindAll(".piece-front-bar .piece-key"), key => Assert.True(key.HasAttribute("disabled")));
        Assert.NotNull(page.Find(".piece-frame .sheet.sheet-whole .sheet-wait .sheet-wait-sweep"));
        Assert.NotNull(page.Find(".piece-about"));

        // What's happening, with Cancel, on a card over the paper: still there once its music is read and being laid out.
        Assert.Equal("Engraving 400 bars", page.Find(".piece-wait .piece-wait-what").TextContent);
        Assert.Equal("Cancel", page.Find(".piece-wait .mu-btn").TextContent);
        await Eventually(() => Assert.Single(host.Browser.Renders));
        Assert.Equal("Cancel", page.Find(".piece-wait .mu-btn").TextContent);
        Assert.All(page.FindAll(".piece-front-bar .piece-key"), key => Assert.True(key.HasAttribute("disabled")));

        // Cancel goes back to the list.
        await page.InvokeAsync(() => page.Find(".piece-wait .mu-btn").ClickAsync(new MouseEventArgs()));
        Assert.Equal([-1], host.Browser.WentBack);
    }

    [Fact]
    public async Task ItsFirstLinesAreToReadAndPlayWhileTheRestComesWithStop()
    {
        await using var host = new ComponentHost();
        await AddLong(host);
        host.Browser.DrawsPieces = false;
        var page = OpenFromItsList(host);
        await Eventually(() => Assert.Single(host.Browser.Renders));

        // Its first lines drawn: the bar says how far it has got, its keys are on, and the card has Stop.
        await Says(page, 6, 38, "coming");
        Assert.Equal("Line 6 of 38", page.Find(".piece-front-bar .bar-note").TextContent);
        Assert.All(page.FindAll(".piece-front-bar .piece-key"), key => Assert.False(key.HasAttribute("disabled")));
        Assert.Equal("The rest is on its way", page.Find(".piece-wait .piece-wait-what").TextContent);
        Assert.Equal("Line 6 of 38 · read and scroll what's here, or start from bar 1.", page.Find(".piece-wait .piece-wait-more").TextContent);
        Assert.Equal(3, page.FindAll(".piece-wait-meter .on").Count);
        Assert.Equal("Stop", page.Find(".piece-wait .mu-btn").TextContent);

        // Stop: the sheet is told, and says where it stopped. The card goes; the bar says where, with the key that draws the rest.
        await page.InvokeAsync(() => page.Find(".piece-wait .mu-btn").ClickAsync(new MouseEventArgs()));
        Assert.Equal(1, host.Browser.SheetStops);
        await Says(page, 8, 38, "stopped");
        Assert.Empty(page.FindAll(".piece-wait"));
        Assert.Equal("Stopped at line 8 of 38", page.Find(".piece-front-bar .bar-note").TextContent);
        Assert.All(page.FindAll(".piece-front-bar .piece-key"), key => Assert.False(key.HasAttribute("disabled")));
        await page.InvokeAsync(() => page.Find(".piece-front-bar .bar-note-key").ClickAsync(new MouseEventArgs()));
        Assert.Equal(1, host.Browser.SheetRests);

        // The rest coming again, then all of it there: nothing more is said.
        await Says(page, 20, 38, "coming");
        Assert.Equal("Stop", page.Find(".piece-wait .mu-btn").TextContent);
        await Says(page, 38, 38, "done");
        Assert.Empty(page.FindAll(".piece-wait"));
        Assert.Empty(page.FindAll(".piece-front-bar .bar-note"));
        Assert.Empty(page.FindAll(".piece-front-bar .bar-note-key"));
    }

    [Fact]
    public async Task TheWayBackGoesBackToTheListAndSaysLibraryWhereItIsntKnown()
    {
        await using var host = new ComponentHost();
        await AddLong(host);
        host.Browser.DrawsPieces = false;
        var nav = host.Context.Services.GetRequiredService<NavigationManager>();
        host.Context.Services.GetRequiredService<AppNavigation>();
        // Opened from a list that didn't name itself.
        nav.NavigateTo("library?by=all");
        nav.NavigateTo("library/long");
        var page = host.Context.Render<Pages.Piece>(p => p.Add(c => c.Id, "long"));

        Assert.Equal("Library", page.WaitForElement(".reading-head .way-back").TextContent);
        await page.InvokeAsync(() => page.Find(".reading-head .way-back").ClickAsync(new MouseEventArgs()));

        Assert.Equal([-1], host.Browser.WentBack);
    }

    [Fact]
    public async Task AMusicThatCantBeDrawnSaysSoWithTheWayBack()
    {
        await using var host = new ComponentHost();
        await AddLong(host);
        host.Browser.DrawsPieces = false;
        var page = OpenFromItsList(host);
        await Eventually(() => Assert.Single(host.Browser.Renders));

        await Says(page, 0, 0, "failed");

        Assert.Equal("Its music couldn't be drawn", page.Find(".piece-wait .piece-wait-what").TextContent);
        Assert.Equal("Back", page.Find(".piece-wait .mu-btn").TextContent);
        Assert.Equal("Not drawn", page.Find(".piece-front-bar .bar-note").TextContent);
    }

    [Fact]
    public async Task APdfShowsItsFirstPageWhileItsSystemsAreFoundBehindIt()
    {
        await using var host = new ComponentHost(new OnePdf());
        var piece = await host.AddPiece(pdf: true, bundled: true);
        host.Browser.PageRatios = [0.75, 0.75, 0.75];
        host.Browser.Profile = new double[300];
        var found = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        host.Browser.ProfileReady = found.Task;
        try
        {
            var page = host.Context.Render<Pages.Piece>(p => p.Add(c => c.Id, piece.Id));

            // Its first page is drawn while its first page's systems are still being found: the bar says which pages are to
            // come, its pages are down the frame's edge with those in outline, and scrolling waits for them.
            page.WaitForAssertion(() => Assert.NotEmpty(host.Browser.ShownPdfs));
            page.WaitForAssertion(() => Assert.Equal("Pages 1 to 3 still coming", page.Find(".piece-bar .bar-note").TextContent));
            Assert.Equal([1], host.Browser.ProfiledPages);
            Assert.Equal(3, page.FindAll(".pages-strip .wait").Count);
            Assert.Empty(page.FindAll(".piece-pdf-wait"));
            Assert.Empty(page.FindAll(".piece-wait"));
            Assert.Null((await host.Store.GetPieceAsync(piece.Id))!.Layout);

            // Found: kept with the piece, and nothing more is said.
            found.SetResult();
            page.WaitForAssertion(() => Assert.Empty(page.FindAll(".piece-bar .bar-note")));
            Assert.Equal([1, 2, 3], host.Browser.ProfiledPages);
            Assert.Empty(page.FindAll(".pages-strip"));
            page.WaitForAssertion(() => Assert.NotNull(host.Store.GetPieceAsync(piece.Id).GetAwaiter().GetResult()!.Layout));
        }
        finally
        {
            found.TrySetResult();
        }
    }

    private sealed class OnePdf : ILibraryBundle
    {
        public Task<LibraryManifest?> ManifestAsync() => Task.FromResult<LibraryManifest?>(null);
        public Task<byte[]?> ReadAsync(string path) => Task.FromResult<byte[]?>([1]);
    }
}
