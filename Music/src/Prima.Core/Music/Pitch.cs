namespace Prima.Core.Music;

/// <summary>A note's letter.</summary>
public enum Step
{
    C,
    D,
    E,
    F,
    G,
    A,
    B,
}

/// <summary>
/// A written pitch: its letter, its sharp or flat (-2 to 2), and its octave (C4 is middle C). F♯4 and G♭4 are the same key but
/// different written notes; the spelling is what the staff shows.
/// </summary>
public readonly record struct Spelled(Step Step, int Alter, int Octave)
{
    private static readonly int[] Semitones = [0, 2, 4, 5, 7, 9, 11];

    /// <summary>The key it plays, as a MIDI number.</summary>
    public int Midi => 12 * (Octave + 1) + Semitones[(int)Step] + Alter;

    /// <summary>The staff position: one per line or space, so two notes a third apart differ by 2.</summary>
    public int Diatonic => Octave * 7 + (int)Step;

    /// <summary>The note this many staff positions away, keeping its letter's key-signature-free spelling (no sharp or flat).</summary>
    public static Spelled FromDiatonic(int diatonic, int alter = 0) => new((Step)(((diatonic % 7) + 7) % 7), alter, (int)Math.Floor(diatonic / 7.0));

    /// <summary>The semitone of a letter without a sharp or flat, C being 0.</summary>
    public static int NaturalSemitone(Step step) => Semitones[(int)step];

    public override string ToString() => $"{Step}{Alter switch { 2 => "𝄪", 1 => "♯", -1 => "♭", -2 => "𝄫", _ => "" }}{Octave}";
}

/// <summary>
/// A key: its signature as a count of fifths (sharps positive, flats negative, -7 to 7) and whether it's minor. It knows which
/// letters its signature sharpens or flattens, its tonic, and how to spell any key on the piano in it.
/// </summary>
public readonly record struct Key(int Fifths, bool Minor = false)
{
    private static readonly Step[] SharpOrder = [Step.F, Step.C, Step.G, Step.D, Step.A, Step.E, Step.B];
    private static readonly Step[] FlatOrder = [Step.B, Step.E, Step.A, Step.D, Step.G, Step.C, Step.F];

    /// <summary>The sharp or flat the signature puts on a letter.</summary>
    public int AlterOf(Step step) => Fifths > 0 && Array.IndexOf(SharpOrder, step) < Fifths ? 1 : Fifths < 0 && Array.IndexOf(FlatOrder, step) < -Fifths ? -1 : 0;

    /// <summary>The tonic's letter: the major key's a fifth per sharp from C, and the minor's a sixth above that.</summary>
    public Step TonicStep => (Step)((((Fifths * 4) + (Minor ? 5 : 0)) % 7 + 7) % 7);

    /// <summary>The tonic's pitch class, C being 0.</summary>
    public int TonicPitchClass => ((Spelled.NaturalSemitone(TonicStep) + AlterOf(TonicStep)) % 12 + 12) % 12;

    /// <summary>The scale's degree <paramref name="degree"/> (0 is the tonic) in octave terms: its letter and sharp or flat, from the signature.</summary>
    public Spelled Degree(int degree, int octaveOfTonic)
    {
        var step = (int)TonicStep + degree;
        var letter = (Step)(((step % 7) + 7) % 7);
        return new Spelled(letter, AlterOf(letter), octaveOfTonic + (int)Math.Floor(step / 7.0));
    }

    /// <summary>
    /// Spells a key on the piano in this key: as the scale has it if it's in the scale; otherwise with the fewest sharps or flats,
    /// raising in sharp keys and in minor (the leading note), lowering in flat keys.
    /// </summary>
    public Spelled Spell(int midi)
    {
        var pitchClass = ((midi % 12) + 12) % 12;
        var best = default(Spelled);
        var bestCost = int.MaxValue;
        foreach (var step in Enum.GetValues<Step>())
        {
            var alter = pitchClass - Spelled.NaturalSemitone(step);
            if (alter > 6) alter -= 12;
            if (alter < -6) alter += 12;
            if (Math.Abs(alter) > 2) continue;

            // In the scale wins; then the fewest sharps or flats (B natural in F major, not C flat); then the key's direction.
            var fromSignature = alter - AlterOf(step);
            var raising = fromSignature > 0;
            var cost = fromSignature == 0 ? 0 : 100 + Math.Abs(alter) * 10 + Math.Abs(fromSignature) * 2 + (raising == (Fifths >= 0 || Minor) ? 0 : 3);
            if (cost >= bestCost) continue;

            var octave = (midi - Spelled.NaturalSemitone(step) - alter) / 12 - 1;
            (best, bestCost) = (new Spelled(step, alter, octave), cost);
        }
        return best;
    }

    /// <summary>The key's name, like "G major" or "F♯ minor".</summary>
    public string Name
    {
        get
        {
            var alter = AlterOf(TonicStep);
            return $"{TonicStep}{(alter > 0 ? "♯" : alter < 0 ? "♭" : "")} {(Minor ? "minor" : "major")}";
        }
    }
}

/// <summary>
/// A time signature. Compound metres (6/8, 9/8, 12/8) count dotted-quarter beats; other x/8 metres count eighths; 2/2 counts
/// halves. Lengths are in ticks, 480 to a quarter note.
/// </summary>
public readonly record struct Meter(int Count, int Unit)
{
    public bool Compound => Unit == 8 && Count % 3 == 0 && Count >= 6;

    public int BarTicks => Count * Ticks.Whole / Unit;

    public int BeatTicks => Compound ? 3 * Ticks.Whole / 8 : Ticks.Whole / Unit;

    public int Beats => BarTicks / BeatTicks;

    public override string ToString() => $"{Count}/{Unit}";
}

/// <summary>Durations in ticks.</summary>
public static class Ticks
{
    public const int Quarter = 480;
    public const int Whole = 4 * Quarter;
    public const int Half = 2 * Quarter;
    public const int Eighth = Quarter / 2;
    public const int Sixteenth = Quarter / 4;
}
