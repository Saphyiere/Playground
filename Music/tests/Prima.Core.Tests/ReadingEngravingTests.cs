using System.Xml.Linq;
using Prima.Core.Music;
using Prima.Core.Transcription;

namespace Prima.Core.Tests;

public class ReadingEngravingTests
{
    [Fact]
    public void OpeningBassClefUsesTheStaffDefinitionAndLaterChangesStayInTheirBar()
    {
        var upper = new ReadStaff(1, ReadClef.Bass, [new(1, [new(ReadKind.Note, [new(Step.C, Accidental.None, 4, false, 0)], NoteValue.Whole, 0, 0, false, false, Articulation.None, [], 0, ReadClef.None)])]);
        var lower = new ReadStaff(2, ReadClef.Bass, [new(1, [new(ReadKind.BarRest, [], NoteValue.Quarter, 0, 0, false, false, Articulation.None, [], 0, ReadClef.None)])]);
        var first = new ReadBar(1, 0, 4, 4, false, BarEnd.Normal, 0, [upper, lower]);
        var second = first with { Index = 2, Staves = [upper with { Clef = ReadClef.Treble }, lower] };
        var score = ReadingScore.Build([new([first, second], [])]).Score;
        var mei = ReadingScore.Mei(score);
        var document = XDocument.Parse(mei);
        XNamespace ns = "http://www.music-encoding.org/ns/mei";

        Assert.Equal("F", document.Descendants(ns + "staffDef").First(d => (string?)d.Attribute("n") == "1").Attribute("clef.shape")?.Value);
        var bars = document.Descendants(ns + "measure").ToArray();
        Assert.Empty(bars[0].Descendants(ns + "clef"));
        Assert.Equal("G", Assert.Single(bars[1].Descendants(ns + "clef")).Attribute("shape")?.Value);
        var readBack = MeiReader.Read(mei);
        Assert.Equal(score.Bars.SelectMany(b => b.Voices).SelectMany(v => v.Events).OfType<Notes>().SelectMany(n => n.Tones).Select(t => t.Pitch),
            readBack.Bars.SelectMany(b => b.Voices).SelectMany(v => v.Events).OfType<Notes>().SelectMany(n => n.Tones).Select(t => t.Pitch));
    }
}
