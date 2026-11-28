namespace Prima.Core.Music;

/// <summary>
/// Music moved to another key, by letters (staff steps) and semitones: up a major second is (1, 2), down a minor third (-2, -3).
/// Each of its keys goes round the circle of fifths by <see cref="Fifths"/> (up a major second, two sharps more), and one that would
/// then have more than seven sharps or flats is written as the key that sounds the same a letter away (G♯ major as A♭ major), its
/// notes with it. Every note keeps its place in its key: one the key gave is one the new key gives, an accidental stays one.
/// </summary>
public readonly record struct Transposition(int Steps, int Semitones)
{
    /// <summary>The lowest and highest keys on the piano, A0 and C8.</summary>
    public const int Lowest = 21, Highest = 108;

    /// <summary>As written.</summary>
    public static Transposition None => default;

    /// <summary>Whether it moves the music at all.</summary>
    public bool Moves => Steps != 0 || Semitones != 0;

    /// <summary>How far round the circle of fifths it moves a key: up a fifth is 1, up a major second 2, up a minor second -5.</summary>
    public int Fifths => 7 * Semitones - 12 * Steps;

    /// <summary>The letters a note in this key moves by: one more or one fewer where the key would pass seven sharps or flats.</summary>
    public int StepsIn(Key key) => Steps + Respelled(key.Fifths + Fifths);

    /// <summary>Where a key goes: its signature moved round by <see cref="Fifths"/> and kept within seven sharps or flats. Minor stays minor.</summary>
    public Key Of(Key key) => key with { Fifths = key.Fifths + Fifths - 12 * Respelled(key.Fifths + Fifths) };

    /// <summary>
    /// Where a note in a key goes: its letter on by the key's steps, its sound by the semitones. One that would then need a double
    /// sharp or flat it didn't have (or more than a double) is spelled the way the key it goes to spells that key on the piano,
    /// for reading at the piano: a G♯ in C major is an F double sharp in B major, so it's written G natural.
    /// </summary>
    public Spelled Of(Spelled pitch, Key key)
    {
        var moved = Moved(pitch, key);
        return Math.Abs(moved.Alter) > 2 || (Math.Abs(moved.Alter) == 2 && Math.Abs(pitch.Alter) < 2) ? Of(key).Spell(pitch.Midi + Semitones) : moved;
    }

    // A note's letter on by its key's steps and its sound by the semitones, whatever sharps or flats that takes.
    private Spelled Moved(Spelled pitch, Key key)
    {
        var moved = Spelled.FromDiatonic(pitch.Diatonic + StepsIn(key));
        return moved with { Alter = pitch.Midi + Semitones - moved.Midi };
    }

    /// <summary>
    /// The way from a score's key to another with the same mode: the nearer way, up or down, unless that takes a note off the
    /// piano (below A0, over C8) and the other way doesn't; of a tritone either way, toward middle C. The score's key is
    /// <paramref name="own"/> where it's given (the key it's in, which its signature doesn't always write: Bach's French Suite
    /// No. 2 is C minor in two flats), its first signature's otherwise. The score's own key is the score as written.
    /// </summary>
    public static Transposition To(Score score, Key key, Key? own = null)
    {
        var fifths = key.Fifths - (own ?? score.Key).Fifths;
        if (fifths == 0) return None;

        var up = Up(fifths);
        Transposition[] ways = [up, new(up.Steps - 7, up.Semitones - 12), new(up.Steps + 7, up.Semitones + 12)];
        var keys = Sounding(score).Select(t => t.Pitch.Midi).ToList();
        if (keys.Count == 0) return ways.MinBy(w => Math.Abs(w.Semitones));

        var (low, high, middle) = (keys.Min(), keys.Max(), keys.Average());
        return ways.OrderBy(w => low + w.Semitones < Lowest || high + w.Semitones > Highest).ThenBy(w => Math.Abs(w.Semitones))
            .ThenBy(w => Math.Abs(middle + w.Semitones - 60)).First();
    }

    /// <summary>
    /// The twelve keys a score can be played in, in the mode of its own key (<paramref name="own"/> where it's given, its first
    /// signature's otherwise), from C up: its own as written, and each other spelled for the piece: so that none of its printed
    /// signatures passes seven sharps or flats, then with the fewest sharps or flats any of them has (G♭ major or F♯ by where it
    /// modulates to), then the fewest notes that would take a double sharp or flat they didn't have, then the fewest where it
    /// starts, then sharps for a piece in sharps (or none) and flats for one in flats. It's the signatures that are moved, so it's
    /// theirs that count: Mozart's Fantasia K. 475, in C minor with none, is offered E♭ minor, whose signature is three flats,
    /// not D♯ minor, whose would be nine sharps.
    /// </summary>
    public static IReadOnlyList<Key> Keys(Score score, Key? ownKey = null)
    {
        var own = ownKey ?? score.Key;
        var sections = score.WithSignatures().Select(s => s.Key.Fifths).Distinct().ToList();
        var tones = Sounding(score).ToList();
        var keys = new List<Key>();
        for (var pitchClass = 0; pitchClass < 12; pitchClass++)
        {
            if (pitchClass == own.TonicPitchClass)
            {
                keys.Add(own);
                continue;
            }

            var spellings = Enumerable.Range(-7, 15).Select(f => own with { Fifths = f }).Where(k => k.TonicPitchClass == pitchClass).ToList();
            keys.Add(spellings.OrderBy(k => sections.Count(f => Math.Abs(f + k.Fifths - own.Fifths) > 7))
                .ThenBy(k => sections.Max(f => Math.Abs(f + k.Fifths - own.Fifths)))
                .ThenBy(k => Doubles(k))
                .ThenBy(k => Math.Abs(k.Fifths))
                .ThenBy(k => own.Fifths < 0 ? k.Fifths > 0 : k.Fifths < 0)
                .First());
        }
        return keys;

        // The notes that would take a double sharp or flat they didn't have in that key, and be spelled anew.
        int Doubles(Key key)
        {
            var by = Up(key.Fifths - own.Fifths);
            return tones.Count(t => Math.Abs(by.Moved(t.Pitch, t.Key).Alter) >= 2 && Math.Abs(t.Pitch.Alter) < 2);
        }
    }

    // Up the letters a key that many fifths round is, from 0 to 6, and the semitones that interval is (a diminished one may go down).
    private static Transposition Up(int fifths)
    {
        var steps = Mod(4 * fifths, 7);
        var natural = Spelled.NaturalSemitone((Step)steps);
        return new(steps, natural + Mod(7 * fifths - natural + 6, 12) - 6);
    }

    // A key past seven sharps is written a letter up (G♯ major as A♭), past seven flats a letter down: twelve fifths each.
    private static int Respelled(int fifths) => fifths > 7 ? (fifths + 4) / 12 : fifths < -7 ? -((4 - fifths) / 12) : 0;

    // Every tone in the score with the key it's written in.
    private static IEnumerable<(Spelled Pitch, Key Key)> Sounding(Score score) => score.WithSignatures()
        .SelectMany(s => s.Bar.Voices.SelectMany(v => v.Events.OfType<Notes>()).SelectMany(n => n.Tones).Select(t => (t.Pitch, s.Key)));

    private static int Mod(int value, int by) => ((value % by) + by) % by;
}
