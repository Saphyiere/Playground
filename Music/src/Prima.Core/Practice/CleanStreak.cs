using Prima.Core.Timing;

namespace Prima.Core.Practice;

/// <summary>
/// What a slip does to a run In time, chosen for each piece under its bar's More. A slip is a wrong note, a missed one, or one put
/// right a moment after: what's heard as it's played. An extra key, and with Timing too a note off the beat, spoil a time round
/// once it's scored, but never stop a run.
/// </summary>
public enum SlipWay
{
    /// <summary>On through it, as a run has always gone.</summary>
    KeepGoing,

    /// <summary>The click stops, and a moment later counts in again from the bar before the slip, inside a loop if one is set.</summary>
    BackABar,

    /// <summary>As Back a bar, then that bar with one either side goes round until it's clean so many times in a row, and on.</summary>
    LockIn,
}

/// <summary>Where a slip takes a run, for Back a bar and Lock in.</summary>
public static class Slips
{
    /// <summary>The clean times round in a row Lock in can ask for: teachers ask for three, five or seven, and no study fixes one.</summary>
    public static readonly int[] InARow = [3, 5, 7];

    /// <summary>The owner's own habit: seven times through clean.</summary>
    public const int Habit = 7;

    /// <summary>The look back's Practice it: two times round in a row clean, as its words say.</summary>
    public const int PracticeIt = 2;

    /// <summary>Where a run counts in again after a slip in <paramref name="bar"/>: the bar before, never before <paramref name="first"/> (the loop's or the piece's).</summary>
    public static int Back(int bar, int first) => Math.Max(first, bar - 1);

    /// <summary>The bars Lock in goes round after a slip in <paramref name="bar"/>: it, with one either side, never outside <paramref name="first"/> to <paramref name="last"/> (the loop's, or the piece's).</summary>
    public static (int From, int To) Spot(int bar, int first, int last)
    {
        var at = Math.Clamp(bar, first, last);
        return (Math.Max(first, at - 1), Math.Min(last, at + 1));
    }
}

/// <summary>
/// Times round in a row played clean, toward a number: Lock in's (3, 5 or 7; the owner's habit is 7) and the look back's Practice
/// it (2). A slip, or a time round spoiled, puts it back to none. Every second spoiled in a row asks for a notch slower; ten
/// spoiled in all and the spot is let go, rather than trap you in it (Duke, Simmons &amp; Cash 2009: the best pianists fixed an
/// error where it was, slowly, until it stuck; Mishra 2002: going back to the start after every error was the slower memorizers'
/// way).
/// </summary>
public sealed class CleanStreak(int needed)
{
    /// <summary>Spoiled times round in a row that take it a notch slower.</summary>
    public const int SlowerAfter = 2;

    /// <summary>Spoiled times round in all after which the spot is let go.</summary>
    public const int LetGoAfter = 10;

    /// <summary>Clean times round in a row it's done at.</summary>
    public int Needed { get; } = Math.Max(1, needed);

    /// <summary>Clean times round in a row so far: the dots lit.</summary>
    public int InARow { get; private set; }

    public int Clean { get; private set; }

    public int Spoiled { get; private set; }

    /// <summary>Spoiled times round in a row since the last clean one or the last notch slower.</summary>
    public int SpoiledInARow { get; private set; }

    /// <summary>The times round played, clean or spoiled.</summary>
    public int Times => Clean + Spoiled;

    public bool Done => InARow >= Needed;

    /// <summary>Spoiled too often to hold you there any longer.</summary>
    public bool LetGo => !Done && Spoiled >= LetGoAfter;

    /// <summary>A time round played, clean or spoiled: whether it asks for a notch slower (every second spoiled in a row).</summary>
    public bool Add(bool clean)
    {
        if (clean)
        {
            (InARow, Clean, SpoiledInARow) = (InARow + 1, Clean + 1, 0);
            return false;
        }
        (InARow, Spoiled, SpoiledInARow) = (0, Spoiled + 1, SpoiledInARow + 1);
        if (SpoiledInARow < SlowerAfter) return false;
        SpoiledInARow = 0;
        return true;
    }

    /// <summary>
    /// Whether a time round was clean: every note right first time (none wrong, missed or put right after a slip, no chord a key
    /// at a time), no key extra, and with <paramref name="timing"/> none off the beat.
    /// </summary>
    public static bool IsClean(PiecePass pass, bool timing) =>
        pass.Notes > 0 && pass.Right == pass.Notes && pass.Extra == 0 && pass.Trouble.All(t => t.NotRight == 0 && (!timing || t.Off == 0));
}

/// <summary>What a time round round a locked spot leads to.</summary>
public enum LockStep
{
    /// <summary>Round again at the same tempo: on without a break after a clean one, counted in again after a slip.</summary>
    Again,

    /// <summary>Counted in again a notch slower: the second spoiled in a row.</summary>
    Slower,

    /// <summary>Clean so many times in a row: on from the spot's first bar at your own tempo.</summary>
    Done,

    /// <summary>Spoiled too often: on from the spot's first bar at your own tempo, the spot left for the review.</summary>
    LetGo,
}

/// <summary>
/// A spot Lock in goes round: its bars, its streak toward the clean times round in a row asked for, and its tempo, which starts
/// at yours and goes a notch slower each second spoiled time round in a row.
/// </summary>
public sealed class LockedSpot(int from, int to, int needed, double tempo)
{
    public int From { get; } = from;

    public int To { get; } = to;

    public CleanStreak Streak { get; } = new(needed);

    /// <summary>The tempo it started at: yours.</summary>
    public double StartTempo { get; } = tempo;

    /// <summary>The tempo it goes round at now.</summary>
    public double Tempo { get; private set; } = tempo;

    /// <summary>A time round: played to its end clean or spoiled (its timing, or a key extra), or cut short by a slip.</summary>
    public LockStep Round(bool clean)
    {
        var slower = Streak.Add(clean);
        if (Streak.Done) return LockStep.Done;
        if (Streak.LetGo) return LockStep.LetGo;
        if (!slower) return LockStep.Again;
        Tempo = TempoMarks.Down(Tempo);
        return LockStep.Slower;
    }
}
