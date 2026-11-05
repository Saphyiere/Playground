namespace Prima.Core.Keyboard;

/// <summary>What a MIDI message from the piano was about. Only what Prima listens to is kept.</summary>
public enum MidiKind
{
    NoteOn,
    NoteOff,
    /// <summary>A controller change: the pedals (64 damper, 66 sostenuto, 67 soft) among others.</summary>
    Control,
    /// <summary>The piano saying it's still there (FE), about every 300 ms on Yamaha pianos. Its arrival times show the MIDI path's jitter.</summary>
    ActiveSensing,
}

/// <summary>
/// One message from the piano and when it arrived, in nanoseconds on the system's monotonic clock (Android's System.nanoTime, the
/// clock the click's audio timestamps use too). Channels are 1 to 16.
/// </summary>
public readonly record struct MidiEvent(MidiKind Kind, int Channel, int Data1, int Data2, long TimeNs)
{
    public const int Damper = 64;
    public const int Sostenuto = 66;
    public const int Soft = 67;

    /// <summary>The key, as a MIDI note number (21 is A0, 60 middle C, 108 C8), for note events.</summary>
    public int Note => Data1;

    /// <summary>How hard the key was struck (1 to 127), for a note-on.</summary>
    public int Velocity => Data2;
}
