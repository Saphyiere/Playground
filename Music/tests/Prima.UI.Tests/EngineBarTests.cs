using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Prima.Core.Generation;
using Prima.UI.Components;
using Prima.UI.Pages;
using Prima.UI.Practice;

namespace Prima.UI.Tests;

/// <summary>
/// The practice screen: one for every screen that plays music, top to bottom the head, the music, a run's review, the play bar
/// (PlayAlong) and the foot, the bar's keys in the same places everywhere. The owner, 28 November 2026: "we have practices and
/// exercises, and the issue is that they do the exact same thing", "we need to go for simple first, and then have the ability
/// to do the advanced stuff in like a very clean, sleek way", "a left, right, and both, so that you can just click the one you
/// want", and on the wireframes' C, the bar under the music: "the change, the next, the practice ... the one practice screen
/// for all of it".
/// </summary>
public class EngineBarTests
{
    // What every run needs, in its order on the bar, after More: the hands, the mode, the tempo, Loop, Listen and Play.
    private static readonly string[] Run = ["[data-action=more]", "[data-hand=left]", "[data-hand=right]", "[data-hand=both]", "[data-mode=time]", "[data-mode=wait]", ".play-tempo-figure", ".play-loop button", ".play-listen button", "[data-piano=go]"];

    private static void AssertOrder<T>(IRenderedComponent<T> view, params string[] selectors) where T : IComponent
    {
        var keys = view.Find(".play-bar").QuerySelectorAll("button").ToArray();
        var previous = -1;
        foreach (var selector in selectors)
        {
            var index = Array.FindIndex(keys, key => key.Matches(selector));
            Assert.True(index > previous, $"{selector} is missing or out of order.");
            previous = index;
        }
    }

    // The screen's zones as they come down it: the head, the music, the review (after a run), the bar, the foot.
    private static string[] Zones<T>(IRenderedComponent<T> view) where T : IComponent =>
        [.. view.FindAll(".page-head, .piece-stage, .summary, .play-bar, .play-foot").Select(e => e.ClassList.Contains("page-head") ? "head" : e.ClassList.Contains("piece-stage") ? "music" : e.ClassList.Contains("summary") ? "review" : e.ClassList.Contains("play-bar") ? "bar" : "foot")];

    private static Task Tap<T>(IRenderedComponent<T> view, string selector) where T : IComponent => view.InvokeAsync(() => view.Find(selector).ClickAsync(new MouseEventArgs()));

    [Fact]
    public async Task A_piece_an_exercise_and_a_technique_drill_are_the_one_screen_with_the_one_bar()
    {
        await using var host = new ComponentHost(keyboard: new TestPiano(plugged: true));
        var piece = await host.AddPiece();
        await host.Store.StartSessionAsync(piece.Id, "custom", DateTimeOffset.Now);
        var pieceView = host.Context.Render<Piece>(p => p.Add(c => c.Id, piece.Id));
        pieceView.WaitForElement(".pause-keys .mu-primary");
        await pieceView.InvokeAsync(() => pieceView.Find(".pause-keys .mu-primary").ClickAsync(new MouseEventArgs()));
        pieceView.WaitForElement(".play-bar [data-piano=go]");
        // The test piece is for one staff, so it has no hands to choose; the rest are the exercise's.
        Assert.Empty(pieceView.FindAll(".play-bar .hands-choice"));
        AssertOrder(pieceView, [.. Run.Where(key => !key.Contains("data-hand"))]);
        Assert.Equal(["head", "music", "bar", "foot"], Zones(pieceView));
        // A piece's foot: its bars, and the session's clocks with Finish practice.
        Assert.NotNull(pieceView.Find(".play-foot .bar-strip"));
        Assert.NotNull(pieceView.Find(".play-foot .practice-strip"));

        var exercise = host.Context.Render<ExercisePractice>(p => p.Add(c => c.Id, "five"));
        exercise.WaitForAssertion(() => Assert.NotNull(exercise.FindComponent<PlayAlong>().Instance.Music));
        AssertOrder(exercise, Run);
        Assert.Equal(["head", "music", "bar", "foot"], Zones(exercise));
        // An exercise's foot: what's chosen on its card with Change, and the next one in its section.
        Assert.NotNull(exercise.Find(".play-foot .choice-card .choice-change"));
        Assert.NotNull(exercise.Find(".play-foot [data-action=exercise-next]"));
        exercise.Dispose();

        host.Context.Services.GetRequiredService<NavigationManager>().NavigateTo("drills/scales");
        var scales = host.Context.Render<TechniquePractice>();
        scales.WaitForAssertion(() => Assert.NotNull(scales.FindComponent<PlayAlong>().Instance.Music));
        AssertOrder(scales, Run);
        Assert.Equal(["head", "music", "bar", "foot"], Zones(scales));
        // Play as everywhere, not Run.
        Assert.Equal("Play", scales.Find(".play-bar [data-piano=go]").FirstChild!.TextContent.Trim());
        scales.Dispose();

        // An exercise draws its music in a piece's paper frame, the way a piece's is drawn.
        Assert.NotEmpty(pieceView.FindAll(".piece-frame.sheet-frame"));
        var again = host.Context.Render<ExercisePractice>(p => p.Add(c => c.Id, "five"));
        again.WaitForElement(".play-bar [data-piano=go]");
        Assert.Equal("scroll", again.FindComponent<Sheet>().Instance.Layout);
        Assert.NotNull(again.Find(".practice-page .piece-frame.sheet-frame.piece-frame-scroll[data-follow]"));
    }

    [Fact]
    public async Task A_runs_review_is_under_the_music_and_over_the_bar()
    {
        await using var host = new ComponentHost(keyboard: new TestPiano(plugged: true));
        var view = host.Context.Render<ExercisePractice>(p => p.Add(c => c.Id, "five"));
        view.WaitForAssertion(() => Assert.NotNull(view.FindComponent<PlayAlong>().Instance.Music));
        var bar = view.FindComponent<PlayAlong>().Instance;
        await view.InvokeAsync(() => bar.Start());
        view.WaitForAssertion(() => Assert.True(bar.Running));
        await view.InvokeAsync(bar.Stop);
        view.WaitForElement(".summary");
        Assert.Equal(["head", "music", "review", "bar", "foot"], Zones(view));
    }

    [Fact]
    public async Task The_hands_are_one_tap_each_left_right_or_both()
    {
        await using var host = new ComponentHost(keyboard: new TestPiano(plugged: true));
        var view = host.Context.Render<ExercisePractice>(p => p.Add(c => c.Id, "five"));
        // Opened, as it was last left, before anything's tapped.
        view.WaitForAssertion(() => Assert.NotNull(view.FindComponent<PlayAlong>().Instance.Music));
        view.WaitForElement(".play-bar .hands-choice");
        string Lit() => view.Find(".hands-choice [aria-checked=true]").GetAttribute("data-hand")!;
        var right = Lit();

        // One tap is that hand alone, or both: never two taps, whichever is lit.
        await Tap(view, ".hands-choice [data-hand=left]");
        view.WaitForAssertion(() => Assert.Equal("left", Lit()));
        Assert.Single(view.FindAll(".hands-choice [aria-checked=true]"));
        await Tap(view, ".hands-choice [data-hand=both]");
        view.WaitForAssertion(() => Assert.Equal("both", Lit()));
        await Tap(view, ".hands-choice [data-hand=right]");
        view.WaitForAssertion(() => Assert.Equal("right", Lit()));
        Assert.Equal(["Left", "Right", "Both"], view.FindAll(".hands-choice .choice-word").Select(w => w.TextContent));

        // Kept with the exercise, and the music is the hand's: both hands' notes on two staves.
        var kept = await host.Store.GetTrackAsync<ExerciseChoice>("exercise:five");
        for (var i = 0; i < 50 && kept?.Hands != HandUse.Right; i++)
        {
            await Task.Delay(100, Xunit.TestContext.Current.CancellationToken);
            kept = await host.Store.GetTrackAsync<ExerciseChoice>("exercise:five");
        }
        Assert.Equal(HandUse.Right, kept!.Hands);
        await Tap(view, ".hands-choice [data-hand=both]");
        view.WaitForAssertion(() => Assert.True(view.FindComponent<PlayAlong>().Instance.Music!.Notes.Select(n => n.Staff).Distinct().Count() == 2));
        // Change has the key and the variations; the hands are the bar's.
        await Choosing.OpenChange(view);
        Assert.Empty(view.FindAll(".change-panel .hands-choice"));
        Assert.NotNull(right);
    }

    [Fact]
    public async Task The_heads_key_takes_a_screen_full_screen_and_the_bars_brings_it_back()
    {
        await using var host = new ComponentHost(keyboard: new TestPiano(plugged: true));
        var view = host.Context.Render<ExercisePractice>(p => p.Add(c => c.Id, "five"));
        view.WaitForAssertion(() => Assert.NotNull(view.FindComponent<PlayAlong>().Instance.Music));
        view.WaitForElement(".page-head [aria-label='Full screen']");
        // What isn't the music, the review or the bar is marked to go: the head and the foot.
        Assert.NotEmpty(view.FindAll("header.page-head.full-hide"));
        Assert.NotEmpty(view.FindAll("footer.play-foot.full-hide"));
        Assert.Empty(view.FindAll(".play-bar [data-action=fullscreen]"));

        await Tap(view, ".page-head [aria-label='Full screen']");
        view.WaitForAssertion(() => Assert.Equal([true], host.Browser.Fullscreen));
        Assert.Equal([false], host.Bars.Shown);
        // The head is gone in full screen, so the way out is on the bar.
        view.WaitForElement(".play-bar [aria-label='Leave full screen']");
        Assert.Equal("true", view.Find(".play-bar [aria-label='Leave full screen']").GetAttribute("aria-pressed"));

        await Tap(view, ".play-bar [aria-label='Leave full screen']");
        view.WaitForAssertion(() => Assert.Equal([true, false], host.Browser.Fullscreen));
        view.WaitForAssertion(() => Assert.Empty(view.FindAll(".play-bar [data-action=fullscreen]")));

        // On again, and the screen left: the next has its rail and head back.
        await Tap(view, ".page-head [aria-label='Full screen']");
        view.WaitForAssertion(() => Assert.Equal([true, false, true], host.Browser.Fullscreen));
        var key = view.FindComponents<FullScreenKey>().First().Instance;
        await view.InvokeAsync(key.Dispose);
        await Task.Delay(100, Xunit.TestContext.Current.CancellationToken);
        Assert.Equal([true, false, true, false], host.Browser.Fullscreen);
        Assert.Equal([false, true, false, true], host.Bars.Shown);
    }

    [Fact]
    public async Task More_holds_the_click_and_the_music_and_the_tempo_holds_the_musics_own()
    {
        await using var host = new ComponentHost(keyboard: new TestPiano(plugged: true));
        var view = host.Context.Render<ExercisePractice>(p => p.Add(c => c.Id, "five"));
        view.WaitForAssertion(() => Assert.NotNull(view.FindComponent<PlayAlong>().Instance.Music));
        view.WaitForElement(".play-bar [data-action=more]");
        // Simple first: the bar has no click, no settings and no figures of its own.
        Assert.Empty(view.FindAll(".play-bar .play-more"));
        Assert.Empty(view.FindAll(".play-bar .play-click-alone"));

        await Tap(view, "[data-action=more]");
        var more = view.WaitForElement(".play-more");
        Assert.Equal(["The click", "The music"], more.QuerySelectorAll(".more-head").Select(h => h.TextContent));
        Assert.Equal(["The click alone", "Tablet", "Piano", "Wood", "Beep", "Show cursor", "Mark notes as I play", "Done"], more.QuerySelectorAll("button").Select(b => b.TextContent.Trim()));

        // The click alone, lit on More's key while it goes, stopped by a run.
        await Tap(view, ".play-click-alone");
        view.WaitForAssertion(() => Assert.NotNull(view.Find(".play-more-key .play-more-lamp")));
        await Tap(view, ".play-more .change-done");
        view.WaitForAssertion(() => Assert.Empty(view.FindAll(".play-more")));

        // The exercise's own tempo is with the tempo's − and + once it's been changed.
        await view.InvokeAsync(() => view.FindComponent<PlayAlong>().Instance.TempoChanged.InvokeAsync(90));
        await Tap(view, ".play-tempo-figure");
        var original = view.WaitForElement(".tempo-pop .play-tempo-original");
        Assert.StartsWith("Original", original.TextContent.Trim());
    }

    [Fact]
    public async Task About_shows_an_exercises_words_in_the_musics_place()
    {
        await using var host = new ComponentHost(keyboard: new TestPiano(plugged: true));
        var view = host.Context.Render<ExercisePractice>(p => p.Add(c => c.Id, "five"));
        view.WaitForAssertion(() => Assert.NotNull(view.FindComponent<PlayAlong>().Instance.Music));
        view.WaitForElement(".page-head [data-action=about]");
        Assert.Empty(view.FindAll(".exercise-words"));
        await Tap(view, ".page-head [data-action=about]");
        view.WaitForElement(".piece-stage .exercise-words");
        Assert.Contains("piece-frame-hidden", view.Find(".piece-frame").ClassList);
        Assert.Equal("true", view.Find(".page-head [data-action=about]").GetAttribute("aria-pressed"));
        await Tap(view, ".page-head [data-action=about]");
        view.WaitForAssertion(() => Assert.Empty(view.FindAll(".exercise-words")));
    }
}
