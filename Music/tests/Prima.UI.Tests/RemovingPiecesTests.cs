using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Playground.Kit;
using Prima.Core.Library;
using Prima.Core.Music;
using Prima.UI.Library;
using Prima.UI.Storage;

namespace Prima.UI.Tests;

// The owner, 17 November: "If I remove something, it doesn't remember that." Removing a piece threw you to the Library's top and
// said nothing. It goes back to the list it was opened from now, which says what went and can put it back.
public class RemovingPiecesTests
{
    private static readonly DateOnly Today = DateOnly.FromDateTime(DateTime.Now);

    private static async Task<PieceRecord> Add(ComponentHost host, string id, string title, string origin = "collection")
    {
        var piece = new PieceRecord(id, origin, title, "Johann Sebastian Bach", null, "mei", null, null, 5, 5, "one note", 4, null, null, null, null, null, null, null, null, null,
            DateTimeOffset.Now, null);
        var bars = Enumerable.Range(1, 4).Select(b => new Bar(b, [new Voice(1, [.. Enumerable.Range(0, 4).Select(q =>
            new Notes($"n{b}-{q}", q * Ticks.Quarter, Ticks.Quarter, [new Tone($"t{b}-{q}", new Spelled(Step.C, 0, 4))]))])])).ToArray();
        await host.Store.SavePieceAsync(piece, MeiWriter.Write(new Score(new Prima.Core.Music.Key(0), new Meter(4, 4), bars)), [new BundledExcerpt(1, 2, 5, 0.5), new BundledExcerpt(3, 4, 5, 0.25)]);
        return piece;
    }

    [Fact]
    public async Task UndoPutsAPieceTakenOutBackAsItWas_ItsMusicExcerptsReadingAndFiles()
    {
        await using var host = new ComponentHost();
        await Add(host, "wtc1", "Prelude No. 1 in C major, BWV 846");
        await host.Store.SetPieceAsync("wtc1", tempo: 66, position: "{\"from\":3}");
        var excerpt = (await host.Store.GetExcerptsAsync(5, 2, Today)).First(e => e.Piece == "wtc1");
        await host.Store.MarkExcerptReadAsync(excerpt.Id, Today.AddDays(-30));
        // An upload's PDF, with its MIDI beside it and a reading of its notes waiting to be looked over.
        Directory.CreateDirectory(host.Store.FilesPath);
        await File.WriteAllBytesAsync(Path.Combine(host.Store.FilesPath, "import-scan.pdf"), "%PDF-1.4 a scan"u8.ToArray(), Xunit.TestContext.Current.CancellationToken);
        await File.WriteAllBytesAsync(Path.Combine(host.Store.FilesPath, "import-scan.mid"), "MThd"u8.ToArray(), Xunit.TestContext.Current.CancellationToken);
        await host.Store.SavePieceAsync(new PieceRecord("import-scan", "import", "A scan", "", null, "pdf", "import-scan.pdf", "import-scan.mid", null, null, null, null, "My scan.pdf", null,
            null, null, null, null, null, null, null, DateTimeOffset.Now, null), null, []);
        await host.Store.SaveReadingAsync("import-scan", "{}");
        var before = (Piece: await host.Store.GetPieceAsync("wtc1"), Mei: await host.Store.GetMeiAsync("wtc1"), Excerpts: await Excerpts(host, "wtc1"));

        var removedPiece = await host.Store.RemovePieceAsync("wtc1");
        var removedScan = await host.Store.RemovePieceAsync("import-scan");

        Assert.Null(await host.Store.GetPieceAsync("wtc1"));
        Assert.Null(await host.Store.GetPieceAsync("import-scan"));
        Assert.False(File.Exists(Path.Combine(host.Store.FilesPath, "import-scan.pdf")));
        Assert.Contains("wtc1", await host.Store.GetCollectionPiecesOutAsync());

        await host.Store.PutPieceBackAsync(removedPiece!);
        await host.Store.PutPieceBackAsync(removedScan!);

        Assert.Equal(before.Piece, await host.Store.GetPieceAsync("wtc1"));
        Assert.Equal(before.Mei, await host.Store.GetMeiAsync("wtc1"));
        Assert.Equal(before.Excerpts, await Excerpts(host, "wtc1"));
        Assert.DoesNotContain("wtc1", await host.Store.GetCollectionPiecesOutAsync());
        Assert.NotNull(await host.Store.GetReadingAsync("import-scan"));
        Assert.Equal("%PDF-1.4 a scan"u8.ToArray(), await File.ReadAllBytesAsync(Path.Combine(host.Store.FilesPath, "import-scan.pdf"), Xunit.TestContext.Current.CancellationToken));
        Assert.True(File.Exists(Path.Combine(host.Store.FilesPath, "import-scan.mid")));
        foreach (var file in new[] { "import-scan.pdf", "import-scan.mid" }) File.Delete(Path.Combine(host.Store.FilesPath, file));
    }

    [Fact]
    public async Task RemovingAPieceGoesBackToTheListItWasOpenedFromWhichSaysWhatWentAndUndoPutsItBack()
    {
        await using var host = new ComponentHost();
        await Add(host, "wtc1", "Prelude No. 1 in C major, BWV 846");
        await Add(host, "wtc2", "Fugue No. 2 in C minor, BWV 847");
        var nav = host.Context.Services.GetRequiredService<NavigationManager>();
        host.Context.Services.GetRequiredService<AppNavigation>();
        nav.NavigateTo("library?by=all&q=bwv");
        nav.NavigateTo("library/wtc1");
        var page = host.Context.Render<Pages.Piece>(p => p.Add(c => c.Id, "wtc1"));
        page.WaitForAssertion(() => Assert.Contains(page.FindAll("button"), b => b.TextContent == "Remove from the Library…"));
        await page.InvokeAsync(() => page.FindAll("button").Single(b => b.TextContent == "Remove from the Library…").ClickAsync(new MouseEventArgs()));
        Assert.Equal("From your collection: it stays out of the Library, even when the collection is imported again (the Library has a key that lets it back in). Your runs on it still count.",
            page.Find(".piece-about [role=status]").TextContent);

        await page.InvokeAsync(() => page.FindAll("button").Single(b => b.TextContent == "Remove it").ClickAsync(new MouseEventArgs()));

        // A step back to the list, as the way back takes it, which comes back with its search.
        Assert.Equal([-1], host.Browser.WentBack);
        nav.NavigateTo("library?by=all&q=bwv");
        var list = host.Context.Render<Pages.Library>();
        list.WaitForAssertion(() => Assert.Equal(["Fugue No. 2 in C minor, BWV 847"], list.FindAll(".piece-title").Select(t => t.TextContent)));
        Assert.Equal("Removed “Prelude No. 1 in C major, BWV 846” from the Library.", list.Find(".library-removed .control-note").TextContent);

        await list.InvokeAsync(() => list.Find(".library-removed button").ClickAsync(new MouseEventArgs()));

        list.WaitForAssertion(() => Assert.Equal(2, list.FindAll(".piece-item").Count));
        Assert.Empty(list.FindAll(".library-removed"));
        Assert.Equal("“Prelude No. 1 in C major, BWV 846” is back in the Library.", list.Find(".control-note").TextContent);
        Assert.NotNull(await host.Store.GetMeiAsync("wtc1"));
    }

    [Fact]
    public async Task LeavingTheLibraryAfterwardsKeepsThePieceOut()
    {
        await using var host = new ComponentHost();
        await Add(host, "wtc1", "Prelude No. 1 in C major, BWV 846");
        var place = host.Context.Services.GetRequiredService<LibraryPlace>();
        place.Removed = await host.Store.RemovePieceAsync("wtc1");
        host.Context.Services.GetRequiredService<NavigationManager>().NavigateTo("library?by=all");
        var list = host.Context.Render<Pages.Library>();
        list.WaitForElement(".library-removed");

        await list.InvokeAsync(() => list.Instance.DisposeAsync().AsTask());

        Assert.Null(place.Removed);
        Assert.Null(await host.Store.GetPieceAsync("wtc1"));
    }

    private static async Task<List<(int FirstBar, int LastBar, int Level, double Within, int Used, DateOnly? LastUsed)>> Excerpts(ComponentHost host, string piece) =>
        [.. (await host.Store.GetExcerptsAsync(20, 2, Today.AddDays(1), restDays: 0)).Where(e => e.Piece == piece).Select(e => (e.FirstBar, e.LastBar, e.Level, e.Within, e.Used, e.LastUsed))];
}
