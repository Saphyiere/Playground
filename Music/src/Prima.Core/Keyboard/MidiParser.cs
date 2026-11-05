namespace Prima.Core.Keyboard;

/// <summary>
/// Turns the bytes the piano sends into messages. Android hands them over as they arrive: a call may hold several messages, or
/// part of one, and real-time bytes (Active Sensing, clock) can sit in the middle of another message. So the parser keeps its place
/// between calls, takes real-time bytes out first, follows running status (a status byte left out because it repeats), and skips
/// SysEx and system common messages. A note-on with velocity 0 is a note-off. One parser per input port; not thread-safe.
/// </summary>
public sealed class MidiParser
{
    private int status;
    private int needed;
    private int first;
    private int count;
    private bool inSysEx;

    /// <summary>Reads bytes that arrived at <paramref name="timeNs"/>, adding each message they complete to <paramref name="into"/>.</summary>
    public void Read(ReadOnlySpan<byte> bytes, long timeNs, List<MidiEvent> into)
    {
        foreach (var b in bytes)
        {
            // System real-time (F8-FF) may come anywhere, even inside SysEx, and changes nothing else.
            if (b >= 0xF8)
            {
                if (b == 0xFE) into.Add(new MidiEvent(MidiKind.ActiveSensing, 0, 0, 0, timeNs));
                continue;
            }

            if (b == 0xF0)
            {
                (inSysEx, status, count) = (true, 0, 0);
                continue;
            }

            // End of SysEx, or system common (F1-F6), which ends running status; its data bytes then have no status and are dropped.
            if (b >= 0xF1)
            {
                (inSysEx, status, count) = (false, 0, 0);
                continue;
            }

            if (b >= 0x80)
            {
                (inSysEx, status, count) = (false, b, 0);
                needed = (b & 0xF0) is 0xC0 or 0xD0 ? 1 : 2;
                continue;
            }

            if (inSysEx || status == 0) continue;

            if (count == 0) first = b;
            if (++count < needed) continue;

            // Complete. The status stays for running status: the next data byte starts another message like this one.
            count = 0;
            var channel = (status & 0x0F) + 1;
            switch (status & 0xF0)
            {
                case 0x90 when b > 0: into.Add(new MidiEvent(MidiKind.NoteOn, channel, first, b, timeNs)); break;
                case 0x90 or 0x80: into.Add(new MidiEvent(MidiKind.NoteOff, channel, first, b, timeNs)); break;
                case 0xB0: into.Add(new MidiEvent(MidiKind.Control, channel, first, b, timeNs)); break;
            }
        }
    }
}
