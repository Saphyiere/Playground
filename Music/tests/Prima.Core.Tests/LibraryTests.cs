using System.Buffers.Binary;
using Prima.Core.Generation;
using Prima.Core.Library;
using Prima.Core.Music;

namespace Prima.Core.Tests;

public class LibraryTests
{
    private static Spelled P(Step step, int octave, int alter = 0) => new(step, alter, octave);

    [Fact]
    public void GeneratedPassagesGradeNoHigherThanTheirLevel()
    {
        for (var level = 1; level <= Ladder.Top; level++)
        {
            var grades = Enumerable.Range(1, 6).Select(seed => Grader.Grade(PassageGenerator.Generate(Ladder.Level(level), (ulong)(level * 100 + seed)).Score)).ToList();

            Assert.All(grades, g => Assert.True(g.Level <= level, $"A level {level} passage graded {g.Level} ({string.Join(", ", g.Reasons)})"));
            if (level >= 4) Assert.True(grades.Average(g => g.Level) >= level - 3.5, $"Level {level} passages graded {string.Join(", ", grades.Select(g => g.Level))}");
        }
    }

    [Fact]
    public void GradesNameWhatSetThem()
    {
        var bar = new Bar(1,
        [
            new Voice(1, [new Notes("a", 0, 160, [new Tone("a1", P(Step.C, 5))]) { Tuplet = new Tuplet(3, 2) }, new Notes("b", 160, 160, [new Tone("b1", P(Step.D, 5))]) { Tuplet = new Tuplet(3, 2) }, new Notes("c", 320, 160, [new Tone("c1", P(Step.E, 5))]) { Tuplet = new Tuplet(3, 2) }, new Notes("d", 480, 1440, [new Tone("d1", P(Step.F, 5))])]),
            new Voice(2, [new Notes("e", 0, 1920, [new Tone("e1", P(Step.C, 3)), new Tone("e2", P(Step.G, 3))])]),
        ]);

        var grade = Grader.Grade(new Score(new Key(0), new Meter(4, 4), [bar]));

        Assert.Equal(9, grade.Level);
        Assert.Equal(["triplets"], grade.Reasons);
        Assert.InRange(grade.Within, 0, 1);
    }

    [Fact]
    public void NotesUnderAn8vaLineAreGradedAsPrinted()
    {
        // E6 to A6 under an 8va line are printed on the staff, E5 to A5, and read like those: not four ledger lines up.
        Score Run(int octave, int ottava) => new(new Key(0), new Meter(4, 4), [new Bar(1, [new Voice(1, [.. new[] { Step.E, Step.F, Step.G, Step.A }.Select((step, i) => new Notes($"n{i}", i * Ticks.Quarter, Ticks.Quarter, [new Tone($"t{i}", P(step, octave)) { Ottava = ottava }]))])])]);

        var (printed, under) = (Grader.Grade(Run(5, 0)), Grader.Grade(Run(6, 1)));

        Assert.Equal((printed.Level, printed.Within), (under.Level, under.Within));
        Assert.Equal(printed.Reasons, under.Reasons);
        Assert.DoesNotContain(under.Reasons, r => r.EndsWith("ledger lines"));
    }

    [Fact]
    public void AMinorPiecesLeadingNoteIsntChromatic()
    {
        // A minor written with no signature, as files often are: G sharp is its leading note, not an accidental to grade.
        var bar = new Bar(1, [new Voice(1, [new Notes("a", 0, 960, [new Tone("a1", P(Step.G, 4, 1))]), new Notes("b", 960, 960, [new Tone("b1", P(Step.A, 4))])])]);

        Assert.DoesNotContain("accidentals", Grader.Grade(new Score(new Key(0), new Meter(4, 4), [bar])).Reasons);
        Assert.Equal(7, Grader.Grade(new Score(new Key(0), new Meter(4, 4), [new Bar(1, [new Voice(1, [new Notes("a", 0, 1920, [new Tone("a1", P(Step.E, 4, -1))])])])])).Level);
    }

    [Fact]
    public void ExcerptsAreCutAtPhrasesAndNeverIntoATie()
    {
        var passage = PassageGenerator.Generate(Ladder.Level(10), 77).Score;
        var tiedInto = passage.Bars.Where(b => b.Voices.Any(v => v.Events.OfType<Notes>().Any(n => n.Start == 0 && n.Tones.Any(t => t.TiedFromPrevious)))).Select(b => b.Number).ToHashSet();

        var cuts = Excerpts.Cut(passage);

        Assert.NotEmpty(cuts);
        Assert.All(cuts, c => Assert.Contains(c.Bars, Excerpts.Lengths));
        Assert.All(cuts, c => Assert.DoesNotContain(Math.Max(1, c.FirstBar), tiedInto));
        foreach (var length in Excerpts.Lengths)
        {
            var same = cuts.Where(c => c.Bars == length).OrderBy(c => c.FirstBar).ToList();
            Assert.All(same.Zip(same.Skip(1)), p => Assert.True(p.Second.FirstBar > p.First.LastBar));
        }
        Assert.All(cuts, c => Assert.InRange(c.Grade.Level, 1, 10));
    }

    [Fact]
    public void RepeatedBarsArentCutTwice()
    {
        Bar Melody(int number) => new(number, [new Voice(1, [new Notes($"n{number}a", 0, 960, [new Tone($"n{number}a", P(Step.C, 5))]), new Notes($"n{number}b", 960, 960, [new Tone($"n{number}b", P(Step.E, 5))])])]);
        var score = new Score(new Key(0), new Meter(4, 4), [.. Enumerable.Range(1, 8).Select(Melody)]);

        // Eight identical bars: one cut of each length, not four.
        Assert.Equal(3, Excerpts.Cut(score).Count);
    }

    [Fact]
    public void APdfPagesSystemsAreFoundFromItsStaffLines()
    {
        // A page 1200 rows tall with four grand staves: lines 2 rows thick and 10 apart, 80 rows between the staves of a system
        // and 120 between systems; a title near the top that's dark in places but never a line.
        var rows = new double[1200];
        for (var i = 0; i < rows.Length; i++) rows[i] = 0.04;
        for (var i = 20; i < 40; i++) rows[i] = 0.3;
        var top = 80;
        for (var system = 0; system < 6; system++)
        {
            foreach (var staffTop in new[] { top, top + 120 })
            {
                for (var line = 0; line < 5; line++) rows[staffTop + line * 10] = rows[staffTop + line * 10 + 1] = 0.9;
            }
            top += 120 + 40 + 120;
            if (top > 1100) break;
        }

        var systems = PdfLayout.FindSystems(rows);

        Assert.Equal(4, systems.Count);
        Assert.All(systems.Zip(systems.Skip(1)), p => Assert.True(p.First.Bottom <= p.Second.Top));
        Assert.InRange(systems[0].Top, 0, 80);
        var half = PdfLayout.HalfAt(systems, 1200);
        Assert.True(half > systems[1].Top && half < systems[2].Bottom);
        Assert.Equal(12, PdfLayout.SecondsFor(new PageSystem(0, 100, 3), 4, 60));
    }

    [Fact]
    public void AHalfPageTurnShowsOnlyWholeSystems()
    {
        // This page's systems every 200 px from 100; the next page's start higher, at 60 (no title), every 180.
        var current = Enumerable.Range(0, 5).Select(i => new PageSystem(100 + 200 * i, 280 + 200 * i, 4)).ToList();
        var next = Enumerable.Range(0, 5).Select(i => new PageSystem(60 + 180 * i, 220 + 180 * i, 4)).ToList();

        var (cover, shown) = PdfLayout.HalfTurn(current, 1200, next);

        // This page keeps its systems from the gap nearest its middle (600): the one between its third and fourth, at 690. The next
        // page shows its first three, down to the gap after them (590), and none of its fourth, which would reach past 690.
        Assert.Equal(690, cover);
        Assert.Equal(590, shown);
        Assert.All(next.Where(s => s.Top < shown), s => Assert.True(s.Bottom <= shown));
    }

    [Fact]
    public void AMidiFilesNotesTempoAndMetreAreRead()
    {
        // Two tracks at 480 ticks a quarter: the tempo (120 a minute) and 3/4, then C4 and E4 a quarter each.
        static byte[] Track(params byte[] events) => [.. "MTrk"u8, .. BigEndian(events.Length), .. events];
        static byte[] BigEndian(int value)
        {
            var bytes = new byte[4];
            BinaryPrimitives.WriteInt32BigEndian(bytes, value);
            return bytes;
        }

        byte[] file =
        [
            .. "MThd"u8, 0, 0, 0, 6, 0, 1, 0, 2, 0x01, 0xE0,
            .. Track(0, 0xFF, 0x51, 3, 0x07, 0xA1, 0x20, 0, 0xFF, 0x58, 4, 3, 2, 24, 8, 0, 0xFF, 0x2F, 0),
            .. Track(0, 0x90, 60, 80, 0x83, 0x60, 0x80, 60, 64, 0, 0x90, 64, 90, 0x83, 0x60, 64, 0, 0, 0xFF, 0x2F, 0),
        ];

        var midi = MidiFile.Read(file);

        Assert.Equal(120, midi.Bpm);
        Assert.Equal((3, 4), (midi.BeatsPerBar, midi.BeatUnit));
        Assert.Equal([new FileNote(60, 0, 0.5, 80), new FileNote(64, 0.5, 1.0, 90)], midi.Notes);
        Assert.Equal(1, midi.Bars);
    }
}

public class CollectionTests
{
    // Two pieces engraved as one score: the first in C, 4/4, its bass staff moving to the treble clef in bar 2; the second in
    // D major, 3/4, from a definition between them. Each ends at a final barline.
    private const string TwoPieces = """
        <mei xmlns="http://www.music-encoding.org/ns/mei">
          <meiHead><fileDesc><titleStmt><title>Little Pieces</title></titleStmt></fileDesc></meiHead>
          <music><body><mdiv><score>
            <scoreDef xml:id="top">
              <pgHead><rend>Little Pieces</rend></pgHead>
              <staffGrp><staffGrp><label>No. 1 in C</label>
                <staffDef xml:id="s1" n="1" lines="5"><clef xml:id="c1" shape="G" line="2" /><keySig xml:id="k1" sig="0" /><meterSig xml:id="m1" count="4" unit="4" /></staffDef>
                <staffDef xml:id="s2" n="2" lines="5"><clef xml:id="c2" shape="F" line="4" /><keySig xml:id="k2" sig="0" /><meterSig xml:id="m2" count="4" unit="4" /></staffDef>
              </staffGrp></staffGrp>
            </scoreDef>
            <section>
              <measure xml:id="b1" n="1"><staff n="1"><layer n="1"><note xml:id="n1" dur="1" pname="c" oct="5" /></layer></staff><staff n="2"><layer n="1"><note xml:id="n2" dur="1" pname="c" oct="3" /></layer></staff></measure>
              <measure xml:id="b2" n="2"><staff n="1"><layer n="1"><note xml:id="n3" dur="1" pname="d" oct="5" /></layer></staff><staff n="2"><layer n="1"><clef xml:id="cc" shape="G" line="2" /><note xml:id="n4" dur="1" pname="e" oct="4" /></layer></staff></measure>
              <sb />
              <measure xml:id="b3" n="3" right="end"><staff n="1"><layer n="1"><note xml:id="n5" dur="1" pname="e" oct="5" /></layer></staff><staff n="2"><layer n="1"><note xml:id="n6" dur="1" pname="g" oct="4" /></layer></staff></measure>
              <scoreDef xml:id="change"><keySig xml:id="k3" sig="2s" /><meterSig xml:id="m3" count="3" unit="4" /></scoreDef>
              <measure xml:id="b4" n="4"><staff n="1"><layer n="1"><note xml:id="n7" dur="2" dots="1" pname="f" oct="5" accid.ges="s" /></layer></staff><staff n="2"><layer n="1"><note xml:id="n8" dur="2" dots="1" pname="d" oct="4" /></layer></staff></measure>
              <measure xml:id="b5" n="5" right="end"><staff n="1"><layer n="1"><note xml:id="n9" dur="2" dots="1" pname="a" oct="5" /></layer></staff><staff n="2"><layer n="1"><note xml:id="n10" dur="2" dots="1" pname="d" oct="4" /></layer></staff></measure>
            </section>
          </score></mdiv></body></music>
        </mei>
        """;

    [Fact]
    public void ACollectionIsSplitAtItsFinalBarlinesEachPartStartingWithWhatIsInForce()
    {
        var parts = Collections.Split(TwoPieces, longBars: 0, fewestPieces: 2, shortestPiece: 2);

        Assert.Equal(2, parts.Count);
        var first = MeiReader.Read(parts[0]);
        var second = MeiReader.Read(parts[1]);
        Assert.Equal(3, first.Bars.Count);
        Assert.Equal(2, second.Bars.Count);
        Assert.Equal((0, 4, 4), (first.Key.Fifths, first.Meter.Beats, first.Meter.Unit));
        Assert.Equal((2, 3, 4), (second.Key.Fifths, second.Meter.Beats, second.Meter.Unit));
        // The second piece's bass staff starts in the treble clef it changed to; nothing of the first piece is left in it.
        var bass = System.Xml.Linq.XDocument.Parse(parts[1]).Descendants().First(e => e.Name.LocalName == "staffDef" && (string?)e.Attribute("n") == "2");
        Assert.Equal(("G", "2"), ((string?)bass.Element(bass.Name.Namespace + "clef")?.Attribute("shape"), (string?)bass.Element(bass.Name.Namespace + "clef")?.Attribute("line")));
        Assert.DoesNotContain("n1\"", parts[1]);
        Assert.DoesNotContain("pgHead", parts[1]);
        Assert.DoesNotContain("No. 1 in C", parts[1]);
        Assert.Contains("Little Pieces, No. 2", parts[1]);
        Assert.Equal(["n10", "n7", "n8", "n9"], second.Expected().Select(n => n.Id).Order(StringComparer.Ordinal));
    }

    [Fact]
    public void AShortScoreOrOneWithFewPiecesStaysWhole()
    {
        Assert.Equal([TwoPieces], Collections.Split(TwoPieces));
        Assert.Single(Collections.Split(TwoPieces, longBars: 0, fewestPieces: 3, shortestPiece: 2));
        Assert.Single(Collections.Split(TwoPieces, longBars: 0, fewestPieces: 2, shortestPiece: 3));
    }
}
