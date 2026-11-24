using Bunit;
using Microsoft.Extensions.DependencyInjection;
using Prima.UI.Pages;
using Prima.UI.Routines;

namespace Prima.UI.Tests;

public class RoutineRequestsKeptTests
{
    // Review of main at a7ab94d. PrimaStore.Routines.cs:28-29 (GetRoutineJobsAsync) takes the 30 requests last updated, and
    // RoutineLibrary.razor:129 leaves the accepted ones out only after that. Accepting a request updates it, so thirty accepted
    // after one that is still waiting push it off Routines, and since e202312 took Recent requests off My routine, nothing else
    // lists it. Music/README.md: Routines "keeps every routine accepted and every request that isn't one yet".
    [Fact]
    public async Task ARequestThatIsNotARoutineYetStaysInAskedForHoweverManyAreAcceptedAfterIt()
    {
        await using var host = new ComponentHost();
        var service = host.Context.Services.GetRequiredService<RoutineService>();
        await service.RecentAsync();
        var waiting = RoutineStoreTests.ReadyJob() with { Updated = DateTimeOffset.UtcNow.AddDays(-30), Draft = new("Left-hand chords", "Shapes", RoutineStoreTests.ReadyJob().Draft!.Items) };
        await host.Store.CreateRoutineJobAsync(waiting);
        for (var i = 0; i < 30; i++)
        {
            var job = RoutineStoreTests.ReadyJob();
            await host.Store.CreateRoutineJobAsync(job);
            await host.Store.AcceptRoutineAsync(job.Id);
        }

        var view = host.Context.Render<RoutineLibrary>();
        view.WaitForElement("[data-routine-job]");

        Assert.Equal(30, view.FindAll("[data-routine]").Count);
        Assert.Equal([waiting.Id], view.FindAll("[data-routine-job]").Select(r => r.GetAttribute("data-routine-job")));
    }
}
