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

    private static List<ScoreEvent> Scale()
    {
        // C major up and down an octave in quarters: 16 notes, 4 bars of 4/4.
        int[] midi = [60, 62, 64, 65, 67, 69, 71, 72, 72, 71, 69, 67, 65, 64, 62, 60];
        return [.. midi.Select((m, i) => new ScoreEvent(i, i, [m], [$"n{i}"], i / 4 + 1, i % 4 == 0))];
    }

    private static long Ms(double ms) => (long)(ms * 1e6);

    [Fact]
    public void TheFollowerFollowsAndTakesTheTempo()
    {
        var events = Scale();
        var follower = new Follower(events, 60);

        for (var i = 0; i < events.Count; i++) follower.NoteOn(events[i].Pitches[0], Ms(i * 800));

        Assert.True(follower.Finished);
        Assert.InRange(follower.Tempo, 73, 77);
        Assert.Equal(0, follower.Extra);
    }

    [Fact]
    public void AWrongNoteDoesntMoveTheFollowerBack()
    {
        var events = Scale();
        var follower = new Follower(events, 60);

        for (var i = 0; i < 6; i++) follower.NoteOn(events[i].Pitches[0], Ms(i * 1000));
        follower.NoteOn(40, Ms(6000));
        var after = follower.Position;
        for (var i = 7; i < events.Count; i++) follower.NoteOn(events[i].Pitches[0], Ms(i * 1000));

        Assert.Equal(6, after);
        Assert.True(follower.Finished);
        Assert.Equal(1, follower.Extra);
    }

    [Fact]
    public void TheFollowerFindsASkipAndARestart()
    {
        // Two bars of C, D, E, F, then the player skips to the two bars of F, G, A, B; a jump the local window can't see.
        int[] tune = [60, 62, 64, 65, 60, 62, 64, 65, 48, 50, 52, 53, 77, 79, 81, 83, 77, 79, 81, 83];
        var events = tune.Select((m, i) => new ScoreEvent(i, i, [m], [$"n{i}"], i / 4 + 1, i % 4 == 0)).ToList();
        var follower = new Follower(events, 60);

        follower.NoteOn(60, Ms(0));
        follower.NoteOn(62, Ms(1000));
        foreach (var (m, i) in new[] { 77, 79, 81, 83 }.Select((m, i) => (m, i))) follower.NoteOn(m, Ms(2000 + i * 1000));

        Assert.InRange(follower.Position, 15, 16);

        // A silence of over two bars, then the player starts the bar again.
        follower.NoteOn(77, Ms(20_000));

        Assert.Equal(17, follower.Position);
    }

    [Fact]
    public void ATakeIsAlignedWithTheScoreAfterwards()
    {
        var expected = new[] { 60, 62, 64, 65 }.Select((m, i) => new ExpectedNote($"n{i}", m, 1, 1, i * Ticks.Quarter, Ticks.Quarter, null)).ToList();
        (int, long)[] played = [(60, 0), (63, Ms(500)), (64, Ms(1000)), (67, Ms(1500)), (69, Ms(1600))];

        var (notes, extra) = Alignment.Align(expected, played);

        Assert.Equal([Played.Right, Played.Wrong, Played.Right, Played.Wrong], notes.Select(n => n.Outcome));
        Assert.Equal(63, notes[1].Instead);
        Assert.Equal(1, extra);
    }

    [Fact]
    public void ATakeThatStopsEarlyLinesUpWithWhatWasPlayed()
    {
        // The same four notes twice; the player plays them once and stops. Given the stretch played, all four are right.
        var expected = new[] { 60, 62, 64, 65, 60, 62, 64, 65 }.Select((m, i) => new ExpectedNote($"n{i}", m, i / 4 + 1, 1, i * Ticks.Quarter, Ticks.Quarter, null)).ToList();
        (int, long)[] played = [(60, 0), (62, Ms(500)), (64, Ms(1000)), (65, Ms(1500))];

        var (notes, extra) = Alignment.Align(expected.Take(4).ToList(), played);

        Assert.All(notes, n => Assert.Equal(Played.Right, n.Outcome));
        Assert.Equal(["n0", "n1", "n2", "n3"], notes.Select(n => n.Id));
        Assert.Equal(0, extra);
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
