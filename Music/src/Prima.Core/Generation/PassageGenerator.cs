using Prima.Core.Music;

namespace Prima.Core.Generation;

/// <summary>From level 5 a passage takes one of four templates for its left hand and metre, rotated (ours, for Zhukov's styles).</summary>
public enum Style
{
    Plain,
    Chorale,
    Dance,
    March,
    Song,
}

/// <summary>A generated passage: its score, the level it was written for, its seed (to write it again), and how it's described.</summary>
public sealed record Passage(Score Score, LevelSpec Spec, ulong Seed, Style Style, string Description);

/// <summary>
/// Writes new tonal music at a level (plan, The generator): a key and metre, a harmonic plan ending in a cadence, rhythm from the
/// level's figures with bar 1's rhythm coming back, a melody of chord tones on strong beats and steps between, inside the hand's
/// position, and the left hand by level: none, the melody passed between hands, phrases one after the other, then an
/// accompaniment from long notes to chords. It ends on the tonic, and stays inside A0–C8 and the level's ledger lines.
/// </summary>
public static class PassageGenerator
{
    private static readonly int[][] Opening = [[0, 3, 4, 4], [0, 5, 3, 4], [0, 3, 0, 4], [0, 4, 5, 4]];
    private static readonly int[][] Closing = [[0, 3, 4, 0], [0, 1, 4, 0], [5, 3, 4, 0], [0, 5, 4, 0]];
    private static readonly string[] Numbers = ["", "One", "Two", "Three", "Four", "Five", "Six", "Seven", "Eight", "Nine", "Ten", "Eleven", "Twelve"];

    public static Passage Generate(LevelSpec spec, ulong seed, Key? avoidKey = null)
    {
        var rng = new Rng(seed);
        var keys = spec.Keys.Count > 1 && avoidKey is { } avoid ? spec.Keys.Where(k => k != avoid).ToList() : spec.Keys;
        var key = rng.Pick(keys);
        var style = spec.Hands == Hands.Together ? rng.Pick([Style.Chorale, Style.Dance, Style.March, Style.Song]) : Style.Plain;
        var meter = style switch
        {
            Style.Dance when spec.Meters.Contains(new Meter(3, 4)) => new Meter(3, 4),
            Style.March when spec.Meters.Contains(new Meter(2, 4)) => new Meter(2, 4),
            _ => rng.Pick(spec.Meters),
        };
        var bars = spec.Bars.Min + rng.Int(spec.Bars.Max - spec.Bars.Min + 1);
        if (spec.Level == 3 && meter == new Meter(2, 4)) bars = 6;

        var writer = new Writer(spec, key, meter, rng, style);
        var score = writer.Write(bars);
        var hands = spec.Hands switch { Hands.One => "one hand", Hands.Split => "the melody shared between the hands", Hands.Separate => "hands separately", _ => "hands together" };
        var count = bars < Numbers.Length ? Numbers[bars] : bars.ToString(System.Globalization.CultureInfo.InvariantCulture);
        return new Passage(score, spec, seed, style, $"{count} bars in {key.Name}, {meter}, {hands}");
    }

    /// <summary>The number of ledger lines a written note needs on its staff.</summary>
    public static int LedgerLines(Spelled pitch, int staff)
    {
        var (bottom, top) = staff == 1 ? (30, 38) : (18, 26);
        return pitch.Diatonic < bottom ? (bottom - pitch.Diatonic) / 2 : pitch.Diatonic > top ? (pitch.Diatonic - top) / 2 : 0;
    }

    private sealed class Writer(LevelSpec spec, Key key, Meter meter, Rng rng, Style style)
    {
        private int nextId;

        // Melody notes as scale degrees from the tonic (0 = tonic in the hand's octave), with any chromatic raise.
        private sealed record MelodyNote(int Bar, int Start, int Length, int Degree, int Raise, Tuplet? Tuplet, bool Rest);

        public Score Write(int bars)
        {
            var harmony = Harmony(bars);
            var rhythms = Rhythms(bars);
            var hasPickup = spec.Pickups && rng.Chance(0.3) && !meter.Compound && meter.Unit == 4;
            var melody = Melody(rhythms, harmony, bars);

            var right = new List<Event>[bars];
            var left = new List<Event>[bars];
            for (var b = 0; b < bars; b++) (right[b], left[b]) = ([], []);

            // Which hand plays the melody in each bar.
            var rightHandMelody = new bool[bars];
            var leftOnly = spec.Hands == Hands.One && rng.Chance(0.3);
            for (var b = 0; b < bars; b++)
            {
                rightHandMelody[b] = spec.Hands switch
                {
                    Hands.One => !leftOnly,
                    Hands.Split => b % 2 == 0,
                    Hands.Separate => b < (bars + 1) / 2,
                    _ => true,
                };
            }

            foreach (var note in melody)
            {
                var onRight = rightHandMelody[note.Bar];
                var events = onRight ? right[note.Bar] : left[note.Bar];
                if (note.Rest) events.Add(new Rest(Id("r"), note.Start, note.Length) { Tuplet = note.Tuplet });
                else events.Add(Single(note.Start, note.Length, Pitch(note.Degree, onRight ? 1 : 2, note.Raise), note.Tuplet));
            }

            // Ties at the higher levels: a note held over the bar line into the next bar's first note, when it's the same pitch.
            if (spec.Ties) TieRepeatedNotes(right);

            if (spec.Hands == Hands.Together) Accompany(left, harmony, bars);

            var allBars = new List<Bar>();
            if (hasPickup)
            {
                // The dominant below the tonic, leading into bar 1.
                var pick = Single(0, meter.BeatTicks, Pitch(-3, 1, 0), null);
                allBars.Add(new Bar(0, [new Voice(1, [pick]), new Voice(2, [new Rest(Id("r"), 0, meter.BeatTicks) { Hidden = true }])]) { Length = meter.BeatTicks });
            }

            var marks = new List<Mark>();
            for (var b = 0; b < bars; b++)
            {
                var rightEvents = Fill(right[b]);
                var leftEvents = Fill(left[b]);
                if (spec.ClefChanges && b == bars / 2 && spec.Hands == Hands.Together && bars >= 8 && rng.Chance(0.5)) leftEvents = RaiseIntoTreble(leftEvents, b + 1 < bars ? left[b + 1] : null);
                allBars.Add(new Bar(b + 1, [new Voice(1, rightEvents), new Voice(2, leftEvents)]));
            }

            Mark(allBars, marks);
            return new Score(key, meter, allBars) { Marks = marks };
        }

        private string Id(string kind) => $"{kind}{++nextId}";

        private Notes Single(int start, int length, Spelled pitch, Tuplet? tuplet)
        {
            var id = Id("n");
            return new Notes(id, start, length, [new Tone(id, pitch)]) { Tuplet = tuplet };
        }

        // The written pitch of a degree in a hand: the right hand's tonic in octave 4, the left hand's in 3 (or 2 for F and up).
        private Spelled Pitch(int degree, int staff, int raise)
        {
            var tonicOctave = staff == 1 ? 4 : key.TonicStep >= Step.F ? 2 : 3;
            if (staff == 1 && key.TonicStep >= Step.A) tonicOctave = 4;
            var spelled = key.Degree(degree, tonicOctave);
            return spelled with { Alter = spelled.Alter + raise };
        }

        // One chord root (a scale degree) per bar: phrases of four bars, a half cadence before the last, an authentic one at the end.
        private int[] Harmony(int bars)
        {
            if (bars <= 2) return [.. Enumerable.Repeat(0, bars)];

            var plan = new List<int>();
            var phrases = (bars + 3) / 4;
            for (var p = 0; p < phrases; p++) plan.AddRange(rng.Pick(p == phrases - 1 ? Closing : Opening));
            return [.. plan.Skip(plan.Count - bars)];
        }

        // Each bar's rhythm: the level's figures, bar 1's rhythm returning in bar 3 (and 5), and a held note to end.
        private List<(int Length, bool IsRest, Tuplet? Tuplet)>[] Rhythms(int bars)
        {
            var cells = Ladder.CellsFor(spec, meter);
            var rhythms = new List<(int, bool, Tuplet?)>[bars];
            for (var b = 0; b < bars; b++)
            {
                if (b == bars - 1) rhythms[b] = Ending();
                else if (b >= 2 && b % 2 == 0 && b < bars - 1 && rng.Chance(0.6)) rhythms[b] = rhythms[b % 4 == 0 ? 0 : 2 * (b / 4)].ToList();
                else rhythms[b] = BarRhythm(cells, b == 0);
            }
            return rhythms;
        }

        private List<(int, bool, Tuplet?)> BarRhythm(IReadOnlyList<Cell> cells, bool first)
        {
            var result = new List<(int, bool, Tuplet?)>();
            var position = 0;
            var rests = 0;
            var beat = cells.Any(c => c.Compound) ? 720 : meter.Unit == 8 ? 240 : Ticks.Quarter;
            while (position < meter.BarTicks)
            {
                var remaining = meter.BarTicks - position;
                var fitting = cells.Where(c => c.Ticks <= remaining && Fits(c, position, beat) && (!c.Lengths.Any(l => l < 0) || (rests == 0 && !first && position > 0))).ToList();
                if (fitting.Count == 0) fitting = [new Cell("beat", [Math.Min(beat, remaining)], 1)];

                // More notes to read: figures weighted by notes per beat, held notes of three beats or more seldom, the level's new figures more often.
                var cell = rng.Weighted(fitting.Select(c => (c, (0.35 + Math.Min(1.0, c.Lengths.Count(l => l > 0) / Math.Max(1.0, c.Ticks / (double)beat))) * (c.Level == spec.Level ? 1.6 : 1.0) * (c.Ticks >= 3 * beat ? 0.35 : 1.0))).ToList());
                var tuplet = cell.Tuplet is null ? null : new Tuplet(cell.Tuplet.Actual, cell.Tuplet.Normal);
                foreach (var length in cell.Lengths) result.Add((Math.Abs(length), length < 0, tuplet));
                rests += cell.Lengths.Count(l => l < 0);
                position += cell.Ticks;
            }
            return result;
        }

        // A figure longer than a beat starts on the bar or half-bar; one within a beat starts on a beat.
        private bool Fits(Cell cell, int position, int beat)
        {
            if (cell.Ticks <= beat) return position % beat == 0 || cell.Ticks < beat && position % cell.Ticks == 0;

            var half = meter.BarTicks % 2 == 0 && meter.Beats % 2 == 0 ? meter.BarTicks / 2 : meter.BarTicks;
            return position % half == 0 || (cell.Ticks == 2 * beat && position % beat == 0 && meter.Beats > 2);
        }

        // The last bar: one held note if the bar's length can be written as one; otherwise held notes by beat group.
        private List<(int, bool, Tuplet?)> Ending()
        {
            if (Durations.IsWritable(meter.BarTicks)) return [(meter.BarTicks, false, null)];

            var lengths = new List<(int, bool, Tuplet?)>();
            var left = meter.BarTicks;
            foreach (var part in new[] { 1440, 960, 720, 480, 240 })
            {
                while (left >= part)
                {
                    lengths.Add((part, false, null));
                    left -= part;
                }
            }
            return lengths;
        }

        // The melody as degrees: chord tones on strong beats, steps between, leaps within the level's limit and turned back by step,
        // inside the hand's position; the penultimate note a step from the closing tonic.
        private List<MelodyNote> Melody(List<(int Length, bool IsRest, Tuplet? Tuplet)>[] rhythms, int[] harmony, int bars)
        {
            var (low, high) = Range();
            var notes = new List<MelodyNote>();
            var previous = Math.Clamp(rng.Pick(new[] { 0, 2, 4 }), low, high);
            var lastStep = 0;
            var totalNotes = rhythms.Sum(r => r.Count(n => !n.IsRest));
            var index = 0;
            var direction = previous <= (low + high) / 2 ? 1 : -1;
            for (var b = 0; b < bars; b++)
            {
                if (spec.Level == 1 && (previous + direction > high || previous + direction < low)) direction = -direction;
                var start = 0;
                foreach (var (length, rest, tuplet) in rhythms[b])
                {
                    if (rest)
                    {
                        notes.Add(new MelodyNote(b, start, length, 0, 0, tuplet, true));
                        start += length;
                        continue;
                    }

                    index++;
                    // Strong: the downbeat, the half-bar in even metres, and every beat in compound ones.
                    var strong = start == 0 || (meter.Compound ? start % 720 == 0 : meter.Beats % 2 == 0 && start == meter.BarTicks / 2);
                    var chord = new[] { harmony[b], harmony[b] + 2, harmony[b] + 4 }.Select(d => ((d % 7) + 7) % 7).ToHashSet();
                    int degree;
                    if (index == totalNotes) degree = Nearest(previous, d => ((d % 7) + 7) % 7 == 0, low, high);
                    else if (spec.Level == 1)
                    {
                        if (previous + direction > high || previous + direction < low) direction = -direction;
                        degree = previous + direction;
                    }
                    else
                    {
                        var penultimate = index == totalNotes - 1;
                        var finalTonic = Nearest(previous, d => ((d % 7) + 7) % 7 == 0, low, high);
                        var candidates = Enumerable.Range(low, high - low + 1).Where(d => Math.Abs(d - previous) <= spec.MaxLeap).Select(d =>
                        {
                            var move = d - previous;
                            var weight = Math.Abs(move) switch { 0 => 0.35, 1 => 3.0, 2 => 1.3, _ => 0.5 / Math.Abs(move) };
                            if (strong && chord.Contains(((d % 7) + 7) % 7)) weight *= 3;
                            if (Math.Abs(lastStep) >= 3) weight *= Math.Sign(move) == -Math.Sign(lastStep) && Math.Abs(move) == 1 ? 4 : 0.2;
                            if (penultimate) weight *= Math.Abs(d - finalTonic) == 1 ? 6 : 0.1;
                            return (d, weight);
                        }).ToList();
                        degree = rng.Weighted(candidates);
                    }

                    lastStep = degree - previous;
                    previous = degree;
                    notes.Add(new MelodyNote(b, start, length, degree, 0, tuplet, false));
                    start += length;
                }
            }

            return Inflect(notes);
        }

        // Minor keys raise the leading note before the closing tonic (from level 4); chromatic passing notes from level 7.
        private List<MelodyNote> Inflect(List<MelodyNote> notes)
        {
            var sounding = notes.Where(n => !n.Rest).ToList();
            for (var i = 0; i < sounding.Count - 1; i++)
            {
                var (note, next) = (sounding[i], sounding[i + 1]);
                var degreeInScale = ((note.Degree % 7) + 7) % 7;
                var raise = 0;
                if (key.Minor && spec.Level >= 4 && degreeInScale == 6 && ((next.Degree % 7) + 7) % 7 == 0 && next.Degree == note.Degree + 1) raise = 1;
                else if (spec.Chromatic && next.Degree == note.Degree + 1 && note.Start % meter.BeatTicks != 0 && Semitones(note.Degree, next.Degree) == 2 && rng.Chance(0.35)) raise = 1;
                if (raise != 0) notes[notes.IndexOf(note)] = note with { Raise = raise };
            }
            return notes;
        }

        private int Semitones(int from, int to) => key.Degree(to, 4).Midi - key.Degree(from, 4).Midi;

        private static int Nearest(int from, Func<int, bool> wanted, int low, int high) =>
            Enumerable.Range(low, high - low + 1).Where(wanted).DefaultIfEmpty(Math.Clamp(0, low, high)).MinBy(d => Math.Abs(d - from));

        // The melody's degrees: a five-finger position (on the tonic, or from level 4 on another degree), or wider from level 5,
        // kept within the level's ledger lines in both hands' octaves.
        private (int Low, int High) Range()
        {
            int low;
            if (spec.FixedPosition || spec.Span <= 5) low = spec.Level <= 3 ? 0 : rng.Pick(new[] { 0, 0, 3, 4, -3 });
            else low = -Math.Min(3, Math.Max(0, (spec.Span - 8) / 2));
            var high = low + Math.Max(4, spec.Span - 1);
            while (high > low + 4 && (LedgerLines(Pitch(high, 1, 0), 1) > spec.LedgerLines || Pitch(high, 1, 0).Midi > 100)) high--;
            while (low < high - 4 && LedgerLines(Pitch(low, 1, 0), 1) > spec.LedgerLines) low++;
            if (spec.Hands is Hands.Split or Hands.Separate or Hands.One)
            {
                while (low < high - 4 && LedgerLines(Pitch(low, 2, 0), 2) > spec.LedgerLines) low++;
                while (high > low + 4 && LedgerLines(Pitch(high, 2, 0), 2) > spec.LedgerLines) high--;
            }
            return (low, high);
        }

        private static void TieRepeatedNotes(List<Event>[] bars)
        {
            for (var b = 0; b + 1 < bars.Length; b++)
            {
                if (bars[b].LastOrDefault() is not Notes { Tones.Count: 1, Tuplet: null } last || bars[b + 1].FirstOrDefault() is not Notes { Tones.Count: 1, Tuplet: null } first) continue;
                if (last.Tones[0].Pitch != first.Tones[0].Pitch || last.Length > Ticks.Half) continue;

                bars[b][^1] = last with { Tones = [last.Tones[0] with { TieToNext = true }] };
                bars[b + 1][0] = first with { Tones = [first.Tones[0] with { TiedFromPrevious = true }] };
            }
        }

        // The left hand's part by texture and style, on each bar's chord root, ending on the tonic.
        private void Accompany(List<Event>[] left, int[] harmony, int bars)
        {
            for (var b = 0; b < bars; b++)
            {
                var root = harmony[b];
                var last = b == bars - 1;
                var events = left[b];
                events.Clear();
                var groups = BeatGroups();
                var texture = last ? Texture.LongNotes : spec.Texture;
                if (style == Style.Chorale && texture is Texture.LongNotes or Texture.BrokenChords or Texture.Patterns) texture = spec.Level >= 6 ? Texture.TwoNoteChords : Texture.LongNotes;
                if (style == Style.Dance && meter == new Meter(3, 4) && !last) texture = Texture.Patterns;
                switch (texture)
                {
                    case Texture.LongNotes:
                        AddHeld(events, groups, root, [0]);
                        if (last && spec.Level >= 6) events[^1] = Chord(events[^1].Start, events[^1].Length, root, [0, 4]);
                        break;
                    case Texture.TwoNoteChords:
                    case Texture.FourPart:
                        AddHeld(events, groups, root, [0, rng.Chance(0.5) ? 4 : 2]);
                        break;
                    case Texture.ThreeNoteChords:
                        AddHeld(events, groups, root, [0, 2, 4]);
                        break;
                    default:
                        Broken(events, root);
                        break;
                }
            }
        }

        // Beat groups for held left-hand notes: the whole bar if it's one writable note; else halves, else beat groups.
        private List<(int Start, int Length)> BeatGroups()
        {
            if (Durations.IsWritable(meter.BarTicks) && rng.Chance(0.6)) return [(0, meter.BarTicks)];
            if (meter.BarTicks % 2 == 0 && Durations.IsWritable(meter.BarTicks / 2) && meter.Beats % 2 == 0) return [(0, meter.BarTicks / 2), (meter.BarTicks / 2, meter.BarTicks / 2)];

            var groups = new List<(int, int)>();
            var at = 0;
            int[] parts = (meter.Count, meter.Unit) switch
            {
                (3, 8) => [720],
                (5, 8) => [480, 720],
                (7, 8) => [480, 480, 720],
                _ => [.. Enumerable.Repeat(meter.BeatTicks, meter.Beats)],
            };
            foreach (var part in parts)
            {
                groups.Add((at, part));
                at += part;
            }
            return groups;
        }

        private void AddHeld(List<Event> events, List<(int Start, int Length)> groups, int root, int[] chordTones)
        {
            for (var g = 0; g < groups.Count; g++)
            {
                var (start, length) = groups[g];
                var bass = g % 2 == 1 && chordTones.Length == 1 && groups.Count == 2 && rng.Chance(0.4) ? root + 4 : root;
                events.Add(chordTones.Length == 1 ? Single(start, length, BassPitch(bass), null) : Chord(start, length, root, chordTones));
            }
        }

        private Notes Chord(int start, int length, int root, int[] above)
        {
            var id = Id("c");
            var pitches = above.Select(a => BassPitch(root + a)).Distinct().ToList();
            return new Notes(id, start, length, pitches.Select((p, i) => new Tone($"{id}-{i}", p)).ToList());
        }

        // A degree in the bass, between about F2 and E3, so it sits on the bass staff.
        private Spelled BassPitch(int degree)
        {
            var normal = ((degree % 7) + 7) % 7;
            var pitch = key.Degree(normal, 2);
            while (pitch.Diatonic < 2 * 7 + 3) pitch = pitch with { Octave = pitch.Octave + 1 };
            while (pitch.Diatonic > 3 * 7 + 3) pitch = pitch with { Octave = pitch.Octave - 1 };
            return pitch;
        }

        // Broken chords by metre: quarters (root, fifth, third, fifth), the waltz's root then chords, march's root and fifth,
        // Alberti eighths at the higher levels, and compound metres in eighths.
        private void Broken(List<Event> events, int root)
        {
            if (meter.Unit == 8 && !meter.Compound)
            {
                AddHeld(events, BeatGroups(), root, [0]);
                return;
            }

            if (style == Style.Dance && meter == new Meter(3, 4))
            {
                events.Add(Single(0, 480, BassPitch(root), null));
                events.Add(Chord(480, 480, root, [2, 4]));
                events.Add(Chord(960, 480, root, [2, 4]));
                return;
            }

            int[] pattern = meter.Compound ? [0, 4, 2] : [0, 4, 2, 4];
            var step = meter.Compound || spec.Texture == Texture.Patterns ? 240 : 480;
            for (int at = 0, i = 0; at < meter.BarTicks; at += step, i++) events.Add(Single(at, step, BassPitch(root + pattern[i % pattern.Length]), null));
        }

        // Fills gaps in a bar's events with rests (hidden in the hand that isn't playing, where the bar is empty).
        private List<Event> Fill(List<Event> events)
        {
            if (events.Count == 0) return [new Rest(Id("r"), 0, meter.BarTicks)];

            var filled = new List<Event>();
            var at = 0;
            foreach (var e in events.OrderBy(e => e.Start))
            {
                if (e.Start > at) filled.AddRange(RestsFor(at, e.Start - at));
                filled.Add(e);
                at = e.Start + e.Length;
            }
            if (at < meter.BarTicks) filled.AddRange(RestsFor(at, meter.BarTicks - at));
            return filled;
        }

        private IEnumerable<Rest> RestsFor(int start, int length)
        {
            foreach (var part in new[] { 1920, 1440, 960, 720, 480, 360, 240, 120 })
            {
                while (length >= part && (start % part == 0 || part <= 480))
                {
                    yield return new Rest(Id("r"), start, part);
                    (start, length) = (start + part, length - part);
                }
            }
        }

        // A bar where the left hand moves up into the treble clef and back: a clef change at its start, the notes an octave up.
        private List<Event> RaiseIntoTreble(List<Event> events, List<Event>? next)
        {
            var raised = events.Select(e => e is Notes n ? n with { Tones = n.Tones.Select(t => t with { Pitch = t.Pitch with { Octave = t.Pitch.Octave + 1 } }).ToList() } : e).ToList();
            raised.Insert(0, new ClefChange(0, Clef.Treble));
            next?.Insert(0, new ClefChange(0, Clef.Bass));
            return raised;
        }

        // Printed marks by level (ABRSM's table): dynamics, slurs over the first phrase, staccato and accents, hairpins, a pause at
        // the end, and pedal under held bass notes. Not scored.
        private void Mark(List<Bar> bars, List<Mark> marks)
        {
            var melody = bars.SelectMany(b => b.Voices.Where(v => v.Staff == 1).SelectMany(v => v.Events.OfType<Notes>())).ToList();
            if (melody.Count == 0 || spec.Level < 3) return;

            string[] dynamics = spec.Level switch { < 4 => ["p", "f"], < 5 => ["p", "f", "mp", "mf"], < 8 => ["p", "f", "mp", "mf", "pp"], _ => ["p", "f", "mp", "mf", "pp", "ff"] };
            marks.Add(new Mark(MarkKind.Dynamic, melody[0].Tones[0].Id, Text: rng.Pick(dynamics)));
            if (spec.Level >= 4)
            {
                var phraseEnd = melody.LastOrDefault(n => bars.First(b => b.Voices.Any(v => v.Events.Contains(n))).Number <= 2);
                if (phraseEnd is not null && phraseEnd != melody[0]) marks.Add(new Mark(MarkKind.Slur, melody[0].Tones[0].Id, phraseEnd.Tones[0].Id));
                if (melody.Count > 6) marks.Add(new Mark(rng.Chance(0.5) ? MarkKind.Crescendo : MarkKind.Diminuendo, melody[^5].Tones[0].Id, melody[^3].Tones[0].Id));
            }
            if (spec.Level >= 7) marks.Add(new Mark(MarkKind.Fermata, melody[^1].Tones[0].Id));
            if (spec.Pedal)
            {
                foreach (var bar in bars.Where(b => b.Number % 4 == 1))
                {
                    var bass = bar.Voices.Where(v => v.Staff == 2).SelectMany(v => v.Events.OfType<Notes>()).ToList();
                    if (bass.Count == 0) continue;
                    marks.Add(new Mark(MarkKind.PedalDown, bass[0].Tones[0].Id));
                    marks.Add(new Mark(MarkKind.PedalUp, bass[^1].Tones[0].Id));
                }
            }
            if (spec.Ornaments && melody.FirstOrDefault(n => n.Length >= Ticks.Half && n != melody[^1]) is { } held) marks.Add(new Mark(rng.Chance(0.5) ? MarkKind.Trill : MarkKind.Mordent, held.Tones[0].Id));
        }
    }
}

/// <summary>Which lengths can be written as one note: a plain or dotted (once or twice) whole, half, quarter, eighth, sixteenth or 32nd.</summary>
public static class Durations
{
    public static bool IsWritable(int ticks) => Enumerable.Range(0, 3).Any(dots =>
    {
        var baseTicks = ticks * (1 << dots) / ((1 << (dots + 1)) - 1);
        return baseTicks * ((1 << (dots + 1)) - 1) == ticks * (1 << dots) && baseTicks > 0 && Ticks.Whole % baseTicks == 0 && Ticks.Whole / baseTicks is 1 or 2 or 4 or 8 or 16 or 32;
    });
}
