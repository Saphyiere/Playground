using Prima.Core.Music;

namespace Prima.Core.Generation;

/// <summary>How the hands share a passage, level by level.</summary>
public enum Hands
{
    /// <summary>One hand in a fixed five-finger position.</summary>
    One,
    /// <summary>The melody passed between the hands, a phrase each.</summary>
    Split,
    /// <summary>Each hand its own phrase, one after the other.</summary>
    Separate,
    /// <summary>Both at once: a melody over an accompaniment.</summary>
    Together,
}

/// <summary>The left hand's part when the hands play together, from long notes to four-part chords.</summary>
public enum Texture
{
    LongNotes,
    TwoNoteChords,
    BrokenChords,
    Patterns,
    FourPart,
    ThreeNoteChords,
}

/// <summary>A rhythm figure: its notes' lengths in ticks (negative for a rest), how many beats it takes, and its tuplet if any.</summary>
public sealed record Cell(string Name, int[] Lengths, int Level, bool Compound = false, Tuplet? Tuplet = null)
{
    public int Ticks => Lengths.Sum(Math.Abs);
}

/// <summary>
/// What a level asks for, in four kinds of difficulty that come in one at a time (plan: "One thing at a time"): rhythm (the
/// figures), hands and texture, keys and metres, and range and leaps. Bars and tempo go with the level itself.
/// </summary>
public sealed record LevelSpec(
    int Level,
    (int Min, int Max) Bars,
    (int Min, int Max) Tempo,
    IReadOnlyList<Cell> Cells,
    bool Ties,
    bool Pickups,
    bool Chromatic,
    Hands Hands,
    Texture Texture,
    bool ClefChanges,
    bool Pedal,
    bool Octava,
    bool Ornaments,
    IReadOnlyList<Key> Keys,
    IReadOnlyList<Meter> Meters,
    bool FixedPosition,
    int Span,
    int MaxLeap,
    int LedgerLines)
{
    /// <summary>The fastest tempo for the level's figures: no faster than about five notes a second below level 9 (ours).</summary>
    public int TopTempo
    {
        get
        {
            if (Level >= 9) return Tempo.Max;

            var shortest = Cells.SelectMany(c => c.Lengths.Select(Math.Abs)).DefaultIfEmpty(Music.Ticks.Quarter).Min();
            var perBeat = Music.Ticks.Quarter / (double)shortest;
            return Math.Max(Tempo.Min, Math.Min(Tempo.Max, (int)(5 * 60 / perBeat)));
        }
    }
}

/// <summary>
/// The twelve levels: research.md's merged ladder (section 6), with what no board gives filled in (plan, The generator). Each level
/// keeps what came before. <see cref="Effective"/> brings a new level's four kinds of difficulty in one step at a time.
/// </summary>
public static class Ladder
{
    public const int Top = 12;

    private static readonly Key[] Keys0 = [new(0)];
    private static readonly Key[] Keys3 = [new(0), new(1), new(-1), new(0, true), new(-1, true)];

    private static Key[] KeysUpTo(int accidentals) =>
    [
        .. Enumerable.Range(-accidentals, 2 * accidentals + 1).Select(f => new Key(f)),
        .. Enumerable.Range(-accidentals, 2 * accidentals + 1).Select(f => new Key(f, true)),
    ];

    // Simple-metre figures, beat = quarter.
    private static readonly Cell[] SimpleCells =
    [
        new("whole", [1920], 1), new("half", [960], 1), new("quarter", [480], 1),
        new("half rest", [-960], 2),
        new("two eighths", [240, 240], 3),
        new("dotted half", [1440], 4), new("quarter rest", [-480], 4), new("four eighths", [240, 240, 240, 240], 4),
        new("dotted quarter and eighth", [720, 240], 5),
        new("dotted eighth and sixteenth", [360, 120], 6), new("four sixteenths", [120, 120, 120, 120], 6), new("eighth and two sixteenths", [240, 120, 120], 6), new("eighth rest and eighth", [-240, 240], 6),
        new("syncopation", [240, 480, 240], 8),
        new("triplet", [160, 160, 160], 9, Tuplet: new Tuplet(3, 2)),
    ];

    // Compound-metre figures, beat = dotted quarter.
    private static readonly Cell[] CompoundCells =
    [
        new("dotted quarter", [720], 6, true), new("quarter and eighth", [480, 240], 6, true), new("three eighths", [240, 240, 240], 6, true), new("dotted half", [1440], 6, true),
        new("eighth and quarter", [240, 480], 8, true),
        new("two sixteenths and eighths", [120, 120, 240, 240], 9, true),
        new("duplet", [360, 360], 11, true, new Tuplet(2, 3)),
    ];

    private static LevelSpec Raw(int level) => level switch
    {
        1 => new(1, (2, 2), (60, 80), Up(1), false, false, false, Hands.One, Texture.LongNotes, false, false, false, false, Keys0, [new(4, 4)], true, 5, 1, 1),
        2 => new(2, (4, 4), (60, 84), Up(2), false, false, false, Hands.Split, Texture.LongNotes, false, false, false, false, Keys0, [new(4, 4), new(2, 4)], true, 5, 2, 1),
        3 => new(3, (4, 4), (60, 88), Up(3), false, false, false, Hands.Separate, Texture.LongNotes, false, false, false, false, Keys3, [new(4, 4), new(2, 4)], true, 5, 4, 1),
        4 => new(4, (4, 6), (60, 92), Up(4), true, false, false, Hands.Separate, Texture.LongNotes, false, false, false, false, KeysUpTo(1), [new(4, 4), new(2, 4), new(3, 4)], false, 5, 4, 1),
        5 => new(5, (4, 8), (60, 96), Up(5), true, false, false, Hands.Together, Texture.LongNotes, false, false, false, false, KeysUpTo(2), [new(4, 4), new(2, 4), new(3, 4)], false, 8, 5, 1),
        6 => new(6, (6, 8), (60, 100), Up(6), true, false, false, Hands.Together, Texture.TwoNoteChords, false, false, false, false, KeysUpTo(3), [new(4, 4), new(2, 4), new(3, 4), new(3, 8)], false, 10, 7, 2),
        7 => new(7, (8, 8), (60, 104), Up(7), true, true, true, Hands.Together, Texture.BrokenChords, false, false, false, false, KeysUpTo(3), [new(4, 4), new(2, 4), new(3, 4), new(3, 8), new(6, 8)], false, 15, 7, 2),
        8 => new(8, (8, 12), (60, 108), Up(8), true, true, true, Hands.Together, Texture.FourPart, false, false, false, false, KeysUpTo(4), [new(4, 4), new(2, 4), new(3, 4), new(3, 8), new(6, 8)], false, 15, 7, 3),
        9 => new(9, (12, 16), (60, 112), Up(9), true, true, true, Hands.Together, Texture.Patterns, true, true, false, false, KeysUpTo(4), [new(4, 4), new(2, 4), new(3, 4), new(3, 8), new(6, 8), new(9, 8), new(5, 8), new(5, 4)], false, 15, 9, 3),
        10 => new(10, (16, 20), (60, 116), Up(10), true, true, true, Hands.Together, Texture.Patterns, true, true, true, false, KeysUpTo(5), [new(4, 4), new(2, 4), new(3, 4), new(3, 8), new(6, 8), new(9, 8), new(5, 8), new(5, 4), new(7, 8), new(7, 4)], false, 17, 9, 4),
        11 => new(11, (20, 24), (60, 120), Up(11), true, true, true, Hands.Together, Texture.ThreeNoteChords, true, true, true, true, KeysUpTo(5), [new(4, 4), new(2, 4), new(3, 4), new(3, 8), new(6, 8), new(9, 8), new(5, 8), new(5, 4), new(7, 8), new(7, 4), new(12, 8), new(2, 2)], false, 20, 10, 4),
        _ => new(12, (24, 32), (60, 132), Up(12), true, true, true, Hands.Together, Texture.ThreeNoteChords, true, true, true, true, KeysUpTo(7), [new(4, 4), new(2, 4), new(3, 4), new(3, 8), new(6, 8), new(9, 8), new(5, 8), new(5, 4), new(7, 8), new(7, 4), new(12, 8), new(2, 2)], false, 22, 12, 5),
    };

    private static Cell[] Up(int level) => [.. SimpleCells.Where(c => c.Level <= level), .. CompoundCells.Where(c => c.Level <= level)];

    private static readonly LevelSpec[] Levels = [.. Enumerable.Range(1, Top).Select(Raw)];

    public static LevelSpec Level(int level) => Levels[Math.Clamp(level, 1, Top) - 1];

    /// <summary>
    /// The level as heard at a step of its introduction: step 1 brings the new level's rhythm, 2 its hands and texture, 3 its keys
    /// and metres, 4 its range and leaps; until then each kind is the level below's. Step 0 is the level below entirely, where the
    /// step back from step 1 goes. Bars and tempo are the level's own from step 1.
    /// </summary>
    public static LevelSpec Effective(int level, int step)
    {
        var now = Level(level);
        if (level <= 1 || step >= 4) return now;

        var below = Level(level - 1);
        if (step <= 0) return below;

        var spec = below with { Level = now.Level, Bars = now.Bars, Tempo = now.Tempo, Cells = now.Cells, Ties = now.Ties, Pickups = now.Pickups, Chromatic = now.Chromatic };
        if (step >= 2) spec = spec with { Hands = now.Hands, Texture = now.Texture, ClefChanges = now.ClefChanges, Pedal = now.Pedal, Octava = now.Octava, Ornaments = now.Ornaments };
        if (step >= 3) spec = spec with { Keys = now.Keys, Meters = now.Meters };
        return spec;
    }

    /// <summary>The figures that fit a metre: compound figures for 3/8, 6/8, 9/8 and 12/8; simple ones otherwise.</summary>
    public static IReadOnlyList<Cell> CellsFor(LevelSpec spec, Meter meter)
    {
        var compound = meter.Unit == 8 && meter.Count % 3 == 0;
        if (compound) return spec.Cells.Where(c => c.Compound).DefaultIfEmpty(CompoundCells[0]).ToList();
        if (meter.Unit == 8) return [new Cell("eighth", [240], 1), new Cell("quarter", [480], 1), new Cell("dotted quarter", [720], 1), new Cell("two eighths", [240, 240], 3)];
        return spec.Cells.Where(c => !c.Compound).ToList();
    }
}
