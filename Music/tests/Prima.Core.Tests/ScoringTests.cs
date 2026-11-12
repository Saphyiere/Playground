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

    private static PassageResult Score(IReadOnlyList<ExpectedNote> expected, IEnumerable<PlayedNote> played, double offsetMs = 0, IReadOnlySet<string>? ornamented = null, IReadOnlyList<(long, long)>? damper = null,
        Markings? markings = null, (int, int)? range = null, IReadOnlySet<string>? rolled = null) =>
        PassageScorer.Score(expected, At, 480, BeatNs, 4 * BeatNs, [.. played], offsetMs, ornamented, damper, markings, range, rolled);

    // A key down and up, in beats, struck this hard.
    private static PlayedNote Hit(int midi, double down, double up, int velocity) => new(midi, At(down), At(up), velocity);

    private static Markings Marked(IReadOnlyList<Markings.Dynamic>? dynamics = null, IReadOnlyList<Markings.Hairpin>? hairpins = null, IReadOnlyList<Markings.Pedalled>? pedal = null) =>
        new(dynamics ?? [], hairpins ?? [], pedal ?? [], new HashSet<string>());

    private static PlayedNote P(int midi, double beats, double offsetMs = 0) => new(midi, At(beats) + (long)(offsetMs * Ms), At(beats) + (long)(offsetMs * Ms) + 400 * Ms, 70);

    // A key down and up, in beats.
    private static PlayedNote Held(int midi, double down, double up) => new(midi, At(down), At(up), 70);

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
    public void AWrongKeyJustBeforeTheRightOneMakesTheNoteWrong()
    {
        // E♭ then E, 60 ms apart, for the E: a slip put right still counts, and the E that put it right isn't extra.
        var played = Scale().Select((e, i) => P(e.Midi, i)).Where((_, i) => i != 2).Append(P(63, 2, -30)).Append(P(64, 2, 30)).ToList();
        var result = Score(Scale(), played);

        Assert.Equal((Outcome.Wrong, 63, true), (result.Notes[2].Outcome, result.Notes[2].Played!.Value, result.Notes[2].Corrected));
        Assert.Null(result.Notes[2].ErrorMs);
        Assert.Equal((7, 1, 0, 1), (result.Right, result.Wrong, result.Extra, result.Corrected));
        Assert.False(result.Clean);
    }

    [Fact]
    public void AWrongKeyAfterTheRightOneIsOnlyExtra()
    {
        var result = Score(Scale(), Scale().Select((e, i) => P(e.Midi, i)).Append(P(63, 2, 60)));

        Assert.Equal((8, 0, 1), (result.Right, result.Wrong, result.Extra));
    }

    [Fact]
    public void AChordsKeyStruckTwiceIsExtraNotASlip()
    {
        var expected = new List<ExpectedNote> { new("c0", 60, 1, 1, 0, 960, "c"), new("c1", 64, 1, 1, 0, 960, "c"), new("c2", 67, 1, 1, 0, 960, "c") };
        var result = Score(expected, [P(60, 0, -20), P(60, 0), P(64, 0, 10), P(67, 0, 20)]);

        Assert.Equal((3, 0, 1), (result.Right, result.Wrong, result.Extra));
    }

    [Fact]
    public void ATrillStartingOnTheNoteAboveIsNotASlip()
    {
        var expected = new List<ExpectedNote> { new("t", 67, 1, 1, 0, 1920, null), new("u", 65, 2, 1, 1920, 480, null) };
        var trill = Enumerable.Range(0, 10).Select(i => P(i % 2 == 0 ? 69 : 67, i * 0.3)).Append(P(65, 4));
        var result = Score(expected, trill, ornamented: new HashSet<string> { "t" });

        Assert.Equal((2, 0, 0), (result.Right, result.Wrong, result.Extra));
    }

    [Fact]
    public void ANoteLetGoBeforeFourFifthsOfItsLengthIsLetGoEarly()
    {
        // At 120 bpm: a half note held a beat and a half is let go early, one held 1.7 beats isn't; a quarter needs three quarters of a beat.
        var expected = new List<ExpectedNote> { new("h1", 60, 1, 1, 0, 960, null), new("h2", 62, 1, 1, 960, 960, null), new("q1", 64, 2, 1, 1920, 480, null), new("q2", 65, 2, 1, 2400, 480, null) };
        var result = Score(expected, [Held(60, 0, 1.5), Held(62, 2, 3.7), Held(64, 4, 4.7), Held(65, 5, 5.8)]);

        Assert.Equal([true, false, true, false], result.Notes.Select(n => n.LetGoEarly));
        Assert.Equal(75, result.Notes[0].HeldPercent!.Value, 6);
        Assert.Equal((4, 2, 4), (result.HoldsJudged, result.LetGoEarly, result.Right));
        Assert.True(result.Clean);
    }

    [Fact]
    public void ThePedalHoldsAKeyLetGoUntilItComesUpOrTheKeyIsStruckAgain()
    {
        var expected = new List<ExpectedNote> { new("a", 60, 1, 1, 0, 1920, null), new("b", 64, 2, 1, 1920, 960, null), new("c", 64, 2, 1, 2880, 960, null), new("d", 67, 3, 1, 3840, 960, null) };
        // a let go after a beat with the pedal down to 3.9; b and c let go with it down from 4.2 to 8.2, b sounding until c strikes
        // its key again; d let go after the pedal came up.
        var result = Score(expected, [Held(60, 0, 1), Held(64, 4, 4.5), Held(64, 6, 6.5), Held(67, 8, 8.6)], damper: [(At(0.5), At(3.9)), (At(4.2), At(8.2))]);

        Assert.Equal([false, false, false, true], result.Notes.Select(n => n.LetGoEarly));
        Assert.Equal([97.5, 100, 110, 30], result.Notes.Select(n => Math.Round(n.HeldPercent!.Value, 6)));
    }

    [Fact]
    public void StaccatoOrnamentedAndCutOffNotesAreNotJudgedOnTheirHold()
    {
        // s is staccato, t trilled, and h still down when the run was stopped a beat into it; k was still down at the end, held to its end.
        var expected = new List<ExpectedNote> { new("s", 60, 1, 1, 0, 960, null) { Lifted = true }, new("t", 62, 1, 1, 960, 960, null), new("h", 64, 2, 1, 1920, 1920, null), new("k", 48, 2, 1, 1920, 960, null) };
        var result = Score(expected, [Held(60, 0, 0.25), Held(62, 2, 2.2), new PlayedNote(64, At(4), At(5), 70) { HeldToEnd = true }, new PlayedNote(48, At(4), At(6), 70) { HeldToEnd = true }],
            ornamented: new HashSet<string> { "t" });

        Assert.Equal([null, null, null, 100], result.Notes.Select(n => n.HeldPercent));
        Assert.Equal((1, 0), (result.HoldsJudged, result.LetGoEarly));
    }

    [Fact]
    public void AStaccatoHeldLongAndATenutoLetGoShortAreFound()
    {
        // At 120 bpm a staccato quarter is short under 0.3 s, and a tenuto half is held nine tenths of its second.
        var expected = new List<ExpectedNote>
        {
            new("s1", 60, 1, 1, 0, 480, null) { Articulation = Articulation.Staccato, Lifted = true },
            new("s2", 62, 1, 1, 480, 480, null) { Articulation = Articulation.Staccato, Lifted = true },
            new("t", 64, 1, 1, 960, 960, null) { Articulation = Articulation.Tenuto },
        };
        var result = Score(expected, [Held(60, 0, 0.3), Held(62, 1, 1.8), Held(64, 2, 3.7)]);

        Assert.Equal([null, Touch.StaccatoHeldLong, Touch.TenutoShort], result.Notes.Select(n => n.Touch));
        Assert.Equal(2, result.Touches);
    }

    [Fact]
    public void UnderASlurANoteLetGoBeforeTheNextBreaksTheLine()
    {
        var expected = Scale().Take(5).Select((e, i) => i < 4 ? e with { Slur = "n0" } : e).ToList();
        // The second note let go at 1.7 beats, 0.15 s before the third; the fifth isn't under the slur.
        var result = Score(expected, [Held(60, 0, 1), Held(62, 1, 1.7), Held(64, 2, 3), Held(65, 3, 3.9), Held(67, 4.2, 5)]);

        Assert.Equal([null, Touch.GapUnderSlur, null, null, null], result.Notes.Select(n => n.Touch));
    }

    [Fact]
    public void AnAccentNoHarderThanTheNotesAroundItIsFound()
    {
        var expected = Enumerable.Range(0, 8).Select(i => new ExpectedNote($"n{i}", 60 + i, 1, 1, i * 240L, 240, null) { Articulation = i is 1 or 5 ? Articulation.Accent : Articulation.None }).ToList();
        int[] velocities = [60, 62, 61, 63, 60, 80, 61, 62];
        var result = Score(expected, expected.Select((e, i) => Hit(e.Midi, i * 0.5, i * 0.5 + 0.45, velocities[i])));

        Assert.Equal(Touch.AccentNotStronger, result.Notes[1].Touch);
        Assert.Null(result.Notes[5].Touch);
        Assert.Equal(62, result.Notes[1].Velocity);
    }

    [Fact]
    public void EachDynamicIsJudgedOnYourRangeAndAgainstTheOneBefore()
    {
        var marked = Marked(dynamics: [new("p", 0, 1920), new("f", 1920, 3840)]);

        // With the range taken as 25 to 105: a p at 70 is loud for it, and an f at 72 is no louder than the p before it.
        var loud = Score(Scale(), Scale().Select((e, i) => Hit(e.Midi, i, i + 0.9, i < 4 ? 70 + i % 2 : 72 + i % 2)), markings: marked);
        Assert.Equal([Loudness.TooLoud, Loudness.NoChange], loud.Dynamics.Select(d => d.Outcome));
        Assert.Equal((1, 1), (loud.Dynamics[0].FromBar, loud.Dynamics[0].ToBar));

        // On a range of 20 to 100: the p at 40 and the f at 85 are both right.
        var right = Score(Scale(), Scale().Select((e, i) => Hit(e.Midi, i, i + 0.9, i < 4 ? 40 + i % 2 : 85 + i % 2)), markings: marked, range: (20, 100));
        Assert.Equal([Loudness.Right, Loudness.Right], right.Dynamics.Select(d => d.Outcome));
        Assert.Equal(25.6, right.Dynamics[0].LevelPercent, 1);
        Assert.True(right.TouchVaries);
    }

    [Fact]
    public void AFixedTouchJudgesNoLoudness()
    {
        var result = Score(Scale(), Scale().Select((e, i) => Hit(e.Midi, i, i + 0.9, 64)), markings: Marked(dynamics: [new("p", 0, 3840)]));

        Assert.False(result.TouchVaries);
        Assert.Empty(result.Dynamics);
    }

    [Fact]
    public void ACrescendoMustGrow()
    {
        var crescendo = Marked(hairpins: [new(true, 0, 3360)]);

        int[] even = [58, 63, 60, 62, 59, 63, 61, 60];
        var flat = Score(Scale(), Scale().Select((e, i) => Hit(e.Midi, i, i + 0.9, even[i])), markings: crescendo);
        var grew = Score(Scale(), Scale().Select((e, i) => Hit(e.Midi, i, i + 0.9, 50 + 5 * i)), markings: crescendo);
        var fell = Score(Scale(), Scale().Select((e, i) => Hit(e.Midi, i, i + 0.9, 90 - 5 * i)), markings: crescendo);

        Assert.Equal(Swell.Flat, Assert.Single(flat.Hairpins).Outcome);
        Assert.Equal((Swell.Right, 35.0), (Assert.Single(grew.Hairpins).Outcome, Math.Round(grew.Hairpins[0].Change, 6)));
        Assert.Equal(Swell.WrongWay, Assert.Single(fell.Hairpins).Outcome);
    }

    [Fact]
    public void ThePedalIsJudgedWhereItsMarked()
    {
        // Marked: beats 0 to 2, changed at 2, held to 4; and 5 to 6. Pedalled: changed in time at 2, held on past 4 to 4.6, nothing
        // at 5, and down from 7 to 7.8 where nothing is marked.
        var marked = Marked(pedal: [new(0, 960), new(960, 1920), new(2400, 2880)]);
        var result = Score(Scale(), Scale().Select((e, i) => Held(e.Midi, i, i + 0.9)), damper: [(At(0.1), At(1.95)), (At(2.1), At(4.6)), (At(7), At(7.8))], markings: marked);

        Assert.Equal([Pedalling.Right, Pedalling.NotLifted, Pedalling.NotPressed], result.Pedal.Select(p => p.Outcome));
        Assert.Equal([(1, 1), (1, 1), (2, 2)], result.Pedal.Select(p => (p.FromBar, p.ToBar)));
        Assert.Equal([2], result.ExtraPedal);
    }

    [Fact]
    public void APedalPressedLateOrLetUpIsFound()
    {
        var marked = Marked(pedal: [new(0, 1920), new(1920, 3840)]);
        var result = Score(Scale(), Scale().Select((e, i) => Held(e.Midi, i, i + 0.9)), damper: [(At(1), At(3.9)), (At(4), At(5))], markings: marked);

        Assert.Equal([Pedalling.Late, Pedalling.LiftedEarly], result.Pedal.Select(p => p.Outcome));
        Assert.Empty(result.ExtraPedal);
    }

    [Fact]
    public void WithoutPedalMarksThePedalIsntJudged()
    {
        var result = Score(Scale(), Scale().Select((e, i) => Held(e.Midi, i, i + 0.9)), damper: [(At(0), At(8))]);

        Assert.Empty(result.Pedal);
        Assert.Empty(result.ExtraPedal);
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
    public void WaitForMeTakesAChordMarkedToBeRolledAsARoll()
    {
        // C–E–G–C marked to be rolled, then a D.
        ExpectedNote[] notes = [new("a", 48, 1, 1, 0, 480, "ch"), new("b", 52, 1, 1, 0, 480, "ch"), new("c", 55, 1, 1, 0, 480, "ch"), new("d", 60, 1, 1, 0, 480, "ch"), new("e", 62, 1, 1, 480, 480, null)];
        var judge = new WaitJudge(notes, rolled: new HashSet<string> { "a", "b", "c", "d" });

        // Rolled at 70 ms a note: 0.21 s from the first key to the last, further apart than keys together, and it's the chord.
        Assert.True(judge.Rolling);
        foreach (var (key, at) in new[] { (48, 0), (52, 70), (55, 140) }) Assert.False(judge.Play(key, at * Ms).Moved);
        Assert.True(judge.Play(60, 210 * Ms).Moved);
        Assert.False(judge.Rolling);
        judge.Play(62, 700 * Ms);

        var result = judge.Result();
        Assert.Equal((5, 5, 0, 0), (result.Notes, result.RightFirstTime, result.WrongKeys, result.Apart));

        // The same chord not marked to be rolled wants its keys together.
        var plain = new WaitJudge(notes);
        foreach (var (key, at) in new[] { (48, 0), (52, 70), (55, 140) }) plain.Play(key, at * Ms);
        Assert.False(plain.Play(60, 210 * Ms).Moved);
        Assert.True(plain.Apart);
    }

    [Theory]
    [InlineData(300, true)]
    [InlineData(301, false)]
    public void WaitForMeWantsARollInOneGo(int msBetween, bool rolled)
    {
        var judge = new WaitJudge([new("a", 48, 1, 1, 0, 480, "ch"), new("b", 52, 1, 1, 0, 480, "ch"), new("c", 55, 1, 1, 0, 480, "ch")], rolled: new HashSet<string> { "a", "b", "c" });

        // A wait in the middle of the roll: no longer than a roll's, it's still one roll; longer, it's keys found one at a time.
        judge.Play(48, 0);
        judge.Play(52, 60 * Ms);
        Assert.Equal(rolled, judge.Play(55, (60 + msBetween) * Ms).Moved);
        Assert.Equal(!rolled, judge.Apart);
        if (rolled) return;

        // Rolled again in one go, it moves on; the chord wasn't right first time.
        judge.Play(48, 2000 * Ms);
        judge.Play(52, 2060 * Ms);
        Assert.True(judge.Play(55, 2120 * Ms).Moved);
        Assert.Equal((0, 1), (judge.Result().RightFirstTime, judge.Result().Apart));
    }

    [Fact]
    public void WaitForMeCountsAStretchOfTheMusicByItself()
    {
        // Two bars round a loop, twice: C D | E, with a slip on the D the first time and none the second.
        ExpectedNote[] notes = [new("a", 60, 1, 1, 0, 480, null), new("b", 62, 1, 1, 480, 480, null), new("c", 64, 2, 1, 960, 480, null),
            new("a~2", 60, 1, 1, 1440, 480, null), new("b~2", 62, 1, 1, 1920, 480, null), new("c~2", 64, 2, 1, 2400, 480, null)];
        var judge = new WaitJudge(notes);
        foreach (var (key, at) in new[] { (60, 0), (61, 400), (62, 800), (64, 1200), (60, 1600), (62, 2000) }) judge.Play(key, at * Ms);

        // The first time round: all three reached, two right first time, the slip its one wrong key, 1.2 s from its first key to
        // its last. The second, so far: two of its three reached, both right, the time from its first key to its last.
        var (first, second, run) = (judge.Stretch(0, 1440), judge.Stretch(1440, 2880), judge.Result());
        Assert.Equal((3, 3, 2, 1, 1.2), (first.Notes, first.Total, first.RightFirstTime, first.WrongKeys, first.Seconds));
        Assert.Equal((2, 3, 2, 0, 0.4), (second.Notes, second.Total, second.RightFirstTime, second.WrongKeys, second.Seconds));
        Assert.Equal([1200 * Ms], first.StepNs.Skip(2));
        Assert.Equal((5, 6, 4, 1, 2.0), (run.Notes, run.Total, run.RightFirstTime, run.WrongKeys, run.Seconds));
    }

    [Fact]
    public void WaitForMeKeepsWhenEachMomentOfTheMusicWasPlayed()
    {
        // Hands together: C in each hand, then D in each, then E in each, the hands landing 10 ms apart, half a second a note.
        var judge = new WaitJudge([new("l1", 48, 1, 1, 0, 480, null), new("r1", 60, 1, 1, 0, 480, null), new("l2", 50, 1, 1, 480, 480, null), new("r2", 62, 1, 1, 480, 480, null),
            new("l3", 52, 1, 2, 960, 480, null), new("r3", 64, 1, 2, 960, 480, null)]);
        foreach (var (key, at) in new[] { (60, 0), (48, 10), (62, 500), (50, 510), (61, 900), (64, 1000), (52, 1010) }) judge.Play(key, at * Ms);

        // A moment is played when the last of its keys goes down: the gaps are the pace, not the 10 ms between the hands. A wrong
        // key on the way isn't a moment.
        Assert.Equal([10 * Ms, 510 * Ms, 1010 * Ms], judge.Result().StepNs);
    }

    [Fact]
    public void WaitForMeWantsAChordsKeysDownTogether()
    {
        // A chord E–G, then C.
        var judge = new WaitJudge([new("b", 64, 1, 1, 0, 480, "ch"), new("c", 67, 1, 1, 0, 480, "ch"), new("d", 72, 1, 1, 480, 480, null)]);

        // One key, and the other half a second on: each is the right key, but that's the chord found, not played. It waits, its
        // notes marked apart.
        Assert.Equal(new LiveMark("b", LiveKind.Right, 64, 0), Assert.Single(judge.Play(64, 0).Marks));
        var (marks, moved) = judge.Play(67, 500 * Ms);
        Assert.False(moved);
        Assert.Equal([new LiveMark("b", LiveKind.Apart, 64, 0), new LiveMark("c", LiveKind.Apart, 67, 0)], marks);
        Assert.True(judge.Apart);
        // One after another again is still nothing; struck together, it moves on.
        Assert.False(judge.Play(64, 1500 * Ms).Moved);
        Assert.False(judge.Play(67, 2000 * Ms).Moved);
        Assert.False(judge.Play(64, 2500 * Ms).Moved);
        var together = judge.Play(67, 2540 * Ms);
        Assert.Equal((0, true), (together.Marks.Count, together.Moved));
        Assert.False(judge.Apart);
        judge.Play(72, 3000 * Ms);

        // The chord's notes weren't right first time; the note after it was.
        var result = judge.Result();
        Assert.Equal((3, 1, 0, 1), (result.Notes, result.RightFirstTime, result.WrongKeys, result.Apart));
        Assert.True(result.Finished);
    }

    [Theory]
    [InlineData(150, true)]
    [InlineData(151, false)]
    public void WaitForMeCallsKeysTogetherWithinAMoment(int msApart, bool together)
    {
        var judge = new WaitJudge([new("b", 64, 1, 1, 0, 480, "ch"), new("c", 67, 1, 1, 0, 480, "ch")]);
        judge.Play(64, 0);

        Assert.Equal(together, judge.Play(67, msApart * Ms).Moved);
        Assert.Equal(together ? 0 : 1, judge.Result().Apart);
    }

    [Fact]
    public void WaitForMeTakesAKeyFoundAloneAndThenTheChordTogether()
    {
        var judge = new WaitJudge([new("b", 64, 1, 1, 0, 480, "ch"), new("c", 67, 1, 1, 0, 480, "ch")]);

        // A key of the chord on its own, a pause, then the chord: nothing was played apart, so both are right first time.
        Assert.False(judge.Play(64, 0).Moved);
        Assert.False(judge.Play(64, 2000 * Ms).Moved);
        Assert.True(judge.Play(67, 2030 * Ms).Moved);
        Assert.Equal((2, 0), (judge.Result().RightFirstTime, judge.Result().Apart));
    }

    [Fact]
    public void WaitForMeTakesAChordWithASlipPlayedAgainTogether()
    {
        // C–E–G, played C–E–A: the A is a slip for the G, and the chord again, right, moves on without being apart.
        var judge = new WaitJudge([new("a", 60, 1, 1, 0, 480, "ch"), new("b", 64, 1, 1, 0, 480, "ch"), new("c", 67, 1, 1, 0, 480, "ch")]);
        judge.Play(60, 0);
        judge.Play(64, 10 * Ms);
        Assert.Equal(new LiveMark("c", LiveKind.Wrong, 69, 0), Assert.Single(judge.Play(69, 20 * Ms).Marks));
        judge.Play(60, 1000 * Ms);
        judge.Play(64, 1010 * Ms);

        Assert.True(judge.Play(67, 1020 * Ms).Moved);
        var result = judge.Result();
        Assert.Equal((3, 2, 1, 0), (result.Notes, result.RightFirstTime, result.WrongKeys, result.Apart));
    }

    [Fact]
    public void WaitForMeMarksAWrongKeyOnAChordWaitingToBePlayedTogether()
    {
        // E–G found a key at a time, then an F: a slip for the E, the nearest, though every key of the chord has been found.
        var judge = new WaitJudge([new("b", 64, 1, 1, 0, 480, "ch"), new("c", 67, 1, 1, 0, 480, "ch")]);
        judge.Play(64, 0);
        judge.Play(67, 500 * Ms);

        Assert.Equal(new LiveMark("b", LiveKind.Wrong, 65, 0), Assert.Single(judge.Play(65, 900 * Ms).Marks));
        judge.Play(64, 1500 * Ms);
        Assert.True(judge.Play(67, 1520 * Ms).Moved);
        Assert.Equal((0, 1, 1), (judge.Result().RightFirstTime, judge.Result().WrongKeys, judge.Result().Apart));
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

public class RolledChordTests
{
    private const long Ms = 1_000_000;
    private const double BeatNs = 500 * Ms; // 120 beats a minute
    private static long At(double beats) => (long)(beats * BeatNs);

    [Fact]
    public void InTimeARolledChordIsTimedByTheRollsStart()
    {
        // C, E and G on the beat, then D: the chord rolled up, its keys 90 ms apart. Marked to be rolled, every note is in time
        // and no key is extra; not marked, the roll's keys are off the beat.
        ExpectedNote[] notes = [new("c", 60, 1, 1, 0, 480, "x"), new("e", 64, 1, 1, 0, 480, "x"), new("g", 67, 1, 1, 0, 480, "x"), new("d", 62, 1, 1, 480, 480, null)];
        PlayedNote[] keys = [new(60, 0, At(0.9), 70), new(64, 90 * Ms, At(0.9), 70), new(67, 180 * Ms, At(0.9), 70), new(62, At(1), At(1.9), 70)];

        var rolled = PassageScorer.Score(notes, At, 480, BeatNs, 4 * BeatNs, keys, 0, rolled: new HashSet<string> { "c", "e", "g" });
        var plain = PassageScorer.Score(notes, At, 480, BeatNs, 4 * BeatNs, keys, 0);

        Assert.Equal((4, 4, 0), (rolled.Right, rolled.InTime, rolled.Extra));
        Assert.True(plain.Off > 0);
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
        // 20% late, with the lean of the two keys before it taken off: (5 − 20) / 2 = −7.5, so 27.5.
        Assert.Equal(27.5, late.ErrorPercent, 3);
    }

    [Fact]
    public void ASteadyLeanIsTakenOffAsTheSummaryTakesItOff()
    {
        // Every key 15% of a beat early: the first is marked early, with no lean yet to go by; the rest are right, as the summary
        // will have them all once it takes the run's lean off.
        var judge = new LiveJudge([N("a", 60, 0), N("b", 62, 1), N("c", 64, 2), N("d", 65, 3)], At, Ticks.Quarter, BeatNs, 0);

        var marks = new[] { (60, 0), (62, 1), (64, 2), (65, 3) }.Select(k => judge.Play(k.Item1, At(k.Item2 - 0.15))!.Kind);

        Assert.Equal([LiveKind.Early, LiveKind.Right, LiveKind.Right, LiveKind.Right], marks);
    }

    [Fact]
    public void ARolledChordIsTimedByItsFirstKey()
    {
        // C, E and G rolled up from the beat, a fifth of a beat between keys: each right, as its first is, though the top one
        // comes after the window. Not marked to be rolled, the middle key is late and the top one too late to be the chord's.
        ExpectedNote[] chord = [N("c", 60, 0) with { ChordId = "x" }, N("e", 64, 0) with { ChordId = "x" }, N("g", 67, 0) with { ChordId = "x" }];
        var rolled = new LiveJudge(chord, At, Ticks.Quarter, BeatNs, 0, rolled: new HashSet<string> { "c", "e", "g" });
        var plain = new LiveJudge(chord, At, Ticks.Quarter, BeatNs, 0);

        Assert.All(new[] { (60, 0.0), (64, 0.2), (67, 0.4) }.Select(k => rolled.Play(k.Item1, At(k.Item2))), m => Assert.Equal(LiveKind.Right, m!.Kind));
        Assert.Empty(rolled.Due(At(1)));
        Assert.Equal(LiveKind.Right, plain.Play(60, At(0))!.Kind);
        Assert.Equal(LiveKind.Late, plain.Play(64, At(0.2))!.Kind);
        Assert.Null(plain.Play(67, At(0.4)));
    }

    [Fact]
    public void AWrongKeyStaysWrongWhenTheRightKeyFollows()
    {
        var judge = new LiveJudge([N("a", 60, 0), N("b", 62, 1)], At, Ticks.Quarter, BeatNs, 0);

        var slip = judge.Play(61, At(0))!;
        Assert.Equal((LiveKind.Wrong, "a", 61), (slip.Kind, slip.Id, slip.Played));
        // The right key a moment later leaves the mark as it is; the next note is untouched by the slip, and a wrong note isn't
        // also counted missed.
        Assert.Null(judge.Play(60, At(0.08)));
        Assert.Equal(LiveKind.Right, judge.Play(62, At(1))!.Kind);
        Assert.Empty(judge.Due(At(3)));
    }

    [Fact]
    public void AChordsKeyStruckAgainAndAnOrnamentsKeysMakeNoMark()
    {
        var notes = new[] { N("low", 48, 0) with { ChordId = "c" }, N("high", 55, 0) with { ChordId = "c" }, N("t", 67, 1), N("u", 64, 3) };
        var judge = new LiveJudge(notes, At, Ticks.Quarter, BeatNs, 0, new HashSet<string> { "t" });

        Assert.Equal(LiveKind.Right, judge.Play(48, At(0))!.Kind);
        Assert.Null(judge.Play(48, At(0.02)));
        Assert.Equal(LiveKind.Right, judge.Play(55, At(0.04))!.Kind);
        // The trill on t starts on the note above.
        Assert.Null(judge.Play(69, At(1)));
        Assert.Equal(LiveKind.Right, judge.Play(67, At(1.05))!.Kind);
        Assert.Empty(judge.Due(At(2)));
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
