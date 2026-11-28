using System.Text.RegularExpressions;
using AngleSharp.Dom;
using Bunit;
using Bunit.TestDoubles;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Playground.Sync;
using Playground.Updates;
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

        Assert.Equal("practice", Selected(view));
        Assert.Equal(["practice", "playback", "reading", "profile"], Shown(view));
        foreach (var tab in SettingsFinder.Tabs.Skip(1))
        {
            await Open(view, tab.Id);
            Assert.Equal(tab.Id, Selected(view));
            Assert.NotEmpty(Shown(view));
            Assert.Equal($"http://localhost/settings?tab={tab.Id}", nav.Uri);
            Assert.True(nav.History.First().Options.ReplaceHistoryEntry);
        }
    }

    [Fact]
    public async Task TheTabInTheAddressIsTheOneOpen()
    {
        await using var host = new ComponentHost();
        var nav = host.Context.Services.GetRequiredService<NavigationManager>();
        nav.NavigateTo("settings?tab=claude");
        var view = host.Context.Render<Settings>();
        Assert.Equal("claude", Selected(view));
        nav.NavigateTo("settings?tab=app");
        view.WaitForAssertion(() => Assert.Equal(["updates", "data", "problems"], Shown(view)));
        nav.NavigateTo("settings?tab=nonsense");
        view.WaitForAssertion(() => Assert.Equal("practice", Selected(view)));
    }

    [Fact]
    public async Task SearchingAcrossTabsExpandsMatchingOptionsAndClearingRestoresTheSelectedTab()
    {
        await using var host = new ComponentHost();
        var view = host.Context.Render<Settings>();
        await Open(view, "look");
        await Search(view, "headphones");
        Assert.Empty(view.FindAll("[role=tab]"));
        Assert.Equal(["click"], Shown(view));
        await Search(view, "each pass's figure");
        Assert.Equal(["practice"], Shown(view));
        Assert.Single(view.FindAll("#practice details[open] [role=switch]"));
        await Search(view, "zzz");
        Assert.Empty(Shown(view));
        await Search(view, " ");
        Assert.Equal("look", Selected(view));
        Assert.Equal(["look", "notation"], Shown(view));
    }

    [Fact]
    public async Task PresetsChangeFeedbackWhilePreservingAutomaticRepeatAndIndependentLoopSettings()
    {
        await using var host = new ComponentHost();
        var view = host.Context.Render<Settings>();
        var options = host.Context.Services.GetRequiredService<PrimaSettings>();
        await view.Find("#playback [role=switch]").ClickAsync(new MouseEventArgs());
        await view.Find("[data-preset=hands-on]").ClickAsync(new MouseEventArgs());
        Assert.Equal(("review", true, true, true), (options.AfterRun, options.PassFigure, options.PassWaits, options.RunAgain));
        await view.Find("#practice [role=switch]").ClickAsync(new MouseEventArgs());
        Assert.Null(options.Way);
        Assert.False(options.PassFigure);
        Assert.True(options.PassWaits);
        var restored = new PrimaSettings();
        await host.Store.LoadAsync(restored);
        Assert.Equal((true, false, true), (restored.RunAgain, restored.PassFigure, restored.PassWaits));
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
            await Open(view, tab.Id);
            // The Tools tab holds the tools, a card each: each is found by its name.
            if (tab.Id == "tools")
            {
                var tools = view.FindAll("#tools .tool-card-name").Select(n => n.TextContent).ToList();
                Assert.NotEmpty(tools);
                foreach (var tool in tools)
                {
                    await Search(view, tool);
                    Assert.Contains(tool, view.FindAll("section.settings-section:not([hidden]) .tool-card-name").Select(n => n.TextContent));
                }
                continue;
            }
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

    // Porcelain's accent: offered only with Porcelain or Night, chosen with a tap, kept with the settings, and the same in Night.
    [Fact]
    public async Task PorcelainsAccentIsChosenInSettingsAndNightWearsItToo()
    {
        await using var host = new ComponentHost();
        var looks = host.Context.Services.GetRequiredService<LookSetting>();
        host.Context.Services.GetRequiredService<NavigationManager>().NavigateTo("settings?tab=look");
        static string Pressed(IRenderedComponent<Settings> view, string name) =>
            view.FindAll(".accent-choice").Single(b => b.TextContent.Trim() == name).GetAttribute("aria-pressed") ?? "";

        // Porcelain and Night are the only looks (28 November 2026), each choice drawn in its own.
        Assert.Equal([Look.Porcelain, Look.Night], Look.All);
        Assert.Equal(Look.Porcelain, Look.FromId("studio"));
        Assert.Equal(Look.Porcelain, Look.FromId("vinyl"));

        var porcelain = host.Context.Render<Settings>(p => p.AddCascadingValue(Look.Porcelain));
        Assert.Equal(Accent.All.Select(a => a.Name), porcelain.FindAll(".accent-choice").Select(b => b.TextContent.Trim()));
        Assert.Equal("true", Pressed(porcelain, "Red"));

        await porcelain.InvokeAsync(() => porcelain.FindAll(".accent-choice").Single(b => b.TextContent.Trim() == "Blue").ClickAsync(new MouseEventArgs()));

        Assert.Equal(Accent.Blue, looks.CurrentAccent);
        porcelain.WaitForAssertion(() => Assert.Equal("true", Pressed(porcelain, "Blue")));
        var saved = "";
        for (var i = 0; i < 50 && saved != "blue"; i++)
        {
            saved = (await host.Store.GetSettingsAsync()).GetValueOrDefault("LookAccent") ?? "";
            if (saved != "blue") await Task.Delay(100, Xunit.TestContext.Current.CancellationToken);
        }
        Assert.Equal("blue", saved);

        var night = host.Context.Render<Settings>(p => p.AddCascadingValue(Look.Night));
        Assert.Equal("true", Pressed(night, "Blue"));
        Assert.Equal("false", Pressed(night, "Red"));
    }

    // The stopped screen's card opens App & data, Problems brought into view.
    [Fact]
    public async Task TheProblemsLinkOpensAppAndDataWithProblemsInView()
    {
        await using var host = new ComponentHost();
        host.Problems.Keep("Error", "Prima.UI.Layout.ScreenErrorBoundary", "The screen at /library stopped: no strip");
        host.Context.Services.GetRequiredService<NavigationManager>().NavigateTo("settings?tab=app&section=problems");
        var view = host.Context.Render<Settings>();

        Assert.Equal("app", Selected(view));
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

        await view.InvokeAsync(() => view.Find("[data-setting-reset='Start over']").ClickAsync(new MouseEventArgs()));
        await view.InvokeAsync(() => view.Find("[data-action=confirm-reset]").ClickAsync(new MouseEventArgs()));

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
        host.Context.Services.GetRequiredService<StandInUpdates>().Show("1.0", 13);
        var updater = host.Context.Services.GetRequiredService<Updater>();
        host.Context.Services.GetRequiredService<NavigationManager>().NavigateTo("settings?tab=app");
        var view = host.Context.Render<Settings>();
        await view.InvokeAsync(() => view.Find("#updates .control-row > button").ClickAsync(new MouseEventArgs()));
        var install = view.WaitForElement("#updates .update-release .mu-primary:not([disabled])");
        const string withoutCopy = "#updates .update-release button:not(.mu-primary)";
        Assert.Empty(view.FindAll(withoutCopy));

        await view.InvokeAsync(() => install.ClickAsync(new MouseEventArgs()));

        var retry = view.WaitForElement(withoutCopy, TimeSpan.FromSeconds(30));
        Assert.Equal(UpdateStep.Failed, updater.Status.Step);
        Assert.Equal("No space left", updater.Status.CopyProblem);
        await view.InvokeAsync(() => retry.ClickAsync(new MouseEventArgs()));
        view.WaitForAssertion(() => Assert.Equal(UpdateStep.Installing, updater.Status.Step), TimeSpan.FromSeconds(30));
        Assert.Equal(13, updater.Status.Latest!.Build);
        Assert.Null(updater.Status.CopyProblem);
        Assert.Empty(view.FindAll(withoutCopy));
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

        await view.InvokeAsync(() => view.Find("[data-setting-reset='Start over']").ClickAsync(new MouseEventArgs()));
        await view.InvokeAsync(() => view.Find("[data-action=confirm-reset]").ClickAsync(new MouseEventArgs()));

        Assert.Empty(await host.Store.GetDaysAsync(DateOnly.MinValue));
        var kept = Assert.Single(sync.Status.Holding);
        Assert.Equal((SyncHeldFrom.Change, "Start over"), (kept.Held!.From, kept.Held.Why));
        Assert.Equal(1, kept.Manifest.Rows("prima.db", "PracticeDay"));
        // Prima went on to its welcome; back in Your data, the copy is there.
        Assert.Equal("http://localhost/welcome", nav.Uri);
        nav.NavigateTo("settings?tab=app");
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
        await view.InvokeAsync(() => view.Find("[data-setting-reset='Start over']").ClickAsync(new MouseEventArgs()));

        await view.InvokeAsync(() => view.Find("[data-action=confirm-reset]").ClickAsync(new MouseEventArgs()));

        Assert.Single(await host.Store.GetDaysAsync(DateOnly.MinValue));
        Assert.Equal("http://localhost/settings?tab=app", nav.Uri);

        await view.InvokeAsync(() => view.Find("[data-action=reset-without-backup]").ClickAsync(new MouseEventArgs()));

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
        host.Context.Services.GetRequiredService<LookSetting>().Choose(Look.Night);
        // The playing: levels and tracks, a note card and its answer, a passage, a practice day, a routine followed today and a
        // request, a sight-reading passage from the piece read, a session on the piece yesterday whose run left something to do
        // next, and the piece's place, tempo and hand.
        await host.Store.SaveTrackAsync("reading", Prima.Core.Practice.ReadingState.Start with { Level = 3 });
        var mine = RoutineStoreTests.Own("Leaps", "octave-jump");
        await host.Store.SaveOwnRoutineAsync(mine);
        await host.Store.SaveTrackAsync("routine-stars", new[] { mine.Revision });
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
        var history = host.Context.Render<Pages.PracticeHistory>(p => p.Add(c => c.Id, piece.Id));
        history.WaitForElement($"[data-session='{session}']");
        nav.NavigateTo("settings?tab=app");
        var view = host.Context.Render<Settings>();

        await view.InvokeAsync(() => view.Find("[data-setting-reset='Start over']").ClickAsync(new MouseEventArgs()));
        await view.InvokeAsync(() => view.Find("[data-action=confirm-reset]").ClickAsync(new MouseEventArgs()));

        // The playing has gone.
        Assert.Null(await host.Store.GetTrackAsync<Prima.Core.Practice.ReadingState>("reading"));
        Assert.Null(await host.Store.GetTrackAsync<string[]>("routine-stars"));
        Assert.Empty(await host.Store.GetRoutineDefinitionsAsync());
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
        Assert.Equal("porcelain-night", saved["Look"]);
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
    private static Task Open(IRenderedComponent<Settings> view, string tab) => view.InvokeAsync(() => view.Find($"#settings-tab-{tab}").Click());

    private static Task Search(IRenderedComponent<Settings> view, string words) => view.InvokeAsync(() => view.Find("input.settings-search").Input(words));

    private static string? Selected(IRenderedComponent<Settings> view) => view.FindAll("[role=tab][aria-selected=true]").SingleOrDefault()?.Id?.Replace("settings-tab-", "");

    private static string[] Shown(IRenderedComponent<Settings> view) => [.. view.FindAll("section.settings-section:not([hidden])").Select(section => section.Id!)];

    private static IElement Choice(IRenderedComponent<Settings> view, string words) => view.FindAll("button.mu-toggle").Single(b => Words(b.TextContent) == words);

    private static string Label(IElement row) => Words(row.QuerySelector(".control-label")?.TextContent ?? "");

    // A row's buttons and links, by their words, without an ellipsis ("Start over…").
    private static string[] Choices(IElement row) => [.. row.QuerySelectorAll("button, a").Select(b => Words(b.TextContent).TrimEnd('…'))];

    private static string Words(string text) => Regex.Replace(text, @"\s+", " ").Trim();
}
