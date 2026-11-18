using System.Xml.Linq;
using Prima.Core.Music;

namespace Prima.Core.Tests;

public class LyricDynamicsTests
{
    private static readonly XNamespace Mei = "http://www.music-encoding.org/ns/mei";
    private static readonly XNamespace Xml = XNamespace.Xml;

    // A grand staff in 4/4, its bars given: each a <measure> of staves (and the marks after them).
    private static string Piece(params string[] measures) =>
        $"""<mei xmlns="http://www.music-encoding.org/ns/mei" meiversion="5.0"><music><body><mdiv><score><scoreDef meter.count="4" meter.unit="4"><staffGrp symbol="brace"><staffDef n="1" lines="5" clef.shape="G" clef.line="2"/><staffDef n="2" lines="5" clef.shape="F" clef.line="4"/></staffGrp></scoreDef><section>{string.Concat(measures.Select((m, i) => $"""<measure n="{i + 1}">{m}</measure>"""))}</section></score></mdiv></body></music></mei>""";

    // A bar's upper staff: four quarter notes, each with its id and the sign typed under it (none for "").
    private static string Upper(string bar, params string[] signs) =>
        $"""<staff n="1"><layer n="1">{string.Concat(signs.Select((s, i) => $"""<note xml:id="{bar}{i + 1}" pname="{"cdef"[i]}" oct="5" dur="4">{Verse(s)}</note>"""))}</layer></staff>""";

    // A bar's lower staff: a whole note, with a sign under it or none.
    private static string Lower(string id, string sign = "") => $"""<staff n="2"><layer n="1"><note xml:id="{id}" pname="c" oct="3" dur="1">{Verse(sign)}</note></layer></staff>""";

    private static string Verse(string sign) => sign.Length == 0 ? "" : $"""<verse n="1"><syl>{System.Security.SecurityElement.Escape(sign)}</syl></verse>""";

    // The marks a piece has, in order: "dynam p a1", "hairpin cres a1-a4", "dir dolce b2".
    private static List<string> Marks(string mei) =>
        [.. XDocument.Parse(mei).Descendants().Where(e => e.Name.LocalName is "dynam" or "hairpin" or "dir").Select(e =>
            $"{e.Name.LocalName} {(e.Name.LocalName == "hairpin" ? (string?)e.Attribute("form") : e.Value)} {((string?)e.Attribute("startid"))?.TrimStart('#')}" +
            (e.Attribute("endid") is { } end ? $"-{((string)end).TrimStart('#')}" : ""))];

    [Fact]
    public void MusicWithoutLyricsComesBackAsItIs()
    {
        var mei = Piece(Upper("a", "", "", "", "") + Lower("b"));

        Assert.Same(mei, LyricDynamics.Mark(mei));
    }

    [Fact]
    public void ASongsWordsStayAsTheyAre()
    {
        // Signs among a song's words are the song's: "p" could be a syllable.
        var mei = Piece(Upper("a", "la", "p", "<", "f") + Lower("b"));

        Assert.Same(mei, LyricDynamics.Mark(mei));
    }

    [Fact]
    public void LettersAreDynamicsOnTheirNotesAndTheLyricsLineGoes()
    {
        // As the music font's letters too, and split over syllables ("(", p, ")"), as Verovio keeps an editor's "(p)".
        var mei = Piece(Upper("a", "pp", "", "\uE522\uE522", "") + Lower("b", "sf"),
            Upper("c", "", "", "", "").Replace("""<note xml:id="c1" pname="c" oct="5" dur="4">""", """<note xml:id="c1" pname="c" oct="5" dur="4"><verse n="1"><syl>(</syl><syl>""" + "\uE520" + """</syl><syl>)</syl></verse>""") + Lower("d"));

        var marked = LyricDynamics.Mark(mei);

        Assert.Equal(["dynam pp a1", "dynam ff a3", "dynam sf b", "dynam (p) c1"], Marks(marked));
        Assert.DoesNotContain("<verse", marked, StringComparison.Ordinal);
        Assert.All(XDocument.Parse(marked).Descendants(Mei + "dynam"), d => Assert.Equal("below", (string?)d.Attribute("place")));
    }

    [Fact]
    public void AStartWithItsNotesMarkedIsAHairpinToTheLastOfThem()
    {
        // A crescendo over bar 1 to its end in bar 2's first note; a diminuendo from bar 2's third note over the rest.
        var mei = Piece(Upper("a", "<", "(", "(", "(") + Lower("b"), Upper("c", "[", "", ">", ")") + Lower("d"), Upper("e", "p", "", "", "") + Lower("f"));

        Assert.Equal(["hairpin cres a1-c1", "hairpin dim c3-c4", "dynam p e1"], Marks(LyricDynamics.Mark(mei)));
    }

    [Fact]
    public void ADiminuendoMarkedRightAfterACrescendosEndStartsWhereItEnded()
    {
        // The Moonlight's left hand in bar 16: "<", "[", ")" (a swell, the diminuendo's start not typed), and in bar 18 "<", "[", "]".
        var mei = Piece(Upper("a", "", "<", "[", ")") + Lower("b"), Upper("c", "", "<", "[", "]") + Lower("d"));

        Assert.Equal(["hairpin cres a2-a3", "hairpin dim a3-a4", "hairpin cres c2-c3", "hairpin dim c3-c4"], Marks(LyricDynamics.Mark(mei)));
    }

    [Fact]
    public void AStartWithNothingToEndItIsTheWord()
    {
        // No notes under it marked, no end: a hairpin would have to guess where it stops.
        var mei = Piece(Upper("a", "<", "", "", "") + Lower("b"), Upper("c", ">", "", "f", "") + Lower("d"), Upper("e", "", "", "", ">") + Lower("f"));

        Assert.Equal(["dynam cresc. a1", "dynam dim. c1", "dynam f c3", "dynam dim. e4"], Marks(LyricDynamics.Mark(mei)));
    }

    [Fact]
    public void SeveralSignsOnANoteEndFirstThenPlayThenStart()
    {
        var mei = Piece(Upper("a", "<", "(", "[ >", ")") + Lower("b"), Upper("c", "] p", "", "sf >", ")") + Lower("d"), Upper("e", "]", "", "", "") + Lower("f"));

        Assert.Equal(["hairpin cres a1-a3", "hairpin dim a3-c1", "dynam p c1", "dynam sf c3", "hairpin dim c3-e1"], Marks(LyricDynamics.Mark(mei)));
    }

    [Fact]
    public void DolceIsADirectionAndAPedalsStarAndStraySignsAreLeftOut()
    {
        // A ")" and a "]" with nothing to go on or end, a pedal's release "*".
        var mei = Piece(Upper("a", "dolce", ")", "", "*") + Lower("b", "]"));

        var marked = LyricDynamics.Mark(mei);

        Assert.Equal(["dir dolce a1"], Marks(marked));
        Assert.Equal("italic", (string?)XDocument.Parse(marked).Descendants(Mei + "rend").Single().Attribute("fontstyle"));
    }

    [Fact]
    public void SignsTheScoreMarksAlreadyAreLeftOut()
    {
        // The score's own p (under the lower staff) at bar 1's start, and its crescendo from bar 2's third beat: the signs typed at
        // the same moment and two beats before are their copies. The f on beat 3, the score's p nearest it, is a dynamic of its
        // own; so is bar 3's p, the score's f nearest it.
        var mei = Piece(
            Upper("a", "p", "", "f", "") + Lower("b") + """<dynam staff="2" tstamp="1">p</dynam>""",
            Upper("c", "<", "", "", "") + Lower("d") + """<hairpin staff="1" tstamp="3" tstamp2="0m+4" form="cres"/><dynam staff="1" tstamp="4">f</dynam>""",
            Upper("e", "p", "", "", "") + Lower("f"));

        Assert.Equal(["dynam p ", "dynam f a3", "hairpin cres ", "dynam f ", "dynam p e1"], Marks(LyricDynamics.Mark(mei)));
    }

    [Fact]
    public void AnotherDynamicOfTheScoresAtTheSameMomentStands()
    {
        var mei = Piece(Upper("a", "f", "", "", "") + Lower("b") + """<dynam staff="1" tstamp="1">p</dynam>""");

        Assert.Equal(["dynam p "], Marks(LyricDynamics.Mark(mei)));
    }

    [Fact]
    public void EachOfTheScoresMarksIsTheCopyOfOneSignTheNearest()
    {
        // The score's sf on beat 1, typed there under both staves; the sf on beat 2 is an accent of its own.
        var mei = Piece(Upper("a", "sf", "sf", "", "") + Lower("b", "sf") + """<dynam staff="1" tstamp="1">sf</dynam>""");

        Assert.Equal(["dynam sf ", "dynam sf a2"], Marks(LyricDynamics.Mark(mei)));
    }

    [Fact]
    public void ChordsKeepTheirSignsAndPrimaReadsTheMarks()
    {
        // A chord's sign is on the chord; the marks are the app's to judge playing by, as any score's are.
        var chord = """<staff n="1"><layer n="1"><chord xml:id="k" dur="2"><note xml:id="k1" pname="c" oct="5"><verse n="1"><syl>pp</syl></verse></note><note xml:id="k2" pname="e" oct="5"/></chord><note xml:id="n" pname="g" oct="5" dur="4"><verse n="1"><syl>&lt;</syl></verse></note><note xml:id="o" pname="a" oct="5" dur="4"><verse n="1"><syl>[</syl></verse></note></layer></staff>""";
        var marked = LyricDynamics.Mark(Piece(chord + Lower("b")));

        Assert.Equal(["dynam pp k", "hairpin cres n-o"], Marks(marked));
        var marks = MeiReader.Read(marked).FileMarks;
        Assert.Contains(marks, m => m.Kind == MarkKind.Dynamic);
        Assert.Contains(marks, m => m.Kind == MarkKind.Crescendo && m.EndId == "o");
    }

    [Fact]
    public void MarkingAgainChangesNothing()
    {
        var once = LyricDynamics.Mark(Piece(Upper("a", "pp", "<", "(", "[") + Lower("b")));

        Assert.Same(once, LyricDynamics.Mark(once));
        Assert.Equal(["dynam pp a1", "hairpin cres a2-a4"], Marks(once));
    }
}
