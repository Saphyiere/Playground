using System.Text.RegularExpressions;
using AngleSharp.Dom;
using Bunit;
using Bunit.TestDoubles;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
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
        Assert.Equal("problems", await host.Browser.SectionShown.Task.WaitAsync(TimeSpan.FromSeconds(10)));
        Assert.Equal(["problems"], host.Browser.ShownSections);
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
