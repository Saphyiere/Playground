using Prima.Core.Music;

namespace Prima.Core.Generation;

/// <summary>A drill item on the sheet: its score, the notes to play in order (a chord is one step), and its prompt.</summary>
public sealed record DrillItem(Score Score, IReadOnlyList<IReadOnlyList<int>> Answer, string Prompt)
{
    /// <summary>For the key-signature drill: the notes the key sharpens or flattens, kept per key as "forgot the key".</summary>
    public IReadOnlySet<string> Affected { get; init; } = new HashSet<string>();
}

/// <summary>
/// Material for the drills (plan, Drill material): single notes and ledger lines from the note schedule, interval figures,
/// chords, key signatures with a phrase, keyboard geography, and phrases to listen to and play back. All on the grand staff,
/// without a time signature, except the phrases, which are music.
/// </summary>
public static class Drills
{
    private static Score Item(IReadOnlyList<Event> treble, IReadOnlyList<Event> bass, Key? key = null, IReadOnlyList<Mark>? marks = null)
    {
        var length = Math.Max(treble.Sum(e => e.Length), bass.Sum(e => e.Length));
        static IReadOnlyList<Event> Pad(IReadOnlyList<Event> events, int length, string id) => events.Count == 0 ? [new Rest(id, 0, length) { Hidden = true }] : events;
        var meter = new Meter(length / Ticks.Quarter, 4);
        return new Score(key ?? new Key(0), meter, [new Bar(1, [new Voice(1, Pad(treble, length, "t-space")), new Voice(2, Pad(bass, length, "b-space"))])]) { ShowMeter = false, Marks = marks ?? [] };
    }

    private static Notes Note(string id, int start, Spelled pitch, int length = Ticks.Whole) => new(id, start, length, [new Tone(id, pitch)]);

    /// <summary>One note to read and play: on the treble staff (clef 1) or the bass (2).</summary>
    public static DrillItem Note(int staff, Spelled pitch)
    {
        var note = Note("q1", 0, pitch);
        return new DrillItem(staff == 1 ? Item([note], []) : Item([], [note]), [[pitch.Midi]], "Play this note");
    }

    /// <summary>
    /// An interval figure of 2 to 5 notes from <paramref name="first"/>, moving by the given steps (staff positions: 1 a second,
    /// 2 a third), in quarters. The first note's name is printed above it: the figure is read by its distances.
    /// </summary>
    public static DrillItem Figure(int staff, Spelled first, IReadOnlyList<int> steps, Key key)
    {
        var pitches = new List<Spelled> { first };
        foreach (var step in steps)
        {
            var next = Spelled.FromDiatonic(pitches[^1].Diatonic + step);
            pitches.Add(next with { Alter = key.AlterOf(next.Step) });
        }
        var notes = pitches.Select((p, i) => (Event)Note($"q{i + 1}", i * Ticks.Quarter, p, Ticks.Quarter)).ToList();
        var marks = new[] { new Mark(MarkKind.Text, "q1", Text: first.ToString()) };
        return new DrillItem(staff == 1 ? Item(notes, [], key, marks) : Item([], notes, key, marks), [.. pitches.Select(p => (IReadOnlyList<int>)[p.Midi])], "Play the figure from the named note");
    }

    /// <summary>A chord to play at once: its notes split between the staves at middle C.</summary>
    public static DrillItem Chord(IReadOnlyList<Spelled> pitches, Key key)
    {
        var treble = pitches.Where(p => p.Midi >= 60).ToList();
        var bass = pitches.Where(p => p.Midi < 60).ToList();
        Notes Stack(string id, List<Spelled> stack) => new(id, 0, Ticks.Whole, stack.Select((p, i) => new Tone($"{id}-{i}", p)).ToList());
        return new DrillItem(Item(treble.Count > 0 ? [Stack("qt", treble)] : [], bass.Count > 0 ? [Stack("qb", bass)] : [], key), [[.. pitches.Select(p => p.Midi).Order()]], "Play the chord");
    }

    /// <summary>The chords for a step of the chord ladder (the boards'): 1 two-note chords, 2 triads, 3 inversions, 4 four parts.</summary>
    public static DrillItem RandomChord(int step, Key key, Rng rng)
    {
        var root = rng.Int(7);
        var octave = rng.Chance(0.5) ? 4 : 3;
        List<Spelled> pitches = step switch
        {
            <= 1 => [key.Degree(root, octave), key.Degree(root + rng.Pick(new[] { 2, 4, 5, 7 }), octave)],
            2 => [key.Degree(root, octave), key.Degree(root + 2, octave), key.Degree(root + 4, octave)],
            3 => rng.Pick(new[] { new[] { 2, 4, 7 }, new[] { 4, 7, 9 }, new[] { 0, 2, 4 } }).Select(d => key.Degree(root + d, octave)).ToList(),
            _ => [key.Degree(root, 2), key.Degree(root + 4, 3), key.Degree(root + 9, 3), key.Degree(root + 14, 3)],
        };
        return Chord(pitches, key);
    }

    /// <summary>A key signature on its own, to name the tonic in a mode by playing it.</summary>
    public static DrillItem KeySignature(Key key)
    {
        var tonic = key.Degree(0, 4);
        return new DrillItem(Item([new Rest("t-space", 0, Ticks.Whole) { Hidden = true }], [], key), [[tonic.Midi % 12 + 60]], $"Play the tonic of this key: {(key.Minor ? "minor" : "major")}");
    }

    /// <summary>A short phrase in a key, at a level, whose notes include ones the signature sharpens or flattens.</summary>
    public static DrillItem KeyPhrase(Key key, int level, ulong seed)
    {
        var spec = Ladder.Level(Math.Clamp(level, 3, 8)) with { Keys = [key], Bars = (2, 2), Hands = Hands.One };
        for (var attempt = 0UL; ; attempt++)
        {
            var passage = PassageGenerator.Generate(spec, seed + attempt * 101);
            var tones = passage.Score.Bars.SelectMany(b => b.Voices.SelectMany(v => v.Events.OfType<Notes>().SelectMany(n => n.Tones))).ToList();
            var affected = tones.Where(t => key.AlterOf(t.Pitch.Step) != 0 && t.Pitch.Alter == key.AlterOf(t.Pitch.Step)).Select(t => t.Id).ToHashSet();
            if (affected.Count == 0 && attempt < 20) continue;

            var answer = passage.Score.Expected().Select(n => (IReadOnlyList<int>)[n.Midi]).ToList();
            return new DrillItem(passage.Score, answer, "Play the phrase, with the key's sharps or flats") { Affected = affected };
        }
    }

    /// <summary>A phrase to hear and play back (RCM's ladder): the first five notes of a scale, then six to eight, then more keys.</summary>
    public static Passage Playback(int step, ulong seed)
    {
        var spec = Ladder.Level(step <= 1 ? 1 : step == 2 ? 2 : 4) with { Bars = (2, 2), Hands = Hands.One, FixedPosition = true };
        if (step <= 2) spec = spec with { Keys = [new Key(0)] };
        return PassageGenerator.Generate(spec, seed);
    }

    /// <summary>
    /// Keyboard geography: a note, or two in a row for a leap, from a stated hand position: within the position (step 1), just
    /// out of it (2), an octave away (3), or anywhere in two octaves (4).
    /// </summary>
    public static DrillItem Geography(int step, Rng rng)
    {
        var home = new Spelled(Step.C, 0, 4);
        var away = step switch { <= 1 => rng.Int(5), 2 => rng.Pick(new[] { -2, -1, 5, 6 }), 3 => rng.Pick(new[] { -7, 7 }), _ => rng.Int(15) - 7 };
        var target = Spelled.FromDiatonic(home.Diatonic + away);
        var staff = target.Midi >= 60 ? 1 : 2;
        var item = Note(staff, target);
        return item with { Prompt = step <= 1 ? "Right hand on C to G. Play it without looking down" : "From middle C: play it without looking down" };
    }
}
