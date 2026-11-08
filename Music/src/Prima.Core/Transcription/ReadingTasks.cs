using Playground.AI.Assist;

namespace Prima.Core.Transcription;

/// <summary>Reads one system of printed piano music, from its image, into a <see cref="SystemReading"/>.</summary>
/// <param name="png">The system, cut from its page.</param>
public sealed class ReadSystem(byte[] png) : AssistTask<SystemToRead, SystemReading>
{
    public override string Name => "prima.read-system";

    public override int Version => 1;

    public override string Instructions => """
        Read one system of printed piano music, from the image, into the JSON shape you're given: every bar from left to right,
        both staves, every voice, every note, rest and clef change. Prima turns it into notation to play along with, so read
        exactly what's printed.

        - Pitch: the letter and octave of the line or space each notehead sits on, in the clef in force (C4 is middle C), and the
          accidental printed just before it, or none. Don't apply the key signature or the bar's earlier accidentals: Prima does.
        - Ledger lines: count them for every notehead above or below the staff. A ledger line drawn for another note of a chord
          isn't a note; only noteheads are notes. In a chord stacked on ledger lines, check each notehead: on a line, or in the
          space between two.
        - Read what's printed, never what the harmony or the voice leading suggests. Where a note is hard to make out, give your
          best reading and list the bar under doubts.
        - A chord is one event with several pitches on one stem. Notes on separate stems at the same moment are separate voices.
        - Voices: voice 1 is the stems-up voice, or the staff's only one; voice 2 the stems-down voice where a staff has two. Each
          voice fills its bar: where it's silent and no rest is printed, a space of that length.
        - Values: the printed value and dots, and for a tuplet the number over its group, with tupletStart on the group's first
          note or rest. Grace notes are the small notes: grace true, with their printed value; they take no time from the bar.
        - Rests: as printed; a whole rest centred in a bar is a barRest.
        - Ties: tie true on a note tied to the same pitch in the voice's next note, also across the barline. A slur, a curve
          between different pitches, isn't a tie.
        - Clefs: each staff's clef in force at the start of the bar, and a clef event where it changes inside a bar.
        - Key and time signatures: the ones in force for every bar. The key signature is printed at the start of every system;
          the time signature only at the start of the piece or where it changes, and the input gives the one in force.
        - Barlines: repeatStart, how the bar ends (repeatEnd, double, final or normal), and the ending bracket it's under.
        - 8va and 15ma lines: ottava on every note under the line, with the pitch as written on the staff.
        - Articulation: a staccato dot, an accent or a tenuto line printed on the note.
        - Leave out fingering, dynamics, slurs, pedal marks, words and chord symbols.

        Doubts: the bars where the print is unclear (smudged, faint, crowded) and the reading may be wrong, with a few words on
        what. Empty when everything is clear.
        """;

    public override IReadOnlyList<Attachment> Attachments(SystemToRead input) => [Attachment.Png(png, "The system to read")];

    public override IReadOnlyList<string> Check(SystemToRead input, SystemReading answer) => ReadingChecks.Problems(answer, input.First);
}

/// <summary>
/// Compares a system of the print with the engraving of its reading, side by side, and reads again the bars that differ.
/// </summary>
/// <param name="print">The system, cut from its page.</param>
/// <param name="engraving">The reading of it, engraved by Verovio.</param>
public sealed class CompareSystem(byte[] print, byte[] engraving) : AssistTask<SystemToCompare, SystemComparison>
{
    public override string Name => "prima.compare-system";

    public override int Version => 1;

    public override string Instructions => """
        Two images: first a system of printed piano music, then the same system as it was read, engraved by Prima from the
        reading in the input. Compare them bar by bar and staff by staff: the noteheads (each one's line or space, and its ledger
        lines), accidentals, note values, dots, rests, ties, tuplets, grace notes, clefs, key and time signatures, barlines,
        repeats and endings.

        For every bar where the engraving differs from the print in what's played, give the whole bar again, read from the
        print, in the reading's shape and with the same index. Leave out the bars that match.

        - Where they differ, look at the print again closely: count the ledger lines, and check whether each notehead sits on a
          line or in a space. Report only what's printed, never what the harmony or the voice leading suggests.
        - Some differences don't matter: spacing, line breaks, beaming, stem directions, and the slurs, dynamics, fingering and
          pedal marks the reading leaves out. A passage under an 8va line is engraved at the pitch it sounds, an octave higher,
          without the line. An accidental the print repeats as a reminder may be missing from the engraving. Ignore all of these.

        Doubts: the bars where the print itself is unclear and the reading may still be wrong, with a few words on what. Empty
        when everything is clear.
        """;

    public override IReadOnlyList<Attachment> Attachments(SystemToCompare input) =>
        [Attachment.Png(print, "1. The printed system"), Attachment.Png(engraving, "2. The reading of it, engraved")];

    public override IReadOnlyList<string> Check(SystemToCompare input, SystemComparison answer)
    {
        var indexes = input.Reading.Bars.Select(b => b.Index).ToHashSet();
        var strangers = answer.Corrected.Where(c => !indexes.Contains(c.Index)).Select(c => $"The reading has no bar {c.Index}: give a corrected bar the index it had.");
        var corrected = input.Reading with { Bars = [.. input.Reading.Bars.Select(b => answer.Corrected.FirstOrDefault(c => c.Index == b.Index) ?? b)] };
        return [.. strangers, .. ReadingChecks.Problems(corrected, input.First)];
    }
}
