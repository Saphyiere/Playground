using Bunit;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.JSInterop;
using Microsoft.AspNetCore.Components;
using Playground.AI.Assist;
using Prima.Core.Generation;
using Prima.Core.Keyboard;
using Prima.Core.Music;
using Prima.Core.Routines;
using Prima.Core.Timing;
using Prima.UI.Components;
using Prima.UI.Host;
using Prima.UI.Pages;
using Prima.UI.Practice;
using Prima.UI.Routines;
using Prima.UI.Storage;

namespace Prima.UI.Tests;

public class RoutinePlayerTests
{
    [Fact]
    public async Task RoutineExerciseUsesRequestedSettingsAndLinksItsAttemptWithoutChangingNormalDefaults()
    {
        await using var host = new PlayerHost();
        var service = host.Context.Services.GetRequiredService<RoutineService>();
        var original = new ExerciseChoice(0, false, HandUse.Left, 100);
        await host.Store.SaveTrackAsync("exercise:octave-jump", original);
        var item = new RoutineItem { Id = "jump", TargetId = "octave-jump", Title = "Landing", Purpose = "Prepare the landing", Fifths = 2, Hands = HandUse.Right, Tempo = 50, Mode = "time" };
        var execution = await host.AddRoutineAsync(item);
        host.Context.Services.GetRequiredService<NavigationManager>().NavigateTo($"exercises/{item.TargetId}?execution={execution.Id}");
        var view = host.Context.Render<ExercisePractice>(p => p.Add(c => c.Id, item.TargetId));
        view.WaitForAssertion(() => Assert.Equal(50, view.FindComponent<PlayAlong>().Instance.Tempo));
        var player = view.FindComponent<PlayAlong>().Instance;
        Assert.Equal(2, player.Music!.Score.Key.Fifths);
        Assert.All(player.Music.Notes, note => Assert.Equal(1, note.Staff));
        Assert.Equal(PlayMode.InTime, player.Ask);
        await view.InvokeAsync(() => player.TempoChanged.InvokeAsync(55));
        Assert.Equal(original, await host.Store.GetTrackAsync<ExerciseChoice>("exercise:octave-jump"));

        await view.InvokeAsync(() => player.Start());
        var run = host.Context.Services.GetRequiredService<SessionRegistry>().Current!;
        var first = player.Music.Notes[0];
        var at = run.TimeAt(first.Tick / (double)player.Music.BeatTicks);
        host.Clock.NowNs = at;
        host.Keyboard.Emit(new MidiEvent(MidiKind.NoteOn, 1, first.Midi, 80, at));
        host.Clock.NowNs = at + 100_000_000;
        host.Keyboard.Emit(new MidiEvent(MidiKind.NoteOff, 1, first.Midi, 0, host.Clock.NowNs));
        await view.InvokeAsync(player.StopAndSaveAsync);
        await view.InvokeAsync(player.StopAndSaveAsync);

        var attempts = await host.Store.GetAttemptsAsync(execution.Day, "exercise");
        var attempt = Assert.Single(attempts);
        Assert.Equal(item.TargetId, attempt.SetId);
        Assert.Equal(execution.Day, attempt.Day);
        Assert.Equal(55, attempt.Tempo);
        Assert.Equal(original, await host.Store.GetTrackAsync<ExerciseChoice>("exercise:octave-jump"));
        Assert.True((await service.GetExecutionAsync(execution.Id))!.ActiveSeconds >= 0);
        Assert.Equal(0, (await service.GetExecutionAsync(execution.Id))!.CompletedPasses);
    }

    [Fact]
    public async Task RhythmScoringAcceptsDifferentPitchesAtTheExpectedOnsets()
    {
        await using var host = new PlayerHost();
        var score = new Score(new Prima.Core.Music.Key(0), new Meter(4, 4), [new Bar(1, [new Voice(1,
            Enumerable.Range(0, 3).Select(i => (Prima.Core.Music.Event)new Notes($"n{i}", i * Ticks.Quarter, Ticks.Quarter, [new Tone($"t{i}", new Spelled(Step.C, 0, 4))])).ToArray())])]);
        var music = new PlayMusic(score, score.Expected());
        var sheet = host.Context.Render<Sheet>(p => p.Add(c => c.Mei, MeiWriter.Write(score)));
        var completion = new TaskCompletionSource<PlayOutcome>(TaskCreationOptions.RunContinuationsAsynchronously);
        var view = host.Context.Render<PlayAlong>(p => p.Add(c => c.Name, "rhythm-test").Add(c => c.Music, music).Add(c => c.SheetOf, () => sheet.Instance)
            .Add(c => c.Tempo, 60).Add(c => c.Ask, PlayMode.InTime).Add(c => c.RhythmOnly, true).Add(c => c.OnFinished, result => completion.TrySetResult(result)));
        await view.InvokeAsync(() => view.Instance.Start());
        var run = host.Context.Services.GetRequiredService<SessionRegistry>().Current!;
        foreach (var (note, i) in music.Notes.Select((n, i) => (n, i)))
        {
            var at = run.TimeAt(note.Tick / (double)music.BeatTicks);
            host.Clock.NowNs = at;
            host.Keyboard.Emit(new MidiEvent(MidiKind.NoteOn, 1, 70 + i, 80, at));
            host.Clock.NowNs = at + 100_000_000;
            host.Keyboard.Emit(new MidiEvent(MidiKind.NoteOff, 1, 70 + i, 0, host.Clock.NowNs));
        }
        run.Perform([]);
        var outcome = await completion.Task.WaitAsync(TimeSpan.FromSeconds(5), Xunit.TestContext.Current.CancellationToken);
        Assert.NotNull(outcome.InTime);
        Assert.Equal(3, outcome.InTime.Right);
        Assert.Equal(0, outcome.InTime.Wrong);
        Assert.Equal(1, outcome.InTime.InTimeAccuracy);
        Assert.Equal([70, 71, 72], outcome.Played.Select(n => n.Midi));
    }

    [Fact]
    public async Task StopAndSaveWaitsForTheAsyncOutcomeHandlerBeforeCompleting()
    {
        await using var host = new PlayerHost();
        var score = new Score(new Prima.Core.Music.Key(0), new Meter(4, 4), [new Bar(1, [new Voice(1,
            [new Notes("n", 0, Ticks.Whole, [new Tone("t", new Spelled(Step.C, 0, 4))])])])]);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var sheet = host.Context.Render<Sheet>(p => p.Add(c => c.Mei, MeiWriter.Write(score)));
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var saved = false;
        var view = host.Context.Render<PlayAlong>(p => p.Add(c => c.Name, "save-test").Add(c => c.Music, new PlayMusic(score, score.Expected()))
            .Add(c => c.SheetOf, () => sheet.Instance).Add(c => c.Tempo, 60).Add(c => c.Ask, PlayMode.InTime).Add(c => c.OnFinished, async _ =>
            {
                entered.TrySetResult();
                await release.Task;
                saved = true;
            }));
        await view.InvokeAsync(() => view.Instance.Start());
        var run = host.Context.Services.GetRequiredService<SessionRegistry>().Current!;
        var at = run.TimeAt(0);
        host.Clock.NowNs = at;
        host.Keyboard.Emit(new MidiEvent(MidiKind.NoteOn, 1, 60, 80, at));
        host.Clock.NowNs += 100_000_000;
        var stopping = view.InvokeAsync(view.Instance.StopAndSaveAsync);
        try
        {
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(5), Xunit.TestContext.Current.CancellationToken);
            Assert.False(stopping.IsCompleted);
            Assert.False(saved);
        }
        finally { release.TrySetResult(); }
        await stopping.WaitAsync(TimeSpan.FromSeconds(5), Xunit.TestContext.Current.CancellationToken);
        Assert.True(saved);
    }

    [Fact]
    public async Task ARunMovedToAnotherBarKeepsTheBarPlaying()
    {
        await using var host = new PlayerHost();
        var score = new Score(new Prima.Core.Music.Key(0), new Meter(4, 4), [.. Enumerable.Range(1, 3).Select(n => new Bar(n, [new Voice(1,
            [new Notes($"n{n}", 0, Ticks.Whole, [new Tone($"t{n}", new Spelled(Step.C, 0, 4))])])]))]);
        var music = new PlayMusic(score, score.Expected());
        var sheet = host.Context.Render<Sheet>(p => p.Add(c => c.Mei, MeiWriter.Write(score)));
        // The screen round the bar draws again as a run starts, as the piece page does, so the bar draws with it: a moved run's must
        // stay as it plays, never showing Start in between.
        IRenderedComponent<PlayAlong>? view = null;
        var startShown = 0;
        view = host.Context.Render<PlayAlong>(p => p.Add(c => c.Name, "move-test").Add(c => c.Music, music).Add(c => c.SheetOf, () => sheet.Instance)
            .Add(c => c.Tempo, 60).Add(c => c.Ask, PlayMode.WaitForMe).Add(c => c.OnStarting, () =>
            {
                view!.Render(p => p.Add(c => c.Tempo, 60));
                startShown += view!.FindAll("[data-piano='go']").Count;
            }));
        view.WaitForAssertion(() => Assert.Equal(PlayMode.WaitForMe, view.Instance.Mode));
        await view.InvokeAsync(() => view.Instance.Start());
        Assert.True(view.Instance.Running);
        startShown = 0;
        await view.InvokeAsync(() => view.Instance.Restart(music));
        Assert.True(view.Instance.Running);
        Assert.Equal(0, startShown);
    }

    [Fact]
    public async Task GoingRoundARunPlayedToItsEndStartsAgainUntilStop()
    {
        await using var host = new PlayerHost();
        var score = new Score(new Prima.Core.Music.Key(0), new Meter(4, 4), [new Bar(1, [new Voice(1, [new Notes("n1", 0, Ticks.Half, [new Tone("t1", new Spelled(Step.C, 0, 4))]),
            new Notes("n2", Ticks.Half, Ticks.Half, [new Tone("t2", new Spelled(Step.D, 0, 4))])])])]);
        var sheet = host.Context.Render<Sheet>(p => p.Add(c => c.Mei, MeiWriter.Write(score)));
        var finished = new List<PlayOutcome>();
        var view = host.Context.Render<PlayAlong>(p => p.Add(c => c.Name, "again-test").Add(c => c.Music, new PlayMusic(score, score.Expected()))
            .Add(c => c.SheetOf, () => sheet.Instance).Add(c => c.Tempo, 60).Add(c => c.Ask, PlayMode.WaitForMe).Add(c => c.Again, true)
            .Add(c => c.OnFinished, how => finished.Add(how)));
        view.WaitForAssertion(() => Assert.Equal(PlayMode.WaitForMe, view.Instance.Mode));
        await view.InvokeAsync(() => view.Instance.Start());
        foreach (var midi in new[] { 60, 62 })
        {
            host.Clock.NowNs += 300_000_000;
            host.Keyboard.Emit(new MidiEvent(MidiKind.NoteOn, 1, midi, 80, host.Clock.NowNs));
        }
        // Played to its end: kept, and going again, with how it went in the bar.
        view.WaitForAssertion(() => Assert.Single(finished));
        Assert.False(finished[0].Stopped);
        view.WaitForAssertion(() => Assert.Contains("Last run", view.Find(".play-bar").TextContent));
        Assert.True(view.Instance.Running);
        // Stop is the way out.
        await view.InvokeAsync(view.Instance.Stop);
        view.WaitForAssertion(() => Assert.Equal(2, finished.Count));
        Assert.True(finished[1].Stopped);
        Assert.False(view.Instance.Running);
    }

    [Fact]
    public async Task TheMetronomeClicksAtTheTempoByTheMetreUntilARunStarts()
    {
        await using var host = new PlayerHost();
        var score = new Score(new Prima.Core.Music.Key(0), new Meter(3, 4), [new Bar(1, [new Voice(1, [new Notes("n1", 0, Ticks.Quarter * 3, [new Tone("t1", new Spelled(Step.C, 0, 4))])])])]);
        var sheet = host.Context.Render<Sheet>(p => p.Add(c => c.Mei, MeiWriter.Write(score)));
        var click = host.Context.Services.GetRequiredService<Click>();
        double? asked = null;
        var view = host.Context.Render<PlayAlong>(p => p.Add(c => c.Name, "metronome-test").Add(c => c.Music, new PlayMusic(score, score.Expected()))
            .Add(c => c.SheetOf, () => sheet.Instance).Add(c => c.Tempo, 90).Add(c => c.Ask, PlayMode.WaitForMe).Add(c => c.Metronome, true)
            .Add(c => c.OriginalTempo, 100).Add(c => c.TempoChanged, bpm => asked = bpm));
        view.WaitForAssertion(() => Assert.Equal(PlayMode.WaitForMe, view.Instance.Mode));
        // The tempo shows in Wait for me too, with the music's own beside it once it's been changed: a tap asks to go back to it.
        Assert.NotNull(view.Find(".play-tempo-figure"));
        await view.Find(".play-tempo-original").ClickAsync(new Microsoft.AspNetCore.Components.Web.MouseEventArgs());
        Assert.Equal(100, asked);
        // Its key: a click at the tempo, three a bar as the music's metre, nothing counted in.
        await view.Find(".play-metronome").ClickAsync(new Microsoft.AspNetCore.Components.Web.MouseEventArgs());
        Assert.True(click.Running);
        Assert.Equal((90.0, 3, 0), (click.Plan!.Bpm, click.Plan.BeatsPerBar, click.Plan.CountInBars));
        // A new tempo starts it again at that.
        view.Render(p => p.Add(c => c.Tempo, 100));
        Assert.Equal(100, click.Plan!.Bpm);
        // A run has the click to itself: the metronome stops, and stays stopped after it.
        await view.InvokeAsync(() => view.Instance.Start());
        Assert.False(click.Running);
        await view.InvokeAsync(view.Instance.Stop);
        Assert.Equal("false", view.Find(".play-metronome").GetAttribute("aria-pressed"));
    }

    [Fact]
    public async Task ListeningDoesNotStartPracticeAccounting()
    {
        await using var host = new PlayerHost();
        var score = new Score(new Prima.Core.Music.Key(0), new Meter(4, 4), [new Bar(1, [new Voice(1,
            [new Notes("n", 0, Ticks.Whole, [new Tone("t", new Spelled(Step.C, 0, 4))])])])]);
        var sheet = host.Context.Render<Sheet>(p => p.Add(c => c.Mei, MeiWriter.Write(score)));
        var starts = 0;
        var practiceStarts = 0;
        var view = host.Context.Render<PlayAlong>(p => p.Add(c => c.Name, "listen-test").Add(c => c.Music, new PlayMusic(score, score.Expected()))
            .Add(c => c.SheetOf, () => sheet.Instance).Add(c => c.Tempo, 60).Add(c => c.Ask, PlayMode.InTime)
            .Add(c => c.OnStarting, () => starts++).Add(c => c.OnPracticeStarting, () => practiceStarts++));
        await view.InvokeAsync(() => view.Instance.Listen());
        Assert.Equal(1, starts);
        Assert.Equal(0, practiceStarts);
        await view.InvokeAsync(view.Instance.StopAndSaveAsync);
        await view.InvokeAsync(() => view.Instance.Start());
        Assert.Equal(2, starts);
        Assert.Equal(1, practiceStarts);
        await view.InvokeAsync(view.Instance.StopAndSaveAsync);
    }

    [Fact]
    public async Task SuspendedQuizResumesTheSameItemAndAcceptsItsAnswer()
    {
        await using var host = new PlayerHost();
        var item = new RoutineItem { Id = "geography", Kind = "drill", TargetId = "geography", Title = "Find keys", Purpose = "Practise locations", Hands = HandUse.Together };
        var execution = await host.AddRoutineAsync(item);
        var registry = host.Context.Services.GetRequiredService<SessionRegistry>();
        host.Context.Services.GetRequiredService<NavigationManager>().NavigateTo($"drills/{item.TargetId}?execution={execution.Id}");
        var view = host.Context.Render<ItemDrill>(p => p.Add(c => c.Key, item.TargetId));
        view.WaitForAssertion(() => Assert.NotNull(registry.DrillAnswer));
        var sheet = view.FindComponent<Sheet>().Instance;
        await view.InvokeAsync(() => sheet.OnPainted.InvokeAsync(host.Clock.NowNs));
        var original = sheet.Mei;
        await host.Context.Services.GetRequiredService<PracticeLifecycle>().PauseAsync();
        Assert.Null(registry.DrillAnswer);
        var bar = view.FindComponent<RoutineBar>();
        await bar.Find(".mu-primary[data-piano='off']").ClickAsync(new Microsoft.AspNetCore.Components.Web.MouseEventArgs());
        Assert.NotNull(registry.DrillAnswer);
        Assert.Equal(original, view.FindComponent<Sheet>().Instance.Mei);
        var answer = registry.DrillAnswer(true)[0][0];
        host.Clock.NowNs += 100_000_000;
        host.Keyboard.Emit(new MidiEvent(MidiKind.NoteOn, 1, answer, 80, host.Clock.NowNs));
        view.WaitForElement(".drill-feedback.right");
    }

    [Fact]
    public async Task AQuizItemTheSameAsTheOneBeforeIsListenedForAndMovesOn()
    {
        await using var host = new PlayerHost();
        var registry = host.Context.Services.GetRequiredService<SessionRegistry>();
        var view = host.Context.Render<ItemDrill>(p => p.Add(c => c.Key, "geography"));
        view.WaitForAssertion(() => Assert.NotNull(registry.DrillAnswer));
        // Keyboard geography's first step asks for one of five keys, so most sets ask for a key twice running. Each item is played
        // right, and the sheet says it's painted only when its music changes, as in the browser: the same music isn't drawn again.
        var (anotherSet, itemReadout, painted) = ("[data-piano='go']", ".reading-bar .mu-readout-value", "");
        for (var played = 0; played < 60; played++)
        {
            if (view.FindAll(anotherSet).Count > 0)
            {
                await view.Find(anotherSet).ClickAsync(new Microsoft.AspNetCore.Components.Web.MouseEventArgs());
                painted = "";
            }
            var sheet = view.FindComponent<Sheet>().Instance;
            var again = sheet.Mei == painted;
            if (!again) await view.InvokeAsync(() => sheet.OnPainted.InvokeAsync(host.Clock.NowNs));
            painted = sheet.Mei;
            var item = view.Find(itemReadout).TextContent;
            host.Clock.NowNs += 100_000_000;
            host.Keyboard.Emit(new MidiEvent(MidiKind.NoteOn, 1, registry.DrillAnswer!(true)[0][0], 80, host.Clock.NowNs));
            var stuck = $"Item {item}, played right, didn't move on{(again ? ": the same key as the item before" : "")}.";
            view.WaitForAssertion(() => Assert.True(view.FindAll(anotherSet).Count > 0 || view.Find(itemReadout).TextContent != item, stuck), TimeSpan.FromSeconds(5));
            if (again) return;
        }
        Assert.Fail("No key came twice running in 60 items.");
    }

    [Fact]
    public async Task HowAnActivityFeltIsSavedAndItsKeyLightsUp()
    {
        await using var host = new PlayerHost();
        var service = host.Context.Services.GetRequiredService<RoutineService>();
        var item = new RoutineItem { Id = "geography", Kind = "drill", TargetId = "geography", Title = "Find keys", Purpose = "Practise locations", Hands = HandUse.Together };
        var execution = await host.AddRoutineAsync(item);
        // A minute played on it already, so its bar asks how it felt.
        await service.CheckpointAsync(execution.Id, 60);
        host.Context.Services.GetRequiredService<NavigationManager>().NavigateTo($"drills/{item.TargetId}?execution={execution.Id}");
        var view = host.Context.Render<ItemDrill>(p => p.Add(c => c.Key, item.TargetId));
        await view.WaitForElement("[data-feedback='hard']").ClickAsync(new Microsoft.AspNetCore.Components.Web.MouseEventArgs());
        view.WaitForAssertion(() => Assert.Equal("true", view.Find("[data-feedback='hard']").GetAttribute("aria-pressed")));
        Assert.Equal("hard", (await service.GetExecutionAsync(execution.Id))!.Feedback);
        // A toggle shows it's on by its lamp, lit in Studio and a dot in Vinyl: without one, Studio shows no change at all.
        Assert.All(view.FindAll("[data-feedback]"), key => Assert.Contains(key.Children, c => c.ClassList.Contains("mu-led")));
    }

    [Fact]
    public async Task AnActivitysClockCountsWhileItsOpenPlayedOrNotAndStopsWhileAway()
    {
        await using var host = new PlayerHost();
        var service = host.Context.Services.GetRequiredService<RoutineService>();
        var item = new RoutineItem { Id = "geography", Kind = "drill", TargetId = "geography", Title = "Find keys", Purpose = "Practise locations", Hands = HandUse.Together };
        var execution = await host.AddRoutineAsync(item);
        var session = new RoutineSession(service);
        Assert.True(await session.LoadAsync(execution.Id, "drill", "geography"));
        var token = Xunit.TestContext.Current.CancellationToken;
        // Open, with nothing played: it counts.
        await Task.Delay(250, token);
        Assert.True(session.Seconds >= 0.2);
        // A run kept: the clock counts on through it, and after it with the music stopped.
        session.Start();
        await session.RecordAsync(new AttemptRecord(0, DateTimeOffset.Now, execution.Day, "geography", null, null, 1, null, null, null, "[]", null, "{}", 1, true, null), 1);
        var kept = session.Seconds;
        await Task.Delay(250, token);
        Assert.True(session.Seconds >= kept + 0.2);
        // Away (left, or Prima put away): it stops, its time kept; back again, it counts on from there.
        await session.PauseAsync();
        var away = session.Seconds;
        Assert.True((await service.GetExecutionAsync(execution.Id))!.ActiveSeconds >= away - 0.01);
        await Task.Delay(250, token);
        Assert.Equal(away, session.Seconds, 3);
        session.Resume();
        await Task.Delay(250, token);
        Assert.True(session.Seconds >= away + 0.2);
    }

    [Fact]
    public async Task NextOpensTheNextActivityAndThisOneIsDone()
    {
        await using var host = new PlayerHost();
        var service = host.Context.Services.GetRequiredService<RoutineService>();
        var first = new RoutineItem { Id = "geography", Kind = "drill", TargetId = "geography", Title = "Find keys", Purpose = "Practise locations", Hands = HandUse.Together };
        var second = new RoutineItem { Id = "ledger", Kind = "drill", TargetId = "ledger", Title = "Ledger lines", Purpose = "Read high and low", Hands = HandUse.Together };
        var execution = await host.AddRoutineAsync(first, second);
        var nav = host.Context.Services.GetRequiredService<NavigationManager>();
        nav.NavigateTo($"drills/{first.TargetId}?execution={execution.Id}");
        var view = host.Context.Render<ItemDrill>(p => p.Add(c => c.Key, first.TargetId));
        view.WaitForAssertion(() => Assert.Equal("Next: Ledger lines", view.Find("[data-action='routine-next']").TextContent));
        // Its two minutes, counting down.
        Assert.Matches(@"^[12]:\d\d left$", view.Find(".routine-time").TextContent);
        await view.Find("[data-action='routine-next']").ClickAsync(new Microsoft.AspNetCore.Components.Web.MouseEventArgs());
        // Saving and opening the next take a moment, and moving on draws nothing here to wait for.
        for (var waited = 0; waited < 50 && !nav.Uri.Contains("drills/ledger?execution="); waited++) await Task.Delay(100, Xunit.TestContext.Current.CancellationToken);
        Assert.Contains("drills/ledger?execution=", nav.Uri);
        Assert.Equal(RoutineItemState.Done, (await service.GetExecutionAsync(execution.Id))!.State);
    }

    [Fact]
    public async Task PastItsMinutesTheActivitysTimeIsUpAndNextStandsOut()
    {
        await using var host = new PlayerHost();
        var service = host.Context.Services.GetRequiredService<RoutineService>();
        var item = new RoutineItem { Id = "geography", Kind = "drill", TargetId = "geography", Title = "Find keys", Purpose = "Practise locations", Hands = HandUse.Together };
        var execution = await host.AddRoutineAsync(item);
        // Two minutes and more on it already.
        await service.CheckpointAsync(execution.Id, 125);
        host.Context.Services.GetRequiredService<NavigationManager>().NavigateTo($"drills/{item.TargetId}?execution={execution.Id}");
        var view = host.Context.Render<ItemDrill>(p => p.Add(c => c.Key, item.TargetId));
        view.WaitForAssertion(() => Assert.Equal("time's up", view.Find(".routine-time").TextContent));
        Assert.Contains("mu-primary", view.Find("[data-action='routine-next']").ClassList);
    }

    private sealed class PlayerHost : IAsyncDisposable
    {
        private readonly string database = Path.Combine(Path.GetTempPath(), $"prima-routine-player-{Guid.NewGuid():N}.db");
        public BunitContext Context { get; } = new();
        public ManualClock Clock { get; } = new();
        public TestKeyboard Keyboard { get; } = new();
        public PrimaStore Store { get; }

        public PlayerHost()
        {
            Context.Services.AddSingleton<IJSRuntime>(new BrowserCalls());
            Context.Services.AddSingleton<IClock>(Clock);
            Context.Services.AddSingleton<IKeyboard>(Keyboard);
            Context.Services.AddSingleton<IClaude>(new FakeClaude());
            Context.Services.AddPrima(database);
            Store = Context.Services.GetRequiredService<PrimaStore>();
        }

        // A routine of these activities, accepted, with the first begun today.
        public async Task<RoutineExecution> AddRoutineAsync(params RoutineItem[] items)
        {
            var service = Context.Services.GetRequiredService<RoutineService>();
            await service.RecentAsync();
            var request = new RoutineRequest(5, "Practice");
            var draft = new RoutineDraft("Practice", "Focused practice", items);
            var job = new RoutineGenerationJob(Guid.NewGuid().ToString("N"), 0, RoutineGenerationState.Ready, DateTimeOffset.UtcNow, request,
                new PracticeEvidenceSnapshot(DateTimeOffset.UtcNow, []), null, "test-provider", "test", draft, RoutineCompiler.Prepare(draft, request));
            await Store.CreateRoutineJobAsync(job);
            await service.AcceptAsync(job.Id);
            var day = await service.TodayAsync();
            return await service.BeginExecutionAsync(day!.Id, items[0].Id);
        }

        public async ValueTask DisposeAsync()
        {
            await Context.DisposeAsync();
            await Store.GetSettingsAsync();
            using var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = database, ForeignKeys = true }.ToString());
            SqliteConnection.ClearPool(connection);
            File.Delete(database);
        }
    }

    private sealed class ManualClock : IClock { public long NowNs { get; set; } }
    private sealed class TestKeyboard : IKeyboard
    {
        public string Name => "Test keyboard";
        public bool CanSend => false;
        public SensingJitter Sensing { get; } = new();
        public event Action<MidiEvent>? Received;
        public event Action? Changed { add { } remove { } }
        public void Send(byte[] message, long atNs) { }
        public void Silence() { }
        public void Emit(MidiEvent note) => Received?.Invoke(note);
    }
}
