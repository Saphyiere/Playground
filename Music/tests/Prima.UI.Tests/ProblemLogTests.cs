using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Prima.UI.Host;
using Prima.UI.Layout;

namespace Prima.UI.Tests;

public class ProblemLogTests : IDisposable
{
    private readonly ProblemLog log = new(Path.Combine(Path.GetTempPath(), $"prima-problems-{Guid.NewGuid():N}.log"));

    [Fact]
    public void AProblemIsKeptWithWhatWasSaidFirstAndTheRestUnderItNewestFirst()
    {
        log.Keep("Warning", "Prima.UI.Pages.Library", "Couldn't read a file");
        log.Keep("Error", "Prima.UI.Pages.Piece", "Couldn't draw the bars\nat bars.js:315", "System.InvalidOperationException: strip\n   at Piece.OnAfterRenderAsync()");

        var kept = log.Recent();
        Assert.Equal(["Couldn't draw the bars", "Couldn't read a file"], kept.Select(p => p.Message));
        var newest = kept[0];
        Assert.Equal(("Error", "Prima.UI.Pages.Piece", "Piece"), (newest.Level, newest.Source, newest.Short));
        Assert.Equal("at bars.js:315\nSystem.InvalidOperationException: strip\n   at Piece.OnAfterRenderAsync()", newest.Detail);
        Assert.Contains("=== ", File.ReadAllText(log.Path));
    }

    [Fact]
    public void ALineOfDetailThatLooksLikeAnEntryDoesntSplitIt()
    {
        log.Keep("Error", "Prima.Tablet", "Prima stopped", "=== not a new entry");

        var only = Assert.Single(log.Recent());
        Assert.Equal(" === not a new entry", only.Detail);
    }

    [Fact]
    public void PastHalfAMegabyteTheFileStartsAfreshAndTheOneBeforeIsStillRead()
    {
        var long_ = new string('x', 200 * 1024);
        for (var i = 1; i <= 4; i++) log.Keep("Error", "Prima.Test", $"Problem {i}", long_);

        Assert.True(File.Exists(log.OldPath));
        Assert.True(new FileInfo(log.Path).Length < ProblemLog.Largest);
        Assert.Equal(["Problem 4", "Problem 3", "Problem 2", "Problem 1"], log.Recent().Select(p => p.Message));
        Assert.Equal(["Problem 4", "Problem 3"], log.Recent(2).Select(p => p.Message));
    }

    [Fact]
    public void WarningsAndErrorsLoggedByPrimaTheKitAndBlazorAreKeptAndNothingElse()
    {
        using var provider = new ProblemLogProvider(log);
        provider.CreateLogger("Prima.UI.Routines.RoutineService").LogInformation("Asked for a routine");
        provider.CreateLogger("Prima.UI.Routines.RoutineService").LogWarning("Notification permission could not be requested");
        provider.CreateLogger("Playground.AI.Assist.Claude").LogError(new TimeoutException("no answer"), "Claude couldn't be asked");
        provider.CreateLogger("Microsoft.AspNetCore.Components.RenderTree.Renderer").LogError("Unhandled exception rendering component");
        provider.CreateLogger("System.Net.Http.HttpClient").LogError("Not ours");

        var kept = log.Recent();
        Assert.Equal(["Unhandled exception rendering component", "Claude couldn't be asked", "Notification permission could not be requested"], kept.Select(p => p.Message));
        Assert.Contains("System.TimeoutException: no answer", kept[1].Detail);
    }

    // The page's own problems are logged when Prima next starts: each is kept as of when it happened.
    [Fact]
    public void AProblemLoggedWithItsTimeIsKeptAsOfThen()
    {
        using var provider = new ProblemLogProvider(log);
        var then = new DateTimeOffset(2026, 11, 15, 13, 56, 12, TimeSpan.FromHours(-8));
        provider.CreateLogger("Prima.UI.Layout.MainLayout").LogError("The page stopped answering at {Path} ({At:d MMM HH:mm:ss}): {Said}", "/library/sweden", then, "TypeError: strip.replaceChildren is not a function");

        var kept = Assert.Single(log.Recent());
        Assert.Equal(then, kept.At);
        Assert.Equal("The page stopped answering at /library/sweden (15 Nov 13:56:12): TypeError: strip.replaceChildren is not a function", kept.Message);
    }

    // The owner's rule 44, decided 15 November (the review of main at a7ab94d, part 5): what a page that has gone throws
    // (ObjectDisposedException from its script) isn't kept as "Work nobody waited for failed". Anything else that failed with
    // nobody waiting is, an ObjectDisposedException of Prima's own among it.
    [Fact]
    public void WhatAPageThatHasGoneThrowsIsntKeptAsWorkNobodyWaitedFor()
    {
        using var provider = new ProblemLogProvider(log);
        var logger = provider.CreateLogger("Prima.Tablet");
        // A call into the page's script once it was let go with the page: its module, or an object it held for the script.
        var held = Microsoft.JSInterop.DotNetObjectReference.Create(new object());
        held.Dispose();
        var heldGone = Assert.Throws<ObjectDisposedException>(() => held.Value);
        var moduleGone = new ObjectDisposedException("Microsoft.JSInterop.Implementation.JSObjectReference");

        UnwaitedWork.Keep(logger, new AggregateException(heldGone));
        UnwaitedWork.Keep(logger, new AggregateException(moduleGone, new OperationCanceledException()));

        Assert.Empty(log.Recent());

        var cancelled = new CancellationTokenSource();
        cancelled.Dispose();
        var ours = Assert.Throws<ObjectDisposedException>(() => cancelled.Token);
        UnwaitedWork.Keep(logger, new AggregateException(ours));
        UnwaitedWork.Keep(logger, new AggregateException(moduleGone, new InvalidOperationException("The store failed")));

        var kept = log.Recent();
        Assert.Equal(2, kept.Count);
        Assert.All(kept, p => Assert.StartsWith("Work nobody waited for failed: ", p.Message));
        Assert.Contains("The store failed", $"{kept[0].Message}\n{kept[0].Detail}");
        Assert.Contains("System.Threading.CancellationTokenSource", $"{kept[1].Message}\n{kept[1].Detail}");
    }

    [Fact]
    public void ClearTakesBothFiles()
    {
        var long_ = new string('x', 300 * 1024);
        for (var i = 0; i < 3; i++) log.Keep("Error", "Prima.Test", "Problem", long_);
        log.Clear();

        Assert.Empty(log.Recent());
        Assert.False(File.Exists(log.Path) || File.Exists(log.OldPath));
    }

    // A screen that fails (here, in what a tap does) is kept in the problem log and gives way to a card saying so, with Try again:
    // Prima still answers taps, where before the whole page stopped.
    [Fact]
    public async Task AScreenThatFailsIsKeptAndOfferedAgainAndAnotherScreenStartsAfresh()
    {
        await using var host = new ComponentHost();
        var view = host.Context.Render<ScreenErrorBoundary>(p => p.AddChildContent<Breaks>());

        view.Find("button.breaks").Click();

        view.WaitForElement("[data-state=screen-stopped]");
        Assert.Contains("Sequence contains no matching element", view.Markup);
        var kept = Assert.Single(host.Problems.Recent());
        Assert.Equal(("Error", "ScreenErrorBoundary"), (kept.Level, kept.Short));
        Assert.Contains("Sequence contains no matching element", kept.Message);
        Assert.Contains("InvalidOperationException", kept.Detail);
        // See it in Settings opens the tab Problems is on, and brings it into view.
        Assert.Equal("settings?tab=app&section=problems", view.Find("[data-state=screen-stopped] a[href^=settings]").GetAttribute("href"));

        view.Find("[data-state=screen-stopped] .mu-primary").Click();
        view.WaitForElement("button.breaks").Click();
        view.WaitForElement("[data-state=screen-stopped]");
        host.Context.Services.GetRequiredService<NavigationManager>().NavigateTo("settings");
        view.WaitForElement("button.breaks");
    }

    private sealed class Breaks : ComponentBase
    {
        protected override void BuildRenderTree(RenderTreeBuilder builder)
        {
            builder.OpenElement(0, "button");
            builder.AddAttribute(1, "class", "breaks");
            builder.AddAttribute(2, "onclick", EventCallback.Factory.Create(this, () => throw new InvalidOperationException("Sequence contains no matching element")));
            builder.AddContent(3, "Break");
            builder.CloseElement();
        }
    }

    public void Dispose() => log.Clear();
}
