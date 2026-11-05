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
}
