using Prima.Core.Generation;
using Prima.Core.Music;

namespace Prima.Core.Practice;

public enum StepKind
{
    Technique,
    Notes,
    Rhythm,
    Pause,
    SightReading,
    OneSpot,
}

public enum Variant
{
    ReadAhead,
    Duet,
    HearFirst,
}

/// <summary>One step of the day's plan, its minutes and what it holds.</summary>
public sealed record PlanStep(StepKind Kind, int Minutes, string Name, string Detail);

/// <summary>The day's plan: built once a day and kept.</summary>
public sealed record DayPlan(DateOnly Day, IReadOnlyList<PlanStep> Steps, Key ScaleKey, Key ArpeggioKey, Variant Variant, bool Short);

/// <summary>
/// The daily plan (plan, The daily plan; research's daily session): technique, notes, rhythm, a pause, sight-reading with one
/// variant, then one spot or a piece. It mixes across days, rotating the scale and arpeggio of the day and the variant, rather
/// than repeating one block. The five-minute plan is note review and two passages. The order is a design choice.
/// </summary>
public static class DailyPlan
{
    public static DayPlan Build(DateOnly day, int dailyMinutes, int level, bool pianoPlays, bool shortPlan = false)
    {
        var keys = Technique.Order.Where(k => Ladder.Level(level).Keys.Contains(k)).DefaultIfEmpty(new Key(0)).ToList();
        var scaleKey = keys[day.DayNumber % keys.Count];
        var arpeggioKey = keys[(day.DayNumber + 1) % keys.Count];
        Variant[] variants = pianoPlays ? [Variant.ReadAhead, Variant.Duet, Variant.HearFirst] : [Variant.ReadAhead];
        var variant = variants[day.DayNumber % variants.Length];
        if (shortPlan)
        {
            return new DayPlan(day, [new PlanStep(StepKind.Notes, 3, "Notes", "The notes due for review"), new PlanStep(StepKind.SightReading, 2, "Sight-reading", "Two new passages")], scaleKey, arpeggioKey, variant, true);
        }

        // The research's split of twenty minutes, shared out over the day's minutes less the pause in whole minutes that add up
        // to them: each step its share rounded down, then a minute more for those with the most left over.
        var total = Math.Max(10, dailyMinutes) - 1;
        double[] split = [4, 3.5, 2.5, 7.5, 2.5];
        var shares = split.Select(s => total * s / split.Sum()).ToArray();
        var minutes = shares.Select(s => (int)s).ToArray();
        var fullest = Enumerable.Range(0, shares.Length).OrderByDescending(i => shares[i] - minutes[i]);
        foreach (var i in fullest.Take(total - minutes.Sum())) minutes[i]++;
        var variantName = variant switch { Variant.ReadAhead => "read ahead", Variant.Duet => "duet", _ => "hear first" };
        return new DayPlan(day,
        [
            new PlanStep(StepKind.Technique, minutes[0], "Technique", $"{scaleKey.Name} scale and {arpeggioKey.Name} arpeggio, three to five runs each"),
            new PlanStep(StepKind.Notes, minutes[1], "Notes", "The notes due for review, and new ones after a good day"),
            new PlanStep(StepKind.Rhythm, minutes[2], "Rhythm", "Three or four rhythms; the click fades on later tries"),
            new PlanStep(StepKind.Pause, 1, "Pause", "Stand up, shake out your hands, look away"),
            new PlanStep(StepKind.SightReading, minutes[3], "Sight-reading", $"Four to six new passages, one of them {variantName}"),
            new PlanStep(StepKind.OneSpot, minutes[4], "One spot", "The bar you missed most today, slower, then its line"),
        ], scaleKey, arpeggioKey, variant, false);
    }

    /// <summary>A line suggesting going gradually, when the daily minutes rise more than about 25% over last week's (ours).</summary>
    public static string? GoGradually(int newMinutes, double lastWeekDailyAverage) => lastWeekDailyAverage > 0 && newMinutes > lastWeekDailyAverage * 1.25 ? "That's a big step up from last week. Going up gradually is kinder to your hands." : null;
}

/// <summary>A practised day: its minutes, and whether the five-minute plan was finished.</summary>
public sealed record PracticeDayRecord(DateOnly Day, double Minutes, bool ShortPlanDone);

/// <summary>
/// The streak and the week, worked out from the practised days: the days practised of the last seven, and the minutes this week,
/// from Monday, as You's weeks are.
/// </summary>
public sealed record StreakState(int Days, int FreezesBanked, IReadOnlyList<DateOnly> FreezesUsed, int DaysThisWeek, bool WeekKept, double MinutesThisWeek, DateOnly? LastPractised)
{
    /// <summary>A break said plainly, never as a failure: "Last practised Tuesday."</summary>
    public string? BreakNote(DateOnly today) => LastPractised is { } last && last < today.AddDays(-1) ? $"Last practised {last.ToString("dddd", System.Globalization.CultureInfo.InvariantCulture)}. Today's plan is ready." : null;
}

/// <summary>
/// The repairable streak (plan, The streak and the habit; ours): a practised day has 5 minutes or more, or the five-minute plan
/// done; a freeze is earned every 7 practised days, up to 2 banked, and used by itself on a missed day; a week is kept at 5 of 7.
/// </summary>
public static class Streak
{
    public const double MinutesForADay = 5;
    public const int DaysPerFreeze = 7;
    public const int MostFreezes = 2;

    public static StreakState Compute(IEnumerable<PracticeDayRecord> days, DateOnly today)
    {
        var practised = days.Where(d => d.Minutes >= MinutesForADay || d.ShortPlanDone).Select(d => d.Day).ToHashSet();
        var all = days.ToList();
        var week = Enumerable.Range(0, 7).Select(i => today.AddDays(-i)).ToList();
        var monday = today.AddDays(-(((int)today.DayOfWeek + 6) % 7));
        var minutes = all.Where(d => d.Day >= monday && d.Day <= today).Sum(d => d.Minutes);
        var daysThisWeek = week.Count(practised.Contains);
        if (practised.Count == 0) return new StreakState(0, 0, [], daysThisWeek, daysThisWeek >= 5, minutes, null);

        var (streak, banked, earnedFrom) = (0, 0, 0);
        var used = new List<DateOnly>();
        for (var day = practised.Min(); day <= today; day = day.AddDays(1))
        {
            if (practised.Contains(day))
            {
                streak++;
                earnedFrom++;
                if (earnedFrom % DaysPerFreeze == 0) banked = Math.Min(MostFreezes, banked + 1);
            }
            else if (day < today)
            {
                if (banked > 0)
                {
                    banked--;
                    used.Add(day);
                }
                else (streak, earnedFrom) = (0, 0);
            }
        }
        return new StreakState(streak, banked, used, daysThisWeek, daysThisWeek >= 5, minutes, practised.Max());
    }
}
