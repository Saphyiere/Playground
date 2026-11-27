using Prima.Core.Music;

namespace Prima.Core.Generation;

/// <summary>A note of an exercise's shape: its key, when it starts and how long it lasts in beats, and its hand (1 right, 2 left).</summary>
public sealed record ShapeNote(int Midi, double Start, double Length, int Hand);

/// <summary>
/// An exercise's shape: how its notes move, for its card in the catalogue, so what it is shows before a word is read: a scale is a
/// ramp, a trill a zigzag, a chord a stack, a jump two blocks far apart. Each note is a bar at its pitch and time. It's the
/// exercise's own music in C, in its first hands, from its start (two bars, or four where two hold few notes); a drill's, which
/// is new every time, is what a set of it is like.
/// </summary>
public static class ExerciseShapes
{
    private static readonly Dictionary<string, IReadOnlyList<ShapeNote>> Made = [];

    /// <summary>An exercise's shape; none for one to read or do away from the keys.</summary>
    public static IReadOnlyList<ShapeNote> Of(Exercise exercise)
    {
        lock (Made)
        {
            if (Made.TryGetValue(exercise.Id, out var made)) return made;
        }
        var shape = exercise switch
        {
            { HanonNumber: int n } => Hanon.Opening(n),
            { Kind: ExerciseKind.Played, Music: { } music } => Opening(music(Exercises.KeysOf(exercise)[0], exercise.Hands[0])),
            { Kind: ExerciseKind.Drill } => Drill(exercise.Id),
            _ => [],
        };
        lock (Made) Made[exercise.Id] = shape;
        return shape;
    }

    // The music's opening bars, as notes in beats.
    private static IReadOnlyList<ShapeNote> Opening(Score score)
    {
        var expected = score.Expected();
        var beat = (double)score.Meter.BeatTicks;
        IReadOnlyList<ShapeNote> Bars(int bars) => [.. expected.Where(n => n.Bar <= bars).Select(n => new ShapeNote(n.Midi, n.Tick / beat, n.Length / beat, n.Staff))];
        var two = Bars(2);
        return two.Count >= 6 ? two : Bars(4);
    }

    // A drill's: Scales, Arpeggios and Finger independence as their first form is, in C; the reading drills as a set of each is.
    private static IReadOnlyList<ShapeNote> Drill(string id)
    {
        var c = new Key(0);
        return id switch
        {
            "scales" => Opening(Technique.Scale(c, ScaleKind.Major, 2, HandUse.Together).Score),
            "arpeggios" => Opening(Technique.Arpeggio(c, 2, HandUse.Together).Score),
            "independence" => Opening(Technique.FingerPattern(c, 1, HandUse.Right).Score),
            "notes" => Singles([67, 60, 74, 64, 71, 57, 69, 62], 1.5),
            "endless" => Singles([64, 69, 60, 72, 65, 59, 67, 62, 71, 57, 66, 74], 0.75),
            "ledger" => Singles([79, 81, 84, 81, 79, 43, 40, 36, 40, 43], 1),
            "intervals" => [.. new[] { (60, 64), (62, 69), (64, 67), (65, 72), (67, 71) }.SelectMany((p, i) => new[] { new ShapeNote(p.Item1, i * 2, 0.9, 1), new ShapeNote(p.Item2, i * 2 + 1, 0.9, 1) })],
            "chords" => [.. new[] { new[] { 60, 64, 67 }, [62, 65, 69], [59, 62, 67], [60, 64, 69], [57, 60, 64] }.SelectMany((chord, i) => chord.Select(m => new ShapeNote(m, i * 1.5, 1.2, 1)))],
            "keys" => Singles([67, 69, 71, 72, 74, 72, 71, 69, 67], 0.75),
            "rhythm" => [.. new[] { (0.0, 1.0), (1, 0.5), (1.5, 0.5), (2, 1.5), (3.5, 0.5), (4, 1), (5, 0.25), (5.25, 0.25), (5.5, 0.5), (6, 2) }.Select(r => new ShapeNote(64, r.Item1, r.Item2 * 0.9, 1))],
            "geography" => Singles([60, 84, 48, 72, 55, 79], 1.25),
            "playback" => Singles([67, 64, 65, 67, 72, 71, 69, 67], 0.75),
            _ => [],
        };

        // Single notes one after another, a hand to each side of middle C.
        static IReadOnlyList<ShapeNote> Singles(int[] keys, double each) => [.. keys.Select((m, i) => new ShapeNote(m, i * each, each * 0.8, m >= 60 ? 1 : 2))];
    }
}
