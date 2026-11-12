using Prima.Core.Generation;
using Prima.Core.Music;
using Prima.Core.Practice;
using Prima.Core.Timing;

namespace Prima.Core.Tests;

public class PracticeTests
{
    private static readonly DateOnly Today = new(2026, 11, 5);

    [Fact]
    public void CardsMoveBetweenBoxes()
    {
        var card = new NoteCard(Clef.Treble, 60, 2, Today, Today.AddDays(-5), null);

        Assert.Equal((3, Today.AddDays(2)), (NoteSchedule.Answer(card, true, true, Today).Box, NoteSchedule.Answer(card, true, true, Today).Due));
        Assert.Equal((2, Today.AddDays(1)), (NoteSchedule.Answer(card, true, false, Today).Box, NoteSchedule.Answer(card, true, false, Today).Due));
        Assert.Equal((1, Today), (NoteSchedule.Answer(card, false, true, Today).Box, NoteSchedule.Answer(card, false, true, Today).Due));
        Assert.Equal(6, NoteSchedule.Answer(card with { Box = 6 }, true, true, Today).Box);
    }

    [Fact]
    public void FastIsYourOwnMedianHeldBetweenBounds()
    {
        NoteAnswer A(double ms) => new(DateTimeOffset.Now, Clef.Treble, 60, true, ms);

        Assert.Equal(1250, NoteSchedule.FastMs([A(900), A(1000), A(1100)]), 6);
        Assert.Equal(800, NoteSchedule.FastMs([A(300), A(400)]), 6);
        Assert.Equal(2000, NoteSchedule.FastMs([A(3000)]), 6);
        Assert.Equal(2000, NoteSchedule.FastMs([]), 6);
    }

    [Fact]
    public void FluentNeedsThreeFastRightAnswersOverTwoDays()
    {
        var day1 = new DateTimeOffset(2026, 11, 3, 18, 0, 0, TimeSpan.Zero);
        NoteAnswer A(DateTimeOffset at, bool right = true, double ms = 700) => new(at, Clef.Treble, 60, right, ms);

        Assert.True(NoteSchedule.Fluent([A(day1), A(day1.AddMinutes(5)), A(day1.AddDays(1))], 1000));
        Assert.False(NoteSchedule.Fluent([A(day1), A(day1.AddMinutes(5)), A(day1.AddMinutes(9))], 1000));
        Assert.False(NoteSchedule.Fluent([A(day1), A(day1.AddDays(1), ms: 1500), A(day1.AddDays(2))], 1000));
    }

    [Fact]
    public void NewNotesComeInLandmarkOrder()
    {
        var order = NoteSchedule.LandmarkOrder;

        Assert.Equal([(Clef.Treble, 60), (Clef.Bass, 60), (Clef.Treble, 67), (Clef.Bass, 53), (Clef.Treble, 72), (Clef.Bass, 48), (Clef.Treble, 84), (Clef.Bass, 36)], order.Take(8));
        Assert.All(order, o => Assert.Contains(o.Midi % 12, new[] { 0, 2, 4, 5, 7, 9, 11 }));
        // The ledger lines come after every note on the staff.
        var firstLedger = order.Select((o, i) => (o, i)).First(x => PassageGenerator.LedgerLines(new Key(0).Spell(x.o.Midi), x.o.Clef == Clef.Treble ? 1 : 2) > 0 && x.i >= 8).i;
        Assert.True(order.Skip(firstLedger).All(o => PassageGenerator.LedgerLines(new Key(0).Spell(o.Midi), o.Clef == Clef.Treble ? 1 : 2) > 0));
    }

    [Fact]
    public void TheSessionPutsShakyNotesFirstMixesConfusablesAndAddsNewNotes()
    {
        var cards = new List<NoteCard>
        {
            new(Clef.Treble, 67, 3, Today, Today.AddDays(-9), null),
            new(Clef.Treble, 71, 2, Today, Today.AddDays(-9), null),
            new(Clef.Bass, 57, 2, Today.AddDays(-1), Today.AddDays(-9), null),
            new(Clef.Treble, 76, 2, Today, Today.AddDays(-9), null),
            new(Clef.Treble, 60, 4, Today.AddDays(3), Today.AddDays(-9), null),
        };
        var yesterday = new DateTimeOffset(Today.AddDays(-1).ToDateTime(new TimeOnly(18, 0)));
        var answers = new List<NoteAnswer> { new(yesterday, Clef.Treble, 76, false, 900), new(yesterday, Clef.Treble, 67, true, 800), new(yesterday, Clef.Treble, 71, true, 700) };

        var session = NoteSchedule.Session(cards, answers, Today, 1, new HashSet<Clef> { Clef.Treble, Clef.Bass }, new Rng(1));

        Assert.Equal((Clef.Treble, 76), session[0]);
        // B4 in treble and D3 in bass sit on the same line: they come together.
        var b4 = session.IndexOf((Clef.Treble, 71));
        var d3 = session.IndexOf((Clef.Bass, 57));
        Assert.Equal(1, Math.Abs(b4 - d3));
        Assert.DoesNotContain((Clef.Treble, 60), session.Take(4));
        // Yesterday was 2 of 3, under 85%: no new notes today.
        Assert.Equal(4, session.Count);
        Assert.All(session.Zip(session.Skip(1)), p => Assert.NotEqual(p.First, p.Second));
    }

    [Fact]
    public void AfterAGoodDayANewNoteComesBetweenContrastingOnes()
    {
        var cards = new List<NoteCard> { new(Clef.Treble, 60, 3, Today.AddDays(2), Today, null), new(Clef.Treble, 67, 3, Today.AddDays(2), Today, null) };

        var session = NoteSchedule.Session(cards, [], Today, 1, new HashSet<Clef> { Clef.Treble }, new Rng(1));

        Assert.Equal([(Clef.Treble, 72), (Clef.Treble, 60), (Clef.Treble, 72)], session);
    }

    [Fact]
    public void ReadingMovesOnTheMeanOfThree()
    {
        var state = new ReadingState(5, 4, 70, [], 0, 2);
        (ReadingState, Verdict) Play(ReadingState s, double inTime, bool kept = true) => Reading.After(s, inTime, kept);

        var (one, verdict) = Play(state, 0.95);
        Assert.Equal(Verdict.Hold, verdict);
        var (two, _) = Play(one, 0.92);
        var (three, good) = Play(two, 0.95);
        Assert.Equal(Verdict.Good, good);
        Assert.Equal(74, three.Tempo);

        var (held, hold) = Play(Play(Play(three, 0.85).Item1, 0.82).Item1, 0.88);
        Assert.Equal(Verdict.Hold, hold);
        Assert.Equal(74, held.Tempo);

        // The last three are 0.82, 0.88 and 0.71: a mean just over 0.8, still a hold. Another 0.7 brings it under.
        var (still, stillHold) = Play(held, 0.71);
        Assert.Equal(Verdict.Hold, stillHold);
        var (slower, tooHard) = Play(still, 0.7);
        Assert.Equal(Verdict.TooHard, tooHard);
        Assert.Equal(67, slower.Tempo);
    }

    [Fact]
    public void StoppingDropsTheTempoAtOnce()
    {
        var (state, verdict) = Reading.After(new ReadingState(5, 4, 80, [], 0, 2), 0.95, keptGoing: false);

        Assert.Equal(Verdict.TooHard, verdict);
        Assert.Equal(72, state.Tempo);
        Assert.Equal(65, Reading.After(state, 0.5, true).State.Tempo);
    }

    [Fact]
    public void ANewLevelBringsOneKindOfDifficultyAtATime()
    {
        var state = new ReadingState(5, 4, Ladder.Level(5).TopTempo, [], 0, 2);
        ReadingState Good(ReadingState s) => Reading.After(Reading.After(Reading.After(s, 0.95, true).State, 0.95, true).State, 0.95, true).State;

        var next = Good(state);
        Assert.Equal((6, 1, 60.0), (next.Level, next.Step, next.Tempo));
        Assert.Equal(2, Good(next).Step);
        Assert.Equal(4, Good(Good(Good(next))).Step);
        Assert.Equal(63, Good(Good(Good(Good(next)))).Tempo);
    }

    [Fact]
    public void AtTheBottomTwoMoreTooHardResultsStepBack()
    {
        var floor = new ReadingState(6, 3, 60, [], 0, 2);

        var once = Reading.After(floor, 0.5, true).State;
        Assert.Equal((3, 1), (once.Step, once.TooHardAtFloor));
        var twice = Reading.After(once, 0.5, true).State;
        Assert.Equal((6, 2), (twice.Level, twice.Step));
        var fromFirstStep = Reading.After(Reading.After(new ReadingState(6, 0, 60, [], 0, 2), 0.5, true).State, 0.5, true).State;
        Assert.Equal((5, 3), (fromFirstStep.Level, fromFirstStep.Step));
    }

    [Fact]
    public void FindingALevelClimbsByTwoUntilItFallsUnder80()
    {
        Assert.Equal((3, (int?)null), Reading.FindLevel(1, [0.95, 0.9]));
        Assert.Equal(((int?)null, 4), Reading.FindLevel(5, [0.8, 0.7]));
        Assert.Equal(((int?)null, 1), Reading.FindLevel(1, [0.5, 0.6]));
        Assert.Equal(((int?)null, 12), Reading.FindLevel(12, [0.95, 0.95]));
    }

    [Fact]
    public void ReadAheadNarrowsAndWidensButKeepsASecondAndAHalf()
    {
        // At 120 beats a minute in 4/4 a bar is 2 s, so the least is one bar.
        Assert.Equal(1.5, Reading.ReadAheadAfter(2, Verdict.Good, 120, 4));
        Assert.Equal(1, Reading.ReadAheadAfter(1, Verdict.Good, 120, 4));
        Assert.Equal(2.5, Reading.ReadAheadAfter(2, Verdict.TooHard, 120, 4));
        Assert.Equal(2, Reading.ReadAheadAfter(2, Verdict.Hold, 120, 4));
        // At 60 in 2/4 a bar is 2 s too; at 180 in 2/4, 0.67 s, so the least is two and a half bars... rounded to half bars: 2.5.
        Assert.Equal(2.5, Reading.ReadAheadAfter(1, Verdict.Good, 180, 2));
    }

    [Fact]
    public void TheRhythmClickFadesWithinASetAndBetweenSets()
    {
        var state = RhythmState.Start;

        Assert.Equal([ClickStage.Throughout, ClickStage.CountInOnly, ClickStage.Silent, ClickStage.Silent], Enumerable.Range(0, 4).Select(state.StageForTry));
        Assert.Equal(ClickStage.CountInOnly, state.After(0.95).Stage);
        Assert.Equal(new RhythmState(2, ClickStage.Throughout), new RhythmState(1, ClickStage.Silent).After(0.95));
        Assert.Equal(new RhythmState(1, ClickStage.CountInOnly), new RhythmState(2, ClickStage.Throughout).After(0.5));
        Assert.Equal(state, state.After(0.85));
    }

    [Fact]
    public void OtherDrillsStepUpAfterTwoGoodSets()
    {
        Assert.Equal((2, 0.0), DrillLadder.After(2, 0.85, 0.95, 4) with { LastShare = 0 } is var held ? (held.Step, 0.0) : default);
        Assert.Equal(3, DrillLadder.After(2, 0.95, 0.95, 4).Step);
        Assert.Equal(1, DrillLadder.After(2, 0.95, 0.7, 4).Step);
        Assert.Equal(4, DrillLadder.After(4, 0.95, 0.95, 4).Step);
    }

    [Fact]
    public void TechniqueStartsBelowYourSpeedAndRisesOnEvenRuns()
    {
        var state = TechniqueState.New.Comfortable(100, Today);
        Assert.Equal(85, state.Tempo);
        // Never a tempo the click can't be set to, whatever the first run's pace came to.
        Assert.Equal(240, TechniqueState.New.Comfortable(3000, Today).Tempo);
        Assert.Equal(20, TechniqueState.New.Comfortable(10, Today).Tempo);

        // The first week sets the target (the median, never under 8%).
        foreach (var sd in new[] { 12.0, 10, 11 }) state = state.After(true, sd, Today);
        Assert.Null(state.Target);
        var later = Today.AddDays(8);
        state = state.After(true, 9, later);
        Assert.Equal(11, state.Target);
        state = state.After(true, 9, later).After(true, 13, later).After(true, 10, later);
        Assert.Equal(89, state.Tempo);

        var slipping = state.After(false, null, later).After(false, null, later);
        Assert.Equal(80, slipping.Tempo);
    }

    [Fact]
    public void TheDailyPlanRotatesAndHasAFiveMinuteVersion()
    {
        var plan = DailyPlan.Build(Today, 20, 5, true);
        Assert.Equal([StepKind.Technique, StepKind.Notes, StepKind.Rhythm, StepKind.Pause, StepKind.SightReading, StepKind.OneSpot], plan.Steps.Select(s => s.Kind));
        Assert.Equal(20, plan.Steps.Sum(s => s.Minutes));
        // Whatever the day's minutes, the plan's add up to them, the pause too.
        Assert.All(new[] { 10, 15, 25, 30, 45, 60 }, minutes => Assert.Equal(minutes, DailyPlan.Build(Today, minutes, 5, true).Steps.Sum(s => s.Minutes)));

        var tomorrow = DailyPlan.Build(Today.AddDays(1), 20, 5, true);
        Assert.NotEqual(plan.ScaleKey, tomorrow.ScaleKey);
        Assert.NotEqual(plan.Variant, tomorrow.Variant);
        Assert.Equal(Variant.ReadAhead, DailyPlan.Build(Today, 20, 5, false).Variant);

        var quick = DailyPlan.Build(Today, 20, 5, true, shortPlan: true);
        Assert.Equal([StepKind.Notes, StepKind.SightReading], quick.Steps.Select(s => s.Kind));
        Assert.NotNull(DailyPlan.GoGradually(30, 20));
        Assert.Null(DailyPlan.GoGradually(24, 20));
    }

    [Fact]
    public void TheStreakEarnsFreezesAndUsesThemOnMissedDays()
    {
        var days = Enumerable.Range(0, 14).Select(i => new PracticeDayRecord(Today.AddDays(-20 + i), 15, false)).ToList();
        // Practised 14 days in a row (two freezes earned), then missed two days, then practised again, then missed one more.
        days.AddRange([new(Today.AddDays(-4), 12, false), new(Today.AddDays(-3), 2, true), new(Today.AddDays(-1), 3, false)]);

        var streak = Streak.Compute(days, Today);

        Assert.Equal(0, streak.Days);
        Assert.Equal(2, streak.FreezesUsed.Count);
        Assert.Equal(Today.AddDays(-3), streak.LastPractised);
        Assert.Equal(2, streak.DaysThisWeek);
        // This week's minutes are from Monday, as You's weeks are: Monday's 2 and yesterday's 3, not Sunday's 12.
        Assert.Equal(5, streak.MinutesThisWeek);
        Assert.False(streak.WeekKept);
        Assert.StartsWith("Last practised", streak.BreakNote(Today));
    }

    [Fact]
    public void AMissedDayIsCoveredByAFreeze()
    {
        var days = Enumerable.Range(0, 9).Where(i => i != 7).Select(i => new PracticeDayRecord(Today.AddDays(-8 + i), 20, false)).ToList();

        var streak = Streak.Compute(days, Today);

        Assert.Equal(8, streak.Days);
        Assert.Equal([Today.AddDays(-1)], streak.FreezesUsed);
        Assert.True(streak.WeekKept);
        Assert.Null(streak.BreakNote(Today));
    }
}

public class NoteStreamTests
{
    private static List<(Clef Clef, Spelled Pitch)> Take(NoteStream stream, int count) => [.. Enumerable.Range(0, count).Select(_ => stream.Next())];

    [Theory]
    [InlineData(false, 0)]
    [InlineData(true, 2)]
    public void NotesStayOnTheStaffOrOutToTwoLedgerLines(bool ledgerLines, int most)
    {
        var notes = Take(new NoteStream([Clef.Treble, Clef.Bass], ledgerLines, false, new Rng(3)), 600);

        Assert.All(notes, n => Assert.InRange(PassageGenerator.LedgerLines(n.Pitch, n.Clef == Clef.Treble ? 1 : 2), 0, most));
        // Every line and space of the range comes up.
        Assert.Equal(2 * (NoteStream.Range(Clef.Treble, ledgerLines).High - NoteStream.Range(Clef.Treble, ledgerLines).Low + 1), notes.Distinct().Count());
        Assert.Contains(notes, n => PassageGenerator.LedgerLines(n.Pitch, n.Clef == Clef.Treble ? 1 : 2) == most);
    }

    [Fact]
    public void TheSameLineOrSpaceNeverComesTwiceRunning()
    {
        var notes = Take(new NoteStream([Clef.Treble], false, true, new Rng(5)), 500);

        Assert.All(notes.Zip(notes.Skip(1)), p => Assert.NotEqual(p.First.Pitch.Diatonic, p.Second.Pitch.Diatonic));
    }

    [Fact]
    public void SharpsAndFlatsAreAboutAThirdAndNeverOnWhiteKeys()
    {
        var notes = Take(new NoteStream([Clef.Treble, Clef.Bass], true, true, new Rng(7)), 900);
        var altered = notes.Where(n => n.Pitch.Alter != 0).ToList();

        Assert.InRange(altered.Count / 900.0, 0.27, 0.40);
        Assert.Contains(altered, n => n.Pitch.Alter == 1);
        Assert.Contains(altered, n => n.Pitch.Alter == -1);
        Assert.DoesNotContain(altered, n => (n.Pitch.Step, n.Pitch.Alter) is (Step.E, 1) or (Step.B, 1) or (Step.C, -1) or (Step.F, -1));
        Assert.All(Take(new NoteStream([Clef.Bass], true, false, new Rng(7)), 300), n => Assert.Equal(0, n.Pitch.Alter));
    }

    [Fact]
    public void AMissedNoteComesBackThreeToFiveNotesLater()
    {
        var stream = new NoteStream([Clef.Treble], true, false, new Rng(11));
        var missed = stream.Next();
        stream.Answer(false, 2000);

        var next = Take(stream, 5);
        var back = next.FindIndex(n => n == missed);
        Assert.InRange(back + 1, 3, 5);
    }

    [Fact]
    public void ThePaceIsTheMedianOfTheLastTenRightAnswersAndTheBestOfThose()
    {
        var stream = new NoteStream([Clef.Treble], false, false, new Rng(13));
        foreach (var ms in new[] { 1500.0, 1400, 1300, 1200, 1100 })
        {
            stream.Next();
            stream.Answer(true, ms);
        }
        Assert.Equal(1300, stream.RecentMs);
        Assert.Null(stream.BestMs);

        // A wrong answer counts as an answer, not in the pace.
        stream.Next();
        stream.Answer(false, 400);
        Assert.Equal(400, stream.LastMs);
        Assert.Equal(1300, stream.RecentMs);

        foreach (var ms in new[] { 1000.0, 900, 800, 700, 600, 2000, 2100, 2200, 2300, 2400 })
        {
            stream.Next();
            stream.Answer(true, ms);
        }
        Assert.Equal(15, stream.Right);
        Assert.Equal(16, stream.Answers.Count);
        // The last ten right are 1000 to 600 and 2000 to 2400, median 1500; the best ten were the first ten, median 1050.
        Assert.Equal(1500, stream.RecentMs);
        Assert.Equal(1050, stream.BestMs);
    }
}

public class GeographyTests
{
    [Fact]
    public void TheOctaveStepLeapsASixthToANinthEitherWay()
    {
        var rng = new Rng(17);
        var targets = Enumerable.Range(0, 300).Select(_ => Prima.Core.Generation.Drills.Geography(3, rng).Answer[0][0]).ToList();
        var middleC = new Spelled(Step.C, 0, 4).Diatonic;

        Assert.Equal(8, targets.Distinct().Count());
        Assert.All(targets, m => Assert.InRange(Math.Abs(new Key(0).Spell(m).Diatonic - middleC), 5, 8));
    }
}

public class PieceHistoryTests
{
    private static readonly DateOnly Monday = new(2026, 11, 9);

    // A run In time from the piece's start, played to its end, on a day at a minute: its tempo and how much was right in time.
    private static PieceRun Run(int day, int minute, double tempo, double rightInTime, int notes = 95, int from = 1, bool stopped = false, bool inTime = true)
    {
        var date = Monday.AddDays(day);
        return new PieceRun(new DateTimeOffset(date.ToDateTime(new TimeOnly(19, minute)), TimeSpan.Zero), date, inTime, inTime ? tempo : null, "both", from, stopped, stopped ? 9 : null, (int)(notes * rightInTime), notes, inTime ? rightInTime : null);
    }

    [Fact]
    public void APiecesRunsSayHowOftenAndTheLastOne()
    {
        var history = PieceHistory.Of([Run(0, 10, 60, 0.7), Run(0, 20, 60, 0.8), Run(2, 5, 72, 0.9, from: 13, stopped: true)])!;

        Assert.Equal((3, 2, Monday), (history.Runs, history.Days, history.Since));
        Assert.Equal((Monday.AddDays(2), 13, true), (history.Last.Day, history.Last.From, history.Last.Stopped));
    }

    [Fact]
    public void PlayedThroughIsFromTheStartToTheEndInTime()
    {
        // From a later bar, round a loop until stopped, and in Wait for me aren't the piece played through.
        var history = PieceHistory.Of([Run(0, 10, 60, 0.7), Run(0, 20, 60, 0.5, from: 13, stopped: true), Run(1, 10, 60, 0.6, stopped: true), Run(1, 20, 0, 0.95, inTime: false), Run(2, 10, 72, 0.8)])!;

        Assert.Equal(2, history.Throughs);
        Assert.Equal((72, 0.8), (history.LastThrough!.Tempo, history.LastThrough.RightInTime));
        Assert.Equal(history.LastThrough, history.BestThrough);
        // A pickup's bar is 0: a piece that starts on one is played through from there.
        Assert.True(Run(0, 0, 60, 1, from: 0).Through);
        // Round a loop from the first bar, every time round played with no stop, isn't either.
        Assert.False((Run(0, 0, 60, 1) with { To = 4 }).Through);
    }

    [Fact]
    public void TheBestTimeThroughIsTheMostRightInTimeAndOfTwoAsGoodTheFaster()
    {
        var history = PieceHistory.Of([Run(0, 10, 60, 0.9), Run(1, 10, 84, 0.9), Run(2, 10, 96, 0.7)])!;

        Assert.Equal(84, history.BestThrough!.Tempo);
        Assert.Equal(96, history.LastThrough!.Tempo);
    }

    [Fact]
    public void AFalseStartIsNotARun()
    {
        // Fewer than eight notes reached: a start that came to nothing. Only false starts, and there's no history yet.
        Assert.Null(PieceHistory.Of([Run(0, 10, 60, 1, notes: 7), Run(0, 11, 60, 0.5, notes: 3)]));
        var history = PieceHistory.Of([Run(0, 10, 60, 1, notes: 7), Run(0, 12, 60, 0.75, notes: 8)])!;

        Assert.Equal((1, 8), (history.Runs, history.Last.Notes));
    }

    [Fact]
    public void ALoopOfAFewNotesIsARunByAllItsPasses()
    {
        // One bar of four notes, five times round: twenty notes reached, though its figures are the last pass's four.
        var loop = Run(0, 10, 60, 1, notes: 4, from: 13, stopped: true) with { To = 13, Passes = [.. Enumerable.Repeat(new PiecePass(3, 4, 0.75), 4), new PiecePass(4, 4, 1)] };

        Assert.Equal(20, loop.Reached);
        Assert.Equal(1, PieceHistory.Of([loop])!.Runs);
    }
}
