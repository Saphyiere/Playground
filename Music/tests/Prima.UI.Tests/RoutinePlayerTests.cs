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

        public async Task<RoutineExecution> AddRoutineAsync(RoutineItem item)
        {
            var service = Context.Services.GetRequiredService<RoutineService>();
            await service.RecentAsync();
            var request = new RoutineRequest(5, "Practice");
            var draft = new RoutineDraft("Practice", "Focused practice", [item]);
            var job = new RoutineGenerationJob(Guid.NewGuid().ToString("N"), 0, RoutineGenerationState.Ready, DateTimeOffset.UtcNow, request,
                new PracticeEvidenceSnapshot(DateTimeOffset.UtcNow, []), null, "test-provider", "test", draft, RoutineCompiler.Prepare(draft, request));
            await Store.CreateRoutineJobAsync(job);
            await service.AcceptAsync(job.Id);
            var day = await service.TodayAsync();
            return await service.BeginExecutionAsync(day!.Id, item.Id);
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
