using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Prima.Core.Music;
using Prima.UI.Library;
using Prima.UI.Storage;

namespace Prima.UI.Tests;

// The owner, 17 November: "It doesn't remember anything." A search, a list's filter and where it was scrolled to were gone after
// opening a piece and coming back. They're kept in the list's address now (the scroll in the page, by that address), and the
// rail's Library key goes back to the list as it was left.
public class LibraryPlaceTests
{
    private static async Task Add(ComponentHost host, string id, string composer, string title, string format = "mei")
    {
        var piece = new PieceRecord(id, "collection", title, composer, null, format, format == "pdf" ? $"{id}.pdf" : null, null, 7, 7, null, 4, null, null, null, null, null, null, null, null, null,
            DateTimeOffset.Now, null);
        var bars = Enumerable.Range(1, 4).Select(b => new Bar(b, [new Voice(1, [.. Enumerable.Range(0, 4).Select(q =>
            new Notes($"n{b}-{q}", q * Ticks.Quarter, Ticks.Quarter, [new Tone($"t{b}-{q}", new Spelled(Step.C, 0, 4))]))])])).ToArray();
        await host.Store.SavePieceAsync(piece, format == "pdf" ? null : MeiWriter.Write(new Score(new Prima.Core.Music.Key(0), new Meter(4, 4), bars)), []);
    }

    private static async Task AddFew(ComponentHost host)
    {
        await Add(host, "wtc1", "Johann Sebastian Bach", "Prelude No. 1 in C major, BWV 846");
        await Add(host, "wtc2", "Johann Sebastian Bach", "Fugue No. 2 in C minor, BWV 847");
        await Add(host, "bach-pdf", "Johann Sebastian Bach", "Invention No. 1 in C major, BWV 772", "pdf");
        await Add(host, "op10-1", "Frédéric Chopin", "Étude in C major, Op. 10 No. 1");
        await Add(host, "op10-3", "Frédéric Chopin", "Étude in E major, Op. 10 No. 3");
    }

    [Fact]
    public async Task ASearchGoesInTheListsAddressAndTheListComesBackWithIt()
    {
        await using var host = new ComponentHost();
        await AddFew(host);
        var nav = host.Context.Services.GetRequiredService<NavigationManager>();
        nav.NavigateTo("library");
        var view = host.Context.Render<Pages.Library>();
        view.WaitForAssertion(() => Assert.NotEmpty(view.FindAll(".lib-list .col-item")));

        await view.InvokeAsync(() => view.Find("input[type=search]").InputAsync(new ChangeEventArgs { Value = "chopin 3" }));

        view.WaitForAssertion(() => Assert.EndsWith("library?q=chopin%203", nav.Uri), TimeSpan.FromSeconds(3));
        view.WaitForAssertion(() => Assert.Equal(["Étude in E major, Op. 10 No. 3"], view.FindAll(".piece-title").Select(t => t.TextContent)));

        // A piece opened, and the way back: the Library is drawn afresh at the address it was left at.
        nav.NavigateTo("library/op10-3");
        nav.NavigateTo("library?q=chopin%203");
        var back = host.Context.Render<Pages.Library>();

        back.WaitForAssertion(() => Assert.Equal(["Étude in E major, Op. 10 No. 3"], back.FindAll(".piece-title").Select(t => t.TextContent)));
        Assert.Equal("chopin 3", back.Find("input[type=search]").GetAttribute("value"));
    }

    [Fact]
    public async Task AListsFilterGoesInItsAddressInThePlaceOfTheOneBefore()
    {
        await using var host = new ComponentHost();
        await AddFew(host);
        var nav = host.Context.Services.GetRequiredService<NavigationManager>();
        nav.NavigateTo("library");
        nav.NavigateTo("library?by=level&of=7");
        var view = host.Context.Render<Pages.Library>();
        view.WaitForAssertion(() => Assert.Equal(5, view.FindAll(".piece-item").Count));

        await view.InvokeAsync(() => view.FindAll(".mu-toggle").Single(b => b.TextContent == "PDFs").ClickAsync(new MouseEventArgs()));

        view.WaitForAssertion(() => Assert.EndsWith("library?by=level&of=7&only=pdf", nav.Uri));
        view.WaitForAssertion(() => Assert.Equal(["Invention No. 1 in C major, BWV 772"], view.FindAll(".piece-title").Select(t => t.TextContent)));
        // It took the list's place: the way back goes to the screen before the list, not to the list unfiltered.
        Assert.True(((Bunit.TestDoubles.BunitNavigationManager)nav).History.First().Options.ReplaceHistoryEntry);

        var back = host.Context.Render<Pages.Library>();
        back.WaitForAssertion(() => Assert.Single(back.FindAll(".piece-item")));
        Assert.Equal("true", back.FindAll(".mu-toggle").Single(b => b.TextContent == "PDFs").GetAttribute("aria-pressed"));
    }

    [Fact]
    public async Task AListDrawnIsScrolledToWhereItWasLeftAndLeavingItKeepsThePlace()
    {
        await using var host = new ComponentHost();
        await AddFew(host);
        host.Context.Services.GetRequiredService<NavigationManager>().NavigateTo("library?by=all");

        var view = host.Context.Render<Pages.Library>();

        view.WaitForAssertion(() => Assert.Equal(5, view.FindAll(".piece-item").Count));
        view.WaitForAssertion(() => Assert.Equal(1, host.Browser.ListsPlaced));

        await view.InvokeAsync(() => view.Find("input[type=search]").InputAsync(new ChangeEventArgs { Value = "bach" }));

        view.WaitForAssertion(() => Assert.Equal(3, view.FindAll(".piece-item").Count), TimeSpan.FromSeconds(3));
        view.WaitForAssertion(() => Assert.Equal(2, host.Browser.ListsPlaced));

        await view.InvokeAsync(() => view.Instance.DisposeAsync().AsTask());
        Assert.Equal(1, host.Browser.ListsLeft);
    }

    [Fact]
    public async Task TheRailsLibraryKeyGoesBackToTheListAsItWasLeftAndFromAListToTheLibrarysHome()
    {
        await using var host = new ComponentHost();
        await AddFew(host);
        var nav = host.Context.Services.GetRequiredService<NavigationManager>();
        nav.NavigateTo("library?by=composer&of=Johann%20Sebastian%20Bach&q=fugue");
        var list = host.Context.Render<Pages.Library>();
        list.WaitForAssertion(() => Assert.Single(list.FindAll(".piece-item")));
        var key = host.Context.Render<Layout.LibraryKey>();

        // On the list itself the key goes to the Library's home.
        Assert.Equal("library", key.Find("a").GetAttribute("href"));

        nav.NavigateTo("library/wtc2");
        key.WaitForAssertion(() => Assert.Equal("library?by=composer&of=Johann%20Sebastian%20Bach&q=fugue", key.Find("a").GetAttribute("href")));
        Assert.Equal("page", key.Find("a").GetAttribute("aria-current"));

        nav.NavigateTo("");
        key.WaitForAssertion(() => Assert.Null(key.Find("a").GetAttribute("aria-current")));
        Assert.Equal("library?by=composer&of=Johann%20Sebastian%20Bach&q=fugue", key.Find("a").GetAttribute("href"));
        Assert.Equal("library?by=composer&of=Johann%20Sebastian%20Bach&q=fugue", host.Context.Services.GetRequiredService<LibraryPlace>().Last);
    }
}
