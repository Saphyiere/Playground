using System.Buffers.Binary;

namespace Prima.Core.Library;

/// <summary>A note in a MIDI file: its key, when it starts and ends in seconds, and how hard it's struck.</summary>
public sealed record FileNote(int Midi, double Start, double End, int Velocity);

/// <summary>
/// A Standard MIDI File, read for what a PDF piece needs from the MIDI made with it: the notes in seconds (to hear the piece on the
/// piano), the first tempo and time signature (for the scroll's defaults), and how many bars there are.
/// </summary>
public sealed record MidiFile(IReadOnlyList<FileNote> Notes, double Bpm, int BeatsPerBar, int BeatUnit, double Seconds, int Bars)
{
    public static MidiFile Read(ReadOnlySpan<byte> data)
    {
        if (data.Length < 14 || !data[..4].SequenceEqual("MThd"u8)) throw new FormatException("Not a MIDI file.");

        var division = BinaryPrimitives.ReadUInt16BigEndian(data[12..14]);
        if ((division & 0x8000) != 0) throw new FormatException("Timecode-based MIDI files aren't read.");

        // Every event from every track, by tick; then the tempo map turns ticks into seconds.
        var tempos = new List<(long Tick, int MicrosPerQuarter)>();
        var meters = new List<(long Tick, int Count, int Unit)>();
        var ons = new Dictionary<(int Channel, int Key), Stack<(long Tick, int Velocity)>>();
        var notes = new List<(int Key, long On, long Off, int Velocity)>();
        var lastTick = 0L;

        var at = 8 + (int)BinaryPrimitives.ReadUInt32BigEndian(data[4..8]);
        while (at + 8 <= data.Length)
        {
            var length = (int)BinaryPrimitives.ReadUInt32BigEndian(data[(at + 4)..(at + 8)]);
            var isTrack = data[at..(at + 4)].SequenceEqual("MTrk"u8);
            var end = Math.Min(data.Length, at + 8 + length);
            if (isTrack)
            {
                var track = data[(at + 8)..end];
                var i = 0;
                var tick = 0L;
                byte status = 0;
                while (i < track.Length)
                {
                    tick += VarInt(track, ref i);
                    if (i >= track.Length) break;

                    var b = track[i];
                    if (b >= 0x80) { status = b; i++; }
                    if (status == 0xFF)
                    {
                        var type = track[i++];
                        var size = (int)VarInt(track, ref i);
                        var meta = track.Slice(i, Math.Min(size, track.Length - i));
                        if (type == 0x51 && size == 3) tempos.Add((tick, meta[0] << 16 | meta[1] << 8 | meta[2]));
                        else if (type == 0x58 && size >= 2) meters.Add((tick, meta[0], 1 << meta[1]));
                        i += size;
                        status = 0;
                    }
                    else if (status is 0xF0 or 0xF7)
                    {
                        i += (int)VarInt(track, ref i);
                        status = 0;
                    }
                    else if (status >= 0x80)
                    {
                        var kind = status & 0xF0;
                        var channel = status & 0x0F;
                        var d1 = track[i++];
                        var d2 = kind is 0xC0 or 0xD0 ? (byte)0 : track[i++];
                        if (kind == 0x90 && d2 > 0) Push(ons, (channel, d1), (tick, d2));
                        else if (kind == 0x80 || kind == 0x90 && d2 == 0)
                        {
                            if (ons.TryGetValue((channel, d1), out var open) && open.Count > 0)
                            {
                                var (onTick, velocity) = open.Pop();
                                notes.Add((d1, onTick, tick, velocity));
                            }
                        }
                    }
                    else i++;
                    lastTick = Math.Max(lastTick, tick);
                }
            }
            at = end;
        }

        tempos.Sort((a, b) => a.Tick.CompareTo(b.Tick));
        double Seconds(long tick)
        {
            var seconds = 0.0;
            var (from, micros) = (0L, 500_000);
            foreach (var (t, m) in tempos)
            {
                if (t >= tick) break;
                seconds += (t - from) * micros / 1e6 / division;
                (from, micros) = (t, m);
            }
            return seconds + (tick - from) * micros / 1e6 / division;
        }

        var meter = meters.OrderBy(m => m.Tick).Select(m => (m.Count, m.Unit)).DefaultIfEmpty((Count: 4, Unit: 4)).First();
        var bpm = 60_000_000.0 / (tempos.Count > 0 ? tempos[0].MicrosPerQuarter : 500_000) * meter.Unit / 4;
        var barTicks = (long)division * 4 * meter.Count / meter.Unit;
        var lastNote = notes.Count == 0 ? 0 : notes.Max(n => n.Off);
        return new MidiFile([.. notes.OrderBy(n => n.On).Select(n => new FileNote(n.Key, Seconds(n.On), Seconds(n.Off), n.Velocity))], bpm, meter.Count, meter.Unit,
            Seconds(Math.Max(lastNote, 0)), barTicks > 0 ? (int)Math.Ceiling(lastNote / (double)barTicks) : 0);
    }

    private static void Push(Dictionary<(int, int), Stack<(long, int)>> ons, (int, int) key, (long, int) value)
    {
        if (!ons.TryGetValue(key, out var stack)) ons[key] = stack = new Stack<(long, int)>();
        stack.Push(value);
    }

    private static long VarInt(ReadOnlySpan<byte> data, ref int i)
    {
        long value = 0;
        for (var k = 0; k < 4 && i < data.Length; k++)
        {
            var b = data[i++];
            value = value << 7 | (uint)(b & 0x7F);
            if ((b & 0x80) == 0) break;
        }
        return value;
    }
}
