using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Prima.Core.Generation;
using Prima.Core.Music;
using Prima.UI.Components;
using Prima.UI.Pages;
using Prima.UI.Practice;
using Prima.UI.Storage;

namespace Prima.UI.Tests;

/// <summary>
/// The play bar's Loop, on every screen that has the bar: a run that goes round the music's bars until Stop; an exercise through
/// its keys; and a drill's sets one after another.
/// </summary>
public class LoopTests
{
    // Two bars that go round (C D, E F, in halves) and a closing whole note.
    private static Score TwoBarsAndAClose()
    {
        static Notes N(string id, int start, int length, Step step) => new(id, start, length, [new Tone(id, new Spelled(step, 0, 4))]);
        return new Score(new Prima.Core.Music.Key(0), new Meter(4, 4),
        [
            new Bar(1, [new Voice(1, [N("a", 0, Ticks.Half, Step.C), N("b", Ticks.Half, Ticks.Half, Step.D)])]),
            new Bar(2, [new Voice(1, [N("c", 0, Ticks.Half, Step.E), N("d", Ticks.Half, Ticks.Half, Step.F)])]),
            new Bar(3, [new Voice(1, [N("e", 0, Ticks.Whole, Step.C)])]),
        ]);
    }

    private static string Readout(IRenderedComponent<PlayAlong> bar, string label) =>
        bar.FindAll(".play-bar .mu-readout").FirstOrDefault(r => r.QuerySelector(".mu-readout-label")?.TextContent.Trim() == label)?.QuerySelector(".mu-readout-value")?.TextContent.Trim() ?? "";

    [Fact]
    public void Music_goes_round_its_round_each_time_a_pass()
    {
        var score = TwoBarsAndAClose();
        var music = new PlayMusic(score, score.Expected()) { Round = [0, 1] };

        var looped = music.Looped(3);

        Assert.Equal([0, 1, 0, 1, 0, 1], looped.Order);
        Assert.Equal(2, looped.LoopBars);
        Assert.Equal([60, 62, 64, 65, 60, 62, 64, 65, 60, 62, 64, 65], looped.Notes.Select(n => n.Midi));
        Assert.Equal(12, looped.Notes.Select(n => n.Id).Distinct().Count());
        Assert.Equal(Enumerable.Range(0, 12).Select(i => i * (long)Ticks.Half), looped.Notes.Select(n => n.Tick));
        // One hand's notes stay that hand's; with no round named, all of it goes round.
        var oneNote = new PlayMusic(score, [.. score.Expected().Where(n => n.Midi == 62)]) { Round = [0, 1] };
        Assert.Equal([62, 62], oneNote.Looped(2).Notes.Select(n => n.Midi));
        Assert.Equal([0, 1, 2, 0, 1, 2], new PlayMusic(score, score.Expected()).Looped(2).Order);
        // Half an hour's worth at the tempo, within what a run can carry: 8 beats a time round at 60 is 225 times.
        Assert.Equal(225, music.TimesRound(60));
        Assert.Equal(600, music.TimesRound(240));
    }

    [Fact]
    public async Task The_bars_loop_goes_round_the_music_without_its_close_until_stop()
    {
        var (piano, clock) = (new TestPiano(plugged: true), new SetClock());
        await using var host = new ComponentHost(keyboard: piano, clock: clock);
        var score = TwoBarsAndAClose();
        var sheet = host.Context.Render<Sheet>(p => p.Add(c => c.Mei, MeiWriter.Write(score)));
        var (finished, told) = (new List<PlayOutcome>(), new List<bool>());
        var view = host.Context.Render<PlayAlong>(p => p.Add(c => c.Name, "loop-test").Add(c => c.Music, new PlayMusic(score, score.Expected()) { Round = [0, 1] })
            .Add(c => c.SheetOf, () => sheet.Instance).Add(c => c.Tempo, 60).Add(c => c.Ask, PlayMode.WaitForMe).Add(c => c.ShowLoop, true)
            .Add(c => c.LoopChanged, on => told.Add(on)).Add(c => c.OnFinished, how => finished.Add(how)));
        view.WaitForAssertion(() => Assert.Equal(PlayMode.WaitForMe, view.Instance.Mode));

        // Off to start with: its key lights it, the screen is told, and it's kept for the screen.
        Assert.False(view.Instance.Looping);
        Assert.Equal("false", view.Find(".play-loop .play-mode-option").GetAttribute("aria-checked"));
        await view.Find(".play-loop .play-mode-option").ClickAsync(new MouseEventArgs());
        view.WaitForAssertion(() => Assert.Equal("true", view.Find(".play-loop .play-mode-option").GetAttribute("aria-checked")));
        Assert.Equal([true], told);
        Assert.Equal("on", (await host.Store.GetSettingsAsync())[PlayAlong.LoopKeyOf("loop-test")]);

        // Twice round and into a third: C D E F, C D E F, C. After F comes C again, never the close's long note.
        await view.InvokeAsync(() => view.Instance.Start());
        foreach (var midi in new[] { 60, 62, 64, 65, 60, 62, 64, 65, 60 })
        {
            clock.NowNs += 300_000_000;
            piano.Play(midi, clock.NowNs);
        }
        view.WaitForAssertion(() => Assert.Equal("3", Readout(view, "Pass")));
        Assert.Equal("4/4", Readout(view, "Last pass"));
        Assert.True(view.Instance.Running);

        // Stop is the way out: the run is its last pass played to its end, with every pass counted.
        await view.InvokeAsync(view.Instance.Stop);
        view.WaitForAssertion(() => Assert.Single(finished));
        Assert.Equal((1, 2), finished[0].Loop);
        Assert.Equal([(4, 4), (4, 4)], finished[0].Passes.Select(p => (p.Right, p.Notes)));
        Assert.Equal(4, finished[0].Wait!.Notes);
        Assert.False(view.Instance.Running);

        // The bar opened again on that screen is looping from the start, and says so.
        var again = new List<bool>();
        var reopened = host.Context.Render<PlayAlong>(p => p.Add(c => c.Name, "loop-test").Add(c => c.Music, new PlayMusic(score, score.Expected())).Add(c => c.SheetOf, () => sheet.Instance)
            .Add(c => c.ShowLoop, true).Add(c => c.LoopChanged, on => again.Add(on)));
        reopened.WaitForAssertion(() => Assert.True(reopened.Instance.Looping));
        Assert.Equal([true], again);
        // A screen that doesn't offer it never loops, whatever was kept.
        var without = host.Context.Render<PlayAlong>(p => p.Add(c => c.Name, "loop-test").Add(c => c.Music, new PlayMusic(score, score.Expected())).Add(c => c.SheetOf, () => sheet.Instance));
        without.WaitForAssertion(() => Assert.NotEmpty(without.FindAll("[data-piano='go']")));
        Assert.False(without.Instance.Looping);
        Assert.Empty(without.FindAll(".play-loop"));
    }

    [Fact]
    public async Task In_time_a_looped_run_is_its_last_pass_with_that_passes_own_keys()
    {
        var (piano, clock) = (new TestPiano(plugged: true), new SetClock());
        await using var host = new ComponentHost(keyboard: piano, clock: clock);
        await host.Store.SetSettingAsync(PlayAlong.LoopKeyOf("loop-time"), "on");
        var score = TwoBarsAndAClose();
        var sheet = host.Context.Render<Sheet>(p => p.Add(c => c.Mei, MeiWriter.Write(score)));
        var finished = new List<PlayOutcome>();
        var view = host.Context.Render<PlayAlong>(p => p.Add(c => c.Name, "loop-time").Add(c => c.Music, new PlayMusic(score, score.Expected()) { Round = [0, 1] })
            .Add(c => c.SheetOf, () => sheet.Instance).Add(c => c.Tempo, 60).Add(c => c.Ask, PlayMode.InTime).Add(c => c.ShowLoop, true).Add(c => c.OnFinished, how => finished.Add(how)));
        view.WaitForAssertion(() => Assert.True(view.Instance.Looping));

        await view.InvokeAsync(() => view.Instance.Start());
        var run = host.Context.Services.GetRequiredService<SessionRegistry>().Current!;
        // The run is the round many times over: half an hour's worth at 60.
        Assert.Equal(225 * 4, run.Expected.Count);
        // Two passes played on the beat, the second with a wrong key for its last note, and the third's first note.
        foreach (var (note, i) in run.Expected.Take(9).Select((n, i) => (n, i)))
        {
            var at = run.TimeAt(note.Beat);
            var midi = i == 7 ? note.Midi + 1 : note.Midi;
            clock.NowNs = at;
            piano.Play(midi, at);
            clock.NowNs = at + 400_000_000;
            piano.Release(midi, clock.NowNs);
        }
        await view.InvokeAsync(view.Instance.Stop);
        view.WaitForAssertion(() => Assert.Single(finished));

        var how = finished[0];
        Assert.Equal((1, 2), how.Loop);
        Assert.Equal([(4, 4), (3, 4)], how.Passes.Select(p => (p.Right, p.Notes)));
        // The result is the second pass, and its keys are that pass's four, not the nine played.
        Assert.Equal((3, 4), (how.InTime!.Right, how.InTime.Expected));
        Assert.Equal(9, how.Played.Count);
        Assert.Equal([60, 62, 64, 66], how.Judged.Select(k => k.Midi));
    }

    [Fact]
    public async Task An_exercise_goes_through_its_keys_and_its_loop_is_printed_as_a_repeat()
    {
        await using var host = new ComponentHost(keyboard: new TestPiano(plugged: true));
        var view = host.Context.Render<ExercisePractice>(p => p.Add(c => c.Id, "five"));
        view.WaitForAssertion(() => Assert.NotNull(view.FindComponent<PlayAlong>().Instance.Music));
        PlayMusic Music() => view.FindComponent<PlayAlong>().Instance.Music!;

        // As it opens: the pattern's two bars and its close, the two bars the ones that go round.
        Assert.Equal(3, Music().Score.Bars.Count);
        Assert.Equal([0, 1], Music().Round);
        Assert.DoesNotContain("rptstart", view.FindComponent<Sheet>().Instance.Mei);

        // Through the keys: every key's two bars one after another, and the last key's close; kept for the exercise.
        await Choosing.Tap(view, ".exercise-through");
        view.WaitForAssertion(() => Assert.Equal(Technique.Order.Count * 2 + 1, Music().Score.Bars.Count));
        Assert.Equal(Technique.Order.Select(k => k.Name), Music().Score.Marks.Where(m => m.Kind == MarkKind.Text).Select(m => m.Text));
        Assert.True((await host.Store.GetTrackAsync<ExerciseChoice>("exercise:five"))!.Through);
        Assert.Contains("Through the keys", Choosing.Facts(view));
        // From another key, it starts there and comes round to the key before.
        await Choosing.ChooseKey(view, "A minor");
        view.WaitForAssertion(() => Assert.Equal("A minor", Music().Score.Marks.First(m => m.Kind == MarkKind.Text).Text));
        Assert.Equal("C major", Music().Score.Marks.Last(m => m.Kind == MarkKind.Text).Text);

        // Loop: the sheet has the repeat's signs round all but the close, which are the bars the run goes round.
        await view.Find(".play-loop .play-mode-option").ClickAsync(new MouseEventArgs());
        view.WaitForAssertion(() => Assert.Contains("rptstart", view.FindComponent<Sheet>().Instance.Mei));
        var printed = MeiReader.Read(view.FindComponent<Sheet>().Instance.Mei);
        Assert.True(printed.Bars[0].RepeatStart && printed.Bars[^2].RepeatEnd);
        Assert.Equal(Enumerable.Range(0, Technique.Order.Count * 2), Music().Round);

        // Off again, and one key alone: the signs come off.
        await view.Find(".play-loop .play-mode-option").ClickAsync(new MouseEventArgs());
        await Choosing.Tap(view, ".exercise-through");
        view.WaitForAssertion(() => Assert.Equal(3, Music().Score.Bars.Count));
        Assert.DoesNotContain("rptstart", view.FindComponent<Sheet>().Instance.Mei);
    }

    [Fact]
    public async Task A_family_loops_and_goes_through_its_keys_as_its_variations_are_chosen()
    {
        await using var host = new ComponentHost(keyboard: new TestPiano(plugged: true));
        var view = host.Context.Render<ExercisePractice>(p => p.Add(c => c.Id, "thumb-three"));
        view.WaitForAssertion(() => Assert.NotNull(view.FindComponent<PlayAlong>().Instance.Music));
        PlayMusic Music() => view.FindComponent<PlayAlong>().Instance.Music!;
        string[] Named() => [.. Music().Score.Marks.Where(m => m.Kind == MarkKind.Text).Select(m => m.Text!)];

        // The thumb under three fingers: three bars that go round and the close, through the ten keys where it lands on white ones.
        Assert.Equal([0, 1, 2], Music().Round);
        await Choosing.Tap(view, ".exercise-through");
        view.WaitForAssertion(() => Assert.Equal(10 * 3 + 1, Music().Score.Bars.Count));
        // Only those ten are on the piano: of the major keys, C, D, E, G and A; in F and B the thumb would land on a black key.
        await Choosing.OpenChange(view);
        Assert.Equal(["C major", "D major", "E major", "F: not offered", "G major", "A major", "B: not offered"], view.FindAll(".key-piano-white").Select(k => k.GetAttribute("aria-label")));
        foreach (var name in new[] { "A minor", "G major", "D major" })
        {
            await Choosing.ChooseKey(view, name);
            view.WaitForAssertion(() => Assert.Equal(name, view.Find(".exercise-key").TextContent));
        }
        Assert.Equal(["D major", "E minor", "D minor", "A major", "G minor", "E major", "C minor", "C major", "A minor", "G major"], Named());

        // Under two fingers it would land on F♯ in D: those have six keys of their own, and the key goes back to the first. The
        // run through them is what's chosen: four bars in two to a key, and the close.
        await Choosing.Tap(view, "[data-variation=under] button[aria-label='2 fingers']");
        view.WaitForAssertion(() => Assert.Equal("C major", view.Find(".exercise-key").TextContent));
        Assert.Equal(["C major", "A minor", "G major", "F major", "E minor", "B minor"], Named());
        Assert.Equal(6 * 4 + 1, Music().Score.Bars.Count);
        Assert.Equal(new Meter(2, 4), Music().Score.Meter);
        Assert.Equal("D: not offered", view.FindAll(".key-piano-white")[1].GetAttribute("aria-label"));
        var kept = await host.Store.GetTrackAsync<ExerciseChoice>("exercise:thumb-three");
        Assert.Equal(("under=2", true, 0, false), (kept!.Variation, kept.Through, kept.Fifths, kept.Minor));

        // Loop: the repeat's signs go round all of it but the last key's close.
        await view.Find(".play-loop .play-mode-option").ClickAsync(new MouseEventArgs());
        view.WaitForAssertion(() => Assert.Contains("rptstart", view.FindComponent<Sheet>().Instance.Mei));
        Assert.Equal(Enumerable.Range(0, 24), Music().Round);

        // It opens again as it was left: under two, through its six keys.
        var again = host.Context.Render<ExercisePractice>(p => p.Add(c => c.Id, "thumb-three"));
        again.WaitForAssertion(() => Assert.Equal(25, again.FindComponent<PlayAlong>().Instance.Music?.Score.Bars.Count));
        Assert.Equal(["Under 2 fingers", "Through the keys"], Choosing.Facts(again));
    }

    [Fact]
    public async Task Every_exercise_to_play_has_its_key_and_the_loop()
    {
        await using var host = new ComponentHost(keyboard: new TestPiano(plugged: true));
        // Cover each numbered-study player here. HanonTests checks all sixty scores
        // and their key and hand combinations without mounting thousands of SVG notes.
        foreach (var exercise in Exercises.All.Where(e => e.Kind == ExerciseKind.Played && e.HanonNumber is null or 1 or 39 or 41 or 52 or 60))
        {
            var view = host.Context.Render<ExercisePractice>(p => p.Add(c => c.Id, exercise.Id));
            view.WaitForAssertion(() => Assert.NotNull(view.FindComponent<PlayAlong>().Instance.Music));
            await Choosing.OpenChange(view);
            Assert.Equal(Exercises.KeysOf(exercise).Count > 1, view.FindAll(".key-piano").Any());
            Assert.Equal(exercise.HanonNumber is null, view.FindAll(".exercise-through").Any());
            if (exercise.HanonNumber is not null && Exercises.KeysOf(exercise).Count > 1)
                Assert.Single(view.FindAll("a[href*='routines/hanon/keys?study=']"));
            Assert.Single(view.FindAll(".play-loop"));
            view.Dispose();
        }
    }

    [Fact]
    public async Task Scales_go_through_the_keys_and_loop_too()
    {
        await using var host = new ComponentHost(keyboard: new TestPiano(plugged: true));
        host.Context.Services.GetRequiredService<NavigationManager>().NavigateTo("drills/scales");
        var view = host.Context.Render<TechniquePractice>();
        view.WaitForAssertion(() => Assert.NotNull(view.FindComponent<PlayAlong>().Instance.Music));
        PlayMusic Music() => view.FindComponent<PlayAlong>().Instance.Music!;
        var one = Music().Score.Bars.Count;

        // Through the keys: every key's scale one after another, each named, from the key chosen.
        await Choosing.Tap(view, ".exercise-through");
        view.WaitForAssertion(() => Assert.Equal(Technique.Order.Count, Music().Score.Marks.Count(m => m.Kind == MarkKind.Text)));
        Assert.True(Music().Score.Bars.Count >= Technique.Order.Count * one);
        Assert.Equal("on", (await host.Store.GetSettingsAsync())["Technique.Through.scales"]);
        Assert.Equal(Music().Score.Expected().Count, Music().Notes.Count);

        // Loop: all of it goes round, printed as a repeat.
        await view.Find(".play-loop .play-mode-option").ClickAsync(new MouseEventArgs());
        view.WaitForAssertion(() => Assert.Contains("rptend", view.FindComponent<Sheet>().Instance.Mei));
        var printed = MeiReader.Read(view.FindComponent<Sheet>().Instance.Mei);
        Assert.True(printed.Bars[0].RepeatStart && printed.Bars[^1].RepeatEnd);
    }

    [Fact]
    public async Task A_looped_scale_says_how_many_times_it_went_round_and_that_its_evenness_is_the_last_pass()
    {
        var (piano, clock) = (new TestPiano(plugged: true), new SetClock());
        await using var host = new ComponentHost(keyboard: piano, clock: clock);
        // C major, one octave, the right hand, with a tempo: it opens In time, and looping.
        var choice = new TechniqueChoice(new Prima.Core.Music.Key(0), ScaleKind.Major, 1, HandUse.Right, 1);
        await host.Store.SaveTrackAsync("technique-choice:scales", choice);
        await host.Store.SaveTrackAsync($"technique:scales:{choice.Item("scales").Id}:{HandUse.Right}", Prima.Core.Practice.TechniqueState.New with { Tempo = 60 });
        await host.Store.SetSettingAsync(PlayAlong.LoopKeyOf("scales"), "on");
        host.Context.Services.GetRequiredService<NavigationManager>().NavigateTo("drills/scales");
        var view = host.Context.Render<TechniquePractice>();
        var bar = view.FindComponent<PlayAlong>();
        view.WaitForAssertion(() => Assert.True(bar.Instance.Looping && bar.Instance.Mode == PlayMode.InTime && bar.Instance.Music is not null));
        var once = bar.Instance.Music!.Notes.Count;

        // Twice round on the beat, and the third time's first note; then Stop.
        await view.InvokeAsync(() => bar.Instance.Start());
        var run = host.Context.Services.GetRequiredService<SessionRegistry>().Current!;
        foreach (var note in run.Expected.Take(2 * once + 1))
        {
            clock.NowNs = run.TimeAt(note.Beat);
            piano.Play(note.Midi, clock.NowNs);
            clock.NowNs += 300_000_000;
            piano.Release(note.Midi, clock.NowNs);
        }
        await view.InvokeAsync(bar.Instance.Stop);

        // The review leads with the passes; under Show details the evenness is there, and before it the line every screen's
        // summary gives a loop. One hand played, so none is named.
        view.WaitForAssertion(() => Assert.StartsWith("Twice round, every one clean", view.Find(".review-did span:last-child").TextContent));
        await view.Find(".sum-more").ClickAsync(new MouseEventArgs());
        view.WaitForAssertion(() => Assert.NotEmpty(view.FindAll(".summary-words li")));
        var words = view.FindAll(".summary-words li").Select(li => li.TextContent).ToList();
        Assert.StartsWith("2 passes of ", words[0]);
        Assert.EndsWith("The rest is the last pass.", words[0]);
        Assert.Contains(words, w => w.StartsWith("Evenness"));
        Assert.DoesNotContain(words, w => w.Contains("hand"));
    }

    [Fact]
    public async Task Round_a_loop_a_drills_sets_follow_one_another()
    {
        var (piano, clock) = (new TestPiano(plugged: true), new SetClock());
        await using var host = new ComponentHost(keyboard: piano, clock: clock);
        var registry = host.Context.Services.GetRequiredService<SessionRegistry>();
        var view = host.Context.Render<ItemDrill>(p => p.Add(c => c.Key, "geography"));
        view.WaitForAssertion(() => Assert.NotNull(registry.DrillAnswer));
        await view.Find(".drill-loop .play-mode-option").ClickAsync(new MouseEventArgs());
        view.WaitForAssertion(() => Assert.Equal("true", view.Find(".drill-loop .play-mode-option").GetAttribute("aria-checked")));
        Assert.Equal("on", (await host.Store.GetSettingsAsync())[PlayAlong.LoopKeyOf("drill-geography")]);

        // A set of ten, each played right. The sheet says it's painted only when its music changes, as in the browser.
        var (itemReadout, painted) = (".reading-bar .mu-readout-value", "");
        for (var played = 0; played < 10; played++)
        {
            var sheet = view.FindComponent<Sheet>().Instance;
            if (sheet.Mei != painted) await view.InvokeAsync(() => sheet.OnPainted.InvokeAsync(clock.NowNs));
            painted = sheet.Mei;
            var item = view.Find(itemReadout).TextContent;
            clock.NowNs += 100_000_000;
            piano.Play(registry.DrillAnswer!(true)[0][0], clock.NowNs);
            view.WaitForAssertion(() => Assert.NotEqual(item, view.Find(itemReadout).TextContent), TimeSpan.FromSeconds(5));
        }

        // The set is kept, and the next has begun by itself: no summary to stop at, and how the last one went in the bar.
        view.WaitForAssertion(() => Assert.Equal("1 of 10", view.Find(itemReadout).TextContent), TimeSpan.FromSeconds(5));
        Assert.Empty(view.FindAll("[data-piano='go']"));
        Assert.Contains("Last set", view.Find(".reading-bar").TextContent);
        Assert.Contains("100%", view.Find(".reading-bar").TextContent);
        Assert.Single(await host.Store.GetAttemptsAsync(DateOnly.FromDateTime(DateTime.Now), "geography"));
    }
}
