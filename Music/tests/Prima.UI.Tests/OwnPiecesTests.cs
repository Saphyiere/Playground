using System.IO.Compression;
using System.Text;
using System.Text.Json;
using Bunit;
using Microsoft.AspNetCore.Components.Web;
using Prima.Core.Library;
using Prima.Core.Music;
using Prima.UI.Host;
using Prima.UI.Library;
using Prima.UI.Pages;
using Prima.UI.Storage;

namespace Prima.UI.Tests;

/// <summary>
/// Prima's own pieces, the ones that come with it, taken out of the Library: one removed by hand stays out, and the Library puts
/// them back when it's asked to (the owner's rule 3 and the reviewing session's suggestion, decided 15 November: the review of
/// main at a7ab94d, part 6).
/// </summary>
public class OwnPiecesTests
{
    private static readonly DateOnly Today = DateOnly.FromDateTime(DateTime.Now);

    // Rule 3, by hand: one of Prima's own removed from its page stays out, the way Remove library keeps them all out. The Library
    // storing Prima's own again (as it does each time Prima opens) leaves it out, and the runs kept on it still count.
    [Fact]
    public async Task OneOfPrimasOwnRemovedByHandStaysOutOfTheLibrary_TheRunsOnItStillCounted()
    {
        var bundle = new OwnPieces();
        await using var host = new ComponentHost(bundle);
        await new LibraryPrep(host.Store, bundle, host.Browser).EnsureAsync();
        await host.Store.AddAttemptAsync(new AttemptRecord(0, DateTimeOffset.Now, Today, "piece", "in time", "own-musette", 4, 72, null, null, "[]", null,
            JsonSerializer.Serialize(new { inTime = 0.9, right = 9, wrong = 1, missed = 0, from = 1, hands = "both" }), 0.9, false, null));
        var page = host.Context.Render<Piece>(p => p.Add(c => c.Id, "own-musette"));
        page.WaitForAssertion(() => Assert.Contains(page.FindAll("button"), b => b.TextContent == "Remove from the Library…"));

        // Asked first, saying it stays out.
        await page.InvokeAsync(() => page.FindAll("button").Single(b => b.TextContent == "Remove from the Library…").ClickAsync(new MouseEventArgs()));
        Assert.Equal("One of Prima's own pieces: it stays out of the Library, and Prima won't put it back by itself (the Library has a key that puts Prima's own back). Your runs on it still count.",
            page.Find(".piece-about [role=status]").TextContent);
        await page.InvokeAsync(() => page.FindAll("button").Single(b => b.TextContent == "Remove it").ClickAsync(new MouseEventArgs()));

        Assert.Equal(["own-minuet"], await Ids(host));
        Assert.DoesNotContain(await host.Store.GetExcerptsAsync(4, 1, Today), e => e.Piece == "own-musette");
        await new LibraryPrep(host.Store, bundle, host.Browser).EnsureAsync();
        Assert.Equal(["own-minuet"], await Ids(host));
        Assert.Single(await host.Store.GetPieceRunsAsync("own-musette"));
    }

    // The reviewing session's suggestion, not the owner's words (review part 6, rule 3), so the owner can turn it down: with Prima's
    // own pieces taken out, the Library has a key that puts them back, every one, those removed by hand too.
    [Fact]
    public async Task TheLibraryPutsPrimasOwnPiecesBackWhenAsked_EveryOneOfThem()
    {
        var bundle = new OwnPieces();
        await using var host = new ComponentHost(bundle);
        await new LibraryPrep(host.Store, bundle, host.Browser).EnsureAsync();
        await host.Store.RemovePieceAsync("own-musette");
        await host.Store.RemoveLibraryAsync();
        var view = host.Context.Render<Pages.Library>();
        Assert.Equal("Put Prima's pieces back", view.WaitForElement(".library-put-back button").TextContent);
        Assert.StartsWith("Prima's own pieces are out of the Library: Remove library took them out", view.Find(".page-note").TextContent, StringComparison.Ordinal);

        await view.InvokeAsync(() => view.Find(".library-put-back button").ClickAsync(new MouseEventArgs()));

        view.WaitForAssertion(() => Assert.Contains("Everything 2", view.FindAll(".shelf-chip").Select(a => a.TextContent.Trim())));
        Assert.Empty(view.FindAll(".library-put-back"));
        Assert.Equal(["own-minuet", "own-musette"], await Ids(host));

        // One removed by hand after that: the home's last line says so, with a key to put it back too.
        await host.Store.RemovePieceAsync("own-minuet");
        var again = host.Context.Render<Pages.Library>();
        again.WaitForAssertion(() => Assert.Equal("1 of Prima's own pieces is out of the Library: you removed it, and Prima won't put it back by itself.",
            again.Find(".library-put-back .section-note").TextContent));
        Assert.Equal("Put it back", again.Find(".library-put-back button").TextContent);

        await again.InvokeAsync(() => again.Find(".library-put-back button").ClickAsync(new MouseEventArgs()));

        again.WaitForAssertion(() => Assert.Empty(again.FindAll(".library-put-back")));
        Assert.Equal(["own-minuet", "own-musette"], await Ids(host));
    }

    internal static async Task<string[]> Ids(ComponentHost host) => [.. (await host.Store.GetPieceIdsAsync()).Order(StringComparer.Ordinal)];
}

/// <summary>Prima's own pieces, as its bundle has them: two prepared from MusicXML, a bar of one note each, with an excerpt.</summary>
internal sealed class OwnPieces : ILibraryBundle
{
    private static readonly byte[] Music = Zipped(MeiWriter.Write(new Score(new Prima.Core.Music.Key(0), new Meter(4, 4),
        [new Bar(1, [new Voice(1, [new Notes("n1", 0, 1920, [new Tone("t1", new Spelled(Step.C, 0, 4))])])])])));

    public Task<LibraryManifest?> ManifestAsync() => Task.FromResult<LibraryManifest?>(new LibraryManifest(1, "2026-11-15", [Piece("own-minuet", "Minuet in G"), Piece("own-musette", "Musette in D")]));

    public Task<byte[]?> ReadAsync(string path) => Task.FromResult<byte[]?>(path.EndsWith(".mei.gz", StringComparison.Ordinal) ? Music : null);

    private static BundledPiece Piece(string id, string title) => new($"{id}-score", title, "Johann Sebastian Bach", "Notebook for Anna Magdalena Bach", "mxl", $"{id}.mxl", null, "",
        "Mutopia", "", "Public domain", "", "Public domain.", Tier.A, 1, Prepared: [new PreparedPiece(id, title, $"{id}.mei.gz", 4, 4, "one note", 1, [new BundledExcerpt(1, 1, 4, 0.5)])]);

    private static byte[] Zipped(string text)
    {
        using var bytes = new MemoryStream();
        using (var zip = new GZipStream(bytes, CompressionLevel.Fastest, leaveOpen: true)) zip.Write(Encoding.UTF8.GetBytes(text));
        return bytes.ToArray();
    }
}
