using Prima.Core.Music;

namespace Prima.Core.Generation;

/// <summary>The interval a double note is: two keys at once in one hand.</summary>
public enum DoubleNote
{
    Third,
    Fourth,
    Fifth,
    Sixth,
    Octave,
}

/// <summary>How loud a family's music is marked: not at all, soft, loud, or growing and fading.</summary>
public enum Shading
{
    Plain,
    Soft,
    Loud,
    Swell,
}

/// <summary>
/// The music of the families (<see cref="ExerciseFamilies"/>): a main exercise that a few choices vary, written for a key, a choice
/// of hands and those choices. Like <see cref="ExerciseMusic"/> each is what the right hand and the left hand do, a stroke at a
/// time, laid out in bars with its fingering, and a stroke's id is its hand's letter and its place in the line ("r0", "l7", a
/// chord's keys "r3a"). A hand here can have a second line on its staff, for a key held under moving ones ("rh0", "lh2"); a line
/// that travels far changes clef and goes under an octave line where it would run off its staff.
/// </summary>
public static class FamilyMusic
{
    private const int W = Ticks.Whole, H = Ticks.Half, Q = Ticks.Quarter, E = Ticks.Eighth, S = Ticks.Sixteenth;

    private static readonly Meter Four = new(4, 4);
    private static readonly Meter Three = new(3, 4);

    // A rest that can end a bar: its length, and the beat it has to start on.
    private static readonly (int Length, int Grid)[] RestLengths = [(W, W), (H + Q, H + Q), (H, H), (Q, Q), (E, E), (S, S)];

    /// <summary>A key in a stroke: its pitch, its finger, and whether it's tied on to the same key in the next stroke.</summary>
    private readonly record struct Struck(Spelled Pitch, int? Finger, bool Tie = false);

    /// <summary>One thing a line plays: keys, low to high (none of them: a rest), for a length in ticks.</summary>
    private sealed record Stroke(IReadOnlyList<Struck> Keys, int Length)
    {
        public Articulation Touch { get; init; }

        public Tuplet? Tuplet { get; init; }

        /// <summary>A rest that takes its time unprinted.</summary>
        public bool Hidden { get; init; }
    }

    private static Stroke Note(Spelled pitch, int? finger, int length, Articulation touch = Articulation.None) => new([new Struck(pitch, finger)], length) { Touch = touch };

    private static Stroke Both(Spelled low, Spelled high, int? lowFinger, int? highFinger, int length, Articulation touch = Articulation.None) =>
        new([new Struck(low, lowFinger), new Struck(high, highFinger)], length) { Touch = touch };

    private static Stroke Rest(int length, bool hidden = false) => new([], length) { Hidden = hidden };

    // The hands chosen: the other's lines are left out.
    private static (IReadOnlyList<IReadOnlyList<Stroke>> Right, IReadOnlyList<IReadOnlyList<Stroke>> Left) Chosen(HandUse hands, IReadOnlyList<IReadOnlyList<Stroke>> right, IReadOnlyList<IReadOnlyList<Stroke>> left) =>
        (hands == HandUse.Left ? [] : right, hands == HandUse.Right ? [] : left);

    private static (IReadOnlyList<IReadOnlyList<Stroke>> Right, IReadOnlyList<IReadOnlyList<Stroke>> Left) Chosen(HandUse hands, IReadOnlyList<Stroke> right, IReadOnlyList<Stroke> left) =>
        Chosen(hands, [right], (IReadOnlyList<IReadOnlyList<Stroke>>)[left]);

    // The octave the left hand's tonic is in: the one below middle C's, or lower for the keys whose tonic is high in it, so a
    // line stays on the bass staff; one that climbs an octave starts lower sooner.
    private static int LeftOctave(Key key, bool climbs = false) => key.Degree(0, 3).Midi > (climbs ? 50 : 55) ? 2 : 3;

    /// <summary>
    /// Lays a hand's lines out in bars: each stroke in turn, none over a bar line, the rest of a line's last bar as rests. A hand
    /// with no line takes its bars' time unprinted; a second line is a second layer on the staff, and what it doesn't fill is
    /// unprinted too.
    /// </summary>
    private static Score Lay(Key key, Meter meter, IReadOnlyList<IReadOnlyList<Stroke>> right, IReadOnlyList<IReadOnlyList<Stroke>> left, IReadOnlyList<Mark>? marks = null)
    {
        var barTicks = meter.BarTicks;
        var hands = new[] { (Staff: 1, Prefix: "r", Lines: right), (Staff: 2, Prefix: "l", Lines: left) };
        var barCount = hands.SelectMany(h => h.Lines).Max(l => (l.Sum(s => s.Length) + barTicks - 1) / barTicks);
        var voices = Enumerable.Range(0, barCount).Select(_ => new List<Voice>()).ToList();
        foreach (var (staff, prefix, lines) in hands)
        {
            for (var layer = 0; layer < Math.Max(1, lines.Count); layer++)
            {
                IReadOnlyList<Stroke> strokes = layer < lines.Count ? lines[layer] : [];
                var line = layer == 0 ? prefix : prefix + "h";
                // A hand's one line is kept on its staff by clefs and octave lines; two lines stay where they are.
                var staffing = lines.Count == 1 ? Staffing(staff, strokes) : null;
                var events = Enumerable.Range(0, barCount).Select(_ => new List<Event>()).ToList();
                var (at, clef) = (0, staff == 1 ? Clef.Treble : Clef.Bass);
                for (var i = 0; i < strokes.Count; i++)
                {
                    var stroke = strokes[i];
                    var (bar, start) = (at / barTicks, at % barTicks);
                    if (start + stroke.Length > barTicks) throw new InvalidOperationException($"Stroke {line}{i} runs over its bar line.");

                    var id = $"{line}{i}";
                    at += stroke.Length;
                    if (stroke.Keys.Count == 0)
                    {
                        events[bar].Add(new Rest(id, start, stroke.Length) { Hidden = stroke.Hidden, Tuplet = stroke.Tuplet });
                        continue;
                    }

                    var (wanted, shift) = staffing is null ? (clef, 0) : staffing[i];
                    if (wanted != clef) events[bar].Add(new ClefChange(start, clef = wanted));
                    var tiedIn = i > 0 ? strokes[i - 1].Keys.Where(k => k.Tie).Select(k => k.Pitch.Midi).ToHashSet() : [];
                    var tones = stroke.Keys.Select((k, n) => new Tone(stroke.Keys.Count == 1 ? id : $"{id}{(char)('a' + n)}", k.Pitch) { Finger = k.Finger, TieToNext = k.Tie, TiedFromPrevious = tiedIn.Contains(k.Pitch.Midi), Ottava = shift }).ToList();
                    events[bar].Add(new Notes(id, start, stroke.Length, tones) { Articulation = stroke.Touch, Tuplet = stroke.Tuplet });
                }

                for (var bar = 0; bar < barCount; bar++)
                {
                    var last = events[bar].LastOrDefault(e => e is not ClefChange);
                    var filled = last is null ? 0 : last.Start + last.Length;
                    for (var n = 0; filled < barTicks; n++)
                    {
                        var length = RestLengths.First(r => r.Length <= barTicks - filled && filled % r.Grid == 0).Length;
                        events[bar].Add(new Rest($"{line}b{bar}x{n}", filled, length) { Hidden = strokes.Count == 0 || layer > 0 });
                        filled += length;
                    }
                    voices[bar].Add(new Voice(staff, events[bar], layer + 1));
                }
            }
        }
        return new Score(key, meter, [.. voices.Select((v, b) => new Bar(b + 1, v))]) { Marks = marks ?? [] };
    }

    /// <summary>
    /// The clef each stroke of a travelling line is written in and the octave line it's under: the bass clef below the A under
    /// middle C and the treble above the E over it, whichever staff it's on, and between them the clef it's already in; and a
    /// stretch that goes well past the staff (to the G two and a half octaves above middle C, or to the G at the bottom of the
    /// piano's second octave) printed an octave nearer, from the C before it to the C after.
    /// </summary>
    private static List<(Clef Clef, int Shift)> Staffing(int staff, IReadOnlyList<Stroke> strokes)
    {
        var placed = new List<(Clef Clef, int Shift)>();
        var clef = staff == 1 ? Clef.Treble : Clef.Bass;
        foreach (var stroke in strokes)
        {
            if (stroke.Keys.Count > 0)
            {
                // The clef that takes it within two ledger lines; between the two, the one it's in.
                var (lowest, highest) = (stroke.Keys[0].Pitch.Midi, stroke.Keys[^1].Pitch.Midi);
                clef = highest <= 64 && lowest < 57 ? Clef.Bass : lowest >= 57 && highest > 64 ? Clef.Treble : clef;
            }
            placed.Add((clef, 0));
        }

        // An octave line over a run of high notes in the treble, and under a run of low ones in the bass.
        OctaveLine(Clef.Treble, 1, s => s.Keys[^1].Pitch.Midi >= 84, run => run.Max(s => s.Keys[^1].Pitch.Midi) >= 91);
        OctaveLine(Clef.Bass, -1, s => s.Keys[0].Pitch.Midi <= 36, run => run.Min(s => s.Keys[0].Pitch.Midi) <= 31);
        return placed;

        void OctaveLine(Clef inClef, int shift, Func<Stroke, bool> far, Func<List<Stroke>, bool> farEnough)
        {
            for (var i = 0; i < strokes.Count; i++)
            {
                if (strokes[i].Keys.Count == 0 || placed[i].Clef != inClef || !far(strokes[i])) continue;

                var end = i;
                while (end + 1 < strokes.Count && (strokes[end + 1].Keys.Count == 0 || placed[end + 1].Clef == inClef && far(strokes[end + 1]))) end++;
                while (strokes[end].Keys.Count == 0) end--;
                if (farEnough([.. strokes.Skip(i).Take(end - i + 1).Where(s => s.Keys.Count > 0)]))
                {
                    for (var k = i; k <= end; k++) placed[k] = (inClef, shift);
                }
                i = end;
            }
        }
    }

    // ——— Scales ———

    /// <summary>
    /// A scale that parts in the middle, in eighths: both hands up together for <paramref name="octaves"/> (one or two), apart for as
    /// many (the right on up, the left back down) and in again, on up together, back to the middle, apart and in again, and home.
    /// A minor key's is the harmonic minor. Each hand keeps the scale's own fingering. Detached if <paramref name="touch"/> says so,
    /// and marked soft, loud, or growing as the hands go out or up and fading as they come back.
    /// </summary>
    public static Score PartingScale(Key key, HandUse hands, int octaves = 1, Articulation touch = Articulation.None, Shading loudness = Shading.Plain)
    {
        var (rightUp, leftUp) = ScaleBook(key);
        var n = 7 * octaves;
        // One octave to a leg, the hands are two octaves apart, which keeps each on its staff; two to a leg, an octave apart, lower
        // for the keys that would otherwise run off the top of the piano.
        var leftOctave = octaves == 1 ? 2 : key.Degree(0, 4).Midi <= 64 ? 2 : 1;
        var rightOctave = octaves == 1 ? 4 : leftOctave + 1;
        var right = Path([0, n, 2 * n, n, 2 * n, n, 2 * n, n, 0]);
        var left = Path([0, n, 0, n, 2 * n, n, 0, n, 0]);

        Stroke Played(IReadOnlyList<int> path, int i, (Spelled Pitch, int Finger)[] up, int octave, bool rightHand)
        {
            var (degree, last) = (path[i], i == path.Count - 1);
            var inScale = degree % 7;
            // A tonic takes the finger a scale ends on only where the line turns there: the right hand's at a top, the left's at a bottom.
            var turns = rightHand ? i > 0 && !last && path[i - 1] < degree && path[i + 1] < degree : (i == 0 || path[i - 1] > degree) && (last || path[i + 1] > degree);
            var finger = inScale != 0 ? up[inScale].Finger : rightHand ? up[turns ? 7 : 0].Finger : up[turns ? 0 : 7].Finger;
            var pitch = up[inScale].Pitch with { Octave = up[inScale].Pitch.Octave + octave - 4 + degree / 7 };
            return Note(pitch, finger, last ? W : E, last ? Articulation.None : touch);
        }

        List<Stroke> rightLine = [.. right.Select((_, i) => Played(right, i, rightUp, rightOctave, true))];
        List<Stroke> leftLine = [.. left.Select((_, i) => Played(left, i, leftUp, leftOctave, false))];
        List<Mark> marks = loudness switch
        {
            Shading.Soft => [new(MarkKind.Dynamic, "r0", Text: "p")],
            Shading.Loud => [new(MarkKind.Dynamic, "r0", Text: "f")],
            Shading.Swell =>
            [
                new(MarkKind.Dynamic, "r0", Text: "p"),
                new(MarkKind.Crescendo, "r0", $"r{2 * n}"), new(MarkKind.Diminuendo, $"r{2 * n}", $"r{3 * n}"),
                new(MarkKind.Crescendo, $"r{3 * n}", $"r{4 * n}"), new(MarkKind.Diminuendo, $"r{4 * n}", $"r{5 * n}"),
                new(MarkKind.Crescendo, $"r{5 * n}", $"r{6 * n}"), new(MarkKind.Diminuendo, $"r{6 * n}", $"r{8 * n}"),
            ],
            _ => [],
        };
        return Lay(key, Four, [rightLine], [leftLine], marks);

        // The degrees a hand goes through, from turning point to turning point, each one once.
        static List<int> Path(int[] turns)
        {
            List<int> path = [turns[0]];
            for (var leg = 1; leg < turns.Length; leg++)
            {
                var step = Math.Sign(turns[leg] - turns[leg - 1]);
                for (var d = turns[leg - 1] + step; d != turns[leg] + step; d += step) path.Add(d);
            }
            return path;
        }
    }

    // A scale's eight notes from its tonic up, in the octave above middle C's for both hands, with the scale book's fingers: the
    // right hand's, then the left's. A minor key's is the harmonic minor.
    private static ((Spelled Pitch, int Finger)[] Right, (Spelled Pitch, int Finger)[] Left) ScaleBook(Key key)
    {
        var score = Technique.Scale(key, key.Minor ? ScaleKind.HarmonicMinor : ScaleKind.Major, 1, HandUse.Together).Score;
        (Spelled, int)[] Up(int staff) => [.. Written(score, staff).Take(8).Select(t => (t.Pitch with { Octave = t.Pitch.Octave + (staff == 1 ? 0 : 1) }, t.Finger!.Value))];
        return (Up(1), Up(2));
    }

    private static IEnumerable<Tone> Written(Score score, int staff) => score.Bars.SelectMany(b => b.Voices.Where(v => v.Staff == staff).SelectMany(v => v.Events.OfType<Notes>().Select(n => n.Tones[0])));

    // ——— Broken chords ———

    /// <summary>
    /// An arpeggio over <paramref name="octaves"/> with a lean on the first of every <paramref name="group"/> notes: in fours
    /// (sixteenths, in three) the lean falls on a different note of the chord each time; in threes (triplets, in four) always on
    /// the same one. Up and back twice, and the tonic held. The scale book's notes and fingers.
    /// </summary>
    public static Score GroupedArpeggio(Key key, HandUse hands, int group = 4, int octaves = 2)
    {
        var book = Technique.Arpeggio(key, octaves, HandUse.Together).Score;
        var meter = group == 4 ? Three : Four;
        var triplet = new Tuplet(3, 2);
        // Over two octaves the hands are where the scale book has them, two octaves apart. Over four they're an octave apart, the
        // left from the piano's second octave or, for the keys from F up, its first, so neither end runs far off its staff.
        var low = key.Degree(0, 4).Midi <= 64 ? 2 : 1;
        List<Stroke> Hand(int staff)
        {
            var notes = Written(book, staff).ToList();
            if (octaves == 4)
            {
                var shift = (staff == 1 ? low + 1 : low) - notes[0].Pitch.Octave;
                notes = [.. notes.Select(t => t with { Pitch = t.Pitch with { Octave = t.Pitch.Octave + shift } })];
            }
            var trip = notes.Take(notes.Count - 1).ToList();
            List<Stroke> line = [];
            for (var i = 0; i < 2 * trip.Count; i++)
            {
                var tone = trip[i % trip.Count];
                // A triplet's three notes share one bracket.
                if (group == 3 && i % 3 == 0) triplet = new Tuplet(3, 2);
                line.Add(Note(tone.Pitch, tone.Finger, group == 4 ? S : E * 2 / 3, i % group == 0 ? Articulation.Accent : Articulation.None) with { Tuplet = group == 3 ? triplet : null });
            }
            line.Add(Note(notes[^1].Pitch, notes[^1].Finger, meter.BarTicks));
            return line;
        }
        var (r, l) = Chosen(hands, Hand(1), Hand(2));
        return Lay(key, meter, r, l);
    }

    // ——— Thumb under ———

    /// <summary>
    /// The thumb passing under <paramref name="fingers"/> fingers (two to five) and back, round and round in eighths, a bar to each
    /// time round: the right hand up from the tonic, the left down from it. Under three and under four it's
    /// <see cref="ExerciseMusic.ThumbUnder"/>'s music.
    /// </summary>
    public static Score ThumbUnder(Key key, HandUse hands, int fingers)
    {
        var outward = Enumerable.Range(0, fingers + 1).ToList();
        var steps = outward.Concat(outward.Skip(1).Take(fingers - 1).Reverse()).ToList();
        var meter = new Meter(fingers, 4);
        // The short pass goes round four times, to be as long as the others.
        var rounds = fingers == 2 ? 4 : 3;
        List<Stroke> Hand(int octave, int direction) =>
            [.. Enumerable.Range(0, rounds).SelectMany(_ => steps.Select(d => Note(key.Degree(direction * d, octave), d == fingers ? 1 : d + 1, E))), Note(key.Degree(0, octave), 1, Math.Min(meter.BarTicks, W))];
        var (r, l) = Chosen(hands, Hand(4, 1), Hand(3, -1));
        return Lay(key, meter, r, l);
    }

    // ——— Double notes ———

    /// <summary>
    /// Thirds joined, in eighths: three neighbouring thirds up and back (fingers 1 and 3, 2 and 4, 3 and 5, 2 and 4), twice to a
    /// bar. Staying in one place it's two bars and the first third held; climbing, a bar on each step of the scale and the third
    /// an octave up held. Together the left hand plays the same thirds an octave or two lower, or, in mirror, its own from the top
    /// down, so the same fingers play together.
    /// </summary>
    public static Score Thirds(Key key, HandUse hands, bool mirror = false, bool climb = false)
    {
        var places = climb ? Enumerable.Range(0, 7).ToList() : [0, 0];
        var rest = climb ? 7 : 0;
        List<Stroke> Hand(int octave, bool rightHand)
        {
            // The third on a step of the hand's five notes, with the fingers that sit there.
            Stroke Third(int place, int step, int length) =>
                Both(key.Degree(place + step, octave), key.Degree(place + step + 2, octave), rightHand ? step + 1 : 5 - step, rightHand ? step + 3 : 3 - step, length);
            int[] order = rightHand || !mirror ? [0, 1, 2, 1] : [2, 1, 0, 1];
            return [.. places.SelectMany(p => Enumerable.Range(0, 8).Select(i => Third(p, order[i % 4], E))), Third(rest, order[0], W)];
        }
        var (r, l) = Chosen(hands, Hand(4, true), Hand(LeftOctave(key, climb), false));
        // A slur over each bar: its last third is let go, to move on.
        var slurs = new[] { (Prefix: "r", Lines: r), (Prefix: "l", Lines: l) }.Where(h => h.Lines.Count > 0).SelectMany(h => places.Select((_, bar) => new Mark(MarkKind.Slur, $"{h.Prefix}{8 * bar}", $"{h.Prefix}{8 * bar + 7}")));
        return Lay(key, Four, r, l, [.. slurs]);
    }

    // Where a double note sits on the scale, and the fingers that take it: its lower key's degree to start from, how many degrees
    // up its upper key is, the right hand's fingers low and high, and the left's.
    private static (int From, int Span, (int Low, int High) Right, (int Low, int High) Left) Shape(DoubleNote interval) => interval switch
    {
        DoubleNote.Third => (0, 2, (2, 4), (4, 2)),
        // From the fifth below the tonic, so none of the five is the scale's one augmented fourth.
        DoubleNote.Fourth => (-3, 3, (1, 4), (4, 1)),
        DoubleNote.Fifth => (0, 4, (1, 5), (5, 1)),
        DoubleNote.Sixth => (2, 5, (1, 5), (5, 1)),
        _ => (0, 7, (1, 5), (5, 1)),
    };

    /// <summary>
    /// A double note repeated from the wrist, in three: <paramref name="quick"/> quick ones (one to four) in the first beat that
    /// bounce into a longer one on the second, and a beat's rest to move in. A step up the scale each bar for five bars, back
    /// down for four, and the first one held.
    /// </summary>
    public static Score Bounce(Key key, HandUse hands, DoubleNote interval = DoubleNote.Sixth, int quick = 2)
    {
        var (from, span, rightFingers, leftFingers) = Shape(interval);
        List<Stroke> Hand(int octave, (int Low, int High) fingers)
        {
            List<Stroke> line = [];
            foreach (var (place, bar) in new[] { 0, 1, 2, 3, 4, 3, 2, 1, 0 }.Select((p, i) => (p, i)))
            {
                // The fingers are printed once where the hand starts and once where it turns back.
                var shown = bar is 0 or 4;
                Stroke Struck(int length)
                {
                    var stroke = Both(key.Degree(from + place, octave), key.Degree(from + place + span, octave), shown ? fingers.Low : null, shown ? fingers.High : null, length);
                    shown = false;
                    return stroke;
                }
                var triplet = new Tuplet(3, 2);
                line.AddRange(quick switch
                {
                    1 => [Rest(E), Struck(E)],
                    2 => [Struck(E), Struck(E)],
                    3 => [Struck(E * 2 / 3) with { Tuplet = triplet }, Struck(E * 2 / 3) with { Tuplet = triplet }, Struck(E * 2 / 3) with { Tuplet = triplet }],
                    _ => new[] { Struck(S), Struck(S), Struck(S), Struck(S) },
                });
                line.Add(Struck(Q));
                line.Add(Rest(Q));
            }
            line.Add(Both(key.Degree(from, octave), key.Degree(from + span, octave), fingers.Low, fingers.High, H + Q));
            return line;
        }
        // An octave under the right hand's, or two where an octave apart the hands would want the same key.
        var leftOctave = interval == DoubleNote.Octave ? 2 : LeftOctave(key, climbs: true);
        var (r, l) = Chosen(hands, Hand(4, rightFingers), Hand(leftOctave, leftFingers));
        return Lay(key, Three, r, l);
    }

    /// <summary>
    /// A major scale in octaves, detached, in eighths: each hand an octave between its thumb and little finger, up
    /// <paramref name="octaves"/> (one or two) and back, the last one held to the end of its bar.
    /// </summary>
    public static Score OctaveScale(Key key, HandUse hands, int octaves = 1)
    {
        var steps = Enumerable.Range(0, 7 * octaves + 1).Concat(Enumerable.Range(0, 7 * octaves).Reverse()).ToList();
        var held = W - (steps.Count - 1) * E % W;
        List<Stroke> Hand(int octave, (int Low, int High) fingers) =>
            [.. steps.Select((d, i) => Both(key.Degree(d, octave), key.Degree(d + 7, octave), i == 0 ? fingers.Low : null, i == 0 ? fingers.High : null, i == steps.Count - 1 ? held : E, i == steps.Count - 1 ? Articulation.None : Articulation.Staccato))];
        // The right hand from the octave above middle C's, or for the keys from G up the one below, so its top stays near the staff;
        // the left two octaves under it.
        var rightOctave = key.Degree(0, 4).Midi > 66 ? 3 : 4;
        var (r, l) = Chosen(hands, Hand(rightOctave, (1, 5)), Hand(rightOctave - 2, (5, 1)));
        return Lay(key, Four, r, l);
    }

    // ——— Finger pairs up the scale ———

    // The fingers of a pair that play the lower and the upper of two neighbouring notes: the pair itself in a hand alone (the
    // left's turned round), and with both hands the left takes the opposite pair, 5 and 4 against the right's 1 and 2.
    private static (int Low, int High) Pair(int pair, bool rightHand, HandUse hands) => rightHand ? (pair, pair + 1) : hands == HandUse.Together ? (6 - pair, 5 - pair) : (pair + 1, pair);

    /// <summary>
    /// Trill bursts with a pair of fingers (<paramref name="pair"/> is its lower finger, 1 to 4): four sixteenths back and forth and
    /// a stop on the fifth note, a step higher each time for an octave, then from the upper note down again, and the tonic held.
    /// </summary>
    public static Score TrillBursts(Key key, HandUse hands, int pair = 2)
    {
        List<Stroke> Hand(int octave, bool rightHand)
        {
            var fingers = Pair(pair, rightHand, hands);
            Stroke At(int place, bool upper, int length) => Note(key.Degree(place + (upper ? 1 : 0), octave), upper ? fingers.High : fingers.Low, length);
            IEnumerable<Stroke> Burst(int place, bool fromUpper) => Enumerable.Range(0, 4).Select(i => At(place, fromUpper ^ (i % 2 == 1), S)).Append(At(place, fromUpper, Q));
            return [.. Enumerable.Range(0, 8).SelectMany(p => Burst(p, false)), .. Enumerable.Range(0, 8).Reverse().SelectMany(p => Burst(p, true)), At(0, false, W)];
        }
        var (r, l) = Chosen(hands, Hand(4, true), Hand(LeftOctave(key, climbs: true), false));
        return Lay(key, Four, r, l);
    }

    /// <summary>
    /// Two-note slurs with a pair of fingers (<paramref name="pair"/> is its lower finger, 1 to 4), in quarters: each pair joined, its
    /// second note lighter and let go, starting where the last one ended. Up an octave, then from the upper note down again, and
    /// the tonic held.
    /// </summary>
    public static Score SlurWalk(Key key, HandUse hands, int pair = 2)
    {
        var pairs = Enumerable.Range(0, 7).Select(p => (From: p, To: p + 1)).Concat(Enumerable.Range(0, 7).Reverse().Select(p => (From: p + 1, To: p))).ToList();
        List<Stroke> Hand(int octave, bool rightHand)
        {
            var fingers = Pair(pair, rightHand, hands);
            Stroke At(int degree, bool upper, Articulation touch) => Note(key.Degree(degree, octave), upper ? fingers.High : fingers.Low, Q, touch);
            return [.. pairs.SelectMany(p => new[] { At(p.From, p.From > p.To, Articulation.None), At(p.To, p.To > p.From, Articulation.Staccato) }), Note(key.Degree(0, octave), fingers.Low, W)];
        }
        var (r, l) = Chosen(hands, Hand(4, true), Hand(LeftOctave(key, climbs: true), false));
        var slurs = new[] { (Prefix: "r", Lines: r), (Prefix: "l", Lines: l) }.Where(h => h.Lines.Count > 0).SelectMany(h => pairs.Select((_, i) => new Mark(MarkKind.Slur, $"{h.Prefix}{2 * i}", $"{h.Prefix}{2 * i + 1}")));
        return Lay(key, Four, r, l, [.. slurs]);
    }

    // ——— A key held under moving ones ———

    // A hand's five notes from its tonic up, and the finger on each: the right hand's thumb on the tonic, the left's little finger.
    private static (Spelled Pitch, int Finger) Under(Key key, int octave, bool rightHand, int degree) => (key.Degree(degree, octave), rightHand ? degree + 1 : 5 - degree);

    // A bar of two lines: the upper and the lower. The held key goes in whichever it's on the side of.
    private static void HeldBar(List<Stroke> upper, List<Stroke> lower, bool heldBelow, Stroke held, IEnumerable<Stroke> moving)
    {
        (heldBelow ? lower : upper).Add(held);
        (heldBelow ? upper : lower).AddRange(moving);
    }

    /// <summary>
    /// A key held for a bar while its neighbour (or, with <paramref name="reach"/> 2, the key a finger further on) is played three
    /// times in quarters by turning the forearm: from the thumb's key up the hand's five notes, each key held in turn, then back
    /// down, and the tonic held.
    /// </summary>
    public static Score HoldAndTurn(Key key, HandUse hands, int reach = 1)
    {
        (List<Stroke> Upper, List<Stroke> Lower) Hand(int octave, bool rightHand)
        {
            var (upper, lower) = (new List<Stroke>(), new List<Stroke>());
            void Turn(int hold, int play)
            {
                var (held, struck) = (Under(key, octave, rightHand, hold), Under(key, octave, rightHand, play));
                HeldBar(upper, lower, hold < play, Note(held.Pitch, held.Finger, W), [Rest(Q), .. Enumerable.Range(0, 3).Select(i => Note(struck.Pitch, i == 0 ? struck.Finger : null, Q))]);
            }
            for (var d = 0; d + reach <= 4; d++) Turn(d, d + reach);
            for (var d = 4; d - reach >= 0; d--) Turn(d, d - reach);
            var home = Under(key, octave, rightHand, 0);
            upper.Add(Note(home.Pitch, home.Finger, W));
            return (upper, lower);
        }
        var (right, left) = (Hand(4, true), Hand(LeftOctave(key), false));
        var (r, l) = Chosen(hands, [right.Upper, right.Lower], [left.Upper, left.Lower]);
        return Lay(key, Four, r, l);
    }

    /// <summary>
    /// One finger holds its key for a bar while the other four play theirs up and back in eighths: <paramref name="holding"/> is the
    /// finger (1 to 5), played twice; or 0 for each in turn, from the hand's lowest note up. Then the tonic held.
    /// </summary>
    public static Score HoldOne(Key key, HandUse hands, int holding = 0)
    {
        (List<Stroke> Upper, List<Stroke> Lower) Hand(int octave, bool rightHand)
        {
            var (upper, lower) = (new List<Stroke>(), new List<Stroke>());
            // The finger's own note: the right hand's thumb is on the tonic, the left's on the fifth.
            var own = rightHand ? holding - 1 : 5 - holding;
            foreach (var hold in holding == 0 ? Enumerable.Range(0, 5) : new[] { own, own })
            {
                var free = Enumerable.Range(0, 5).Where(d => d != hold).ToList();
                var (held, moving) = (Under(key, octave, rightHand, hold), free.Concat(free.AsEnumerable().Reverse().Skip(1)).Select(d => Under(key, octave, rightHand, d)));
                HeldBar(upper, lower, hold <= 2, Note(held.Pitch, held.Finger, W), [.. moving.Select(m => Note(m.Pitch, m.Finger, E)), Rest(E)]);
            }
            var home = Under(key, octave, rightHand, 0);
            upper.Add(Note(home.Pitch, home.Finger, W));
            return (upper, lower);
        }
        var (right, left) = (Hand(4, true), Hand(LeftOctave(key), false));
        var (r, l) = Chosen(hands, [right.Upper, right.Lower], [left.Upper, left.Lower]);
        return Lay(key, Four, r, l);
    }
}
