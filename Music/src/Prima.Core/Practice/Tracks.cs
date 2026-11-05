using Prima.Core.Generation;
using Prima.Core.Timing;

namespace Prima.Core.Practice;

/// <summary>What a result means for the level (plan, Levels and how they move; all the numbers ours).</summary>
public enum Verdict
{
    Good,
    Hold,
    TooHard,
}

/// <summary>
/// The reading track: a level, the step of its introduction (one kind of difficulty at a time), a tempo inside the level's range,
/// the last results at this level and tempo, and read ahead's view width in bars.
/// </summary>
public sealed record ReadingState(int Level, int Step, double Tempo, IReadOnlyList<(double InTime, bool KeptGoing)> Recent, int TooHardAtFloor, double ReadAheadBars)
{
    public static ReadingState Start { get; } = new(1, 4, Ladder.Level(1).Tempo.Min, [], 0, 2);

    public LevelSpec Spec => Ladder.Effective(Level, Step);

    /// <summary>The long-scale reading score: the level plus how far through its tempo range, such as 6.4 (ours).</summary>
    public double Score
    {
        get
        {
            var spec = Ladder.Level(Level);
            var through = spec.TopTempo == spec.Tempo.Min ? 0 : (Tempo - spec.Tempo.Min) / (spec.TopTempo - spec.Tempo.Min);
            return Level + Math.Clamp(through, 0, 0.99) * (Step >= 4 ? 1 : 0.5);
        }
    }
}

public static class Reading
{
    /// <summary>
    /// After a passage: decisions use the mean in-time accuracy of the last three at this level and tempo. Over 90% with all kept
    /// going is good: the next kind of difficulty comes in, then the tempo goes up 5%, then the level. 80–90% holds. Under 80% is
    /// too hard: the tempo drops 10%; a passage where you stopped, or under 60%, drops it at once. At the bottom of the range, two
    /// more too-hard results step the last kind of difficulty back.
    /// </summary>
    public static (ReadingState State, Verdict Verdict) After(ReadingState state, double inTime, bool keptGoing)
    {
        var recent = state.Recent.Append((InTime: inTime, KeptGoing: keptGoing)).TakeLast(3).ToList();
        Verdict verdict;
        if (!keptGoing || inTime < 0.6) verdict = Verdict.TooHard;
        else if (recent.Count < 3) verdict = Verdict.Hold;
        else
        {
            var mean = recent.Average(r => r.InTime);
            verdict = mean > 0.9 && recent.All(r => r.KeptGoing) ? Verdict.Good : mean < 0.8 ? Verdict.TooHard : Verdict.Hold;
        }

        var spec = Ladder.Level(state.Level);
        var next = verdict switch
        {
            Verdict.Good when state.Step < 4 => state with { Step = state.Step + 1, Recent = [], TooHardAtFloor = 0 },
            Verdict.Good when state.Tempo * 1.05 <= spec.TopTempo + 0.5 => state with { Tempo = Math.Round(Math.Min(spec.TopTempo, state.Tempo * 1.05)), Recent = [], TooHardAtFloor = 0 },
            Verdict.Good when state.Level < Ladder.Top => state with { Level = state.Level + 1, Step = 1, Tempo = Ladder.Level(state.Level + 1).Tempo.Min, Recent = [], TooHardAtFloor = 0 },
            Verdict.TooHard when state.Tempo > spec.Tempo.Min => state with { Tempo = Math.Round(Math.Max(spec.Tempo.Min, state.Tempo * 0.9)), Recent = [] },
            Verdict.TooHard when state.TooHardAtFloor + 1 >= 2 => StepBack(state),
            Verdict.TooHard => state with { TooHardAtFloor = state.TooHardAtFloor + 1, Recent = [] },
            _ => state with { Recent = recent },
        };
        return (next, verdict);
    }

    // The last kind of difficulty raised goes back a step; from a level's first step, back to the level below in full.
    private static ReadingState StepBack(ReadingState state) => state.Step > 0
        ? state with { Step = state.Step - 1, Recent = [], TooHardAtFloor = 0 }
        : state.Level > 1 ? state with { Level = state.Level - 1, Step = 3, Tempo = Ladder.Level(state.Level - 1).Tempo.Min, Recent = [], TooHardAtFloor = 0 } : state with { Recent = [], TooHardAtFloor = 0 };

    /// <summary>
    /// Read ahead's view: it narrows by half a bar after a good result and widens after a too-hard one, never under 1.5 s at the
    /// tempo (about 1 s plus a margin; ours). A hold leaves it, so slowing at hard spots isn't punished.
    /// </summary>
    public static double ReadAheadAfter(double bars, Verdict verdict, double tempo, int beatsPerBar)
    {
        var barSeconds = beatsPerBar * 60 / tempo;
        var least = Math.Ceiling(1.5 / barSeconds * 2) / 2;
        return verdict switch
        {
            Verdict.Good => Math.Max(least, bars - 0.5),
            Verdict.TooHard => Math.Min(8, bars + 0.5),
            _ => Math.Max(least, bars),
        };
    }

    /// <summary>
    /// Finding a level, the first time: two passages each from levels 1, 3, 5 and up, at each level's lowest tempo, until their
    /// mean falls under 80%; then one level below. Returns the next level to try, or the level found.
    /// </summary>
    public static (int? TryNext, int? Found) FindLevel(int level, IReadOnlyList<double> twoResults)
    {
        if (twoResults.Count < 2) return (level, null);
        if (twoResults.Average() < 0.8) return (null, Math.Max(1, level - 1));
        if (level >= Ladder.Top) return (null, Ladder.Top);
        return (Math.Min(Ladder.Top, level + 2), null);
    }
}

/// <summary>The other drills' step on their own ladders: over 90% in two sets in a row steps up, under 80% steps back (ours).</summary>
public static class DrillLadder
{
    public static (int Step, double LastShare) After(int step, double? lastShare, double share, int top) => share switch
    {
        > 0.9 when lastShare > 0.9 => (Math.Min(top, step + 1), 0),
        < 0.8 => (Math.Max(1, step - 1), share),
        _ => (step, share),
    };
}

/// <summary>
/// The rhythm track: a level on the ladder's rhythm column and the click's stage a set starts at. Within a set, later tries drop
/// the click one stage; between sets the stage moves first, then the level, by the same bands as reading (ours).
/// </summary>
public sealed record RhythmState(int Level, ClickStage Stage)
{
    public static RhythmState Start { get; } = new(1, ClickStage.Throughout);

    public ClickStage StageForTry(int tryIndex) => (ClickStage)Math.Min((int)ClickStage.Silent, (int)Stage + tryIndex);

    public RhythmState After(double setAccuracy) => setAccuracy switch
    {
        > 0.9 when Stage < ClickStage.Silent => this with { Stage = Stage + 1 },
        > 0.9 => new RhythmState(Math.Min(Ladder.Top, Level + 1), ClickStage.Throughout),
        < 0.8 when Stage > ClickStage.Throughout => this with { Stage = Stage - 1 },
        < 0.8 => new RhythmState(Math.Max(1, Level - 1), ClickStage.CountInOnly),
        _ => this,
    };
}

/// <summary>
/// A scale's or arpeggio's tempo track (plan, Technique): it starts at about 85% of your comfortable speed (ours), goes up 4 beats
/// a minute when the IOI SD stays under your target on 3 of 4 valid runs, the target being your first week's median (never
/// below 8%, where listeners start to hear it), and offers the next run 10% slower after two invalid runs in a row (ours).
/// </summary>
public sealed record TechniqueState(double? Tempo, double? Target, IReadOnlyList<double> Baseline, DateOnly? Started, IReadOnlyList<double?> Recent, int InvalidInRow, double? BestEvenTempo)
{
    public const double FloorPercent = 8;
    public const int RunsPerSet = 5;

    public static TechniqueState New { get; } = new(null, null, [], null, [], 0, null);

    /// <summary>The first run, without the click: its tempo is your comfortable speed.</summary>
    public TechniqueState Comfortable(double bpm, DateOnly today) => this with { Tempo = Math.Round(bpm * 0.85), Started = today };

    public TechniqueState After(bool valid, double? ioiSdPercent, DateOnly today)
    {
        if (!valid) return InvalidInRow + 1 >= 2 ? this with { Tempo = Tempo is { } t ? Math.Round(t * 0.9) : null, InvalidInRow = 0, Recent = [.. Recent.TakeLast(3), null] } : this with { InvalidInRow = InvalidInRow + 1, Recent = [.. Recent.TakeLast(3), null] };

        var inFirstWeek = Started is { } started && today < started.AddDays(7);
        var baseline = inFirstWeek ? [.. Baseline, ioiSdPercent ?? 0] : Baseline;
        var target = baseline.Count == 0 ? (double?)null : Math.Max(FloorPercent, Stats.Median(baseline));
        var recent = Recent.Append(ioiSdPercent).TakeLast(4).ToList();
        var under = target is { } t2 ? recent.Count(r => r is { } sd && sd <= t2) : 0;
        var best = target is { } goal && ioiSdPercent <= goal ? Math.Max(BestEvenTempo ?? 0, Tempo ?? 0) : BestEvenTempo;
        var state = this with { Baseline = baseline, Target = inFirstWeek ? null : target, Recent = recent, InvalidInRow = 0, BestEvenTempo = best };
        return !inFirstWeek && under >= 3 ? state with { Tempo = Tempo + 4, Recent = [] } : state;
    }
}
