using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Prima.Core.Generation;
using Prima.Core.Routines;
using Prima.UI.Components;
using Prima.UI.Pages;
using Prima.UI.Practice;
using Prima.UI.Routines;

namespace Prima.UI.Tests;

/// <summary>
/// RCM's technique for a level as a routine of yours (the owner, 25 November 2026: "Do the two", RCM's technique up to Level 4
/// being the second): made from Routines when it's asked for, its activities played in the key and hands the day brings, with
/// RCM's minimum tempo as the target.
/// </summary>
public class RcmTechniqueRoutineTests
{
    [Fact]
    public async Task RcmTechniqueMakesALevelARoutineOfYours_AndOpensTheOneMadeBeforeWhenAskedAgain()
    {
        await using var host = new ComponentHost();
        var service = host.Context.Services.GetRequiredService<RoutineService>();
        await service.RecentAsync();
        var view = host.Context.Render<RoutineLibrary>();
        view.WaitForElement("[data-action='rcm-routine']");

        await view.InvokeAsync(() => view.Find("[data-action='rcm-routine']").ClickAsync(new MouseEventArgs()));
        Assert.Equal(["Preparatory A", "Preparatory B", "Level 1", "Level 2", "Level 3", "Level 4"], view.FindAll(".rcm-levels [data-action^='rcm-']").Select(b => b.TextContent));
        await view.InvokeAsync(() => view.Find("[data-action='rcm-1']").ClickAsync(new MouseEventArgs()));

        var made = (await service.RoutinesAsync()).Single(r => r.Title == "RCM Level 1 technique");
        Assert.True(made.Own);
        Assert.Equal(RcmTechnique.Level("1")!.Summary, made.Summary);
        Assert.Equal(["rcm-1-scales", "rcm-1-contrary", "rcm-1-chromatic", "rcm-1-broken", "rcm-1-solid"], made.Items.Select(i => i.Recipe.Id));
        var nav = host.Context.Services.GetRequiredService<NavigationManager>();
        Assert.EndsWith($"routines/{made.Revision}", nav.Uri, StringComparison.Ordinal);

        var again = host.Context.Render<RoutineLibrary>();
        again.WaitForAssertion(() => Assert.Contains(again.FindAll("a"), a => a.TextContent == "RCM Level 1 technique"));
        await again.InvokeAsync(() => again.Find("[data-action='rcm-routine']").ClickAsync(new MouseEventArgs()));
        await again.InvokeAsync(() => again.Find("[data-action='rcm-1']").ClickAsync(new MouseEventArgs()));

        Assert.Single(await service.RoutinesAsync(), r => r.Title == "RCM Level 1 technique");
        Assert.EndsWith($"routines/{made.Revision}", nav.Uri, StringComparison.Ordinal);
    }

    // Its scale is the day's key, form and hands, two octaves in eighths as RCM writes them, its own speed found first in Wait for
    // me as anywhere, and RCM's tempo beside its target.
    [Fact]
    public async Task ItsScaleIsTheDaysKeyFormAndHandsInEighths_WithRcmsTempoBesideItsTarget()
    {
        await using var host = new ComponentHost();
        var service = host.Context.Services.GetRequiredService<RoutineService>();
        var level = RcmTechnique.Level("1")!;
        var routine = await service.MakeAsync(level.Title, level.Items, level.Summary);
        var day = await service.DayAsync(routine.Revision);
        var today = RcmTechnique.Find("rcm-1-scales")!.On(day!.Day);
        host.Context.Services.GetRequiredService<NavigationManager>().NavigateTo(await service.OpenAsync(day.Id, "rcm-1-scales"));

        var view = host.Context.Render<TechniquePractice>();

        view.WaitForAssertion(() => Assert.Contains(view.FindAll(".mu-readout"), r => r.TextContent == "RCM Level 1♩ 69"));
        var player = view.FindComponent<PlayAlong>().Instance;
        Assert.Equal(today.Key, player.Music!.Score.Key);
        Assert.All(player.Music.Notes, note => Assert.Equal(today.Hands == HandUse.Left ? 2 : 1, note.Staff));
        Assert.Equal(29, player.Music.Notes.Count);
        Assert.Equal(Prima.Core.Music.Ticks.Eighth, player.Music.Score.Bars[0].Voices.SelectMany(v => v.Events).OfType<Prima.Core.Music.Notes>().First().Length);
        Assert.Equal(PlayMode.WaitForMe, player.Ask);
    }

    // An exercise of it is the day's key and hands, at RCM's tempo in the exercise's own beat, and says what RCM asks.
    [Fact]
    public async Task ItsExerciseIsTheDaysKeyAndHandsAtRcmsTempoInItsOwnBeat()
    {
        await using var host = new ComponentHost();
        var service = host.Context.Services.GetRequiredService<RoutineService>();
        var level = RcmTechnique.Level("1")!;
        var routine = await service.MakeAsync(level.Title, level.Items, level.Summary);
        var day = await service.DayAsync(routine.Revision);
        var today = RcmTechnique.Find("rcm-1-broken")!.On(day!.Day);
        host.Context.Services.GetRequiredService<NavigationManager>().NavigateTo(await service.OpenAsync(day.Id, "rcm-1-broken"));

        var view = host.Context.Render<ExercisePractice>(p => p.Add(c => c.Id, "broken-inversions"));

        view.WaitForAssertion(() => Assert.Equal(150, view.FindComponent<PlayAlong>().Instance.Tempo));
        Assert.Equal($"{today.Played}. RCM Level 1 asks at least ♩ 50 in triplets: ♩ 150 in this music's beat.", view.Find(".rcm-asks").TextContent);
        Assert.Equal(today.Key, view.FindComponent<PlayAlong>().Instance.Music!.Score.Key);
        // It leaves the exercise as you last left it elsewhere.
        Assert.Null(await host.Store.GetTrackAsync<ExerciseChoice>("exercise:broken-inversions"));
    }
}
