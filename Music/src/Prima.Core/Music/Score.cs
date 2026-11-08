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

/// <summary>
/// One pitch of a note or chord, as it sounds: the key to play. Under an octave line it's printed an octave or two from there, at
/// <see cref="Written"/>. Its id is the one the sheet's SVG carries, so marks find it.
/// </summary>
public sealed record Tone(string Id, Spelled Pitch)
{
    /// <summary>Tied on to the same pitch in this voice's next note.</summary>
    public bool TieToNext { get; init; }

    /// <summary>The end of a tie from the note before: held, not played again.</summary>
    public bool TiedFromPrevious { get; init; }

    /// <summary>The finger, 1 (thumb) to 5, printed above or below; for technique only.</summary>
    public int? Finger { get; init; }

    /// <summary>The octave line it's under, as octaves it sounds above its printed note: 1 an 8va, 2 a 15ma, -1 an 8vb, -2 a 15mb.</summary>
    public int Ottava { get; init; }

    /// <summary>The note as printed, so its place on the staff: the pitch, moved back by the octave line it's under.</summary>
    public Spelled Written => Pitch with { Octave = Pitch.Octave - Ottava };
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

    /// <summary>A start-repeat sign before the bar.</summary>
    public bool RepeatStart { get; init; }

    /// <summary>An end-repeat sign after the bar.</summary>
    public bool RepeatEnd { get; init; }

    /// <summary>In a first or second ending: the times through the repeat that play the bar (none: every time).</summary>
    public IReadOnlyList<int> Endings { get; init; } = [];

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
    /// <summary>An 8va line. The notes under it say so themselves (<see cref="Tone.Ottava"/> 1), so they're printed an octave below the pitch they sound.</summary>
    OctaveUp,
    Fermata,
    Trill,
    Mordent,
    /// <summary>Words above a note, like a drill's starting note's name.</summary>
    Text,
    /// <summary>A turn, read from a piece's file; Prima writes none.</summary>
    Turn,
}

/// <summary>
/// A printed mark (dynamics, slurs, hairpins, pedal, 8va, ornaments), tied to notes by id: a note's own, or a chord's. A mark a
/// file places by the beat rather than on a note is tied to the note at or before that beat, <see cref="StartOffset"/> ticks
/// after it (and its end <see cref="EndOffset"/> after its end note), like a pedal lifted just before the next beat.
/// </summary>
public sealed record Mark(MarkKind Kind, string StartId, string? EndId = null, string? Text = null)
{
    public long StartOffset { get; init; }

    public long EndOffset { get; init; }
}

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

    /// <summary>
    /// A piece's own marks, as read from its file (slurs, dynamics, hairpins, pedal, ornaments), for judging how it's played.
    /// They're kept apart from <see cref="Marks"/>, which the Library's grading and excerpt cuts read and which a file's marks
    /// have never been part of, so a piece's level and excerpts don't change with them. Verovio engraves them from the file.
    /// </summary>
    public IReadOnlyList<Mark> FileMarks { get; init; } = [];

    /// <summary>All the marks: the score's own and its file's.</summary>
    public IEnumerable<Mark> AllMarks => Marks.Concat(FileMarks);

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
        // Where each note and chord is, for the slurs: the notes under one are those of its voice from its first note to its last.
        var places = new Dictionary<string, (int Staff, int Layer, long Tick)>();
        foreach (var (bar, barStart) in BarStarts())
        {
            foreach (var voice in bar.Voices)
            {
                foreach (var n in voice.Events.OfType<Notes>().Where(n => !n.Grace))
                {
                    places[n.Id] = (voice.Staff, voice.Layer, barStart + n.Start);
                    foreach (var t in n.Tones) places[t.Id] = (voice.Staff, voice.Layer, barStart + n.Start);
                }
            }
        }
        var slurs = AllMarks.Where(m => m.Kind == MarkKind.Slur && m.EndId is not null && places.ContainsKey(m.StartId) && places.ContainsKey(m.EndId))
            .Select(m => (m.StartId, Voice: (places[m.StartId].Staff, places[m.StartId].Layer), From: places[m.StartId].Tick + m.StartOffset, To: places[m.EndId!].Tick + m.EndOffset)).ToList();
        // The last note under a slur, a quarter or shorter, is lifted to end it: a note's id or its chord's.
        var slurEnds = AllMarks.Where(m => m.Kind == MarkKind.Slur && m.EndId is not null).Select(m => m.EndId!).ToHashSet();
        foreach (var (bar, barStart) in BarStarts())
        {
            foreach (var voice in bar.Voices)
            {
                foreach (var notesEvent in voice.Events.OfType<Notes>().Where(n => !n.Grace))
                {
                    var tick = barStart + notesEvent.Start;
                    var slur = slurs.FirstOrDefault(s => s.Voice == (voice.Staff, voice.Layer) && s.From <= tick && tick <= s.To).StartId;
                    foreach (var tone in notesEvent.Tones.Where(t => !t.TiedFromPrevious))
                    {
                        // A tied note sounds for all its tied notes together.
                        var sounding = notesEvent.Length + (tone.TieToNext ? TiedLength(bar, voice, notesEvent, tone) : 0);
                        notes.Add(new ExpectedNote(tone.Id, tone.Pitch.Midi, bar.Number, voice.Staff, tick, sounding, notesEvent.Tones.Count > 1 ? notesEvent.Id : null)
                        {
                            Lifted = notesEvent.Articulation == Articulation.Staccato || notesEvent.Length <= Ticks.Quarter && (slurEnds.Contains(tone.Id) || slurEnds.Contains(notesEvent.Id)),
                            Articulation = notesEvent.Articulation,
                            Slur = slur,
                        });
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
    /// The bars in the order they're played from bar index <paramref name="from"/> on (indexes into <see cref="Bars"/>): each
    /// repeat twice, back to its start-repeat sign or, without one, the bar after the last repeat (or the beginning), the second
    /// time by its second ending. From the middle of a repeat it goes back to that repeat's start. D.C. and D.S. aren't followed.
    /// </summary>
    public IReadOnlyList<int> PlayOrder(int from = 0)
    {
        var order = new List<int>();
        var taken = new HashSet<int>();
        var back = 0;
        for (var j = 0; j < from; j++) back = Bars[j].RepeatEnd ? j + 1 : Bars[j].RepeatStart ? j : back;
        if (from < Bars.Count && Bars[from].RepeatStart) back = from;
        var pass = from < Bars.Count && Bars[from].Endings.Count > 0 ? Bars[from].Endings.Min() : 1;
        for (var i = from; i < Bars.Count; i++)
        {
            var bar = Bars[i];
            if (bar.Endings.Count > 0 && !bar.Endings.Contains(pass)) continue;
            if (bar.RepeatStart && pass == 1) back = i;
            order.Add(i);
            if (bar.RepeatEnd && taken.Add(i))
            {
                (i, pass) = (back - 1, 2);
                continue;
            }
            // A repeat done: the next goes back no further than the bar after it.
            var lastEnding = bar.Endings.Count > 0 && (i + 1 == Bars.Count || Bars[i + 1].Endings.Count == 0);
            if (bar.RepeatEnd || (pass > 1 && lastEnding)) (back, pass) = (i + 1, 1);
        }
        return order;
    }

    /// <summary>
    /// The notes to play through bars in an order (<see cref="PlayOrder"/>'s, or a loop's): each bar's notes timed on from the bar
    /// before, so the music's own ticks hold until the first jump; a note played again has its pass after its id ("n12~2").
    /// </summary>
    public IReadOnlyList<ExpectedNote> Performed(IReadOnlyList<int> order)
    {
        var starts = BarStarts().ToList();
        var lengths = WithSignatures().Select(w => (long)(w.Bar.Length ?? w.Meter.BarTicks)).ToList();
        var byBar = Expected().ToLookup(n => n.Bar);
        var notes = new List<ExpectedNote>();
        var passes = new Dictionary<int, int>();
        var at = order.Count > 0 ? starts[order[0]].Start : 0;
        for (var k = 0; k < order.Count; k++)
        {
            var (bar, written) = starts[order[k]];
            var pass = passes[order[k]] = passes.GetValueOrDefault(order[k]) + 1;
            var shift = at - written;
            string Again(string id) => pass == 1 ? id : $"{id}~{pass}";
            foreach (var n in byBar[bar.Number]) notes.Add(n with { Id = Again(n.Id), Tick = n.Tick + shift, ChordId = n.ChordId is { } c ? Again(c) : null, Slur = n.Slur is { } sl ? Again(sl) : null });
            at += lengths[order[k]];
        }
        return notes;
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
        return this with { Key = taken[0].Key, Meter = taken[0].Meter, Bars = bars, Marks = Within(Marks), FileMarks = Within(FileMarks) };

        // The marks on these bars; a span that runs in or out is cut at their edges, from or to the edge's note.
        List<Mark> Within(IEnumerable<Mark> source)
        {
            var kept = new List<Mark>();
            foreach (var mark in source)
            {
                if (!places.TryGetValue(mark.StartId, out var from)) continue;
                if (mark.EndId is null)
                {
                    if (from.Bar >= firstBar && from.Bar <= lastBar) kept.Add(mark);
                    continue;
                }

                if (!places.TryGetValue(mark.EndId, out var to) || to.Bar < firstBar || from.Bar > lastBar) continue;

                var startId = from.Bar >= firstBar ? mark.StartId : Edge(firstBar, from.Staff, start: true);
                var endId = to.Bar <= lastBar ? mark.EndId : Edge(lastBar, to.Staff, start: false);
                if (startId is null || endId is null || startId == endId && mark.Kind != MarkKind.OctaveUp) continue;

                kept.Add(mark with { StartId = startId, EndId = endId, StartOffset = startId == mark.StartId ? mark.StartOffset : 0, EndOffset = endId == mark.EndId ? mark.EndOffset : 0 });
            }
            return kept;
        }
    }
}

/// <summary>
/// A note to be played: the sheet's id for it, the key, its bar and staff, when it starts in ticks from the first full bar's
/// downbeat, how long it sounds, and the chord it's part of.
/// </summary>
public sealed record ExpectedNote(string Id, int Midi, int Bar, int Staff, long Tick, int Length, string? ChordId)
{
    /// <summary>
    /// Let go early by design, so how long it's held isn't judged: a staccato note, or the last under a slur when it's a quarter or
    /// shorter, which is lifted to end the slur. A longer note ending a phrase is held to its end.
    /// </summary>
    public bool Lifted { get; init; }

    /// <summary>What its articulation asks: short (staccato), a stress (an accent), or held its whole length (tenuto).</summary>
    public Articulation Articulation { get; init; }

    /// <summary>The slur it's under, by the id of the slur's first note (a pass's own, played again), so its notes are joined.</summary>
    public string? Slur { get; init; }

    /// <summary>Its id as written, without the pass a repeat adds to it ("n12" for "n12~2").</summary>
    public string WrittenId => Id.Split('~')[0];

    /// <summary>Which time through a repeat it's played: "" the first, "~2" the second.</summary>
    public string Pass => Id.Contains('~') ? Id[Id.IndexOf('~')..] : "";
}
