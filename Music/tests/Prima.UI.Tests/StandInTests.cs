using Bunit;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Prima.Core.Keyboard;
using Prima.Core.Music;
using Prima.UI.Components;
using Prima.UI.Host;
using Prima.UI.Pages;
using Prima.UI.Practice;

namespace Prima.UI.Tests;

/// <summary>
/// The stand-in piano: in front of the tablet's piano in a build from the PC, the piano's keys and plugging still come through and
/// what Prima sends still reaches it; turned on, Prima plays as if a piano were plugged in; and its player plays a run through and
/// answers a drill's item, as the preview's checks and DevTools have it do.
/// </summary>
public class StandInTests
{
    [Fact]
    public void In_front_of_the_piano_its_keys_and_plugging_come_through_and_what_Prima_sends_reaches_it()
    {
        var (clock, piano) = (new SetClock(), new TestPiano(takesNotes: true));
        var standIn = new StandInKeyboard(clock, piano);
        var (heard, changes) = (new List<MidiEvent>(), 0);
        standIn.Received += heard.Add;
        standIn.Changed += () => changes++;

        // No piano and the stand-in off: nothing's plugged in. On, Prima plays as if one were, and its keys come through.
        Assert.Null(standIn.Name);
        Assert.False(standIn.CanSend);
        standIn.Plug(true, StandInKeyboard.Called);
        Assert.Equal((StandInKeyboard.Called, true, 1), (standIn.Name, standIn.CanSend, changes));
        standIn.Play([60, 64], 70, 200);
        Assert.Equal([(MidiKind.NoteOn, 60), (MidiKind.NoteOn, 64), (MidiKind.NoteOff, 60), (MidiKind.NoteOff, 64)], heard.Select(e => (e.Kind, e.Data1)));

        // The piano plugged in: it's the piano that's named, its keys come through, and what Prima sends reaches it.
        piano.Plug();
        Assert.Equal(("P-225B", 2), (standIn.Name, changes));
        piano.Play(67, 5);
        Assert.Equal((MidiKind.NoteOn, 67), (heard[^1].Kind, heard[^1].Data1));
        standIn.Send([0x90, 72, 64], 10);
        Assert.Equal([72], piano.Sent.Select(m => (int)m[1]));
        Assert.Single(standIn.Sent);

        // Off, the piano is still there.
        standIn.Plug(false);
        Assert.Equal(("P-225B", true), (standIn.Name, standIn.CanSend));
    }

    [Fact]
    public async Task Its_player_answers_a_drills_item_and_plays_a_run_through()
    {
        // The time as it passes: a chord's keys a quarter of a second apart are that far apart.
        var clock = new StopwatchClock();
        var standIn = new StandInKeyboard(clock);
        await using var host = new ComponentHost(keyboard: standIn, clock: clock);
        var registry = host.Context.Services.GetRequiredService<SessionRegistry>();
        var player = new StandInPlayer(standIn, registry, clock);
        Assert.Equal("no drill item", await player.Answer());
        Assert.Equal("no run in progress", player.Perform(new()));
        Assert.Equal("connected", player.Plug(true));

        // A Chords item answered a key at a time.
        var drill = host.Context.Render<ItemDrill>(p => p.Add(c => c.Key, "chords"));
        drill.WaitForAssertion(() => Assert.NotNull(registry.DrillAnswer));
        var sheet = drill.FindComponent<Sheet>().Instance;
        await drill.InvokeAsync(() => sheet.OnPainted.InvokeAsync(clock.NowNs));
        Assert.Equal("answered", await player.Answer(apartMs: 250));
        drill.WaitForAssertion(() => Assert.Contains("played a key at a time", drill.Find(".drill-feedback").TextContent), TimeSpan.FromSeconds(5));

        // A run In time played through, every note right.
        var score = new Score(new Prima.Core.Music.Key(0), new Meter(4, 4), [new Bar(1, [new Voice(1,
            [new Notes("a", 0, Ticks.Half, [new Tone("a", new Spelled(Step.C, 0, 4))]), new Notes("b", Ticks.Half, Ticks.Half, [new Tone("b", new Spelled(Step.E, 0, 4))])])])]);
        var bar = host.Context.Render<Sheet>(p => p.Add(c => c.Mei, MeiWriter.Write(score)));
        var finished = new List<PlayOutcome>();
        var run = host.Context.Render<PlayAlong>(p => p.Add(c => c.Name, "stand-in").Add(c => c.Music, new PlayMusic(score, score.Expected()))
            .Add(c => c.SheetOf, () => bar.Instance).Add(c => c.Tempo, 60).Add(c => c.Ask, PlayMode.InTime).Add(c => c.OnFinished, how => finished.Add(how)));
        run.WaitForAssertion(() => Assert.Equal(PlayMode.InTime, run.Instance.Mode));
        await run.InvokeAsync(() => run.Instance.Start());
        Assert.Equal("performed 2 notes", await run.InvokeAsync(() => player.Perform(new(Right: 1, Lean: 0, Spread: 4))));
        run.WaitForAssertion(() => Assert.Single(finished));
        Assert.Equal((2, 2), (finished[0].InTime!.Right, finished[0].InTime!.Expected));
    }
}
