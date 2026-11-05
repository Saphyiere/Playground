namespace Prima.Core.Music;

public enum Clef
{
    Treble,
    Bass,
}

public enum Articulation
{
    None,
    Staccato,
    Accent,
    Tenuto,
}

/// <summary>One pitch of a note or chord. Its id is the one the sheet's SVG carries, so marks find it.</summary>
public sealed record Tone(string Id, Spelled Pitch)
{
    /// <summary>Tied on to the same pitch in this voice's next note.</summary>
    public bool TieToNext { get; init; }

    /// <summary>The end of a tie from the note before: held, not played again.</summary>
    public bool TiedFromPrevious { get; init; }

    /// <summary>The finger, 1 (thumb) to 5, printed above or below; for technique only.</summary>
    public int? Finger { get; init; }
}

/// <summary>A run of notes under one tuplet number: <see cref="Actual"/> notes in the time of <see cref="Normal"/>, like 3 in 2.</summary>
public sealed record Tuplet(int Actual, int Normal);

/// <summary>Something in a voice, starting <see cref="Start"/> ticks into its bar and lasting <see cref="Length"/> (as it sounds).</summary>
public abstract record Event(int Start, int Length)
{
    /// <summary>Set on each event of a tuplet group; consecutive events with the same instance form the group.</summary>
    public Tuplet? Tuplet { get; init; }
}

/// <summary>A note, or a chord when it has more than one tone.</summary>
public sealed record Notes(string Id, int Start, int Length, IReadOnlyList<Tone> Tones) : Event(Start, Length)
{
    /// <summary>A grace note: printed small, before the beat, and not scored.</summary>
    public bool Grace { get; init; }

    public Articulation Articulation { get; init; }
}

public sealed record Rest(string Id, int Start, int Length) : Event(Start, Length);

/// <summary>A clef change at this point in the voice.</summary>
public sealed record ClefChange(int Start, Clef Clef) : Event(Start, 0);

/// <summary>One line of events on a staff (1 treble, 2 bass); a staff can hold two, as layers.</summary>
public sealed record Voice(int Staff, IReadOnlyList<Event> Events, int Layer = 1);

/// <summary>A bar. Its length is the metre's unless it's a pickup. A new key or metre starts at it.</summary>
public sealed record Bar(int Number, IReadOnlyList<Voice> Voices)
{
    public int? Length { get; init; }

    public Key? NewKey { get; init; }

    public Meter? NewMeter { get; init; }

    public string Id => $"m{Number}";
}

public enum MarkKind
{
    Dynamic,
    Slur,
    Crescendo,
    Diminuendo,
    PedalDown,
    PedalUp,
    OctaveUp,
    Fermata,
    Trill,
    Mordent,
}

/// <summary>Printed marks that aren't scored (dynamics, slurs, hairpins, pedal, 8va, ornaments), tied to notes by id.</summary>
public sealed record Mark(MarkKind Kind, string StartId, string? EndId = null, string? Text = null);

/// <summary>
/// A piece of music: a key and metre to start, bars of voices, and printed marks. A piano score has two staves, treble and bass;
/// a rhythm has one line. Bar numbers start at 1, or 0 for a pickup.
/// </summary>
public sealed record Score(Key Key, Meter Meter, IReadOnlyList<Bar> Bars)
{
    public string? Title { get; init; }

    /// <summary>A single-line rhythm staff instead of the grand staff.</summary>
    public bool RhythmOnly { get; init; }

    public IReadOnlyList<Mark> Marks { get; init; } = [];

    /// <summary>The metre in force at each bar, in order.</summary>
    public IEnumerable<(Bar Bar, Key Key, Meter Meter)> WithSignatures()
    {
        var (key, meter) = (Key, Meter);
        foreach (var bar in Bars)
        {
            (key, meter) = (bar.NewKey ?? key, bar.NewMeter ?? meter);
            yield return (bar, key, meter);
        }
    }

    /// <summary>
    /// The notes to play, in order: one per tone that starts sounding (a tie's later notes and grace notes aren't played again),
    /// timed in ticks from the downbeat of the first full bar, so a pickup's notes come before 0.
    /// </summary>
    public IReadOnlyList<ExpectedNote> Expected()
    {
        var notes = new List<ExpectedNote>();
        var barStart = 0L;
        var first = true;
        foreach (var (bar, _, meter) in WithSignatures())
        {
            var length = bar.Length ?? meter.BarTicks;
            if (first && length < meter.BarTicks) barStart = -length;
            first = false;
            foreach (var voice in bar.Voices)
            {
                foreach (var notesEvent in voice.Events.OfType<Notes>().Where(n => !n.Grace))
                {
                    foreach (var tone in notesEvent.Tones.Where(t => !t.TiedFromPrevious))
                    {
                        // A tied note sounds for all its tied notes together.
                        var sounding = notesEvent.Length + (tone.TieToNext ? TiedLength(bar, voice, notesEvent, tone) : 0);
                        notes.Add(new ExpectedNote(tone.Id, tone.Pitch.Midi, bar.Number, voice.Staff, barStart + notesEvent.Start, sounding, notesEvent.Tones.Count > 1 ? notesEvent.Id : null));
                    }
                }
            }
            barStart += length;
        }
        return notes.OrderBy(n => n.Tick).ThenBy(n => n.Midi).ToList();

        int TiedLength(Bar bar, Voice voice, Notes from, Tone tone)
        {
            // Follows the tie through this voice's later notes, into later bars on the same staff and layer.
            var total = 0;
            var pitch = tone.Pitch.Midi;
            var following = Bars.SkipWhile(b => b != bar).SelectMany(b => b.Voices.Where(v => v.Staff == voice.Staff && v.Layer == voice.Layer).SelectMany(v => v.Events.OfType<Notes>().Where(n => b != bar || n.Start > from.Start)));
            foreach (var next in following)
            {
                var held = next.Tones.FirstOrDefault(t => t.TiedFromPrevious && t.Pitch.Midi == pitch);
                if (held is null) break;

                total += next.Length;
                if (!held.TieToNext) break;
            }
            return total;
        }
    }
}

/// <summary>
/// A note to be played: the sheet's id for it, the key, its bar and staff, when it starts in ticks from the first full bar's
/// downbeat, how long it sounds, and the chord it's part of.
/// </summary>
public sealed record ExpectedNote(string Id, int Midi, int Bar, int Staff, long Tick, int Length, string? ChordId);
