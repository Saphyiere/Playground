using System.ComponentModel;
using Prima.Core.Music;

namespace Prima.Core.Transcription;

// What Claude reads from a system of printed piano music: the bars, left to right, each with both staves and their voices, and
// each voice's notes, rests and clef changes as printed. Pitches are written as the page shows them, the line or space with the
// accidental printed before the note; Prima applies the key signature, the bar's earlier accidentals, ties and 8va lines itself
// (ReadingScore). Every field is required, and none can be null: the API holds Claude's answer to this shape.

public enum ReadKind
{
    /// <summary>A note, or a chord when it has several pitches on one stem.</summary>
    Note,

    /// <summary>A printed rest.</summary>
    Rest,

    /// <summary>Silence in a voice where no rest is printed.</summary>
    Space,

    /// <summary>A rest that fills the whole bar.</summary>
    BarRest,

    /// <summary>A clef change partway through the bar.</summary>
    Clef,
}

/// <summary>A note value as printed, before dots and tuplets.</summary>
public enum NoteValue
{
    Whole,
    Half,
    Quarter,
    Eighth,
    Sixteenth,
    ThirtySecond,
    SixtyFourth,
}

public enum Accidental
{
    None,
    Sharp,
    Flat,
    Natural,
    DoubleSharp,
    DoubleFlat,
}

public enum ReadClef
{
    None,
    Treble,
    Bass,
}

/// <summary>How a bar ends.</summary>
public enum BarEnd
{
    Normal,
    Double,
    Final,
    RepeatEnd,
}

public sealed record ReadPitch(
    [property: Description("The letter of the line or space the notehead sits on, in the clef in force.")]
    Step Step,
    [property: Description("The accidental printed just before this note; none when none is printed, even where the key signature or an earlier accidental in the bar applies.")]
    Accidental Accidental,
    [property: Description("The octave of that line or space as written, before any 8va line: C4 is middle C, the B below it is B3.")]
    int Octave,
    [property: Description("True when a tie runs from this note to the same pitch in this voice's next note.")]
    bool Tie);

public sealed record ReadEvent(
    [property: Description("note (several pitches make a chord), rest (a printed rest), space (silence where no rest is printed), barRest (a rest filling the bar), or clef (a clef change).")]
    ReadKind Kind,
    [property: Description("A note's pitches, lowest first, several for a chord; empty for a rest, a space, a bar rest or a clef.")]
    IReadOnlyList<ReadPitch> Pitches,
    [property: Description("The value as printed, before dots and tuplets. A bar rest or a clef takes quarter, which isn't used.")]
    NoteValue Value,
    [property: Description("The dots after it: 0, 1 or 2.")]
    int Dots,
    [property: Description("The tuplet it's in, by the number printed over the group (3 for triplets, 6 for sextuplets, 2 for duplets); 0 when it isn't in one.")]
    int Tuplet,
    [property: Description("True on the first note or rest of each tuplet group.")]
    bool TupletStart,
    [property: Description("True for a grace note, printed small, with or without a slash.")]
    bool Grace,
    [property: Description("The articulation printed on the note: staccato (a dot), accent (>), tenuto (a line), or none.")]
    Articulation Articulation,
    [property: Description("The octave line it's under: 1 for 8va above, 2 for 15ma, -1 for 8vb below, 0 for none.")]
    int Ottava,
    [property: Description("For a clef change, the new clef; none otherwise.")]
    ReadClef Clef);

public sealed record ReadVoice(
    [property: Description("1 for the stems-up voice, or the staff's only voice; 2 for the stems-down voice where a staff has two.")]
    int Voice,
    [property: Description("Its notes, rests and clef changes in order. A voice fills its bar: where it's silent and no rest is printed, a space.")]
    IReadOnlyList<ReadEvent> Events);

public sealed record ReadStaff(
    [property: Description("1 for the upper staff, 2 for the lower.")]
    int Staff,
    [property: Description("The clef in force at the start of the bar.")]
    ReadClef Clef,
    IReadOnlyList<ReadVoice> Voices);

public sealed record ReadBar(
    [property: Description("Where the bar is in the system, from 1 at the left.")]
    int Index,
    [property: Description("The key signature in force, in fifths: sharps positive, flats negative, 0 for none.")]
    int Fifths,
    [property: Description("The time signature in force: its top number (4 for common time, 2 for cut time).")]
    int MeterCount,
    [property: Description("The time signature in force: its bottom number (4 for common time, 2 for cut time).")]
    int MeterUnit,
    [property: Description("True when a start-repeat sign begins the bar.")]
    bool RepeatStart,
    [property: Description("How the bar ends: normal, double (two thin lines), final (thin and thick), or repeatEnd (dots before the line).")]
    BarEnd End,
    [property: Description("The ending bracket the bar is under: 1 for a first ending, 2 for a second, 0 for none.")]
    int Ending,
    [property: Description("Both staves, upper then lower.")]
    IReadOnlyList<ReadStaff> Staves);

public sealed record ReadDoubt(
    [property: Description("The bar's index in the system.")]
    int Bar,
    [property: Description("1 for the upper staff, 2 for the lower.")]
    int Staff,
    [property: Description("What's unclear, in a few words, like \"the top note of the chord on beat 3\".")]
    string Note);

/// <summary>One system of a printed page, as Claude read it.</summary>
public sealed record SystemReading(
    [property: Description("The system's bars, left to right.")]
    IReadOnlyList<ReadBar> Bars,
    [property: Description("Bars where the print is unclear and the reading may be wrong; empty when everything is clear.")]
    IReadOnlyList<ReadDoubt> Doubts);

/// <summary>What Prima sends with a system's image.</summary>
public sealed record SystemToRead(
    [property: Description("The page the system is on, from 1.")]
    int Page,
    [property: Description("Which system of the page it is, from 1 at the top.")]
    int Index,
    [property: Description("True for the piece's first system: its time signature is printed on it, and its first bar may be a pickup.")]
    bool First,
    [property: Description("The time signature in force where this system starts, from the piece's first system: its top number; 0 for the first system itself.")]
    int MeterCount,
    [property: Description("The time signature in force where this system starts: its bottom number; 0 for the first system itself.")]
    int MeterUnit);

/// <summary>What Prima sends with a system's print and the engraving of its reading.</summary>
public sealed record SystemToCompare(
    [property: Description("The page the system is on, from 1.")]
    int Page,
    [property: Description("Which system of the page it is, from 1 at the top.")]
    int Index,
    [property: Description("True for the piece's first system, whose first bar may be a pickup.")]
    bool First,
    [property: Description("The reading that was engraved.")]
    SystemReading Reading);

/// <summary>What the comparison found: bars to replace, read again from the print, and bars that are still unclear.</summary>
public sealed record SystemComparison(
    [property: Description("Each bar where the engraving differs from the print, given whole as printed, with its index; empty when they match.")]
    IReadOnlyList<ReadBar> Corrected,
    [property: Description("Bars where the print is unclear and the reading may still be wrong; empty when everything is clear.")]
    IReadOnlyList<ReadDoubt> Doubts);
