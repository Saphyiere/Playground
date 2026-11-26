using System.Text;
using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Prima.Core.Library;
using Prima.UI.Host;
using Prima.UI.Library;
using Prima.UI.Pages;

namespace Prima.UI.Tests;

/// <summary>
/// RCM's levels on the pieces RCM's 2022 piano syllabus lists (the owner, 25 November 2026: "Do the two", the levels on the pieces
/// first): from the bundle's file beside its manifest, on a piece's front, in the line under it in the Library, and as a list of
/// RCM's levels beside the composers, kinds and levels.
/// </summary>
public class RcmListingTests
{
    private const string Minuet = """{ "id": "own-minuet", "level": 3, "list": "A", "page": 33, "what": "Petzold, Minuet in G, BWV Anh. 114" }""";

    private static string File(params string[] pieces) =>
        $$"""{ "board": "RCM", "edition": "2022", "name": "The Royal Conservatory's Piano Syllabus, 2022 Edition", "url": "https://example.org/rcm.pdf", "pieces": [ {{string.Join(", ", pieces)}} ] }""";

    [Fact]
    public async Task AListedPiecesFrontSaysWhereRcmListsIt_TheLibraryListsItUnderRcmLevel3()
    {
        var bundle = new ListedPieces(File(Minuet));
        await using var host = new ComponentHost(bundle);
        await new LibraryPrep(host.Store, bundle, host.Browser).EnsureAsync();

        var page = host.Context.Render<Piece>(p => p.Add(c => c.Id, "own-minuet"));
        page.WaitForAssertion(() => Assert.Contains(page.FindAll(".piece-about li"), li => li.TextContent == "RCM's 2022 piano syllabus lists it at Level 3, List A (page 33)."));
        var other = host.Context.Render<Piece>(p => p.Add(c => c.Id, "own-musette"));
        other.WaitForAssertion(() => Assert.NotEmpty(other.FindAll(".piece-about li")));
        Assert.DoesNotContain(other.FindAll(".piece-about li"), li => li.TextContent.Contains("RCM", StringComparison.Ordinal));

        var view = host.Context.Render<Pages.Library>();
        view.WaitForAssertion(() => Assert.Contains(view.FindAll(".col-by a"), a => a.TextContent == "RCM"));
        await view.InvokeAsync(() => view.FindAll(".col-by a").Single(a => a.TextContent == "RCM").ClickAsync(new MouseEventArgs()));
        view.WaitForAssertion(() => Assert.Contains("RCM Level 3 1", view.FindAll(".lib-list .col-item").Select(a => $"{a.QuerySelector("span")!.TextContent} {a.QuerySelector(".n")!.TextContent}")));
        Assert.Equal("library?by=rcm&of=3&list=rcm", view.Find(".lib-list .col-item[href*='by=rcm']").GetAttribute("href"));

        host.Context.Services.GetRequiredService<NavigationManager>().NavigateTo("library?by=rcm&of=3&list=rcm");
        var level = host.Context.Render<Pages.Library>();
        level.WaitForAssertion(() => Assert.Equal("RCM Level 3", level.Find(".col-head").TextContent));
        Assert.Contains("RCM's 2022 piano syllabus lists them at its Level 3, to learn and play; the number beside each is Prima's level, where it reads at sight.",
            level.Find(".lib-main-head .col-sub").TextContent, StringComparison.Ordinal);
        Assert.Equal(["Minuet in G"], level.FindAll(".lib-main .piece-title").Select(t => t.TextContent));
        Assert.Contains($"RCM{(char)160}3", level.Find(".lib-main .mov-name small").TextContent, StringComparison.Ordinal);
    }

    // A newer bundle brings its listings as it brings its credits: a piece it lists now gets one, and one it no longer lists
    // loses it.
    [Fact]
    public async Task ANewerBundlesListingsTakeTheOlderOnesPlace()
    {
        var first = new ListedPieces(File(Minuet));
        await using var host = new ComponentHost(first);
        await new LibraryPrep(host.Store, first, host.Browser).EnsureAsync();
        Assert.Equal(["own-minuet"], (await host.Store.GetListingsAsync()).Keys);

        var newer = new ListedPieces(File("""{ "id": "own-musette", "level": 3, "list": "A", "page": 32, "what": "Musette in D, BWV Anh. 126" }"""));
        await new LibraryPrep(host.Store, newer, host.Browser).EnsureAsync();

        var listings = await host.Store.GetListingsAsync();
        Assert.Equal(["own-musette"], listings.Keys);
        Assert.Equal(new Listing("RCM", "2022", 3, "A", 32), listings["own-musette"]);
        Assert.Null(await host.Store.GetListingAsync("own-minuet"));
    }

    // Without a piece RCM lists, there's no RCM list to choose: the Library's list goes by composers, kinds and levels, as before.
    [Fact]
    public async Task WithNoPieceRcmListsTheLibraryHasNoRcmList()
    {
        var bundle = new OwnPieces();
        await using var host = new ComponentHost(bundle);
        await new LibraryPrep(host.Store, bundle, host.Browser).EnsureAsync();

        var view = host.Context.Render<Pages.Library>();

        view.WaitForAssertion(() => Assert.Equal(["Composers", "Kinds", "Levels"], view.FindAll(".col-by a").Select(a => a.TextContent)));
        Assert.DoesNotContain("RCM", view.Find(".library-credits").TextContent, StringComparison.Ordinal);
    }
}

/// <summary>Prima's own two pieces, as <see cref="OwnPieces"/> has them, with a file of where RCM's syllabus lists them.</summary>
internal sealed class ListedPieces(string rcm) : ILibraryBundle
{
    private readonly OwnPieces own = new();

    public Task<LibraryManifest?> ManifestAsync() => own.ManifestAsync();

    public Task<byte[]?> ReadAsync(string path) => path == Syllabus.RcmFile ? Task.FromResult<byte[]?>(Encoding.UTF8.GetBytes(rcm)) : own.ReadAsync(path);
}
