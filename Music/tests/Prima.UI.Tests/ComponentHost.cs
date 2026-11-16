using System.Text.Json;
using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.JSInterop;
using Playground.AI.Assist;
using Playground.Updates;
using Prima.Core.Keyboard;
using Prima.Core.Music;
using Prima.Core.Timing;
using Prima.Core.Transcription;
using Prima.UI.Host;
using Prima.UI.Storage;
using Prima.UI.Transcription;

namespace Prima.UI.Tests;

internal sealed class ComponentHost : IAsyncDisposable
{
    private readonly string database = Path.Combine(Path.GetTempPath(), $"prima-ui-{Guid.NewGuid():N}.db");
    public BunitContext Context { get; } = new();
    public BrowserCalls Browser { get; } = new();
    public SystemBars Bars { get; } = new();
    public FakeClaude Claude { get; } = new();
    public TestConversionRenderer Renderer { get; } = new();
    public TestConversionHost ConversionHost { get; } = new();
    public ConversionJobs Jobs { get; }
    public PrimaStore Store { get; }
    public string DatabasePath => database;
    /// <summary>What the components logged as problems, in a file of this test's own.</summary>
    public ProblemLog Problems { get; } = new(Path.Combine(Path.GetTempPath(), $"prima-ui-{Guid.NewGuid():N}.errors.log"));

    // bUnit waits a second by default for what a page draws, and a page that reads the store as it opens takes longer than that
    // now and then with every test running at once on a busy machine (a release's runner, say): such a test then failed saying
    // nothing about the page. Ten seconds for every wait; one that passes waits no longer than it did.
    static ComponentHost() => BunitContext.DefaultWaitTimeout = TimeSpan.FromSeconds(10);

    /// <param name="safeguard">What keeps the data before an update, in place of Prima's copies: a test's own, one that fails, say.</param>
    /// <param name="keyboard">The piano, in place of none.</param>
    /// <param name="clock">The clock, in place of a stopwatch: one a test sets, say.</param>
    public ComponentHost(ILibraryBundle? bundle = null, IJSRuntime? js = null, IUpdateSafeguard? safeguard = null, IKeyboard? keyboard = null, IClock? clock = null)
    {
        if (keyboard is not null) Context.Services.AddSingleton(keyboard);
        if (clock is not null) Context.Services.AddSingleton(clock);
        Context.Services.AddSingleton(Problems);
        Context.Services.AddSingleton<ILoggerFactory>(new KeptLogs(new ProblemLogProvider(Problems)));
        Context.Services.AddSingleton(typeof(ILogger<>), typeof(Logger<>));
        Context.Services.AddSingleton<IJSRuntime>(js ?? Browser);
        Context.Services.AddSingleton<ISystemBars>(Bars);
        Context.Services.AddSingleton<IClaude>(Claude);
        Context.Services.AddSingleton<IConversionRenderer>(Renderer);
        Context.Services.AddSingleton<IConversionHost>(ConversionHost);
        // Settings asks after updates: GitHub and the tablet stood in for, with no release yet.
        Context.Services.AddStandInUpdates(new UpdateOptions("prima", "prima.apk"), "Prima");
        if (bundle is not null) Context.Services.AddSingleton(bundle);
        Context.Services.AddPrima(database);
        // Registered after Prima's own, so it's the one the updater is given.
        if (safeguard is not null) Context.Services.AddSingleton(safeguard);
        Store = Context.Services.GetRequiredService<PrimaStore>();
        Jobs = Context.Services.GetRequiredService<ConversionJobs>();
        ConversionHost.Run = Jobs.RunAsync;
    }

    public async Task<PieceRecord> AddPiece(bool pdf = false, bool bundled = false)
    {
        var piece = new PieceRecord("test", bundled ? "bundle" : "import", "Test piece", "", null, pdf ? "pdf" : "mei", bundled ? "score.pdf" : null, null,
            1, 1, null, 1, null, null, null, null, null, null, 72, null, null, DateTimeOffset.Now, null);
        var score = new Score(new Prima.Core.Music.Key(0), new Meter(4, 4),
            [new Bar(1, [new Voice(1, [new Notes("n1", 0, 1920, [new Tone("t1", new Spelled(Step.C, 0, 4))])])])]);
        await Store.SavePieceAsync(piece, pdf ? null : MeiWriter.Write(score), []);
        await Context.Services.GetRequiredService<IAssistKeyStore>().SetAsync("test-key");
        return piece;
    }

    public async ValueTask DisposeAsync()
    {
        await Context.DisposeAsync();
        if (ConversionHost.Execution is { IsCompleted: false } execution)
        {
            await Jobs.CancelAsync(ConversionHost.Started[^1]);
            await execution;
        }
        // Let the page's queued position/layout writes finish before removing its test database.
        await Store.GetSettingsAsync();
        using var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = database, ForeignKeys = true }.ToString());
        SqliteConnection.ClearPool(connection);
        File.Delete(database);
        Problems.Clear();
    }
}

/// <summary>Loggers that keep warnings and errors in a problem log, as the hosts' do.</summary>
internal sealed class KeptLogs(ILoggerProvider provider) : ILoggerFactory
{
    public ILogger CreateLogger(string categoryName) => provider.CreateLogger(categoryName);

    public void AddProvider(ILoggerProvider other)
    {
    }

    public void Dispose()
    {
    }
}

/// <summary>A piano that isn't plugged in until it is (or is from the start), with a key played when a test says.</summary>
internal sealed class TestPiano(bool plugged = false) : IKeyboard
{
    public string? Name { get; private set; } = plugged ? "P-225B" : null;

    public bool CanSend => false;

    public SensingJitter Sensing { get; } = new();

    public event Action<MidiEvent>? Received;

    public event Action? Changed;

    public void Send(byte[] message, long atNs)
    {
    }

    public void Silence()
    {
    }

    public void Plug()
    {
        Name = "P-225B";
        Changed?.Invoke();
    }

    public void Play(int midi, long atNs) => Received?.Invoke(new MidiEvent(MidiKind.NoteOn, 1, midi, 80, atNs));
}

/// <summary>A clock that says the time a test sets.</summary>
internal sealed class SetClock : IClock
{
    public long NowNs { get; set; }
}

internal sealed class SystemBars : ISystemBars
{
    public List<bool> Shown { get; } = [];
    public void Show(bool shown) => Shown.Add(shown);
}

internal sealed class BrowserCalls : IJSRuntime, IJSObjectReference
{
    public List<bool> Fullscreen { get; } = [];
    public List<(ElementReference Element, string? Id)> Outlines { get; } = [];
    public List<ElementReference> ShownPdfs { get; } = [];
    /// <summary>The sections Settings brought into view, by id, and the first of them once it's asked for.</summary>
    public List<string> ShownSections { get; } = [];
    public TaskCompletionSource<string> SectionShown { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public List<JsonElement> BarChoices { get; } = [];
    public List<ElementReference> BarStrips { get; } = [];
    /// <summary>Held, the piece's listening to keys (its last step setting itself up) and full screen wait for it.</summary>
    public Task? ListenKeysReady { get; set; }
    public int ListenKeysCalls { get; private set; }
    public Task? FullScreenReady { get; set; }
    /// <summary>Held, every script module (turns.js, bars.js, the sheet's) is still loading.</summary>
    public Task? ImportsReady { get; set; }
    public Exception? CutError { get; set; }
    public int Cuts { get; private set; }
    public Prima.UI.Components.PdfPages.SystemCut[]? CutResult { get; set; }
    public double[] Profile { get; set; } = [];
    public List<int> ProfiledPages { get; } = [];
    public Task? ProfileReady { get; set; }
    public double[] PageRatios { get; set; } = [0.75];
    /// <summary>What sheets gave Verovio to engrave: the music, and the options with it.</summary>
    public List<(string Mei, JsonElement Options)> Renders { get; } = [];

    public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args) => InvokeAsync<TValue>(identifier, default, args);

    public async ValueTask<TValue> InvokeAsync<TValue>(string identifier, CancellationToken cancellationToken, object?[]? args)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (identifier == "listenKeys") ListenKeysCalls++;
        if (identifier == "listenKeys" && ListenKeysReady is not null) await ListenKeysReady;
        if (identifier == "import" && ImportsReady is not null) await ImportsReady;
        if (identifier is "import" or "listenKeys") return (TValue)(object)this;
        if (identifier == "fullScreen")
        {
            Fullscreen.Add((bool)args![0]!);
            // Held once: leaving full screen as the page goes doesn't wait for it.
            if (FullScreenReady is { } ready)
            {
                FullScreenReady = null;
                await ready;
            }
        }
        if (identifier == "outline") Outlines.Add(((ElementReference)args![0]!, (string?)args[1]));
        if (identifier == "render") lock (Renders) Renders.Add(((string)args![1]!, JsonSerializer.SerializeToElement(args[2])));
        if (identifier == "show" && args?.Length == 7) ShownPdfs.Add((ElementReference)args[0]!);
        if (identifier == "show" && args is [string section])
        {
            ShownSections.Add(section);
            SectionShown.TrySetResult(section);
        }
        if (identifier == "show" && args?.Length == 4)
        {
            BarStrips.Add((ElementReference)args[1]!);
            BarChoices.Add(JsonSerializer.SerializeToElement(args[3]));
        }
        if (identifier == "cut")
        {
            Cuts++;
            if (CutError is { } error) throw error;
            return (TValue)(object)(CutResult ?? [new([1, 2, 3], "blob:test", [0.1, 0.5, 0.9], new(0.08, 0.1, 0.84, 0.3))]);
        }
        if (identifier == "open") return JsonSerializer.Deserialize<TValue>(JsonSerializer.Serialize(new { Pages = PageRatios.Length, Ratios = PageRatios }))!;
        if (identifier == "thumbnail") return (TValue)(object)"blob:thumbnail";
        if (identifier == "profile")
        {
            ProfiledPages.Add((int)args![1]!);
            if (ProfileReady is not null) await ProfileReady;
            return (TValue)(object)Profile;
        }
        if (identifier == "pageWidth") return (TValue)(object)900.0;
        if (identifier == "size") return (TValue)(object)new double[] { 1000, 600 };
        return default!;
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}

internal sealed class FakeClaude : IClaude
{
    public Exception? Error { get; set; }
    public bool WaitForCancellation { get; set; }
    public bool WasCancelled { get; private set; }
    public AnswerKind Kind { get; set; } = AnswerKind.OutOfCredit;
    public int Calls { get; private set; }
    public TaskCompletionSource Asked { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public bool SuccessfulReading { get; set; }
    public int? RecheckOctave { get; set; }
    public int FullReadingOctave { get; set; } = 4;
    public bool CostKnown { get; set; } = true;
    public bool FinalTie { get; set; }
    public List<ChunkToRead> Requests { get; } = [];
    public string ProviderId => "test-provider";

    public async Task<Answer<TAnswer>> AskAsync<TInput, TAnswer>(AssistTask<TInput, TAnswer> task, TInput input, CancellationToken ct = default)
    {
        Calls++;
        Asked.TrySetResult();
        if (input is ChunkToRead sent) Requests.Add(sent);
        if (WaitForCancellation)
        {
            try { await Task.Delay(Timeout.Infinite, ct); }
            catch (OperationCanceledException)
            {
                WasCancelled = true;
                throw;
            }
        }
        if (Error is { } error) throw error;
        if (SuccessfulReading && input is ChunkToRead chunk)
        {
            var bars = chunk.OwnedSourceBars.Select((_, i) => new ReadBar(i + 1, 0, 4, 4, false, BarEnd.Normal, 0,
                new[] { 1, 2 }.Select(staff => new ReadStaff(staff, staff == 1 ? ReadClef.Treble : ReadClef.Bass,
                    [new ReadVoice(1, [new ReadEvent(ReadKind.Note, [new ReadPitch(Step.C, Accidental.None, (chunk.CurrentBar is not null ? RecheckOctave ?? FullReadingOctave : FullReadingOctave) + 1 - staff, FinalTie && staff == 1 && i == chunk.OwnedSourceBars.Count - 1, 0)],
                        NoteValue.Whole, 0, 0, false, false, Articulation.None, [], 0, ReadClef.None)])])).ToArray())).ToArray();
            return new Answer<TAnswer>(AnswerKind.Answered, (TAnswer)(object)new SystemReading(bars, []), "", [], Usage.None with { CostKnown = CostKnown });
        }
        return new Answer<TAnswer>(Kind, default, "Test API failure", [], new Usage(0, 0, 0, 0, 0, TimeSpan.Zero) { CostKnown = CostKnown });
    }

    public Task<Answer<string>> TestAsync(CancellationToken ct = default) => throw new NotSupportedException();
}

internal sealed class TestConversionRenderer : IConversionRenderer
{
    public Exception? CropError { get; set; }
    public Task<string?> UnavailableReasonAsync(CancellationToken ct) => Task.FromResult<string?>(null);
    public Task<byte[]> CropAsync(byte[] pdf, int page, SourceRectangle bounds, int width, CancellationToken ct) =>
        CropError is { } error ? Task.FromException<byte[]>(error) : Task.FromResult<byte[]>([1, 2, 3]);
    public Task<byte[]> EngraveAsync(string mei, int widthPx, CancellationToken ct) => Task.FromResult<byte[]>([4, 5, 6]);
}

internal sealed class TestConversionHost : IConversionHost
{
    public Func<string, CancellationToken, Task> Run { get; set; } = null!;
    public List<string> Started { get; } = [];
    public Task? Execution { get; private set; }
    public Exception? StartError { get; set; }
    public Task StartAsync(string jobId)
    {
        if (StartError is { } error) return Task.FromException(error);
        Started.Add(jobId);
        Execution = Run(jobId, default);
        return Task.CompletedTask;
    }
}
