using AdaptiveWorkout.Core;

namespace AdaptiveWorkout.Core.Tests;

public class DataCopiesTests
{
    [Fact]
    public void Describe_SaysWhatTheCopyHolds()
    {
        Assert.Equal("212 workouts · 3,104 sets · 1,904 food entries · 480 weigh-ins", DataCopies.Describe(212, 3104, 1904, 480));
    }

    [Fact]
    public void Describe_CountsOneOfEach_InTheSingular()
    {
        Assert.Equal("1 workout · 1 set · 1 food entry · 1 weigh-in", DataCopies.Describe(1, 1, 1, 1));
    }

    // Before the first workout, a copy still holds the food and weigh-ins logged on Today.
    [Fact]
    public void Describe_LeavesOutWhatItHasNoneOf()
    {
        Assert.Equal("12 food entries · 3 weigh-ins", DataCopies.Describe(0, 0, 12, 3));
        Assert.Equal("2 workouts · 30 sets", DataCopies.Describe(2, 30, 0, 0));
    }

    [Fact]
    public void Describe_AnEmptyCopy_SaysNothingIsLoggedYet()
    {
        Assert.Equal("Nothing logged yet", DataCopies.Describe(0, 0, 0, 0));
    }
}
