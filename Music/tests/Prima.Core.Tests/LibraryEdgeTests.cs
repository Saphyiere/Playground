using Prima.Core.Library;
using Prima.Core.Music;

namespace Prima.Core.Tests;

/// <summary>
/// From the review of main at a7ab94d: a piece's opening, and a score with a drum part. Each fails on that code; the place in it
/// is given with each.
/// </summary>
public class LibraryEdgeTests
{
    // MusicTests' score with a drum part: two bars' worth of piano in 2/4, and a drum line with a quaver rest in it.
    private const string Piano = """
        <mei xmlns="http://www.music-encoding.org/ns/mei"><music><body><mdiv><score>
        <scoreDef meter.count="2" meter.unit="4" key.sig="0"><staffGrp>
        <staffGrp><staffDef n="1" lines="5" clef.shape="G" clef.line="2"/><staffDef n="2" lines="5" clef.shape="F" clef.line="4"/></staffGrp>
        DRUMDEF
        </staffGrp></scoreDef>
        <section>
        <measure n="1">
        <staff n="1"><layer n="1"><note xml:id="a" pname="e" oct="5" dur="4"/><note xml:id="b" pname="d" oct="5" dur="4"/></layer></staff>
        <staff n="2"><layer n="1"><note xml:id="c" pname="c" oct="3" dur="2"/></layer></staff>
        DRUMS
        </measure>
        </section></score></mdiv></body></music></mei>
        """;

    private const string DrumDef = """<staffDef n="3" lines="5" clef.shape="perc"/>""";

    private const string Drums = """
        <staff n="3"><layer n="1">
        <note xml:id="d" loc="5" dur="8" stem.dir="up"/><rest xml:id="e" dur="8"/><note xml:id="f" loc="5" dur="8" grace="acc"/>
        <chord xml:id="g" dur="4"><note xml:id="g1" loc="1"/><note xml:id="g2" loc="9" head.shape="x"/></chord>
        </layer></staff>
        """;

    private static string Mei(bool drums, string more = "") => Piano.Replace("DRUMDEF", drums ? DrumDef : "").Replace("DRUMS", (drums ? Drums : "") + more);

    // Opening.cs:19-30 times each note from Score.Expected(), which counts from the first full bar's downbeat, so a pickup's
    // notes come before 0 (Score.cs:172-185). Openings.cs:105-115 sends each note that long after it starts: the upbeat's are
    // sent in the past, all at once with their ends, and the piano begins on the downbeat. concept.md: "about half a minute
    // from the start".
    [Fact]
    public void AnOpeningStartsWithThePiecesUpbeat()
    {
        Notes Crotchet(string id, int at) => new(id, at * Ticks.Quarter, Ticks.Quarter, [new Tone("t" + id, new Spelled(Step.C, 0, 4))]);
        List<Bar> bars =
        [
            new Bar(0, [new Voice(1, [Crotchet("up", 0)])]) { Length = Ticks.Quarter, Tempos = [(0, 120.0)] },
            .. Enumerable.Range(1, 2).Select(b => new Bar(b, [new Voice(1, [.. Enumerable.Range(0, 4).Select(q => Crotchet($"n{b}-{q}", q))])])),
        ];

        var opening = Opening.Of(new Score(new Key(0), new Meter(4, 4), bars));

        Assert.Equal(9, opening.Count);
        Assert.Equal(0, opening[0].Start, 3);
        Assert.Equal(0.5, opening[1].Start, 3);
    }

    // MeiReader.cs:259-263 (68c5e53) keeps a drum's notes as time that isn't seen, but Grader.cs:47-48 still grades the drum
    // line's own rests, and any tuplet on its unseen ones, as if the pianist read them. concept.md: a part with no pitches "has
    // no keys to play: its notes only keep their time".
    [Fact]
    public void ADrumPartDoesNotChangeThePiecesLevel()
    {
        var alone = Grader.Grade(MeiReader.Read(Mei(drums: false)));
        var with = Grader.Grade(MeiReader.Read(Mei(drums: true)));

        Assert.DoesNotContain("eighth rests", with.Reasons);
        Assert.Equal(alone.Level, with.Level);
    }

    // MeiReader.cs:149-158 (Place) puts a mark at a beat on its own staff's notes, or with none there on any staff's. A drum
    // line never has notes now, so its loud and soft lands on the piano's, where the pianist is judged against it.
    [Fact]
    public void ADrumPartsLoudAndSoftIsNotPinnedToThePianosNotes()
    {
        var read = MeiReader.Read(Mei(drums: true, """<dynam xml:id="dy" staff="3" tstamp="1">ff</dynam>"""));

        Assert.DoesNotContain(read.FileMarks, m => m.Kind == MarkKind.Dynamic);
    }
}
