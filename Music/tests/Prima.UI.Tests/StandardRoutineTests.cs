using Microsoft.Extensions.DependencyInjection;
using Prima.Core.Generation;
using Prima.Core.Routines;
using Prima.UI.Routines;

namespace Prima.UI.Tests;

/// <summary>
/// The routines that come with Prima: put in once as routines of yours, in their own order, with what each is for; one taken
/// away doesn't come back, one changed stays changed, and one a later Prima adds comes in once. Each is made of exercises and
/// drills a routine can have.
/// </summary>
public class StandardRoutineTests
{
    [Fact]
    public void Each_is_made_of_exercises_and_drills_a_routine_can_have()
    {
        Assert.True(StandardRoutines.All.Count >= 10);
        Assert.Equal(StandardRoutines.All.Count, StandardRoutines.All.Select(r => r.Key).Distinct().Count());
        Assert.Equal(StandardRoutines.All.Count, StandardRoutines.All.Select(r => r.Title).Distinct(StringComparer.OrdinalIgnoreCase).Count());
        foreach (var routine in StandardRoutines.All)
        {
            Assert.InRange(routine.Title.Length, 3, 40);
            Assert.NotEmpty(routine.Purpose);
            Assert.Contains(routine.Level, new[] { "beginner", "intermediate", "advanced", "any" });
            Assert.InRange(routine.Minutes, 4, 60);
            foreach (var activity in routine.Activities)
            {
                var exercise = Exercises.Find(activity.Id);
                Assert.True(exercise is not null && OwnRoutines.CanHave(exercise), $"{routine.Title}: {activity.Id} isn't an exercise or drill a routine can have");
                Assert.InRange(activity.Minutes, 1, 30);
            }
        }
    }

    [Fact]
    public async Task They_come_in_once_as_yours_and_one_taken_away_stays_away()
    {
        await using var host = new ComponentHost();
        // As on a tablet that has never had them.
        await host.Store.SaveTrackAsync<List<string>>(RoutineService.StandardTrack, []);
        var service = host.Context.Services.GetRequiredService<RoutineService>();

        var mine = (await service.RoutinesAsync()).Where(r => r.Own).ToList();
        Assert.Equal(StandardRoutines.All.Select(r => r.Title), mine.Select(r => r.Title));
        var first = StandardRoutines.All[0];
        Assert.Equal(first.Purpose, mine[0].Summary);
        Assert.Equal(first.Activities.Select(a => (a.Id, a.Minutes)), mine[0].Items.Select(i => (OwnRoutines.ExerciseOf(i.Recipe)!.Id, i.Recipe.Minutes)));
        Assert.Equal(StandardRoutines.All.Select(r => r.Key), await host.Store.GetTrackAsync<List<string>>(RoutineService.StandardTrack));

        // One taken away, one renamed, and one a later Prima adds: the next start puts in only the new one.
        await service.DeleteAsync(mine[0].Revision);
        await service.SaveAsync(mine[1] with { Title = "Mine now" });
        await host.Store.SaveTrackAsync<List<string>>(RoutineService.StandardTrack, [.. StandardRoutines.All.Select(r => r.Key).Where(k => k != StandardRoutines.All[^1].Key)]);
        await service.DeleteAsync(mine[^1].Revision);
        var again = new RoutineService(host.Store, host.Context.Services.GetRequiredService<Playground.AI.Assist.IInferenceProvider>(), host.Context.Services.GetRequiredService<Prima.UI.PrimaSettings>(), host.Context.Services.GetRequiredService<Prima.UI.Host.ILibraryBundle>());
        var now = (await again.RoutinesAsync()).Where(r => r.Own).Select(r => r.Title).ToList();
        Assert.DoesNotContain(first.Title, now);
        Assert.Contains("Mine now", now);
        Assert.Single(now, StandardRoutines.All[^1].Title);
        Assert.Equal(StandardRoutines.All.Count - 1, now.Count);
    }
}
