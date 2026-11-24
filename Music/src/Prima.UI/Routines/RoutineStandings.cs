namespace Prima.UI.Routines;

/// <summary>How an activity of a routine is going there: its tempo In time at its first run and its latest, and its runs.</summary>
public sealed record ActivityStanding(double? FirstTempo, double? LastTempo, int Runs, int Clean);

/// <summary>
/// How a routine is going: a lamp for each of the last four weeks' days, lit where it was played; the last day it was; how many of
/// its activities had a clean run that day; this week's minutes and the last four weeks' runs; each activity's own; and what's done
/// of it today.
/// </summary>
public sealed record RoutineStanding(RoutineDefinition Routine, IReadOnlyList<bool> Lamps, DateOnly? Last, int? CleanOnLast, double WeekMinutes,
    int CleanRuns, int Runs, DateOnly? FirstRun, IReadOnlyDictionary<string, ActivityStanding> Activities, IReadOnlySet<string> DoneToday)
{
    /// <summary>The days the lamps go back over.</summary>
    public const int Days = 28;

    /// <summary>The days of the last four weeks it was played.</summary>
    public int DaysPlayed => Lamps.Count(l => l);

    /// <summary>The first of its activities not done or skipped today, in its order; none once all of them are.</summary>
    public Prima.Core.Routines.RoutineItem? Next => Routine.Items.Select(i => i.Recipe).FirstOrDefault(i => !DoneToday.Contains(i.Id));

    /// <summary>How many of its activities were played faster In time at their latest run than at their first, of those played In time.</summary>
    public (int Faster, int Timed) Faster => (Activities.Values.Count(a => a.LastTempo > a.FirstTempo), Activities.Values.Count(a => a.FirstTempo is not null));

    public static RoutineStanding Of(RoutineDefinition routine, IEnumerable<RoutineTime> times, IEnumerable<RoutineRun> runs, DateOnly today)
    {
        var its = times.Where(t => t.Revision == routine.Revision).ToList();
        var played = its.Where(t => t.Seconds > 0).Select(t => t.Day).ToHashSet();
        var lamps = Enumerable.Range(0, Days).Select(i => played.Contains(today.AddDays(i - Days + 1))).ToList();
        DateOnly? last = played.Count == 0 ? null : played.Max();
        var ids = routine.Items.Select(i => i.Recipe.Id).ToHashSet();
        var ran = runs.Where(r => r.Revision == routine.Revision && ids.Contains(r.ItemId)).OrderBy(r => r.At).ToList();
        // Of its activities, how many went clean the last day it was played; none said for a day with no runs to judge.
        int? cleanOnLast = last is { } day && ran.Any(r => r.Day == day) ? ran.Where(r => r.Day == day && r.Clean).Select(r => r.ItemId).Distinct().Count() : null;
        var recent = ran.Where(r => r.Day > today.AddDays(-Days)).ToList();
        var activities = routine.Items.ToDictionary(i => i.Recipe.Id, i =>
        {
            var mine = ran.Where(r => r.ItemId == i.Recipe.Id).ToList();
            var timed = mine.Where(r => r.Tempo is > 0).ToList();
            return new ActivityStanding(timed.FirstOrDefault()?.Tempo, timed.LastOrDefault()?.Tempo, mine.Count, mine.Count(r => r.Clean));
        });
        var done = its.Where(t => t.Day == today && t.State is RoutineItemState.Done or RoutineItemState.Skipped).Select(t => t.ItemId).ToHashSet();
        return new(routine, lamps, last, cleanOnLast, its.Where(t => t.Day > today.AddDays(-7)).Sum(t => t.Seconds) / 60, recent.Count(r => r.Clean), recent.Count,
            ran.Count == 0 ? null : ran[0].Day, activities, done);
    }

    /// <summary>How many days, in words: "1 day", "14 days".</summary>
    public static string DaysSaid(int days) => days == 1 ? "1 day" : $"{days} days";

    /// <summary>A day as the routines say it: today, yesterday, or its date.</summary>
    public static string When(DateOnly day, DateOnly today) =>
        day == today ? "Today" : day == today.AddDays(-1) ? "Yesterday" : day.ToString("d MMM", System.Globalization.CultureInfo.InvariantCulture);
}
