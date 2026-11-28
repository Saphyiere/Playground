using Bunit;
using Bunit.TestDoubles;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Playground.Kit;

namespace Prima.UI.Tests;

// The owner's decisions for the rail, 17 November: "Routines is part of practice. It's not really a collection thing." Tools,
// "you're not really going to it too much. Just put it in settings and then have it as another tab." You, "colour plus the
// initial", with "a little dot on the bottom of it showing if the keyboard is plugged in or not ... We don't need the whole plug
// in your keyboard", and after seeing it beside Settings: "maybe at the very top and not next to settings. So above practice."
// Collection keeps Library, and has the Repertoire beside it: "We have a repertoire tab ... we don't need to put the repertoire in
// the library" (the same day, on the Library's mock-ups).
public class RailTests
{
    [Fact]
    public async Task YouHeadsTheRailRoutinesIsUnderPracticeTheCollectionHoldsTheLibraryAndTheRepertoireAndSettingsIsAloneAtTheFoot()
    {
        var piano = new TestPiano(plugged: false);
        await using var host = new ComponentHost(keyboard: piano);
        host.Context.Services.GetRequiredService<PrimaSettings>().Name = "sam";

        var rail = host.Context.Render<Layout.Rail>();

        Assert.Equal(["Practice", "Collection"], rail.FindAll(".mu-nav-group").Select(g => g.TextContent));
        // The drills are among the exercises (24 November 2026): no key of their own.
        Assert.Equal(["Today", "Sight-read", "Exercises", "Routines", "Library", "Repertoire", "Settings"],
            rail.FindAll(".mu-nav-item").Select(k => k.QuerySelectorAll("span").Last().TextContent));
        // Vinyl's tracklist codes (A1, B2) went with Vinyl (28 November 2026).
        Assert.Empty(rail.FindAll(".mu-nav-code"));
        Assert.Equal("repertoire", rail.FindAll(".mu-nav-item").Single(k => k.TextContent.EndsWith("Repertoire", StringComparison.Ordinal)).GetAttribute("href"));
        var nav = rail.Find("nav.rail");
        Assert.Equal("Settings", nav.LastElementChild!.TextContent.Trim());
        Assert.Contains("mu-nav-end", nav.LastElementChild.ClassList);
        Assert.DoesNotContain(rail.FindAll("a"), a => a.GetAttribute("href") == "tools");
        Assert.DoesNotContain("Plug in your keyboard", rail.Markup, StringComparison.Ordinal);

        // You: first, above Practice, a round key with the name's first letter, opening You, its lamp hollow with no keyboard.
        Assert.Contains("rail-you", nav.FirstElementChild!.ClassList);
        var you = rail.Find(".rail-you");
        Assert.Equal("you", you.GetAttribute("href"));
        Assert.Equal("S", you.QuerySelector(".rail-you-initial")!.TextContent);
        Assert.Equal("off", you.GetAttribute("data-keyboard"));
        Assert.Equal("You. No keyboard is plugged in.", you.GetAttribute("aria-label"));

        piano.Plug();

        rail.WaitForAssertion(() => Assert.Equal("live", rail.Find(".rail-you").GetAttribute("data-keyboard")));
        Assert.Equal("You. P-225B is plugged in.", rail.Find(".rail-you").GetAttribute("aria-label"));
    }

    [Fact]
    public async Task WithoutANameYouIsAFigure()
    {
        await using var host = new ComponentHost();

        var rail = host.Context.Render<Layout.Rail>();

        Assert.Empty(rail.FindAll(".rail-you-initial"));
        Assert.Single(rail.FindAll(".rail-you svg.rail-you-figure"));
    }

    [Fact]
    public async Task TheSettingsKeyIsLitOnSettingsAndOnTheToolsItHolds()
    {
        await using var host = new ComponentHost();
        var nav = host.Context.Services.GetRequiredService<NavigationManager>();
        var rail = host.Context.Render<Layout.Rail>();

        foreach (var (at, lit) in new[] { ("settings?tab=tools", true), ("tools/metronome", true), ("tools/timing?from=welcome", true), ("glossary", true), ("library", false), ("routine", false) })
        {
            nav.NavigateTo(at);
            rail.WaitForAssertion(() => Assert.Equal(lit, rail.Find(".mu-nav-end").HasAttribute("aria-current")));
        }
        // Routines, under Practice, is lit on a routine's screens.
        Assert.True(rail.FindAll(".mu-nav-item").Single(k => k.TextContent.EndsWith("Routines", StringComparison.Ordinal)).HasAttribute("aria-current"));
    }

    [Fact]
    public async Task ToolsIsATabOfSettingsAndWhatOpenedToolsOpensIt()
    {
        await using var host = new ComponentHost();
        var nav = (BunitNavigationManager)host.Context.Services.GetRequiredService<NavigationManager>();
        host.Context.Services.GetRequiredService<AppNavigation>();

        // The old Tools screen's address opens the tab, in its place.
        nav.NavigateTo("tools");
        host.Context.Render<Pages.Tools>();
        Assert.Equal("http://localhost/settings?tab=tools", nav.Uri);
        Assert.True(nav.History.First().Options.ReplaceHistoryEntry);

        var settings = host.Context.Render<Pages.Settings>();
        settings.WaitForAssertion(() => Assert.Equal("settings-tab-tools", settings.Find("[role=tab][aria-selected=true]").Id));
        Assert.Equal(["Glossary", "Metronome", "Keyboard check", "Timing"], settings.FindAll("#tools .tool-card-name").Select(n => n.TextContent));
        Assert.Equal(["glossary", "tools/metronome", "tools/keyboard", "tools/timing"], settings.FindAll("#tools .tool-card").Select(a => a.GetAttribute("href")));

        // Settings' search finds a tool by its name, under the tab's.
        await settings.InvokeAsync(() => settings.Find("input[type=search]").InputAsync(new ChangeEventArgs { Value = "metronome" }));

        settings.WaitForAssertion(() => Assert.Equal(["Metronome"], settings.FindAll("#tools .tool-card-name").Select(n => n.TextContent)));
        Assert.False(settings.Find("#tools").HasAttribute("hidden"));
    }
}
