using Prima.Core.Keyboard;

namespace Prima.Core.Tests;

public class OneVoiceTests
{
    private static MidiEvent On(int channel, int note) => new(MidiKind.NoteOn, channel, note, 70, 0);

    private static MidiEvent Off(int channel, int note) => new(MidiKind.NoteOff, channel, note, 0, 0);

    private static MidiEvent Pedal(int channel, int value) => new(MidiKind.Control, channel, MidiEvent.Damper, value, 0);

    // What gets through, as "+57" for a key down, "-57" for a key up, "p127" for a pedal's value.
    private static List<string> Heard(params MidiEvent[] events)
    {
        var voice = new OneVoice();
        return [.. events.Where(voice.Hears).Select(e => e.Kind switch { MidiKind.NoteOn => $"+{e.Note}", MidiKind.NoteOff => $"-{e.Note}", _ => $"p{e.Data2}" })];
    }

    [Fact]
    public void WithOneVoiceEveryMessagePasses()
    {
        Assert.Equal(["+57", "+45", "p127", "-57", "-45", "p0", "+57", "-57"], Heard(On(1, 57), On(1, 45), Pedal(1, 127), Off(1, 57), Off(1, 45), Pedal(1, 0), On(1, 57), Off(1, 57)));
    }

    [Fact]
    public void AKeyWithTwoVoicesLayeredOnItIsPressedOnce()
    {
        // The P-225's Dual: the key's note on channel 1 and again on channel 2, down and up.
        Assert.Equal(["+57", "-57"], Heard(On(1, 57), On(2, 57), Off(1, 57), Off(2, 57)));
        // A chord, key by key or voice by voice, and let go in another order: three keys down, three up, each up at its last.
        Assert.Equal(["+57", "+45", "+52", "-57", "-45", "-52"], Heard(On(1, 57), On(2, 57), On(1, 45), On(2, 45), On(1, 52), On(2, 52), Off(1, 57), Off(2, 57), Off(1, 45), Off(2, 45), Off(1, 52), Off(2, 52)));
        Assert.Equal(["+57", "+45", "+52", "-57", "-45", "-52"], Heard(On(1, 57), On(1, 45), On(1, 52), On(2, 57), On(2, 45), On(2, 52), Off(2, 57), Off(1, 57), Off(2, 45), Off(2, 52), Off(1, 45), Off(1, 52)));
        // Struck again after it's let go, it's a new press.
        Assert.Equal(["+57", "-57", "+57", "-57"], Heard(On(1, 57), On(2, 57), Off(1, 57), Off(2, 57), On(1, 57), On(2, 57), Off(1, 57), Off(2, 57)));
    }

    [Fact]
    public void APedalSaidOnBothChannelsMovesOnce()
    {
        Assert.Equal(["p127", "p0"], Heard(Pedal(1, 127), Pedal(2, 127), Pedal(1, 0), Pedal(2, 0)));
        // A half pedal's values pass as they change.
        Assert.Equal(["p40", "p80", "p127", "p0"], Heard(Pedal(1, 40), Pedal(2, 40), Pedal(1, 80), Pedal(2, 80), Pedal(1, 127), Pedal(2, 127), Pedal(1, 0), Pedal(2, 0)));
    }

    [Fact]
    public void KeysOnDifferentChannelsAreDifferentKeys()
    {
        // A split keyboard: the left hand's voice on one channel, the right's on another, a key only ever on one.
        Assert.Equal(["+45", "+57", "-45", "-57"], Heard(On(3, 45), On(1, 57), Off(3, 45), Off(1, 57)));
    }

    [Fact]
    public void ALostReleaseCostsNothing()
    {
        // The key's release never came: struck again on the same channel it's a new press, and its second voice is still one.
        Assert.Equal(["+57", "+57", "-57"], Heard(On(1, 57), On(2, 57), On(1, 57), On(2, 57), Off(1, 57), Off(2, 57)));
        // A release for a key that isn't down passes, as it did before.
        Assert.Equal(["-57"], Heard(Off(1, 57)));
    }

    [Fact]
    public void ClearForgetsWhatsDown()
    {
        var voice = new OneVoice();
        Assert.True(voice.Hears(On(1, 57)));
        Assert.True(voice.Hears(Pedal(1, 127)));
        voice.Clear();

        Assert.True(voice.Hears(On(2, 57)));
        Assert.True(voice.Hears(Pedal(2, 127)));
    }
}
