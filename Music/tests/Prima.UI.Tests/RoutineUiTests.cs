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
    public async Task DraftOrderAndRemovalAreSavedBeforeExplicitAcceptanceAndYourRoutinesStayAsTheyWere()
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
        var mine = await service.MakeAsync("Leaps", [OwnRoutines.Activity(Exercises.Find("octave-jump")!)]);
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
        Assert.Equal(mine.Items.Select(i => (i.Recipe.Id, i.Recipe.TargetId, i.Recipe.Minutes)), (await service.GetDefinitionAsync(mine.Revision))!.Items.Select(i => (i.Recipe.Id, i.Recipe.TargetId, i.Recipe.Minutes)));
        Assert.Equal(0, host.Claude.Calls);
    }

    [Fact]
    public async Task ExercisesKeepThePrimaryRoutineActionOutsidePianoNavigation()
    {
        await using var host = new ComponentHost();
        var view = host.Context.Render<ExerciseList>();
        view.WaitForElement(".lib-list[aria-busy='false']");
        var action = view.WaitForElement("[data-action='new-routine']");
        Assert.Equal("routines?new=1", action.GetAttribute("href"));
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

    // Routines: yours and Claude's, three starred on top as cards and the rest in rows under them, the one Today follows marked,
    // and what's been asked of Claude that isn't a routine yet. A fourth star waits for one to come off. A routine's page puts it
    // on Today, or takes it off.
    [Fact]
    public async Task RoutinesShowsThreeStarredOnTopTheRestUnderThemAndTheRequestsAndPutsAnotherOnToday()
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
        var leaps = await service.MakeAsync("Leaps", [OwnRoutines.Activity(Exercises.Find("octave-jump")!), OwnRoutines.Activity(Exercises.Find("jump-ladder")!)]);
        var trills = await service.MakeAsync("Trills", [OwnRoutines.Activity(Exercises.Find("trill-2-3")!)]);
        var thirds = await service.MakeAsync("Thirds", [OwnRoutines.Activity(Exercises.Find("thirds")!)]);
        foreach (var starred in new[] { leaps, landing, trills }) Assert.True(await service.StarAsync(starred.Revision, true));
        Assert.False(await service.StarAsync(thirds.Revision, true));

        var view = host.Context.Render<RoutineLibrary>();
        view.WaitForElement(".rt-card");
        Assert.Equal([leaps.Revision, landing.Revision, trills.Revision], view.FindAll(".rt-card").Select(c => c.GetAttribute("data-routine")));
        Assert.Equal(new[] { scales.Revision, thirds.Revision }.Order(StringComparer.Ordinal), view.FindAll(".rt-rest [data-routine]").Select(r => r.GetAttribute("data-routine")!).Order(StringComparer.Ordinal));
        Assert.Equal("now", view.Find($"[data-routine='{scales.Revision}']").GetAttribute("data-state"));
        Assert.StartsWith("Yours · 2 · ", view.Find($".rt-card[data-routine='{leaps.Revision}'] .rep-kicker").TextContent);
        Assert.Equal(["Everything·5", "Yours·3", "From Claude·2"], view.FindAll(".settings-tab").Select(t => t.TextContent.Trim()));
        Assert.Equal(["ready", "failed"], view.FindAll("[data-routine-job]").Select(r => r.GetAttribute("data-state")).Order(StringComparer.Ordinal).Reverse());
        Assert.Contains("Ready to look over", view.Find($"[data-routine-job='{waiting.Id}']").TextContent);
        Assert.Equal($"routine/jobs/{failed.Id}", view.Find($"[data-routine-job='{failed.Id}']").GetAttribute("href"));

        // A star on a fourth waits for one to come off; a star taken off puts it with the rest.
        await view.InvokeAsync(() => view.Find($"[data-routine='{thirds.Revision}'] .lb-star").ClickAsync(new MouseEventArgs()));
        view.WaitForAssertion(() => Assert.Contains("Three are starred", view.Find("[role='status']").TextContent));
        await view.InvokeAsync(() => view.Find($".rt-card[data-routine='{trills.Revision}'] .lb-star").ClickAsync(new MouseEventArgs()));
        view.WaitForAssertion(() => Assert.Equal(2, view.FindAll(".rt-card").Count));
        Assert.Equal([leaps.Revision, landing.Revision], await service.StarredAsync());

        // On Today, from the routine's own page.
        var page = host.Context.Render<Routine>(p => p.Add(c => c.Revision, landing.Revision));
        page.WaitForElement("[data-action='routine-today']");
        Assert.Equal("false", page.Find("[data-action='routine-today']").GetAttribute("aria-pressed"));
        await page.InvokeAsync(() => page.Find("[data-action='routine-today']").ClickAsync(new MouseEventArgs()));
        page.WaitForAssertion(() => Assert.Equal("true", page.Find("[data-action='routine-today']").GetAttribute("aria-pressed")));
        Assert.Equal(landing.Revision, (await service.ActiveAsync())!.Revision);
        Assert.Equal(0, host.Claude.Calls);
    }

    // A routine of yours: made under a name and opened; filled from the catalogue with a card's +, which is lit once it's in one;
    // and on its page its activities' minutes and places changed, one taken out, renamed, and the routine taken away.
    [Fact]
    public async Task ARoutineOfYoursIsMadeFilledFromTheCatalogueAndChangedOnItsPage()
    {
        await using var host = new ComponentHost();
        var service = host.Context.Services.GetRequiredService<RoutineService>();
        var nav = host.Context.Services.GetRequiredService<Microsoft.AspNetCore.Components.NavigationManager>();
        nav.NavigateTo("routines?new=1");
        var list = host.Context.Render<RoutineLibrary>();
        await list.InvokeAsync(() => list.WaitForElement("[aria-label=\"The new routine's name\"]").InputAsync(new Microsoft.AspNetCore.Components.ChangeEventArgs { Value = "Leaps" }));
        await list.InvokeAsync(() => list.Find("[data-action='make-routine']").ClickAsync(new MouseEventArgs()));
        var made = Assert.Single(await service.RoutinesAsync());
        Assert.True(made.Own);
        Assert.Equal(("Leaps", 0), (made.Title, made.Items.Count));
        Assert.EndsWith($"routines/{made.Revision}", nav.Uri);

        // Three put in from their cards, the last a drill.
        var scalesName = Exercises.All.First(e => e.Route == "drills/scales").Name;
        foreach (var (type, id) in new[] { ("jumps", "octave-jump"), ("jumps", "jump-ladder"), ("drills", "drills/scales") })
        {
            nav.NavigateTo($"exercises?type={type}");
            var catalogue = host.Context.Render<ExerciseList>();
            catalogue.WaitForElement(".lib-list[aria-busy='false']");
            catalogue.WaitForAssertion(() => Assert.NotEmpty(catalogue.FindAll($".ex-card[href$='{id}']")));
            var plus = catalogue.Find($".ex-card[href$='{id}']").Closest(".ex-card-at")!.QuerySelector(".ex-add")!;
            Assert.Equal("false", plus.GetAttribute("aria-pressed"));
            await catalogue.InvokeAsync(() => catalogue.Find($".ex-card[href$='{id}'] ~ .ex-add").ClickAsync(new MouseEventArgs()));
            var into = catalogue.WaitForElement(".lb-menu.add-to [role='menuitemcheckbox']");
            Assert.Equal(("Leaps", "false"), (into.TextContent.Trim(), into.GetAttribute("aria-checked")));
            await catalogue.InvokeAsync(() => catalogue.Find(".lb-menu.add-to [role='menuitemcheckbox']").ClickAsync(new MouseEventArgs()));
            catalogue.WaitForAssertion(() => Assert.Equal("true", catalogue.Find(".lb-menu.add-to [role='menuitemcheckbox']").GetAttribute("aria-checked")));
            await catalogue.InvokeAsync(() => catalogue.Find(".lb-away").ClickAsync(new MouseEventArgs()));
            catalogue.WaitForAssertion(() => Assert.Equal("true", catalogue.Find($".ex-card[href$='{id}'] ~ .ex-add").GetAttribute("aria-pressed")));
        }
        made = (await service.GetDefinitionAsync(made.Revision))!;
        Assert.Equal([("exercise", "octave-jump"), ("exercise", "jump-ladder"), ("drill", "scales")], made.Items.Select(i => (i.Recipe.Kind, i.Recipe.TargetId)));

        // Its page: the last moved up, a minute more on it, the first taken out, and renamed.
        var page = host.Context.Render<Routine>(p => p.Add(c => c.Revision, made.Revision));
        page.WaitForElement("[data-action='routine-edit']");
        await page.InvokeAsync(() => page.Find("[data-action='routine-edit']").ClickAsync(new MouseEventArgs()));
        await page.InvokeAsync(() => page.Find($"[aria-label='Move {scalesName} up']").ClickAsync(new MouseEventArgs()));
        page.WaitForAssertion(() => Assert.Equal(["octave-jump", "scales", "jump-ladder"], page.FindAll("[data-routine-item]").Select(i => (made.Items.First(m => m.Recipe.Id == i.GetAttribute("data-routine-item")).Recipe.TargetId))));
        await page.InvokeAsync(() => page.Find($"[aria-label='{scalesName}: a minute more']").ClickAsync(new MouseEventArgs()));
        await page.InvokeAsync(() => page.Find($"[aria-label='Take {made.Items[0].Recipe.Title} out']").ClickAsync(new MouseEventArgs()));
        page.WaitForAssertion(() => Assert.Equal(2, page.FindAll("[data-routine-item]").Count));
        await page.InvokeAsync(() => page.Find("[aria-label=\"The routine's name\"]").InputAsync(new Microsoft.AspNetCore.Components.ChangeEventArgs { Value = "Leaps and scales" }));
        await page.InvokeAsync(() => page.Find("[aria-label=\"The routine's name\"]").KeyDownAsync(new KeyboardEventArgs { Key = "Enter" }));
        var changed = (await service.GetDefinitionAsync(made.Revision))!;
        Assert.Equal("Leaps and scales", changed.Title);
        Assert.Equal([("scales", 6), ("jump-ladder", 3)], changed.Items.Select(i => (i.Recipe.TargetId, i.Recipe.Minutes)));
        Assert.Equal(9, changed.Request.Minutes);

        // Taken away, after asking.
        await page.InvokeAsync(() => page.Find("[data-action='routine-remove']").ClickAsync(new MouseEventArgs()));
        await page.InvokeAsync(() => page.Find("[data-action='routine-remove-confirm']").ClickAsync(new MouseEventArgs()));
        Assert.Null(await service.GetDefinitionAsync(made.Revision));
        Assert.EndsWith("routines", nav.Uri);
        Assert.Equal(0, host.Claude.Calls);
    }

    // The exercises once kept as My routine, a list of them with nothing else, are now a routine of yours by that name.
    [Fact]
    public async Task TheExercisesOnceKeptAsMyRoutineAreARoutineOfYoursByThatName()
    {
        await using var host = new ComponentHost();
        await host.Store.SaveTrackAsync("exercise-routine", new[] { "octave-jump", "not-an-exercise", "thirds" });
        var service = host.Context.Services.GetRequiredService<RoutineService>();

        var mine = Assert.Single(await service.RoutinesAsync());
        Assert.True(mine.Own);
        Assert.Equal("My routine", mine.Title);
        Assert.Equal(["octave-jump", "thirds"], mine.Items.Select(i => i.Recipe.TargetId));
        Assert.Empty((await host.Store.GetTrackAsync<string[]>("exercise-routine"))!);
    }

    // The owner's rules for routines, decided 15 November (the review of main at a7ab94d, part 5).

    // 39. The Routines key is lit during an exercise or drill opened from a routine (its address carries the routine's run), and
    // it's the only key lit; the same exercise opened from Exercises lights Exercises.
    [Theory]
    [InlineData("exercises/octave-jump?execution=run", "Routines")]
    [InlineData("drills/playback?execution=run", "Routines")]
    [InlineData("drills/scales?execution=run", "Routines")]
    [InlineData("routine/play/run", "Routines")]
    [InlineData("exercises/octave-jump", "Exercises")]
    [InlineData("drills/playback", "Exercises")]
    [InlineData("drills", "Exercises")]
    public async Task TheRoutinesKeyIsLitDuringAnExerciseOrDrillOpenedFromARoutine(string address, string lit)
    {
        await using var host = new ComponentHost();
        host.Context.Services.GetRequiredService<Microsoft.AspNetCore.Components.NavigationManager>().NavigateTo(address);

        var rail = host.Context.Render<Prima.UI.Layout.Rail>();

        Assert.Equal([lit], rail.FindAll(".mu-nav-item.active > span:last-child").Select(k => k.TextContent));
    }

    // 40. Only Claude's words are respelled the American way: not the goal the owner typed, nor a piece's title.
    [Fact]
    public async Task OnlyClaudesWordsAreRespelledNotTheGoalTypedNorAPiecesTitle()
    {
        await using var host = new ComponentHost();
        var service = host.Context.Services.GetRequiredService<RoutineService>();
        await service.RecentAsync();
        var failed = RoutineStoreTests.ReadyJob() with { State = RoutineGenerationState.Failed, Draft = null, Prepared = null, Request = new(10, "Practise the left hand's leaps") };
        var forPiece = failed with { Id = "for-piece", Request = new(10, ""), Source = new RoutineSource("waltz", "Practise Waltz", "revision", "<mei/>", true) };
        var answered = RoutineStoreTests.ReadyJob() with { Draft = new("Practise the leaps", "Practise slowly", RoutineStoreTests.ReadyJob().Draft!.Items) };
        foreach (var job in new[] { failed, forPiece, answered }) await host.Store.CreateRoutineJobAsync(job);

        var view = host.Context.Render<RoutineLibrary>();
        view.WaitForElement($"[data-routine-job='{answered.Id}']");

        Assert.Equal("Practise the left hand's leaps", view.Find($"[data-routine-job='{failed.Id}'] .piece-title").TextContent);
        Assert.Equal("Practise Waltz", view.Find($"[data-routine-job='{forPiece.Id}'] .piece-title").TextContent);
        Assert.Contains("Practise Waltz", view.Find($"[data-routine-job='{forPiece.Id}'] .piece-meta").TextContent);
        Assert.Equal("Practice the leaps", view.Find($"[data-routine-job='{answered.Id}'] .piece-title").TextContent);
    }

    // 41. A request in Asked for that isn't being asked just now can be dismissed, which takes it off for good.
    [Fact]
    public async Task ARequestThatIsntBeingAskedJustNowCanBeDismissedForGood()
    {
        await using var host = new ComponentHost();
        var service = host.Context.Services.GetRequiredService<RoutineService>();
        await service.RecentAsync();
        var failed = RoutineStoreTests.ReadyJob() with { State = RoutineGenerationState.Failed, Draft = null, Prepared = null, Problem = "Claude couldn't be asked" };
        var asking = RoutineStoreTests.ReadyJob() with { State = RoutineGenerationState.Running, Draft = null, Prepared = null };
        var waiting = RoutineStoreTests.ReadyJob() with { State = RoutineGenerationState.Queued, Draft = null, Prepared = null };
        foreach (var job in new[] { failed, asking, waiting }) await host.Store.CreateRoutineJobAsync(job);
        var view = host.Context.Render<RoutineLibrary>();
        view.WaitForElement($"[data-routine-job='{failed.Id}']");

        // Only the one that isn't being asked can go, and it asks first.
        var dismiss = Assert.Single(view.FindAll("[data-action='routine-dismiss']"));
        Assert.Equal(failed.Id, dismiss.Closest(".routine-request")!.QuerySelector("[data-routine-job]")!.GetAttribute("data-routine-job"));
        await view.InvokeAsync(() => view.Find("[data-action='routine-dismiss']").ClickAsync(new MouseEventArgs()));
        Assert.Contains(failed.Id, view.FindAll("[data-routine-job]").Select(r => r.GetAttribute("data-routine-job")));
        await view.InvokeAsync(() => view.Find("[data-action='routine-dismiss-confirm']").ClickAsync(new MouseEventArgs()));

        view.WaitForAssertion(() => Assert.Equal(new[] { asking.Id, waiting.Id }.Order(StringComparer.Ordinal),
            view.FindAll("[data-routine-job]").Select(r => r.GetAttribute("data-routine-job")!).Order(StringComparer.Ordinal)));
        Assert.Null(await host.Store.GetRoutineJobAsync(failed.Id));
        Assert.DoesNotContain(await service.RecentAsync(), j => j.Id == failed.Id);
        // Being asked, it can't be: what Claude sends back would have nowhere to go.
        Assert.False(await service.DismissAsync(asking.Id));
        Assert.False(await service.DismissAsync(waiting.Id));
        Assert.NotNull(await host.Store.GetRoutineJobAsync(asking.Id));
    }
}
