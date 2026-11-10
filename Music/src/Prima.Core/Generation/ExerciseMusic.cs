using Prima.Core.Music;

namespace Prima.Core.Generation;

/// <summary>
/// The music of the exercises that are played: each is written as what the right hand and the left hand do, a stroke at a time,
/// and laid out in bars with its fingering. A hand left out by the choice of hands has no line. A stroke's id on the sheet is
/// its hand's letter and its place in the line ("r0", "l7"); a chord's keys add a letter ("r3a"), and marks point at these.
/// </summary>
public static class ExerciseMusic
{
    private const int W = Ticks.Whole, H = Ticks.Half, Q = Ticks.Quarter, E = Ticks.Eighth, S = Ticks.Sixteenth;

    private static readonly Meter Four = new(4, 4);
    private static readonly Meter Three = new(3, 4);

    // Five notes up and back, short of the last tonic, and the fingers that play them.
    private static readonly int[] UpAndBack = [0, 1, 2, 3, 4, 3, 2, 1];
    private static readonly int[] RightFive = [1, 2, 3, 4, 5, 4, 3, 2];
    private static readonly int[] LeftFive = [5, 4, 3, 2, 1, 2, 3, 4];

    // A rest that can end a bar: its length, and the beat it has to start on.
    private static readonly (int Length, int Grid)[] RestLengths = [(W, W), (H + Q, H + Q), (H, H), (Q, Q), (E, E), (S, S)];

    /// <summary>One thing a hand plays: keys with their fingers (none of them: a rest), for a length in ticks.</summary>
    private sealed record Stroke(IReadOnlyList<(Spelled Pitch, int? Finger)> Keys, int Length)
    {
        public Articulation Touch { get; init; }

        public Tuplet? Tuplet { get; init; }

        /// <summary>Tied on to the next stroke, which has the same keys.</summary>
        public bool Tie { get; init; }
    }

    private static Stroke Note(Spelled pitch, int? finger, int length, Articulation touch = Articulation.None) => new([(pitch, finger)], length) { Touch = touch };

    private static Stroke Chord(IEnumerable<Spelled> keys, int[] fingers, int length) => new([.. keys.Zip(fingers, (k, f) => (k, (int?)f))], length);

    private static Stroke Rest(int length) => new([], length);

    // Notes of the scale, one after another: each `length` long, the last one `last` if that's given and never detached.
    private static List<Stroke> Line(Key key, int octave, IReadOnlyList<int> degrees, IReadOnlyList<int> fingers, int length, int last = 0, Articulation touch = Articulation.None) =>
        [.. degrees.Select((d, i) => i == degrees.Count - 1 && last > 0 ? Note(key.Degree(d, octave), fingers[i % fingers.Count], last) : Note(key.Degree(d, octave), fingers[i % fingers.Count], length, touch))];

    // A note of the scale as a chord has it: the seventh raised in a minor key, so its dominant is major.
    private static Spelled InChord(Key key, int degree, int octave)
    {
        var pitch = key.Degree(degree, octave);
        return key.Minor && ((degree % 7) + 7) % 7 == 6 ? pitch with { Alter = pitch.Alter + 1 } : pitch;
    }

    private static Stroke Triad(Key key, int octave, int[] degrees, int[] fingers, int length) => Chord(degrees.Select(d => InChord(key, d, octave)), fingers, length);

    // The hands chosen: the other's line is left out.
    private static (IReadOnlyList<Stroke> Right, IReadOnlyList<Stroke> Left) Chosen(HandUse hands, IReadOnlyList<Stroke> right, IReadOnlyList<Stroke> left) =>
        (hands == HandUse.Left ? [] : right, hands == HandUse.Right ? [] : left);

    /// <summary>
    /// Lays two lines out in bars: each stroke in turn, none over a bar line, the rest of a line's last bar as rests. A hand with
    /// no line takes its bars' time unprinted.
    /// </summary>
    private static Score Lay(Key key, Meter meter, IReadOnlyList<Stroke> right, IReadOnlyList<Stroke> left, IReadOnlyList<Mark>? marks = null)
    {
        var barTicks = meter.BarTicks;
        var lines = new[] { (Prefix: "r", Strokes: right), (Prefix: "l", Strokes: left) };
        var barCount = lines.Max(l => (l.Strokes.Sum(s => s.Length) + barTicks - 1) / barTicks);
        var voices = lines.Select(_ => Enumerable.Range(0, barCount).Select(_ => new List<Event>()).ToList()).ToList();
        for (var hand = 0; hand < lines.Length; hand++)
        {
            var (prefix, strokes) = lines[hand];
            var at = 0;
            for (var i = 0; i < strokes.Count; i++)
            {
                var stroke = strokes[i];
                var (bar, start) = (at / barTicks, at % barTicks);
                if (start + stroke.Length > barTicks) throw new InvalidOperationException($"Stroke {prefix}{i} runs over its bar line.");

                var id = $"{prefix}{i}";
                var tiedIn = i > 0 && strokes[i - 1].Tie;
                var tones = stroke.Keys.Select((k, n) => new Tone(stroke.Keys.Count == 1 ? id : $"{id}{(char)('a' + n)}", k.Pitch) { Finger = k.Finger, TieToNext = stroke.Tie, TiedFromPrevious = tiedIn }).ToList();
                voices[hand][bar].Add(tones.Count == 0 ? new Rest(id, start, stroke.Length) { Tuplet = stroke.Tuplet } : new Notes(id, start, stroke.Length, tones) { Articulation = stroke.Touch, Tuplet = stroke.Tuplet });
                at += stroke.Length;
            }

            for (var bar = 0; bar < barCount; bar++)
            {
                var filled = voices[hand][bar].Count == 0 ? 0 : voices[hand][bar][^1].Start + voices[hand][bar][^1].Length;
                for (var n = 0; filled < barTicks; n++)
                {
                    var length = RestLengths.First(r => r.Length <= barTicks - filled && filled % r.Grid == 0).Length;
                    voices[hand][bar].Add(new Rest($"{prefix}b{bar}x{n}", filled, length) { Hidden = strokes.Count == 0 });
                    filled += length;
                }
            }
        }
        return new Score(key, meter, [.. Enumerable.Range(0, barCount).Select(b => new Bar(b + 1, [new Voice(1, voices[0][b]), new Voice(2, voices[1][b])]))]) { Marks = marks ?? [] };
    }

    // ——— Arm weight and rotation ———

    /// <summary>A whole bar on a finger at a time, in the C position: the third, the second, the fourth, the third.</summary>
    public static Score Drop(Key key, HandUse hands)
    {
        var right = new[] { (2, 3), (1, 2), (3, 4), (2, 3) }.Select(n => Note(key.Degree(n.Item1, 4), n.Item2, W)).ToList();
        var left = new[] { (2, 3), (3, 2), (1, 4), (2, 3) }.Select(n => Note(key.Degree(n.Item1, 3), n.Item2, W)).ToList();
        var (r, l) = Chosen(hands, right, left);
        return Lay(key, Four, r, l);
    }

    /// <summary>Two notes rocked between: a bar of quarters, a bar of eighths, a bar of sixteenths if <paramref name="quick"/>, and the first note held.</summary>
    public static Score Rock(Key key, HandUse hands, (int Low, int High) rightFingers, bool quick = false)
    {
        // The right hand's fingers sit on the scale from its thumb up; the left's mirror them from its little finger up.
        List<Stroke> Hand(int octave, int low, int high, int lowFinger, int highFinger)
        {
            var (a, b) = (key.Degree(low, octave), key.Degree(high, octave));
            IEnumerable<Stroke> Bar(int length) => Enumerable.Range(0, W / length).Select(i => i % 2 == 0 ? Note(a, lowFinger, length) : Note(b, highFinger, length));
            return [.. Bar(Q), .. Bar(E), .. quick ? Bar(S) : Enumerable.Empty<Stroke>(), Note(a, lowFinger, W)];
        }
        var (r, l) = Chosen(hands, Hand(4, rightFingers.Low - 1, rightFingers.High - 1, rightFingers.Low, rightFingers.High), Hand(3, 5 - rightFingers.High, 5 - rightFingers.Low, rightFingers.High, rightFingers.Low));
        return Lay(key, Four, r, l);
    }

    /// <summary>A bar of eighths on each pair of fingers a third apart: 1 and 3, 2 and 4, 3 and 5, then the tonic held.</summary>
    public static Score SmallTurns(Key key, HandUse hands)
    {
        List<Stroke> Hand(int octave, (int Low, int High)[] fingers) =>
            [.. Enumerable.Range(0, 3).SelectMany(p => Enumerable.Range(0, 8).Select(i => i % 2 == 0 ? Note(key.Degree(p, octave), fingers[p].Low, E) : Note(key.Degree(p + 2, octave), fingers[p].High, E))), Note(key.Degree(0, octave), fingers[0].Low, W)];
        var (r, l) = Chosen(hands, Hand(4, [(1, 3), (2, 4), (3, 5)]), Hand(3, [(5, 3), (4, 2), (3, 1)]));
        return Lay(key, Four, r, l);
    }

    /// <summary>A note held for two bars on the third finger, then the second, then the fourth.</summary>
    public static Score Held(Key key, HandUse hands)
    {
        List<Stroke> Hand(int octave, (int Degree, int Finger)[] notes) => [.. notes.SelectMany(n => new[] { Note(key.Degree(n.Degree, octave), n.Finger, W) with { Tie = true }, Note(key.Degree(n.Degree, octave), n.Finger, W) })];
        var (r, l) = Chosen(hands, Hand(4, [(2, 3), (1, 2), (3, 4)]), Hand(3, [(2, 3), (3, 2), (1, 4)]));
        return Lay(key, Four, r, l);
    }

    // ——— Five-finger patterns ———

    /// <summary>Five notes up and back in quarters and the tonic held, each hand an octave apart; detached if <paramref name="touch"/> says so.</summary>
    public static Score Five(Key key, HandUse hands, Articulation touch = Articulation.None)
    {
        var (r, l) = Chosen(hands, Line(key, 4, [.. UpAndBack, 0], [.. RightFive, 1], Q, W, touch), Line(key, 3, [.. UpAndBack, 0], [.. LeftFive, 5], Q, W, touch));
        return Lay(key, Four, r, l);
    }

    /// <summary>The pattern in major, then with its third lowered.</summary>
    public static Score MajorMinor(Key key, HandUse hands)
    {
        List<Stroke> Hand(int octave, int[] fingers, int last)
        {
            var major = UpAndBack.Select((d, i) => Note(key.Degree(d, octave), fingers[i], Q));
            var minor = UpAndBack.Select((d, i) => Note(d == 2 ? key.Degree(d, octave) with { Alter = key.Degree(d, octave).Alter - 1 } : key.Degree(d, octave), fingers[i], Q));
            return [.. major, .. minor, Note(key.Degree(0, octave), last, W)];
        }
        var (r, l) = Chosen(hands, Hand(4, RightFive, 1), Hand(3, LeftFive, 5));
        return Lay(key, Four, r, l);
    }

    /// <summary>The hands in mirror: the right up from the tonic and back, the left down from the tonic an octave below and back.</summary>
    public static Score Mirror(Key key, HandUse hands) =>
        Lay(key, Four, Line(key, 4, [.. UpAndBack, 0], [.. RightFive, 1], Q, W), Line(key, 3, [0, -1, -2, -3, -4, -3, -2, -1, 0], [.. RightFive, 1], Q, W));

    /// <summary>The pattern long–short: a dotted eighth and a sixteenth to each beat, twice through, and the tonic held.</summary>
    public static Score LongShort(Key key, HandUse hands)
    {
        List<Stroke> Hand(int octave, int[] fingers, int last) =>
            [.. Enumerable.Range(0, 16).Select(i => Note(key.Degree(UpAndBack[i % 8], octave), fingers[i % 8], i % 2 == 0 ? E + S : S)), Note(key.Degree(0, octave), last, W)];
        var (r, l) = Chosen(hands, Hand(4, RightFive, 1), Hand(3, LeftFive, 5));
        return Lay(key, Four, r, l);
    }

    /// <summary>The pattern, then the three of its notes that make the chord: broken, then together.</summary>
    public static Score FiveAndChord(Key key, HandUse hands)
    {
        List<Stroke> Hand(int octave, int[] five, int[] chord) =>
            [.. Line(key, octave, UpAndBack, five, Q), .. Line(key, octave, [0, 2, 4, 2], [chord[0], chord[1], chord[2], chord[1]], Q), Triad(key, octave, [0, 2, 4], chord, W)];
        var (r, l) = Chosen(hands, Hand(4, RightFive, [1, 3, 5]), Hand(3, LeftFive, [5, 3, 1]));
        return Lay(key, Four, r, l);
    }

    /// <summary>
    /// Hanon's first exercise (1873) over one octave, in eighths: a figure that skips a note and walks on, a step higher each bar,
    /// then its mirror back down.
    /// </summary>
    public static Score Hanon(Key key, HandUse hands)
    {
        int[] up = [0, 2, 3, 4, 5, 4, 3, 2];
        List<Stroke> Hand(int octave, int[] rising, int[] falling, int last) =>
        [
            .. Enumerable.Range(0, 7).SelectMany(bar => up.Select((d, i) => Note(key.Degree(bar + d, octave), rising[i], E))),
            .. Enumerable.Range(0, 7).SelectMany(bar => up.Select((d, i) => Note(key.Degree(11 - bar - d, octave), falling[i], E))),
            Note(key.Degree(0, octave), last, W),
        ];
        var (r, l) = Chosen(hands, Hand(4, RightFive, [5, 4, 3, 2, 1, 2, 3, 4], 1), Hand(3, LeftFive, [1, 2, 3, 4, 5, 4, 3, 2], 5));
        return Lay(key, Four, r, l);
    }

    // ——— Scales ———

    /// <summary>A major scale in contrary motion over an octave, in eighths: the right hand up and back, the left down and back, an octave apart.</summary>
    public static Score ContraryScale(Key key, HandUse hands)
    {
        var (rightUp, leftUp) = Technique.MajorFingers(key);
        // Going down from its tonic, the left hand takes its fingering backwards.
        var steps = Enumerable.Range(0, 8).Concat(Enumerable.Range(0, 7).Reverse()).ToList();
        var right = steps.Select((d, i) => Note(key.Degree(d, 4), rightUp[d], i == steps.Count - 1 ? Q : E)).ToList();
        var left = steps.Select((d, i) => Note(key.Degree(-d, 3), leftUp[7 - d], i == steps.Count - 1 ? Q : E)).ToList();
        return Lay(key, Four, right, left);
    }

    /// <summary>The chromatic scale from C over an octave and back, in eighths: the third finger on every black key.</summary>
    public static Score Chromatic(Key key, HandUse hands)
    {
        // The thumb on white keys, but the second finger on the upper of two whites side by side (right hand: F and C; left: E and B).
        static int Finger(int midi, bool rightHand, bool first) => (midi % 12) switch
        {
            1 or 3 or 6 or 8 or 10 => 3,
            0 or 5 when rightHand && !first => 2,
            4 or 11 when !rightHand => 2,
            _ => 1,
        };
        List<Stroke> Hand(int from, bool rightHand)
        {
            var keys = Enumerable.Range(0, 13).Concat(Enumerable.Range(0, 12).Reverse()).Select(k => from + k).ToList();
            return [.. keys.Select((midi, i) => Note(key.Spell(midi), Finger(midi, rightHand, i is 0 or 24), i == keys.Count - 1 ? W : E))];
        }
        var (r, l) = Chosen(hands, Hand(60, true), Hand(48, false));
        return Lay(key, Four, r, l);
    }

    // ——— Broken chords and left-hand patterns ———

    /// <summary>The tonic chord broken and passed from hand to hand, in three: the left up, the right up, the right down, the left down.</summary>
    public static Score Passed(Key key, HandUse hands)
    {
        List<Stroke> right = [Rest(H + Q), .. Line(key, 4, [0, 2, 4], [1, 3, 5], Q), .. Line(key, 4, [4, 2, 0], [5, 3, 1], Q), Rest(H + Q), Note(key.Degree(0, 4), 1, H + Q)];
        List<Stroke> left = [.. Line(key, 3, [0, 2, 4], [5, 3, 1], Q), Rest(H + Q), Rest(H + Q), .. Line(key, 3, [4, 2, 0], [1, 3, 5], Q), Note(key.Degree(0, 3), 5, H + Q)];
        return Lay(key, Three, right, left);
    }

    /// <summary>The tonic chord broken through its inversions, in three: up to the octave, and back.</summary>
    public static Score BrokenInversions(Key key, HandUse hands)
    {
        int[][] shapes = [[0, 2, 4], [2, 4, 7], [4, 7, 9], [7, 9, 11]];
        List<Stroke> Hand(int octave, int[][] fingers, int last)
        {
            var up = Enumerable.Range(0, 4).SelectMany(s => Enumerable.Range(0, 3).Select(i => Note(key.Degree(shapes[s][i], octave), fingers[s][i], Q)));
            var down = Enumerable.Range(0, 3).Select(s => 2 - s).SelectMany(s => Enumerable.Range(0, 3).Select(i => Note(key.Degree(shapes[s][2 - i], octave), fingers[s][2 - i], Q)));
            return [.. up, .. down, Note(key.Degree(0, octave), last, H + Q)];
        }
        var (r, l) = Chosen(hands, Hand(4, [[1, 3, 5], [1, 2, 5], [1, 3, 5], [1, 3, 5]], 1), Hand(3, [[5, 3, 1], [5, 3, 1], [5, 2, 1], [5, 3, 1]], 5));
        return Lay(key, Three, r, l);
    }

    /// <summary>An Alberti bass for the left hand: bottom, top, middle, top, on the tonic, subdominant and dominant chords, hand in one place.</summary>
    public static Score Alberti(Key key, HandUse hands)
    {
        (int[] Chord, int[] Fingers)[] bars = [([0, 2, 4], [5, 3, 1]), ([0, 3, 5], [5, 2, 1]), ([-1, 1, 4], [5, 3, 1]), ([0, 2, 4], [5, 3, 1])];
        List<Stroke> left =
        [
            .. bars.SelectMany(b => Enumerable.Range(0, 8).Select(i => (i % 4) switch { 0 => 0, 2 => 1, _ => 2 }).Select(n => Note(InChord(key, b.Chord[n], 3), b.Fingers[n], E))),
            Triad(key, 3, [0, 2, 4], [5, 3, 1], W),
        ];
        return Lay(key, Four, [], left);
    }

    // ——— Chords and cadences ———

    /// <summary>Solid chords, each a quarter and a quarter's rest, the last one held: <paramref name="chords"/> as scale degrees with each hand's fingers.</summary>
    private static Score Solid(Key key, HandUse hands, IReadOnlyList<(int[] Degrees, int[] Right, int[] Left)> chords)
    {
        List<Stroke> Hand(int octave, Func<(int[] Degrees, int[] Right, int[] Left), int[]> fingers) =>
            [.. chords.Take(chords.Count - 1).SelectMany(c => new[] { Triad(key, octave, c.Degrees, fingers(c), Q), Rest(Q) }), Triad(key, octave, chords[^1].Degrees, fingers(chords[^1]), W)];
        var (r, l) = Chosen(hands, Hand(4, c => c.Right), Hand(3, c => c.Left));
        return Lay(key, Four, r, l);
    }

    /// <summary>One shape up the scale and back: a triad on each note, hand and fingers unchanged.</summary>
    public static Score TriadWalk(Key key, HandUse hands) =>
        Solid(key, hands, [.. Enumerable.Range(0, 8).Concat(Enumerable.Range(0, 7).Reverse()).Select(d => (new[] { d, d + 2, d + 4 }, new[] { 1, 3, 5 }, new[] { 5, 3, 1 }))]);

    /// <summary>The tonic chord up through its inversions to the octave, and back.</summary>
    public static Score Inversions(Key key, HandUse hands)
    {
        (int[] Degrees, int[] Right, int[] Left)[] shapes = [([0, 2, 4], [1, 3, 5], [5, 3, 1]), ([2, 4, 7], [1, 2, 5], [5, 3, 1]), ([4, 7, 9], [1, 3, 5], [5, 2, 1]), ([7, 9, 11], [1, 3, 5], [5, 3, 1])];
        return Solid(key, hands, [shapes[0], shapes[1], shapes[2], shapes[3], shapes[2], shapes[1], shapes[0]]);
    }

    /// <summary>Major, minor, diminished and augmented on the key's tonic, in half notes, and major again to end.</summary>
    public static Score ChordKinds(Key key, HandUse hands)
    {
        List<Stroke> Hand(int octave, int[] fingers)
        {
            var (root, third, fifth) = (key.Degree(0, octave), key.Degree(2, octave), key.Degree(4, octave));
            var (minor, flat, sharp) = (third with { Alter = third.Alter - 1 }, fifth with { Alter = fifth.Alter - 1 }, fifth with { Alter = fifth.Alter + 1 });
            return [Chord([root, third, fifth], fingers, H), Chord([root, minor, fifth], fingers, H), Chord([root, minor, flat], fingers, H), Chord([root, third, sharp], fingers, H), Chord([root, third, fifth], fingers, W)];
        }
        var (r, l) = Chosen(hands, Hand(4, [1, 3, 5]), Hand(3, [5, 3, 1]));
        return Lay(key, Four, r, l);
    }

    /// <summary>
    /// Chords in half notes, the last held. With both hands the right plays the chords and the left their roots; one hand alone
    /// plays the chords. Each chord is its scale degrees with each hand's fingers, and its root for the left hand, from the tonic.
    /// </summary>
    private static Score Progression(Key key, HandUse hands, IReadOnlyList<(int[] Degrees, int[] Right, int[] Left, int Root)> chords, int chordLength = H)
    {
        List<Stroke> Chords(int octave, Func<(int[] Degrees, int[] Right, int[] Left, int Root), int[]> fingers) =>
        [
            .. chords.Take(chords.Count - 1).SelectMany(c => chordLength == H ? [Triad(key, octave, c.Degrees, fingers(c), H)] : new[] { Triad(key, octave, c.Degrees, fingers(c), chordLength), Rest(H - chordLength) }),
            Triad(key, octave, chords[^1].Degrees, fingers(chords[^1]), W),
        ];
        List<Stroke> roots = [.. chords.Select((c, i) => Note(InChord(key, c.Root, 3), null, i == chords.Count - 1 ? W : H))];
        return hands switch
        {
            HandUse.Right => Lay(key, Four, Chords(4, c => c.Right), []),
            HandUse.Left => Lay(key, Four, [], Chords(3, c => c.Left)),
            _ => Lay(key, Four, Chords(4, c => c.Right), roots),
        };
    }

    // The three chords of a key, close together so the hand barely moves, and the same three each in root position.
    private static readonly (int[] Degrees, int[] Right, int[] Left, int Root) Tonic = ([0, 2, 4], [1, 3, 5], [5, 3, 1], 0);
    private static readonly (int[] Degrees, int[] Right, int[] Left, int Root) SubdominantNear = ([0, 3, 5], [1, 3, 5], [5, 2, 1], -4);
    private static readonly (int[] Degrees, int[] Right, int[] Left, int Root) DominantNear = ([-1, 1, 4], [1, 2, 5], [5, 3, 1], -3);
    private static readonly (int[] Degrees, int[] Right, int[] Left, int Root) SubmediantNear = ([0, 2, 5], [1, 2, 5], [5, 3, 1], -2);
    private static readonly (int[] Degrees, int[] Right, int[] Left, int Root) SubdominantFar = ([3, 5, 7], [1, 3, 5], [5, 3, 1], -4);
    private static readonly (int[] Degrees, int[] Right, int[] Left, int Root) DominantFar = ([4, 6, 8], [1, 3, 5], [5, 3, 1], -3);

    /// <summary>The cadence I–IV–I–V–I with the chords close together, common notes kept.</summary>
    public static Score Cadence(Key key, HandUse hands) => Progression(key, hands, [Tonic, SubdominantNear, Tonic, DominantNear, Tonic]);

    /// <summary>The same cadence with every chord in root position, a quarter each and a rest to travel in: the hand jumps.</summary>
    public static Score CadenceFar(Key key, HandUse hands) => Progression(key, hands, [Tonic, SubdominantFar, Tonic, DominantFar, Tonic], Q);

    /// <summary>I–V–vi–IV twice round and home, the chords close together.</summary>
    public static Score FourChords(Key key, HandUse hands) =>
        Progression(key, hands, [Tonic, DominantNear, SubmediantNear, SubdominantNear, Tonic, DominantNear, SubmediantNear, SubdominantNear, Tonic]);

    // ——— Jumps ———

    // From one place to another and back with less and less time to travel: three beats of rest (twice each way), then one beat
    // (twice), then none, then the first place held.
    private static List<Stroke> Travel(Func<int, Stroke> here, Func<int, Stroke> there) =>
    [
        .. Enumerable.Range(0, 4).SelectMany(i => new[] { i % 2 == 0 ? here(Q) : there(Q), Rest(Q), Rest(H) }),
        .. Enumerable.Range(0, 4).SelectMany(i => new[] { i % 2 == 0 ? here(Q) : there(Q), Rest(Q) }),
        here(Q), there(Q), here(Q), there(Q),
        here(W),
    ];

    /// <summary>One finger from the tonic to the tonic an octave away and back (the right hand up, the left down), with less time each line.</summary>
    public static Score OctaveJump(Key key, HandUse hands)
    {
        var (r, l) = Chosen(hands,
            Travel(n => Note(key.Degree(0, 4), 3, n), n => Note(key.Degree(7, 4), 3, n)),
            Travel(n => Note(key.Degree(0, 3), 3, n), n => Note(key.Degree(-7, 3), 3, n)));
        return Lay(key, Four, r, l);
    }

    /// <summary>The tonic chord and the same chord an octave away (the right hand up, the left down), with less time each line.</summary>
    public static Score ChordJump(Key key, HandUse hands)
    {
        var (r, l) = Chosen(hands,
            Travel(n => Triad(key, 4, [0, 2, 4], [1, 3, 5], n), n => Triad(key, 4, [7, 9, 11], [1, 3, 5], n)),
            Travel(n => Triad(key, 3, [0, 2, 4], [5, 3, 1], n), n => Triad(key, 3, [-7, -5, -3], [5, 3, 1], n)));
        return Lay(key, Four, r, l);
    }

    /// <summary>One finger from the tonic to a third, a fifth, an octave and a tenth away, home between each.</summary>
    public static Score JumpLadder(Key key, HandUse hands)
    {
        List<Stroke> Hand(int octave, int direction) =>
            [.. new[] { 2, 4, 7, 9 }.SelectMany(d => new[] { Note(key.Degree(0, octave), 3, Q), Rest(Q), Note(key.Degree(direction * d, octave), 3, Q), Rest(Q) }), Note(key.Degree(0, octave), 3, W)];
        // The left hand goes down a tenth from its home: from near middle C where the key allows, so it stays near its staff.
        var (r, l) = Chosen(hands, Hand(4, 1), Hand(key.Degree(0, 4).Midi <= 64 ? 4 : 3, -1));
        return Lay(key, Four, r, l);
    }

    /// <summary>
    /// A waltz's left hand, in three: a low bass note, then its chord twice. The tonic's bass is an octave under its chord; the
    /// dominant's sits just under its chord, which stays close to the tonic's, so the hand jumps two different distances.
    /// </summary>
    public static Score Waltz(Key key, HandUse hands)
    {
        // The chords sit on the bass staff: an octave lower for the keys whose tonic is high on it.
        var octave = key.Degree(0, 3).Midi > 55 ? 2 : 3;
        (int Bass, int[] Chord)[] bars = [(-7, [0, 2, 4]), (-7, [0, 2, 4]), (-3, [-1, 1, 4]), (-3, [-1, 1, 4]), (-7, [0, 2, 4])];
        List<Stroke> left =
        [
            .. bars.SelectMany(b => new[] { Note(InChord(key, b.Bass, octave), 5, Q), Triad(key, octave, b.Chord, [5, 3, 1], Q), Triad(key, octave, b.Chord, [5, 3, 1], Q) }),
            Note(key.Degree(-7, octave), 5, H + Q),
        ];
        return Lay(key, Three, [], left);
    }

    // ——— Thumb under ———

    /// <summary>
    /// The thumb passing under <paramref name="fingers"/> fingers (three or four) and back, round and round in eighths: the right
    /// hand up from the tonic, the left down from it.
    /// </summary>
    public static Score ThumbUnder(Key key, HandUse hands, int fingers)
    {
        var outward = Enumerable.Range(0, fingers + 1).ToList();
        var steps = outward.Concat(outward.Skip(1).Take(fingers - 1).Reverse()).ToList();
        var used = steps.Select(d => d == fingers ? 1 : d + 1).ToArray();
        var meter = fingers == 3 ? Three : Four;
        List<Stroke> Hand(int octave, int direction) =>
            [.. Enumerable.Range(0, 3).SelectMany(_ => steps.Select((d, i) => Note(key.Degree(direction * d, octave), used[i], E))), Note(key.Degree(0, octave), 1, meter.BarTicks)];
        var (r, l) = Chosen(hands, Hand(4, 1), Hand(3, -1));
        return Lay(key, meter, r, l);
    }

    // ——— Hands doing different things ———

    /// <summary>One hand holds the tonic while the other plays the pattern, then the other way round.</summary>
    public static Score HoldAndMove(Key key, HandUse hands)
    {
        List<Stroke> right = [.. Line(key, 4, UpAndBack, RightFive, Q), Note(key.Degree(0, 4), 1, W), Note(key.Degree(0, 4), 1, W), Note(key.Degree(0, 4), 1, W)];
        List<Stroke> left = [Note(key.Degree(0, 3), 5, W), Note(key.Degree(0, 3), 5, W), .. Line(key, 3, UpAndBack, LeftFive, Q), Note(key.Degree(0, 3), 5, W)];
        return Lay(key, Four, right, left);
    }

    /// <summary>One hand plays the pattern in eighths while the other plays it in quarters, then the other way round.</summary>
    public static Score TwoToOne(Key key, HandUse hands)
    {
        List<Stroke> right = [.. Line(key, 4, UpAndBack, RightFive, E), .. Line(key, 4, UpAndBack, RightFive, E), .. Line(key, 4, UpAndBack, RightFive, Q), Note(key.Degree(0, 4), 1, W)];
        List<Stroke> left = [.. Line(key, 3, UpAndBack, LeftFive, Q), .. Line(key, 3, UpAndBack, LeftFive, E), .. Line(key, 3, UpAndBack, LeftFive, E), Note(key.Degree(0, 3), 5, W)];
        return Lay(key, Four, right, left);
    }

    /// <summary>The pattern smooth in the right hand and detached in the left, then the other way round.</summary>
    public static Score SmoothDetached(Key key, HandUse hands)
    {
        List<Stroke> right = [.. Line(key, 4, UpAndBack, RightFive, Q), .. Line(key, 4, UpAndBack, RightFive, Q, touch: Articulation.Staccato), Note(key.Degree(0, 4), 1, W)];
        List<Stroke> left = [.. Line(key, 3, UpAndBack, LeftFive, Q, touch: Articulation.Staccato), .. Line(key, 3, UpAndBack, LeftFive, Q), Note(key.Degree(0, 3), 5, W)];
        return Lay(key, Four, right, left, [new Mark(MarkKind.Slur, "r0", "r7"), new Mark(MarkKind.Slur, "l8", "l15")]);
    }

    /// <summary>Three in the right hand against two in the left: the chord's notes as triplets over tonic and fifth in eighths.</summary>
    public static Score TwoAgainstThree(Key key, HandUse hands)
    {
        List<Stroke> right = [];
        for (var beat = 0; beat < 8; beat++)
        {
            var triplet = new Tuplet(3, 2);
            // The fingers are printed on the first beat only: after it they repeat, and a 3 for a finger beside each triplet's 3 reads badly.
            right.AddRange(new[] { (0, 1), (2, 3), (4, 5) }.Select(n => Note(key.Degree(n.Item1, 4), beat == 0 ? n.Item2 : null, E * 2 / 3) with { Tuplet = triplet }));
        }
        right.Add(Note(key.Degree(0, 4), 1, W));
        List<Stroke> left = [.. Enumerable.Range(0, 16).Select(i => Note(key.Degree(i % 2 == 0 ? 0 : 4, 3), i > 1 ? null : i == 0 ? 5 : 1, E)), Note(key.Degree(0, 3), 5, W)];
        return Lay(key, Four, right, left);
    }

    // ——— Touch, loud and soft ———

    /// <summary>Two-note slurs up the pattern and back: each pair joined, its second note lighter and let go.</summary>
    public static Score TwoNoteSlurs(Key key, HandUse hands)
    {
        (int From, int To)[] pairs = [(0, 1), (1, 2), (2, 3), (3, 4), (4, 3), (3, 2), (2, 1), (1, 0)];
        List<Stroke> Hand(int octave, bool rightHand) =>
        [
            .. pairs.SelectMany(p => new[] { Note(key.Degree(p.From, octave), rightHand ? p.From + 1 : 5 - p.From, Q), Note(key.Degree(p.To, octave), rightHand ? p.To + 1 : 5 - p.To, Q, Articulation.Staccato) }),
            Note(key.Degree(0, octave), rightHand ? 1 : 5, W),
        ];
        var (r, l) = Chosen(hands, Hand(4, true), Hand(3, false));
        var slurs = new[] { "r", "l" }.Where(p => p == "r" ? r.Count > 0 : l.Count > 0).SelectMany(p => Enumerable.Range(0, 8).Select(i => new Mark(MarkKind.Slur, $"{p}{2 * i}", $"{p}{2 * i + 1}")));
        return Lay(key, Four, r, l, [.. slurs]);
    }

    /// <summary>One note, a bar at each loudness: soft, medium, loud, medium, and soft again, held.</summary>
    public static Score SoftToLoud(Key key, HandUse hands)
    {
        var (r, l) = Chosen(hands,
            [.. Enumerable.Range(0, 16).Select(_ => Note(key.Degree(2, 4), 3, Q)), Note(key.Degree(2, 4), 3, W)],
            [.. Enumerable.Range(0, 16).Select(_ => Note(key.Degree(2, 3), 3, Q)), Note(key.Degree(2, 3), 3, W)]);
        var prefix = r.Count > 0 ? "r" : "l";
        return Lay(key, Four, r, l, [.. new[] { "p", "mf", "f", "mf", "p" }.Select((level, bar) => new Mark(MarkKind.Dynamic, $"{prefix}{4 * bar}", Text: level))]);
    }

    /// <summary>The pattern in eighths, growing to its top note and fading back, twice.</summary>
    public static Score Swell(Key key, HandUse hands)
    {
        var (r, l) = Chosen(hands, Line(key, 4, [.. UpAndBack, .. UpAndBack, 0], [.. RightFive, .. RightFive, 1], E, W), Line(key, 3, [.. UpAndBack, .. UpAndBack, 0], [.. LeftFive, .. LeftFive, 5], E, W));
        var prefix = r.Count > 0 ? "r" : "l";
        List<Mark> marks = [new(MarkKind.Dynamic, $"{prefix}0", Text: "p")];
        foreach (var start in new[] { 0, 8 })
        {
            marks.Add(new Mark(MarkKind.Crescendo, $"{prefix}{start}", $"{prefix}{start + 4}"));
            marks.Add(new Mark(MarkKind.Diminuendo, $"{prefix}{start + 4}", $"{prefix}{start + 8}"));
        }
        return Lay(key, Four, r, l, marks);
    }

    // ——— Pedal ———

    /// <summary>Four chords a bar each, the pedal changed on every one: up as the new chord sounds, down again while it's held.</summary>
    public static Score PedalChanges(Key key, HandUse hands)
    {
        (int[] Degrees, int[] Right, int[] Left, int Root)[] chords = [Tonic, SubdominantNear, DominantNear, Tonic];
        var prefix = hands == HandUse.Left ? "l" : "r";
        List<Mark> marks = [new(MarkKind.PedalDown, $"{prefix}0")];
        for (var i = 1; i < chords.Length; i++)
        {
            marks.Add(new Mark(MarkKind.PedalUp, $"{prefix}{i}"));
            marks.Add(new Mark(MarkKind.PedalDown, $"{prefix}{i}"));
        }
        marks.Add(new Mark(MarkKind.PedalUp, $"{prefix}{chords.Length - 1}"));
        return hands == HandUse.Left
            ? Lay(key, Four, [], [.. chords.Select(c => Triad(key, 3, c.Degrees, c.Left, W))], marks)
            : Lay(key, Four, [.. chords.Select(c => Triad(key, 4, c.Degrees, c.Right, W))], [], marks);
    }
}
