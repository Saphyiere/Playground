using System.Text.Json;
using AdaptiveWorkout.Core;
using AdaptiveWorkout.Core.Assist;
using Playground.AI.Assist;

namespace AdaptiveWorkout.Core.Tests;

public class MusclesTests
{
    private const long Rows = 1, Squats = 2, Walk = 3, Curls = 4;

    private static readonly Dictionary<long, MuscleTags> Tags = new()
    {
        [Rows] = new(Rows, [Muscle.Lats, Muscle.UpperBack], [Muscle.RearDelts, Muscle.Biceps], false),
        [Squats] = new(Squats, [Muscle.Quads, Muscle.Glutes], [Muscle.Hamstrings], true),
        [Walk] = new(Walk, [], [], false),
    };

    private static WorkoutSet Set(long exercise, int? reps = 10, long session = 1, int? seconds = null) =>
        new() { SessionId = session, ExerciseId = exercise, Reps = reps, DurationSeconds = seconds };

    private static double SetsFor(IReadOnlyList<MuscleSets> counts, Muscle muscle) => counts.Single(c => c.Muscle == muscle).Sets;

    [Fact]
    public void Count_GivesOneForTheMainMuscles_AndHalfForTheOnesThatHelp()
    {
        var counts = Muscles.Count([Set(Rows), Set(Rows), Set(Rows), Set(Squats)], Tags);

        Assert.Equal(Muscles.All, counts.Select(c => c.Muscle));
        Assert.Equal(3, SetsFor(counts, Muscle.Lats));
        Assert.Equal(1.5, SetsFor(counts, Muscle.Biceps));
        Assert.Equal(1, SetsFor(counts, Muscle.Quads));
        Assert.Equal(0.5, SetsFor(counts, Muscle.Hamstrings));
        Assert.Equal(0, SetsFor(counts, Muscle.Chest));
    }

    [Fact]
    public void Count_LeavesOutUntaggedExercises_EmptySets_AndCardio()
    {
        var counts = Muscles.Count([Set(Curls), Set(Rows, reps: 0), Set(Rows, reps: null), Set(Walk, reps: null, seconds: 1800)], Tags);

        Assert.All(counts, c => Assert.Equal(0, c.Sets));
        Assert.Equal(Muscles.All, Muscles.Missing(counts));
    }

    [Fact]
    public void InWeek_TakesTheWorkoutsStartedMondayToSunday()
    {
        var offset = TimeSpan.FromHours(-8);
        WorkoutSession On(long id, int day, int hour = 18) => new() { Id = id, StartedAt = new DateTimeOffset(2026, 11, day, hour, 0, 0, offset) };
        // Sunday the 1st is last week's; Monday the 2nd to Sunday the 8th are this week's; Monday the 9th is next week's.
        var sessions = new[] { On(1, 1, 23), On(2, 2, 0), On(3, 8, 23), On(4, 9, 0) };
        var sets = sessions.Select(s => Set(Rows, session: s.Id)).ToList();

        var week = Muscles.InWeek(sessions, sets, new DateOnly(2026, 11, 2));

        Assert.Equal([2L, 3L], week.Select(s => s.SessionId));
    }

    [Fact]
    public void Text_NamesTheMainMusclesThenTheOnesThatHelp()
    {
        Assert.Equal("Lats, upper back · helps rear delts, biceps", Muscles.Text(Tags[Rows]));
        Assert.Equal("Quads, glutes · helps hamstrings", Muscles.Text(Tags[Squats]));
        Assert.Equal("No muscles: cardio", Muscles.Text(Tags[Walk]));
        Assert.Equal(["12", "4½", "½", "0"], new[] { 12, 4.5, 0.5, 0 }.Select(Muscles.SetsText));
    }
}

public class TagExercisesTests
{
    private static readonly ExercisesToTag Input = new(
        [new(1, "Overhand rows", ExerciseMeasure.Weight, "3 × 10 at 100"), new(2, "Walk", ExerciseMeasure.Time, "30:00")],
        [new("Cable rows", [Muscle.Lats, Muscle.UpperBack], [Muscle.Biceps])]);

    private static ExerciseTag Tag(long id, Muscle[] primary, Muscle[]? secondary = null) => new(id, primary, secondary ?? []);

    [Fact]
    public void Check_PassesEachExerciseTaggedOnce_AndCardioWithNoMuscles()
    {
        var answer = new ExerciseTags([Tag(1, [Muscle.Lats, Muscle.UpperBack], [Muscle.RearDelts, Muscle.Biceps]), Tag(2, [])]);

        Assert.Empty(new TagExercises().Check(Input, answer));
    }

    [Fact]
    public void Check_SaysWhatsWrong()
    {
        var answer = new ExerciseTags(
        [
            Tag(1, [Muscle.Lats, Muscle.UpperBack, Muscle.RearDelts, Muscle.Biceps], [Muscle.Lats]),
            Tag(1, [Muscle.Lats]),
            Tag(7, [Muscle.Chest]),
        ]);

        var problems = new TagExercises().Check(Input, answer);

        Assert.Contains("Overhand rows is tagged 2 times; tag it once.", problems);
        Assert.Contains("Overhand rows: give at most three primary muscles, not 4.", problems);
        Assert.Contains("Overhand rows: Lats is in both lists; keep it in primary only.", problems);
        Assert.Contains("Exercise 7 isn't in the input.", problems);
        Assert.Contains("Walk is missing; tag every exercise.", problems);
    }

    [Fact]
    public void Check_SecondaryNeedsAPrimary()
    {
        var problems = new TagExercises().Check(Input, new ExerciseTags([Tag(1, [Muscle.Lats]), Tag(2, [], [Muscle.Calves])]));

        Assert.Equal(["Walk: secondary muscles need a primary one; for cardio leave both empty."], problems);
    }

    [Fact]
    public void Tags_KeepsEachMuscleOnce_InTheMapsOrder_AsClaudes()
    {
        var tags = TagExercises.Tags(new ExerciseTags([Tag(1, [Muscle.UpperBack, Muscle.Lats], [Muscle.Biceps, Muscle.RearDelts])]));

        Assert.Equal([Muscle.Lats, Muscle.UpperBack], tags[0].Main);
        Assert.Equal([Muscle.RearDelts, Muscle.Biceps], tags[0].Helps);
        Assert.False(tags[0].ByOwner);
    }

    [Fact]
    public void Schema_IsOneTheApiTakes_WithTheMusclesAsItsEnum()
    {
        var schema = JsonSerializer.SerializeToElement(AssistSchema.For(typeof(Reply<ExerciseTags>)));
        var tag = schema.GetProperty("properties").GetProperty("answer").GetProperty("properties").GetProperty("exercises").GetProperty("items").GetProperty("properties");

        Assert.Equal(["exerciseId", "primary", "secondary"], tag.EnumerateObject().Select(p => p.Name));
        Assert.Equal(15, tag.GetProperty("primary").GetProperty("items").GetProperty("enum").GetArrayLength());
        Assert.Contains("\"ownerTagged\":[{\"name\":\"Cable rows\",\"primary\":[\"lats\",\"upperBack\"],\"secondary\":[\"biceps\"]}]", JsonSerializer.Serialize(Input, AssistSchema.Json));
    }
}
