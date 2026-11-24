using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Prima.UI.Components;
using Prima.UI.Pages;

namespace Prima.UI.Tests;

/// <summary>
/// The engine's toolbar (PlayAlong, its tools PlayTools): one bar for every screen that plays music, the same keys in the same
/// places. The owner, 24 November 2026: "If I go to the library and then I go to exercises and the buttons are all this different
/// places, that doesn't make any sense", "If I want a metronome to do the exercises, I should be able to have it", "same thing
/// for full screen". A screen says which keys it has; the bar draws them, always in this order.
/// </summary>
public class EngineBarTests
{
    // The bar's keys in order: each by its label, or its words.
    private static List<string> Keys<T>(IRenderedComponent<T> view) where T : IComponent =>
        [.. view.Find(".play-bar").QuerySelectorAll("button").Select(b => b.GetAttribute("aria-label") is { Length: > 0 } label ? label : b.TextContent.Trim())];

    // The keys every bar shares, in the order every bar has them: the tools (the metronome's settings, full screen), then the run's
    // (the metronome, the tempo, the mode, the start key).
    private static void InTheEnginesOrder(List<string> keys, string start)
    {
        string[] order = ["Metronome", "Full screen", "Start the metronome", "Tempo", "In time", "Wait for me", start];
        var at = order.Select(k => keys.FindIndex(key => key == k || k == "Tempo" && key.StartsWith("Tempo ", StringComparison.Ordinal) || k == "Full screen" && key == "Leave full screen")).ToList();
        Assert.All(at, i => Assert.True(i >= 0, $"Missing from the bar: {string.Join(", ", order.Where((_, n) => at[n] < 0))}. It has {string.Join(", ", keys)}."));
        Assert.True(at.SequenceEqual(at.Order()), $"Out of order: {string.Join(", ", keys)}.");
    }

    [Fact]
    public async Task A_piece_an_exercise_a_technique_drill_and_a_routines_activity_have_the_one_bar()
    {
        await using var host = new ComponentHost(keyboard: new TestPiano(plugged: true));
        var piece = await host.AddPiece();
        await host.Store.StartSessionAsync(piece.Id, "custom", DateTimeOffset.Now);
        var pieceView = host.Context.Render<Piece>(p => p.Add(c => c.Id, piece.Id));
        pieceView.WaitForElement(".pause-keys .mu-primary");
        await pieceView.InvokeAsync(() => pieceView.Find(".pause-keys .mu-primary").ClickAsync(new MouseEventArgs()));
        pieceView.WaitForElement(".play-bar [data-piano=go]");
        var pieceKeys = Keys(pieceView);
        InTheEnginesOrder(pieceKeys, "Play");
        // A piece's own tools come first among them: What's this and its Options.
        Assert.True(pieceKeys.IndexOf("What's this?") < pieceKeys.IndexOf("Options") && pieceKeys.IndexOf("Options") < pieceKeys.IndexOf("Metronome"));

        var exercise = host.Context.Render<ExercisePractice>(p => p.Add(c => c.Id, "five"));
        exercise.WaitForAssertion(() => Assert.NotNull(exercise.FindComponent<PlayAlong>().Instance.Music));
        var exerciseKeys = Keys(exercise);
        InTheEnginesOrder(exerciseKeys, "Play");
        // An exercise's words are its Options, in the same place as a piece's; its Loop is beside the mode.
        Assert.True(exerciseKeys.IndexOf("Options") < exerciseKeys.IndexOf("Metronome"));
        Assert.True(exerciseKeys.FindIndex(k => k.StartsWith("Loop", StringComparison.Ordinal)) > exerciseKeys.IndexOf("Wait for me"));
        exercise.Dispose();

        host.Context.Services.GetRequiredService<NavigationManager>().NavigateTo("drills/scales");
        var scales = host.Context.Render<TechniquePractice>();
        scales.WaitForAssertion(() => Assert.NotNull(scales.FindComponent<PlayAlong>().Instance.Music));
        InTheEnginesOrder(Keys(scales), "Run");
        scales.Dispose();

        // An exercise draws its music in a piece's paper frame, the way a piece's is drawn.
        Assert.NotEmpty(pieceView.FindAll(".piece-frame.sheet-frame"));
        var again = host.Context.Render<ExercisePractice>(p => p.Add(c => c.Id, "five"));
        again.WaitForElement(".play-bar [data-piano=go]");
        Assert.Equal("scroll", again.FindComponent<Sheet>().Instance.Layout);
        Assert.NotNull(again.Find(".practice-page .piece-frame.sheet-frame.piece-frame-scroll[data-follow]"));
    }

    [Fact]
    public async Task An_exercise_goes_full_screen_by_the_bars_key_and_comes_out_of_it_as_it_goes()
    {
        await using var host = new ComponentHost(keyboard: new TestPiano(plugged: true));
        var view = host.Context.Render<ExercisePractice>(p => p.Add(c => c.Id, "five"));
        view.WaitForElement(".play-bar [aria-label='Full screen']");
        // What isn't the bar or the music is marked to go: its head, its line, its choices.
        Assert.NotEmpty(view.FindAll("header.page-head.full-hide"));
        Assert.NotEmpty(view.FindAll(".exercise-choice.full-hide"));

        await view.InvokeAsync(() => view.Find(".play-bar [aria-label='Full screen']").ClickAsync(new MouseEventArgs()));
        view.WaitForAssertion(() => Assert.Equal([true], host.Browser.Fullscreen));
        Assert.Equal([false], host.Bars.Shown);
        Assert.Equal("true", view.Find(".play-bar [aria-label='Leave full screen']").GetAttribute("aria-pressed"));

        await view.InvokeAsync(() => view.Find(".play-bar [aria-label='Leave full screen']").ClickAsync(new MouseEventArgs()));
        view.WaitForAssertion(() => Assert.Equal([true, false], host.Browser.Fullscreen));

        // On again, and the screen left: the next has its rail and head back.
        await view.InvokeAsync(() => view.Find(".play-bar [aria-label='Full screen']").ClickAsync(new MouseEventArgs()));
        view.WaitForAssertion(() => Assert.Equal([true, false, true], host.Browser.Fullscreen));
        var tools = view.FindComponent<PlayTools>().Instance;
        await view.InvokeAsync(tools.Dispose);
        await Task.Delay(100, Xunit.TestContext.Current.CancellationToken);
        Assert.Equal([true, false, true, false], host.Browser.Fullscreen);
        Assert.Equal([false, true, false, true], host.Bars.Shown);
    }

    [Fact]
    public async Task The_metronome_and_its_settings_are_on_an_exercises_bar_as_on_a_pieces()
    {
        await using var host = new ComponentHost(keyboard: new TestPiano(plugged: true));
        var view = host.Context.Render<ExercisePractice>(p => p.Add(c => c.Id, "five"));
        view.WaitForElement(".play-bar .play-metronome");
        await view.InvokeAsync(() => view.Find(".play-bar [aria-label='Metronome']").ClickAsync(new MouseEventArgs()));
        var pop = view.WaitForElement(".metronome-pop");
        Assert.Equal(["Tablet", "Piano", "Wood", "Beep", "Done"], pop.QuerySelectorAll("button").Select(b => b.TextContent.Trim()));
        // The exercise's own tempo is there to go back to once it's changed.
        await view.InvokeAsync(() => view.FindComponent<PlayAlong>().Instance.TempoChanged.InvokeAsync(90));
        view.WaitForElement(".play-tempo-original");
    }
}
