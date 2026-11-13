using Prima.Accuracy;
using Prima.Core.Library;
using Prima.Core.Music;
using Prima.Core.Transcription;

namespace Prima.Core.Tests;

public class AccuracyTests
{
    private static AccuracyReference Reference(params ReferenceBar[] bars) => new("synthetic", "", "synthetic", [], bars);

    [Fact]
    public void ReferenceEventsPreserveChordsRestsAndFractionalDurations()
    {
        var reference = Reference(new ReferenceBar(1, 1, 1, "treble", "C4+E4:1.5 r:0.5 G4:2", "C3:4"));

        var expected = AccuracyEvaluator.Expected(reference);

        Assert.Equal([
            new MeasuredNote(1, 1, 0, 720, 60), new MeasuredNote(1, 1, 0, 720, 64),
            new MeasuredNote(1, 1, 960, 960, 67), new MeasuredNote(1, 2, 0, 1920, 48),
        ], expected.Notes);
        Assert.Equal(66, AccuracyEvaluator.Midi("F#4"));
        Assert.Equal(58, AccuracyEvaluator.Midi("Bb3"));
    }

    [Fact]
    public void InvalidReferenceTimingIsRejected()
    {
        var reference = Reference(new ReferenceBar(1, 1, 1, "treble", "C4:3", "r:4"));
        Assert.Throws<FormatException>(() => AccuracyEvaluator.Expected(reference));
    }

    [Fact]
    public void DuplicateAndWrongDurationNotesCannotIncreaseExactAccuracy()
    {
        var reference = Reference(new ReferenceBar(1, 1, 1, "treble", "C4:4", "C3:4"));
        var actual = new CandidateNotes([1], [new(1, 1, 0, 1920, 60), new(1, 1, 0, 1920, 60), new(1, 2, 0, 960, 48)]);

        var report = AccuracyEvaluator.Compare(reference, actual, "synthetic");

        Assert.Equal(1, report.ExactMatches);
        Assert.Equal(2, report.PitchAtOnsetMatches);
        Assert.Equal(1, report.RhythmMatches);
        Assert.Single(report.MissingNotes);
        Assert.Equal(2, report.ExtraNotes.Count);
        Assert.Equal(0.5, report.Recall);
        Assert.Equal(1d / 3, report.Precision);
    }

    [Fact]
    public void SourceBarCoverageIncludesMissingExtraAndDuplicateBars()
    {
        var reference = Reference(new ReferenceBar(1, 1, 1, "treble", "C4:4", "r:4"), new(2, 1, 2, "treble", "D4:4", "r:4"));

        var report = AccuracyEvaluator.Compare(reference, new CandidateNotes([1, 1, 3], []), "synthetic");

        Assert.Equal([2], report.MissingBars);
        Assert.Equal([3], report.ExtraBars);
        Assert.Equal([1], report.DuplicateBars);
        Assert.Equal(0, report.ExactMatches);
    }

    private static ReadEvent Note(Step step, Accidental accidental) => new(ReadKind.Note,
        [new ReadPitch(step, accidental, 4, false, 0)], NoteValue.Quarter, 0, 0, false, false, Articulation.None, [], 0, ReadClef.None);

    [Fact]
    public void RawReadingAccidentalsAreResolvedWithoutReadingScore()
    {
        var reference = Reference(new ReferenceBar(1, 1, 1, "treble", "F#4:1 F4:1 F4:1 F#4:1", "r:4"));
        var events = new[] { Note(Step.F, Accidental.None), Note(Step.F, Accidental.Natural), Note(Step.F, Accidental.None), Note(Step.F, Accidental.Sharp) };
        SystemReading[] readings = [new([new ReadBar(1, 2, 4, 4, false, BarEnd.Normal, 0,
            [new ReadStaff(1, ReadClef.Treble, [new ReadVoice(1, events)])])], [])];

        var report = AccuracyEvaluator.Compare(reference, AccuracyEvaluator.Raw(reference, readings), "raw");

        Assert.Equal(4, report.ExactMatches);
        Assert.Empty(report.MissingNotes);
        Assert.Empty(report.ExtraNotes);
    }

    [Fact]
    public void MissingSystemDoesNotShiftLaterRawSourceBarIdentities()
    {
        var reference = Reference(new ReferenceBar(1, 1, 1, "treble", "C4:4", "r:4"), new(2, 2, 1, "treble", "F4:4", "r:4"));
        var held = Note(Step.F, Accidental.None) with { Value = NoteValue.Whole };
        SystemReading?[] readings = [null, new([new ReadBar(1, 0, 4, 4, false, BarEnd.Normal, 0,
            [new ReadStaff(1, ReadClef.Treble, [new ReadVoice(1, [held])])])], [])];

        var report = AccuracyEvaluator.Compare(reference, AccuracyEvaluator.Raw(reference, readings), "raw");

        Assert.Equal([1], report.MissingBars);
        Assert.Equal(1, report.ExactMatches);
        Assert.Equal(2, Assert.Single(AccuracyEvaluator.Raw(reference, readings).Notes).Bar);
    }

    [Fact]
    public void IndependentRawAndConvertedStagesAgreeForKnownSyntheticMusic()
    {
        var reference = Reference(new ReferenceBar(1, 1, 1, "treble", "C4:4", "C3:4"));
        var upper = Note(Step.C, Accidental.None) with { Value = NoteValue.Whole };
        var lower = upper with { Pitches = [new ReadPitch(Step.C, Accidental.None, 3, false, 0)] };
        SystemReading[] readings = [new([new ReadBar(1, 0, 4, 4, false, BarEnd.Normal, 0,
            [new ReadStaff(1, ReadClef.Treble, [new ReadVoice(1, [upper])]), new ReadStaff(2, ReadClef.Bass, [new ReadVoice(1, [lower])])])], [])];
        var score = ReadingScore.Build(readings).Score;
        CandidateNotes[] stages = [AccuracyEvaluator.Raw(reference, readings), AccuracyEvaluator.Canonical(score),
            AccuracyEvaluator.Canonical(MeiReader.Read(ReadingScore.Mei(score))), AccuracyEvaluator.Playback(score)];

        Assert.All(stages, stage =>
        {
            var report = AccuracyEvaluator.Compare(reference, stage, "synthetic");
            Assert.Equal(2, report.ExactMatches);
            Assert.Empty(report.MissingNotes);
            Assert.Empty(report.ExtraNotes);
        });
    }

    [Fact]
    public void MidiUsesMusicalTicksAndDoesNotGuessTheStaffFromTrack()
    {
        var midi = new MidiFile([], 50, 4, 4, 9.6, 2)
        {
            Division = 960,
            Ticked = [new TickNote(7, 0, 60, 0, 960, 80), new TickNote(4, 3, 62, 3840, 5760, 60)],
        };

        var notes = AccuracyEvaluator.MidiNotes(midi);

        Assert.Equal([new MeasuredNote(1, 0, 0, 480, 60), new MeasuredNote(2, 0, 0, 960, 62)], notes.Notes);
    }
}
