using Prima.Core.Timing;
using Prima.UI.Host;

namespace Prima.UI.Tests;

/// <summary>
/// The click on the piano, heard in its headphones: Prima's own click as audio over USB where the tablet lists the piano as an
/// audio output, else the piano's own sounds over MIDI, as Settings chooses.
/// </summary>
public class ClickOnThePianoTests
{
    // The keys the piano is sent for the first beats of a bar of four: their status bytes and their notes, note-ons only.
    private static async Task<(int Status, int Key)[]> Sent(PianoClickSound sound, int beats = 3)
    {
        var (piano, clock) = (new TestPiano(plugged: true, takesNotes: true), new SetClock());
        var settings = new PrimaSettings { ClickOnPiano = true, PianoClick = sound };
        var click = new Click(new Speaker(canPlayOnPiano: false), piano, clock, settings);
        click.Start(new ClickPlan(96, 4, 0, ClickStage.Throughout));
        // The beats are sent half a second ahead of the clock, a tenth of a second at a time.
        clock.NowNs = 3_000_000_000;
        for (var i = 0; i < 50 && piano.Sent.Count(m => (m[0] & 0xF0) == 0x90) < beats; i++) await Task.Delay(50);
        click.Stop();
        return [.. piano.Sent.Where(m => (m[0] & 0xF0) == 0x90).Take(beats).Select(m => ((int)m[0], (int)m[1]))];
    }

    [Fact]
    public async Task ThePianosMetronomeIsItsDrumsClickWithItsBellOnTheFirstBeat()
    {
        Assert.Equal([(0x99, 34), (0x99, 33), (0x99, 33)], await Sent(PianoClickSound.MetronomeClick));
    }

    [Fact]
    public async Task ItsWoodBlocksAreHighOnTheFirstBeatAndLowOnTheOthers()
    {
        Assert.Equal([(0x99, 76), (0x99, 77), (0x99, 77)], await Sent(PianoClickSound.WoodBlock));
    }

    [Fact]
    public async Task APianoKeyIsAHighCThenAGOnPrimasOwnChannel()
    {
        Assert.Equal([(0x9F, 96), (0x9F, 91), (0x9F, 91)], await Sent(PianoClickSound.PianoKey));
    }

    [Fact]
    public async Task PrimasClickWithNoWayToThePianosAudioIsAPianoKeyAsBefore()
    {
        Assert.Equal([(0x9F, 96), (0x9F, 91), (0x9F, 91)], await Sent(PianoClickSound.Audio));
    }

    [Fact]
    public void PrimasClickGoesToThePianosAudioWhereTheTabletCanSendIt()
    {
        var (piano, speaker) = (new TestPiano(plugged: true, takesNotes: true), new Speaker(canPlayOnPiano: true));
        var click = new Click(speaker, piano, new SetClock(), new PrimaSettings { ClickOnPiano = true, PianoClick = PianoClickSound.Audio, ClickSound = ClickSoundKind.Beep });
        click.Start(new ClickPlan(96, 4));
        Assert.Equal((true, ClickSoundKind.Beep), (speaker.OnPiano, speaker.Sound));
        Assert.True(click.OnPiano);
        Assert.Empty(piano.Sent);

        // On the tablet, it's the tablet's, wherever the tablet's sound goes.
        var tablet = new Click(speaker, piano, new SetClock(), new PrimaSettings { ClickOnPiano = false, PianoClick = PianoClickSound.Audio });
        tablet.Start(new ClickPlan(96, 4));
        Assert.False(speaker.OnPiano);
        Assert.False(tablet.OnPiano);
    }

    // A tablet's click that says whether it can reach the piano's audio, and what it was asked.
    private sealed class Speaker(bool canPlayOnPiano) : IClick
    {
        public bool Running { get; private set; }

        public bool Approximate => false;

        public bool CanPlayOnPiano => canPlayOnPiano;

        public bool OnPiano { get; private set; }

        public ClickSoundKind Sound { get; private set; }

        public void Start(ClickPlan plan, ClickSoundKind sound, double volume, bool onPiano) => (Running, Sound, OnPiano) = (true, sound, onPiano);

        public void Stop() => Running = false;

        public long BeatTime(int beat) => 0;
    }
}
