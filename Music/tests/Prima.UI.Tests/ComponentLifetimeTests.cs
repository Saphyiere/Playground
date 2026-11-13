using System.Text.Json;
using Bunit;
using Microsoft.JSInterop;
using Playground.AI.Assist;
using Prima.Core.Library;
using Prima.Core.Music;
using Prima.Core.Transcription;
using Prima.UI.Components;
using Prima.UI.Transcription;

namespace Prima.UI.Tests;

public class ComponentLifetimeTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task LateModuleImportIsDisposedWithoutRenderingOrOpeningTheOldComponent(bool pdf)
    {
        var browser = new LifetimeBrowser { HoldFirstImport = true };
        await using var host = new ComponentHost(js: browser);
        IAsyncDisposable component = pdf
            ? host.Context.Render<PdfPages>(p => p.Add(c => c.Data, [1])).Instance
            : host.Context.Render<Sheet>(p => p.Add(c => c.Mei, "test-mei")).Instance;
        await browser.ImportStarted.Task.WaitAsync(Xunit.TestContext.Current.CancellationToken);
        await component.DisposeAsync();
        browser.ImportReady.SetResult();
        await browser.First.Disposed.Task.WaitAsync(Xunit.TestContext.Current.CancellationToken);

        Assert.Empty(browser.First.Calls);
        Assert.Equal(1, browser.First.DisposeCount);
        if (pdf) host.Context.Render<PdfPages>(p => p.Add(c => c.Data, [1]));
        else host.Context.Render<Sheet>(p => p.Add(c => c.Mei, "new-mei"));
        await browser.Fresh.Started.Task.WaitAsync(Xunit.TestContext.Current.CancellationToken);
        Assert.Contains(pdf ? "open" : "render", browser.Fresh.Calls);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task DisposedPdfDoesNotPublishALateOpenResultOrCancellationError(bool fails)
    {
        var browser = new LifetimeBrowser();
        browser.First.OpenReady = new(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var host = new ComponentHost(js: browser);
        var opened = 0;
        var view = host.Context.Render<PdfPages>(p => p.Add(c => c.Data, [1]).Add(c => c.OnOpened, _ => opened++));
        await browser.First.Started.Task.WaitAsync(Xunit.TestContext.Current.CancellationToken);
        await view.Instance.DisposeAsync();
        if (fails) browser.First.OpenReady.SetException(new JSException("Loading was cancelled by close"));
        else browser.First.OpenReady.SetResult();
        await browser.First.OpenReturned.Task.WaitAsync(Xunit.TestContext.Current.CancellationToken);
        await view.InvokeAsync(() => Task.CompletedTask);

        Assert.Equal(0, opened);
        Assert.Equal(1, browser.First.DisposeCount);
        Assert.Contains("close", browser.First.Calls);
        await view.Instance.Release(["old-url"]);
        Assert.DoesNotContain("release", browser.First.Calls);
    }

    [Fact]
    public async Task ClosingReaderDuringThumbnailStopsItsCutPipelineAndFreshReaderCanOpen()
    {
        var browser = new LifetimeBrowser();
        browser.First.ThumbnailReady = new(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var host = new ComponentHost(js: browser);
        var piece = await host.AddPiece(pdf: true);
        var written = new ReadBar(1, 0, 4, 4, false, BarEnd.Normal, 0,
            [new(1, ReadClef.Treble, [new(1, [new(ReadKind.Note, [new(Step.C, Accidental.None, 4, false, 0)], NoteValue.Whole, 0, 0, false, false, Articulation.None, [], 0, ReadClef.None)])])]);
        var reading = new SystemReading([written], []);
        var mei = ReadingScore.Mei(ReadingScore.Build([reading]).Score);
        var draft = new ReadingDraft(true, "", mei, [], [new(1, 1, SystemStep.Done, "")], 0, TimeSpan.Zero, [reading], 1, 1);
        await host.Store.SaveReadingAsync(piece.Id, JsonSerializer.Serialize(draft, AssistSchema.Json));
        var view = host.Context.Render<ReadNotes>(p => p.Add(c => c.Piece, piece).Add(c => c.Pdf, [1])
            .Add(c => c.Layout, [new List<PageSystem> { new(10, 200, 4) }]));
        await browser.First.ThumbnailStarted.Task.WaitAsync(Xunit.TestContext.Current.CancellationToken);
        var cutter = view.FindComponent<PdfPages>().Instance;
        await view.InvokeAsync(() => view.Instance.DisposeAsync().AsTask());
        await view.InvokeAsync(() => cutter.DisposeAsync().AsTask());
        view.Dispose();
        var releases = browser.First.Calls.Count(c => c == "release");
        browser.First.ThumbnailReady.SetResult();
        await browser.First.ThumbnailReturned.Task.WaitAsync(Xunit.TestContext.Current.CancellationToken);
        await host.Context.Renderer.Dispatcher.InvokeAsync(() => Task.CompletedTask);

        Assert.DoesNotContain("cut", browser.First.Calls);
        Assert.Equal(releases, browser.First.Calls.Count(c => c == "release"));
        var reopened = host.Context.Render<ReadNotes>(p => p.Add(c => c.Piece, piece).Add(c => c.Pdf, [1])
            .Add(c => c.Layout, [new List<PageSystem> { new(10, 200, 4) }]));
        reopened.WaitForElement(".pdfread-card .pdfread-paper img");
        Assert.Contains("cut", browser.Fresh.Calls);
        Assert.Equal(0, host.Claude.Calls);
    }

    private sealed class LifetimeBrowser : IJSRuntime
    {
        public LifetimeModule First { get; } = new();
        public LifetimeModule Fresh { get; } = new();
        public bool HoldFirstImport { get; init; }
        public TaskCompletionSource ImportStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource ImportReady { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int imports;
        private int pdfImports;

        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args) => InvokeAsync<TValue>(identifier, default, args);
        public async ValueTask<TValue> InvokeAsync<TValue>(string identifier, CancellationToken cancellationToken, object?[]? args)
        {
            if (identifier != "import") return default!;
            LifetimeModule module;
            if (HoldFirstImport) module = ++imports == 1 ? First : Fresh;
            else module = ((string)args![0]!).Contains("PdfPages", StringComparison.Ordinal) ? ++pdfImports == 1 ? First : Fresh : new LifetimeModule();
            ImportStarted.TrySetResult();
            if (HoldFirstImport && ReferenceEquals(module, First)) await ImportReady.Task.WaitAsync(cancellationToken);
            return (TValue)(object)module;
        }
    }

    private sealed class LifetimeModule : IJSObjectReference
    {
        public List<string> Calls { get; } = [];
        public int DisposeCount { get; private set; }
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Disposed { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource OpenReturned { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource ThumbnailStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource ThumbnailReturned { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource? OpenReady { get; set; }
        public TaskCompletionSource? ThumbnailReady { get; set; }

        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args) => InvokeAsync<TValue>(identifier, default, args);
        public async ValueTask<TValue> InvokeAsync<TValue>(string identifier, CancellationToken cancellationToken, object?[]? args)
        {
            ObjectDisposedException.ThrowIf(DisposeCount > 0, this);
            Calls.Add(identifier);
            Started.TrySetResult();
            if (identifier == "open")
            {
                try { if (OpenReady is not null) await OpenReady.Task.WaitAsync(cancellationToken); }
                finally { OpenReturned.TrySetResult(); }
                return JsonSerializer.Deserialize<TValue>("{\"Pages\":1,\"Ratios\":[0.75]}")!;
            }
            if (identifier == "thumbnail")
            {
                ThumbnailStarted.TrySetResult();
                if (ThumbnailReady is not null) await ThumbnailReady.Task.WaitAsync(cancellationToken);
                ThumbnailReturned.TrySetResult();
                return (TValue)(object)"blob:thumbnail";
            }
            if (identifier == "cut") return (TValue)(object)new PdfPages.SystemCut[] { new([1], "blob:cut", [0.1, 0.9], new(0.1, 0.1, 0.8, 0.3)) };
            if (identifier == "profile") return (TValue)(object)Array.Empty<double>();
            return default!;
        }

        public ValueTask DisposeAsync()
        {
            DisposeCount++;
            Disposed.TrySetResult();
            return ValueTask.CompletedTask;
        }
    }
}
