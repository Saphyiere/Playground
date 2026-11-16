using Prima.Core.Practice;

namespace Prima.Core.Tests;

/// <summary>
/// A practice session in words (the look back): the kind of work it was, what it came to, and what to do next, from runs as a
/// piece keeps them. The piece is 32 bars for two hands, eight notes a bar, that goes at 115.
/// </summary>
public class SessionStoryTests
{
    private static readonly DateTimeOffset Start = new(2026, 11, 15, 18, 0, 0, TimeSpan.Zero);
    private static readonly DateOnly Today = DateOnly.FromDateTime(Start.Date);
    private static readonly PieceShape Piece = new(1, 32, 115) { Notes = [.. Enumerable.Repeat(8, 32)] };

    // A run In time from a bar to the end: so many notes right of its notes, and its share right in time.
    private static PieceRun Timed(double minute, double tempo, int right, int notes, double share, int from = 1, long session = 2, params BarTrouble[] trouble) =>
        new(Start.AddMinutes(minute), DateOnly.FromDateTime(Start.AddMinutes(minute).Date), true, tempo, "both", from, false, null, right, notes, share) { Session = session, Trouble = trouble, Seconds = 100, Timing = new(-12, 4) };

    // Round a loop In time: each pass's notes right of 32 and its share right in time.
    private static PieceRun Loop(double minute, double tempo, int from, int to, params (int Right, double Share)[] passes) => Loop(minute, tempo, from, to, [.. passes.Select(p => new PiecePass(p.Right, 32, p.Share))]);

    private static PieceRun Loop(double minute, double tempo, int from, int to, PiecePass[] passes) =>
        new(Start.AddMinutes(minute), DateOnly.FromDateTime(Start.AddMinutes(minute).Date), true, tempo, "both", from, true, to, passes[^1].Right, passes[^1].Notes, passes[^1].RightInTime) { Session = 2, To = to, Passes = passes, Seconds = 60, Timing = new(-12, 4) };

    // Round a loop in Wait for me, a hand or both: each pass's notes right first time of its notes.
    private static PieceRun Waited(double minute, string hands, int from, int to, int notes, long session, params int[] right) =>
        new(Start.AddMinutes(minute), DateOnly.FromDateTime(Start.AddMinutes(minute).Date), false, null, hands, from, true, to, right[^1], notes, null)
        {
            Session = session, To = to, Seconds = 90, Passes = [.. right.Select(r => new PiecePass(r, notes, null) { Seconds = 30 })],
        };

    [Fact]
    public void AFirstLookIsReadingAndWhatsNextIsTheHardBarThenTogetherThenNewBars()
    {
        var wrong2 = new BarTrouble(2, 1, 0, 0, 0);
        PieceRun[] session =
        [
            Waited(2, "right", 1, 8, 33, 2, 24, 28, 30),
            Waited(6, "left", 1, 8, 17, 2, 10, 14),
            Waited(10, "both", 1, 4, 23, 2, 14, 16, 18) with { Passes = [new(14, 23, null) { Trouble = [wrong2] }, new(16, 23, null) { Trouble = [wrong2] }, new(18, 23, null) { Trouble = [wrong2] }] },
        ];

        var story = SessionStory.Of(session, [], Piece, Today);

        Assert.Equal(Work.Reading, story.Work);
        Assert.Equal("A first look: bars 1–8 a hand at a time, bars 1–4 together. Together, bars 1–4 ended at 18 of 23 notes right first time.", story.Headline);
        // Bar 2 was played eight times, the last two (its last third) hands together, each with a wrong note. Nothing was
        // played before today to set it against.
        Assert.Equal(
        [
            new("Moved", Tint.Good, "Bars 1–8 (right hand): 24 of 33 → 30 of 33 right first time over 3 passes."),
            new("Still hard", Tint.Bad, "Bar 2: still going wrong in 2 of its last 2 times, mostly wrong notes."),
        ], story.Lines);
        Assert.Equal(
        [
            "Bars 1–3, hands together, Wait for me",
            "Bars 5–8, hands together, Wait for me",
            "Bars 9–12, right hand, Wait for me",
        ], story.Steps.Select(s => s.What));
        Assert.Equal((1, 3, "both", false), (story.Steps[0].From, story.Steps[0].To, story.Steps[0].Hands, story.Steps[0].InTime));
        Assert.Equal([1, 2, 3, 4, 5, 6, 7, 8], story.Today.Order());
        Assert.Equal(8, story.Standing.Count(BarState.Reading));
        Assert.Equal(24, story.Standing.Count(BarState.NotPlayed));
    }

    [Fact]
    public void WhatStayedOvernightIsTheFirstGoTodayAgainstWhereItWasLeft()
    {
        PieceRun[] yesterday = [Waited(-1440, "right", 1, 8, 33, 1, 22, 27, 30)];
        var stayed = SessionStory.Of([Waited(2, "right", 1, 8, 33, 2, 29, 31), Waited(6, "right", 9, 12, 17, 2, 9, 14)], yesterday, Piece, Today);
        var went = SessionStory.Of([Waited(2, "right", 1, 8, 33, 2, 24, 30)], yesterday, Piece, Today);
        var gone = SessionStory.Of([Waited(2, "right", 1, 8, 33, 2, 24, 26)], yesterday, Piece, Today);

        Assert.Equal(new StoryLine("Since yesterday", Tint.Good, "Bars 9–12 are new; bars 1–8 (right hand) left at 30 of 33, and 29 of 33 the first go today: it stayed."), stayed.Lines.Single(l => l.About == "Since yesterday"));
        Assert.Equal("Bars 1–8 (right hand) left at 30 of 33, and 24 of 33 the first go today: some of it went, and came back.", went.Lines.Single(l => l.About == "Since yesterday").Words);
        Assert.Equal(new StoryLine("Since yesterday", Tint.Plain, "Bars 1–8 (right hand) left at 30 of 33, and 24 of 33 the first go today: some of it went."), gone.Lines.Single(l => l.About == "Since yesterday"));
    }

    [Fact]
    public void WithTheNotesMostlyThereItsWhetherItHoldsTogetherAndTheBarStillWrongANotchSlower()
    {
        var wrong21 = new BarTrouble(21, 2, 0, 1, 0);
        PieceRun[] before = [Timed(-1440, 66, 440, 512, 0.76, session: 1)];
        PieceRun[] session =
        [
            Timed(2, 72, 450, 512, 0.78, trouble: wrong21),
            Loop(8, 66, 5, 8, (22, 0.60), (26, 0.72), (28, 0.80), (30, 0.91)),
            Loop(12, 72, 5, 8, (28, 0.80), (27, 0.79), (27, 0.78)),
            Timed(18, 72, 470, 512, 0.86, trouble: wrong21),
        ];

        var story = SessionStory.Of(session, before, Piece, Today);

        Assert.Equal(Work.Securing, story.Work);
        Assert.Equal("It holds together at 72: 86% right in time the last time through, from 78% the first.", story.Headline);
        Assert.Equal(
        [
            new("Moved", Tint.Good, "Bars 5–8 at 66: 60% → 91% right in time over 4 passes."),
            new("Still hard", Tint.Bad, "Bar 21: wrong notes the last time it was played."),
            new("Too fast", Tint.Plain, "At 72, bars 5–8 dropped to 78%, from 91% at 66: 66 is where it holds for now."),
            new("Since yesterday", Tint.Good, "The whole piece at 72 (86% right in time), from 66 (76%)."),
        ], story.Lines);
        Assert.False(story.Tired);
        Assert.Equal(
        [
            new NextStep(20, 22, "both", true, 69, "Bars 20–22, In time at 69", "Bar 21 was still going wrong at the end, at 72. A notch slower, until it's clean three times running."),
            new NextStep(5, 8, "both", true, 66, "Bars 5–8, In time at 66", "It dropped to 78% at 72: 66 is where it holds for now."),
            new NextStep(1, null, "both", true, 72, "The whole piece, In time at 72", "86% right in time at 72 today. First thing next time, before anything else: the first try of the day is the honest one."),
        ], story.Steps);
        Assert.Equal(72, story.Standing.Held);
    }

    [Fact]
    public void WithTheNotesThereItsTheTimingWhichWayItsOutAndWhereAndTheMarkings()
    {
        // Two times through at 92: the notes all but all right, an eighth of them off the beat the first time, early, in bars
        // 5-6; a wrong note in bar 9 that time, and none the last.
        BarTrouble[] first = [new(5, 0, 0, 30, 0, Ahead: 28), new(6, 0, 0, 30, 0, Ahead: 27), new(9, 1, 0, 0, 6)];
        BarTrouble[] last = [new(5, 0, 0, 14, 0, Ahead: 13), new(6, 0, 0, 12, 0, Ahead: 12)];
        MarkPlayed[] marks = [new(Marking.Dynamic, 1, 16, 1, 0, "mf"), new(Marking.Dynamic, 17, 24, 1, 1, "f"), new(Marking.Swell, 21, 22, 1, 1, "crescendo"), new(Marking.Staccato, 4, 4, 4, 1), new(Marking.Staccato, 10, 10, 2, 0)];
        PieceRun[] session =
        [
            Timed(2, 92, 500, 512, 0.84, trouble: first),
            Timed(12, 92, 506, 512, 0.93, trouble: last) with { Marked = marks },
        ];

        var story = SessionStory.Of(session, [], Piece, Today);

        Assert.Equal(Work.Shaping, story.Work);
        Assert.Equal("The notes are there at 92 of the piece's 115 (98% right). What's left is the timing, the touch and the loud and soft.", story.Headline);
        Assert.Equal(
        [
            new("Timing", Tint.Bad, "91% of the right notes in time. What's out is early: it runs ahead in bars 5–6. Through at 92: 88% the first time, 95% the last."),
            new("Loud and soft", Tint.Bad, "1 of the 3 dynamics and hairpins came out as marked. Not yet: the f at bar 17 and the crescendo in bars 21–22."),
            new("Touch", Tint.Bad, "1 of 6 staccato notes held long (bar 4)."),
        ], story.Lines);
        Assert.Equal(
        [
            new NextStep(5, 6, "both", true, 84, "Bars 5–6, In time at 84", "It runs ahead of the click here. In time three passes running, then back up a notch at a time."),
            new NextStep(21, 22, "both", true, 92, "Bars 21–22, In time at 92", "The crescendo in bars 21–22 hasn't come out yet."),
            new NextStep(1, null, "both", true, 96, "The whole piece, In time at 96", "93% right in time at 92: over 90%, a notch up."),
        ], story.Steps);
    }

    [Fact]
    public void AtThePiecesOwnTempoItsTheTimesThroughTheColdOneAndTheBarThatKeepsSlipping()
    {
        var slip29 = new BarTrouble(29, 1, 0, 0, 0);
        PieceRun[] before = [Timed(-1440, 115, 500, 512, 0.96, session: 1)];
        PieceRun[] session =
        [
            Timed(2, 115, 504, 512, 0.96, trouble: slip29),
            Timed(6, 115, 509, 512, 0.99, trouble: slip29),
            Timed(10, 115, 512, 512, 0.99),
            Timed(14, 120, 500, 512, 0.93, trouble: slip29),
        ];

        var story = SessionStory.Of(session, before, Piece, Today);

        Assert.Equal(Work.Performing, story.Work);
        Assert.Equal("3 times through at 115, the piece's own tempo: 96% to 99% right in time, once with every note right.", story.Headline);
        Assert.Equal(
        [
            new("First try", Tint.Plain, "Cold, the first time through was 96%; after that, 99%. The cold one is the one that tells."),
            new("Slips", Tint.Bad, "Bar 29 had a note wrong in 3 of the 4 times through."),
            new("Faster", Tint.Good, "Through at 120: 93% right in time."),
            new("Since yesterday", Tint.Good, "The whole piece at 115: 99% right in time, from 96%."),
        ], story.Lines);
        Assert.Equal(["Bars 28–30, In time at 115", "The whole piece, In time at 115"], story.Steps.Select(s => s.What));
        Assert.Equal(32, story.Standing.Count(BarState.AtTempo));
    }

    [Fact]
    public void AnEndThatWentWorseThanBeforeIsTirednessAndTheSessionIsJudgedByItsBest()
    {
        PieceRun[] session =
        [
            Timed(2, 72, 460, 512, 0.80),
            Loop(8, 72, 21, 24, (26, 0.70), (28, 0.80), (29, 0.86)),
            Timed(14, 72, 472, 512, 0.84),
            Loop(20, 72, 9, 12, (27, 0.80), (29, 0.88), (30, 0.92)),
            // Half an hour on: the same things, worse.
            Loop(34, 72, 21, 24, (26, 0.72), (24, 0.66), (22, 0.60)),
            Timed(40, 72, 440, 512, 0.66),
        ];

        var story = SessionStory.Of(session, [], Piece, Today);

        Assert.True(story.Tired);
        Assert.Equal("Nearly holding together at 72: 84% right in time at best today (66% the last time through, at the end).", story.Headline);
        Assert.Equal(new StoryLine("The end", Tint.Plain, "The last 10 minutes or so went worse than what came before: the whole piece at 72, 82% → 66%; bars 21–24 at 72, 79% → 66%. That's more likely tiredness than the bars."), story.Lines[^1]);
        // What's next goes by the best time through, not the tired one: 84% is the same again, not a notch down.
        Assert.Equal(new NextStep(1, null, "both", true, 72, "The whole piece, In time at 72", "84% right in time at 72 today. First thing next time, before anything else: the first try of the day is the honest one."), story.Steps[^1]);
    }

    [Fact]
    public void AStretchJustTakenAStepFasterThatSinksIsTooFastNotTired()
    {
        PieceRun[] session =
        [
            Timed(2, 72, 460, 512, 0.80),
            Loop(10, 72, 5, 8, (27, 0.80), (29, 0.88), (30, 0.92)),
            Timed(20, 72, 470, 512, 0.84),
            Loop(36, 80, 5, 8, (28, 0.84), (26, 0.74), (24, 0.66)),
        ];

        var story = SessionStory.Of(session, [], Piece, Today);

        Assert.False(story.Tired);
        Assert.Contains(new StoryLine("Too fast", Tint.Plain, "At 80, bars 5–8 dropped to 66%, from 92% at 72: 72 is where it holds for now."), story.Lines);
    }

    [Fact]
    public void RunsStoppedOnTheWayAreTheBarsTheyGotThroughNotTheWholePiece()
    {
        // Stopped at bar 9, twice: bars 1-8.
        PieceRun Stopped(double minute, int right, double share) => Timed(minute, 50, right, 64, share) with { Stopped = true, Bar = 9 };
        PieceRun[] session = [Waited(2, "both", 1, 8, 64, 2, 40, 50, 56), Stopped(10, 40, 0.50), Stopped(14, 46, 0.62), Stopped(18, 50, 0.70)];

        var story = SessionStory.Of(session, [], Piece, Today);

        Assert.Equal(Work.Reading, story.Work);
        Assert.Equal("A first look: bars 1–8, hands together. With the click at 50, bars 1–8 got to 70% right in time.", story.Headline);
        Assert.Equal([1, 2, 3, 4, 5, 6, 7, 8], story.Today.Order());
        Assert.False(story.Standing.Through);
    }

    [Fact]
    public void WhatsNextOnAPiecesFrontIsTheLastSessionsStory()
    {
        PieceRun[] runs =
        [
            Timed(-2880, 60, 400, 512, 0.70, session: 1),
            Timed(-1440, 66, 470, 512, 0.86, session: 2, trouble: new BarTrouble(14, 1, 1, 0, 0)),
            // Played through from Play since: not a practice session's.
            Timed(-60, 66, 480, 512, 0.90) with { Session = null, Play = true },
        ];

        var next = SessionStory.Next(runs, Piece, Today);

        Assert.Equal(Today.AddDays(-1), next!.Value.Day);
        Assert.Equal(["Bars 13–15, In time at 63", "The whole piece, In time at 66"], next.Value.Steps.Select(s => s.What));
        Assert.Null(SessionStory.Next([], Piece, Today));
    }

    [Fact]
    public void NothingPlayedToAnEndHasNoStory()
    {
        var story = SessionStory.Of([], [Timed(-1440, 66, 470, 512, 0.86, session: 1)], Piece, Today);

        Assert.Equal("Nothing was played to the end of a run or a pass this time.", story.Headline);
        Assert.Empty(story.Lines);
        Assert.Empty(story.Steps);
        Assert.Equal(32, story.Standing.Count(BarState.InTime));
    }
}
