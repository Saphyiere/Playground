using System.Net;
using Microsoft.Extensions.DependencyInjection;

namespace Playground.AI.Assist.Tests;

/// <summary>Each ask keeps the app going from its request to its answer, and lets go however it ends.</summary>
public class KeepAliveTests
{
    const string Key = "sk-ant-api03-test-key-0123456789abcdef";
    const string Good = """{"status":"answered","summary":"Done.","notes":[],"answer":{"lifts":[{"exerciseId":12,"weight":105,"reps":10,"change":"up","why":"Measured history"}]}}""";
    const string Stranger = """{"status":"answered","summary":"Done.","notes":[],"answer":{"lifts":[{"exerciseId":99,"weight":105,"reps":10,"change":"up","why":"Unowned exercise"}]}}""";
    static readonly History Owners = new([12]);

    readonly FakeApi api = new();
    readonly MemoryAssistLog log = new();
    readonly CountingKeepAlive alive = new();
    readonly List<int> heldWhileSending = [];

    public KeepAliveTests() => api.Sending = () => heldWhileSending.Add(alive.Held);

    Claude Client(string? key = Key) => new(new MemoryKeys(key), log, TimeProvider.System, api, maxRetries: 0, keepAlive: alive);

    [Fact]
    public async Task AnAskHoldsTheAppGoingUntilItsAnswerIsBack()
    {
        api.Reply(Good);

        var answer = await Client().AskAsync(new NextLifts(), Owners, TestContext.Current.CancellationToken);

        Assert.True(answer.Ok);
        Assert.Equal([1], heldWhileSending);
        Assert.Equal(0, alive.Held);
        Assert.Equal(["Asking Claude"], alive.Doings);
    }

    [Fact]
    public async Task TheNotificationSaysWhatTheTaskIsDoing()
    {
        api.Reply(Good);

        await Client().AskAsync(new Lookback(), Owners, TestContext.Current.CancellationToken);

        Assert.Equal(["Looking back at your workout"], alive.Doings);
    }

    [Fact]
    public async Task AskingAgainForFailedChecksIsOneHoldThroughout()
    {
        api.Reply(Stranger);
        api.Reply(Good);

        var answer = await Client().AskAsync(new NextLifts(), Owners, TestContext.Current.CancellationToken);

        Assert.True(answer.Ok);
        Assert.Equal([1, 1], heldWhileSending);
        Assert.Single(alive.Doings);
        Assert.Equal(0, alive.Held);
    }

    [Fact]
    public async Task AFailedAskLetsGo()
    {
        api.Error(HttpStatusCode.Unauthorized, "authentication_error", "invalid x-api-key");

        var answer = await Client().AskAsync(new NextLifts(), Owners, TestContext.Current.CancellationToken);

        Assert.Equal(AnswerKind.KeyRejected, answer.Kind);
        Assert.Equal([1], heldWhileSending);
        Assert.Equal(0, alive.Held);
    }

    [Fact]
    public async Task ACancelledAskLetsGo()
    {
        api.Reply(Good);
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Client().AskAsync(new NextLifts(), Owners, cancelled.Token));
        Assert.Equal(0, alive.Held);
    }

    [Fact]
    public async Task WithoutAKeyNothingIsHeld()
    {
        var answer = await Client(key: null).AskAsync(new NextLifts(), Owners, TestContext.Current.CancellationToken);

        Assert.Equal(AnswerKind.NoKey, answer.Kind);
        Assert.Empty(alive.Doings);
        Assert.Empty(api.Requests);
    }

    [Fact]
    public void ByDefaultNothingIsKeptGoing()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IAssistKeyStore>(new MemoryKeys(Key));
        services.AddAiAssist();

        Assert.Same(NoKeepAlive.Instance, services.BuildServiceProvider().GetRequiredService<IAssistKeepAlive>());
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void AHostsKeepAliveIsTheOneUsedWhetherItsAddedBeforeOrAfter(bool before)
    {
        var services = new ServiceCollection();
        services.AddSingleton<IAssistKeyStore>(new MemoryKeys(Key));
        if (before) services.AddSingleton<IAssistKeepAlive>(alive);
        services.AddAiAssist();
        if (!before) services.AddSingleton<IAssistKeepAlive>(alive);

        var provider = services.BuildServiceProvider();

        Assert.Same(alive, provider.GetRequiredService<IAssistKeepAlive>());
        Assert.IsType<Claude>(provider.GetRequiredService<IClaude>());
    }

    /// <summary>NextLifts, saying what it does the way the workout app's look back does.</summary>
    sealed class Lookback : AssistTask<History, Plan>
    {
        readonly NextLifts lifts = new();

        public override string Name => lifts.Name;

        public override int Version => lifts.Version;

        public override string Doing => "Looking back at your workout";

        public override string Instructions => lifts.Instructions;

        public override IReadOnlyList<string> Check(History input, Plan answer) => lifts.Check(input, answer);
    }

    /// <summary>Counts the holds open now, and keeps what each said.</summary>
    sealed class CountingKeepAlive : IAssistKeepAlive
    {
        readonly Lock gate = new();
        int held;

        public int Held { get { lock (gate) return held; } }

        public List<string> Doings { get; } = [];

        public IDisposable Hold(string doing)
        {
            lock (gate)
            {
                held++;
                Doings.Add(doing);
            }

            return new Release(this);
        }

        sealed class Release(CountingKeepAlive owner) : IDisposable
        {
            bool done;

            public void Dispose()
            {
                if (done) return;
                done = true;
                lock (owner.gate) owner.held--;
            }
        }
    }
}
