using System.Text.RegularExpressions;

namespace Prima.Core.Music;

/// <summary>How a stretch's key was told, the surest first.</summary>
public enum KeyTold
{
    /// <summary>The file says it's minor: its key signature carries the mode (MusicXML's, as Verovio keeps it).</summary>
    File,

    /// <summary>The piece's title names it: "Sonatina in F major", "Menuet g-Moll", "Canon in D".</summary>
    Title,

    /// <summary>The music ends on its keynote, in the bass, and the rest of what's heard agrees.</summary>
    Ending,

    /// <summary>Its notes lean to it: the time each pitch sounds fits that key's profile better than the other's.</summary>
    Notes,

    /// <summary>Not told: nothing says which, and the signature's two keys fit about as well.</summary>
    Unsure,
}

/// <summary>
/// A stretch of a piece in one key signature, bars <see cref="First"/> to <see cref="Last"/> (indexes into the score's bars):
/// the signature as printed (<see cref="Fifths"/>, sharps over 0 and flats under), the key the music is in, and how that was
/// told. The key is nearly always one of the signature's two. Now and then it's another, and <see cref="AsWritten"/> is false:
/// Bach's French Suite in C minor, written with two flats, or Mozart's Fantasia in C minor, written with none. Unsure, it's the
/// signature's major key, and <see cref="Other"/> its minor.
/// </summary>
public sealed record KeyStretch(int First, int Last, int Fifths, Key Key, KeyTold Told)
{
    public bool Sure => Told != KeyTold.Unsure;

    /// <summary>The key is the signature's own: one of its two, a major key or its relative minor.</summary>
    public bool AsWritten => Key.Fifths == Fifths;

    /// <summary>The other key the signature can be: a major key's relative minor, or a minor key's relative major.</summary>
    public Key Other => Key with { Minor = !Key.Minor };

    /// <summary>The printed signature, as a key: its sharps or flats, which <see cref="Key.Signature"/> names.</summary>
    public Key Signature => new(Fifths);
}

/// <summary>
/// Which key a piece is in. A key signature is two keys, a major one and its relative minor (two sharps: D major or B minor),
/// and the signature alone can't say which. So for each stretch of the piece in one signature, in turn: the file's mode when it
/// says minor (a file's "major" is often only its program's default: MuseScore's files say it of Chopin's G minor Ballade); the
/// key its title names (a later movement's or a variation's title names its work's key, and counts only where the music ends on
/// that keynote); and otherwise what's heard in all of the music in that signature: how the time each pitch sounds fits each
/// key's profile (Krumhansl and Kessler's, as <c>MidiNotation</c> fits a MIDI file's key), with a vote for the key whose keynote
/// the bass ends on (at Fine, where a D.C. takes it) and a smaller one for the key it starts on. Checked against the 447 titles
/// in the bundle and the owner's collection that name one of their signature's two keys (a first movement's, or a piece's own),
/// with the title left out: 440 right, 4 wrong (Chopin's B♭ minor Scherzo ends in D♭ major) and 3 not told. The bass's ending
/// alone was wrong about twice as often and said nothing for one in nine; a file's "major" was wrong in 8 of 115.
/// </summary>
public static partial class KeyFinder
{
    // Krumhansl and Kessler's key profiles: how much each degree of the scale is heard in a major and a minor key.
    private static readonly double[] MajorProfile = [6.35, 2.23, 3.48, 2.33, 4.38, 4.09, 2.52, 5.19, 2.39, 3.66, 2.29, 2.88];
    private static readonly double[] MinorProfile = [6.33, 2.68, 3.52, 5.38, 2.60, 3.53, 2.54, 4.75, 3.98, 2.69, 3.34, 3.17];

    // What's heard, in the profiles' correlations: the bass's ending and its first note are votes worth these, and the minor key
    // or the major must win by at least Clear to be told.
    private const double EndingVote = 0.15;
    private const double StartVote = 0.1;
    private const double Clear = 0.05;

    // A letter's place on the circle of fifths from C: C D E F G A B.
    private static readonly int[] FifthsOf = [0, 2, 4, -1, 1, 3, 5];

    /// <summary>A key a title names, and whether it says the mode ("in D minor") or leaves it to be major ("in D").</summary>
    private sealed record Naming(Key Key, bool ModeSaid);

    /// <summary>
    /// The keys the score is in: a stretch for each run of bars in one key signature, in order. <paramref name="titles"/> are
    /// what the piece is called (its title, its work), the first that names a key counting.
    /// </summary>
    public static IReadOnlyList<KeyStretch> Find(Score score, params string?[] titles)
    {
        var title = titles.FirstOrDefault(t => NameOf(t) is not null);
        var work = title is null ? null : NameOf(title);
        var named = title is null || Later().IsMatch(title) ? null : work;
        var keys = score.WithSignatures().Select(s => s.Key).ToList();
        var runs = new List<(int First, int Last)>();
        for (var first = 0; first < keys.Count;)
        {
            // A stretch goes on while the signature does, and the file doesn't say the mode changes.
            var last = first;
            while (last + 1 < keys.Count && keys[last + 1].Fifths == keys[first].Fifths && !(score.StatedModes.Contains(last + 1) && keys[last + 1].Minor != keys[first].Minor)) last++;
            runs.Add((first, last));
            first = last + 1;
        }
        // Where the music ends: at Fine, where a D.C. or a D.S. ends it, or its last bar.
        var end = score.Bars.Count - 1;
        for (var i = end; i >= 0; i--)
        {
            if (!score.Bars[i].Fine) continue;
            end = i;
            break;
        }
        return [.. runs.Select(run => Told(score, keys, runs, run.First, run.Last, end, named, work))];
    }

    private static KeyStretch Told(Score score, List<Key> keys, List<(int First, int Last)> runs, int first, int last, int end, Naming? named, Naming? work)
    {
        var signature = keys[first];
        var (major, minor) = (signature with { Minor = false }, signature with { Minor = true });
        KeyStretch Said(Key key, KeyTold told) => new(first, last, signature.Fifths, key, told);
        if (score.StatedModes.Contains(first) && signature.Minor) return Said(minor, KeyTold.File);

        // All the music in this signature is heard as one: its notes, its first bass note, and its last (the music's own last
        // note when the music ends in this signature, or else the last stretch's in it).
        var home = keys[0].Fifths == signature.Fifths;
        var same = runs.Where(r => keys[r.First].Fifths == signature.Fifths).ToList();
        var bars = same.SelectMany(r => Enumerable.Range(r.First, r.Last - r.First + 1)).ToList();
        int? final = keys[end].Fifths == signature.Fifths ? LastBass(score, 0, end) : null;
        var ends = final ?? LastBass(score, same[^1].First, same[^1].Last);

        if (named is { } title && TitleKey(title, signature, home) is { } key) return Said(key, KeyTold.Title);

        // A title the music ends on, where neither of the signature's keys has that keynote: a piece copied without its key
        // signature, or a later movement in its work's key.
        if (home && work is { } said && final is { } bass && PitchClass(bass) == said.Key.TonicPitchClass && PitchClass(bass) != major.TonicPitchClass && PitchClass(bass) != minor.TonicPitchClass)
            return Said(said.Key, KeyTold.Title);

        // The music ending on the second note of the signature's major scale, where the notes fit the minor key on it best: an
        // older way of writing a minor key, with one flat fewer (or a sharp more) than its own signature, as Bach wrote his French
        // Suite in C minor with two flats, and many chorales; and the Dorian mode, which reads as that minor key.
        if (home && final is { } close && PitchClass(close) == (major.TonicPitchClass + 2) % 12 && Older(score, bars, signature) is { } older) return Said(older, KeyTold.Ending);

        var ending = Vote(ends, major, minor);
        var heard = Lean(score, bars) + EndingVote * ending + StartVote * Vote(FirstBass(score, first, last), major, minor);
        // Nothing heard is clear: a file's "major" is better than nothing.
        if (Math.Abs(heard) < Clear) return Said(major, score.StatedModes.Contains(first) ? KeyTold.File : KeyTold.Unsure);

        var isMinor = heard > 0;
        return Said(isMinor ? minor : major, ending == (isMinor ? 1 : -1) ? KeyTold.Ending : KeyTold.Notes);
    }

    // The title's key as the signature has it. The same key; on the piece's own signature, the signature's key in the title's
    // mode on the same letter, the title having lost its sharp or flat ("in C minor" over four sharps is C♯ minor); or a key on
    // the same keynote as one of the signature's: the signature's, where the title doesn't say the mode ("Toccata in D" over one
    // flat is D minor), and the title's where it does (Mozart's "Fantasia in C minor", written without a signature).
    private static Key? TitleKey(Naming title, Key signature, bool home)
    {
        if (title.Key.Fifths == signature.Fifths) return title.Key;
        if (!home) return null;

        var same = signature with { Minor = title.Key.Minor };
        if (same.TonicStep == title.Key.TonicStep) return same;

        Key[] pair = [signature with { Minor = false }, signature with { Minor = true }];
        if (pair.Where(k => k.TonicPitchClass == title.Key.TonicPitchClass).ToList() is not [var parallel]) return null;
        return title.ModeSaid ? title.Key : parallel;
    }

    // The minor key a flat more than the signature (C minor for two flats), where its profile fits the notes clearly better
    // than either of the signature's own keys.
    private static Key? Older(Score score, List<int> bars, Key signature)
    {
        if (signature.Fifths <= -7) return null;

        var older = new Key(signature.Fifths - 1, true);
        var weight = Weights(score, bars);
        var fit = Correlation(weight, MinorProfile, older.TonicPitchClass);
        var own = Math.Max(Correlation(weight, MajorProfile, (signature with { Minor = false }).TonicPitchClass), Correlation(weight, MinorProfile, (signature with { Minor = true }).TonicPitchClass));
        return fit > own + Clear ? older : null;
    }

    // +1 for a bass note on the minor key's keynote, -1 on the major's, 0 for any other or none.
    private static int Vote(int? bass, Key major, Key minor) => bass is not { } b ? 0 : PitchClass(b) == minor.TonicPitchClass ? 1 : PitchClass(b) == major.TonicPitchClass ? -1 : 0;

    private static int PitchClass(int midi) => (midi % 12 + 12) % 12;

    /// <summary>
    /// The key a title names, if it names one: "in F major", "B-flat minor", "E♭ major", "in D" (major), German "g-Moll",
    /// "Es-Dur" or "H-moll" (B, its B being B♭), and French, Italian or Spanish "en ré mineur", "Do diesis minore"; one with its
    /// mode first, wherever it is. None for a key past seven sharps or flats.
    /// </summary>
    public static Key? Named(string? title) => NameOf(title)?.Key;

    private static Naming? NameOf(string? title)
    {
        if (string.IsNullOrWhiteSpace(title)) return null;

        var found = new[] { English().Match(title), German().Match(title), Romance().Match(title) }.Where(m => m.Success).MinBy(m => m.Index)
            ?? (Plain().Match(title) is { Success: true } plain ? plain : null);
        if (found is null) return null;

        var g = found.Groups;
        var mode = g["mode"].Value.ToLowerInvariant();
        var minor = mode.StartsWith("min", StringComparison.Ordinal) || mode is "moll" or "mineur" or "menor";
        Step step;
        int alter;
        if (g["syllable"].Success)
        {
            step = g["syllable"].Value.ToLowerInvariant() switch { "do" or "ut" => Step.C, "ré" or "re" => Step.D, "mi" => Step.E, "fa" => Step.F, "sol" => Step.G, "la" => Step.A, _ => Step.B };
            alter = g["accidental"].Value.ToLowerInvariant() switch { "" => 0, var a when a.StartsWith('d') || a.StartsWith('s') => 1, _ => -1 };
        }
        else
        {
            var letter = char.ToUpperInvariant(g["letter"].Value[0]);
            // German's H is B, and its B on its own is B flat.
            step = letter == 'H' ? Step.B : Enum.Parse<Step>(letter.ToString());
            alter = g["accidental"].Value.ToLowerInvariant() switch
            {
                "" => g["german"].Success && letter == 'B' ? -1 : 0,
                "is" or "sharp" or "♯" or "#" => 1,
                _ => -1,
            };
        }
        var fifths = FifthsOf[(int)step] + 7 * alter - (minor ? 3 : 0);
        return fifths is >= -7 and <= 7 ? new Naming(new Key(fifths, minor), mode.Length > 0) : null;
    }

    /// <summary>
    /// The last note the bass plays in bars <paramref name="first"/> to <paramref name="last"/> (indexes into the score's bars), as
    /// a MIDI number: in the last of them with notes, on its lowest staff with notes there, the latest to start, its lowest key.
    /// </summary>
    public static int? LastBass(Score score, int first, int last)
    {
        for (var i = Math.Min(last, score.Bars.Count - 1); i >= first; i--)
        {
            var played = score.Bars[i].Voices.SelectMany(v => v.Events.OfType<Notes>().Where(n => !n.Grace).Select(n => (v.Staff, Notes: n))).ToList();
            if (played.Count == 0) continue;

            var lowest = played.Max(p => p.Staff);
            var bass = played.Where(p => p.Staff == lowest).ToList();
            var latest = bass.Max(p => p.Notes.Start);
            return bass.Where(p => p.Notes.Start == latest).SelectMany(p => p.Notes.Tones).Min(t => t.Pitch.Midi);
        }
        return null;
    }

    /// <summary>
    /// The first note the bass plays in bars <paramref name="first"/> to <paramref name="last"/>: the lowest key of the first
    /// notes on the lowest staff of the first full bar with notes (a pickup often leads in from the fifth).
    /// </summary>
    public static int? FirstBass(Score score, int first, int last)
    {
        for (var i = first; i <= Math.Min(last, score.Bars.Count - 1); i++)
        {
            var played = score.Bars[i].Voices.SelectMany(v => v.Events.OfType<Notes>().Where(n => !n.Grace).Select(n => (v.Staff, Notes: n))).ToList();
            if (played.Count == 0 || score.Bars[i].Length is not null && i < last) continue;

            var lowest = played.Max(p => p.Staff);
            var bass = played.Where(p => p.Staff == lowest).ToList();
            var earliest = bass.Min(p => p.Notes.Start);
            return bass.Where(p => p.Notes.Start == earliest).SelectMany(p => p.Notes.Tones).Min(t => t.Pitch.Midi);
        }
        return null;
    }

    /// <summary>
    /// How much better the notes of these bars (indexes into the score's bars) fit their signature's minor key than its major
    /// one: each key's profile correlated with how long each pitch sounds there, minor's less major's (over 0 leans minor, under
    /// 0 major; 0 with no notes). The signature is the first bar's.
    /// </summary>
    public static double Lean(Score score, IReadOnlyList<int> bars)
    {
        var weight = Weights(score, bars);
        if (bars.Count == 0 || weight.All(w => w == 0)) return 0;

        var signature = score.WithSignatures().ElementAt(bars[0]).Key;
        return Correlation(weight, MinorProfile, (signature with { Minor = true }).TonicPitchClass) - Correlation(weight, MajorProfile, (signature with { Minor = false }).TonicPitchClass);
    }

    // How long each pitch sounds in these bars, in ticks, by pitch class.
    private static double[] Weights(Score score, IEnumerable<int> bars)
    {
        var weight = new double[12];
        foreach (var i in bars)
        {
            foreach (var notes in score.Bars[i].Voices.SelectMany(v => v.Events.OfType<Notes>()).Where(n => !n.Grace))
            {
                foreach (var tone in notes.Tones) weight[PitchClass(tone.Pitch.Midi)] += notes.Length;
            }
        }
        return weight;
    }

    // Pearson's correlation between how long each pitch sounds and a key's profile, turned to its keynote.
    private static double Correlation(double[] weight, double[] profile, int tonic)
    {
        var (w, p) = (weight.Average(), profile.Average());
        double sum = 0, ww = 0, pp = 0;
        for (var pc = 0; pc < 12; pc++)
        {
            var (a, b) = (weight[pc] - w, profile[((pc - tonic) % 12 + 12) % 12] - p);
            (sum, ww, pp) = (sum + a * b, ww + a * a, pp + b * b);
        }
        return ww == 0 ? 0 : sum / Math.Sqrt(ww * pp);
    }

    // "in F major", "B-flat minor", "E♭ Major", "Bb minor", "C Sharp Minor", "d minor".
    [GeneratedRegex(@"(?<![\p{L}])(?<letter>[A-G])(?:[\s-]*(?<accidental>flat|sharp|♭|♯|#)|(?<accidental>b)(?=[\s-]*(?:major|minor)\b))?[\s-]*(?<mode>major|minor)(?![\p{L}])", RegexOptions.IgnoreCase)]
    private static partial Regex English();

    // "g-Moll", "F-Dur", "Es-Dur", "Fis-moll", "H-moll", "Bb Dur".
    [GeneratedRegex(@"(?<![\p{L}])(?<german>)(?<letter>[A-H])(?<accidental>is|es|s|♭|♯|b|#)?[\s-]*(?<mode>dur|moll)(?![\p{L}])", RegexOptions.IgnoreCase)]
    private static partial Regex German();

    // "en ré mineur", "Do dièse mineur", "en Sol major", "Si bemolle maggiore", "Re menor".
    [GeneratedRegex(@"(?<![\p{L}])(?<syllable>do|ut|ré|re|mi|fa|sol|la|si)(?:[\s-]+(?<accidental>dièse|diese|diesis|sostenido|bémol|bemol|bemolle))?[\s-]+(?<mode>majeur|mineur|maggiore|minore|mayor|menor|major|minor)(?![\p{L}])", RegexOptions.IgnoreCase)]
    private static partial Regex Romance();

    // "Canon in D", "Minuet in A-flat", "Impromptu in Eb": English says a minor key's mode, so a key on its own is major.
    [GeneratedRegex(@"\bin\s+(?<letter>[A-G])(?:[\s-]*(?<accidental>flat|sharp|♭|♯)|(?<accidental>b))?(?![\p{L}\d])(?<mode>)")]
    private static partial Regex Plain();

    // A later movement or a variation, named after its work: "Sonata in A minor, Wq. 50/3: II. Largo", "Movement 2", "3. Satz",
    // "K. 331: I. Var. III".
    [GeneratedRegex(@"(?:^|:\s*|\s)(?:II|III|IV|V|VI|VII|VIII|IX)\.|\b(?:Movement|Mvt\.?|Mov\.?)\s*[2-9]|\b[2-9](?:nd|rd|th)\s+(?:movement|mvt)|\b[2-9]\.\s*Satz|\bVar(?:\.|iation)\s*(?:[IVX]+|\d+)", RegexOptions.IgnoreCase)]
    private static partial Regex Later();
}
