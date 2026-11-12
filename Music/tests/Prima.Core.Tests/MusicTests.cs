using System.Xml.Linq;
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
    public void ADottedEighthAndItsSixteenthShareABeam()
    {
        // A bar of 4/4: long then short on the first beat, short then long on the second, a dotted eighth with a rest after it on
        // the third, and a quarter.
        static Notes Note(string id, int start, int length, Step step) => new(id, start, length, [new Tone(id, P(step, 4))]);
        var voice = new Voice(1,
        [
            Note("a", 0, 360, Step.C), Note("b", 360, 120, Step.D),
            Note("c", 480, 120, Step.E), Note("d", 600, 360, Step.F),
            Note("e", 960, 360, Step.G), new Rest("f", 1320, 120),
            Note("g", 1440, Ticks.Quarter, Step.A),
        ]);
        var mei = XDocument.Parse(MeiWriter.Write(new Score(new Key(0), new Meter(4, 4), [new Bar(1, [voice])])));

        // Each pair is under one beam, inside its beat. A dotted eighth with no note to join keeps its flag, and a rest or a
        // quarter is never under a beam.
        var beams = mei.Descendants().Where(e => e.Name.LocalName == "beam").Select(b => string.Join(" ", b.Elements().Select(n => n.Attributes().First(a => a.Name.LocalName == "id").Value)));
        Assert.Equal(["a b", "c d"], beams);
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
    public void AChordMarkedToBeRolledIsReadAsOne()
    {
        // A roll's sign over both hands' chords, then one on a single note's chord by its start.
        var mei = """
            <mei xmlns="http://www.music-encoding.org/ns/mei"><music><body><mdiv><score><scoreDef><staffGrp>
            <staffDef n="1" lines="5" clef.shape="G" clef.line="2" meter.count="4" meter.unit="4"/><staffDef n="2" lines="5" clef.shape="F" clef.line="4"/></staffGrp></scoreDef>
            <section><measure n="1"><staff n="1"><layer n="1">
            <chord xml:id="rh" dur="2"><note xml:id="a" pname="e" oct="4"/><note xml:id="b" pname="g" oct="4"/></chord><chord xml:id="rh2" dur="2"><note xml:id="c" pname="f" oct="4"/><note xml:id="d" pname="a" oct="4"/></chord>
            </layer></staff><staff n="2"><layer n="1"><chord xml:id="lh" dur="1"><note xml:id="e" pname="c" oct="3"/><note xml:id="f" pname="g" oct="3"/></chord></layer></staff>
            <arpeg plist="#rh #lh"/><arpeg startid="#rh2"/></measure></section></score></mdiv></body></music></mei>
            """;

        Assert.Equal([new Mark(MarkKind.Roll, "rh"), new Mark(MarkKind.Roll, "lh"), new Mark(MarkKind.Roll, "rh2")], MeiReader.Read(mei).FileMarks);
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
        Assert.Equal([new Mark(MarkKind.Slur, "c", "d"), new Mark(MarkKind.Trill, "c")], read.FileMarks);
        Assert.Equal([true, true, false, true], read.Expected().Select(n => n.Lifted));
        Assert.Equal([null, null, "c", "c"], read.Expected().Select(n => n.Slur));
        // They're the file's, for judging a run, apart from the score's own marks that grading reads.
        Assert.Empty(read.Marks);
    }

    [Fact]
    public void MarksPlacedByTheBeatAreTiedToTheNoteAtOrBeforeIt()
    {
        // 3/4: a dynamic on beat 1, a pedal down at 1 and up just before beat 3 (2.9), a hairpin from beat 2 to the next bar's
        // beat 3, and a pedal mark under a staff with no note at its beat.
        var mei = """
            <mei xmlns="http://www.music-encoding.org/ns/mei"><music><body><mdiv><score><scoreDef><staffGrp><staffDef n="1" lines="5" clef.shape="G" clef.line="2" meter.count="3" meter.unit="4"/><staffDef n="2" lines="5" clef.shape="F" clef.line="4"/></staffGrp></scoreDef>
            <section>
            <measure n="1"><staff n="1"><layer n="1"><note xml:id="a" pname="c" oct="5" dur="4"/><note xml:id="b" pname="d" oct="5" dur="4"/><note xml:id="c" pname="e" oct="5" dur="4"/></layer></staff>
            <staff n="2"><layer n="1"><note xml:id="g" pname="c" oct="3" dur="2" dots="1"/></layer></staff>
            <dynam staff="1" tstamp="1">p <rend>dolce</rend></dynam><pedal staff="2" tstamp="1" dir="down"/><pedal staff="2" tstamp="2.9" dir="up"/>
            <hairpin staff="1" tstamp="2" tstamp2="1m+3" form="cres"/></measure>
            <measure n="2"><staff n="1"><layer n="1"><note xml:id="d" pname="f" oct="5" dur="4"/><note xml:id="e" pname="g" oct="5" dur="4"/><note xml:id="f" pname="a" oct="5" dur="4"/></layer></staff>
            <staff n="2"><layer n="1"><note xml:id="h" pname="g" oct="2" dur="2" dots="1"/></layer></staff>
            <pedal staff="2" tstamp="3" dir="bounce"/><dynam staff="1" tstamp="1">cresc.</dynam></measure>
            </section></score></mdiv></body></music></mei>
            """;

        var marks = MeiReader.Read(mei).FileMarks;

        Assert.Equal([
            new Mark(MarkKind.Dynamic, "a", Text: "p"),
            new Mark(MarkKind.PedalDown, "g"),
            new Mark(MarkKind.PedalUp, "g") { StartOffset = 912 },
            new Mark(MarkKind.Crescendo, "b", "f"),
            new Mark(MarkKind.PedalUp, "h") { StartOffset = 960 },
            new Mark(MarkKind.PedalDown, "h") { StartOffset = 960 },
            new Mark(MarkKind.Crescendo, "d"),
        ], marks);
    }

    [Fact]
    public void MarkingsAreTimedEachTimeTheirBarsArePlayed()
    {
        // Two bars, repeated: p on the first note, f on bar 2's first; the pedal down and up on bar 1's bass whole note; a slur over
        // bar 2's first two notes.
        Notes N(string id, int start, int length, Step step, int octave = 4) => new(id, start, length, [new Tone($"{id}1", P(step, octave))]);
        Voice Hands(string at, Step tune) => new(1, [N($"{at}a", 0, Ticks.Quarter, tune), N($"{at}b", Ticks.Quarter, Ticks.Quarter, tune), N($"{at}c", Ticks.Half, Ticks.Half, tune)]);
        var bar1 = new Bar(1, [Hands("x", Step.C), new Voice(2, [N("bass", 0, Ticks.Whole, Step.C, 3)])]);
        var bar2 = new Bar(2, [Hands("y", Step.D), new Voice(2, [N("bass2", 0, Ticks.Whole, Step.G, 2)])]) { RepeatEnd = true };
        var score = new Score(new Key(0), new Meter(4, 4), [bar1, bar2])
        {
            Marks = [new Mark(MarkKind.Dynamic, "xa1", Text: "p"), new Mark(MarkKind.Dynamic, "ya1", Text: "f"), new Mark(MarkKind.PedalDown, "bass1"), new Mark(MarkKind.PedalUp, "bass1"), new Mark(MarkKind.Slur, "ya1", "yb1")],
        };

        var played = score.Performed(score.PlayOrder());
        var markings = Prima.Core.Scoring.Markings.Of(score, played);

        Assert.Equal([("p", 0L, 1920L), ("f", 1920L, 3840L), ("p", 3840L, 5760L), ("f", 5760L, 7680L)], markings.Dynamics.Select(d => (d.Level, d.From, d.To)));
        // Down and up on the one note: held to its end, each time through.
        Assert.Equal([(0L, 1920L), (3840L, 5760L)], markings.Pedal.Select(p => (p.Down, p.Up)));
        // The slur's notes, each time through as their own.
        Assert.Equal(["ya1", "ya1", "ya1~2", "ya1~2"], played.Where(n => n.Slur is not null).Select(n => n.Slur));

        // Played from bar 2 alone, the f is there; from a bar with no dynamic, the one in force before it is.
        Assert.Equal("f", Prima.Core.Scoring.Markings.Of(score, score.Performed([1])).Dynamics[0].Level);
        var unmarked = score with { Marks = [.. score.Marks.Where(m => m.Text != "f")] };
        Assert.Equal([("p", 1920L)], Prima.Core.Scoring.Markings.Of(unmarked, unmarked.Performed([1])).Dynamics.Select(d => (d.Level, d.From)));
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

    // An 8va line over a bar's last three notes and a chord, as Verovio writes it from MusicXML: each note under it printed in its
    // written octave (oct) with the one it sounds in (oct.ges). F♯5 comes before the line, F5 in the bar after.
    private const string UnderAn8va = """
        <mei xmlns="http://www.music-encoding.org/ns/mei"><music><body><mdiv><score>
        <scoreDef meter.count="4" meter.unit="4" key.sig="0"><staffGrp><staffDef n="1" lines="5" clef.shape="G" clef.line="2"/></staffGrp></scoreDef>
        <section>
        <measure n="1"><staff n="1"><layer n="1">
        <note xml:id="a" pname="f" oct="5" dur="4" accid="s"/><note xml:id="b" pname="g" oct="5" oct.ges="6" dur="4"/>
        <note xml:id="c" pname="f" oct="5" oct.ges="6" dur="4"/>
        <chord xml:id="d" dur="4"><note xml:id="d1" pname="a" oct="4" oct.ges="5"/><note xml:id="d2" pname="e" oct="5" oct.ges="6"/></chord>
        </layer></staff><octave staff="1" startid="#b" endid="#d" dis="8" dis.place="above"/></measure>
        <measure n="2"><staff n="1"><layer n="1"><note xml:id="e" pname="f" oct="5" dur="1"/></layer></staff></measure>
        </section></score></mdiv></body></music></mei>
        """;

    private static Dictionary<string, Tone> TonesOf(Score score) => score.Bars.SelectMany(b => b.Voices).SelectMany(v => v.Events.OfType<Notes>()).SelectMany(n => n.Tones).ToDictionary(t => t.Id);

    [Fact]
    public void NotesUnderAn8vaLineAreExpectedAnOctaveAboveWhereTheyrePrinted()
    {
        var read = MeiReader.Read(UnderAn8va);
        var tones = TonesOf(read);
        string[] ids = ["a", "b", "c", "d1", "d2", "e"];

        // F♯5; under the line G6, F♯6 (the sharp lasts the bar on its line of the staff) and A5 with E6; F5 after it.
        Assert.Equal([78, 91, 90, 81, 88, 77], read.Expected().Select(n => n.Midi));
        // Each keeps where it's printed, which the sheet's marks go by.
        Assert.Equal(["F♯5", "G5", "F♯5", "A4", "E5", "F5"], ids.Select(id => tones[id].Written.ToString()));
        Assert.Equal([0, 1, 1, 1, 1, 0], ids.Select(id => tones[id].Ottava));

        // Played as it sounds, waiting at each note, every note is right first time.
        var judge = new Prima.Core.Scoring.WaitJudge(read.Expected());
        foreach (var key in new[] { 78, 91, 90, 81, 88, 77 }) judge.Play(key, 0);
        Assert.Equal((6, 0), (judge.Result().RightFirstTime, judge.Result().WrongKeys));
    }

    [Theory]
    [InlineData("8", "below", -1)]
    [InlineData("15", "above", 2)]
    [InlineData("15", "below", -2)]
    public void NotesUnderOtherOctaveLinesAreExpectedWhereTheLineMovesThem(string dis, string place, int octaves)
    {
        // C3 and G2 on the bass staff under the line, then C3 after it.
        var mei = $"""
            <mei xmlns="http://www.music-encoding.org/ns/mei"><music><body><mdiv><score>
            <scoreDef meter.count="2" meter.unit="4" key.sig="0"><staffGrp><staffDef n="1" lines="5" clef.shape="F" clef.line="4"/></staffGrp></scoreDef>
            <section>
            <measure n="1"><staff n="1"><layer n="1"><note xml:id="a" pname="c" oct="3" oct.ges="{3 + octaves}" dur="4"/><note xml:id="b" pname="g" oct="2" oct.ges="{2 + octaves}" dur="4"/></layer></staff>
            <octave staff="1" startid="#a" endid="#b" dis="{dis}" dis.place="{place}"/></measure>
            <measure n="2"><staff n="1"><layer n="1"><note xml:id="c" pname="c" oct="3" dur="2"/></layer></staff></measure>
            </section></score></mdiv></body></music></mei>
            """;

        var read = MeiReader.Read(mei);

        Assert.Equal([48 + 12 * octaves, 43 + 12 * octaves, 48], read.Expected().Select(n => n.Midi));
        Assert.Equal(["C3", "G2", "C3"], new[] { "a", "b", "c" }.Select(id => TonesOf(read)[id].Written.ToString()));
    }

    [Fact]
    public void NotesUnderAnOctaveLineAreWrittenBackWhereTheyrePrinted()
    {
        var read = MeiReader.Read(UnderAn8va);

        var mei = MeiWriter.Write(read);
        var reread = MeiReader.Read(mei);

        Assert.Equal(read.Expected(), reread.Expected());
        Assert.Equal(TonesOf(read), TonesOf(reread));
        // In its written octave with the one it sounds in, and without a sharp: the one before the line lasts on its line.
        var c = System.Xml.Linq.XDocument.Parse(mei).Descendants().Single(e => (string?)e.Attribute(System.Xml.Linq.XNamespace.Xml + "id") == "c");
        Assert.Equal(("5", "6", null, "s"), ((string?)c.Attribute("oct"), (string?)c.Attribute("oct.ges"), (string?)c.Attribute("accid"), (string?)c.Attribute("accid.ges")));
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

    [Fact]
    public void Bar_times_run_on_through_a_loop_from_the_first_bars_own_tick()
    {
        var score = MeiReader.Read(Repeats);
        var bar = (long)score.Meter.BarTicks;

        // Bars 3 and 4, twice round: the loop starts where bar 3 is written, and each bar follows the one before.
        Assert.Equal([(2, 2 * bar, bar), (3, 3 * bar, bar), (2, 4 * bar, bar), (3, 5 * bar, bar)], score.BarTimes([2, 3, 2, 3]));
        Assert.Empty(score.BarTimes([]));
    }

    [Fact]
    public void Bar_times_give_a_pickup_its_own_length_before_the_first_downbeat()
    {
        // A quarter's pickup into two bars of 4/4, the second with nothing but a rest: it has its time all the same.
        var pickup = new Bar(0, [new Voice(1, [new Notes("u", 0, Ticks.Quarter, [new Tone("u1", P(Step.G, 4))])])]) { Length = Ticks.Quarter };
        var full = new Bar(1, [new Voice(1, [new Notes("n", 0, 4 * Ticks.Quarter, [new Tone("n1", P(Step.C, 5))])])]);
        var silent = new Bar(2, [new Voice(1, [new Rest("r", 0, 4 * Ticks.Quarter)])]);
        var score = new Score(new Key(0), new Meter(4, 4), [pickup, full, silent]);

        Assert.Equal([(0, -(long)Ticks.Quarter, (long)Ticks.Quarter), (1, 0L, 4L * Ticks.Quarter), (2, 4L * Ticks.Quarter, 4L * Ticks.Quarter)], score.BarTimes([0, 1, 2]));
    }
}
