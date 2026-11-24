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
        // The lesson its idea is from, and what it trains, are under its words.
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
        Assert.Empty(plain.FindAll(".exercise-lesson"));
        Assert.Equal("Trains finger strength, evenness.", plain.Find(".exercise-trains").TextContent);
    }

    [Fact]
    public async Task TheListMarksFamiliesAndNarrowsToWhatAnExerciseTrains()
    {
        await using var host = new ComponentHost();
        var navigation = host.Context.Services.GetRequiredService<NavigationManager>();
        var list = host.Context.Render<ExerciseList>();
        list.WaitForElement(".exercise-filters[aria-busy='false']");
        // Every family is in the list to play, in its section, marked as one that varies.
        var varied = list.FindAll(".piece-row").Where(r => r.QuerySelectorAll(".piece-tag").Any(t => t.TextContent == "Variations")).Select(r => r.QuerySelector(".piece-title")!.TextContent).ToList();
        Assert.Equal(ExerciseFamilies.All.Select(e => e.Name).Order(), varied.Order());
        Assert.Contains("Double notes", list.FindAll(".section-title").Select(h => h.TextContent));
        Assert.Equal(["Trains anything", .. ExerciseFocus.All.Select(f => f.Name)], list.FindAll(".exercise-focus option").Select(o => o.TextContent));

        // Choosing a focus puts it in the address, as the list showing is.
        await list.InvokeAsync(() => list.Find(".exercise-focus").ChangeAsync(new ChangeEventArgs { Value = "wrist" }));
        Assert.EndsWith("exercises?trains=wrist", navigation.Uri);
        navigation.NavigateTo("exercises?show=new&trains=wrist");
        var narrowed = host.Context.Render<ExerciseList>();
        narrowed.WaitForElement(".exercise-filters[aria-busy='false']");
        Assert.Equal(["Drop onto one note", "Wrist circles on a held note", "Bounced double notes", "Scale in octaves", "Two-note slurs: down, then up", "Two-note slurs up the scale"], narrowed.FindAll(".piece-title").Select(t => t.TextContent));
        Assert.StartsWith("Arm and wrist.", narrowed.Find(".exercise-focus-note").TextContent);
        // The lists' keys keep the focus.
        await narrowed.InvokeAsync(() => narrowed.Find(".exercise-filters .mu-toggle").ClickAsync(new MouseEventArgs()));
        Assert.EndsWith("exercises?trains=wrist", navigation.Uri);
    }
}
