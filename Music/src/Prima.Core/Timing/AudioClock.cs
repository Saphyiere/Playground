namespace Prima.Core.Timing;

/// <summary>
/// Puts the click's audio on the system clock. Android's audio timestamp pairs a frame the speaker has played with the moment it
/// played, in nanoseconds on CLOCK_MONOTONIC, the clock MIDI timestamps use. Any other frame then sounds that many frames later
/// (or earlier), at the sample rate.
/// </summary>
public static class AudioClock
{
    /// <summary>When <paramref name="frame"/> sounds, given a timestamp that frame <paramref name="stampFrame"/> sounded at <paramref name="stampNs"/>.</summary>
    public static long TimeOf(long frame, long stampFrame, long stampNs, int sampleRate) => stampNs + (frame - stampFrame) * 1_000_000_000L / sampleRate;

    /// <summary>The frame a time falls on, the other way round.</summary>
    public static long FrameAt(long timeNs, long stampFrame, long stampNs, int sampleRate) => stampFrame + (timeNs - stampNs) * sampleRate / 1_000_000_000L;
}
