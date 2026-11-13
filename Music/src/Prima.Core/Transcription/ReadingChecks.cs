using System.Globalization;
using Prima.Core.Music;

namespace Prima.Core.Transcription;

/// <summary>
/// What a reading must get right before it's taken: each voice fills its bar, every pitch is on the piano, every tie lands, and
/// the staves and voices are as a piano score has them. Each problem is a sentence, which Claude reads when it's asked again.
/// </summary>
public static class ReadingChecks
{
    private static readonly int[] Values = [Ticks.Whole, Ticks.Half, Ticks.Quarter, Ticks.Eighth, Ticks.Sixteenth, Ticks.Sixteenth / 2, Ticks.Sixteenth / 4];

    /// <summary>
    /// How long an event lasts, in ticks, from its value, dots and tuplet; for a grace note, its printed value, though it takes no
    /// time from the bar. Null when that isn't a whole number of ticks (a septuplet's notes, say).
    /// </summary>
    public static int? Length(ReadEvent e)
    {
        var dots = Math.Clamp(e.Dots, 0, 2);
        // A dot adds half the value, a second a quarter: value × (2^(dots+1) - 1) / 2^dots.
        var top = Values[(int)e.Value] * ((1 << (dots + 1)) - 1) * (e.Tuplet == 0 ? 1 : Normal(e.Tuplet));
        var bottom = (1 << dots) * (e.Tuplet == 0 ? 1 : e.Tuplet);
        return top % bottom == 0 ? top / bottom : null;
    }

    /// <summary>The notes a tuplet takes the time of: a triplet two, a duplet or a quadruplet (in compound time) three, five to seven four.</summary>
    public static int Normal(int tuplet) => tuplet switch { 2 or 4 => 3, 3 => 2, < 8 => 4, _ => 8 };

    /// <summary>The sharp or flat an accidental puts on its note; none and a natural put none.</summary>
    public static int Alter(Accidental accidental) => accidental switch
    {
        Accidental.Sharp => 1,
        Accidental.Flat => -1,
        Accidental.DoubleSharp => 2,
        Accidental.DoubleFlat => -2,
        _ => 0,
    };

    /// <summary>The problems with a system's reading; none when it's sound. The piece's first system may start with a pickup.</summary>
    public static IReadOnlyList<string> Problems(SystemReading reading, bool first)
    {
        // Nullable annotations don't reject null items inside a JSON array. Check the shape before following any ties.
        if (reading.Bars is null || reading.Doubts is null || reading.Doubts.Any(d => d is null)) return ["The reading has missing bars or doubts."];
        foreach (var bar in reading.Bars)
        {
            if (bar?.Staves is null) return ["The reading has a missing bar or staff list."];
            foreach (var staff in bar.Staves)
            {
                if (staff?.Voices is null) return ["The reading has a missing staff or voice list."];
                foreach (var voice in staff.Voices)
                {
                    if (voice?.Events is null) return ["The reading has a missing voice or event list."];
                    foreach (var e in voice.Events)
                    {
                        if (e?.Pitches is null || e.Marks is null || e.Pitches.Any(p => p is null)) return ["The reading has a missing event, pitch or mark list."];
                    }
                }
            }
        }
        if (reading.Bars.Count == 0) return ["No bars were read: read every bar of the system."];
        var problems = new List<string>();
        static string Beats(int ticks) => (ticks / (double)Ticks.Quarter).ToString("0.##", CultureInfo.InvariantCulture);
        for (var b = 0; b < reading.Bars.Count; b++)
        {
            var bar = reading.Bars[b];
            var where = $"Bar {bar.Index}";
            if (bar.Index != b + 1) problems.Add($"{where} is bar {b + 1} from the left: number the bars 1, 2, 3 and on, in order.");
            if (bar.Fifths is < -7 or > 7) problems.Add($"{where}: a key signature has 7 sharps or flats at most, not {Math.Abs(bar.Fifths)}.");
            if (bar.Ending is < 0 or > 3) problems.Add($"{where}: the ending is 0 for none, or 1, 2 or 3.");
            var metered = bar.MeterCount is >= 1 and <= 32 && bar.MeterUnit is 1 or 2 or 4 or 8 or 16;
            if (!metered) problems.Add($"{where}: {bar.MeterCount}/{bar.MeterUnit} isn't a time signature.");
            var barTicks = metered ? bar.MeterCount * Ticks.Whole / bar.MeterUnit : 0;
            if (!bar.Staves.Select(s => s.Staff).Order().SequenceEqual([1, 2])) problems.Add($"{where} needs both staves, 1 (upper) and 2 (lower), once each.");

            var voices = new List<(string Where, int Ticks)>();
            foreach (var staff in bar.Staves)
            {
                var staffName = staff.Staff == 1 ? "upper staff" : "lower staff";
                if (staff.Clef == ReadClef.None) problems.Add($"{where}, {staffName}: give the clef in force, treble or bass.");
                if (staff.Voices.Count == 0) problems.Add($"{where}, {staffName}: a staff has at least one voice, even if it's only a bar rest.");
                if (staff.Voices.Select(v => v.Voice).Distinct().Count() != staff.Voices.Count || staff.Voices.Any(v => v.Voice is < 1 or > 4)) problems.Add($"{where}, {staffName}: number its voices 1 to 4, once each.");
                foreach (var voice in staff.Voices)
                {
                    var at = $"{where}, {staffName}, voice {voice.Voice}";
                    var ticks = 0;
                    for (var i = 0; i < voice.Events.Count; i++)
                    {
                        var e = voice.Events[i];
                        if (e.Kind == ReadKind.Note && e.Pitches.Count == 0) problems.Add($"{at}: event {i + 1} is a note without a pitch.");
                        if (e.Kind != ReadKind.Note && e.Pitches.Count > 0) problems.Add($"{at}: event {i + 1} is a {e.Kind.ToString().ToLowerInvariant()}, which has no pitches.");
                        if (e.Kind == ReadKind.Clef && e.Clef == ReadClef.None) problems.Add($"{at}: event {i + 1} is a clef change without its clef.");
                        if (e.Dots is < 0 or > 2) problems.Add($"{at}: event {i + 1} has {e.Dots} dots; give 0, 1 or 2.");
                        if (e.Ottava is < -1 or > 2) problems.Add($"{at}: event {i + 1}'s ottava is {e.Ottava}; give 1 (8va), 2 (15ma), -1 (8vb) or 0.");
                        if (e.Tuplet is 1 or < 0 or > 9) problems.Add($"{at}: event {i + 1}'s tuplet is {e.Tuplet}; give the number printed over the group, or 0.");
                        var tied = e.Pitches.Where(p => p.Tie).Select(p => (p.Step, p.Octave)).ToList();
                        if (tied.Distinct().Count() != tied.Count) problems.Add($"{at}: event {i + 1} repeats a tied pitch; give each tied pitch once.");
                        foreach (var p in e.Pitches)
                        {
                            var sounding = new Spelled(p.Step, Alter(p.Accidental), p.Octave + e.Ottava);
                            if (sounding.Midi is < 21 or > 108) problems.Add($"{at}: {p.Step}{p.Octave} isn't on the piano; check the octave and the ledger lines.");
                            if (p.Finger is < 0 or > 5) problems.Add($"{at}: event {i + 1}'s finger is {p.Finger}; give 1 to 5 as printed, or 0 where none is.");
                            if (!p.Tie) continue;

                            // A tie lands on the same pitch in the voice's next note: later in this bar, or in its next bar, or in the next
                            // system when this is the system's last bar.
                            var next = voice.Events.Skip(i + 1).FirstOrDefault(n => !n.Grace && n.Kind != ReadKind.Clef);
                            var nextBar = b + 1 < reading.Bars.Count ? reading.Bars[b + 1] : null;
                            next ??= nextBar?.Staves.FirstOrDefault(s => s.Staff == staff.Staff)?.Voices.FirstOrDefault(v => v.Voice == voice.Voice)?.Events.FirstOrDefault(n => !n.Grace && n.Kind != ReadKind.Clef);
                            if (next is null && nextBar is null) continue;
                            if (next is null || !next.Pitches.Any(q => q.Step == p.Step && q.Octave + next.Ottava == p.Octave + e.Ottava)) problems.Add($"{at}: the tie from {p.Step}{p.Octave} doesn't reach a {p.Step}{p.Octave} in the voice's next note.");
                        }

                        if (e.Grace || e.Kind == ReadKind.Clef) continue;
                        if (e.Kind == ReadKind.BarRest) ticks += barTicks;
                        else if (Length(e) is { } length) ticks += length;
                        else problems.Add($"{at}: event {i + 1}, a {e.Value} in a tuplet of {e.Tuplet}, can't be timed; check the tuplet.");
                    }
                    voices.Add((at, ticks));
                }
            }

            // Every voice fills the bar. A bar can be short only as the piece's pickup, or where a repeat or the end cuts it short,
            // and then all its voices are as short.
            if (barTicks == 0 || voices.Count == 0) continue;
            var mayBeShort = (first && b == 0) || bar.RepeatStart || bar.End != BarEnd.Normal;
            var full = mayBeShort ? Math.Min(barTicks, voices.Max(v => v.Ticks)) : barTicks;
            foreach (var (at, ticks) in voices)
            {
                if (ticks > barTicks) problems.Add($"{at} adds up to {Beats(ticks)} quarter notes, but the bar holds {Beats(barTicks)}.");
                else if (ticks < full) problems.Add($"{at} adds up to {Beats(ticks)} quarter notes, short of the bar's {Beats(full)}: fill its silences with rests or spaces.");
            }
        }
        return problems;
    }
}
