namespace Prima.Core.Keyboard;

/// <summary>
/// The piano's messages with each thing the player does said once. With two voices layered on the keys (Yamaha's Dual), a key
/// sends its note on two channels, the second voice's on the channel after the first's, and a pedal may say its move on both.
/// Taken as they come, every key is pressed twice, and the second press lands on whatever the music has next. So a key is one
/// key whichever channels sound it: its note passes when it first goes down and when its last channel lets go, and a
/// controller's message passes when its value is new. One per input port; not thread-safe.
/// </summary>
public sealed class OneVoice
{
    // For each key, the channels it's down on, a bit each; for each controller, the value last passed, -1 before any.
    private readonly int[] down = new int[128];
    private readonly int[] values = [.. Enumerable.Repeat(-1, 128)];

    /// <summary>Whether a message says something new: a key going down, a key all the way up, a controller at another value.</summary>
    public bool Hears(MidiEvent e)
    {
        var channel = 1 << e.Channel;
        switch (e.Kind)
        {
            case MidiKind.NoteOn:
                // Down again on a channel it's already down on, its release was lost: that's a new press, on that channel alone.
                var pressed = down[e.Note] == 0 || (down[e.Note] & channel) != 0;
                down[e.Note] = pressed ? channel : down[e.Note] | channel;
                return pressed;
            case MidiKind.NoteOff:
                // A release for a key that isn't down passes, as any message did before.
                var held = down[e.Note];
                down[e.Note] &= ~channel;
                return held == 0 || (held & channel) != 0 && down[e.Note] == 0;
            case MidiKind.Control:
                var moved = values[e.Data1] != e.Data2;
                values[e.Data1] = e.Data2;
                return moved;
            default:
                return true;
        }
    }

    /// <summary>Forgets what's down: for a piano plugged in again.</summary>
    public void Clear()
    {
        Array.Clear(down);
        Array.Fill(values, -1);
    }
}
