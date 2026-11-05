using Prima.Core.Generation;
using Prima.Core.Music;

namespace Prima.Core.Library;

/// <summary>
/// The level a stretch of real music reads at, by the ladder's features (library.md §3): each feature found in the score gives the
/// lowest level it's allowed at, and the stretch's level is the highest of these, the way Henle grades by the hardest bars. Keys,
/// metres and rhythm figures come in where the ladder brings them in. Texture, range and leaps are looser than the generator's
/// (which brings them in one small step at a time), so that pieces land near their published anchors: Anna Magdalena's minuets at
/// 4–5, Burgmüller and Clementi's sonatinas at 6–8, the Inventions at 8–10 (calibrated on the bundle; ours to tune).
/// </summary>
/// <param name="Level">1 to 12.</param>
/// <param name="Within">0 to 1, ordering stretches within a level: denser, leapier, more varied music is higher (ours).</param>
/// <param name="Reasons">The features that set the level, for the Library's details ("triplets", "4 sharps").</param>
public sealed record Grade(int Level, double Within, IReadOnlyList<string> Reasons);

public static class Grader
{
    public static Grade Grade(Score score)
    {
        var found = new List<(int Level, string Why)>();
        void Need(int level, string why) => found.Add((Math.Clamp(level, 1, Ladder.Top), why));

        var signatures = score.WithSignatures().ToList();
        foreach (var key in signatures.Select(s => s.Key).Distinct()) Need(KeyLevel(key), key.Name);
        foreach (var meter in signatures.Select(s => s.Meter).Distinct()) Need(MeterLevel(meter), $"{meter.Count}/{meter.Unit}");
        if (score.Bars.Count > 0 && score.Bars[0].Length is { } pickup && pickup < signatures[0].Meter.BarTicks) Need(6, "a pickup");

        // Every note with where it sits: bar start, staff, the clef in force, and its event.
        var clefs = new Dictionary<int, Clef> { [1] = Clef.Treble, [2] = Clef.Bass };
        var notes = new List<(long At, int Staff, Clef Clef, Notes Event, Meter Meter)>();
        var barStart = 0L;
        foreach (var (bar, _, meter) in signatures)
        {
            foreach (var voice in bar.Voices)
            {
                foreach (var e in voice.Events)
                {
                    if (e is ClefChange change)
                    {
                        if (voice.Layer == 1) clefs[voice.Staff] = change.Clef;
                        Need(7, "clef changes");
                    }
                    else if (e is Notes n) notes.Add((barStart + n.Start, voice.Staff, clefs.GetValueOrDefault(voice.Staff, Clef.Treble), n, meter));
                    // A bar's rest for a hand that's silent is nothing to read; rests inside the music are.
                    else if (e is Rest { Hidden: false } rest && !(rest.Start == 0 && rest.Length >= (bar.Length ?? meter.BarTicks))) Need(RestLevel(rest.Length), $"{Name(rest.Length)} rests");
                    if (e.Tuplet is { } t) Need(t is { Actual: 3, Normal: 2 } ? 9 : 11, t.Actual == 3 ? "triplets" : $"{t.Actual}-tuplets");
                }
            }
            barStart += bar.Length ?? meter.BarTicks;
        }

        var played = notes.Where(n => !n.Event.Grace).ToList();
        foreach (var n in notes.Where(n => n.Event.Grace).Take(1)) Need(7, "grace notes");

        // Rhythm: each written length's level, dots, ties and syncopation.
        foreach (var length in played.Where(n => n.Event.Tuplet is null).Select(n => (n.Event.Length, n.Meter.Compound)).Distinct()) Need(LengthLevel(length.Length, length.Compound), Name(length.Length) + "s");
        if (played.Any(n => n.Event.Tones.Any(t => t.TieToNext))) Need(4, "ties");
        if (played.Any(n => Syncopated(n.At, n.Event, n.Meter))) Need(8, "syncopation");

        // Hands: one staff, both in turn, or both at once.
        var staves = played.Select(n => n.Staff).Distinct().Count();
        var together = played.Where(n => n.Staff == 1).Any(r => played.Any(l => l.Staff == 2 && l.At < r.At + r.Event.Length && r.At < l.At + l.Event.Length));
        Need(staves <= 1 ? 1 : together ? 5 : 2, staves <= 1 ? "one hand" : together ? "hands together" : "hands in turn");

        // Texture: the most notes one hand holds at once (a chord, or voices on one staff).
        foreach (var staff in new[] { 1, 2 })
        {
            var most = played.Where(n => n.Staff == staff).Select(n => played.Where(o => o.Staff == staff && o.At <= n.At && n.At < o.At + o.Event.Length).Sum(o => o.Event.Tones.Count)).DefaultIfEmpty(0).Max();
            if (most >= 4) Need(10, "four notes in one hand");
            else if (most == 3) Need(8, "three-note chords");
            else if (most == 2) Need(staff == 1 ? 7 : 6, staff == 1 ? "two notes in the right hand" : "two notes in the left hand");
        }

        // Accidentals outside the key, apart from a minor key's raised sixth and seventh (the major signature's fourth and fifth, a
        // semitone up), which a minor piece needs and a file often doesn't say is minor.
        if (played.Any(n => n.Event.Tones.Any(t => Chromatic(t.Pitch, KeyAt(signatures, n.At))))) Need(7, "accidentals");

        // Range and leaps per hand (in steps of the scale), and ledger lines.
        foreach (var staff in new[] { 1, 2 })
        {
            var hand = played.Where(n => n.Staff == staff).OrderBy(n => n.At).ToList();
            if (hand.Count == 0) continue;

            var steps = hand.SelectMany(n => n.Event.Tones.Select(t => t.Pitch.Diatonic)).ToList();
            var span = steps.Max() - steps.Min() + 1;
            Need(span switch { <= 5 => 1, <= 8 => 5, <= 10 => 6, <= 15 => 7, <= 17 => 8, <= 20 => 9, <= 22 => 10, _ => 11 }, $"a range of {span} notes");
            // Leaps in the tune, along its top line and not across a rest: the right hand's, and the left hand's when the hands take
            // turns. With the hands together the left hand's leaps are its accompaniment's pattern, graded by texture and range.
            if (staff == 2 && together) continue;

            var line = hand.GroupBy(n => n.At).OrderBy(g => g.Key).Select(g => (At: g.Key, End: g.Max(n => n.At + n.Event.Length), Step: g.SelectMany(n => n.Event.Tones).Max(t => t.Pitch.Diatonic))).ToList();
            var leap = line.Zip(line.Skip(1), (a, b) => b.At <= a.End ? Math.Abs(b.Step - a.Step) : 0).DefaultIfEmpty(0).Max();
            Need(leap switch { <= 1 => 1, <= 2 => 2, <= 4 => 3, 5 => 5, <= 7 => 6, <= 9 => 7, <= 11 => 9, _ => 10 }, $"leaps of {Intervals[Math.Min(leap, Intervals.Length - 1)]}");
            // One ledger line past the level's, as the generator's accompaniment sometimes goes.
            var ledger = hand.SelectMany(n => n.Event.Tones.Select(t => Ledger(t.Pitch, n.Clef))).Max();
            if (ledger > 1) Need(Lowest(s => s.LedgerLines + 1 >= ledger), $"{ledger} ledger lines");
        }

        foreach (var kind in score.Marks.Select(m => m.Kind).Distinct())
        {
            switch (kind)
            {
                case MarkKind.PedalDown or MarkKind.PedalUp: Need(9, "pedal"); break;
                case MarkKind.OctaveUp: Need(10, "8va"); break;
                case MarkKind.Trill or MarkKind.Mordent: Need(9, "ornaments"); break;
            }
        }

        var level = found.Count == 0 ? 1 : found.Max(f => f.Level);
        var reasons = found.Where(f => f.Level == level).Select(f => f.Why).Distinct().ToList();
        return new Grade(level, Within(played.Select(n => (n.At, n.Staff, n.Event)).ToList(), signatures), reasons);
    }

    // An interval's name by its size in steps of the scale (0 is a unison).
    private static readonly string[] Intervals = ["unisons", "seconds", "thirds", "fourths", "fifths", "sixths", "sevenths", "octaves", "ninths", "tenths", "elevenths", "twelfths", "more than a twelfth"];

    private static int Lowest(Func<LevelSpec, bool> allows) => Enumerable.Range(1, Ladder.Top).FirstOrDefault(l => allows(Ladder.Level(l)), Ladder.Top);

    public static int KeyLevel(Key key) => Enumerable.Range(1, Ladder.Top).FirstOrDefault(l => Ladder.Level(l).Keys.Contains(key), Ladder.Top);

    public static int MeterLevel(Meter meter)
    {
        // Half-note beats read like 4/4 and 3/4 counted in halves; the generator brings them in late, real music uses them early.
        if (meter == new Meter(2, 2)) return 6;
        if (meter == new Meter(3, 2)) return 7;

        var level = Enumerable.Range(1, Ladder.Top).FirstOrDefault(l => Ladder.Level(l).Meters.Contains(meter));
        if (level > 0) return level;
        // Metres the generator doesn't write: like their nearest kind.
        return meter.Compound ? 9 : meter.Unit == 2 ? 8 : 12;
    }

    // The level a written length first comes in at, from the ladder's rhythm figures.
    private static int LengthLevel(int length, bool compound) => length switch
    {
        Ticks.Whole or Ticks.Half or Ticks.Quarter => compound ? 6 : 1,
        Ticks.Eighth => compound ? 6 : 3,
        720 => compound ? 6 : 5,
        1440 => compound ? 6 : 4,
        360 or Ticks.Sixteenth => compound ? 9 : 6,
        2880 => 4,
        60 or 90 or 180 => 9,
        _ => Durations.IsWritable(length) ? 9 : 11,
    };

    private static int RestLevel(int length) => length switch
    {
        >= Ticks.Half => 2,
        >= Ticks.Quarter => 4,
        _ => 6,
    };

    private static string Name(int length) => length switch
    {
        Ticks.Whole => "whole note",
        Ticks.Half => "half note",
        Ticks.Quarter => "quarter note",
        Ticks.Eighth => "eighth note",
        Ticks.Sixteenth => "sixteenth note",
        720 => "dotted quarter",
        1440 => "dotted half",
        360 => "dotted eighth",
        _ => $"{length}-tick note",
    };

    // A note that starts off the beat and is still sounding at the next beat (its own length or a tie), in a simple metre; in a
    // compound one, a quarter starting on the beat's second eighth.
    private static bool Syncopated(long at, Notes n, Meter meter)
    {
        var beat = meter.BeatTicks;
        var inBeat = ((at % beat) + beat) % beat;
        if (meter.Compound) return inBeat == Ticks.Eighth && n.Length >= Ticks.Quarter;
        return inBeat != 0 && (inBeat + n.Length > beat || n.Tones.Any(t => t.TieToNext));
    }

    private static bool Chromatic(Spelled pitch, Key key)
    {
        var expected = key.AlterOf(pitch.Step);
        if (pitch.Alter == expected) return false;

        var degree = ((int)pitch.Step - (int)new Key(key.Fifths).TonicStep + 7) % 7;
        return !(pitch.Alter == expected + 1 && degree is 3 or 4);
    }

    private static Key KeyAt(List<(Bar Bar, Key Key, Meter Meter)> signatures, long at)
    {
        var start = 0L;
        foreach (var (bar, key, meter) in signatures)
        {
            var length = bar.Length ?? meter.BarTicks;
            if (at < start + length) return key;
            start += length;
        }
        return signatures.Count > 0 ? signatures[^1].Key : new Key(0);
    }

    private static int Ledger(Spelled pitch, Clef clef)
    {
        var (bottom, top) = clef == Clef.Treble ? (30, 38) : (18, 26);
        return pitch.Diatonic < bottom ? (bottom - pitch.Diatonic) / 2 : pitch.Diatonic > top ? (pitch.Diatonic - top) / 2 : 0;
    }

    // Within a level (ours, a hand-weighted rubric): density, leap rate, pitch-class entropy, range and rhythmic variety.
    private static double Within(List<(long At, int Staff, Notes Event)> notes, List<(Bar Bar, Key Key, Meter Meter)> signatures)
    {
        if (notes.Count == 0) return 0;

        var beats = signatures.Sum(s => (s.Bar.Length ?? s.Meter.BarTicks) / (double)s.Meter.BeatTicks);
        var density = Math.Min(1, notes.Sum(n => n.Event.Tones.Count) / Math.Max(1, beats) / 4);
        var intervals = notes.GroupBy(n => n.Staff).SelectMany(g =>
        {
            var tops = g.OrderBy(n => n.At).Select(n => n.Event.Tones.Max(t => t.Pitch.Diatonic)).ToList();
            return tops.Zip(tops.Skip(1), (a, b) => Math.Abs(b - a));
        }).ToList();
        var leaps = intervals.Count == 0 ? 0 : intervals.Count(i => i > 2) / (double)intervals.Count;
        var classes = notes.SelectMany(n => n.Event.Tones).GroupBy(t => ((t.Pitch.Midi % 12) + 12) % 12).Select(g => (double)g.Count()).ToList();
        var total = classes.Sum();
        var entropy = -classes.Sum(c => c / total * Math.Log2(c / total)) / Math.Log2(12);
        var range = notes.GroupBy(n => n.Staff).Average(g => g.SelectMany(n => n.Event.Tones).Max(t => t.Pitch.Diatonic) - g.SelectMany(n => n.Event.Tones).Min(t => t.Pitch.Diatonic)) / 22.0;
        var variety = Math.Min(1, notes.Select(n => n.Event.Length).Distinct().Count() / 6.0);
        return Math.Clamp(0.3 * density + 0.2 * leaps + 0.2 * entropy + 0.15 * Math.Min(1, range) + 0.15 * variety, 0, 0.999);
    }
}
