using Prima.Core.Generation;
using Prima.Core.Music;
using Prima.Core.Scoring;

namespace Prima.Core.Tests;

public class ScoringTests
{
    private const long Ms = 1_000_000;
    private const double BeatNs = 500 * Ms; // 120 beats a minute
    private static long At(double beats) => (long)(beats * BeatNs);

    // Eight quarters, C D E F G A B C, one a beat, in bars of four.
    private static List<ExpectedNote> Scale() => [.. new[] { 60, 62, 64, 65, 67, 69, 71, 72 }.Select((m, i) => new ExpectedNote($"n{i}", m, i / 4 + 1, 1, i * 480L, 480, null))];

    private static PassageResult Score(IReadOnlyList<ExpectedNote> expected, IEnumerable<PlayedNote> played, double offsetMs = 0, IReadOnlySet<string>? ornamented = null) =>
        PassageScorer.Score(expected, At, 480, BeatNs, 4 * BeatNs, [.. played], offsetMs, ornamented);

    private static PlayedNote P(int midi, double beats, double offsetMs = 0) => new(midi, At(beats) + (long)(offsetMs * Ms), At(beats) + (long)(offsetMs * Ms) + 400 * Ms, 70);

    [Fact]
    public void APerfectPassageIsAllInTimeAndClean()
    {
        var result = Score(Scale(), Scale().Select((e, i) => P(e.Midi, i)));

        Assert.Equal(1, result.InTimeAccuracy);
        Assert.Equal(8, result.InTime);
        Assert.True(result.Clean);
        Assert.True(result.KeptGoing);
        Assert.Equal(0, result.LeanMs, 6);
        Assert.Null(result.WorstBar);
        Assert.Equal(0, result.TempoDriftPercent!.Value, 6);
    }

    [Fact]
    public void ASteadyEarlyLeanIsShownOnItsOwnAndTakenOffBeforeGrading()
    {
        var result = Score(Scale(), Scale().Select((e, i) => P(e.Midi, i, -40)));

        Assert.Equal(-40, result.LeanMs, 6);
        Assert.Equal(-8, result.LeanPercent, 6);
        Assert.Equal(8, result.InTime);
        Assert.Equal(0, result.SteadinessPercent, 6);
    }

    [Fact]
    public void TimingIsGradedInBandsOfTheBeat()
    {
        // After a lean of 0: +4% in time, +8% audible, +16% off.
        double[] offsets = [0, 0, 20, -20, 40, -40, 80, -80];
        var result = Score(Scale(), Scale().Select((e, i) => P(e.Midi, i, offsets[i])));

        Assert.Equal([Band.InTime, Band.InTime, Band.InTime, Band.InTime, Band.Audible, Band.Audible, Band.Off, Band.Off], result.Notes.Select(n => n.Band!.Value));
        Assert.Equal(6 / 8.0, result.InTimeAccuracy);
        Assert.Equal(2, result.Audible);
    }

    [Fact]
    public void TheCalibrationOffsetComesOffEveryNote()
    {
        var result = Score(Scale(), Scale().Select((e, i) => P(e.Midi, i, 30)), offsetMs: 30);

        Assert.Equal(0, result.LeanMs, 6);
    }

    [Fact]
    public void WrongNotesAreNamedAndSorted()
    {
        var played = Scale().Select((e, i) => P(e.Midi, i)).ToList();
        played[1] = P(63, 1); // D4 for E♭4: a semitone, lost on the keyboard
        played[3] = P(77, 3); // F5 for F4: an octave
        played[5] = P(74, 5); // A4 played as D5: a reading error
        var result = Score(Scale(), played);

        Assert.Equal(3, result.Wrong);
        Assert.Equal((WrongKind.LostOnKeyboard, 63), (result.Notes[1].Wrong!.Value, result.Notes[1].Played!.Value));
        Assert.Equal(WrongKind.Octave, result.Notes[3].Wrong);
        Assert.Equal(WrongKind.Reading, result.Notes[5].Wrong);
        Assert.Equal(1, result.LostOnKeyboard);
        Assert.Equal((3, 3), (result.ZhukovMissed, result.ZhukovExtra));
        Assert.False(result.Clean);
    }

    [Fact]
    public void ExtraAndMissedNotesAreCounted()
    {
        var played = Scale().Select((e, i) => P(e.Midi, i)).Where((_, i) => i != 6).Append(P(50, 2.5)).ToList();
        var result = Score(Scale(), played);

        Assert.Equal(1, result.Missed);
        Assert.Equal(Outcome.Missed, result.Notes[6].Outcome);
        Assert.Equal(1, result.Extra);
        Assert.Equal(2, result.WorstBar);
    }

    [Fact]
    public void StoppingForMoreThanABarIsNotKeepingGoing()
    {
        // Two bars of quarters; nothing played in beats 2 to 7.
        var expected = Enumerable.Range(0, 8).Select(i => new ExpectedNote($"n{i}", 60, i / 4 + 1, 1, i * 480L, 480, null)).ToList();
        var result = Score(expected, [P(60, 0), P(60, 1), P(60, 7)]);

        Assert.False(result.KeptGoing);
    }

    [Fact]
    public void AHeldNoteIsNotAStop()
    {
        var expected = new List<ExpectedNote> { new("a", 60, 1, 1, 0, 1920, null), new("b", 62, 2, 1, 1920, 1920, null), new("c", 64, 3, 1, 3840, 480, null) };
        var result = Score(expected, [P(60, 0), P(62, 4), P(64, 8)]);

        Assert.True(result.KeptGoing);
    }

    [Fact]
    public void PlayingSlowerShowsAsDrift()
    {
        // Each note a little later than the last: played at 110% of the written length, about 9% slower.
        var result = Score(Scale(), Scale().Select((e, i) => P(e.Midi, i * 1.02)));

        Assert.True(result.TempoDriftPercent < -1);
        Assert.True(result.TempoDriftPercent > -3);
    }

    [Fact]
    public void AChordsNotesAreScoredEachAndHowTogetherTheyLanded()
    {
        var expected = new List<ExpectedNote> { new("c0", 60, 1, 1, 0, 960, "c"), new("c1", 64, 1, 1, 0, 960, "c"), new("c2", 67, 1, 1, 0, 960, "c") };
        var result = Score(expected, [P(60, 0), P(64, 0, 12), P(67, 0, 30)]);

        Assert.Equal(3, result.Right);
        Assert.Equal(30, result.ChordSpreadMs!.Value, 6);
    }

    [Theory]
    [InlineData(60.0, 300)]
    [InlineData(120.0, 200)]
    [InlineData(200.0, 120)]
    public void TheWindowScalesWithTempo(double bpm, double windowMs)
    {
        Assert.Equal(windowMs, Grading.WindowNs(60e9 / bpm) / 1e6, 6);
    }

    [Fact]
    public void ANoteOutsideTheWindowIsMissedAndExtra()
    {
        var played = Scale().Select((e, i) => P(e.Midi, i)).ToList();
        played[2] = P(64, 2, 260); // 260 ms late at 120 bpm: outside 200 ms
        var result = Score(Scale(), played);

        Assert.Equal(Outcome.Missed, result.Notes[2].Outcome);
        Assert.Equal(1, result.Extra);
    }

    [Fact]
    public void ATrillsNotesAreNotExtra()
    {
        var expected = new List<ExpectedNote> { new("t", 67, 1, 1, 0, 1920, null), new("u", 65, 2, 1, 1920, 480, null) };
        var trill = Enumerable.Range(0, 10).Select(i => P(i % 2 == 0 ? 67 : 69, i * 0.3)).Append(P(65, 4));
        var result = Score(expected, trill, ornamented: new HashSet<string> { "t" });

        Assert.Equal(0, result.Extra);
        Assert.Equal(2, result.Right);
    }

    [Fact]
    public void RhythmGradesLeanSteadinessAndDriftAgainstTheBeat()
    {
        // A bar of beat, then two bars of quarters; taps 5% of a beat late in the rhythm, and one missed, one extra.
        var onsets = Enumerable.Range(0, 12).Select(i => At(i)).ToList();
        var ids = onsets.Select((_, i) => $"r{i}").ToList();
        var taps = onsets.Select((t, i) => i < 4 ? t : t + 25 * Ms).Where((_, i) => i != 9).Append(At(10.5)).ToList();
        var result = RhythmScorer.Score(onsets, ids, 4, taps, BeatNs, 4 * BeatNs, 0);

        Assert.Equal(5, result.LeanPercent, 6);
        Assert.Equal(0, result.SteadinessPercent, 6);
        Assert.Equal(0, result.PulseLeanPercent, 6);
        Assert.Equal(1, result.Missed);
        Assert.Equal(1, result.Extra);
        Assert.Equal(11 / 12.0, result.Accuracy, 6);
    }

    [Fact]
    public void DrillAnswersAreTheFirstNotesAfterShowing()
    {
        var shown = 1000 * Ms;
        var note = DrillScorer.Note(60, [new PlayedNote(62, shown + 900 * Ms, 0, 70)], shown)!;
        Assert.False(note.Right);
        Assert.True(note.LostOnKeyboard);
        Assert.Equal(900, note.ResponseMs, 6);

        var chord = DrillScorer.Chord([60, 64, 67], [new PlayedNote(60, shown + 800 * Ms, 0, 70), new PlayedNote(64, shown + 860 * Ms, 0, 70), new PlayedNote(67, shown + 1000 * Ms, 0, 70)], shown)!;
        Assert.False(chord.Right);
        Assert.Equal(1, chord.Missing);
        Assert.Equal(60, chord.SpreadMs, 6);

        var figure = DrillScorer.Figure([67, 71, 74], [new PlayedNote(65, shown + 700 * Ms, 0, 70), new PlayedNote(69, shown + 900 * Ms, 0, 70), new PlayedNote(72, shown + 1100 * Ms, 0, 70)], shown)!;
        Assert.True(figure.WrongFirstNote);
        Assert.Equal(0, figure.WrongIntervals);
    }

    [Fact]
    public void ForgettingTheKeyIsCountedPerAffectedNote()
    {
        // G major: F♯ played as F.
        var expected = new List<ExpectedNote> { new("a", 67, 1, 1, 0, 480, null), new("b", 66, 1, 1, 480, 480, null), new("c", 67, 1, 1, 960, 480, null) };
        var result = Score(expected, [P(67, 0), P(65, 1), P(67, 2)]);

        Assert.Equal(1, DrillScorer.ForgotTheKey(result, new HashSet<string> { "b" }, new Key(1), expected));
    }

    [Fact]
    public void WaitForMeMovesOnOnlyWhenAStepsKeysAreAllPlayed()
    {
        // C, then a chord E–G, then C again.
        var judge = new WaitJudge([new("a", 60, 1, 1, 0, 480, null), new("b", 64, 1, 1, 480, 480, "ch"), new("c", 67, 1, 1, 480, 480, "ch"), new("d", 72, 1, 1, 960, 480, null)]);

        Assert.True(judge.Play(60, 0).Moved);
        Assert.Equal(["b", "c"], judge.Current.Select(n => n.Id));
        Assert.False(judge.Play(67, 1000 * Ms).Moved);
        Assert.Empty(judge.Play(67, 1100 * Ms).Marks);
        Assert.True(judge.Play(64, 1200 * Ms).Moved);
        Assert.True(judge.Play(72, 3000 * Ms).Moved);
        Assert.True(judge.Done);
        var result = judge.Result();
        Assert.Equal(4, result.RightFirstTime);
        Assert.True(result.Finished);
        Assert.Equal(80, result.NotesPerMinute, 6);
    }

    [Fact]
    public void WaitForMeMarksASlipWhereItWasAndKeepsWaiting()
    {
        var judge = new WaitJudge(Scale().Take(3));
        judge.Play(60, 0);
        var (marks, moved) = judge.Play(63, 500 * Ms);

        Assert.False(moved);
        Assert.Equal(new LiveMark("n1", LiveKind.Wrong, 63, 0), Assert.Single(marks));
        // Put right, it moves on but keeps its wrong mark.
        Assert.Equal((0, true), (judge.Play(62, 900 * Ms).Marks.Count, judge.Step == 2));
        judge.Play(64, 1400 * Ms);
        Assert.Equal((3, 2, 1), (judge.Result().Notes, judge.Result().RightFirstTime, judge.Result().WrongKeys));
    }

    [Fact]
    public void WaitForMeLetsAnOrnamentsNotesBe()
    {
        // D with a mordent (D C D), then E: the C and the second D aren't slips.
        var judge = new WaitJudge([new("d", 62, 1, 1, 0, 480, null), new("e", 64, 1, 1, 480, 480, null)], new HashSet<string> { "d" });
        judge.Play(62, 0);
        judge.Play(60, 60 * Ms);
        judge.Play(62, 120 * Ms);
        judge.Play(64, 500 * Ms);

        Assert.Equal(0, judge.Result().WrongKeys);
        Assert.True(judge.Done);
    }

    private static TechniqueItem CMajor(HandUse hands = HandUse.Right) => Technique.Scale(new Key(0), ScaleKind.Major, 1, hands);

    private static List<PlayedNote> Run(IReadOnlyList<TechniqueNote> notes, Func<int, double> gapMs, double start = 0, Func<int, int>? velocity = null)
    {
        var at = start;
        var played = new List<PlayedNote>();
        for (var i = 0; i < notes.Count; i++)
        {
            played.Add(new PlayedNote(notes[i].Midi, (long)(at * Ms), (long)((at + gapMs(i) + 10) * Ms), velocity?.Invoke(i) ?? 70));
            at += gapMs(i);
        }
        return played;
    }

    [Fact]
    public void AnEvenScaleHasNoSpreadAndItsOwnTempo()
    {
        var item = CMajor();
        var result = TechniqueScorer.Score(item, Run(item.Right, _ => 250));

        Assert.True(result.Valid);
        Assert.Equal(0, result.Right!.IoiSdMs, 6);
        Assert.Equal(120, result.Right.Bpm, 6);
        Assert.Equal(10, result.Right.LegatoMs, 6);
    }

    [Fact]
    public void ASteadySpeedUpIsNotUnevenness()
    {
        var item = CMajor();
        var result = TechniqueScorer.Score(item, Run(item.Right, i => 260 - i * 2));

        Assert.Equal(0, result.Right!.IoiSdMs, 6);
        Assert.True(result.Right.PlainIoiSdMs > 5);
        Assert.Equal(-2, result.Right.SlopeMsPerNote, 6);
    }

    [Fact]
    public void ALateThumbCrossingShowsOnItsOwn()
    {
        var item = CMajor();
        // Right hand C major: 1 2 3 1 ... the thumb crosses under after the third note (E to F) going up.
        var crossings = item.Right.Where(n => n.CrossingNext).Select(n => n.Index).ToHashSet();
        var result = TechniqueScorer.Score(item, Run(item.Right, i => crossings.Contains(i) ? 280 : 250));

        Assert.Contains(2, crossings);
        Assert.True(result.Right!.CrossingMs > 20);
        Assert.True(result.Right.IoiSdMs > 5);
    }

    [Fact]
    public void AWrongNoteMakesARunInvalid()
    {
        var item = CMajor();
        var played = Run(item.Right, _ => 250);
        played[4] = played[4] with { Midi = 68 };
        var result = TechniqueScorer.Score(item, played);

        Assert.False(result.Valid);
        Assert.False(result.Right!.Valid);
    }

    [Fact]
    public void OneWrongKeyIsOneWrongNote()
    {
        var item = CMajor();
        var played = Run(item.Right, _ => 250);
        played[4] = played[4] with { Midi = 68 };
        var run = TechniqueScorer.Score(item, played).Right!;

        Assert.Equal(1, run.Wrong);
        Assert.Equal(0, run.Missed);
        Assert.Empty(run.Extras);
        Assert.Equal(new RunNote("r4", 67, Outcome.Wrong, 68), run.Notes[4]);
        Assert.All(run.Notes.Where(n => n.Id != "r4"), n => Assert.Equal(Outcome.Right, n.Outcome));
    }

    [Fact]
    public void KeysAStepAheadInTheNotesPlacesAreWrongNotes()
    {
        var item = CMajor();
        var played = Run(item.Right, _ => 250);
        // E and F played as F and G, then G again: two wrong notes, not a missed E and an extra G.
        played[2] = played[2] with { Midi = 65 };
        played[3] = played[3] with { Midi = 67 };
        var run = TechniqueScorer.Score(item, played).Right!;

        Assert.Equal(2, run.Wrong);
        Assert.Equal(0, run.Missed);
        Assert.Empty(run.Extras);
    }

    [Fact]
    public void ASkippedNoteAndAnExtraKeyAreFoundWhereTheyHappened()
    {
        var item = CMajor();
        var played = Run(item.Right, _ => 250);
        // A4 (r5) skipped, and C♯5 brushed just after the top C (r7).
        played.RemoveAt(5);
        played.Insert(7, new PlayedNote(73, played[6].OnNs + 100 * Ms, played[6].OnNs + 150 * Ms, 70));
        var run = TechniqueScorer.Score(item, played).Right!;

        Assert.False(run.Valid);
        Assert.Equal(0, run.Wrong);
        Assert.Equal(1, run.Missed);
        Assert.Equal(Outcome.Missed, run.Notes[5].Outcome);
        Assert.Equal(new ExtraKey("r7", 73), Assert.Single(run.Extras));
    }

    [Fact]
    public void KeysBeforeTheFirstNoteAndAfterTheLastAreNotPartOfTheRun()
    {
        var item = CMajor();
        var played = Run(item.Right, _ => 250, start: 1000);
        // A key in the count-in, and the piano's button key pressed just after the last note.
        played.Add(new PlayedNote(61, 200 * Ms, 300 * Ms, 70));
        played.Add(new PlayedNote(61, played[14].OnNs + 400 * Ms, played[14].OnNs + 500 * Ms, 70));

        Assert.True(TechniqueScorer.Score(item, played).Valid);
    }

    [Fact]
    public void HandsTogetherAWrongKeyGoesToTheNearerHand()
    {
        var item = CMajor(HandUse.Together);
        var right = Run(item.Right, _ => 250);
        var left = Run(item.Left, _ => 250, start: 10);
        left[3] = left[3] with { Midi = left[3].Midi + 1 };
        var result = TechniqueScorer.Score(item, [.. right, .. left]);

        Assert.True(result.Right!.Valid);
        Assert.Equal(1, result.Left!.Wrong);
        Assert.Equal(Outcome.Wrong, result.Left.Notes[3].Outcome);
    }

    [Fact]
    public void HandsTogetherAreSyncedAndTouchIsMeasured()
    {
        var item = CMajor(HandUse.Together);
        var right = Run(item.Right, _ => 250, velocity: i => i % 2 == 0 ? 60 : 80);
        var left = Run(item.Left, _ => 250, start: 15);
        var result = TechniqueScorer.Score(item, [.. right, .. left]);

        Assert.True(result.Valid);
        Assert.Equal(-15, result.SyncMeanMs!.Value, 6);
        Assert.True(result.Right!.VelocitySd > 9);
        // The thumb plays notes 0, 3, 11 and 14 of the run: 60, 80, 80 and 60.
        Assert.Equal(70, result.Right.VelocityByFinger[1], 6);
        Assert.Equal(5, result.Right.VelocityByFinger.Count);
    }

    [Fact]
    public void SmallDifferencesAreDescribedPlainly()
    {
        Assert.Equal("below what anyone can hear", TechniqueScorer.Describe(7, null));
        Assert.Equal("within the keyboard's noise", TechniqueScorer.Describe(3, 4));
        Assert.Equal("14 ms", TechniqueScorer.Describe(14, 4));
        var (consistent, random) = TechniqueScorer.Consistency([[10, -5, 0], [10, -5, 0]]);
        Assert.Equal(Math.Sqrt((100 + 25) / 3.0), consistent, 6);
        Assert.Equal(0, random, 6);
    }
}

public class LiveJudgeTests
{
    private const double BeatNs = 1e9;

    private static ExpectedNote N(string id, int midi, int beat) => new(id, midi, 1, 1, beat * Ticks.Quarter, Ticks.Quarter, null);

    private static long At(double beats) => (long)(beats * BeatNs);

    [Fact]
    public void AKeyOnTimeIsRightAndOffTheBeatIsEarlyOrLate()
    {
        var judge = new LiveJudge([N("a", 60, 0), N("b", 62, 1), N("c", 64, 2)], At, Ticks.Quarter, BeatNs, 0);

        Assert.Equal(LiveKind.Right, judge.Play(60, At(0.05))!.Kind);
        Assert.Equal(LiveKind.Early, judge.Play(62, At(0.8))!.Kind);
        var late = judge.Play(64, At(2.2))!;
        Assert.Equal((LiveKind.Late, "c"), (late.Kind, late.Id));
        Assert.Equal(20, late.ErrorPercent, 3);
    }

    [Fact]
    public void AWrongKeyIsMarkedAndPutRightByTheRightKeyAMomentLater()
    {
        var judge = new LiveJudge([N("a", 60, 0), N("b", 62, 1)], At, Ticks.Quarter, BeatNs, 0);

        var slip = judge.Play(61, At(0))!;
        Assert.Equal((LiveKind.Wrong, "a", 61), (slip.Kind, slip.Id, slip.Played));
        Assert.Equal(LiveKind.Right, judge.Play(60, At(0.08))!.Kind);
        // The next note is untouched by the slip, and a wrong note isn't also counted missed.
        Assert.Equal(LiveKind.Right, judge.Play(62, At(1))!.Kind);
        Assert.Empty(judge.Due(At(3)));
    }

    [Fact]
    public void ANoteWithNothingPlayedIsMissedOnceItsWindowCloses()
    {
        var judge = new LiveJudge([N("a", 60, 0), N("b", 62, 1)], At, Ticks.Quarter, BeatNs, 0);

        Assert.Empty(judge.Due(At(0.2)));
        Assert.Equal(["a"], judge.Due(At(0.5)).Select(m => m.Id));
        Assert.Null(judge.Play(70, At(5)));
    }

    [Fact]
    public void AWrongKeyInAChordGoesToTheNoteNearestInPitch()
    {
        var chord = new[] { N("low", 48, 0) with { ChordId = "c" }, N("mid", 52, 0) with { ChordId = "c" }, N("high", 55, 0) with { ChordId = "c" } };
        var judge = new LiveJudge(chord, At, Ticks.Quarter, BeatNs, 0);

        Assert.Equal(LiveKind.Right, judge.Play(48, At(0.01))!.Kind);
        Assert.Equal("mid", judge.Play(53, At(0.02))!.Id);
    }

    [Fact]
    public void TheTimingOffsetIsTakenOffWhatIsPlayed()
    {
        // The piano's notes arrive 30 ms late: a note 30 ms after its time is on it.
        var judge = new LiveJudge([N("a", 60, 0)], At, Ticks.Quarter, BeatNs, 30);

        Assert.Equal(0, judge.Play(60, 30_000_000)!.ErrorPercent, 6);
    }

    [Fact]
    public void TheClicksTimesAreAskedForAsEachKeyComes()
    {
        // The click's beat times settle once its audio is running: a note is judged against them as they are when it's played.
        var settled = 0L;
        var judge = new LiveJudge([N("a", 60, 1)], b => At(b) + settled, Ticks.Quarter, BeatNs, 0);
        settled = 200_000_000;

        Assert.Equal(LiveKind.Right, judge.Play(60, At(1) + 200_000_000)!.Kind);
    }
}

public class RhythmByBarTests
{
    [Fact]
    public void EachBarSaysWhereItsTapsLandedAndWhatWasMissed()
    {
        // A beat of a second: bar 1 on the beat, bar 2 a fifth of a beat late, bar 3 one tapped early and one missed, bar 4 on the
        // beat on average but with one note late and one early.
        NoteResult Tapped(string id, double ms) => new(id, 0, 0, Outcome.Right, ms, ms / 10, Grading.BandOf(ms / 10));
        NoteResult[] notes = [Tapped("a", 10), Tapped("b", -10), Tapped("c", 180), Tapped("d", 220), Tapped("e", -150), new("f", 0, 0, Outcome.Missed), Tapped("g", 150), Tapped("h", -140)];

        var bars = RhythmScorer.ByBar(notes, [1, 1, 2, 2, 3, 3, 4, 4], 1e9);

        Assert.Equal([1, 2, 3, 4], bars.Select(b => b.Bar));
        Assert.Equal((0.0, 0, 0, false), (Math.Round(bars[0].Percent!.Value, 6), bars[0].Early, bars[0].Late, bars[0].Off));
        Assert.Equal((20.0, 2, true), (Math.Round(bars[1].Percent!.Value, 6), bars[1].Late, bars[1].Off));
        Assert.Equal((-15.0, 1, 1), (Math.Round(bars[2].Percent!.Value, 6), bars[2].Early, bars[2].Missed));
        Assert.Equal((1, 1, true), (bars[3].Early, bars[3].Late, bars[3].Off));
        Assert.Null(RhythmScorer.ByBar([new NoteResult("x", 0, 0, Outcome.Missed)], [5], 1e9)[0].Percent);
    }
}
