using AdaptiveWorkout.Core;

namespace AdaptiveWorkout.Core.Tests;

public class ChecksTests
{
    private static readonly DateOnly Today = new(2026, 11, 8);
    private static readonly DateTimeOffset Now = new(2026, 11, 8, 9, 0, 0, TimeSpan.FromHours(-8));

    // The four weeks before today, each eating about 2,000, with some days changed.
    private static List<CalorieDay> Days(params (int DaysAgo, int Eaten)[] changed) =>
        [.. Enumerable.Range(1, 28).Reverse().Select(ago =>
            new CalorieDay(Today.AddDays(-ago), changed.FirstOrDefault(c => c.DaysAgo == ago) is { Eaten: > 0 } c ? c.Eaten : 1950 + ago * 5, 2400))];

    // A weigh-in every other morning for four weeks, losing 2 lb over them, some changed.
    private static List<BodyWeightEntry> WeighIns(params (int DaysAgo, double Weight)[] changed) =>
        [.. Enumerable.Range(0, 15).Select(i => 28 - 2 * i).Select(ago => new BodyWeightEntry
        {
            Id = ago, At = Now.AddDays(-ago).AddHours(-2), WeightUnit = WeightUnit.Pounds,
            Weight = changed.FirstOrDefault(c => c.DaysAgo == ago) is { Weight: > 0 } c ? c.Weight : 200 - 2.0 * (28 - ago) / 28 + (ago % 4 == 0 ? 0.6 : -0.6),
        })];

    [Fact]
    public void ShortDays_AreThoseUnderHalfTheUsualDay_NewestFirst()
    {
        // Under half of 2,025 is under 1,012: the 900 and the 950, not the 1,500.
        var (days, usual) = Checks.ShortDays(Days((3, 900), (10, 950), (12, 1500)), Today, new HashSet<DateOnly>());

        Assert.Equal([Today.AddDays(-3), Today.AddDays(-10)], days.Select(d => d.Day));
        Assert.Equal(2025, usual);
    }

    [Fact]
    public void ShortDays_LeaveOutAnsweredOnes_AndNeedAWeekLogged()
    {
        Assert.Equal([Today.AddDays(-10)], Checks.ShortDays(Days((3, 900), (10, 950)), Today, new HashSet<DateOnly> { Today.AddDays(-3) }).Days.Select(d => d.Day));
        Assert.Empty(Checks.ShortDays([.. Days((3, 900)).TakeLast(6)], Today, new HashSet<DateOnly>()).Days);
    }

    [Fact]
    public void Days_CountADayAtMaintenance_WhenSaidTo()
    {
        var food = new[] { new FoodEntry { At = Now.AddDays(-3), Calories = 900 }, new FoodEntry { At = Now.AddDays(-2), Calories = 2100 } };

        var days = Calories.Days(food, [], new AppSettings(), Today.AddDays(-3), Today, new Dictionary<DateOnly, int>(), new HashSet<DateOnly> { Today.AddDays(-3) });

        // Three days ago is counted at maintenance, like yesterday, which nothing was logged on; today is still going.
        Assert.Equal([(null, true), (2100, false), (null, true), (null, false)], days.Select(d => (d.Eaten, d.Forgotten)));
    }

    [Fact]
    public void OddWeighIns_AreFarOffTheLineThroughTheOthers()
    {
        var odd = Checks.OddWeighIns(WeighIns((10, 205.5), (20, 199.5)), Now, new HashSet<long>());

        var (weighIn, expected) = Assert.Single(odd);
        Assert.Equal(10, weighIn.Id);
        Assert.InRange(expected, 198.4, 199.6);
    }

    [Fact]
    public void OddWeighIns_LeaveOutAnsweredOnes_AndThoseWithTooFewAround()
    {
        Assert.Empty(Checks.OddWeighIns(WeighIns((10, 205.5)), Now, new HashSet<long> { 10 }));
        Assert.Empty(Checks.OddWeighIns([.. WeighIns((10, 205.5)).Where(w => w.Id is 10 or 12 or 8)], Now, new HashSet<long>()));
    }
}
