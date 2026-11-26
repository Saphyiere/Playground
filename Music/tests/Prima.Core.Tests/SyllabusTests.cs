using System.Text.Json;
using Prima.Core.Library;

namespace Prima.Core.Tests;

/// <summary>
/// Where RCM's 2022 piano syllabus lists Prima's own pieces (the owner, 25 November 2026: "Do the two", the levels on the pieces
/// RCM lists being the first): the bundle's file of them, each a fact about one piece with its page, read and checked.
/// </summary>
public class SyllabusTests
{
    private const string Sample = """
        {
          "board": "RCM",
          "edition": "2022",
          "name": "The Royal Conservatory's Piano Syllabus, 2022 Edition",
          "url": "https://example.org/rcm.pdf",
          "about": "Facts about Prima's own pieces.",
          "pieces": [
            { "id": "minuet", "level": 3, "list": "A", "page": 33, "what": "Petzold, Minuet in G, BWV Anh. 114" },
            { "id": "arabesque", "level": 3, "etude": true, "page": 31, "what": "Burgmüller, Arabesque, Op. 100 No. 2" }
          ]
        }
        """;

    [Fact]
    public void ASyllabusFileGivesEachListedPieceItsLevelListAndPage_AnEtudeWithoutAList()
    {
        var syllabus = Syllabus.Read(Sample);

        Assert.Equal(("RCM", "2022", "https://example.org/rcm.pdf"), (syllabus.Board, syllabus.Edition, syllabus.Url));
        var listings = syllabus.Listings.ToDictionary(l => l.Piece, l => l.Listing);
        Assert.Equal(new Listing("RCM", "2022", 3, "A", 33), listings["minuet"]);
        Assert.Equal(new Listing("RCM", "2022", 3, null, 31), listings["arabesque"]);
        Assert.True(listings["arabesque"].Etude);
    }

    [Fact]
    public void AListingIsSaidAsTheBoardsOwnStatement_RcmsLevelUnderAPieceAndItsPlaceOnItsFront()
    {
        var listed = new Listing("RCM", "2022", 3, "A", 33);
        var etude = new Listing("RCM", "2022", 4, null, 38);

        Assert.Equal($"RCM{(char)160}3", listed.Short);
        Assert.Equal("RCM's 2022 piano syllabus lists it at Level 3, List A (page 33).", listed.Sentence);
        Assert.Equal("RCM's 2022 piano syllabus has it among the Level 4 études (page 38).", etude.Sentence);
    }

    [Theory]
    [InlineData("""{ "id": "x", "level": 3, "page": 33 }""")]
    [InlineData("""{ "id": "x", "level": 3, "list": "A", "etude": true, "page": 33 }""")]
    [InlineData("""{ "id": "x", "level": 11, "list": "A", "page": 33 }""")]
    [InlineData("""{ "id": "x", "level": 3, "list": "E", "page": 33 }""")]
    [InlineData("""{ "id": "x", "level": 3, "list": "A", "page": 0 }""")]
    [InlineData("""{ "level": 3, "list": "A", "page": 33 }""")]
    public void AnEntryWithoutALevelFromOneToTenAPageAndEitherAListOrAnEtudeIsRefused(string entry)
    {
        var json = $$"""{ "board": "RCM", "edition": "2022", "name": "RCM", "url": "", "pieces": [ {{entry}} ] }""";

        Assert.Throws<JsonException>(() => Syllabus.Read(json));
    }

    // The file is written by hand, beside a manifest the Library tool writes: a piece the tool no longer keeps under the same id
    // (a source's file changed, a collection split differently) would lose its listing without a word, so this names it.
    [Fact]
    public void TheBundlesRcmFileListsOnlyPiecesTheBundleKeeps_EachOnce()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "Directory.Build.props"))) root = root.Parent;
        var library = Path.Combine(root!.FullName, "Music", "library");
        var kept = LibraryManifest.Kept(LibraryManifest.Read(File.ReadAllText(Path.Combine(library, "manifest.json"))).Pieces).ToHashSet();

        var syllabus = Syllabus.Read(File.ReadAllText(Path.Combine(library, Syllabus.RcmFile)));

        Assert.Equal(("RCM", "2022"), (syllabus.Board, syllabus.Edition));
        Assert.DoesNotContain(syllabus.Pieces, p => !kept.Contains(p.Id));
        Assert.Empty(syllabus.Pieces.GroupBy(p => p.Id).Where(g => g.Count() > 1).Select(g => g.Key));
        Assert.All(syllabus.Pieces, p => Assert.False(string.IsNullOrWhiteSpace(p.What)));
    }
}
