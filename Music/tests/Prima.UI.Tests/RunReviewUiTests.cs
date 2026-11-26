using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Prima.Core.Generation;
using Prima.Core.Practice;
using Prima.UI.Components;
using Prima.UI.Host;
using Prima.UI.Pages;
using Prima.UI.Practice;

namespace Prima.UI.Tests;

/// <summary>
/// A run's review on the exercise and scale screens: what you did and next time, in two lines; the rest under Show details, and a
/// hand named only when both played.
/// </summary>
public class RunReviewUiTests
{
    [Fact]
    public async Task An_exercises_run_is_two_lines_and_the_rest_waits_under_show_details()
    {
        var clock = new StopwatchClock();
        var piano = new StandInKeyboard(clock);
        await using var host = new ComponentHost(keyboard: piano, clock: clock);
        var player = new StandInPlayer(piano, host.Context.Services.GetRequiredService<SessionRegistry>(), clock);
        player.Plug(true);
        await host.Store.SaveTrackAsync("exercise:five", new ExerciseChoice(0, false, HandUse.Right, 72));
        var view = host.Context.Render<ExercisePractice>(p => p.Add(c => c.Id, "five"));
        var bar = view.FindComponent<PlayAlong>();
        view.WaitForAssertion(() => Assert.True(bar.Instance.Mode == PlayMode.InTime && bar.Instance.Music is not null));

        // Every note right, on the beat, each key held into the next.
        await view.InvokeAsync(() => bar.Instance.Start());
        await view.InvokeAsync(() => player.Perform(new(Right: 1, Lean: 0, Spread: 2, Hold: 1.05)));

        view.WaitForAssertion(() => Assert.Equal("Every note right, steady and legato.", view.Find(".review-did span:last-child").TextContent));
        Assert.Equal("A little faster.", view.Find(".review-next span:last-child").TextContent);
        Assert.Equal(["This run", "Next time"], view.FindAll(".sum-point .mu-readout-label").Select(l => l.TextContent));
        Assert.Empty(view.FindAll(".sum-details"));

        await view.Find(".sum-more").ClickAsync(new MouseEventArgs());
        view.WaitForAssertion(() => Assert.Equal(3, view.FindAll(".sum-details .sum-panel").Count));
    }

    [Fact]
    public async Task A_scale_with_one_hand_never_names_it_not_even_in_the_details()
    {
        var clock = new StopwatchClock();
        var piano = new StandInKeyboard(clock);
        await using var host = new ComponentHost(keyboard: piano, clock: clock);
        var player = new StandInPlayer(piano, host.Context.Services.GetRequiredService<SessionRegistry>(), clock);
        player.Plug(true);
        // C major, one octave, the left hand, with a tempo: it opens In time.
        var choice = new TechniqueChoice(new Prima.Core.Music.Key(0), ScaleKind.Major, 1, HandUse.Left, 1);
        await host.Store.SaveTrackAsync("technique-choice:scales", choice);
        await host.Store.SaveTrackAsync($"technique:scales:{choice.Item("scales").Id}:{HandUse.Left}", TechniqueState.New with { Tempo = 60 });
        host.Context.Services.GetRequiredService<NavigationManager>().NavigateTo("drills/scales");
        var view = host.Context.Render<TechniquePractice>();
        var bar = view.FindComponent<PlayAlong>();
        view.WaitForAssertion(() => Assert.True(bar.Instance.Mode == PlayMode.InTime && bar.Instance.Music is not null));

        await view.InvokeAsync(() => bar.Instance.Start());
        await view.InvokeAsync(() => player.Perform(new(Right: 1, Lean: 0, Spread: 2, Hold: 1.08)));

        view.WaitForAssertion(() => Assert.Equal("Every note right, even and legato.", view.Find(".review-did span:last-child").TextContent));
        Assert.Equal("A little faster.", view.Find(".review-next span:last-child").TextContent);
        Assert.Empty(view.FindAll(".summary-words li"));

        // Under Show details, the figures and a line on each, none of them naming the hand.
        await view.Find(".sum-more").ClickAsync(new MouseEventArgs());
        view.WaitForAssertion(() => Assert.NotEmpty(view.FindAll(".summary-words li")));
        Assert.Equal("Evenness", view.Find(".summary-figures .mu-readout-label").TextContent);
        Assert.StartsWith("Evenness: the gaps between notes", view.FindAll(".summary-words li")[0].TextContent);
        Assert.All(view.FindAll(".summary-words li, .sum-point, .summary-figures .mu-readout").Select(e => e.TextContent), t => Assert.DoesNotContain("hand", t, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task A_scales_first_run_at_your_own_pace_leads_on_to_in_time()
    {
        var piano = new TestPiano(plugged: true);
        await using var host = new ComponentHost(keyboard: piano);
        await host.Store.SaveTrackAsync("technique-choice:scales", new TechniqueChoice(new Prima.Core.Music.Key(0), ScaleKind.Major, 1, HandUse.Right, 1));
        host.Context.Services.GetRequiredService<NavigationManager>().NavigateTo("drills/scales");
        var view = host.Context.Render<TechniquePractice>();
        var bar = view.FindComponent<PlayAlong>();
        view.WaitForAssertion(() => Assert.True(bar.Instance.Mode == PlayMode.WaitForMe && bar.Instance.Music is not null));

        // Each note right, a third of a second apart.
        await view.InvokeAsync(() => bar.Instance.Start());
        var at = 1_000_000_000L;
        foreach (var note in bar.Instance.Music!.Notes.OrderBy(n => n.Tick))
        {
            piano.Play(note.Midi, at);
            piano.Release(note.Midi, at + 250_000_000);
            at += 330_000_000;
        }

        view.WaitForAssertion(() => Assert.Equal("Every note right first time, at your own pace.", view.Find(".review-did span:last-child").TextContent));
        Assert.Equal("In time, a little under your own speed.", view.Find(".review-next span:last-child").TextContent);
    }
}
