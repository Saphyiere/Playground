using System.Buffers.Binary;
using Playground.AI.Assist;
using Prima.Core.Music;
using Prima.Core.Scoring;
using Prima.Core.Transcription;

namespace Prima.Core.Tests;

public class TranscriptionTests
{
    private static readonly Usage Cents = new(0, 0, 0, 0, 0.1m, TimeSpan.Zero);

    private static ReadPitch P(Step step, int octave, Accidental accidental = Accidental.None, bool tie = false, int finger = 0) => new(step, accidental, octave, tie, finger);

    private static ReadEvent N(NoteValue value, params ReadPitch[] pitches) => new(ReadKind.Note, pitches, value, 0, 0, false, false, Articulation.None, [], 0, ReadClef.None);

    private static ReadEvent Rest(NoteValue value) => new(ReadKind.Rest, [], value, 0, 0, false, false, Articulation.None, [], 0, ReadClef.None);

    private static readonly ReadEvent BarRest = new(ReadKind.BarRest, [], NoteValue.Quarter, 0, 0, false, false, Articulation.None, [], 0, ReadClef.None);

    private static ReadBar Bar(int index, IReadOnlyList<ReadEvent> upper, IReadOnlyList<ReadEvent>? lower = null, int fifths = 0, int count = 4, int unit = 4) =>
        new(index, fifths, count, unit, false, BarEnd.Normal, 0,
            [new ReadStaff(1, ReadClef.Treble, [new ReadVoice(1, upper)]), new ReadStaff(2, ReadClef.Bass, [new ReadVoice(1, lower ?? [BarRest])])]);

    private static SystemReading Sys(params ReadBar[] bars) => new(bars, []);

    private static ReadEvent With(ReadEvent e, params ReadMark[] marks) => e with { Marks = marks };

    // Bar 1: n1 C5 (p), r2 (f), n3 E5 (sfz, a crescendo starts), n4 F5 above; n5 C3 (Ped.) and n6 G3 (a change) below.
    // Bar 2: n7 G5, r8 (the crescendo ends, dim.) above; n9 C3, r10 (the pedal's let up) below.
    private static readonly SystemReading Marked = Sys(
        Bar(1,
            [With(N(NoteValue.Quarter, P(Step.C, 5)), ReadMark.P), With(Rest(NoteValue.Quarter), ReadMark.F),
                With(N(NoteValue.Quarter, P(Step.E, 5)), ReadMark.CrescendoStart, ReadMark.Sfz), N(NoteValue.Quarter, P(Step.F, 5))],
            [With(N(NoteValue.Half, P(Step.C, 3)), ReadMark.PedalDown), With(N(NoteValue.Half, P(Step.G, 3)), ReadMark.PedalChange)]),
        Bar(2,
            [N(NoteValue.Half, P(Step.G, 5)), With(Rest(NoteValue.Half), ReadMark.Dim, ReadMark.HairpinEnd)],
            [N(NoteValue.Half, P(Step.C, 3)), With(Rest(NoteValue.Half), ReadMark.PedalUp)]));

    private static IEnumerable<Notes> NotesOf(Score score, int bar, int staff = 1) =>
        score.Bars.Single(b => b.Number == bar).Voices.Where(v => v.Staff == staff).SelectMany(v => v.Events).OfType<Notes>();

    private static Answer<T> Ok<T>(T value) => new(AnswerKind.Answered, value, "", [], Cents);

    private static Answer<T> Not<T>(AnswerKind kind, string summary) => new(kind, default, summary, [], Cents);

    /// <summary>Answers each question from a script, and keeps what was asked.</summary>
    private sealed class ScriptedClaude : IClaude
    {
        public List<(string Task, object Input)> Asked { get; } = [];

        public Func<SystemToRead, Answer<SystemReading>> Read { get; set; } = _ => throw new InvalidOperationException("No reading scripted.");

        public Func<SystemToCompare, Answer<SystemComparison>> Compare { get; set; } = _ => Ok(new SystemComparison([], []));

        public Task<Answer<TAnswer>> AskAsync<TInput, TAnswer>(AssistTask<TInput, TAnswer> task, TInput input, CancellationToken ct = default)
        {
            lock (Asked) Asked.Add((task.Name, input!));
            object answer = input switch
            {
                SystemToRead read => Read(read),
                SystemToCompare compare => Compare(compare),
                _ => throw new InvalidOperationException($"No script for {task.Name}."),
            };
            return Task.FromResult((Answer<TAnswer>)answer);
        }

        public Task<Answer<string>> TestAsync(CancellationToken ct = default) => Task.FromResult(Ok("Claude"));
    }

    private sealed class FakeEngraver : IEngraver
    {
        public List<(string Mei, int Width)> Engraved { get; } = [];

        public Task<byte[]> EngraveAsync(string mei, int widthPx, CancellationToken ct)
        {
            lock (Engraved) Engraved.Add((mei, widthPx));
            return Task.FromResult(Png(widthPx));
        }
    }

    /// <summary>The start of a PNG: enough for its width, at bytes 16 to 19.</summary>
    private static byte[] Png(int width)
    {
        var bytes = new byte[32];
        BinaryPrimitives.WriteInt32BigEndian(bytes.AsSpan(16, 4), width);
        return bytes;
    }

    private static readonly ReadBar CBar = Bar(1, [N(NoteValue.Whole, P(Step.C, 5))]);

    private static readonly ReadBar DBar = Bar(1, [N(NoteValue.Whole, P(Step.D, 5))]);

    [Fact]
    public void Schemas_FitTheApisRules()
    {
        Assert.NotEmpty(AssistSchema.For(typeof(Reply<SystemReading>)));
        Assert.NotEmpty(AssistSchema.For(typeof(Reply<SystemComparison>)));
    }

    [Fact]
    public void Length_ValuesDotsAndTuplets()
    {
        ReadEvent E(NoteValue value, int dots = 0, int tuplet = 0) => N(value, P(Step.C, 4)) with { Dots = dots, Tuplet = tuplet };

        Assert.Equal(480, ReadingChecks.Length(E(NoteValue.Quarter)));
        Assert.Equal(720, ReadingChecks.Length(E(NoteValue.Quarter, dots: 1)));
        Assert.Equal(1680, ReadingChecks.Length(E(NoteValue.Half, dots: 2)));
        Assert.Equal(160, ReadingChecks.Length(E(NoteValue.Eighth, tuplet: 3)));
        Assert.Equal(360, ReadingChecks.Length(E(NoteValue.Eighth, tuplet: 2)));
        Assert.Null(ReadingChecks.Length(E(NoteValue.Sixteenth, tuplet: 7)));
    }

    [Fact]
    public void Problems_NoneForBarsThatAddUp()
    {
        var reading = Sys(Bar(1, [N(NoteValue.Quarter, P(Step.C, 5)), N(NoteValue.Quarter, P(Step.D, 5)), N(NoteValue.Half, P(Step.E, 5), P(Step.G, 5))]), DBar with { Index = 2 });

        Assert.Empty(ReadingChecks.Problems(reading, first: false));
    }

    [Fact]
    public void Problems_NameAVoiceThatRunsOverOrFallsShort()
    {
        var over = Sys(Bar(1, [N(NoteValue.Whole, P(Step.C, 5)), N(NoteValue.Quarter, P(Step.D, 5))]));
        var shortOne = Sys(Bar(1, [N(NoteValue.Half, P(Step.C, 5))]));

        Assert.Equal(["Bar 1, upper staff, voice 1 adds up to 5 quarter notes, but the bar holds 4."], ReadingChecks.Problems(over, first: false));
        Assert.Contains("short of the bar's 4", Assert.Single(ReadingChecks.Problems(shortOne, first: false)));
    }

    [Fact]
    public void Problems_TheFirstSystemMayStartWithAPickup()
    {
        var pickup = Sys(Bar(1, [N(NoteValue.Quarter, P(Step.G, 4))], [Rest(NoteValue.Quarter)]), CBar with { Index = 2 });

        Assert.Empty(ReadingChecks.Problems(pickup, first: true));
        Assert.NotEmpty(ReadingChecks.Problems(pickup, first: false));
    }

    [Fact]
    public void Problems_ATieMustLandOnItsPitch()
    {
        var missed = Sys(Bar(1, [N(NoteValue.Half, P(Step.C, 5, tie: true)), N(NoteValue.Half, P(Step.D, 5))]));
        var intoTheNextSystem = Sys(Bar(1, [N(NoteValue.Whole, P(Step.C, 5, tie: true))]));
        var acrossTheBarline = Sys(Bar(1, [N(NoteValue.Whole, P(Step.C, 5, tie: true))]), CBar with { Index = 2 });

        Assert.Contains("the tie from C5", Assert.Single(ReadingChecks.Problems(missed, first: false)));
        Assert.Empty(ReadingChecks.Problems(intoTheNextSystem, first: false));
        Assert.Empty(ReadingChecks.Problems(acrossTheBarline, first: false));
    }

    [Fact]
    public void Problems_ABarNeedsBothStavesAndTheirClefs()
    {
        var oneStaff = new ReadBar(1, 0, 4, 4, false, BarEnd.Normal, 0, [new ReadStaff(1, ReadClef.None, [new ReadVoice(1, [BarRest])])]);

        var problems = ReadingChecks.Problems(Sys(oneStaff), first: false);

        Assert.Contains("Bar 1 needs both staves, 1 (upper) and 2 (lower), once each.", problems);
        Assert.Contains("Bar 1, upper staff: give the clef in force, treble or bass.", problems);
    }

    [Fact]
    public void Problems_AFingerIsOneToFive()
    {
        var problems = ReadingChecks.Problems(Sys(Bar(1, [N(NoteValue.Whole, P(Step.C, 5, finger: 6))])), first: false);

        Assert.Equal(["Bar 1, upper staff, voice 1: event 1's finger is 6; give 1 to 5 as printed, or 0 where none is."], problems);
    }

    [Fact]
    public void Build_TheKeyAndTheBarsEarlierAccidentalsSetThePitchesThatSound()
    {
        // G major: an F is sharp until a natural, which holds for the rest of the bar on its line, and not on another octave's.
        var bar1 = Bar(1, [N(NoteValue.Quarter, P(Step.F, 5)), N(NoteValue.Quarter, P(Step.F, 5, Accidental.Natural)), N(NoteValue.Quarter, P(Step.F, 5)),
            N(NoteValue.Quarter, P(Step.F, 4))], fifths: 1);
        var bar2 = Bar(2, [N(NoteValue.Whole, P(Step.F, 5))], fifths: 1);

        var (score, _) = ReadingScore.Build([Sys(bar1, bar2)]);

        Assert.Equal([78, 77, 77, 66], NotesOf(score, 1).Select(n => n.Tones[0].Pitch.Midi));
        Assert.Equal(78, NotesOf(score, 2).Single().Tones[0].Pitch.Midi);
    }

    [Fact]
    public void Build_ATiedNoteKeepsItsSharpAcrossTheBarline_ButTheBarsOtherNotesDont()
    {
        var bar1 = Bar(1, [N(NoteValue.Whole, P(Step.F, 4, Accidental.Sharp, tie: true))]);
        var bar2 = Bar(2, [N(NoteValue.Half, P(Step.F, 4)), N(NoteValue.Half, P(Step.F, 4))]);

        var (score, _) = ReadingScore.Build([Sys(bar1), Sys(bar2 with { Index = 1 })]);

        Assert.True(NotesOf(score, 1).Single().Tones[0].TieToNext);
        var held = NotesOf(score, 2).ToList();
        Assert.Equal((66, true), (held[0].Tones[0].Pitch.Midi, held[0].Tones[0].TiedFromPrevious));
        Assert.Equal((65, false), (held[1].Tones[0].Pitch.Midi, held[1].Tones[0].TiedFromPrevious));
    }

    [Fact]
    public void Build_ATieThatLandsNowhereIsDropped()
    {
        var bar1 = Bar(1, [N(NoteValue.Half, P(Step.C, 5, tie: true)), Rest(NoteValue.Half)]);
        var bar2 = Bar(2, [N(NoteValue.Whole, P(Step.E, 5, tie: true))]);

        var (score, _) = ReadingScore.Build([Sys(bar1, bar2)]);

        Assert.All(score.Bars.SelectMany(b => b.Voices).SelectMany(v => v.Events).OfType<Notes>().SelectMany(n => n.Tones), t => Assert.False(t.TieToNext));
    }

    [Fact]
    public void Build_AnOctaveLineRaisesWhatSounds()
    {
        var bar = Bar(1, [N(NoteValue.Whole, P(Step.D, 5)) with { Ottava = 1 }]);

        Assert.Equal(new Spelled(Step.D, 0, 6), NotesOf(ReadingScore.Build([Sys(bar)]).Score, 1).Single().Tones[0].Pitch);
    }

    [Fact]
    public void Build_AShortFirstBarIsAPickup_BarZero()
    {
        var pickup = Bar(1, [N(NoteValue.Quarter, P(Step.G, 4))], [Rest(NoteValue.Quarter)], count: 3);
        var full = Bar(2, [N(NoteValue.Half, P(Step.C, 5)) with { Dots = 1 }], count: 3);

        var (score, firstBars) = ReadingScore.Build([Sys(pickup, full)]);

        Assert.Equal([0, 1], score.Bars.Select(b => b.Number));
        Assert.Equal(480, score.Bars[0].Length);
        Assert.Null(score.Bars[1].Length);
        Assert.Equal([0], firstBars);
    }

    [Fact]
    public void Build_GraceNotesTripletsClefChangesAndRepeats()
    {
        var grace = N(NoteValue.Sixteenth, P(Step.B, 4)) with { Grace = true };
        var triplet = N(NoteValue.Eighth, P(Step.C, 5)) with { Tuplet = 3 };
        var upper = new[] { grace, N(NoteValue.Quarter, P(Step.C, 5)), triplet with { TupletStart = true }, triplet, triplet, N(NoteValue.Half, P(Step.E, 5)) };
        var bar = Bar(1, upper) with { RepeatStart = true, End = BarEnd.RepeatEnd, Ending = 1 };
        bar = bar with { Staves = [bar.Staves[0], bar.Staves[1] with { Clef = ReadClef.Treble }] };

        var built = ReadingScore.Build([Sys(bar)]).Score.Bars.Single();

        var notes = built.Voices.Single(v => v.Staff == 1).Events.OfType<Notes>().ToList();
        Assert.True(notes[0].Grace);
        Assert.Equal((0, 120), (notes[0].Start, notes[0].Length));
        Assert.Equal(0, notes[1].Start);
        Assert.Equal([160, 160, 160], notes.Skip(2).Take(3).Select(n => n.Length));
        Assert.Same(notes[2].Tuplet, notes[4].Tuplet);
        Assert.Equal(960, notes[5].Start);
        Assert.Equal(new ClefChange(0, Clef.Treble), built.Voices.Single(v => v.Staff == 2).Events[0]);
        Assert.True(built.RepeatStart);
        Assert.True(built.RepeatEnd);
        Assert.Equal([1], built.Endings);
    }

    [Fact]
    public void Build_WritesMeiForVerovio()
    {
        var grace = N(NoteValue.Sixteenth, P(Step.B, 4)) with { Grace = true };
        var triplet = N(NoteValue.Eighth, P(Step.C, 5)) with { Tuplet = 3 };
        var bar = Bar(1, [grace, N(NoteValue.Quarter, P(Step.C, 5, tie: true)), N(NoteValue.Quarter, P(Step.C, 5)), triplet with { TupletStart = true }, triplet, triplet,
            N(NoteValue.Quarter, P(Step.G, 4), P(Step.C, 5))]);

        var mei = ReadingScore.Mei(ReadingScore.Build([Sys(bar)]).Score);

        Assert.Contains("grace=\"unacc\"", mei);
        Assert.Contains("<tuplet", mei);
        Assert.Contains("tie=\"i\"", mei);
        Assert.Contains("<chord", mei);
    }

    [Fact]
    public void Build_PutsFingeringOnItsNotes()
    {
        var score = ReadingScore.Build([Sys(Bar(1, [N(NoteValue.Whole, P(Step.C, 4, finger: 1), P(Step.E, 4, finger: 3), P(Step.G, 4))]))]).Score;

        Assert.Equal(new int?[] { 1, 3, null }, NotesOf(score, 1).Single().Tones.Select(t => t.Finger));
        Assert.Equal(2, ReadingScore.Mei(score).Split("<fing ").Length - 1);
    }

    [Fact]
    public void Build_JoinsSlursInTheirVoice_AcrossTheBarline_AndDropsTheOpenEnded()
    {
        // A note can end one slur and start the next, whichever way round they're listed.
        var bar1 = Bar(1,
            [With(N(NoteValue.Quarter, P(Step.C, 5)), ReadMark.SlurStart), N(NoteValue.Quarter, P(Step.D, 5)),
                With(N(NoteValue.Quarter, P(Step.E, 5)), ReadMark.SlurStart, ReadMark.SlurEnd), N(NoteValue.Quarter, P(Step.F, 5))],
            [With(N(NoteValue.Whole, P(Step.C, 3)), ReadMark.SlurEnd)]);
        var bar2 = Bar(2, [With(N(NoteValue.Half, P(Step.G, 5)), ReadMark.SlurEnd), With(N(NoteValue.Half, P(Step.A, 5)), ReadMark.SlurStart)]);

        var score = ReadingScore.Build([Sys(bar1, bar2)]).Score;

        // n1 to n4 are bar 1's upper notes and n5 its lower one; n6 and n7 are bar 2's. The lower slur has no start, A5's no end.
        Assert.Equal([new Mark(MarkKind.Slur, "n1", "n3"), new Mark(MarkKind.Slur, "n3", "n6")], score.FileMarks);
    }

    [Fact]
    public void Build_ASlurCanStartOnAGraceNote_AndAnotherMarkThereGoesToTheNoteItLeadsTo()
    {
        var grace = With(N(NoteValue.Eighth, P(Step.F, 5)) with { Grace = true }, ReadMark.SlurStart, ReadMark.Mf);
        var bar = Bar(1, [grace, With(N(NoteValue.Quarter, P(Step.D, 6)), ReadMark.SlurEnd), Rest(NoteValue.Quarter), N(NoteValue.Half, P(Step.F, 5))]);

        var score = ReadingScore.Build([Sys(bar)]).Score;

        Assert.Equal([new Mark(MarkKind.Dynamic, "n2", null, "mf"), new Mark(MarkKind.Slur, "n1", "n2")], score.FileMarks);
    }

    [Fact]
    public void Build_TiesDynamicsHairpinsAndPedalToNotes_AsAPiecesOwnFileWould()
    {
        var score = ReadingScore.Build([Marked]).Score;

        // A mark at a rest goes to the note before it on its staff, with the ticks between; a pedal change is up, then down.
        Assert.Equal(
        [
            new Mark(MarkKind.Dynamic, "n1", null, "p"),
            new Mark(MarkKind.PedalDown, "n5"),
            new Mark(MarkKind.Dynamic, "n1", null, "f") { StartOffset = 480 },
            new Mark(MarkKind.Dynamic, "n3", null, "sfz"),
            new Mark(MarkKind.PedalUp, "n6"),
            new Mark(MarkKind.PedalDown, "n6"),
            new Mark(MarkKind.Crescendo, "n3", "n7") { EndOffset = 960 },
            new Mark(MarkKind.PedalUp, "n9") { StartOffset = 960 },
            new Mark(MarkKind.Diminuendo, "n7") { StartOffset = 960 },
        ], score.FileMarks);
    }

    [Fact]
    public void Mei_CarriesTheMarks_SoAKeptPieceHasThemToo_AndTheJudgesTimeThemFromTheScore()
    {
        var score = ReadingScore.Build([Marked]).Score;

        var mei = ReadingScore.Mei(score);

        Assert.Equal(score.FileMarks, MeiReader.Read(mei).FileMarks);
        Assert.Contains("dir=\"bounce\"", mei);
        var markings = Markings.Of(score, score.Expected());
        Assert.Equal([new Markings.Dynamic("p", 0, 480), new Markings.Dynamic("f", 480, 2880)], markings.Dynamics);
        Assert.Equal([new Markings.Hairpin(true, 960, 2880)], markings.Hairpins);
        Assert.Equal([new Markings.Pedalled(0, 960), new Markings.Pedalled(960, 2880)], markings.Pedal);
        Assert.Equal(["n3", "n6"], markings.Stressed.Order(StringComparer.Ordinal));
    }

    [Fact]
    public async Task ReadAsync_ReadsTheFirstSystemFirst_ComparesEach_AndBuildsTheNotation()
    {
        var claude = new ScriptedClaude
        {
            Read = r => Ok(Sys(r.First ? CBar : DBar)),
            // The second system's engraving shows D where the print has E: the comparison reads the bar again.
            Compare = c => Ok(new SystemComparison(c.First ? [] : [Bar(1, [N(NoteValue.Whole, P(Step.E, 5))])], [])),
        };
        var engraver = new FakeEngraver();

        var result = await new PdfToNotation(claude).ReadAsync([new SystemImage(1, 2, Png(2300)), new SystemImage(1, 1, Png(2400))], engraver, title: "Étude",
            ct: TestContext.Current.CancellationToken);

        Assert.True(result.Ok);
        Assert.Null(result.StoppedBy);
        Assert.Equal("Read 2 bars from 2 systems.", result.Summary);
        Assert.Equal([72, 76], result.Score!.Bars.Select(b => NotesOf(result.Score, b.Number).Single().Tones[0].Pitch.Midi));
        Assert.Equal([(SystemStep.Done, ""), (SystemStep.Done, "1 bar corrected.")], result.Systems.Select(s => (s.Step, s.Note)));
        Assert.Equal(0.4m, result.Cost);
        Assert.Contains("<title>Étude</title>", result.Mei);
        var reads = claude.Asked.Where(a => a.Task == "prima.read-system").Select(a => (SystemToRead)a.Input).ToList();
        Assert.Equal(new SystemToRead(1, 1, true, 0, 0, ""), reads[0]);
        Assert.Equal(new SystemToRead(1, 2, false, 4, 4, ""), reads[1]);
        Assert.Equal([2400, 2300], engraver.Engraved.Select(e => e.Width));
    }

    [Fact]
    public async Task ReadAsync_KeepsADoubtWithItsBarInThePiece()
    {
        var claude = new ScriptedClaude
        {
            Read = r => Ok(Sys(r.First ? CBar : DBar)),
            Compare = c => Ok(new SystemComparison([], c.First ? [] : [new ReadDoubt(1, 1, "The top note is smudged.")])),
        };

        var result = await new PdfToNotation(claude).ReadAsync([new SystemImage(1, 1, Png(2400)), new SystemImage(1, 2, Png(2400))], new FakeEngraver(),
            ct: TestContext.Current.CancellationToken);

        Assert.True(result.Ok);
        Assert.Equal("Read 2 bars from 2 systems. 1 bar to check.", result.Summary);
        Assert.Equal(new Doubt(1, 2, 2, 1, "The top note is smudged.") { BarInSystem = 1 }, Assert.Single(result.Doubts));
        Assert.Equal(SystemStep.Doubtful, result.Systems[1].Step);
    }

    [Fact]
    public async Task ReadAsync_ADoubtKeepsItsBarInItsSystem_WhileAnEarlierSystemIsMissing()
    {
        var claude = new ScriptedClaude
        {
            Read = r => r.Index == 2 ? Not<SystemReading>(AnswerKind.CheckFailed, "Claude's answer didn't pass the task's checks.") : Ok(Sys(r.First ? CBar : DBar)),
            Compare = c => Ok(new SystemComparison([], c.Index == 3 ? [new ReadDoubt(1, 1, "The top note is smudged.")] : [])),
        };
        SystemImage[] images = [new(1, 1, Png(2400)), new(1, 2, Png(2400)), new(1, 3, Png(2400))];

        var result = await new PdfToNotation(claude).ReadAsync(images, new FakeEngraver(), ct: TestContext.Current.CancellationToken);

        // System 2's bar is missing, so system 3's first bar is bar 2 of what was read; it's still the first of its system.
        Assert.Equal(new Doubt(1, 3, 2, 1, "The top note is smudged.") { BarInSystem = 1 }, Assert.Single(result.Doubts));
    }

    [Fact]
    public async Task ReadAsync_LeavesOutASystemThatCantBeRead_AndNamesIt()
    {
        var claude = new ScriptedClaude { Read = r => r.First ? Ok(Sys(CBar)) : Not<SystemReading>(AnswerKind.CheckFailed, "Claude's answer didn't pass the task's checks.") };

        var result = await new PdfToNotation(claude).ReadAsync([new SystemImage(1, 1, Png(2400)), new SystemImage(2, 1, Png(2400))], new FakeEngraver(),
            ct: TestContext.Current.CancellationToken);

        Assert.False(result.Ok);
        Assert.Equal("1 of 2 systems couldn't be read. Page 2, system 1: Claude's answer didn't pass the task's checks.", result.Summary);
        Assert.Single(result.Score!.Bars);
        Assert.Equal([SystemStep.Done, SystemStep.Failed], result.Systems.Select(s => s.Step));
    }

    [Fact]
    public async Task ReadAsync_StopsWhenTheCreditIsUsedUp()
    {
        var claude = new ScriptedClaude { Read = _ => Not<SystemReading>(AnswerKind.OutOfCredit, "This month's API credits are used up.") };

        var result = await new PdfToNotation(claude).ReadAsync([new SystemImage(1, 1, Png(2400)), new SystemImage(1, 2, Png(2400)), new SystemImage(1, 3, Png(2400))],
            new FakeEngraver(), ct: TestContext.Current.CancellationToken);

        Assert.False(result.Ok);
        Assert.Equal("This month's API credits are used up.", result.Summary);
        Assert.Equal(AnswerKind.OutOfCredit, result.StoppedBy);
        Assert.Single(claude.Asked);
        Assert.All(result.Systems, s => Assert.Equal(SystemStep.Failed, s.Step));
        Assert.Null(result.Score);
    }

    [Fact]
    public async Task RereadAsync_ReadsOneSystemAgainWithTheOwnersHint_AndRebuilds()
    {
        var claude = new ScriptedClaude { Read = r => Ok(Sys(r.First ? CBar : DBar)) };
        var images = new[] { new SystemImage(1, 1, Png(2400)), new SystemImage(1, 2, Png(2400)) };
        var reader = new PdfToNotation(claude);
        var first = await reader.ReadAsync(images, new FakeEngraver(), ct: TestContext.Current.CancellationToken);

        claude.Read = _ => Ok(Sys(Bar(1, [N(NoteValue.Whole, P(Step.F, 5))])));
        var again = await reader.RereadAsync(first with { Took = TimeSpan.FromMinutes(4) }, images[1], new FakeEngraver(),
            hint: "Bar 1: the long note is F5, in the top space", ct: TestContext.Current.CancellationToken);

        Assert.True(again.Ok);
        Assert.Equal(77, NotesOf(again.Score!, 2).Single().Tones[0].Pitch.Midi);
        Assert.Equal(first.Cost + 0.2m, again.Cost);
        Assert.InRange(again.Took, TimeSpan.FromMinutes(4), TimeSpan.FromMinutes(5));
        Assert.Equal(new SystemToRead(1, 2, false, 4, 4, "Bar 1: the long note is F5, in the top space"), claude.Asked.Select(a => a.Input).OfType<SystemToRead>().Last());
        Assert.Equal("Bar 1: the long note is F5, in the top space", claude.Asked.Select(a => a.Input).OfType<SystemToCompare>().Last().Hint);
    }

    [Fact]
    public void Estimate_GivesARangeAndATime()
    {
        var reader = new PdfToNotation(new ScriptedClaude());

        Assert.Equal(new ReadingEstimate(2, 12, 1.80m, 7.20m, TimeSpan.FromMinutes(10)), reader.Estimate(2, 12));
        Assert.Equal(TimeSpan.FromMinutes(5), reader.Estimate(1, 5).About);
    }
}
