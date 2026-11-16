using Prima.Core.Practice;

namespace Prima.Core.Tests;

/// <summary>
/// From the review of main at a7ab94d: what the look back should say and ask for at its edges. Each fails on that code; the place
/// in it is given with each. The piece is SessionStoryTests': 32 bars for two hands, eight notes a bar, that goes at 115.
/// </summary>
public class LookBackEdgeTests
{
    private static readonly DateTimeOffset Start = new(2026, 11, 15, 18, 0, 0, TimeSpan.Zero);
    private static readonly DateOnly Today = DateOnly.FromDateTime(Start.Date);
    private static readonly PieceShape Piece = new(1, 32, 115) { Notes = [.. Enumerable.Repeat(8, 32)] };

    // A run In time from bar 1 to the end: so many notes right of its notes, and its share right in time.
    private static PieceRun Timed(double minute, double tempo, int right, int notes, double share, params BarTrouble[] trouble) =>
        new(Start.AddMinutes(minute), Today, true, tempo, "both", 1, false, null, right, notes, share) { Session = 2, Trouble = trouble, Seconds = 100, Timing = new(-12, 4) };

    // Round a loop In time: so many passes, each with every note of its 32 right, and its share right in time.
    private static PieceRun Loop(double minute, double tempo, int from, int to, int passes, double share) =>
        new(Start.AddMinutes(minute), Today, true, tempo, "both", from, true, to, 32, 32, share)
        {
            Session = 2, To = to, Seconds = 60, Timing = new(-12, 4), Passes = [.. Enumerable.Repeat(new PiecePass(32, 32, share), passes)],
        };

    private static string Said(SessionStory story) => string.Join(" | ", story.Lines.Select(l => $"{l.About} ({l.Tint}): {l.Words}").Concat(story.Steps.Select(s => $"NEXT {s.What}. {s.Why}")));

    // SessionReview.cs:134 keeps the three bars with most to fix, notes off the beat counted with the wrong ones, and
    // SessionStory.cs:300-301 and :641 look for a bar with a wrong note among those three only. Three bars with four notes off
    // the beat each outweigh a bar with a wrong note, so the look back says "No bar had a wrong note left in it by the end." of a
    // session that left one, and asks for nothing on it.
    [Fact]
    public void ABarLeftWithAWrongNoteIsNamedWhenThreeBarsOffTheBeatWeighMore()
    {
        var story = SessionStory.Of([Timed(2, 72, 470, 512, 0.86, new(3, 0, 0, 4, 0), new(4, 0, 0, 4, 0), new(5, 0, 0, 4, 0), new(9, 1, 0, 0, 0))], [], Piece, Today);

        Assert.True(story.Lines.Any(l => l is { About: "Still hard", Tint: Tint.Bad } && l.Words.StartsWith("Bar 9:", StringComparison.Ordinal)), Said(story));
        Assert.True(story.Steps.Any(s => (s.From, s.To) == (8, 10)), Said(story));
    }

    // SessionStory.cs:254-257 (Where) says "the bars where something happens most of the times they're played (three times in
    // four)", but adds up the notes, not the times: three notes off the beat in one time through of four count as three times.
    // The timing line and its step then name a bar that went out once.
    [Fact]
    public void TheTimingLineNamesTheBarsThatGoOutMostTimesNotOneThatWentOutOnce()
    {
        var piece = new PieceShape(1, 4, 115) { Notes = [8, 8, 8, 8] };
        var always = new BarTrouble(3, 0, 0, 1, 0);
        PieceRun[] session = [Timed(2, 92, 31, 32, 0.95, new(2, 0, 0, 3, 0), always), Timed(4, 92, 31, 32, 0.95, always), Timed(6, 92, 31, 32, 0.95, always), Timed(8, 92, 31, 32, 0.95, always)];

        var story = SessionStory.Of(session, [], piece, Today);

        var timing = story.Lines.SingleOrDefault(l => l.About == "Timing");
        Assert.True(timing is { Tint: Tint.Bad } && timing.Words.Contains("bar 3", StringComparison.Ordinal), Said(story));
        Assert.False(timing!.Words.Contains("bars 2–3", StringComparison.Ordinal), Said(story));
    }

    // PieceStanding.cs:80-82 takes how often a run played its bars from all its notes against all its written ones, and gives
    // every bar that many times the room for wrong notes. With only bars 1–4 repeated (96 notes of 64 written, which rounds to
    // twice), a bar played once is let off a wrong note in eight. practice.md: right is "none wrong … in a bar of fewer than ten,
    // played once".
    [Fact]
    public void ABarPlayedOnceWithAWrongNoteIsStillBeingReadWhenOnlyOtherBarsRepeat()
    {
        var piece = new PieceShape(1, 8, 100) { Notes = [.. Enumerable.Repeat(8, 8)] };
        var run = new PieceRun(Start, Today, true, 60, "both", 1, false, null, 95, 96, 0.95) { Trouble = [new(6, 1, 0, 0, 0)] };

        Assert.Equal(BarState.Reading, PieceStanding.Of([run], piece).Bars[5].State);
    }

    // BarWords.cs:29-30 calls the pickup "the upbeat" only when it stands alone: starting a longer stretch among others, it is
    // "bars 0–2". The summary above it: "the pickup first, as 'the upbeat'".
    [Fact]
    public void BarsFromThePickupOnAreNamedFromTheUpbeat()
    {
        Assert.Equal("the upbeat to bar 2", BarWords.Runs([0, 1, 2]));
        Assert.DoesNotContain("0–2", BarWords.Runs([0, 1, 2, 5]));
    }

    // TempoMarks.cs:12 gives the fastest mark, 208, for any tempo at or over it, and SessionStory.cs:766 asks for that as "a notch
    // up" (so do :669, :691 and :768 with Down under 30). Practice goes to 240: from 220, a notch up asks for 208.
    [Fact]
    public void ANotchUpIsFasterThanTheTempoJustPlayed()
    {
        var piece = new PieceShape(1, 32, 240) { Notes = [.. Enumerable.Repeat(8, 32)] };

        var story = SessionStory.Of([Timed(2, 220, 495, 512, 0.92), Timed(6, 220, 495, 512, 0.92)], [], piece, Today);

        var up = story.Steps.SingleOrDefault(s => s.Why.EndsWith("a notch up.", StringComparison.Ordinal));
        Assert.True(up is { Tempo: > 220 }, Said(story));
    }

    // SessionStory.cs:478-483: when no time through was at the tempo most of the session was played at (its loops, here), every
    // time through is counted and all of them are given the first one's tempo.
    [Fact]
    public void TimesThroughAtTwoTemposAreNotBothGivenTheFirstOnes()
    {
        PieceRun[] session = [Loop(2, 115, 1, 4, 20, 0.99), Timed(20, 110, 507, 512, 0.99), Timed(26, 120, 502, 512, 0.98)];

        var story = SessionStory.Of(session, [], Piece, Today);

        Assert.Equal(Work.Performing, story.Work);
        Assert.DoesNotContain("Twice through at 110", story.Headline);
    }
}
