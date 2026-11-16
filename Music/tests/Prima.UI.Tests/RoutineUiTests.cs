using Bunit;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.AspNetCore.Components.Web;
using Prima.Core.Generation;
using Prima.Core.Music;
using Prima.Core.Routines;
using Prima.UI.Components;
using Prima.UI.Pages;
using Prima.UI.Routines;

namespace Prima.UI.Tests;

public class RoutineUiTests
{
    [Fact]
    public async Task OpeningTheBuilderDoesNotDispatchAndItsRequestSurvivesNavigation()
    {
        await using var host = new ComponentHost();
        var view = host.Context.Render<RoutineAsk>();
        // The form shows once the recent practice is looked at, which takes longer than bUnit's second with every test running.
        var goal = view.WaitForElement("#routine-goal", TimeSpan.FromSeconds(10));
        Assert.Equal(0, host.Claude.Calls);
        Assert.Equal("off", view.Find("[data-action='routine-generate']").GetAttribute("data-piano"));
        Assert.True(view.Find("[data-action='routine-generate']").HasAttribute("disabled"));
        // Each change is saved as it's made: waited for, so the store is read after the saves, not during them.
        await view.InvokeAsync(() => goal.ChangeAsync("The move into A needs work"));
        await view.InvokeAsync(() => view.Find("#routine-minutes").ChangeAsync("15"));

        var stored = await host.Store.GetTrackAsync<RoutineAsk.SavedRequest>("routine-request-form");
        Assert.NotNull(stored);
        Assert.Equal(15, stored.Minutes);
        Assert.Equal("The move into A needs work", stored.Goal);
        Assert.Equal(0, host.Claude.Calls);
    }

    [Fact]
    public async Task DraftOrderAndRemovalAreSavedBeforeExplicitAcceptanceAndPinsRemain()
    {
        await using var host = new ComponentHost();
        var service = host.Context.Services.GetRequiredService<RoutineService>();
        var request = new RoutineRequest(10, "Improve jumps");
        var draft = new RoutineDraft("Practice", "A short routine", [
            new RoutineItem { Id = "first", TargetId = "octave-jump", Title = "First activity", Purpose = "Practice the landing", Minutes = 3 },
            new RoutineItem { Id = "second", TargetId = "jump-ladder", Title = "Second activity", Purpose = "Practice distances", Minutes = 2 },
        ]);
        var job = new RoutineGenerationJob("proposal", 0, RoutineGenerationState.Ready, DateTimeOffset.UtcNow, request,
            new PracticeEvidenceSnapshot(DateTimeOffset.UtcNow, []), null, "test-provider", "test", draft, RoutineCompiler.Prepare(draft, request));
        // Construct the service before inserting a completed proposal, as the application does before creating requests.
        await service.RecentAsync();
        await host.Store.CreateRoutineJobAsync(job);
        await host.Store.SaveTrackAsync("exercise-routine", new[] { "octave-jump" });
        var view = host.Context.Render<RoutineReview>(p => p.Add(c => c.JobId, job.Id));
        view.WaitForElement("[data-routine-item='first'] [data-action='routine-move-down']");
        await view.InvokeAsync(() => view.Find("[data-routine-item='first'] [data-action='routine-move-down']").ClickAsync(new MouseEventArgs()));
        view.WaitForAssertion(() => Assert.Equal("second", view.FindAll("[data-routine-item]")[0].GetAttribute("data-routine-item")));
        Assert.Equal("second", (await service.GetJobAsync(job.Id))!.Draft!.Items[0].Id);
        Assert.Null(await service.ActiveAsync());

        await view.InvokeAsync(() => view.Find("[data-routine-item='first'] [data-action='routine-remove-item']").ClickAsync(new MouseEventArgs()));
        view.WaitForAssertion(() => Assert.Single(view.FindAll("[data-routine-item]")));
        await view.InvokeAsync(() => view.Find("[data-action='routine-accept']").ClickAsync(new MouseEventArgs()));
        var accepted = await service.ActiveAsync();
        Assert.NotNull(accepted);
        Assert.Equal("second", Assert.Single(accepted.Items).Recipe.Id);
        var pins = await host.Store.GetTrackAsync<string[]>("exercise-routine");
        Assert.NotNull(pins);
        Assert.Equal(["octave-jump"], pins);
        Assert.Equal(0, host.Claude.Calls);
    }

    [Fact]
    public async Task ExercisesKeepThePrimaryRoutineActionOutsidePianoNavigation()
    {
        await using var host = new ComponentHost();
        var view = host.Context.Render<ExerciseList>();
        view.WaitForElement(".exercise-filters[aria-busy='false']");
        var action = view.WaitForElement("[data-action='ask-routine']");
        Assert.Equal("routine/ask", action.GetAttribute("href"));
        Assert.Equal("off", action.GetAttribute("data-piano"));
        Assert.Equal(0, host.Claude.Calls);
    }

    [Fact]
    public async Task GeneratedRhythmUsesAnyKeyScoringAndItsSavedSnapshot()
    {
        await using var host = new ComponentHost();
        var service = host.Context.Services.GetRequiredService<RoutineService>();
        await service.RecentAsync();
        var score = new Score(new Prima.Core.Music.Key(0), new Meter(4, 4), [new Bar(1, [new Voice(1,
            [new Notes("n", 0, 1920, [new Tone("t", new Spelled(Step.E, 0, 4))])])])]);
        var mei = MeiWriter.Write(score);
        var source = new RoutineSource("source", "Source", RoutineCompiler.SourceRevision(mei), mei, true);
        var request = new RoutineRequest(5, "Rhythm", source.PieceId);
        var item = new RoutineItem { Id = "rhythm-item", Kind = "generated", TargetId = "rhythm", Title = "Rhythm", Purpose = "Tap the source rhythm", Mode = "time", Hands = HandUse.Right, FirstBar = 1, LastBar = 1 };
        var draft = new RoutineDraft("Practice", "Source rhythm", [item]);
        var job = new RoutineGenerationJob("rhythm-proposal", 0, RoutineGenerationState.Ready, DateTimeOffset.UtcNow, request,
            new PracticeEvidenceSnapshot(DateTimeOffset.UtcNow, []), source, "test-provider", "test", draft, RoutineCompiler.Prepare(draft, request, source));
        await host.Store.CreateRoutineJobAsync(job);
        await service.AcceptAsync(job.Id);
        var day = await service.TodayAsync();
        var execution = await service.BeginExecutionAsync(day!.Id, item.Id);
        var view = host.Context.Render<RoutinePlay>(p => p.Add(c => c.ExecutionId, execution.Id));
        view.WaitForAssertion(() => Assert.True(view.FindComponent<PlayAlong>().Instance.RhythmOnly));
        Assert.Equal(execution.Item.Mei, view.FindComponent<Sheet>().Instance.Mei);
        Assert.False(view.FindComponent<PlayAlong>().Instance.ShowMode);
        Assert.Equal(0, host.Claude.Calls);
    }

    // Routines Claude wrote before it was asked for American spelling are shown spelled as Prima spells.
    [Theory]
    [InlineData("Warm up, then practise the two leaps", "Warm up, then practice the two leaps")]
    [InlineData("Practised slowly; PRACTISING daily", "Practiced slowly; PRACTICING daily")]
    [InlineData("Practice what you practice", "Practice what you practice")]
    [InlineData(null, "")]
    public void RoutineWordsAreShownInAmericanSpelling(string? written, string shown) => Assert.Equal(shown, RoutineWords.American(written));

    // Routines: the one followed, what's been asked for and isn't a routine yet, and every routine kept, to follow another.
    [Fact]
    public async Task RoutinesShowsTheOneFollowedTheRequestsAndEveryRoutineKeptAndFollowsAnother()
    {
        await using var host = new ComponentHost();
        var service = host.Context.Services.GetRequiredService<RoutineService>();
        await service.RecentAsync();
        var first = RoutineStoreTests.ReadyJob() with { Draft = new("Landing on A", "Leaps up to A", RoutineStoreTests.ReadyJob().Draft!.Items) };
        var second = RoutineStoreTests.ReadyJob() with { Draft = new("Scales, slowly", "Even scales", RoutineStoreTests.ReadyJob().Draft!.Items) };
        var waiting = RoutineStoreTests.ReadyJob() with { Draft = new("Left-hand chords", "Shapes", RoutineStoreTests.ReadyJob().Draft!.Items) };
        var failed = RoutineStoreTests.ReadyJob() with { State = RoutineGenerationState.Failed, Draft = null, Prepared = null, Problem = "Claude couldn't be asked" };
        foreach (var job in new[] { first, second, waiting, failed }) await host.Store.CreateRoutineJobAsync(job);
        var landing = await service.AcceptAsync(first.Id);
        var scales = await service.AcceptAsync(second.Id);

        var view = host.Context.Render<RoutineLibrary>();
        view.WaitForElement("[data-state='routine-following']");
        Assert.Contains("Scales, slowly", view.Find("[data-state='routine-following']").TextContent);
        Assert.Equal([scales.Revision, landing.Revision], view.FindAll("[data-routine]").Select(r => r.GetAttribute("data-routine")));
        Assert.Equal("now", view.Find($"[data-routine='{scales.Revision}']").GetAttribute("data-state"));
        Assert.Equal(["ready", "failed"], view.FindAll("[data-routine-job]").Select(r => r.GetAttribute("data-state")).Order(StringComparer.Ordinal).Reverse());
        Assert.Contains("Ready to look over", view.Find($"[data-routine-job='{waiting.Id}']").TextContent);
        Assert.Equal($"routine/jobs/{failed.Id}", view.Find($"[data-routine-job='{failed.Id}']").GetAttribute("href"));

        await view.InvokeAsync(() => view.Find($"[data-routine='{landing.Revision}'] [data-action='routine-follow']").ClickAsync(new MouseEventArgs()));
        view.WaitForAssertion(() => Assert.Equal("now", view.Find($"[data-routine='{landing.Revision}']").GetAttribute("data-state")));
        Assert.Contains("Landing on A", view.Find("[data-state='routine-following']").TextContent);
        Assert.Equal(landing.Revision, (await service.ActiveAsync())!.Revision);
        Assert.Equal(0, host.Claude.Calls);
    }
}
