using System.IO.Compression;
using System.Text.Json;
using Prima.Core.Music;

namespace Prima.Core.Generation;

/// <summary>The numbered studies in the order of the Schirmer edition.</summary>
public static class Hanon
{
    private static readonly (string Name, string Section, string Tip)[] Studies =
    [
        ("The opening third", "five", "Aim for the second note before playing the first. Listen for an even step from finger four to five."),
        ("Changing direction", "five", "Sing the first eight notes. Listen for the same beat when the melody turns back."),
        ("The returning third", "five", "Play one hand softly. Keep the notes after the opening leap as clear as the landing."),
        ("Returning to the starting note", "five", "Listen to the first three notes as a group. Let the hand travel for the next leap."),
        ("Alternating reaches", "arm", "Guide the hand toward each landing with a small forearm turn. Listen for accents on the longer reaches."),
        ("Returning to the upper note", "arm", "Play the first bar with one hand. Let the forearm turn toward the recurring note and back toward each changing note."),
        ("Interlocking thirds", "five", "Hear the short pairs separately, then join them without pausing between pairs."),
        ("Broken shapes", "five", "Find the first four notes silently. Move the hand as a unit when the pattern starts on the next key."),
        ("Widening neighbours", "five", "Listen for the lower notes between the rising ones. Give both lines the same pulse."),
        ("The descending turn", "five", "Play the first leap quietly. Carry that light touch through the changes of direction."),
        ("Upper-finger alternation", "trills", "Play the alternating notes as slowly as the surrounding ones. Listen for an accent on the fifth finger."),
        ("Dropping to the lower note", "five", "Locate the lower landing before releasing the first note. Listen through the change of position."),
        ("Travelling pairs", "five", "Hear each pair as a single gesture. Move the forearm sideways as the pair shifts up a step."),
        ("Inner-finger turns", "five", "Keep the returning inner note audible even when the outer notes change."),
        ("Rising thirds", "five", "Group the ascending skips in twos. Keep the first note of the next bar on the beat."),
        ("Reaching past a neighbour", "five", "Practise just the middle leap, then add the two notes that lead into it."),
        ("Expanding the reach", "five", "Move toward the highest note before playing it. Release the previous shape as you travel."),
        ("Moving back through the hand", "five", "Give the last two notes their full time before starting the next bar."),
        ("Changing the span", "five", "Play the changing intervals in pairs. Notice where the hand needs to move sideways."),
        ("Broken shapes and a chord close", "broken", "Find the final chord with each hand alone. Bring its notes down together when you join the hands."),
        ("Longer finger groups", "five", "Practise one beat at a time. Hear the return to the first pitch before moving into the next group."),
        ("Returning through the middle", "five", "Keep the recurring middle note light. Listen for equal spacing around it."),
        ("Repeated neighbouring turns", "trills", "Play the returning notes in one hand and tap the beat with the other."),
        ("Alternating around an anchor", "trills", "Listen to the anchor note while the notes on either side change."),
        ("Shifting three-note groups", "five", "Notice where a group starts against the beat. Keep counting through the shift."),
        ("Changing the lower landing", "five", "Play slowly enough to name the landing before moving the hand."),
        ("Turns and skips", "five", "Separate the turn from the skip, then put them together with the same spacing."),
        ("Repeated finger pairs", "trills", "Listen to the second note of each pair. Let it sound as clearly as the first."),
        ("Alternating neighbours", "trills", "Keep the recurring lower note even as its partner moves farther away."),
        ("Preparing the trill", "trills", "Play a short group of alternating notes, release the hand, and repeat it at the same tempo."),
        ("The recurring upper note", "arm", "Let the forearm follow the changing lower pitches. Keep the upper note light."),
        ("Thumb under the second finger", "thumb", "Listen to the notes either side of the thumb crossing. Keep the space between them even."),
        ("Thumb under the third finger", "thumb", "Move the forearm toward the new position as the thumb crosses."),
        ("Thumb under the fourth finger", "thumb", "Work on one crossing in each direction. Listen for a bump when the thumb lands."),
        ("Thumb under the fifth finger", "thumb", "Give the hand time to travel. Practise the crossing alone before adding speed."),
        ("Thumb crossings in sequence", "thumb", "Locate the next thumb note before playing the group that precedes it."),
        ("Thumb movements", "thumb", "Practise the thumb movements quietly and release each position before moving to the next."),
        ("Preparing the scales", "scales", "Listen for the thumb crossings in each hand separately, then line up the beats together."),
        ("Major and minor scales", "scales", "Compare the sixth and seventh on the way down in the harmonic and melodic minor forms."),
        ("Chromatic scales", "scales", "Listen at each change between white and black keys. Keep the semitone steps equally spaced."),
        ("Triad arpeggios", "broken", "Play the triad together once, then spread it out. Move the arm toward the next octave before the thumb lands."),
        ("Diminished-seventh arpeggios", "broken", "Hear the equal minor thirds. Release the previous position as the hand moves on."),
        ("Dominant-seventh arpeggios", "broken", "Find the seventh in each shape before breaking up the chord. Let the final chord roll."),
        ("Repeated notes in threes", "touch", "Allow the key to rise between attacks. Listen for three distinct notes on one beat."),
        ("Repeated notes in twos", "touch", "Listen to each pair at one volume as the fingering changes."),
        ("The trill", "trills", "Start with four even alternations. Lengthen the group when each note speaks clearly."),
        ("Repeated notes in fours", "touch", "Count the beat above four quiet attacks. Let each finger leave as the next one arrives."),
        ("Wrist movement in double notes", "double", "Play one pair and release. Listen for both notes arriving together before joining the pairs."),
        ("Changing spans in double notes", "double", "Find each new interval silently. Release the old span when moving the hand."),
        ("Preparing scales in thirds", "double", "Listen to the lower line alone, then add the upper note without losing that line."),
        ("Preparing scales in octaves", "double", "Practise a short group with a comfortable octave. Let the wrist rebound between landings."),
        ("Scales in thirds", "double", "Play the two lines separately first. Listen for both voices through each hand change."),
        ("Scales in octaves", "double", "Move to each octave as a single shape. Listen for one clean attack from its two notes."),
        ("Fourfold trill in thirds", "trills", "Practise each line of the double trill by itself, then bring the two attacks together."),
        ("Threefold trill", "trills", "Hear each change of finger pair. Keep the tempo steady at the change."),
        ("Scales in broken octaves", "double", "Let the forearm turn between low and high notes. Keep the two ends equally audible."),
        ("Broken arpeggios in octaves", "broken", "Find the next octave before leaving the current one. Start with one hand and a single arpeggio."),
        ("Sustained octaves", "double", "Listen for the held octave while the inner fingers move. Release pressure once the outer keys are down."),
        ("Fourfold trill in sixths", "trills", "Practise two pairs slowly. Listen to the upper voice on one pass and the lower voice on the next."),
        ("The tremolo", "arm", "Alternate the single note and the chord softly. Keep the forearm free enough to shape the printed swells."),
    ];

    public static string Id(int number) => $"hanon-{number:00}";

    public static IReadOnlyList<Exercise> All { get; } = Studies.Select((study, i) =>
    {
        var number = i + 1;
        return new Exercise(Id(number), study.Section, $"Hanon No. {number} · {study.Name}", number <= 20 ? ExerciseLevel.Beginner : ExerciseLevel.Intermediate,
            ExerciseKind.Played, study.Name, ["Work on one bar with each hand.", "Join the hands when both parts have a steady pulse."], study.Tip)
        {
            HanonNumber = number, Music = (key, hands) => Music(number, key, hands),
            Hands = [HandUse.Together, HandUse.Right, HandUse.Left], Tempo = number == 60 ? 48 : 60, Close = 0,
            KeyLabel = AsPrintedOnly(number) ? _ => "As printed" : null,
        };
    }).ToList();

    public static IReadOnlyList<string> Focus(int number) => Studies[number - 1].Section switch
    {
        "thumb" or "scales" => ["thumb", "even"],
        "arm" => ["rotation", "even"],
        "trills" => ["independence", "even"],
        "double" => ["double", "wrist"],
        "broken" => ["chords", "even"],
        "touch" => ["touch", "even"],
        _ => ["even", "independence"],
    };

    public static bool PrintedKeys(int number) => number is 39 or 41;
    public static bool AsPrintedOnly(int number) => number is 52 or 53 or 56 or 57;
    private static readonly Key[] ScaleOrder = new[] { 0, -1, -2, -3, -4, -5, -6, 5, 4, 3, 2, 1 }.SelectMany(f => new Key[] { new(f), new(f, true) }).ToArray();
    private static readonly Key[] MajorKeys = Technique.Order.Where(k => !k.Minor).ToArray();
    public static IReadOnlyList<Key> Keys(int number) => PrintedKeys(number) ? ScaleOrder : AsPrintedOnly(number) ? [new(0)] : MajorKeys;

    private static readonly Lazy<double[][][]> Openings = new(() =>
    {
        using var stream = typeof(Hanon).Assembly.GetManifestResourceStream("Hanon/openings.json")!;
        return JsonSerializer.Deserialize<double[][][]>(stream)!;
    });

    internal static IReadOnlyList<ShapeNote> Opening(int number) => [.. Openings.Value[number - 1].Select(n => new ShapeNote((int)n[0], n[1], n[2], (int)n[3]))];

    // Only the study on the stand is cached. The catalogue reads just the openings.
    private static readonly object Gate = new();
    private static (int Number, Score Score)? last;

    public static Score Printed(int number)
    {
        lock (Gate)
        {
            if (last is { } cached && cached.Number == number) return cached.Score;
            using var resource = typeof(Hanon).Assembly.GetManifestResourceStream($"Hanon/{number:00}.mei.gz") ?? throw new ArgumentOutOfRangeException(nameof(number));
            using var compressed = new GZipStream(resource, CompressionMode.Decompress);
            using var reader = new StreamReader(compressed);
            var read = MeiReader.Read(reader.ReadToEnd());
            var score = read with { Marks = read.FileMarks, FileMarks = [] };
            last = (number, score);
            return score;
        }
    }

    /// <summary>The edition marks each return point explicitly. In No. 42 two successive end signs return to the same start.</summary>
    public static IReadOnlyList<int> PlayOrder(Score score)
    {
        var order = new List<int>();
        var start = 0;
        for (var i = 0; i < score.Bars.Count; i++)
        {
            if (score.Bars[i].RepeatStart) start = i;
            order.Add(i);
            if (score.Bars[i].RepeatEnd) order.AddRange(Enumerable.Range(start, i - start + 1));
        }
        return order;
    }

    public static Score Music(int number, Key key, HandUse hands)
    {
        var score = Printed(number);
        if (PrintedKeys(number))
        {
            var at = Array.IndexOf(ScaleOrder, key);
            if (at < 0) throw new ArgumentOutOfRangeException(nameof(key));
            var first = number == 39 ? at / 2 * 30 + at % 2 * 10 : at * 3;
            var count = number == 39 ? (key.Minor ? 20 : 10) : 3;
            var excerpt = score.Excerpt(first + 1, first + count);
            // Excerpt removes repeat signs when a passage is cut out of a piece.
            score = excerpt with { Key = key, Bars = [.. excerpt.Bars.Select((bar, i) => bar with
            { Number = i + 1, RepeatStart = score.Bars[first + i].RepeatStart, RepeatEnd = score.Bars[first + i].RepeatEnd })] };
            if (number == 39 && key.Minor)
                score = score with { Marks = [.. score.Marks, new(MarkKind.Text, FirstNote(0), Text: "Harmonic minor"), new(MarkKind.Text, FirstNote(10), Text: "Melodic minor")] };
            string FirstNote(int index) => score.Bars[index].Voices.First(v => hands != HandUse.Left || v.Staff == 2).Events.OfType<Notes>().First().Tones[0].Id;
        }
        else if (!AsPrintedOnly(number) && key != score.Key)
        {
            var pitches = score.Bars.SelectMany(b => b.Voices).SelectMany(v => v.Events.OfType<Notes>()).SelectMany(n => n.Tones).Select(t => t.Pitch.Midi).ToList();
            var shift = key.TonicPitchClass > 6 ? key.TonicPitchClass - 12 : key.TonicPitchClass;
            if (pitches.Min() + shift < 21) shift += 12;
            if (pitches.Max() + shift > 108) shift -= 12;
            var steps = (int)key.TonicStep - (shift < 0 ? 7 : 0);
            Tone Transpose(Tone tone)
            {
                var target = Spelled.FromDiatonic(tone.Pitch.Diatonic + steps);
                var midi = tone.Pitch.Midi + shift;
                var alter = midi - target.Midi;
                return tone with { Pitch = Math.Abs(alter) <= 2 ? target with { Alter = alter } : key.Spell(midi) };
            }
            score = score with { Key = key, Bars = [.. score.Bars.Select(bar => bar with
            {
                Voices = [.. bar.Voices.Select(v => v with { Events = [.. v.Events.Select(e => e is Notes chord ? chord with { Tones = [.. chord.Tones.Select(Transpose)] } : e)] })],
            })] };
        }
        if (hands == HandUse.Together) return score;
        var keep = hands == HandUse.Right ? 1 : 2;
        var ids = score.Bars.SelectMany(b => b.Voices.Where(v => v.Staff == keep)).SelectMany(v => v.Events.OfType<Notes>())
            .SelectMany(n => n.Tones.Select(t => t.Id).Append(n.Id)).ToHashSet();
        return score with
        {
            Bars = [.. score.WithSignatures().Select(s => s.Bar with { Voices = [.. s.Bar.Voices.Where(v => v.Staff == keep || v.Layer == 1)
                .Select(v => v.Staff == keep ? v : v with { Events = [new Rest($"silent{s.Bar.Number}", 0, s.Bar.Length ?? s.Meter.BarTicks) { Hidden = true }] })] })],
            Marks = [.. score.Marks.Where(m => ids.Contains(m.StartId) && (m.EndId is null || ids.Contains(m.EndId)))],
        };
    }
}
