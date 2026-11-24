using Bunit;
using Microsoft.AspNetCore.Components.Web;
using Prima.UI.Pages;

namespace Prima.UI.Tests;

/// <summary>The Rhythm drill: a set of tries, then another.</summary>
public class RhythmDrillTests
{
    [Fact]
    public async Task Another_set_starts_without_the_last_tries_marks()
    {
        var (piano, clock) = (new TestPiano(plugged: true), new SetClock());
        await using var host = new ComponentHost(keyboard: piano, clock: clock);
        var view = host.Context.Render<RhythmPractice>();
        const string go = "[data-piano='go']";
        view.WaitForAssertion(() => Assert.False(view.Find(go).HasAttribute("disabled")));

        // A set's three tries, nothing played: each ends once its music has gone by, and its notes are marked.
        foreach (var next in new[] { "Next try", "Next try", "Another set" })
        {
            await view.Find(go).ClickAsync(new MouseEventArgs());
            clock.NowNs += 600_000_000_000;
            view.WaitForAssertion(() => Assert.Equal(next, view.Find(go).TextContent.Trim()));
        }
        Assert.Equal("showMarks", host.Browser.Marks[^1]);

        // Another set: its first rhythm shows none of them, whether or not it's the last one's music (the sheet doesn't draw the
        // same music again, so nothing else would take them off).
        await view.Find(go).ClickAsync(new MouseEventArgs());
        view.WaitForAssertion(() => Assert.Equal("Start", view.Find(go).TextContent.Trim()));
        Assert.Equal("clearMarks", host.Browser.Marks[^1]);
    }
}
