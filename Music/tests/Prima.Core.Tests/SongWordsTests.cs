using System.IO.Compression;
using System.Xml.Linq;
using Prima.Core.Music;

namespace Prima.Core.Tests;

public class SongWordsTests
{
    private static readonly XNamespace Mei = "http://www.music-encoding.org/ns/mei";

    // A grand staff in 4/4, its bars given: each a <measure> of staves (and the directions after them).
    private static string Piece(params string[] measures) =>
        $"""<mei xmlns="http://www.music-encoding.org/ns/mei" meiversion="5.0"><music><body><mdiv><score><scoreDef meter.count="4" meter.unit="4"><staffGrp symbol="brace"><staffDef n="1" lines="5" clef.shape="G" clef.line="2"/><staffDef n="2" lines="5" clef.shape="F" clef.line="4"/></staffGrp></scoreDef><section>{string.Concat(measures.Select((m, i) => $"""<measure n="{i + 1}">{m}</measure>"""))}</section></score></mdiv></body></music></mei>""";

    private static string Bass => """<staff n="2"><layer n="1"><note pname="c" oct="3" dur="1"/></layer></staff>""";

    // The words under each note or chord that has some, in the music's order: its id, and "n:text" for each of its verses.
    private static List<(string Id, string Words)> Lines(string mei) =>
        [.. XDocument.Parse(mei).Descendants().Where(e => e.Name.LocalName is "note" or "chord" && e.Elements(Mei + "verse").Any())
            .Select(e => ((string?)e.Attribute(XNamespace.Xml + "id") ?? "", string.Join(" ", e.Elements(Mei + "verse").Select(v => $"{(string?)v.Attribute("n")}:{v.Value.Trim()}"))))];

    [Fact]
    public void MusicWithoutWordsComesBackAsItIs()
    {
        var mei = Piece($"""<staff n="1"><layer n="1"><note pname="e" oct="5" dur="1"/></layer></staff>{Bass}<dir staff="1" tstamp="1">dolce</dir>""");

        Assert.False(SongWords.Has(mei));
        Assert.Same(mei, SongWords.Prepare(mei, shown: true));
        Assert.Same(mei, SongWords.Prepare(mei, shown: false));
    }

    [Fact]
    public void WhatIsntMeiComesBackAsItIs()
    {
        const string broken = "<mei><note><verse><syl>la</syl></note>";

        Assert.Same(broken, SongWords.Prepare(broken, shown: true));
        Assert.Same(broken, SongWords.Prepare(broken, shown: false));
    }

    [Fact]
    public void EachVoicesWordsAndEachVerseGetALineOfTheirOwn()
    {
        // Verovio's MEI from a MusicXML file whose verses an exporter named "part1verse1" and "part1verse2" (both read as verse 1),
        // with a second voice's words numbered 1 as well, and a chord with its syllables.
        var mei = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "verovio-lyrics.mei"));
        Assert.True(SongWords.Has(mei));

        var lines = Lines(SongWords.Prepare(mei, shown: true)).Select(l => l.Words);

        Assert.Equal(["1:Morn 2:Night", "1:ing 2:falls", "1:comes 2:so", "1:slow 2:soon", "3:low", "3:words", "1:home 2:again", "3:now"], lines);
    }

    [Fact]
    public void AnotherStavesWordsKeepTheirOwnLines()
    {
        var mei = Piece($"""<staff n="1"><layer n="1"><note xml:id="a" pname="e" oct="5" dur="1"><verse n="1"><syl>high</syl></verse></note></layer></staff><staff n="2"><layer n="1"><note xml:id="b" pname="c" oct="3" dur="1"><verse n="1"><syl>low</syl></verse></note></layer></staff>""");

        Assert.Equal([("a", "1:high"), ("b", "1:low")], Lines(SongWords.Prepare(mei, shown: true)));
    }

    [Fact]
    public void TheSameWordsInTwoVoicesOnAStaffPrintOnce()
    {
        // A hymn's soprano and alto on the upper staff, two verses each: the same words in bar 1, note for note, and in bar 2 in
        // the alto's own rhythm; in bar 3 the alto sings bar 2's words again, a bar after the soprano, which are its own.
        static string Sung(string id, string pname, string dur, string first, string second) =>
            $"""<note xml:id="{id}" pname="{pname}" oct="4" dur="{dur}"><verse n="1"><syl>{first}</syl></verse><verse n="2"><syl>{second}</syl></verse></note>""";
        var mei = Piece(
            $"""<staff n="1"><layer n="1">{Sung("s1", "g", "2", "Praise", "Praise")}{Sung("s2", "a", "2", "God", "him")}</layer><layer n="2">{Sung("a1", "e", "2", "Praise", "Praise")}{Sung("a2", "f", "2", "God", "him")}</layer></staff>{Bass}""",
            $"""<staff n="1"><layer n="1">{Sung("s3", "b", "4", "from", "all")}{Sung("s4", "a", "4", "whom", "crea")}{Sung("s5", "g", "2", "all", "tures")}</layer><layer n="2">{Sung("a3", "d", "2", "from", "all")}{Sung("a4", "e", "4", "whom", "crea")}{Sung("a5", "d", "4", "all", "tures")}</layer></staff>{Bass}""",
            $"""<staff n="1"><layer n="1">{Sung("s6", "g", "1", "bless", "here")}</layer><layer n="2">{Sung("a6", "d", "4", "from", "all")}{Sung("a7", "e", "4", "whom", "crea")}{Sung("a8", "d", "2", "all", "tures")}</layer></staff>{Bass}""");

        var prepared = SongWords.Prepare(mei, shown: true);

        Assert.Equal(
        [
            ("s1", "1:Praise 2:Praise"), ("s2", "1:God 2:him"),
            ("s3", "1:from 2:all"), ("s4", "1:whom 2:crea"), ("s5", "1:all 2:tures"),
            ("s6", "1:bless 2:here"), ("a6", "3:from 4:all"), ("a7", "3:whom 4:crea"), ("a8", "3:all 4:tures"),
        ], Lines(prepared));
        Assert.Equal(XDocument.Parse(mei).Descendants(Mei + "note").Count(), XDocument.Parse(prepared).Descendants(Mei + "note").Count());
    }

    [Fact]
    public void HiddenWordsAreLeftOffAndTheMusicStays()
    {
        var mei = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "verovio-lyrics.mei")).Replace("</measure>", """<dir staff="1" tstamp="1">dolce</dir></measure>""");

        var hidden = XDocument.Parse(SongWords.Prepare(mei, shown: false));

        Assert.Empty(hidden.Descendants(Mei + "verse"));
        Assert.Empty(hidden.Descendants(Mei + "syl"));
        Assert.Equal(XDocument.Parse(mei).Descendants(Mei + "note").Count(), hidden.Descendants(Mei + "note").Count());
        Assert.Equal(["dolce", "dolce"], hidden.Descendants(Mei + "dir").Select(d => d.Value));
    }

    [Fact]
    public void AWholeVerseTypedAsTextIsLeftOffAndAPlayersDirectionsStay()
    {
        const string verse = "1. So oft ich meine Tabakspfeife, mit gutem Knaster angefüllt, zur Lust und Zeitvertreib ergreife";
        var mei = Piece($"""<staff n="1"><layer n="1"><note pname="e" oct="5" dur="1"/></layer></staff>{Bass}<dir staff="1" tstamp="1">poco a poco cresc.</dir><dir staff="1" tstamp="4">{verse}</dir>""");

        foreach (var shown in new[] { true, false })
        {
            var directions = XDocument.Parse(SongWords.Prepare(mei, shown)).Descendants(Mei + "dir").Select(d => d.Value).ToList();
            Assert.Equal(["poco a poco cresc."], directions);
        }
        Assert.False(SongWords.Has(mei));
    }

    [Fact]
    public void LinesTypedAsTextGoUnderTheNotesTheyStartAt()
    {
        // Four bars, a line of words in each, by the beat: on beat 1, on the second half of beat 2 (the fourth eighth, in a beam),
        // on the triplet's third note, and one on the bass staff; and a line the file ties to its note by id.
        var mei = Piece(
            $"""<staff n="1"><layer n="1"><note xml:id="a1" pname="e" oct="5" dur="2"/><note xml:id="a2" pname="d" oct="5" dur="2"/></layer></staff>{Bass}<dir staff="1" place="below" tstamp="1">every word I say</dir>""",
            $"""<staff n="1"><layer n="1"><beam><note xml:id="b1" pname="e" oct="5" dur="8"/><note xml:id="b2" pname="d" oct="5" dur="8"/><note xml:id="b3" pname="c" oct="5" dur="8"/><note xml:id="b4" pname="d" oct="5" dur="8"/></beam><note xml:id="b5" pname="e" oct="5" dur="2"/></layer></staff>{Bass}<dir staff="1" place="below" tstamp="2.5">comes back to me</dir>""",
            $"""<staff n="1"><layer n="1"><tuplet num="3" numbase="2"><note xml:id="c1" pname="e" oct="5" dur="4"/><note xml:id="c2" pname="d" oct="5" dur="4"/><note xml:id="c3" pname="c" oct="5" dur="4"/></tuplet><note xml:id="c4" pname="d" oct="5" dur="2"/></layer></staff>{Bass}<dir staff="1" place="above" tstamp="2.33">and then away</dir>""",
            $"""<staff n="1"><layer n="1"><note xml:id="d1" pname="e" oct="5" dur="1"/></layer></staff><staff n="2"><layer n="1"><note xml:id="d2" pname="c" oct="3" dur="2"/><note xml:id="d3" pname="g" oct="2" dur="2"/></layer></staff><dir staff="2" place="below" tstamp="3">down below the line</dir><dir staff="1" startid="#d1">over and over again</dir>""");
        Assert.True(SongWords.Has(mei));

        var shown = SongWords.Prepare(mei, shown: true);

        Assert.Equal(
        [
            ("a1", "1:every word I say"),
            ("b4", "1:comes back to me"),
            ("c3", "1:and then away"),
            ("d1", "1:over and over again"),
            ("d3", "1:down below the line"),
        ], Lines(shown));
        Assert.Empty(XDocument.Parse(shown).Descendants(Mei + "dir"));
        Assert.Empty(XDocument.Parse(SongWords.Prepare(mei, shown: false)).Descendants(Mei + "dir"));
    }

    [Fact]
    public void WordsTypedAsTextGoOnALineUnderTheLyrics()
    {
        string Bar(int i) => $"""<staff n="1"><layer n="1"><note xml:id="n{i}" pname="e" oct="5" dur="1"><verse n="1"><syl>la</syl></verse></note></layer></staff>{Bass}<dir staff="1" tstamp="1">the words of line {i}</dir>""";

        var lines = Lines(SongWords.Prepare(Piece(Bar(1), Bar(2), Bar(3), Bar(4)), shown: true));

        Assert.Equal(("n3", "1:la 2:the words of line 3"), lines[2]);
    }

    [Fact]
    public void APlayersFewDirectionsAreNotTakenForWords()
    {
        // Three directions of a few words over eight bars, as a piece's tempo and character words are (the dashes in "ri - tar - dan
        // - do" aren't words), and one of two words.
        var bar = $"""<staff n="1"><layer n="1"><note pname="e" oct="5" dur="1"/></layer></staff>{Bass}""";
        var mei = Piece(
            bar + """<dir staff="1" tstamp="1">Allegro ma non troppo</dir>""", bar, bar + """<dir staff="1" tstamp="1">poco a poco cresc.</dir>""", bar,
            bar + """<dir staff="1" tstamp="1">ri - tar - dan - do</dir>""", bar, bar + """<dir staff="1" tstamp="1">a tempo</dir>""", bar);

        Assert.False(SongWords.Has(mei));
        Assert.Same(mei, SongWords.Prepare(mei, shown: true));
    }

    [Fact]
    public void ADirectionMadeOfMusicsOwnWordsIsNeverALineOfASong()
    {
        // The owner's four, one a bar, as many directions of three words or more as a song's lines would be; and the ritardando
        // of Beethoven's last sonata, spaced out over the bars it slows, as long as a verse.
        string[] players =
        [
            "D.C. al Fine", "poco a poco cresc.", "Allegro ma non troppo", "dim. e rit.",
            "ritar - - - - - - - - - - - - - - - - - - - - - - - dan - - - - - - - - - - - - - - - - do",
        ];
        string Bar(int i, string direction) => $"""<staff n="1"><layer n="1"><note xml:id="n{i}" pname="e" oct="5" dur="1"/></layer></staff>{Bass}<dir staff="1" tstamp="1">{direction}</dir>""";

        // They don't make a piece a song, and it comes back as it is.
        var piece = Piece([.. players.Select((p, i) => Bar(i + 1, p))]);
        Assert.False(SongWords.Has(piece));
        Assert.Same(piece, SongWords.Prepare(piece, shown: true));
        Assert.Same(piece, SongWords.Prepare(piece, shown: false));

        // In a song, its lines typed as text in the same bars, they aren't moved under the notes, and they stay with Lyrics off.
        var song = Piece([.. players.Select((p, i) => Bar(i + 1, p) + $"""<dir staff="1" tstamp="1">the words of line {i + 1}</dir>""")]);
        Assert.True(SongWords.Has(song));
        var shown = SongWords.Prepare(song, shown: true);
        Assert.Equal([.. players.Select((_, i) => ($"n{i + 1}", $"1:the words of line {i + 1}"))], Lines(shown));
        Assert.Equal(players, XDocument.Parse(shown).Descendants(Mei + "dir").Select(d => d.Value));
        Assert.Equal(players, XDocument.Parse(SongWords.Prepare(song, shown: false)).Descendants(Mei + "dir").Select(d => d.Value));
    }

    [Fact]
    public void TheBundlesDirectionsAreNeverTakenForWords()
    {
        // Of the bundled pieces, only the one with lyrics (Karussell-Walzer) has words: no piece's directions (Satie's advice, Italian
        // tempo words, a verse after the music) are taken for lines of a song.
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "Directory.Build.props"))) root = root.Parent;
        var pieces = Path.Combine(root!.FullName, "Music", "library", "pieces");

        var withWords = new List<string>();
        foreach (var file in Directory.EnumerateFiles(pieces, "*.mei.gz"))
        {
            using var reader = new StreamReader(new GZipStream(File.OpenRead(file), CompressionMode.Decompress));
            var mei = reader.ReadToEnd();
            if (SongWords.Has(mei)) withWords.Add(XDocument.Parse(mei).Descendants(Mei + "title").First().Value.Trim());
        }

        Assert.Equal(["Karussell-Walzer"], withWords);
    }
}
