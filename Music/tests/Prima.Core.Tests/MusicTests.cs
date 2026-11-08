using Prima.Core.Music;

namespace Prima.Core.Tests;

public class MusicTests
{
    private static Spelled P(Step step, int octave, int alter = 0) => new(step, alter, octave);

    [Theory]
    [InlineData(2, false, 66, "F♯4")]
    [InlineData(-1, false, 70, "B♭4")]
    [InlineData(0, true, 68, "G♯4")]
    [InlineData(0, false, 61, "C♯4")]
    [InlineData(-3, false, 61, "D♭4")]
    [InlineData(-1, false, 71, "B4")]
    [InlineData(7, false, 60, "B♯3")]
    [InlineData(-7, false, 59, "C♭4")]
    public void KeysAreSpelledTheWayTheKeyWritesThem(int fifths, bool minor, int midi, string written)
    {
        var spelled = new Key(fifths, minor).Spell(midi);

        Assert.Equal(written, spelled.ToString());
        Assert.Equal(midi, spelled.Midi);
    }

    [Fact]
    public void KeysKnowTheirTonicAndSignature()
    {
        Assert.Equal("G major", new Key(1).Name);
        Assert.Equal("E minor", new Key(1, true).Name);
        Assert.Equal("E♭ major", new Key(-3).Name);
        Assert.Equal("C minor", new Key(-3, true).Name);
        Assert.Equal("F♯ minor", new Key(3, true).Name);
        Assert.Equal(1, new Key(2).AlterOf(Step.C));
        Assert.Equal(0, new Key(2).AlterOf(Step.G));
        Assert.Equal(P(Step.D, 5), new Key(1).Degree(4, 4));
        Assert.Equal(P(Step.F, 4, 1), new Key(1).Degree(6, 3));
        Assert.Equal(9, new Key(0, true).TonicPitchClass);
    }

    [Fact]
    public void MetresCountTheirBeats()
    {
        Assert.Equal((2, 720), (new Meter(6, 8).Beats, new Meter(6, 8).BeatTicks));
        Assert.Equal((3, 1440), (new Meter(3, 4).Beats, new Meter(3, 4).BarTicks));
        Assert.Equal((2, 960), (new Meter(2, 2).Beats, new Meter(2, 2).BeatTicks));
        Assert.Equal((5, 240), (new Meter(5, 8).Beats, new Meter(5, 8).BeatTicks));
    }

    [Fact]
    public void MiddleCAndTheStaffPositionsAgree()
    {
        Assert.Equal(60, P(Step.C, 4).Midi);
        Assert.Equal(2, P(Step.E, 4).Diatonic - P(Step.C, 4).Diatonic);
        Assert.Equal(P(Step.B, 3), Spelled.FromDiatonic(P(Step.C, 4).Diatonic - 1));
    }

    // A pickup, then two bars in G: a chord, a triplet, a tie across the bar, an accidental, and a change to D major.
    private static Score Sample()
    {
        var triplet = new Tuplet(3, 2);
        var pickup = new Bar(0, [new Voice(1, [new Notes("p1", 0, Ticks.Quarter, [new Tone("p1t", P(Step.D, 4))])])]) { Length = Ticks.Quarter };
        var bar1 = new Bar(1,
        [
            new Voice(1,
            [
                new Notes("a", 0, Ticks.Quarter, [new Tone("a1", P(Step.G, 4))]),
                new Notes("b", 480, 160, [new Tone("b1", P(Step.A, 4))]) { Tuplet = triplet },
                new Notes("c", 640, 160, [new Tone("c1", P(Step.B, 4))]) { Tuplet = triplet },
                new Notes("d", 800, 160, [new Tone("d1", P(Step.C, 5, 1))]) { Tuplet = triplet },
                new Notes("e", 960, Ticks.Half, [new Tone("e1", P(Step.D, 5)) { TieToNext = true }]),
            ]),
            new Voice(2, [new Notes("f", 0, Ticks.Whole, [new Tone("f1", P(Step.G, 2)), new Tone("f2", P(Step.D, 3)), new Tone("f3", P(Step.B, 3))])]),
        ]);
        var bar2 = new Bar(2,
        [
            new Voice(1,
            [
                new Notes("g", 0, Ticks.Quarter, [new Tone("g1", P(Step.D, 5)) { TiedFromPrevious = true }]),
                new Notes("h", 480, Ticks.Eighth + Ticks.Quarter, [new Tone("h1", P(Step.C, 5, 1))]),
                new Notes("i", 1200, Ticks.Eighth, [new Tone("i1", P(Step.E, 5))]),
                new Rest("j", 1440, Ticks.Half),
            ]),
            new Voice(2, [new Rest("k", 0, Ticks.Whole)]),
        ]) { NewKey = new Key(2) };
        return new Score(new Key(1), new Meter(4, 4), [pickup, bar1, bar2]) { Title = "Sample", Marks = [new Mark(MarkKind.Dynamic, "a1", Text: "p"), new Mark(MarkKind.Slur, "a1", "e1")] };
    }

    [Fact]
    public void ExpectedNotesAreTheOnsetsFromTheFirstFullBar()
    {
        var expected = Sample().Expected();

        Assert.Equal(["p1t", "f1", "f2", "f3", "a1", "b1", "c1", "d1", "e1", "h1", "i1"], expected.Select(n => n.Id));
        Assert.Equal(-480, expected[0].Tick);
        Assert.Equal(480 + 160, expected.Single(n => n.Id == "c1").Tick);
        Assert.Equal(Ticks.Half + Ticks.Quarter, expected.Single(n => n.Id == "e1").Length);
        Assert.Equal(Ticks.Whole + 480, expected.Single(n => n.Id == "h1").Tick);
        Assert.Equal("f", expected.Single(n => n.Id == "f2").ChordId);
        Assert.Equal(2, expected.Single(n => n.Id == "f1").Staff);
    }

    [Fact]
    public void AnExcerptStandsOnItsOwn()
    {
        var sample = Sample();
        var withClef = sample with
        {
            Bars = [sample.Bars[0], sample.Bars[1] with { Voices = [sample.Bars[1].Voices[0], sample.Bars[1].Voices[1] with { Events = [new ClefChange(0, Clef.Treble), .. sample.Bars[1].Voices[1].Events] }] }, sample.Bars[2]],
            Marks = [.. sample.Marks, new Mark(MarkKind.Slur, "d1", "h1")],
        };

        var bar2 = withClef.Excerpt(2, 2);

        // The key in force at bar 2, still numbered 2; the note tied from bar 1 is played; the bass staff is still in treble.
        Assert.Equal(new Key(2), bar2.Key);
        Assert.Equal(2, Assert.Single(bar2.Bars).Number);
        Assert.Null(bar2.Bars[0].NewKey);
        Assert.Equal(["g1", "h1", "i1"], bar2.Expected().Select(n => n.Id));
        Assert.Equal(0, bar2.Expected()[0].Tick);
        Assert.Equal(new ClefChange(0, Clef.Treble), bar2.Bars[0].Voices[1].Events[0]);
        // The slur from bar 1 is cut at bar 2's first note; the marks wholly in bar 1 are gone.
        Assert.Equal(new Mark(MarkKind.Slur, "g1", "h1"), Assert.Single(bar2.Marks));
        Assert.Contains("startid=\"#g1\" endid=\"#h1\"", MeiWriter.Write(bar2));

        var bar1 = withClef.Excerpt(1, 1);

        // The tie out of bar 1 is dropped, so its last note sounds only as long as it's written.
        Assert.Equal(Ticks.Half, bar1.Expected().Single(n => n.Id == "e1").Length);
        Assert.Equal(new Key(1), bar1.Key);
        Assert.Equal(3, bar1.Marks.Count);
        Assert.Equal("e1", bar1.Marks[2].EndId);

        // A pickup stays bar 0.
        Assert.Equal([0, 1], withClef.Excerpt(0, 1).Bars.Select(b => b.Number));
    }

    [Fact]
    public void MeiWritesWhatAScoreShows()
    {
        var mei = MeiWriter.Write(Sample(), 90);

        Assert.Contains("key.sig=\"1s\"", mei);
        Assert.Contains("<staffGrp symbol=\"brace\" bar.thru=\"true\">", mei);
        Assert.Contains("metcon=\"false\"", mei);
        Assert.Contains("<tuplet num=\"3\" numbase=\"2\"><beam>", mei);
        Assert.Contains("xml:id=\"d1\" pname=\"c\" oct=\"5\" dur=\"8\" accid=\"s\"", mei);
        // D major's signature has the C sharp, so bar 2's C sharp isn't printed again.
        Assert.Contains("xml:id=\"h1\" pname=\"c\" oct=\"5\" dur=\"4\" dots=\"1\" accid.ges=\"s\"", mei);
        Assert.Contains("tie=\"i\"", mei);
        Assert.Contains("<mRest xml:id=\"k\"", mei);
        Assert.Contains("<dynam staff=\"1\" startid=\"#a1\" place=\"below\">p</dynam>", mei);
        Assert.Contains("right=\"end\"", mei);
    }

    [Fact]
    public void NotesWithoutAMetreHaveNoBarlinesOrNumbers()
    {
        var mei = MeiWriter.Write(Sample() with { ShowMeter = false });

        Assert.DoesNotContain("meter.count", mei);
        Assert.Contains("mnum.visible=\"false\"", mei);
        Assert.Equal(3, mei.Split("right=\"invis\"").Length - 1);
        Assert.DoesNotContain("right=\"end\"", mei);
    }

    [Fact]
    public void StaccatoNotesAndAShortNoteEndingASlurAreLifted()
    {
        Notes N(string id, int start, int length, Step step) => new(id, start, length, [new Tone($"{id}1", P(step, 4))]);
        var bar1 = new Bar(1, [new Voice(1, [N("a", 0, Ticks.Quarter, Step.C), N("b", Ticks.Quarter, Ticks.Quarter, Step.D), N("c", Ticks.Half, Ticks.Half, Step.E)])]);
        var bar2 = new Bar(2, [new Voice(1, [N("d", 0, Ticks.Quarter, Step.F) with { Articulation = Articulation.Staccato }, N("e", Ticks.Quarter, Ticks.Quarter, Step.G), N("f", Ticks.Half, Ticks.Half, Step.A)])]);
        var score = new Score(new Key(0), new Meter(4, 4), [bar1, bar2]) { Marks = [new Mark(MarkKind.Slur, "a1", "b1"), new Mark(MarkKind.Slur, "e1", "f1")] };

        // b ends a slur as a quarter, d is staccato; f ends one as a half note, held to its end.
        Assert.Equal([false, true, false, true, false, false], score.Expected().Select(n => n.Lifted));
    }

    [Fact]
    public void APiecesArticulationsSlurEndsAndOrnamentsAreReadOntoItsNotes()
    {
        var mei = """
            <mei xmlns="http://www.music-encoding.org/ns/mei"><music><body><mdiv><score><scoreDef><staffGrp><staffDef n="1" lines="5" clef.shape="G" clef.line="2" meter.count="4" meter.unit="4"/></staffGrp></scoreDef>
            <section><measure n="1"><staff n="1"><layer n="1">
            <note xml:id="a" pname="c" oct="4" dur="4"><artic artic="stacciss"/></note><note xml:id="b" pname="d" oct="4" dur="4" artic="acc stacc"/>
            <note xml:id="c" pname="e" oct="4" dur="4" artic="ten"/><note xml:id="d" pname="f" oct="4" dur="4"/>
            </layer></staff><slur startid="#c" endid="#d"/><trill startid="#c"/></measure></section></score></mdiv></body></music></mei>
            """;

        var read = MeiReader.Read(mei);
        var notes = read.Bars[0].Voices[0].Events.OfType<Notes>().ToList();

        Assert.Equal([Articulation.Staccato, Articulation.Staccato, Articulation.Tenuto, Articulation.None], notes.Select(n => n.Articulation));
        Assert.Equal([false, false, true, false], notes.Select(n => n.Ornamented));
        Assert.Equal([true, true, false, true], read.Expected().Select(n => n.Lifted));
        // They're kept on the notes for judging a run, not as the score's marks.
        Assert.Empty(read.Marks);
    }

    [Fact]
    public void MeiReadsBackWhatItWrote()
    {
        var score = Sample();

        var read = MeiReader.Read(MeiWriter.Write(score));

        Assert.Equal(score.Expected(), read.Expected());
        Assert.Equal(new Key(1), read.Key);
        Assert.Equal(new Key(2), read.Bars[2].NewKey);
        Assert.Equal(Ticks.Quarter, read.Bars[0].Length);
        Assert.Equal("Sample", read.Title);
    }

    [Fact]
    public void MeiFromVerovioIsRead()
    {
        // Verovio's MEI from a MusicXML file: D major in 3/4, a triplet, a chord, a tie into bar 2, and a natural.
        var read = MeiReader.Read(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "verovio-from-musicxml.mei")));

        var expected = read.Expected();
        Assert.Equal(new Meter(3, 4), read.Meter);
        Assert.Equal(new Key(2), read.Key);
        Assert.Equal([(0L, 50), (0, 57), (0, 74), (480, 76), (640, 78), (800, 79), (960, 78), (1440, 50), (1920, 72), (2400, 71)], expected.Select(n => (n.Tick, n.Midi)));
        Assert.Equal(Ticks.Half, expected.Single(n => n.Tick == 960).Length);
        Assert.Equal("Fixture", read.Title);
    }

    // Two bars repeated from the start, then a repeat with first and second endings, and a bar after: 2/4, a half note a bar.
    private const string Repeats = """
        <mei xmlns="http://www.music-encoding.org/ns/mei"><music><body><mdiv><score>
        <scoreDef meter.count="2" meter.unit="4" key.sig="0"><staffGrp><staffDef n="1" lines="5" clef.shape="G" clef.line="2"/></staffGrp></scoreDef>
        <section>
        <measure n="1"><staff n="1"><layer n="1"><note xml:id="a" pname="c" oct="4" dur="2"/></layer></staff></measure>
        <measure n="2" right="rptend"><staff n="1"><layer n="1"><note xml:id="b" pname="d" oct="4" dur="2"/></layer></staff></measure>
        <measure n="3" left="rptstart"><staff n="1"><layer n="1"><note xml:id="c" pname="e" oct="4" dur="2"/></layer></staff></measure>
        <ending n="1"><measure n="4" right="rptend"><staff n="1"><layer n="1"><note xml:id="d" pname="f" oct="4" dur="2"/></layer></staff></measure></ending>
        <ending n="2"><measure n="5"><staff n="1"><layer n="1"><note xml:id="e" pname="g" oct="4" dur="2"/></layer></staff></measure></ending>
        <measure n="6"><staff n="1"><layer n="1"><note xml:id="f" pname="a" oct="4" dur="2"/></layer></staff></measure>
        </section></score></mdiv></body></music></mei>
        """;

    [Fact]
    public void Reading_keeps_repeat_signs_and_endings()
    {
        var score = MeiReader.Read(Repeats);

        Assert.Equal([false, false, true, false, false, false], score.Bars.Select(b => b.RepeatStart));
        Assert.Equal([false, true, false, true, false, false], score.Bars.Select(b => b.RepeatEnd));
        Assert.Equal(["", "", "", "1", "2", ""], score.Bars.Select(b => string.Join(",", b.Endings)));
    }

    [Theory]
    [InlineData(0, new[] { 0, 1, 0, 1, 2, 3, 2, 4, 5 })]
    [InlineData(1, new[] { 1, 0, 1, 2, 3, 2, 4, 5 })]
    [InlineData(2, new[] { 2, 3, 2, 4, 5 })]
    [InlineData(4, new[] { 4, 5 })]
    public void Play_order_takes_each_repeat_twice_and_the_second_ending_second(int from, int[] expected)
    {
        Assert.Equal(expected, MeiReader.Read(Repeats).PlayOrder(from));
    }

    [Fact]
    public void Music_without_repeats_plays_as_written()
    {
        var score = MeiReader.Read(MeiWriter.Write(new Score(new Key(0), new Meter(4, 4), [new Bar(1, []), new Bar(2, []), new Bar(3, [])])));

        Assert.Equal([0, 1, 2], score.PlayOrder());
        Assert.Equal([1, 2], score.PlayOrder(1));
    }

    [Fact]
    public void Performed_notes_run_on_through_the_repeats_and_name_each_pass()
    {
        var score = MeiReader.Read(Repeats);
        var bar = score.Meter.BarTicks;

        var notes = score.Performed(score.PlayOrder());

        Assert.Equal(["a", "b", "a~2", "b~2", "c", "d", "c~2", "e", "f"], notes.Select(n => n.Id));
        Assert.Equal(Enumerable.Range(0, 9).Select(k => (long)k * bar), notes.Select(n => n.Tick));
        Assert.Equal([1, 2, 1, 2, 3, 4, 3, 5, 6], notes.Select(n => n.Bar));
    }
}
