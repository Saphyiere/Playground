using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Prima.Core.Generation;
using Prima.UI.Components;
using Prima.UI.Pages;
using Prima.UI.Practice;

namespace Prima.UI.Tests;

public class ExerciseFamilyUiTests
{
    // The keys the page's music starts with: its first moment's.
    private static int[] Opening(IRenderedComponent<ExercisePractice> page)
    {
        var notes = page.FindComponent<PlayAlong>().Instance.Music!.Notes;
        return [.. notes.Where(n => n.Tick == notes[0].Tick).Select(n => n.Midi)];
    }

    private static Task Tap<T>(IRenderedComponent<T> page, string selector) where T : IComponent => page.InvokeAsync(() => page.Find(selector).ClickAsync(new MouseEventArgs()));

    [Fact]
    public async Task AFamilysVariationsAreChosenOnItsPageAndKeptWithItsKeyAndHands()
    {
        await using var host = new ComponentHost();
        var page = host.Context.Render<ExercisePractice>(p => p.Add(c => c.Id, "bounce"));

        // It opens as the main exercise: sixths, two quick notes. More than three options are stepped through; fewer are keys.
        page.WaitForAssertion(() => Assert.Equal([64, 72], Opening(page)));
        Assert.Equal(["interval", "quick"], page.FindAll("[data-variation]").Select(r => r.GetAttribute("data-variation")));
        Assert.Equal("Sixths", page.Find("[data-variation=interval] .exercise-option").TextContent);
        Assert.Empty(page.FindAll("[data-variation] .mu-toggle"));

        await Tap(page, "[data-variation=interval] button[aria-label='Interval: the next']");
        page.WaitForAssertion(() => Assert.Equal([60, 72], Opening(page)));
        Assert.Equal("Octaves", page.Find("[data-variation=interval] .exercise-option").TextContent);
        // The last option has nothing after it.
        Assert.True(page.Find("[data-variation=interval] button[aria-label='Interval: the next']").HasAttribute("disabled"));
        await Tap(page, "[data-variation=quick] button[aria-label='Quick notes: the one before']");
        page.WaitForAssertion(() => Assert.Equal("One", page.Find("[data-variation=quick] .exercise-option").TextContent));
        // One quick note comes after half a beat's rest.
        Assert.Equal(240, page.FindComponent<PlayAlong>().Instance.Music!.Notes[0].Tick);

        var kept = await host.Store.GetTrackAsync<ExerciseChoice>("exercise:bounce");
        Assert.Equal("interval=octave;quick=1", kept!.Variation);
        Assert.Equal((0, HandUse.Right), (kept.Fifths, kept.Hands));

        // The key and the hands keep the variation, and it opens that way the next time.
        await Tap(page, ".exercise-choice button[aria-label='A harder key']");
        page.WaitForAssertion(() => Assert.Equal("G major", page.Find(".exercise-key").TextContent));
        Assert.Equal("interval=octave;quick=1", (await host.Store.GetTrackAsync<ExerciseChoice>("exercise:bounce"))!.Variation);
        var again = host.Context.Render<ExercisePractice>(p => p.Add(c => c.Id, "bounce"));
        again.WaitForAssertion(() => Assert.Equal("Octaves", again.Find("[data-variation=interval] .exercise-option").TextContent));
        Assert.Equal("G major", again.Find(".exercise-key").TextContent);
        Assert.Equal([67, 79], Opening(again));
        // The lesson its idea is from, and what it trains, are among its words: the play bar's Options shows them in the music's place.
        Assert.Empty(again.FindAll(".exercise-words"));
        await Tap(again, ".play-bar [aria-label='Options']");
        again.WaitForElement(".piece-options.exercise-words");
        Assert.Contains("piece-frame-hidden", again.Find(".piece-frame").ClassList);
        Assert.StartsWith("https://www.youtube.com/watch?v=8QI_InPpq0s", again.Find(".exercise-lesson a").GetAttribute("href"));
        Assert.Equal("Trains arm and wrist, double notes.", again.Find(".exercise-trains").TextContent);
    }

    [Fact]
    public async Task AVariationForTwoHandsIsOfferedOnlyWithBoth()
    {
        await using var host = new ComponentHost();
        var page = host.Context.Render<ExercisePractice>(p => p.Add(c => c.Id, "thirds"));
        page.WaitForAssertion(() => Assert.Equal([60, 64], Opening(page)));
        // Two options are keys to press; how the hands move against each other isn't asked of one hand.
        Assert.Equal(["where"], page.FindAll("[data-variation]").Select(r => r.GetAttribute("data-variation")));
        Assert.Equal(["In one place", "Up the scale"], page.FindAll("[data-variation=where] .mu-toggle").Select(b => b.TextContent));

        // Together, the third of the hands (the key's row has a key of its own now, Through the keys).
        await page.InvokeAsync(() => page.FindAll(".exercise-choice .mu-toggle").Single(b => b.TextContent == "Together").ClickAsync(new MouseEventArgs()));
        page.WaitForAssertion(() => Assert.Equal(["where", "motion"], page.FindAll("[data-variation]").Select(r => r.GetAttribute("data-variation"))));
        Assert.Equal([48, 52, 60, 64], Opening(page));
        await Tap(page, "[data-variation=motion] .mu-toggle:nth-of-type(2)");
        // In mirror the left hand starts from its upper third.
        page.WaitForAssertion(() => Assert.Equal([52, 55, 60, 64], Opening(page)));
        Assert.Equal("where=place;motion=mirror", (await host.Store.GetTrackAsync<ExerciseChoice>("exercise:thirds"))!.Variation);
    }

    [Fact]
    public async Task AnExerciseThatBecameAVariationOpensAsItWasAndAPlainOneHasNoVariations()
    {
        await using var host = new ComponentHost();
        // Thumb under four fingers is the thumb family at four now; its own address still opens it.
        var four = host.Context.Render<ExercisePractice>(p => p.Add(c => c.Id, "thumb-four"));
        four.WaitForAssertion(() => Assert.Equal("4 fingers", four.Find("[data-variation=under] .exercise-option").TextContent));
        Assert.Equal("Thumb under", four.Find("h1").TextContent);
        Assert.Equal([60, 62, 64, 65, 67], four.FindComponent<PlayAlong>().Instance.Music!.Notes.Take(5).Select(n => n.Midi));

        var three = host.Context.Render<ExercisePractice>(p => p.Add(c => c.Id, "thumb-three"));
        three.WaitForAssertion(() => Assert.Equal("3 fingers", three.Find("[data-variation=under] .exercise-option").TextContent));

        var plain = host.Context.Render<ExercisePractice>(p => p.Add(c => c.Id, "five"));
        plain.WaitForAssertion(() => Assert.Equal([60], Opening(plain)));
        Assert.Empty(plain.FindAll("[data-variation]"));
        await Tap(plain, ".play-bar [aria-label='Options']");
        plain.WaitForElement(".exercise-words");
        Assert.Empty(plain.FindAll(".exercise-lesson"));
        Assert.Equal("Trains finger strength, evenness.", plain.Find(".exercise-trains").TextContent);
    }

    [Fact]
    public async Task TheCatalogueListsByTypeTrainsAndLevelAsTheLibraryDoesWithACardForEach()
    {
        await using var host = new ComponentHost();
        var navigation = host.Context.Services.GetRequiredService<NavigationManager>();
        var list = host.Context.Render<ExerciseList>();
        list.WaitForElement(".lib-list[aria-busy='false']");

        // It opens on the ones to start with, as cards, each with its shape, its name and when it was done.
        Assert.Equal("Start here", list.Find(".lib-main-head .col-head").TextContent);
        var first = list.Find(".ex-card");
        Assert.NotEmpty(first.QuerySelectorAll("svg.ex-shape rect"));
        Assert.Equal("Not tried", first.QuerySelector(".exercise-done")!.TextContent);

        // The catalogue's types down the left, as the Library's composers: the drills first, then the rest to play, then to read.
        var types = list.FindAll(".lib-list .col-item span:first-child").Select(s => s.TextContent).ToList();
        Assert.Equal(["Start here", "Not tried yet", "Played lately", "Drills", "Arm and wrist", "Five fingers", "Scales", "Broken chords", "Chords", "Jumps", "Thumb under", "Trills", "Double notes", "Two hands", "Touch", "Pedal", "Sitting", "Warm-ups", "Ways to practice"], types);

        // The drills are a type of their own: every drill, the reading drills and Scales, Arpeggios and Finger independence.
        navigation.NavigateTo("exercises?type=drills");
        var drills = host.Context.Render<ExerciseList>();
        drills.WaitForElement(".lib-list[aria-busy='false']");
        Assert.Equal(Exercises.All.Where(e => e.Kind == ExerciseKind.Drill).Select(e => e.Name), drills.FindAll(".ex-card-name").Select(n => n.TextContent));
        Assert.Contains("Notes", drills.FindAll(".ex-card-name").Select(n => n.TextContent));
        Assert.Equal("drills/notes", drills.FindAll(".ex-card").First(c => c.QuerySelector(".ex-card-name")!.TextContent == "Notes").GetAttribute("href"));
        Assert.All(drills.FindAll(".ex-card"), card => Assert.Contains("Drill", card.QuerySelectorAll(".piece-tag").Select(t => t.TextContent)));

        // A family is tagged as one that varies.
        navigation.NavigateTo("exercises?type=double");
        var doubles = host.Context.Render<ExerciseList>();
        doubles.WaitForElement(".lib-list[aria-busy='false']");
        Assert.Equal(["Thirds, three up and back", "Bounced double notes", "Scale in octaves"], doubles.FindAll(".ex-card-name").Select(n => n.TextContent));
        Assert.All(doubles.FindAll(".ex-card"), card => Assert.Contains("Variations", card.QuerySelectorAll(".piece-tag").Select(t => t.TextContent)));

        // Listed by what they train, as the Library's by kinds: the focus chosen, and the list beside it kept that way.
        navigation.NavigateTo("exercises?trains=wrist");
        var wrist = host.Context.Render<ExerciseList>();
        wrist.WaitForElement(".lib-list[aria-busy='false']");
        Assert.Equal("true", wrist.Find(".col-by a[aria-current]").TextContent == "Trains" ? "true" : "false");
        Assert.Equal(["Drop onto one note", "Wrist circles on a held note", "Bounced double notes", "Scale in octaves", "Two-note slurs: down, then up", "Two-note slurs up the scale"], wrist.FindAll(".ex-card-name").Select(t => t.TextContent));
        Assert.StartsWith("Arm and wrist", wrist.Find(".lib-main-head .col-head").TextContent);
        Assert.Equal(ExerciseFocus.All.Select(f => f.Name), wrist.FindAll(".lib-list .col-item span:first-child").Select(s => s.TextContent).Skip(3));

        // The search looks across them all.
        await wrist.InvokeAsync(() => wrist.Find(".head-search").InputAsync(new ChangeEventArgs { Value = "pedal" }));
        wrist.WaitForAssertion(() => Assert.Equal("Found for “pedal”", wrist.Find(".lib-main-head .col-head").TextContent));
        Assert.Contains("Change the pedal on each chord", wrist.FindAll(".ex-card-name").Select(t => t.TextContent));
    }
}
