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

public sealed record Rest(string Id, int Start, int Length) : Event(Start, Length)
{
    /// <summary>Takes the time without printing a rest: for a hand that's silent in a pickup, or a drill item's empty staff.</summary>
    public bool Hidden { get; init; }
}

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
    /// <summary>Words above a note, like a drill's starting note's name.</summary>
    Text,
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

    /// <summary>Whether the time signature is printed. Without one (drill items, the notes on You) there are no barlines or bar numbers either.</summary>
    public bool ShowMeter { get; init; } = true;

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

    /// <summary>Each bar and when it starts, in ticks from the downbeat of the first full bar, so a pickup starts before 0.</summary>
    public IEnumerable<(Bar Bar, long Start)> BarStarts()
    {
        var start = 0L;
        var first = true;
        foreach (var (bar, _, meter) in WithSignatures())
        {
            var length = bar.Length ?? meter.BarTicks;
            if (first && length < meter.BarTicks) start = -length;
            first = false;
            yield return (bar, start);
            start += length;
        }
    }

    /// <summary>
    /// The notes to play, in order: one per tone that starts sounding (a tie's later notes and grace notes aren't played again),
    /// timed in ticks from the downbeat of the first full bar, so a pickup's notes come before 0.
    /// </summary>
    public IReadOnlyList<ExpectedNote> Expected()
    {
        var notes = new List<ExpectedNote>();
        foreach (var (bar, barStart) in BarStarts())
        {
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

    /// <summary>
    /// Bars <paramref name="first"/> to <paramref name="last"/> as music of their own, keeping their numbers as a printed excerpt
    /// does: the key, metre and clefs in force at the first bar, ties into and out of it dropped so their notes are played, and the
    /// marks on it, with spans that run in or out cut at its edges.
    /// </summary>
    public Score Excerpt(int first, int last)
    {
        var taken = WithSignatures().Where(s => s.Bar.Number >= first && s.Bar.Number <= last).ToList();
        if (taken.Count == 0) throw new ArgumentOutOfRangeException(nameof(first), $"No bars from {first} to {last}.");

        // Each staff's clef at the start is the last change before it; the grand staff starts treble over bass.
        var clefs = new Dictionary<int, Clef>();
        foreach (var (staff, clef) in Bars.Where(b => b.Number < first).SelectMany(b => b.Voices.SelectMany(v => v.Events.OfType<ClefChange>().Select(c => (v.Staff, c.Clef))))) clefs[staff] = clef;

        var bars = taken.Select((s, i) => new Bar(s.Bar.Number, [.. s.Bar.Voices.Select(v =>
        {
            List<Event> events = [.. v.Events.Select(e => e is Notes n ? n with { Tones = [.. n.Tones.Select(t => t with { TiedFromPrevious = t.TiedFromPrevious && i > 0, TieToNext = t.TieToNext && i < taken.Count - 1 })] } : e)];
            var inForce = clefs.TryGetValue(v.Staff, out var clef) ? clef : v.Staff == 1 ? Clef.Treble : Clef.Bass;
            if (i == 0 && v.Layer == 1 && !RhythmOnly && inForce != (v.Staff == 1 ? Clef.Treble : Clef.Bass) && events.FirstOrDefault() is not ClefChange { Start: 0 }) events.Insert(0, new ClefChange(0, inForce));
            return v with { Events = events };
        })]) { Length = s.Bar.Length, NewKey = i == 0 ? null : s.Bar.NewKey, NewMeter = i == 0 ? null : s.Bar.NewMeter }).ToList();

        // Where every note (and each of a chord's tones) is: its bar, its start, its staff, and its event's id.
        var places = new Dictionary<string, (int Bar, int Start, int Staff, string Id)>();
        foreach (var bar in Bars)
        {
            foreach (var voice in bar.Voices)
            {
                foreach (var n in voice.Events.OfType<Notes>())
                {
                    places[n.Id] = (bar.Number, n.Start, voice.Staff, n.Id);
                    foreach (var t in n.Tones) places[t.Id] = (bar.Number, n.Start, voice.Staff, n.Id);
                }
            }
        }

        string? Edge(int barNumber, int staff, bool start)
        {
            var bar = Bars.First(b => b.Number == barNumber);
            var notes = bar.Voices.OrderBy(v => v.Staff != staff).SelectMany(v => v.Events.OfType<Notes>().Where(n => !n.Grace).Select(n => (v.Staff, n))).ToList();
            var onStaff = notes.Where(p => p.Staff == staff).Select(p => p.n).ToList();
            var pool = onStaff.Count > 0 ? onStaff : notes.Select(p => p.n).ToList();
            // The id the sheet carries: a single note's tone, or a chord's.
            var edge = pool.Count == 0 ? null : start ? pool.MinBy(n => n.Start) : pool.MaxBy(n => n.Start);
            return edge is null ? null : edge.Tones.Count == 1 ? edge.Tones[0].Id : edge.Id;
        }

        var (firstBar, lastBar) = (taken[0].Bar.Number, taken[^1].Bar.Number);
        var marks = new List<Mark>();
        foreach (var mark in Marks)
        {
            if (!places.TryGetValue(mark.StartId, out var from)) continue;
            if (mark.EndId is null)
            {
                if (from.Bar >= firstBar && from.Bar <= lastBar) marks.Add(mark);
                continue;
            }

            if (!places.TryGetValue(mark.EndId, out var to) || to.Bar < firstBar || from.Bar > lastBar) continue;

            var startId = from.Bar >= firstBar ? mark.StartId : Edge(firstBar, from.Staff, start: true);
            var endId = to.Bar <= lastBar ? mark.EndId : Edge(lastBar, to.Staff, start: false);
            if (startId is null || endId is null || startId == endId && mark.Kind != MarkKind.OctaveUp) continue;

            marks.Add(mark with { StartId = startId, EndId = endId });
        }

        return this with { Key = taken[0].Key, Meter = taken[0].Meter, Bars = bars, Marks = marks };
    }
}

/// <summary>
/// A note to be played: the sheet's id for it, the key, its bar and staff, when it starts in ticks from the first full bar's
/// downbeat, how long it sounds, and the chord it's part of.
/// </summary>
public sealed record ExpectedNote(string Id, int Midi, int Bar, int Staff, long Tick, int Length, string? ChordId);
