using Bunit;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Prima.Core.Music;
using Prima.Core.Practice;
using Prima.UI.Components;
using Prima.UI.Pages;
using Prima.UI.Practice;

namespace Prima.UI.Tests;

/// <summary>When I slip: a run In time stopped at a slip and counted in again from the bar before, the go kept as one run (Back a bar).</summary>
public class SlipTests
{
    // Four bars of four quarter notes each, C D E F.
    private static Score FourBars()
    {
        static Notes N(string id, int beat, Step step) => new(id, beat * Ticks.Quarter, Ticks.Quarter, [new Tone(id, new Spelled(step, 0, 4))]);
        return new Score(new Prima.Core.Music.Key(0), new Meter(4, 4),
            [.. Enumerable.Range(1, 4).Select(b => new Bar(b, [new Voice(1, [N($"b{b}a", 0, Step.C), N($"b{b}b", 1, Step.D), N($"b{b}c", 2, Step.E), N($"b{b}d", 3, Step.F)])]))]);
    }

    // The music from a bar to the end, as a piece's Practice makes it.
    private static PlayMusic From(Score score, int bar)
    {
        var order = score.PlayOrder(bar - 1);
        return new PlayMusic(score, score.Performed(order), score.BarStarts().First(b => b.Bar.Number == bar).Start) { Order = order };
    }

    // The run going's notes from the first on, each on its beat (one a key two away, by its place), held to just before the next.
    private static async Task Play(IRenderedComponent<PlayAlong> view, TestPiano piano, SetClock clock, PlaySession run, int count, int? wrong = null)
    {
        foreach (var (note, i) in run.Expected.Take(count).Select((n, i) => (n, i)))
        {
            var (at, key) = (run.TimeAt(note.Beat), i == wrong ? note.Midi + 2 : note.Midi);
            clock.NowNs = at;
            await view.InvokeAsync(() => piano.Play(key, at));
            clock.NowNs = run.TimeAt(note.Beat + note.Beats) - 30_000_000;
            await view.InvokeAsync(() => piano.Release(key, clock.NowNs));
        }
    }

    // Until the run ends: told after the bar has drawn its last, so no draw comes to check it.
    private static async Task Until(Func<bool> done)
    {
        for (var tries = 0; tries < 100 && !done(); tries++) await Task.Delay(100, Xunit.TestContext.Current.CancellationToken);
        Assert.True(done());
    }

    [Fact]
    public async Task A_slip_stops_the_run_and_it_counts_in_again_from_the_bar_before_with_its_mark_left()
    {
        var (piano, clock) = (new TestPiano(plugged: true), new SetClock());
        await using var host = new ComponentHost(keyboard: piano, clock: clock);
        var score = FourBars();
        var sheet = host.Context.Render<Sheet>(p => p.Add(c => c.Mei, MeiWriter.Write(score)));
        var (parts, finished) = (new List<PlayOutcome>(), new List<PlayOutcome>());
        var view = host.Context.Render<PlayAlong>(p => p.Add(c => c.Name, "slip-test").Add(c => c.Music, From(score, 1)).Add(c => c.SheetOf, () => sheet.Instance)
            .Add(c => c.Tempo, 60).Add(c => c.Ask, PlayMode.InTime).Add(c => c.CutPause, TimeSpan.Zero)
            .Add(c => c.OnSlip, part => { parts.Add(part); return Task.FromResult<PlayMusic?>(From(score, part.Bar - 1)); })
            .Add(c => c.OnFinished, how => finished.Add(how)));
        view.WaitForAssertion(() => Assert.Equal(PlayMode.InTime, view.Instance.Mode));
        var registry = host.Context.Services.GetRequiredService<SessionRegistry>();

        await view.InvokeAsync(() => view.Instance.Start());
        var first = registry.Current!;
        var clears = host.Browser.Marks.Count(m => m == "clearMarks");
        // Bars 1 and 2 right, then bar 3's first note a key away.
        await Play(view, piano, clock, first, 9, wrong: 8);

        // The run stops there: what was played up to the slip is the part, as a run stopped at bar 3, and nothing has finished.
        view.WaitForAssertion(() => Assert.Single(parts));
        var part = parts[0];
        Assert.Equal((3, true, true), (part.Bar, part.Stopped, part.Slipped));
        Assert.Equal((8, 9), (part.InTime!.Right, part.InTime.Expected));
        Assert.Contains(part.Marks, m => m is { Id: "b3a", Kind: "wrong" });
        // Counted in again from bar 2, the same go: the marks so far stay on the music.
        view.WaitForAssertion(() => Assert.NotSame(first, registry.Current));
        var second = registry.Current!;
        Assert.Equal(12, second.Expected.Count);
        Assert.Equal(clears, host.Browser.Marks.Count(m => m == "clearMarks"));
        Assert.Empty(finished);

        // Bars 2 to 4 right, to the end.
        await Play(view, piano, clock, second, 12);
        clock.NowNs = second.TimeAt(second.Expected.Max(e => e.Beat + e.Beats) + 2);
        await Until(() => finished.Count == 1);
        var end = finished[0];
        Assert.Equal((12, 12, false), (end.InTime!.Right, end.InTime.Expected, end.Stopped));

        // As one go: every note played, the slip in bar 3 among them, and on the music the slip's mark, not the right note after it.
        var go = Go.Join(parts, finished[0]);
        Assert.Equal((20, 21, false), (go.InTime!.Right, go.InTime.Expected, go.Stopped));
        Assert.Equal([(3, 1)], Trouble.Of(go.InTime!.Notes).Where(t => t.NotRight > 0).Select(t => (t.Bar, t.NotRight)));
        Assert.Equal("wrong", go.Marks.Single(m => m.Id == "b3a").Kind);
    }

    [Fact]
    public async Task Stopped_in_the_moment_after_a_slip_the_run_ends_at_the_slip()
    {
        var (piano, clock) = (new TestPiano(plugged: true), new SetClock());
        await using var host = new ComponentHost(keyboard: piano, clock: clock);
        var score = FourBars();
        var sheet = host.Context.Render<Sheet>(p => p.Add(c => c.Mei, MeiWriter.Write(score)));
        var (parts, finished) = (new List<PlayOutcome>(), new List<PlayOutcome>());
        var view = host.Context.Render<PlayAlong>(p => p.Add(c => c.Name, "slip-stop").Add(c => c.Music, From(score, 1)).Add(c => c.SheetOf, () => sheet.Instance)
            .Add(c => c.Tempo, 60).Add(c => c.Ask, PlayMode.InTime).Add(c => c.CutPause, TimeSpan.FromSeconds(30))
            .Add(c => c.OnSlip, part => { parts.Add(part); return Task.FromResult<PlayMusic?>(From(score, part.Bar - 1)); })
            .Add(c => c.OnFinished, how => finished.Add(how)));
        view.WaitForAssertion(() => Assert.Equal(PlayMode.InTime, view.Instance.Mode));

        await view.InvokeAsync(() => view.Instance.Start());
        var run = host.Context.Services.GetRequiredService<SessionRegistry>().Current!;
        await Play(view, piano, clock, run, 6, wrong: 5);
        view.WaitForAssertion(() => Assert.Single(parts));
        // The bar still says a run plays, through the moment's pause.
        Assert.Contains(view.FindAll(".play-bar button"), b => b.TextContent.Trim() == "Stop");

        await view.InvokeAsync(view.Instance.StopAndSaveAsync).WaitAsync(TimeSpan.FromSeconds(5), Xunit.TestContext.Current.CancellationToken);
        await Until(() => finished.Count == 1);
        Assert.Same(parts[0], finished[0]);
        Assert.Equal(2, finished[0].Bar);
        Assert.False(view.Instance.Running);
    }

    [Fact]
    public async Task Back_a_bar_is_kept_for_the_piece_and_its_go_is_kept_as_one_run_from_where_it_started()
    {
        var (piano, clock) = (new TestPiano(plugged: true), new SetClock());
        await using var host = new ComponentHost(keyboard: piano, clock: clock);
        var piece = await host.AddPiece();
        await host.Store.SavePieceAsync(piece, MeiWriter.Write(FourBars()), []);
        await host.Store.SetSettingAsync(PlayAlong.ModeKeyOf("library"), "time");
        await host.Store.StartSessionAsync(piece.Id, "custom", DateTimeOffset.Now);
        var view = host.Context.Render<Piece>(p => p.Add(c => c.Id, piece.Id));
        view.WaitForElement(".pause-keys .mu-primary");
        await view.InvokeAsync(() => view.Find(".pause-keys .mu-primary").ClickAsync(new MouseEventArgs()));
        view.WaitForAssertion(() => Assert.NotNull(view.FindComponent<PlayAlong>().Instance.Music));

        // Chosen under More, and kept for the piece.
        await view.InvokeAsync(() => view.Find("[data-action=more]").ClickAsync(new MouseEventArgs()));
        Assert.Equal("true", view.Find("[data-slip=keep]").GetAttribute("aria-pressed"));
        await view.InvokeAsync(() => view.Find("[data-slip=back]").ClickAsync(new MouseEventArgs()));
        view.WaitForAssertion(() => Assert.Equal("true", view.Find("[data-slip=back]").GetAttribute("aria-pressed")));
        Assert.Equal("back", (await host.Store.GetSettingsAsync())["Library.Slip.test"]);

        var bar = view.FindComponent<PlayAlong>();
        var registry = host.Context.Services.GetRequiredService<SessionRegistry>();
        await view.InvokeAsync(() => bar.Instance.Start());
        var first = registry.Current!;
        await Play(bar, piano, clock, first, 9, wrong: 8);
        // A moment later it counts in again from bar 2, and plays on to the end.
        view.WaitForAssertion(() => Assert.NotSame(first, registry.Current), TimeSpan.FromSeconds(10));
        var second = registry.Current!;
        Assert.Equal(12, second.Expected.Count);
        await Play(bar, piano, clock, second, 12);
        clock.NowNs = second.TimeAt(second.Expected.Max(e => e.Beat + e.Beats) + 2);
        view.WaitForAssertion(() => Assert.False(bar.Instance.Running));

        // One run, from bar 1 to the end, with the slip in bar 3; the piece still starts from bar 1.
        List<PieceRun> runs = [];
        await Until(() => (runs = [.. host.Store.GetPieceRunsAsync(piece.Id).GetAwaiter().GetResult().Select(r => r.Run)]).Count > 0);
        Assert.Single(runs);
        Assert.Equal((1, false, 20, 21), (runs[0].From, runs[0].Stopped, runs[0].Right, runs[0].Notes));
        Assert.Equal([(3, 1)], runs[0].Trouble.Where(t => t.NotRight > 0).Select(t => (t.Bar, t.Wrong)));
        Assert.Equal("Play", view.Find(".play-start").TextContent.Trim());
    }
}
