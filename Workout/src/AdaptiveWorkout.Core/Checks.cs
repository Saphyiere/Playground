namespace AdaptiveWorkout.Core;

/// <summary>Entries that look like slips, for Calories to ask about once: a day logged far short, and a weigh-in far off the line.</summary>
public static class Checks
{
    /// <summary>
    /// Days in the four weeks before <paramref name="today"/> logged at under half my usual day (the middle of the days I logged in those
    /// weeks, which needs a week of them), newest first, leaving out the <paramref name="answered"/> ones; and the usual day, for the screen.
    /// A day I've said counts at maintenance has no logged amount, so it isn't asked about again.
    /// </summary>
    public static (IReadOnlyList<CalorieDay> Days, int? Usual) ShortDays(IReadOnlyList<CalorieDay> days, DateOnly today, IReadOnlySet<DateOnly> answered)
    {
        var logged = days.Where(d => d.Day >= today.AddDays(-28) && d.Day < today && d.Eaten is not null).ToList();
        if (logged.Count < 7) return ([], null);

        var amounts = logged.Select(d => d.Eaten!.Value).Order().ToList();
        var usual = amounts[amounts.Count / 2];
        return ([.. logged.Where(d => d.Eaten!.Value * 2 < usual && !answered.Contains(d.Day)).OrderByDescending(d => d.Day)], usual);
    }

    /// <summary>
    /// Weigh-ins in the four weeks before <paramref name="now"/> more than 4 lb off the line through the others within two weeks either
    /// side (or 2% of the weight, if that's more), newest first, leaving out the <paramref name="answered"/> ones; each with where the
    /// line put it, in its own unit. It needs 4 others across a week or more.
    /// </summary>
    public static IReadOnlyList<(BodyWeightEntry WeighIn, double Expected)> OddWeighIns(IReadOnlyList<BodyWeightEntry> weighIns, DateTimeOffset now, IReadOnlySet<long> answered)
    {
        var odd = new List<(BodyWeightEntry WeighIn, double Expected)>();
        foreach (var weighIn in weighIns.Where(w => w.At >= now.AddDays(-28) && w.At <= now && !answered.Contains(w.Id)))
        {
            var others = weighIns.Where(o => o.Id != weighIn.Id && (o.At - weighIn.At).Duration() <= TimeSpan.FromDays(14)).ToList();
            if (others.Count < 4 || (others.Max(o => o.At) - others.Min(o => o.At)).TotalDays < 7) continue;

            // The straight line through the others, in pounds, read at this weigh-in: days from it along, pounds up.
            var points = others.Select(o => (Day: (o.At - weighIn.At).TotalDays, Pounds: WorkoutStats.Pounds(o.Weight, o.WeightUnit))).ToList();
            var (meanDay, meanPounds) = (points.Average(p => p.Day), points.Average(p => p.Pounds));
            var spread = points.Sum(p => (p.Day - meanDay) * (p.Day - meanDay));
            var slope = spread == 0 ? 0 : points.Sum(p => (p.Day - meanDay) * (p.Pounds - meanPounds)) / spread;
            var expected = meanPounds - slope * meanDay;
            if (Math.Abs(WorkoutStats.Pounds(weighIn.Weight, weighIn.WeightUnit) - expected) > Math.Max(4, expected * 0.02))
                odd.Add((weighIn, weighIn.WeightUnit == WeightUnit.Kilograms ? WorkoutStats.Kilograms(expected, WeightUnit.Pounds) : expected));
        }

        return [.. odd.OrderByDescending(o => o.WeighIn.At)];
    }
}
