using Bunit;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Prima.Core.Music;
using Prima.Core.Practice;
using Prima.Core.Timing;
using Prima.UI.Components;
using Prima.UI.Pages;
using Prima.UI.Practice;
using Prima.UI.Storage;

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
            // The key judged before the clock moves on: judged after, by a tick that came first, its note's time had passed with
            // nothing played for it, a slip no player made (only a test's clock jumps).
            await view.InvokeAsync(() => { });
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
        view.WaitForAssertion(() => Assert.Equal("Play", view.Find(".play-start").TextContent.Trim()));
    }

    // The four bars as a piece in a practice session, In time, with More open.
    private static async Task<(IRenderedComponent<Piece> View, IRenderedComponent<PlayAlong> Bar, PieceRecord Piece)> Practicing(ComponentHost host)
    {
        var piece = await host.AddPiece();
        await host.Store.SavePieceAsync(piece, MeiWriter.Write(FourBars()), []);
        await host.Store.SetSettingAsync(PlayAlong.ModeKeyOf("library"), "time");
        await host.Store.StartSessionAsync(piece.Id, "custom", DateTimeOffset.Now);
        var view = host.Context.Render<Piece>(p => p.Add(c => c.Id, piece.Id));
        view.WaitForElement(".pause-keys .mu-primary");
        await view.InvokeAsync(() => view.Find(".pause-keys .mu-primary").ClickAsync(new MouseEventArgs()));
        view.WaitForAssertion(() => Assert.NotNull(view.FindComponent<PlayAlong>().Instance.Music));
        await view.InvokeAsync(() => view.Find("[data-action=more]").ClickAsync(new MouseEventArgs()));
        return (view, view.FindComponent<PlayAlong>(), piece);
    }

    // The run that's taken the one before's place, once it's under way.
    private static async Task<PlaySession> Next(SessionRegistry registry, PlaySession before)
    {
        await Until(() => registry.Current is { Phase: Phase.CountIn or Phase.Playing } now && !ReferenceEquals(now, before));
        return registry.Current!;
    }

    // The piece's runs kept, once there are so many.
    private static async Task<List<PieceRun>> Kept(ComponentHost host, PieceRecord piece, int count)
    {
        List<PieceRun> runs = [];
        await Until(() => (runs = [.. host.Store.GetPieceRunsAsync(piece.Id).GetAwaiter().GetResult().Select(r => r.Run)]).Count >= count);
        return runs;
    }

    [Fact]
    public async Task Lock_in_goes_round_the_slip_with_a_bar_either_side_a_notch_slower_after_two_slips_until_clean_so_many_times_then_on()
    {
        var (piano, clock) = (new TestPiano(plugged: true), new SetClock());
        await using var host = new ComponentHost(keyboard: piano, clock: clock);
        var (view, bar, piece) = await Practicing(host);
        var registry = host.Context.Services.GetRequiredService<SessionRegistry>();
        // Lock in, three clean in a row: kept for every piece.
        await view.InvokeAsync(() => view.Find("[data-slip=lock]").ClickAsync(new MouseEventArgs()));
        await view.InvokeAsync(() => view.Find("[data-clean='3']").ClickAsync(new MouseEventArgs()));
        view.WaitForAssertion(() => Assert.Equal("true", view.Find("[data-clean='3']").GetAttribute("aria-pressed")));
        Assert.Contains(view.FindAll(".slip-choice .control-label"), l => l.TextContent == "Timing too");
        var settings = await host.Store.GetSettingsAsync();
        Assert.Equal(("lock", "3"), (settings["Library.Slip.test"], settings["Practice.LockClean"]));
        var own = bar.Instance.Tempo;

        await view.InvokeAsync(() => bar.Instance.Start());
        var first = registry.Current!;
        await Play(bar, piano, clock, first, 9, wrong: 8);

        // Bars 2 to 4 go round, twelve notes a time round, the bar saying so.
        var spot = await Next(registry, first);
        Assert.Equal(12 * 50, spot.Expected.Count);
        Assert.Equal("Bars 2–4 until 3 clean in a row", view.Find(".play-saying").TextContent);
        Assert.Equal(3, view.FindAll(".play-streak-dot").Count);
        // Two slips in a row: counted in again each time, then a notch slower.
        await Play(bar, piano, clock, spot, 1, wrong: 0);
        var again = await Next(registry, spot);
        Assert.Equal(own, bar.Instance.Tempo);
        await Play(bar, piano, clock, again, 1, wrong: 0);
        var slower = await Next(registry, again);
        Assert.Equal(TempoMarks.Down(own), bar.Instance.Tempo);
        Assert.Empty(view.FindAll(".play-streak-dot.on"));

        // Three clean times round (and the fourth's first note, so the third is scored): on from bar 2 to the end, at your tempo.
        await Play(bar, piano, clock, slower, 37);
        var on = await Next(registry, slower);
        Assert.Equal(12, on.Expected.Count);
        Assert.Equal(own, bar.Instance.Tempo);
        await Play(bar, piano, clock, on, 12);
        clock.NowNs = on.TimeAt(on.Expected.Max(e => e.Beat + e.Beats) + 2);

        // Kept: the spot as a loop with every time round, those the slips cut short too, and the go as one run from bar 1.
        var runs = await Kept(host, piece, 2);
        Assert.True(runs.Count == 2 && runs.Count(r => r.Lock is not null) == 1, string.Join(" / ", runs.Select(r =>
            $"{r.From}-{r.To ?? r.Bar} {(r.Stopped ? "stopped" : "end")} {r.Lock?.Ended} {r.Right}/{r.Notes} passes [{string.Join(",", r.Passes.Select(p => $"{p.Right}/{p.Notes}"))}] trouble [{string.Join(",", r.Trouble.Select(t => $"{t.Bar}:{t.Wrong}w{t.Missed}m"))}]")));
        var (locked, go) = (runs.Single(r => r.Lock is not null), runs.Single(r => r.Lock is null));
        Assert.Equal((2, 4, 3, "done"), (locked.From, locked.To, locked.Lock!.Needed, locked.Lock.Ended));
        Assert.Equal([(0, 1), (0, 1), (12, 12), (12, 12), (12, 12)], locked.Passes.Select(p => (p.Right, p.Notes)));
        Assert.Equal(TempoMarks.Down(own), locked.Tempo);
        Assert.Equal((1, false, 20, 21), (go.From, go.Stopped, go.Right, go.Notes));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Stopped_round_a_spot_the_piece_still_starts_where_it_did_and_the_spot_is_kept(bool inThePause)
    {
        var (piano, clock) = (new TestPiano(plugged: true), new SetClock());
        await using var host = new ComponentHost(keyboard: piano, clock: clock);
        var (view, bar, piece) = await Practicing(host);
        var registry = host.Context.Services.GetRequiredService<SessionRegistry>();
        await view.InvokeAsync(() => view.Find("[data-slip=lock]").ClickAsync(new MouseEventArgs()));
        Assert.Equal("Play", view.Find(".play-start").TextContent.Trim());

        await view.InvokeAsync(() => bar.Instance.Start());
        var first = registry.Current!;
        await Play(bar, piano, clock, first, 9, wrong: 8);
        if (!inThePause)
        {
            // Round the spot, bars 2 to 4, a time round and a bit.
            var spot = await Next(registry, first);
            await Play(bar, piano, clock, spot, 14);
        }
        else await Until(() => view.FindAll(".play-bar[data-cut]").Count > 0);
        await view.InvokeAsync(bar.Instance.StopAndSaveAsync);
        await Until(() => !bar.Instance.Running && view.FindAll(".summary").Count > 0);

        // The piece starts from bar 1 still, nothing chosen on its music. The go up to the slip is kept as a run stopped there, and
        // the spot as a loop of its own once it went round (stopped before it did, there's no spot to keep).
        view.WaitForAssertion(() => Assert.Equal("Play", view.Find(".play-start").TextContent.Trim()));
        Assert.False(host.Browser.BarChoices[^1].GetProperty("lit").GetBoolean());
        Assert.Equal(1, host.Browser.BarChoices[^1].GetProperty("from").GetInt32());
        var runs = await Kept(host, piece, inThePause ? 1 : 2);
        var go = runs.Single(r => r.Lock is null);
        Assert.Equal((1, true, 3), (go.From, go.Stopped, go.Bar));
        Assert.Equal([(3, 1)], go.Trouble.Where(t => t.NotRight > 0).Select(t => (t.Bar, t.Wrong)));
        if (inThePause)
        {
            Assert.Single(runs);
            return;
        }
        var locked = runs.Single(r => r.Lock is not null);
        Assert.Equal((2, 4, "stopped"), (locked.From, locked.To, locked.Lock!.Ended));
    }

    [Fact]
    public async Task Lock_in_on_your_own_loop_ends_it_at_so_many_clean_in_a_row_with_a_notch_faster_or_play_on()
    {
        var (piano, clock) = (new TestPiano(plugged: true), new SetClock());
        await using var host = new ComponentHost(keyboard: piano, clock: clock);
        host.Context.Services.GetRequiredService<PrimaSettings>().LockClean = 3;
        var (view, bar, piece) = await Practicing(host);
        var registry = host.Context.Services.GetRequiredService<SessionRegistry>();
        await view.InvokeAsync(() => view.Find("[data-slip=lock]").ClickAsync(new MouseEventArgs()));
        await view.InvokeAsync(() => view.Instance.Looped(1, 2));

        await view.InvokeAsync(() => bar.Instance.Start());
        var loop = registry.Current!;
        Assert.Equal(8 * 50, loop.Expected.Count);
        // Three clean times round, and the fourth's first note.
        await Play(bar, piano, clock, loop, 25);

        // The run ends at the third, and the review says so, with a notch faster and play on.
        view.WaitForAssertion(() => Assert.Equal("Locked in: 3 clean in a row at ♩ 72.", view.Find(".summary .qr-said").TextContent.Trim()));
        Assert.False(bar.Instance.Running);
        Assert.Equal("A notch faster, ♩ 76", view.Find("[data-action=faster]").TextContent.Trim());
        var runs = await Kept(host, piece, 1);
        Assert.Equal((1, 2, 3, "done"), (runs[0].From, runs[0].To, runs[0].Lock!.Needed, runs[0].Lock!.Ended));
        Assert.Equal(3, runs[0].Passes.Count);

        // Play on: the loop off, from its first bar to the end.
        await view.InvokeAsync(() => view.Find("[data-action=play-on]").ClickAsync(new MouseEventArgs()));
        var on = await Next(registry, loop);
        Assert.Equal(16, on.Expected.Count);
        Assert.Null(bar.Instance.Music!.LoopBars);
    }

    [Fact]
    public async Task Practice_it_counts_its_loop_to_two_clean_in_a_row_and_the_look_back_says_so()
    {
        var (piano, clock) = (new TestPiano(plugged: true), new SetClock());
        await using var host = new ComponentHost(keyboard: piano, clock: clock);
        var (view, bar, piece) = await Practicing(host);
        var registry = host.Context.Services.GetRequiredService<SessionRegistry>();

        // A run through, bar 3's first note wrong, played on through it (Keep going).
        await view.InvokeAsync(() => bar.Instance.Start());
        var run = registry.Current!;
        await Play(bar, piano, clock, run, 16, wrong: 8);
        clock.NowNs = run.TimeAt(run.Expected.Max(e => e.Beat + e.Beats) + 2);
        await Until(() => !bar.Instance.Running);
        await Kept(host, piece, 1);

        // The look back's Next loops bar 3 a notch under the tempo it ended at, until two times round in a row are clean.
        await view.InvokeAsync(() => view.FindAll("button").First(b => b.TextContent.Trim() == "Finish practice").ClickAsync(new MouseEventArgs()));
        view.WaitForElement("[data-action=practice-it]");
        Assert.Contains("until two times round in a row are clean", view.Find(".qs-next").TextContent);
        await view.InvokeAsync(() => view.Find("[data-action=practice-it]").ClickAsync(new MouseEventArgs()));
        bar = view.FindComponent<PlayAlong>();
        view.WaitForAssertion(() => Assert.Equal(1, bar.Instance.Music?.LoopBars));

        // Two clean times round, the dots lit as they come: done, said so, with a notch faster and play on.
        await view.InvokeAsync(() => bar.Instance.Start());
        var loop = registry.Current!;
        view.WaitForAssertion(() => Assert.Equal(2, view.FindAll(".play-streak-dot").Count));
        await Play(bar, piano, clock, loop, 9);
        view.WaitForAssertion(() => Assert.Equal($"Done: 2 clean in a row at {QuickReview.Mark(TempoMarks.Down(72))}.", view.Find(".summary .qr-said").TextContent.Trim()));
        Assert.NotEmpty(view.FindAll("[data-action=faster]"));

        // The look back says how the loop went.
        var runs = await Kept(host, piece, 2);
        Assert.Equal((3, 3, 2, "done"), (runs[^1].From, runs[^1].To, runs[^1].Lock!.Needed, runs[^1].Lock!.Ended));
    }
}
