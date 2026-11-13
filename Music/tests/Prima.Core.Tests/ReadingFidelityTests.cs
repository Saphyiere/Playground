using System.Xml.Linq;
using Prima.Core.Music;
using Prima.Core.Transcription;

namespace Prima.Core.Tests;

public class ReadingFidelityTests
{
    private static ReadEvent Note(params ReadMark[] marks) => new(ReadKind.Note, [new(Step.D, Accidental.None, 5, false, 0)],
        NoteValue.Whole, 0, 0, false, false, Articulation.None, marks, 0, ReadClef.None);

    private static ReadEvent Rest(params ReadMark[] marks) => Note(marks) with { Kind = ReadKind.BarRest, Pitches = [] };

    private static ReadBar Bar(ReadEvent upper, ReadEvent? lower = null) => new(1, 0, 4, 4, false, BarEnd.Normal, 0,
        [new(1, ReadClef.Treble, [new(1, [upper])]), new(2, ReadClef.Bass, [new(1, [lower ?? Rest()])])]);

    [Theory]
    [InlineData(1, "8", "above")]
    [InlineData(2, "15", "above")]
    [InlineData(-1, "8", "below")]
    public void OctaveLinesKeepTheWrittenPositionAndSoundingPitchAcrossSystems(int shift, string displacement, string place)
    {
        var first = Bar(Note() with { Ottava = shift });
        var score = ReadingScore.Build([new([first], []), new([first], [])]).Score;
        var tones = score.Bars.SelectMany(b => b.Voices).SelectMany(v => v.Events).OfType<Notes>().SelectMany(n => n.Tones).ToArray();
        Assert.All(tones, tone =>
        {
            Assert.Equal(shift, tone.Ottava);
            Assert.Equal(new Spelled(Step.D, 0, 5), tone.Written);
            Assert.Equal(new Spelled(Step.D, 0, 5 + shift), tone.Pitch);
        });
        var mei = ReadingScore.Mei(score);
        XNamespace ns = "http://www.music-encoding.org/ns/mei";
        var document = XDocument.Parse(mei);
        var line = Assert.Single(document.Descendants(ns + "octave"));
        Assert.Equal(displacement, (string?)line.Attribute("dis"));
        Assert.Equal(place, (string?)line.Attribute("dis.place"));
        Assert.NotEqual((string?)line.Attribute("startid"), (string?)line.Attribute("endid"));
        Assert.All(document.Descendants(ns + "note"), note =>
        {
            Assert.Equal("5", (string?)note.Attribute("oct"));
            Assert.Equal((5 + shift).ToString(), (string?)note.Attribute("oct.ges"));
        });
        var roundTrip = MeiReader.Read(mei);
        Assert.Equal(score.Expected().Select(n => (n.Midi, n.Tick, n.Length)), roundTrip.Expected().Select(n => (n.Midi, n.Tick, n.Length)));
        Assert.All(roundTrip.Bars.SelectMany(b => b.Voices).SelectMany(v => v.Events).OfType<Notes>().SelectMany(n => n.Tones), t => Assert.Equal(shift, t.Ottava));
        Assert.Equal(5, first.Staves[0].Voices[0].Events[0].Pitches[0].Octave);
    }

    [Fact]
    public void SharedOctaveRunsInTwoVoicesProduceOneVisibleLine()
    {
        var note = Note() with { Ottava = 1 };
        var bar = Bar(note);
        bar = bar with { Staves = [bar.Staves[0] with { Voices = [new(1, [note]), new(2, [note])] }, bar.Staves[1]] };
        var score = ReadingScore.Build([new([bar, bar with { Index = 2 }], [])]).Score;
        XNamespace ns = "http://www.music-encoding.org/ns/mei";

        Assert.Single(XDocument.Parse(ReadingScore.Mei(score)).Descendants(ns + "octave"));
    }

    [Fact]
    public void ReturningToNormalOctaveEndsTheLineBeforeTheNextShiftedRun()
    {
        var shifted = Bar(Note() with { Ottava = 1 });
        var normal = Bar(Note()) with { Index = 2 };
        var score = ReadingScore.Build([new([shifted, normal, shifted with { Index = 3 }], [])]).Score;
        XNamespace ns = "http://www.music-encoding.org/ns/mei";

        Assert.Equal(2, XDocument.Parse(ReadingScore.Mei(score)).Descendants(ns + "octave").Count());
    }

    [Fact]
    public void ValidTiesSlursHairpinsAndPedalAcrossAdjacentSystemsHaveNoIssues()
    {
        var start = Note(ReadMark.SlurStart, ReadMark.CrescendoStart, ReadMark.PedalDown);
        start = start with { Pitches = [start.Pitches[0] with { Tie = true }] };
        var end = Note(ReadMark.SlurEnd, ReadMark.HairpinEnd, ReadMark.PedalUp);

        Assert.Empty(ReadingFidelity.Find([new([Bar(start)], []), new([Bar(end)], [])]));
    }

    [Fact]
    public void FinalUnresolvedMarksAreReportedAtTheirOriginalSourceEvent()
    {
        var start = Note(ReadMark.SlurStart, ReadMark.DiminuendoStart, ReadMark.PedalDown);
        start = start with { Pitches = [start.Pitches[0] with { Tie = true }] };
        var reading = new SystemReading([Bar(start)], []);

        var issues = ReadingFidelity.Find([reading]);

        Assert.Equal(new[] { ReadingFidelityKind.DanglingTie, ReadingFidelityKind.UnclosedSlur, ReadingFidelityKind.UnclosedHairpin, ReadingFidelityKind.UnclosedPedal }.Order(), issues.Select(i => i.Kind).Order());
        Assert.All(issues, i => Assert.Equal((0, 1, 1, 1, 0), (i.SystemIndex, i.BarIndex, i.Staff, i.Voice, i.EventIndex)));
        Assert.True(start.Pitches[0].Tie);
        Assert.Equal(3, start.Marks.Count);
    }

    [Fact]
    public void MissingSystemsDoNotJoinMarksOrClaimThatUnknownContinuationsAreWrong()
    {
        var start = Note(ReadMark.SlurStart, ReadMark.CrescendoStart, ReadMark.PedalDown);
        start = start with { Pitches = [start.Pitches[0] with { Tie = true }] };
        var end = Note(ReadMark.SlurEnd, ReadMark.HairpinEnd, ReadMark.PedalUp);

        var issues = ReadingFidelity.Find([new([Bar(start)], []), null, new([Bar(end)], [])]);

        Assert.Equal(7, issues.Count);
        Assert.All(issues, issue => Assert.Equal(ReadingFidelityKind.BoundaryContinuation, issue.Kind));
        Assert.Equal([0, 2], issues.Select(i => i.SystemIndex).Distinct());
    }

    [Fact]
    public void UnmatchedEndMarksAndBrokenTiesAreExplicit()
    {
        var start = Note() with { Pitches = [new(Step.C, Accidental.None, 5, true, 0)] };
        var end = Note(ReadMark.SlurEnd, ReadMark.HairpinEnd, ReadMark.PedalUp);

        var issues = ReadingFidelity.Find([new([Bar(start), Bar(end) with { Index = 2 }], [])]);

        Assert.Contains(issues, i => i.Kind == ReadingFidelityKind.DanglingTie && i.BarIndex == 1);
        Assert.Contains(issues, i => i.Kind == ReadingFidelityKind.UnmatchedSlurEnd && i.BarIndex == 2);
        Assert.Contains(issues, i => i.Kind == ReadingFidelityKind.UnmatchedHairpinEnd);
        Assert.Contains(issues, i => i.Kind == ReadingFidelityKind.UnmatchedPedalUp);
    }

    [Fact]
    public void MarksOnRestOnlyBarsAndStaffsExposeTheirEngravingLoss()
    {
        var empty = Bar(Rest(), Rest(ReadMark.P));
        var otherStaff = Bar(Note(), Rest(ReadMark.P));

        var dropped = Assert.Single(ReadingFidelity.Find([new([empty], [])]));
        var moved = Assert.Single(ReadingFidelity.Find([new([otherStaff], [])]));

        Assert.Equal(ReadingFidelityKind.MarkDropped, dropped.Kind);
        Assert.Equal(ReadingFidelityKind.MarkReanchored, moved.Kind);
        Assert.Equal(2, dropped.Staff);
        Assert.Equal(2, moved.Staff);
    }
}
