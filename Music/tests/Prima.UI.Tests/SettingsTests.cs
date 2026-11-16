using System.Text.RegularExpressions;
using AngleSharp.Dom;
using Bunit;
using Bunit.TestDoubles;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Playground.Sync;
using Prima.UI.Components;
using Prima.UI.Looks;
using Prima.UI.Pages;

namespace Prima.UI.Tests;

// Settings in five tabs, the one open kept in the address, and a search that finds a setting in any tab.
public class SettingsTests
{
    [Fact]
    public async Task SettingsOpensOnPracticeAndATabTappedOpensInTheAddressInPlace()
    {
        await using var host = new ComponentHost();
        var nav = (BunitNavigationManager)host.Context.Services.GetRequiredService<NavigationManager>();
        nav.NavigateTo("settings");
        var view = host.Context.Render<Settings>();

        Assert.Equal(["Practice", "Keyboard & sound", "Look & music", "Claude", "App & data"], view.FindAll("[role=tab]").Select(t => t.TextContent));
        Assert.Equal("Practice", Selected(view));
        Assert.Equal(["Profile", "Practice"], Shown(view));

        await Open(view, "Keyboard & sound");
        Assert.Equal("Keyboard & sound", Selected(view));
        Assert.Equal(["Keyboard", "Click"], Shown(view));
        Assert.Equal("http://localhost/settings?tab=keyboard", nav.Uri);
        // In the address's place: the way back leaves Settings rather than stepping back through its tabs.
        Assert.True(nav.History.First().Options.ReplaceHistoryEntry);

        await Open(view, "Look & music");
        Assert.Equal(["Look", "Notation"], Shown(view));
        await Open(view, "Claude");
        Assert.Equal(["Claude"], Shown(view));
        await Open(view, "App & data");
        Assert.Equal(["Updates", "Your data", "Problems"], Shown(view));
        Assert.Equal("http://localhost/settings?tab=app", nav.Uri);
    }

    // Coming back to Settings (the way back, or a link), the address says which tab; one it doesn't know is the first.
    [Fact]
    public async Task TheTabInTheAddressIsTheOneOpen()
    {
        await using var host = new ComponentHost();
        var nav = host.Context.Services.GetRequiredService<NavigationManager>();
        nav.NavigateTo("settings?tab=claude");
        var view = host.Context.Render<Settings>();
        Assert.Equal("Claude", Selected(view));
        Assert.Equal(["Claude"], Shown(view));

        nav.NavigateTo("settings?tab=app");
        view.WaitForAssertion(() => Assert.Equal(["Updates", "Your data", "Problems"], Shown(view)));
        Assert.Equal("App & data", Selected(view));

        nav.NavigateTo("settings?tab=nonsense");
        view.WaitForAssertion(() => Assert.Equal("Practice", Selected(view)));
        Assert.Equal(["Profile", "Practice"], Shown(view));
    }

    [Fact]
    public async Task ASearchFindsSettingsFromEveryTabByNameChoiceOrWhatTheySayAndClearedTheTabsComeBack()
    {
        await using var host = new ComponentHost();
        var view = host.Context.Render<Settings>();
        await Open(view, "Look & music");

        // In what a setting says: only it, under its tab and its section, and no tabs.
        await Search(view, "headphones");
        Assert.Empty(view.FindAll("[role=tab]"));
        Assert.Equal(["Keyboard & sound › Click"], Shown(view));
        Assert.Equal(["Where"], Labels(view));
        Assert.Contains("With headphones in the piano", view.Find("#click").TextContent);

        // A choice, whatever its case; a section named like its tab is headed by the tab alone.
        await Search(view, "two BARS");
        Assert.Equal(["Practice"], Shown(view));
        Assert.Equal(["Count-in"], Labels(view));

        // A section's name finds all of it.
        await Search(view, "click");
        Assert.Equal(["Where", "Sound", "Volume"], Labels(view));

        // From two tabs: Claude says Back up never carries the key.
        await Search(view, "back up");
        Assert.Equal(["Claude", "App & data › Your data"], Shown(view));

        // Every word, in any order; # for ♯.
        await Search(view, "above c#");
        Assert.Equal(["Look before playing", "Piano buttons"], Labels(view));
        Assert.Equal(["Practice", "Keyboard & sound › Keyboard"], Shown(view));

        await Search(view, "zzz");
        Assert.Empty(Shown(view));
        Assert.Equal("Nothing in Settings for “zzz”.", view.Find(".page-note").TextContent);

        // Cleared, the tabs come back, on the one that was open.
        await Search(view, " ");
        Assert.Equal("Look & music", Selected(view));
        Assert.Equal(["Look", "Notation"], Shown(view));
        Assert.Empty(view.FindAll(".settings-found-in"));
    }

    // What a search finds is the setting itself: its choices still set it.
    [Fact]
    public async Task AChoiceASearchFoundStillSetsIt()
    {
        await using var host = new ComponentHost();
        var view = host.Context.Render<Settings>();
        await Search(view, "two bars");
        await view.InvokeAsync(() => Choice(view, "Two bars").ClickAsync(new MouseEventArgs()));

        Assert.Equal("true", Choice(view, "Two bars").GetAttribute("aria-pressed"));
        Assert.Equal("false", Choice(view, "One bar").GetAttribute("aria-pressed"));
        var saved = new PrimaSettings();
        await host.Store.LoadAsync(saved);
        Assert.Equal(2, saved.CountInBars);
    }

    // Each row of every tab is found by its label and by each of its choices, so a word on the screen is a word that finds it.
    [Fact]
    public async Task EverySettingIsFoundByItsNameAndByEachOfItsChoices()
    {
        await using var host = new ComponentHost();
        var view = host.Context.Render<Settings>();
        foreach (var tab in SettingsFinder.Tabs)
        {
            await Search(view, "");
            await Open(view, tab.Name);
            var rows = view.FindAll("section.settings-section:not([hidden]) .control-row").Select(r => (Label: Label(r), Choices: Choices(r)))
                .Where(r => r.Label.Length > 0 || r.Choices.Length > 0).ToList();
            Assert.NotEmpty(rows);
            foreach (var (label, choices) in rows)
            {
                foreach (var words in choices.Prepend(label).Where(w => w.Length > 0))
                {
                    await Search(view, words);
                    Assert.True(view.FindAll("section.settings-section:not([hidden]) .control-row").Any(r => Label(r) == label && Choices(r).SequenceEqual(choices)),
                        $"“{words}” doesn't find {tab.Name} › {(label.Length > 0 ? label : string.Join(", ", choices))}");
                }
            }
        }

        foreach (var look in Look.All)
        {
            await Search(view, look.Name);
            Assert.Single(view.FindAll($".look-option[data-theme={look.Id}]"));
        }
    }

    // The stopped screen's card opens App & data, Problems brought into view.
    [Fact]
    public async Task TheProblemsLinkOpensAppAndDataWithProblemsInView()
    {
        await using var host = new ComponentHost();
        host.Problems.Keep("Error", "Prima.UI.Layout.ScreenErrorBoundary", "The screen at /library stopped: no strip");
        host.Context.Services.GetRequiredService<NavigationManager>().NavigateTo("settings?tab=app&section=problems");
        var view = host.Context.Render<Settings>();

        Assert.Equal("App & data", Selected(view));
        Assert.Contains("The screen at /library stopped: no strip", view.Find("#problems").TextContent);
        // Once the page has what it reads as it opens, so nothing drawn above it moves it after.
        Assert.Equal("problems", await host.Browser.SectionShown.Task.WaitAsync(TimeSpan.FromSeconds(10), Xunit.TestContext.Current.CancellationToken));
        Assert.Equal(["problems"], host.Browser.ShownSections);
    }

    // Start over clears what was practised and nothing of the Library: the music imported and the collection aren't progress. Then
    // Prima opens on its welcome (the owner's rule 2, decided 15 November: YourDataTests has the rest of it).
    [Fact]
    public async Task StartingOverClearsTheProgressAndKeepsTheLibrary()
    {
        await using var host = new ComponentHost();
        var piece = await host.AddPiece();
        var today = DateOnly.FromDateTime(DateTime.Now);
        var session = await host.Store.StartSessionAsync(piece.Id, "standard", DateTimeOffset.Now.AddMinutes(-5));
        await host.Store.AddAttemptAsync(new Prima.UI.Storage.AttemptRecord(0, DateTimeOffset.Now, today, "piece", "in time", piece.Id, 1, 72, null, null, "[]", null, $"{{\"session\":{session}}}", 0, false, null));
        await host.Store.SaveTrackAsync("drill:playback", new Prima.UI.Practice.DrillTrack(3, 0.9, 0.9, today));
        await host.Store.AddMinutesAsync(today, 5);
        var nav = host.Context.Services.GetRequiredService<NavigationManager>();
        nav.NavigateTo("settings?tab=app");
        var view = host.Context.Render<Settings>();

        await view.InvokeAsync(() => view.FindAll("button").Single(b => b.TextContent == "Start over…").ClickAsync(new MouseEventArgs()));
        Assert.Contains(view.FindAll("[role=status]"), s => s.TextContent.Contains("The settings and the Library stay as they are"));
        await view.InvokeAsync(() => view.FindAll("button").Single(b => b.TextContent == "Clear my progress").ClickAsync(new MouseEventArgs()));

        Assert.Equal("http://localhost/welcome", nav.Uri);
        Assert.Equal([piece.Id], await host.Store.GetPieceIdsAsync());
        Assert.NotNull(await host.Store.GetPieceAsync(piece.Id));
        Assert.Empty(await host.Store.GetAttemptsAsync(DateOnly.MinValue));
        Assert.Empty(await host.Store.GetSessionsAsync(piece.Id));
        Assert.Empty(await host.Store.GetDaysAsync(DateOnly.MinValue));
        Assert.Null(await host.Store.GetTrackAsync<Prima.UI.Practice.DrillTrack>("drill:playback"));
    }

    // The copy before an update couldn't be made: the update's screen says why and offers to install without one, saying there'll
    // be nothing to go back to; only that installs it.
    [Fact]
    public async Task AnUpdateWhoseCopyCantBeMadeInstallsOnlyWhenAskedAgainWithoutACopy()
    {
        await using var host = new ComponentHost(safeguard: new NoCopy());
        host.Context.Services.GetRequiredService<Playground.Updates.StandInUpdates>().Show("1.0", 13);
        host.Context.Services.GetRequiredService<NavigationManager>().NavigateTo("settings?tab=app");
        var view = host.Context.Render<Settings>();
        await view.InvokeAsync(() => view.FindAll("button").Single(b => b.TextContent == "Check").ClickAsync(new MouseEventArgs()));
        view.WaitForAssertion(() => Assert.Contains(view.FindAll("button"), b => b.TextContent == "Install"));
        Assert.DoesNotContain(view.FindAll("button"), b => b.TextContent == "Install without a copy");

        await view.InvokeAsync(() => view.FindAll("button").Single(b => b.TextContent == "Install").ClickAsync(new MouseEventArgs()));

        view.WaitForAssertion(() => Assert.Contains("A copy of your data couldn't be kept first (No space left), so nothing was installed.", view.Find("#updates").TextContent), TimeSpan.FromSeconds(30));
        Assert.Contains("there will be nothing to go back to", view.Find("#updates").TextContent);
        await view.InvokeAsync(() => view.FindAll("button").Single(b => b.TextContent == "Install without a copy").ClickAsync(new MouseEventArgs()));
        view.WaitForAssertion(() => Assert.Contains("Confirm the update to Prima 1.0.13. No copy of your data was kept first.", view.Find("#updates").TextContent), TimeSpan.FromSeconds(30));
        Assert.DoesNotContain(view.FindAll("button"), b => b.TextContent == "Install without a copy");
    }

    private sealed class NoCopy : Playground.Updates.IUpdateSafeguard
    {
        public Task<string> KeepBeforeUpdateAsync(Playground.Updates.Release release, IProgress<double>? progress, CancellationToken ct) =>
            throw new IOException("No space left");
    }

    // The owner's rule 43, decided 15 November (the review of main at a7ab94d, part 5): Copy in Problems copies every problem kept,
    // not the newest 12; the list still shows 12.
    [Fact]
    public async Task CopyInProblemsCopiesEveryProblemKeptThoughTheListShowsTwelve()
    {
        var clipboard = new Clipboard(new BrowserCalls());
        await using var host = new ComponentHost(js: clipboard);
        for (var i = 1; i <= 15; i++) host.Problems.Keep("Error", "Prima.Test", $"Problem {i}");
        host.Context.Services.GetRequiredService<NavigationManager>().NavigateTo("settings?tab=app&section=problems");
        var view = host.Context.Render<Settings>();
        view.WaitForAssertion(() => Assert.Equal(12, view.FindAll("#problems .problem").Count));

        await view.InvokeAsync(() => view.FindAll("#problems button").Single(b => b.TextContent == "Copy").ClickAsync(new MouseEventArgs()));

        view.WaitForAssertion(() => Assert.Contains("Copied 15 problems.", view.Find("#problems").TextContent));
        Assert.Equal(15, clipboard.Text!.Split("\n\n").Length);
        Assert.All(Enumerable.Range(1, 15), i => Assert.Contains($"\nProblem {i}\n", clipboard.Text + "\n"));
        Assert.Equal(12, view.FindAll("#problems .problem").Count);
    }

    // The page's browser, keeping what was put on the clipboard.
    private sealed class Clipboard(BrowserCalls browser) : Microsoft.JSInterop.IJSRuntime
    {
        public string? Text { get; private set; }

        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args) => InvokeAsync<TValue>(identifier, default, args);

        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, CancellationToken cancellationToken, object?[]? args)
        {
            if (identifier == "navigator.clipboard.writeText") Text = (string?)args?[0];
            return browser.InvokeAsync<TValue>(identifier, cancellationToken, args);
        }
    }

    // Start over keeps a copy of everything first, held in Your data (named for it, with its time) until it's restored or let go.
    [Fact]
    public async Task StartingOverKeepsACopyFirst_HeldInYourData()
    {
        await using var host = new ComponentHost();
        var sync = host.Context.Services.GetRequiredService<Syncer>();
        await host.AddPiece();
        await host.Store.AddMinutesAsync(DateOnly.FromDateTime(DateTime.Now), 5);
        var nav = host.Context.Services.GetRequiredService<NavigationManager>();
        nav.NavigateTo("settings?tab=app");
        var view = host.Context.Render<Settings>();

        await view.InvokeAsync(() => view.FindAll("button").Single(b => b.TextContent == "Start over…").ClickAsync(new MouseEventArgs()));
        Assert.Contains(view.FindAll("[role=status]"), s => s.TextContent.EndsWith("A copy of everything is kept first, held in Your data until you restore it or let it go.", StringComparison.Ordinal));
        await view.InvokeAsync(() => view.FindAll("button").Single(b => b.TextContent == "Clear my progress").ClickAsync(new MouseEventArgs()));

        Assert.Empty(await host.Store.GetDaysAsync(DateOnly.MinValue));
        var kept = Assert.Single(sync.Status.Holding);
        Assert.Equal((SyncHeldFrom.Change, "Start over"), (kept.Held!.From, kept.Held.Why));
        Assert.Equal(1, kept.Manifest.Rows("prima.db", "PracticeDay"));
        // Prima went on to its welcome; back in Your data, the copy is there.
        Assert.Equal("http://localhost/welcome", nav.Uri);
        nav.NavigateTo("settings?tab=app");
        view.WaitForAssertion(() => Assert.Equal($"Kept from before Start over · {SyncText.When(kept.Made)}", view.Find(".copies-held-head").TextContent));
        Directory.Delete(sync.Options.WorkFolder, recursive: true);
    }

    // When no copy can be kept first, nothing is cleared and the note says why; clearing without one is asked for apart.
    [Fact]
    public async Task StartingOverWithoutACopy_ClearsOnlyWhenAskedTo()
    {
        await using var host = new ComponentHost();
        var sync = host.Context.Services.GetRequiredService<Syncer>();
        await host.AddPiece();
        await host.Store.AddMinutesAsync(DateOnly.FromDateTime(DateTime.Now), 5);
        // A problem kept with the key in it: no copy can be taken.
        host.Problems.Keep("Error", "Prima.UI.Pages.Settings", "The key test-key was refused");
        var nav = host.Context.Services.GetRequiredService<NavigationManager>();
        nav.NavigateTo("settings?tab=app");
        var view = host.Context.Render<Settings>();
        await view.InvokeAsync(() => view.FindAll("button").Single(b => b.TextContent == "Start over…").ClickAsync(new MouseEventArgs()));

        await view.InvokeAsync(() => view.FindAll("button").Single(b => b.TextContent == "Clear my progress").ClickAsync(new MouseEventArgs()));

        Assert.Contains(view.FindAll("[role=status]"), s => s.TextContent == "A copy couldn't be kept first (errors.log holds a secret, which copies never take), so nothing was cleared.");
        Assert.Single(await host.Store.GetDaysAsync(DateOnly.MinValue));
        Assert.Equal("http://localhost/settings?tab=app", nav.Uri);

        await view.InvokeAsync(() => view.FindAll("button").Single(b => b.TextContent == "Clear it without a copy").ClickAsync(new MouseEventArgs()));

        Assert.Equal("http://localhost/welcome", nav.Uri);
        Assert.Empty(await host.Store.GetDaysAsync(DateOnly.MinValue));
        Assert.Empty(sync.Status.Holding);
        if (Directory.Exists(sync.Options.WorkFolder)) Directory.Delete(sync.Options.WorkFolder, recursive: true);
    }

    // The owner's rule 2, decided 15 November (the review of main at a7ab94d, part 6): Start over takes everything Prima keeps about
    // the playing, so Prima is as on its first open, with the settings and the Library as they are, and then it goes to the welcome
    // screen. That's levels and tracks, note cards and answers, every run, practice days and sessions, routines and requests, which
    // sight-reading passages have come up, and whatever else a first open doesn't have: what's next on each piece, where it was
    // left, its tempo, the hand practiced, when it was opened.
    [Fact]
    public async Task StartOverTakesEverythingAboutThePlayingAsOnAFirstOpen_TheSettingsAndTheLibraryStaying_ThenOpensTheWelcome()
    {
        await using var host = new ComponentHost();
        var nav = host.Context.Services.GetRequiredService<NavigationManager>();
        var options = host.Context.Services.GetRequiredService<PrimaSettings>();
        var today = DateOnly.FromDateTime(DateTime.Now);
        // The Library: a piece with an excerpt, and the view it shows in.
        var piece = await host.AddPiece();
        await host.Store.SavePieceAsync(piece, await host.Store.GetMeiAsync(piece.Id), [new Prima.Core.Library.BundledExcerpt(1, 1, 4, 0.5)]);
        await host.Store.SetSettingAsync($"Library.View.{piece.Id}", "pages");
        // The settings, the look among them.
        (options.Name, options.CountInBars, options.Welcomed) = ("Ana", 2, true);
        await host.Store.SaveAsync(options);
        host.Context.Services.GetRequiredService<LookSetting>().Choose(Look.Vinyl);
        // The playing: levels and tracks, a note card and its answer, a passage, a practice day, a routine followed today and a
        // request, a sight-reading passage from the piece read, a session on the piece yesterday whose run left something to do
        // next, and the piece's place, tempo and hand.
        await host.Store.SaveTrackAsync("reading", Prima.Core.Practice.ReadingState.Start with { Level = 3 });
        await host.Store.SaveTrackAsync("exercise-routine", new[] { "octave-jump" });
        await host.Store.SaveAnswerAsync(new Prima.Core.Practice.NoteCard(Prima.Core.Music.Clef.Treble, 60, 2, today, today, today),
            new Prima.Core.Practice.NoteAnswer(DateTimeOffset.Now, Prima.Core.Music.Clef.Treble, 60, true, 900), 60, "notes");
        await host.Store.AddAttemptAsync(new Prima.UI.Storage.AttemptRecord(0, DateTimeOffset.Now, today, "passage", null, null, 3, 66, 7, null, "[]", null, "{\"inTime\":0.9}", 0.9, false, null));
        await host.Store.AddMinutesAsync(today, 12);
        var routine = RoutineStoreTests.ReadyJob();
        await host.Store.CreateRoutineJobAsync(routine);
        await host.Store.AcceptRoutineAsync(routine.Id);
        await host.Store.GetOrCreateRoutineDayAsync(today);
        await host.Store.CreateRoutineJobAsync(RoutineStoreTests.ReadyJob());
        var excerpt = Assert.Single(await host.Store.GetExcerptsAsync(4, 1, today));
        await host.Store.MarkExcerptReadAsync(excerpt.Id, today);
        var yesterday = DateTimeOffset.Now.AddDays(-1);
        var session = await host.Store.StartSessionAsync(piece.Id, "standard", yesterday);
        await host.Store.AddAttemptAsync(new Prima.UI.Storage.AttemptRecord(0, yesterday.AddMinutes(3), DateOnly.FromDateTime(yesterday.LocalDateTime), "piece", "in time", piece.Id, 1, 72, null, null, "[]", null,
            System.Text.Json.JsonSerializer.Serialize(new { inTime = 0.0, right = 0, wrong = 1, missed = 0, from = 1, stopped = false, bar = 1, hands = "both", session, took = 12.5, trouble = new[] { new[] { 1, 1, 0, 0, 0, 0, 0, 0 } } }), 0, false, null));
        await host.Store.SaveSessionAsync(new Prima.UI.Storage.PracticeSession(session, piece.Id, yesterday, TimeSpan.FromMinutes(4), TimeSpan.FromMinutes(1), true, yesterday.AddMinutes(3), "standard", yesterday.AddMinutes(5)));
        await host.Store.SetPieceAsync(piece.Id, tempo: 96, position: "{\"page\":1,\"y\":0,\"from\":1,\"bar\":1}");
        await host.Store.SetSettingAsync($"Library.Hands.{piece.Id}", "right");
        // The piece's front says what to do next.
        host.Context.Render<Piece>(p => p.Add(c => c.Id, piece.Id)).WaitForElement(".about-next");
        nav.NavigateTo("settings?tab=app");
        var view = host.Context.Render<Settings>();

        await view.InvokeAsync(() => view.FindAll("button").Single(b => b.TextContent == "Start over…").ClickAsync(new MouseEventArgs()));
        await view.InvokeAsync(() => view.FindAll("button").Single(b => b.TextContent == "Clear my progress").ClickAsync(new MouseEventArgs()));

        // The playing has gone.
        Assert.Null(await host.Store.GetTrackAsync<Prima.Core.Practice.ReadingState>("reading"));
        Assert.Null(await host.Store.GetTrackAsync<string[]>("exercise-routine"));
        Assert.Empty(await host.Store.GetCardsAsync());
        Assert.Empty(await host.Store.GetAnswersAsync(DateTimeOffset.MinValue));
        Assert.Empty(await host.Store.GetAttemptsAsync(DateOnly.MinValue));
        Assert.Empty(await host.Store.GetPieceRunsAsync(piece.Id));
        Assert.Empty(await host.Store.GetDaysAsync(DateOnly.MinValue));
        Assert.Empty(await host.Store.GetSessionsAsync(piece.Id));
        Assert.Null(await host.Store.GetActiveRoutineAsync());
        Assert.Empty(await host.Store.GetRoutineJobsNotAcceptedAsync());
        var read = await host.Store.GetExcerptAsync(excerpt.Id);
        Assert.NotNull(read);
        Assert.Equal((0, (DateOnly?)null), (read.Used, read.LastUsed));
        var opened = await host.Store.GetPieceAsync(piece.Id);
        Assert.NotNull(opened);
        Assert.Null(opened.Position);
        Assert.Null(opened.Tempo);
        Assert.Null(opened.Opened);
        var saved = await host.Store.GetSettingsAsync();
        Assert.False(saved.ContainsKey($"Library.Hands.{piece.Id}"));
        // The settings and the Library are as they were.
        Assert.Equal("pages", saved[$"Library.View.{piece.Id}"]);
        Assert.Equal("vinyl", saved["Look"]);
        var settings = new PrimaSettings();
        await host.Store.LoadAsync(settings);
        Assert.Equal(("Ana", 2, true), (settings.Name, settings.CountInBars, settings.Welcomed));
        Assert.Equal([piece.Id], await host.Store.GetPieceIdsAsync());
        Assert.NotNull(await host.Store.GetMeiAsync(piece.Id));
        // Then the welcome.
        Assert.Equal("http://localhost/welcome", nav.Uri);
        Directory.Delete(host.Context.Services.GetRequiredService<Syncer>().Options.WorkFolder, recursive: true);
    }

    // On the page's dispatcher, so it draws nothing between finding the element and tapping it (Settings redraws as it loads).
    private static Task Open(IRenderedComponent<Settings> view, string tab) => view.InvokeAsync(() => view.FindAll("[role=tab]").Single(t => t.TextContent == tab).Click());

    private static Task Search(IRenderedComponent<Settings> view, string words) => view.InvokeAsync(() => view.Find("input.settings-search").Input(words));

    private static string? Selected(IRenderedComponent<Settings> view) => view.FindAll("[role=tab][aria-selected=true]").SingleOrDefault()?.TextContent;

    private static string[] Shown(IRenderedComponent<Settings> view) => [.. view.FindAll("section.settings-section:not([hidden]) > h2").Select(h => Words(h.TextContent))];

    private static string[] Labels(IRenderedComponent<Settings> view) =>
        [.. view.FindAll("section.settings-section:not([hidden]) .control-label").Select(l => Words(l.TextContent)).Where(l => l.Length > 0)];

    private static IElement Choice(IRenderedComponent<Settings> view, string words) => view.FindAll("button.mu-toggle").Single(b => Words(b.TextContent) == words);

    private static string Label(IElement row) => Words(row.QuerySelector(".control-label")?.TextContent ?? "");

    // A row's buttons and links, by their words, without an ellipsis ("Start over…").
    private static string[] Choices(IElement row) => [.. row.QuerySelectorAll("button, a").Select(b => Words(b.TextContent).TrimEnd('…'))];

    private static string Words(string text) => Regex.Replace(text, @"\s+", " ").Trim();
}
