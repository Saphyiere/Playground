using Prima.Core.Generation;
using Prima.Core.Music;
using Prima.Core.Practice;
using Prima.Core.Scoring;

namespace Prima.Core.Tests;

/// <summary>
/// The review after a run (concept, "A run's review"): what you did in a line and one thing for next time, from what the run's
/// scorer measured, the first thing to fix in the review's order, and a hand named only when both played.
/// </summary>
public class RunReviewTests
{
    private const long Ms = 1_000_000;
    private const double BeatNs = 500 * Ms; // 120 beats a minute, a quarter note a beat

    private static long At(double beats) => (long)(beats * BeatNs);

    // A five-finger pattern in quarters, four to a bar, C D E F G F E D and round again; fingers 1 2 3 4 5 4 3 2.
    private static readonly int[] Pattern = [60, 62, 64, 65, 67, 65, 64, 62];
    private static readonly int[] Fingers = [1, 2, 3, 4, 5, 4, 3, 2];

    private static List<ExpectedNote> Notes(int count = 16, int staff = 1, int transpose = 0, Articulation touch = Articulation.None, string? slur = null) =>
        [.. Enumerable.Range(0, count).Select(i => new ExpectedNote($"{(staff == 1 ? "r" : "l")}{i}", Pattern[i % 8] + transpose, i / 4 + 1, staff, i * 480L, 480, null)
        {
            Finger = Fingers[i % 8],
            Articulation = touch,
            Lifted = touch == Articulation.Staccato,
            Slur = slur,
        })];

    // Each note as played: ms after its beat, its key down for this much of a beat (1.05 runs into the next), the key (the one
    // written unless said), how hard.
    private static List<PlayedNote> Play(IReadOnlyList<ExpectedNote> notes, Func<int, double>? late = null, double hold = 1.05, Func<int, int>? key = null, Func<int, int>? velocity = null) =>
        [.. notes.Select((n, i) =>
        {
            var on = At(n.Tick / 480.0) + (long)((late?.Invoke(i) ?? 0) * Ms);
            return new PlayedNote(key?.Invoke(i) ?? n.Midi, on, on + (long)(hold * BeatNs), velocity?.Invoke(i) ?? 70);
        })];

    private static PassageResult Score(IReadOnlyList<ExpectedNote> notes, IEnumerable<PlayedNote> played) =>
        PassageScorer.Score(notes, At, 480, BeatNs, 4 * BeatNs, [.. played], 0);

    private static RunReview Review(IReadOnlyList<ExpectedNote> notes, IEnumerable<PlayedNote> played, Key? key = null, IReadOnlyList<PiecePass>? passes = null, string? stoppedAt = null) =>
        RunReviews.InTime(Score(notes, played), key, passes, stoppedAt);

    [Fact]
    public void ACleanRunIsEveryNoteRightSteadyAndLegatoAndNextALittleFaster()
    {
        var notes = Notes();
        var review = Review(notes, Play(notes, i => i % 3 - 1));

        Assert.Equal("Every note right, steady and legato.", review.Did);
        Assert.Equal("A little faster.", review.Next);
        Assert.Equal(ReviewFocus.None, review.Focus);
    }

    [Fact]
    public void PlayingFasterThanTheClickIsGettingAheadOfIt()
    {
        var notes = Notes();
        // Each beat 10 ms sooner than the one before: 150 ms ahead by the last note, 30% of a beat.
        var review = Review(notes, Play(notes, i => -10.0 * i));

        Assert.Equal("Every note right, but you got ahead of the click as you went.", review.Did);
        Assert.Equal("Stay with the click right to the end.", review.Next);
        Assert.Equal(ReviewFocus.Ahead, review.Focus);
    }

    [Fact]
    public void PlayingSlowerThanTheClickIsFallingBehindIt()
    {
        var notes = Notes();
        var review = Review(notes, Play(notes, i => 10.0 * i));

        Assert.Equal("Every note right, but you fell behind the click as you went.", review.Did);
        Assert.Equal("Keep up with the click; a little slower if you need.", review.Next);
    }

    [Fact]
    public void UnevenGapsAreSaidThoughNoNoteIsOffTheClick()
    {
        var notes = Notes();
        // Every other note 70 ms late: no note a tenth of a beat out once the lean is taken off, but every gap 14% long or short.
        var result = Score(notes, Play(notes, i => i % 2 * 70));
        var review = RunReviews.InTime(result);

        Assert.Equal(0, result.Off);
        Assert.Equal("Every note right, but the gaps between them were uneven.", review.Did);
        Assert.Equal("Listen for even gaps; slower helps.", review.Next);
        Assert.Equal(ReviewFocus.Uneven, review.Focus);
    }

    [Fact]
    public void ALittleUnevenIsSaidWhenThereIsNothingMore()
    {
        var notes = Notes();
        var review = Review(notes, Play(notes, i => i % 2 * 50));

        Assert.Equal("Every note right, legato, but a little uneven.", review.Did);
        Assert.Equal("Listen for even gaps between the notes.", review.Next);
    }

    [Fact]
    public void KeysLetUpLongBeforeTheNextAreDetached()
    {
        var notes = Notes();
        var review = Review(notes, Play(notes, hold: 0.5));

        Assert.Equal("Every note right, but detached: each key came up well before the next.", review.Did);
        Assert.Equal("Hold each note until the next one sounds.", review.Next);
        Assert.Equal(ReviewFocus.Legato, review.Focus);
    }

    [Fact]
    public void KeysLetUpABitEarlyAreALittleShortOfLegato()
    {
        var notes = Notes();
        var review = Review(notes, Play(notes, hold: 0.85));

        Assert.Equal("Every note right, steady, but a little short of legato.", review.Did);
        Assert.Equal("Let each key up only as the next goes down.", review.Next);
    }

    [Fact]
    public void StaccatoNotesAndAKeyStruckAgainAreNotAskedToJoin()
    {
        var staccato = Notes(touch: Articulation.Staccato);
        List<ExpectedNote> repeated = [.. Enumerable.Range(0, 8).Select(i => new ExpectedNote($"r{i}", 60, i / 4 + 1, 1, i * 480L, 480, null))];

        Assert.Equal("Every note right, steady.", Review(staccato, Play(staccato, hold: 0.4)).Did);
        Assert.Equal("Every note right, steady.", Review(repeated, Play(repeated, hold: 0.8)).Did);
    }

    [Fact]
    public void NotesAKeyOffAreSaidWithTheTimingThatWasGood()
    {
        var notes = Notes(32);
        int[] off = [2, 11, 19, 28];
        var review = Review(notes, Play(notes, key: i => off.Contains(i) ? notes[i].Midi + 1 : notes[i].Midi));

        Assert.Equal("Four notes landed a key off; steady otherwise.", review.Did);
        Assert.Equal("A little slower, so each finger finds its own key.", review.Next);
        Assert.Equal(ReviewFocus.Notes, review.Focus);
    }

    [Fact]
    public void SlipsInOneBarAreThatBarToPractise()
    {
        var notes = Notes(32);
        var review = Review(notes, Play(notes, key: i => i is 8 or 9 or 10 ? notes[i].Midi + 2 : notes[i].Midi));

        Assert.Equal("Most of the slips were in bar 3; steady otherwise.", review.Did);
        Assert.Equal("Bar 3 slowly on its own, then the whole.", review.Next);
        Assert.Equal((3, 3), review.Bars);
    }

    [Fact]
    public void ASharpOfTheKeyPlayedAsItsLetterIsNamed()
    {
        // G major's G A B C D E F♯ G, the F♯ played as F both times.
        int[] scale = [67, 69, 71, 72, 74, 76, 78, 79];
        List<ExpectedNote> notes = [.. Enumerable.Range(0, 16).Select(i => new ExpectedNote($"r{i}", scale[i % 8], i / 4 + 1, 1, i * 480L, 480, null))];
        var review = Review(notes, Play(notes, key: i => notes[i].Midi == 78 ? 77 : notes[i].Midi), new Key(1));

        Assert.Equal("F instead of F♯ twice; steady otherwise.", review.Did);
        Assert.Equal("Think of the F♯ before you start.", review.Next);
        Assert.Equal(ReviewFocus.Key, review.Focus);
    }

    [Fact]
    public void ASlipIsCountedWithItsBar()
    {
        var notes = Notes();
        var review = Review(notes, Play(notes, key: i => i == 5 ? notes[i].Midi + 1 : notes[i].Midi));

        Assert.Equal("One slip, in bar 2; steady and legato otherwise.", review.Did);
        Assert.Equal("Once more at this tempo, for every note.", review.Next);
    }

    [Fact]
    public void OneHandAloneIsNeverNamed()
    {
        var left = Notes(staff: 2, transpose: -12);

        var clean = Review(left, Play(left));
        var slips = Review(left, Play(left, key: i => i is 1 or 6 or 9 or 14 ? left[i].Midi + 1 : left[i].Midi));
        var uneven = Review(left, Play(left, i => i % 2 * 70));

        Assert.Equal("Every note right, steady and legato.", clean.Did);
        Assert.All(new[] { clean, slips, uneven }, r =>
        {
            Assert.DoesNotContain("hand", r.Did);
            Assert.DoesNotContain("hand", r.Next);
        });
    }

    [Fact]
    public void BothHandsPlayedNameTheHandWithTheSlips()
    {
        var (right, left) = (Notes(32), Notes(32, staff: 2, transpose: -12));
        var review = Review([.. right, .. left], [.. Play(right), .. Play(left, key: i => i is 2 or 11 or 19 or 28 ? left[i].Midi + 1 : left[i].Midi)]);

        Assert.Equal("Four slips, all in the left hand; steady otherwise.", review.Did);
        Assert.Equal("The left hand alone for a run or two, then together.", review.Next);
        Assert.Equal(ReviewFocus.OneHand, review.Focus);
    }

    [Fact]
    public void TheHandsTogetherOrNot()
    {
        var (right, left) = (Notes(), Notes(staff: 2, transpose: -12));

        var together = Review([.. right, .. left], [.. Play(right), .. Play(left)]);
        var apart = Review([.. right, .. left], [.. Play(right), .. Play(left, _ => 40)]);

        Assert.Equal("Every note right, steady, legato and the hands together.", together.Did);
        Assert.Equal("Every note right, but the left hand came in a little after the right.", apart.Did);
        Assert.Equal("Land the hands exactly together.", apart.Next);
    }

    [Fact]
    public void WhatTheMusicMarksComesBeforeEvenness()
    {
        var notes = Notes(touch: Articulation.Staccato);
        var review = Review(notes, Play(notes, i => i % 2 * 70, hold: 0.9));

        Assert.Equal("Every note right, but the staccato notes were held long.", review.Did);
        Assert.Equal("Staccato short and light: let each key spring up.", review.Next);
        Assert.Equal(ReviewFocus.Marked, review.Focus);
    }

    [Fact]
    public void AFingerLouderThanTheRestIsSaidWhenThereIsNothingMore()
    {
        var notes = Notes(32);
        var review = Review(notes, Play(notes, velocity: i => notes[i].Finger == 1 ? 90 : 70 + i % 3));

        Assert.Equal("Every note right, steady and legato, but the thumb was louder than the other fingers.", review.Did);
        Assert.Equal("Keep the thumb light.", review.Next);
        Assert.Equal(ReviewFocus.Loudness, review.Focus);
    }

    [Fact]
    public void MusicShapedBySlursIsNotJudgedForOneLoudness()
    {
        var notes = Notes(32, slur: "r0");
        var review = Review(notes, Play(notes, velocity: i => notes[i].Finger == 1 ? 90 : 70 + i % 3));

        Assert.Equal(ReviewFocus.None, review.Focus);
    }

    [Fact]
    public void ARunStoppedEarlyIsReviewedAsFarAsItWent()
    {
        var notes = Notes();

        Assert.Equal("Up to bar 2: every note right, steady and legato.", Review(notes, Play(notes), stoppedAt: "bar 2").Did);
        Assert.Equal("Too little played to say much.", Review(notes, Play(notes).Take(2)).Did);
    }

    [Theory]
    [InlineData(new[] { 1.0, 1.0, 0.9, 1.0 }, "Four times round, every one clean but the third.")]
    [InlineData(new[] { 0.8, 0.9, 1.0 }, "Three times round, clean by the last.")]
    [InlineData(new[] { 1.0, 0.9, 0.85 }, "Three times round, slipping by the last.")]
    [InlineData(new[] { 0.7, 0.8, 0.9 }, "Three times round, better each time.")]
    [InlineData(new[] { 1.0, 1.0 }, "Twice round, every one clean.")]
    public void RoundALoopThePassesLead(double[] rightInTime, string did)
    {
        var notes = Notes();
        var review = Review(notes, Play(notes), passes: [.. rightInTime.Select(r => new PiecePass((int)(16 * r), 16, r))]);

        Assert.Equal(did, review.Did);
    }

    // ——— A scale or an arpeggio ———

    private static TechniqueItem CMajor(HandUse hands = HandUse.Right) => Technique.Scale(new Key(0), ScaleKind.Major, 1, hands);

    // A hand's run: each gap in ms, each key down 10 ms into the next.
    private static List<PlayedNote> Run(IReadOnlyList<TechniqueNote> notes, Func<int, double> gapMs, double start = 0, Func<int, int>? key = null)
    {
        var at = start;
        var played = new List<PlayedNote>();
        for (var i = 0; i < notes.Count; i++)
        {
            played.Add(new PlayedNote(key?.Invoke(i) ?? notes[i].Midi, (long)(at * Ms), (long)((at + gapMs(i) + 10) * Ms), 70));
            at += gapMs(i);
        }
        return played;
    }

    // C major at 250 ms a note, two to a beat: 120 beats a minute.
    private static RunReview Scale(TechniqueItem item, IReadOnlyList<PlayedNote> played, double tempo = 120) => RunReviews.Technique(TechniqueScorer.Score(item, played), item, tempo);

    [Fact]
    public void ACleanScaleIsEveryNoteRightEvenAndLegato()
    {
        var item = CMajor();
        var review = Scale(item, Run(item.Right, _ => 250));

        Assert.Equal("Every note right, even and legato.", review.Did);
        Assert.Equal("A little faster.", review.Next);
    }

    [Fact]
    public void AScaleFasterThanTheClickGetsAheadOfIt()
    {
        var item = CMajor();

        Assert.Equal("Every note right, but you got ahead of the click as you went.", Scale(item, Run(item.Right, _ => 250), tempo: 110).Did);
    }

    [Fact]
    public void AScaleThatSpeedsUpWithinItselfIsSaid()
    {
        var item = CMajor();
        // Each gap 4 ms shorter than the one before, at the click's tempo on average.
        var review = Scale(item, Run(item.Right, i => 260 - 4 * i), tempo: 60_000 / (234.0 * 2));

        Assert.Equal("Every note right, but you sped up as you went.", review.Did);
        Assert.Equal("Keep one speed all the way, with the click.", review.Next);
    }

    [Fact]
    public void ALateThumbCrossingIsABump()
    {
        var item = CMajor();
        var crossings = item.Right.Where(n => n.CrossingNext).Select(n => n.Index).ToHashSet();
        // At its own tempo: the bumps alone would leave it a little behind the click.
        var review = Scale(item, Run(item.Right, i => crossings.Contains(i) ? 280 : 250), tempo: 60_000 / ((12 * 250 + 2 * 280) / 14.0 * 2));

        Assert.Equal("Every note right, but a bump at each thumb crossing.", review.Did);
        Assert.Equal("Get the thumb under early, so it's ready.", review.Next);
        Assert.Equal(ReviewFocus.Crossings, review.Focus);
    }

    [Fact]
    public void TheLeftHandsScaleAloneIsNeverNamed()
    {
        var item = CMajor(HandUse.Left);
        var review = Scale(item, Run(item.Left, i => i % 2 == 0 ? 215 : 285));

        Assert.Equal("Every note right, but the gaps between them were uneven.", review.Did);
        Assert.DoesNotContain("hand", review.Did);
        Assert.DoesNotContain("hand", review.Next);
    }

    [Fact]
    public void BothHandsNameTheLessEvenOne()
    {
        var item = CMajor(HandUse.Together);
        var review = Scale(item, [.. Run(item.Right, _ => 250), .. Run(item.Left, i => i % 2 == 0 ? 215 : 285)]);

        Assert.Equal("Every note right, but the left hand was less even than the right.", review.Did);
        Assert.Equal("The left hand alone for a run or two, for even gaps.", review.Next);
    }

    [Fact]
    public void AScaleSlipIsPlacedInTheScale()
    {
        var item = CMajor();
        // Index 10 is the G on the way down.
        var review = Scale(item, Run(item.Right, _ => 250, key: i => i == 10 ? item.Right[i].Midi + 1 : item.Right[i].Midi));

        Assert.Equal("One slip, coming down.", review.Did);
        Assert.Equal("Once more at this tempo, for every note.", review.Next);
    }

    [Fact]
    public void AScalesForgottenSharpIsNamed()
    {
        var item = Technique.Scale(new Key(1), ScaleKind.Major, 1, HandUse.Right);
        var review = Scale(item, Run(item.Right, _ => 250, key: i => item.Right[i].Midi % 12 == 6 ? item.Right[i].Midi - 1 : item.Right[i].Midi));

        Assert.Equal("F instead of F♯ twice.", review.Did);
        Assert.Equal("Think of the F♯ before you start.", review.Next);
    }

    [Fact]
    public void AScalesStepOnIsRcmsTempoWhereItsOneOfRcms()
    {
        Assert.Equal("That's RCM Level 2's tempo: keep it there, and on to the next key.", RunReviews.StepOn(90, 88, "Level 2"));
        Assert.Equal("A little faster, towards RCM Level 2's tempo.", RunReviews.StepOn(80, 88, "Level 2"));
        Assert.Equal("A little faster.", RunReviews.StepOn(80, null, null));
    }

    // ——— Wait for me ———

    [Fact]
    public void WaitForMesReviewIsTheNotesRightFirstTime()
    {
        var clean = RunReviews.Waited(new WaitResult(20, 20, 20, 0, 30), []);
        var apart = RunReviews.Waited(new WaitResult(20, 20, 20, 0, 30) { Apart = 2 }, []);
        var oneBar = RunReviews.Waited(new WaitResult(20, 20, 16, 4, 30), [new BarTrouble(3, 4, 0, 0, 0)]);
        var one = RunReviews.Waited(new WaitResult(20, 20, 19, 1, 30), [new BarTrouble(2, 1, 0, 0, 0)]);

        Assert.Equal(("Every note right first time, at your own pace.", "In time, with the click."), (clean.Did, clean.Next));
        Assert.Equal(("Two chords were played a key at a time.", "Put each chord's keys down together."), (apart.Did, apart.Next));
        Assert.Equal(("Most of the slips were in bar 3.", "Bar 3 slowly on its own, then the whole."), (oneBar.Did, oneBar.Next));
        Assert.Equal("One note wasn't right first time, in bar 2.", one.Did);
    }

    [Fact]
    public void WaitForMesPassesLeadRoundALoop()
    {
        var review = RunReviews.Waited(new WaitResult(20, 20, 20, 0, 30), [], [new PiecePass(20, 20, null), new PiecePass(19, 20, null), new PiecePass(20, 20, null)]);

        Assert.Equal("Three times round, every one clean but the second.", review.Did);
    }
}

/// <summary>What In time measures of each hand for the review: how even its gaps, how joined its notes, how together the hands.</summary>
public class StaffPlayingTests
{
    private const long Ms = 1_000_000;
    private const double BeatNs = 500 * Ms;

    private static long At(double beats) => (long)(beats * BeatNs);

    private static List<ExpectedNote> Notes(int staff = 1, int count = 16) =>
        [.. Enumerable.Range(0, count).Select(i => new ExpectedNote($"n{staff}-{i}", (staff == 1 ? 60 : 48) + i % 5, i / 4 + 1, staff, i * 480L, 480, null) { Finger = i % 5 + 1 })];

    private static List<PlayedNote> Play(IReadOnlyList<ExpectedNote> notes, Func<int, double>? late = null, double hold = 1.0, Func<int, int>? velocity = null) =>
        [.. notes.Select((n, i) =>
        {
            var on = At(n.Tick / 480.0) + (long)((late?.Invoke(i) ?? 0) * Ms);
            return new PlayedNote(n.Midi, on, on + (long)(hold * BeatNs), velocity?.Invoke(i) ?? 70);
        })];

    private static PassageResult Score(IReadOnlyList<ExpectedNote> notes, IEnumerable<PlayedNote> played) =>
        PassageScorer.Score(notes, At, 480, BeatNs, 4 * BeatNs, [.. played], 0);

    [Fact]
    public void GapsAreMeasuredAgainstTheGapsWrittenAndADriftIsNotUneven()
    {
        var notes = Notes();
        var drifting = Score(notes, Play(notes, i => -10.0 * i));
        var uneven = Score(notes, Play(notes, i => i % 2 * 50));

        Assert.Equal(0, drifting.Staves[0].GapSpreadPercent!.Value, 6);
        Assert.Equal(30, drifting.AheadPercent!.Value, 6);
        Assert.InRange(uneven.Staves[0].GapSpreadPercent!.Value, 10, 11);
    }

    [Fact]
    public void JoinedIsTheSilenceBetweenAKeyUpAndTheNextDownInTheGap()
    {
        var notes = Notes();

        Assert.Equal(40, Score(notes, Play(notes, hold: 0.6)).Staves[0].SilencePercent!.Value, 6);
        Assert.Equal(-10, Score(notes, Play(notes, hold: 1.1)).Staves[0].SilencePercent!.Value, 6);
    }

    [Fact]
    public void TheHandsApartAreWhereBothPlayAtOnce()
    {
        var (right, left) = (Notes(1), Notes(2));
        var result = Score([.. right, .. left], [.. Play(right), .. Play(left, _ => 30)]);

        Assert.Equal(2, result.Staves.Count);
        Assert.Equal(-30, result.HandsApartMs!.Value, 6);
        Assert.Equal(0, result.HandsApartSdMs!.Value, 6);
    }

    [Fact]
    public void LoudnessByFingerIsOfFingersOnFourNotesOrMore()
    {
        // Fingers 1 to 5 in turn over 18 notes: 1, 2 and 3 four times, 4 and 5 three.
        var notes = Notes(count: 18);
        var result = Score(notes, Play(notes, velocity: i => 60 + i));

        Assert.Equal(new[] { 1, 2, 3 }, result.Staves[0].VelocityByFinger.Keys.Order());
        Assert.NotNull(result.Staves[0].VelocitySd);
    }
}
