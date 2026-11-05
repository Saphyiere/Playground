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
        Assert.InRange(plan.Steps.Sum(s => s.Minutes), 19, 22);

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
