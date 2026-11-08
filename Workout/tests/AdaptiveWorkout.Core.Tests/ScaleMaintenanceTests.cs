using AdaptiveWorkout.Core;

namespace AdaptiveWorkout.Core.Tests;

public class ScaleMaintenanceTests
{
    private static readonly TimeSpan Offset = TimeSpan.FromHours(-8);

    // Four weeks: Oct 11 through Nov 7.
    private static readonly DateOnly First = new(2026, 10, 11);

    private static DateTimeOffset Start(DateOnly day) => new(day.ToDateTime(TimeOnly.MinValue), Offset);

    private static readonly (DateTimeOffset From, DateTimeOffset To) Weeks = (Start(First), Start(First.AddDays(28)));

    // Eating 2,000 a day against maintenance 2,500, the first days forgotten.
    private static List<CalorieDay> Days(int forgotten = 0) =>
        [.. Enumerable.Range(0, 28).Select(i => i < forgotten ? new CalorieDay(First.AddDays(i), null, 2500, Forgotten: true) : new CalorieDay(First.AddDays(i), 2000, 2500))];

    // A weigh-in every so many days, losing evenly: so many pounds over the four weeks.
    private static List<BodyWeightEntry> Scale(double pounds, int every = 2) =>
        [.. Enumerable.Range(0, 28).Where(i => i % every == 0)
            .Select(i => new BodyWeightEntry { At = Start(First.AddDays(i)).AddHours(7), Weight = 200 + pounds * i / 28.0, WeightUnit = WeightUnit.Pounds })];

    [Fact]
    public void FromScale_IsWhatWasEatenPlusTheScalesChange_InCaloriesADay()
    {
        // The logs say 500 under maintenance a day, 4 lb in four weeks; the scale lost 2: 2,000 + 2 × 3,500 / 28 = 2,250.
        var scale = Calories.FromScale(Days(), Scale(-2), Weeks.From, Weeks.To);

        Assert.Equal(new ScaleMaintenance(2250, 2500, 28, 28), scale);
        Assert.Equal(-250, scale!.Adjustment);
        Assert.True(scale.Differs);
    }

    [Fact]
    public void FromScale_LeavesForgottenDaysOut()
    {
        // Eight days forgotten count at maintenance on Calories, but not here: the 20 logged say what was eaten.
        Assert.Equal(new ScaleMaintenance(2250, 2500, 20, 28), Calories.FromScale(Days(forgotten: 8), Scale(-2), Weeks.From, Weeks.To));
    }

    [Fact]
    public void FromScale_NeedsThreeWeeks_TwoThirdsLogged_AndWeighInsTwiceAWeekAcrossTwo()
    {
        Assert.Null(Calories.FromScale([.. Days().Take(20)], Scale(-2), Weeks.From, Weeks.To));
        Assert.Null(Calories.FromScale(Days(forgotten: 10), Scale(-2), Weeks.From, Weeks.To));
        Assert.Null(Calories.FromScale(Days(), Scale(-2, every: 7), Weeks.From, Weeks.To));
        Assert.Null(Calories.FromScale(Days(), [.. Scale(-2).Take(5)], Weeks.From, Weeks.To));
    }

    [Fact]
    public void FromScale_UnderAHundredIsntWorthSaying()
    {
        // The scale lost 3.5 of the logs' 4: 2,000 + 437.5, 50 under the formula.
        var scale = Calories.FromScale(Days(), Scale(-3.5), Weeks.From, Weeks.To);

        Assert.Equal(-50, scale!.Adjustment);
        Assert.False(scale.Differs);
    }

    [Fact]
    public void Maintenance_AddsWhatTheScaleSaid()
    {
        // 2,759 by the formula (see CaloriesTests), 250 less by the scale.
        var him = new AppSettings { Sex = Sex.Male, BirthYear = 1996, HeightCm = 180, DailyActivity = DailyActivity.MostlyOnFeet, MaintenanceAdjust = -250 };
        var monday = new DateOnly(2026, 10, 12);

        Assert.Equal(2509, Calories.Maintenance(him, new BodyWeightEntry { At = Start(monday).AddHours(7), Weight = 80, WeightUnit = WeightUnit.Kilograms }, monday));
    }
}
