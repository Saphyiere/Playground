using Prima.Core.Music;

namespace Prima.Core.Library;

/// <summary>
/// A piece's opening, to hear before choosing it in the Library: its notes from the start for about half a minute, in seconds,
/// from its notation at the tempo it starts at (its first mark, else the one it goes at most, else 80 a minute), or from the MIDI a
/// PDF came with; and those notes as a small MIDI file, for a player that takes one (the tablet's own, without the piano).
/// </summary>
public static class Opening
{
    /// <summary>How much of a piece is heard, in seconds.</summary>
    public const double Seconds = 30;

    // A note's last moment, past the opening's end, so the last chord rings a little rather than stopping dead.
    private const double Ring = 1.5;

    /// <summary>The opening of a piece's notation: each note it expects played (a tied note once, held through its ties).</summary>
    public static IReadOnlyList<FileNote> Of(Score score, double seconds = Seconds)
    {
        var changes = score.TempoChanges();
        var perMinute = changes.Count > 0 && changes[0] is { Index: 0, Tick: 0 } first ? first.PerMinute : score.MainTempo() ?? 80;
        var secondsPerTick = 60 / perMinute / Ticks.Quarter;
        return [.. score.Expected()
            .Select(n => new FileNote(n.Midi, n.Tick * secondsPerTick, (n.Tick + n.Length) * secondsPerTick, 72))
            .Where(n => n.Start < seconds)
            .Select(n => n with { End = Math.Min(n.End, seconds + Ring) })
            .OrderBy(n => n.Start)
            .ThenBy(n => n.Midi)];
    }

    /// <summary>The opening of the MIDI a PDF came with, from its first note.</summary>
    public static IReadOnlyList<FileNote> Of(MidiFile midi, double seconds = Seconds)
    {
        if (midi.Notes.Count == 0) return [];

        // A file that waits before its first note (a count-in, a pickup written from the bar's start) starts at that note.
        var start = midi.Notes.Min(n => n.Start);
        return [.. midi.Notes
            .Where(n => n.Start - start < seconds)
            .Select(n => new FileNote(n.Midi, n.Start - start, Math.Min(n.End, start + seconds + Ring) - start, n.Velocity))
            .OrderBy(n => n.Start)
            .ThenBy(n => n.Midi)];
    }

    /// <summary>
    /// The notes as a Standard MIDI File: one track, a grand piano on the first channel, 120 quarters a minute and 480 ticks a
    /// quarter, so 960 ticks a second.
    /// </summary>
    public static byte[] ToMidi(IReadOnlyList<FileNote> notes)
    {
        const int TicksPerSecond = 960;
        var events = notes
            .SelectMany(n => new[]
            {
                (Tick: (long)Math.Round(n.Start * TicksPerSecond), On: true, n.Midi, Velocity: Math.Clamp(n.Velocity, 1, 127)),
                (Tick: (long)Math.Round(Math.Max(n.End, n.Start + 0.01) * TicksPerSecond), On: false, n.Midi, Velocity: 64),
            })
            // At one moment, a key let go before it's struck again.
            .OrderBy(e => e.Tick).ThenBy(e => e.On);

        var track = new List<byte>();
        track.AddRange([0x00, 0xFF, 0x51, 0x03, 0x07, 0xA1, 0x20]); // 500,000 µs a quarter: 120 a minute
        track.AddRange([0x00, 0xC0, 0x00]); // the first channel's voice: acoustic grand piano
        var last = 0L;
        foreach (var e in events)
        {
            Delta(track, e.Tick - last);
            last = e.Tick;
            track.AddRange([(byte)(e.On ? 0x90 : 0x80), (byte)Math.Clamp(e.Midi, 0, 127), (byte)e.Velocity]);
        }
        track.AddRange([0x00, 0xFF, 0x2F, 0x00]); // the track's end

        byte[] header = [(byte)'M', (byte)'T', (byte)'h', (byte)'d', 0, 0, 0, 6, 0, 0, 0, 1, 0x01, 0xE0];
        byte[] chunk = [(byte)'M', (byte)'T', (byte)'r', (byte)'k', (byte)(track.Count >> 24), (byte)(track.Count >> 16), (byte)(track.Count >> 8), (byte)track.Count];
        return [.. header, .. chunk, .. track];
    }

    // A delta time as MIDI writes it: seven bits a byte, the high bit set on all but the last.
    private static void Delta(List<byte> into, long value)
    {
        var bytes = new Stack<byte>();
        bytes.Push((byte)(value & 0x7F));
        while ((value >>= 7) > 0) bytes.Push((byte)(value & 0x7F | 0x80));
        into.AddRange(bytes);
    }
}
