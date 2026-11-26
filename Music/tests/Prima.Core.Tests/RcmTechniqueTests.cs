using Prima.Core.Generation;
using Prima.Core.Music;
using Prima.Core.Routines;

namespace Prima.Core.Tests;

/// <summary>
/// RCM's technique from Preparatory A to Level 4 as Prima's drills and exercises play it (the owner, 25 November 2026: "Do the
/// two", RCM's technique up to Level 4 being the second): a level's activities, a key a day, with RCM's minimum tempo as the
/// target; and the natural minor scale and RCM's eighths it needed.
/// </summary>
public class RcmTechniqueTests
{
    [Fact]
    public void EachLevelsActivitiesAreTheCataloguesOwn_InKeysAndHandsTheyPlay()
    {
        Assert.Equal(["prep-a", "prep-b", "1", "2", "3", "4"], RcmTechnique.Levels.Select(l => l.Key));
        var activities = RcmTechnique.Levels.SelectMany(l => l.Activities).ToList();
        Assert.Equal(activities.Count, activities.Select(a => a.Id).Distinct().Count());
        foreach (var activity in activities)
        {
            Assert.Same(activity, RcmTechnique.Find(activity.Id));
            if (activity.Kind == "drill")
            {
                Assert.Contains(activity.Target, (string[])["scales", "arpeggios"]);
                continue;
            }
            var exercise = Exercises.Find(activity.Target);
            Assert.NotNull(exercise);
            var keys = Exercises.KeysOf(exercise);
            Assert.All(activity.Keys, k => Assert.Contains(k.Key, keys));
            Assert.All(activity.Separately ? [HandUse.Right, HandUse.Left] : (HandUse[])[HandUse.Together], h => Assert.Contains(h, exercise.Hands));
        }
        Assert.Null(RcmTechnique.Find("scales"));
    }

    [Fact]
    public void AKeyComesRoundADay_EachHandOnADayOfItsOwnWhereRcmAsksThemSeparately()
    {
        var scales = RcmTechnique.Find("rcm-1-scales")!;
        var start = new DateOnly(2026, 11, 25);

        var days = Enumerable.Range(0, 18).Select(i => scales.On(start.AddDays(i))).ToList();

        // Nine scales (C, G and F major; A, E and D minor, natural and harmonic), each hand on a day of its own: eighteen days round.
        Assert.Equal(18, days.Select(d => (d.Key, d.Form, d.Hands)).Distinct().Count());
        Assert.Equal(days[0], scales.On(start.AddDays(18)));
        Assert.All(days.Zip(days.Skip(1)), pair => Assert.NotEqual(pair.First.Hands, pair.Second.Hands));
        Assert.Contains(days, d => d is { Key: { Fifths: 0, Minor: true }, Form: ScaleKind.NaturalMinor });
        // Hands together where RCM asks for them so: Level 3's scales.
        Assert.All(Enumerable.Range(0, 9).Select(i => RcmTechnique.Find("rcm-3-scales")!.On(start.AddDays(i))), d => Assert.Equal(HandUse.Together, d.Hands));
    }

    [Fact]
    public void ALevelIsARoutineOfItsActivities_EachAsRcmAsksItInItsFirstKey()
    {
        var level = RcmTechnique.Level("1")!;

        Assert.Equal("RCM Level 1 technique", level.Title);
        Assert.Equal(["rcm-1-scales", "rcm-1-contrary", "rcm-1-chromatic", "rcm-1-broken", "rcm-1-solid"], level.Items.Select(i => i.Recipe.Id));
        var scales = level.Items[0].Recipe;
        Assert.Equal(("drill", "scales", 69.0, 0, false, HandUse.Right, 3), (scales.Kind, scales.TargetId, scales.Tempo, scales.Fifths, scales.Minor, scales.Hands, scales.Minutes));
        Assert.Equal("RCM Level 1: scales, two octaves, a key and a hand a day, in C, G and F major; A, E and D minor, natural and harmonic.", scales.Purpose);
        Assert.Equal("RCM Level 1: chromatic scale, a hand a day, from C.", level.Items[2].Recipe.Purpose);
        Assert.Equal("RCM Level 1: contrary motion, in C major.", level.Items[1].Recipe.Purpose);
        Assert.StartsWith("What RCM's 2022 piano syllabus asks of Level 1's technique (page 19), a key a day and,", level.Summary, StringComparison.Ordinal);
        Assert.EndsWith("Not in Prima yet: the formula pattern.", RcmTechnique.Level("2")!.Summary, StringComparison.Ordinal);
        // The routines' own rule: no technical activity over five minutes.
        Assert.All(RcmTechnique.Levels.SelectMany(l => l.Items), i => Assert.InRange(i.Recipe.Minutes, 1, 5));
    }

    // A drill writes RCM's notes, so its tempo is RCM's; an exercise keeps its own, so RCM's goes into its beat at the same notes a minute.
    [Fact]
    public void ATargetIsRcmsTempo_InAnExercisesOwnBeatWhereItsNotesDiffer()
    {
        var broken = RcmTechnique.Find("rcm-1-broken")!;
        var solid = RcmTechnique.Find("rcm-1-solid")!;

        Assert.Equal((50.0, 150.0), (broken.Rcm, broken.Tempo));
        Assert.Equal("RCM Level 1 asks at least ♩ 50 in triplets: ♩ 150 in this music's beat.", broken.Asks);
        Assert.Equal("RCM Level 1 asks at least ♩ 100 with a chord every two beats.", solid.Asks);
        Assert.Equal("RCM Level 1 asks at least ♩ 69 in eighths.", RcmTechnique.Find("rcm-1-chromatic")!.Asks);
        Assert.Equal("From C, left hand", new RcmToday(RcmTechnique.Find("rcm-1-chromatic")!, new Key(0), ScaleKind.Major, HandUse.Left).Played);
        Assert.Equal("A minor, natural, right hand", new RcmToday(RcmTechnique.Find("rcm-1-scales")!, new Key(0, true), ScaleKind.NaturalMinor, HandUse.Right).Played);
    }

    [Fact]
    public void ANaturalMinorScaleRaisesNothing_AndRcmsEighthsAreAnItemOfTheirOwn()
    {
        var natural = new TechniqueChoice(new Key(0, true), ScaleKind.NaturalMinor, 2, HandUse.Right, 1) { PerBeat = 2 }.Item("scales");
        var harmonic = new TechniqueChoice(new Key(0, true), ScaleKind.HarmonicMinor, 2, HandUse.Right, 1).Item("scales");

        Assert.Equal(ScaleKind.NaturalMinor, natural.Kind);
        Assert.DoesNotContain(natural.Right, n => n.Midi % 12 == 8);
        Assert.Contains(harmonic.Right, n => n.Midi % 12 == 8);
        // In eighths, as RCM writes it, it keeps its own tempo: the same number is another speed in sixteenths.
        Assert.Equal(("A minor natural scale, 2 octaves, in eighths", 2), (natural.Id, natural.NotesPerBeat));
        Assert.Equal(("A minor harmonic scale, 2 octaves", 4), (harmonic.Id, harmonic.NotesPerBeat));
        Assert.Equal(Ticks.Eighth, natural.Score.Bars[0].Voices[0].Events.OfType<Notes>().First().Length);
        Assert.Equal("C major arpeggio, 2 octaves", new TechniqueChoice(new Key(0), ScaleKind.Arpeggio, 2, HandUse.Right, 1) { PerBeat = 2 }.Item("arpeggios").Id);
    }
}
