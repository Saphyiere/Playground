namespace Prima.Core.Timing;

/// <summary>The click's two sounds: a short beep, or a dry wood-block tick. The downbeat is higher in both.</summary>
public enum ClickSoundKind
{
    Beep,
    Wood,
}

/// <summary>Draws one click as samples, for the host's audio track to place on exact frames.</summary>
public static class ClickSound
{
    /// <summary>How long a click lasts, in ms.</summary>
    public const int LengthMs = 15;

    /// <summary>One click at <paramref name="sampleRate"/>, between -1 and 1, scaled by <paramref name="volume"/> (0 to 1).</summary>
    public static float[] Render(ClickSoundKind kind, bool accent, int sampleRate, double volume)
    {
        var length = sampleRate * LengthMs / 1000;
        var samples = new float[length];
        // A beep is a sine at A6 (A7 on the downbeat); a tick is two higher partials that die away fast, like a wood block.
        var pitch = kind == ClickSoundKind.Beep ? (accent ? 3520.0 : 1760.0) : (accent ? 2400.0 : 1800.0);
        for (var i = 0; i < length; i++)
        {
            var t = i / (double)sampleRate;
            var attack = Math.Min(1, i / (sampleRate * 0.0008));
            var decay = Math.Exp(-t / (kind == ClickSoundKind.Beep ? 0.006 : 0.0025));
            var wave = kind == ClickSoundKind.Beep
                ? Math.Sin(2 * Math.PI * pitch * t)
                : 0.6 * Math.Sin(2 * Math.PI * pitch * t) + 0.4 * Math.Sin(2 * Math.PI * pitch * 2.7 * t);
            samples[i] = (float)(wave * attack * decay * volume * (accent ? 1 : 0.8));
        }
        return samples;
    }
}
