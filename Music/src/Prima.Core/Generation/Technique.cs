using Prima.Core.Music;

namespace Prima.Core.Generation;

public enum ScaleKind
{
    Major,
    HarmonicMinor,
    MelodicMinor,
    Arpeggio,
    /// <summary>Up and down a triad within a fifth: the first arpeggio step.</summary>
    BrokenTriad,
}

public enum HandUse
{
    Right,
    Left,
    Together,
}

/// <summary>A note of a technique run: its id on the sheet, key, finger, place in the run, and whether a crossing or the tonic comes next.</summary>
public sealed record TechniqueNote(string Id, int Midi, int Finger, int Index, bool CrossingNext, bool SeamNext);

/// <summary>A scale or arpeggio to play: its score with fingering, each hand's notes in order, and how many notes to a beat.</summary>
public sealed record TechniqueItem(string Name, Key Key, ScaleKind Kind, int Octaves, HandUse Hands, Score Score, IReadOnlyList<TechniqueNote> Right, IReadOnlyList<TechniqueNote> Left, int NotesPerBeat)
{
    /// <summary>What its progress is kept under: its name, and its octaves past one, as more octaves are their own item.</summary>
    public string Id => Octaves > 1 ? $"{Name}, {Octaves} octaves" : Name;
}

/// <summary>
/// The scale book: the standard fingerings for every major and minor scale (one octave going up, repeated for more), and
/// arpeggios (1-2-3-5 / 5-4-2-1 from a white key, 2-1-2-4 / 2-1-4-2 from a black one), written as runs up and down.
/// </summary>
public static class Technique
{
    // One octave up, eight notes, tonic to tonic: right hand, then left.
    private static readonly Dictionary<int, (int[] Right, int[] Left)> Major = new()
    {
        [0] = ([1, 2, 3, 1, 2, 3, 4, 5], [5, 4, 3, 2, 1, 3, 2, 1]),
        [1] = ([1, 2, 3, 1, 2, 3, 4, 5], [5, 4, 3, 2, 1, 3, 2, 1]),
        [2] = ([1, 2, 3, 1, 2, 3, 4, 5], [5, 4, 3, 2, 1, 3, 2, 1]),
        [3] = ([1, 2, 3, 1, 2, 3, 4, 5], [5, 4, 3, 2, 1, 3, 2, 1]),
        [4] = ([1, 2, 3, 1, 2, 3, 4, 5], [5, 4, 3, 2, 1, 3, 2, 1]),
        [5] = ([1, 2, 3, 1, 2, 3, 4, 5], [4, 3, 2, 1, 4, 3, 2, 1]),
        [6] = ([2, 3, 4, 1, 2, 3, 1, 2], [4, 3, 2, 1, 3, 2, 1, 4]),
        [7] = ([2, 3, 1, 2, 3, 4, 1, 2], [3, 2, 1, 4, 3, 2, 1, 3]),
        [-1] = ([1, 2, 3, 4, 1, 2, 3, 4], [5, 4, 3, 2, 1, 3, 2, 1]),
        [-2] = ([4, 1, 2, 3, 1, 2, 3, 4], [3, 2, 1, 4, 3, 2, 1, 3]),
        [-3] = ([3, 1, 2, 3, 4, 1, 2, 3], [3, 2, 1, 4, 3, 2, 1, 3]),
        [-4] = ([3, 4, 1, 2, 3, 1, 2, 3], [3, 2, 1, 4, 3, 2, 1, 3]),
        [-5] = ([2, 3, 1, 2, 3, 4, 1, 2], [3, 2, 1, 4, 3, 2, 1, 3]),
        [-6] = ([2, 3, 4, 1, 2, 3, 1, 2], [4, 3, 2, 1, 3, 2, 1, 4]),
        [-7] = ([1, 2, 3, 1, 2, 3, 4, 5], [4, 3, 2, 1, 4, 3, 2, 1]),
    };

    private static readonly Dictionary<int, (int[] Right, int[] Left)> Minor = new()
    {
        [0] = ([1, 2, 3, 1, 2, 3, 4, 5], [5, 4, 3, 2, 1, 3, 2, 1]),
        [1] = ([1, 2, 3, 1, 2, 3, 4, 5], [5, 4, 3, 2, 1, 3, 2, 1]),
        [2] = ([1, 2, 3, 1, 2, 3, 4, 5], [4, 3, 2, 1, 4, 3, 2, 1]),
        [3] = ([3, 4, 1, 2, 3, 1, 2, 3], [4, 3, 2, 1, 3, 2, 1, 4]),
        [4] = ([3, 4, 1, 2, 3, 1, 2, 3], [3, 2, 1, 4, 3, 2, 1, 3]),
        [5] = ([3, 4, 1, 2, 3, 1, 2, 3], [3, 2, 1, 3, 2, 1, 4, 3]),
        [6] = ([3, 1, 2, 3, 4, 1, 2, 3], [2, 1, 4, 3, 2, 1, 3, 2]),
        [7] = ([4, 1, 2, 3, 1, 2, 3, 4], [2, 1, 3, 2, 1, 4, 3, 2]),
        [-1] = ([1, 2, 3, 1, 2, 3, 4, 5], [5, 4, 3, 2, 1, 3, 2, 1]),
        [-2] = ([1, 2, 3, 1, 2, 3, 4, 5], [5, 4, 3, 2, 1, 3, 2, 1]),
        [-3] = ([1, 2, 3, 1, 2, 3, 4, 5], [5, 4, 3, 2, 1, 3, 2, 1]),
        [-4] = ([1, 2, 3, 4, 1, 2, 3, 4], [5, 4, 3, 2, 1, 3, 2, 1]),
        [-5] = ([4, 1, 2, 3, 1, 2, 3, 4], [2, 1, 3, 2, 1, 4, 3, 2]),
        [-6] = ([3, 1, 2, 3, 4, 1, 2, 3], [2, 1, 4, 3, 2, 1, 3, 2]),
        [-7] = ([3, 4, 1, 2, 3, 1, 2, 3], [3, 2, 1, 3, 2, 1, 4, 3]),
    };

    /// <summary>A major scale's fingering over one octave going up, tonic to tonic: the right hand's, then the left's.</summary>
    public static (int[] Right, int[] Left) MajorFingers(Key key) => Major[Math.Clamp(key.Fifths, -7, 7)];

    /// <summary>The order keys come in, by the boards' ladder: C, G, F, D and their minors first, then a sharp or flat more each.</summary>
    public static IReadOnlyList<Key> Order { get; } =
    [
        new(0), new(0, true), new(1), new(-1), new(2), new(1, true), new(-1, true), new(3), new(-2), new(2, true), new(-2, true),
        new(-3), new(4), new(3, true), new(-3, true), new(5), new(-4), new(4, true), new(-4, true), new(-5), new(6), new(5, true),
    ];

    /// <summary>A scale up and down over <paramref name="octaves"/>, in sixteenths (eighths for one octave at the start), ending on a held tonic.</summary>
    public static TechniqueItem Scale(Key key, ScaleKind kind, int octaves, HandUse hands)
    {
        var minor = kind is ScaleKind.HarmonicMinor or ScaleKind.MelodicMinor;
        var sound = key with { Minor = minor };
        var (right, left) = (minor ? Minor : Major)[Math.Clamp(key.Fifths, -7, 7)];
        int Degree(int i) => i;
        Spelled Pitch(int degree, int octave, bool up)
        {
            var p = sound.Degree(degree, octave);
            var inScale = ((degree % 7) + 7) % 7;
            if (kind == ScaleKind.HarmonicMinor && inScale == 6) p = p with { Alter = p.Alter + 1 };
            if (kind == ScaleKind.MelodicMinor && up && inScale is 5 or 6) p = p with { Alter = p.Alter + 1 };
            return p;
        }

        var count = 7 * octaves;
        var upDegrees = Enumerable.Range(0, count + 1).Select(Degree).ToList();
        var degrees = upDegrees.Select(d => (d, true)).Concat(upDegrees.AsEnumerable().Reverse().Skip(1).Select(d => (d, false))).ToList();
        int RightFinger(int i) => i == count ? right[7] : right[i % 7];
        int LeftFinger(int i) => i == 0 ? left[0] : left[((i - 1) % 7) + 1];
        int Finger(int index, bool rh) { var i = index <= count ? index : 2 * count - index; return rh ? RightFinger(i) : LeftFinger(i); }
        var name = $"{sound.Name}{(kind == ScaleKind.MelodicMinor ? " melodic" : kind == ScaleKind.HarmonicMinor ? " harmonic" : "")} scale";
        var (rightOctave, leftOctave) = Octaves(octaves);
        return Build(name, sound, kind, octaves, hands, degrees.Select((x, i) => (Pitch(x.d, rightOctave, x.Item2), Finger(i, true))).ToList(), degrees.Select((x, i) => (Pitch(x.d, leftOctave, x.Item2), Finger(i, false))).ToList(), octaves == 1 ? 2 : 4);
    }

    /// <summary>An arpeggio up and down over <paramref name="octaves"/> (or a broken triad within a fifth), in eighths.</summary>
    public static TechniqueItem Arpeggio(Key key, int octaves, HandUse hands, bool brokenTriad = false)
    {
        var tonic = key.Degree(0, 4);
        var black = ((tonic.Midi % 12) + 12) % 12 is 1 or 3 or 6 or 8 or 10;
        if (brokenTriad)
        {
            int[] steps = [0, 2, 4, 2, 0];
            var rightTriad = steps.Select((d, i) => (key.Degree(d, 4), new[] { 1, 3, 5, 3, 1 }[i])).ToList();
            var leftTriad = steps.Select((d, i) => (key.Degree(d, 3), new[] { 5, 3, 1, 3, 5 }[i])).ToList();
            return Build($"{key.Name} broken triad", key, ScaleKind.BrokenTriad, 1, hands, rightTriad, leftTriad, 1);
        }

        int[] rightPattern = black ? [2, 1, 2, 4] : [1, 2, 3, 5];
        int[] leftPattern = black ? [2, 1, 4, 2] : [5, 4, 2, 1];
        var count = 3 * octaves;
        var up = Enumerable.Range(0, count + 1).Select(i => i / 3 * 7 + new[] { 0, 2, 4 }[i % 3]).ToList();
        var degrees = up.Concat(up.AsEnumerable().Reverse().Skip(1)).ToList();
        int RightFinger(int i) => i == count ? rightPattern[3] : i % 3 == 0 && i > 0 ? rightPattern[0] : rightPattern[i % 3];
        int LeftFinger(int i) => i == 0 ? leftPattern[0] : i % 3 == 0 ? leftPattern[3] : leftPattern[i % 3];
        int Finger(int index, bool rh) { var i = index <= count ? index : 2 * count - index; return rh ? RightFinger(i) : LeftFinger(i); }
        var (rightOctave, leftOctave) = Octaves(octaves);
        return Build($"{key.Name} arpeggio", key, ScaleKind.Arpeggio, octaves, hands, degrees.Select((d, i) => (key.Degree(d, rightOctave), Finger(i, true))).ToList(), degrees.Select((d, i) => (key.Degree(d, leftOctave), Finger(i, false))).ToList(), 2);
    }

    /// <summary>
    /// Finger independence (plan): a five-finger pattern up and down from the tonic (step 1), then trills on the weaker pairs,
    /// 3–4 (step 2) and 4–5 (step 3), each a bar of sixteenths with fixed fingering.
    /// </summary>
    public static TechniqueItem FingerPattern(Key key, int step, HandUse hands)
    {
        var (rightOctave, leftOctave) = Octaves(1);
        (int[] Degrees, int[] Right, int[] Left, string Name) pattern = step switch
        {
            <= 1 => ([0, 1, 2, 3, 4, 3, 2, 1, 0], [1, 2, 3, 4, 5, 4, 3, 2, 1], [5, 4, 3, 2, 1, 2, 3, 4, 5], "five-finger pattern"),
            2 => ([2, 3, 2, 3, 2, 3, 2, 3, 2, 3, 2, 3, 2, 3, 2, 3, 2], [3, 4, 3, 4, 3, 4, 3, 4, 3, 4, 3, 4, 3, 4, 3, 4, 3], [3, 2, 3, 2, 3, 2, 3, 2, 3, 2, 3, 2, 3, 2, 3, 2, 3], "trill, fingers 3 and 4"),
            _ => ([3, 4, 3, 4, 3, 4, 3, 4, 3, 4, 3, 4, 3, 4, 3, 4, 3], [4, 5, 4, 5, 4, 5, 4, 5, 4, 5, 4, 5, 4, 5, 4, 5, 4], [2, 1, 2, 1, 2, 1, 2, 1, 2, 1, 2, 1, 2, 1, 2, 1, 2], "trill, fingers 4 and 5"),
        };
        // The left hand plays the same notes an octave down, so its trill pair is 3–2 or 2–1 by position; the right hand's are the weak ones.
        var right = pattern.Degrees.Select((d, i) => (key.Degree(d, rightOctave), pattern.Right[i])).ToList();
        var left = pattern.Degrees.Select((d, i) => (key.Degree(d, leftOctave), pattern.Left[i])).ToList();
        return Build($"{key.Name} {pattern.Name}", key, ScaleKind.BrokenTriad, 1, hands, right, left, 4);
    }

    // Where each hand starts, so runs stay near their staves: an octave apart for one octave, further apart for more.
    private static (int Right, int Left) Octaves(int octaves) => octaves switch { <= 1 => (4, 3), 2 => (4, 2), _ => (3, 1) };

    // Lays the run out in bars of 4/4: the notes at the given speed, the last one held to the end of its bar, fingering printed.
    private static TechniqueItem Build(string name, Key key, ScaleKind kind, int octaves, HandUse hands, List<(Spelled Pitch, int Finger)> right, List<(Spelled Pitch, int Finger)> left, int perBeat)
    {
        var step = Ticks.Quarter / perBeat;
        var tonicClass = key.TonicPitchClass;
        List<TechniqueNote> Notes(List<(Spelled Pitch, int Finger)> run, string prefix) => run.Select((n, i) =>
        {
            var next = i + 1 < run.Count ? run[i + 1] : default;
            var crossing = i + 1 < run.Count && (n.Finger == 1 && next.Finger >= 3 || n.Finger >= 3 && next.Finger == 1);
            var seam = i + 1 < run.Count && i + 1 < run.Count - 1 && ((next.Pitch.Midi % 12) + 12) % 12 == tonicClass;
            return new TechniqueNote($"{prefix}{i}", n.Pitch.Midi, n.Finger, i, crossing, seam);
        }).ToList();

        var rightNotes = hands == HandUse.Left ? [] : Notes(right, "r");
        var leftNotes = hands == HandUse.Right ? [] : Notes(left, "l");
        var total = right.Count;
        var bars = new List<Bar>();
        var ticks = (total - 1) * step + Ticks.Quarter;
        var barCount = (ticks + Ticks.Whole - 1) / Ticks.Whole;
        for (var b = 0; b < barCount; b++)
        {
            var voices = new List<Voice>();
            foreach (var (staff, run, notes) in new[] { (1, right, rightNotes), (2, left, leftNotes) })
            {
                var events = new List<Event>();
                var at = 0;
                for (var i = 0; i < total; i++)
                {
                    var start = i * step - b * Ticks.Whole;
                    var length = i == total - 1 ? Ticks.Quarter : step;
                    if (start < 0 || start >= Ticks.Whole) continue;
                    if (notes.Count == 0) continue;

                    events.Add(new Notes(notes[i].Id, start, length, [new Tone(notes[i].Id, run[i].Pitch) { Finger = run[i].Finger }]));
                    at = start + length;
                }
                for (var rest = 0; at < Ticks.Whole; rest++)
                {
                    var length = new[] { 960, 480, 240, 120 }.First(l => l <= Ticks.Whole - at && at % l == 0);
                    events.Add(new Rest($"s{staff}b{b}r{rest}", at, length) { Hidden = notes.Count == 0 });
                    at += length;
                }
                voices.Add(new Voice(staff, events));
            }
            bars.Add(new Bar(b + 1, voices));
        }
        return new TechniqueItem(name, key, kind, octaves, hands, new Score(key, new Meter(4, 4), bars) { Title = name }, rightNotes, leftNotes, perBeat);
    }
}

/// <summary>
/// What a technique drill plays, chosen freely whatever the reading level, or the day's: the key, a minor scale's form, the
/// octaves (0 for an arpeggio's broken triad within a fifth), the hands, and finger independence's pattern (1 the five-finger
/// pattern, 2 the 3–4 trill, 3 the 4–5 trill).
/// </summary>
public sealed record TechniqueChoice(Key Key, ScaleKind Form, int Octaves, HandUse Hands, int Pattern)
{
    /// <summary>The scale, arpeggio or pattern for drill "scales", "arpeggios" or "independence".</summary>
    public TechniqueItem Item(string drill) => drill switch
    {
        "arpeggios" => Octaves <= 0 ? Technique.Arpeggio(Key, 1, Hands, brokenTriad: true) : Technique.Arpeggio(Key, Octaves, Hands),
        "independence" => Technique.FingerPattern(Key with { Minor = false }, Pattern, Hands),
        _ => Technique.Scale(Key, Key.Minor ? (Form == ScaleKind.MelodicMinor ? ScaleKind.MelodicMinor : ScaleKind.HarmonicMinor) : ScaleKind.Major, Math.Max(1, Octaves), Hands),
    };

    /// <summary>
    /// The day's: the plan's key for the drill, at the octaves the reading level gives (one to level 2, two to 6, three to 9, then
    /// four; an arpeggio starts as a broken triad at level 1), a minor scale melodic every other day from level 6.
    /// </summary>
    public static TechniqueChoice Today(string drill, Key scaleKey, Key arpeggioKey, int level, int pattern, DateOnly day)
    {
        var octaves = level <= 2 ? 1 : level <= 6 ? 2 : level <= 9 ? 3 : 4;
        return drill switch
        {
            "arpeggios" => new(arpeggioKey, ScaleKind.Arpeggio, level <= 1 ? 0 : Math.Min(octaves, 2 + level / 6), HandUse.Right, pattern),
            "independence" => new(scaleKey with { Minor = false }, ScaleKind.Major, 1, HandUse.Right, pattern),
            _ => new(scaleKey, scaleKey.Minor && level >= 6 && day.DayNumber % 2 == 1 ? ScaleKind.MelodicMinor : ScaleKind.HarmonicMinor, octaves, HandUse.Right, pattern),
        };
    }
}

/// <summary>
/// The rhythm drill's material (RCM's shape): one bar of beat, then the rhythm on one line, 2 bars at the early levels, 4 in the
/// middle and 4 to 6 at the top, from the level's figures.
/// </summary>
public static class RhythmDrill
{
    public static Score Generate(int level, ulong seed)
    {
        var bars = level <= 4 ? 2 : level <= 8 ? 4 : 4 + (int)(seed % 3);
        var spec = Ladder.Level(level) with { Hands = Hands.One, Bars = (bars, bars), Keys = [new Key(0)], Pickups = false, FixedPosition = true, Span = 5 };
        var passage = PassageGenerator.Generate(spec, seed);
        var line = new Spelled(Step.B, 0, 4);
        var meter = passage.Score.Meter;
        var beatEvents = Enumerable.Range(0, meter.Beats).Select(b => (Event)new Notes($"beat{b}", b * meter.BeatTicks, meter.BeatTicks, [new Tone($"beat{b}", line)])).ToList();
        var rhythmBars = passage.Score.Bars.Select(bar =>
        {
            var voice = bar.Voices.Where(v => v.Events.OfType<Notes>().Any()).DefaultIfEmpty(bar.Voices[0]).First();
            var events = voice.Events.Select(e => e switch
            {
                Notes n => n with { Tones = [new Tone(n.Id, line) { TieToNext = n.Tones[0].TieToNext, TiedFromPrevious = n.Tones[0].TiedFromPrevious }] },
                Rest r => r with { Hidden = false },
                _ => e,
            }).Where(e => e is not ClefChange).ToList();
            return new Bar(bar.Number + 1, [new Voice(1, events)]);
        }).ToList();
        return new Score(new Key(0), meter, [new Bar(1, [new Voice(1, beatEvents)]), .. rhythmBars]) { RhythmOnly = true };
    }
}
