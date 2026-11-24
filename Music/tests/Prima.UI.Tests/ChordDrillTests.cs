using Bunit;
using Microsoft.Extensions.DependencyInjection;
using Prima.UI.Components;
using Prima.UI.Pages;
using Prima.UI.Practice;

namespace Prima.UI.Tests;

/// <summary>
/// The Chords drill's answer: a chord's keys go down together, within 150 ms. Played a key at a time it's listened to until it's
/// all down and said so, rather than calling the keys still to come missing; a key left out is still missing.
/// </summary>
public class ChordDrillTests
{
    private static string Feedback(IRenderedComponent<ItemDrill> view) => view.Find(".drill-feedback").TextContent.Trim();

    [Fact]
    public async Task A_chord_played_a_key_at_a_time_is_said_so_and_a_key_left_out_is_missing()
    {
        var (piano, clock) = (new TestPiano(plugged: true), new SetClock());
        await using var host = new ComponentHost(keyboard: piano, clock: clock);
        var registry = host.Context.Services.GetRequiredService<SessionRegistry>();
        var view = host.Context.Render<ItemDrill>(p => p.Add(c => c.Key, "chords"));
        view.WaitForAssertion(() => Assert.NotNull(registry.DrillAnswer));
        var sheet = view.FindComponent<Sheet>().Instance;
        await view.InvokeAsync(() => sheet.OnPainted.InvokeAsync(clock.NowNs));

        // The chord's keys a quarter of a second apart, as a hand spreading it would.
        var (chord, painted) = (registry.DrillAnswer!(true)[0], sheet.Mei);
        foreach (var key in chord)
        {
            clock.NowNs += 250_000_000;
            piano.Play(key, clock.NowNs);
            await Task.Delay(250, Xunit.TestContext.Current.CancellationToken);
        }
        view.WaitForAssertion(() => Assert.Equal("Not quite: played a key at a time. A chord's keys go down together.", Feedback(view)), TimeSpan.FromSeconds(5));

        // The next chord with its top key left out, the rest together: listened to a little longer, then that key is missing.
        view.WaitForAssertion(() => Assert.Equal("", Feedback(view)), TimeSpan.FromSeconds(5));
        // The sheet says it's painted only when its music changes, as in the browser.
        if (sheet.Mei != painted) await view.InvokeAsync(() => sheet.OnPainted.InvokeAsync(clock.NowNs));
        var next = registry.DrillAnswer!(true)[0];
        clock.NowNs += 1_000_000_000;
        foreach (var key in next.Order().SkipLast(1)) piano.Play(key, clock.NowNs);
        view.WaitForAssertion(() => Assert.EndsWith(" missing.", Feedback(view)), TimeSpan.FromSeconds(5));
        Assert.DoesNotContain("a key at a time", Feedback(view));
    }
}
