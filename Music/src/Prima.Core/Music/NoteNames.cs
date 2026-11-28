namespace Prima.Core.Music;

/// <summary>What can make a printed note hard to read, when it's there.</summary>
[Flags]
public enum Hard
{
    None = 0,

    /// <summary>On or past a ledger line, above or below its staff (<see cref="NamedNote.Ledgers"/> says how many).</summary>
    LedgerLines = 1,

    /// <summary>Under an octave line (8va, 15ma, 8vb): played an octave or two from where it's printed.</summary>
    OctaveLine = 2,

    /// <summary>Sharpened or flattened by the key signature, nothing printed beside it.</summary>
    Signature = 4,

    /// <summary>Its own sharp, flat or natural printed in front of it.</summary>
    Accidental = 8,

    /// <summary>Sharpened, flattened or made natural by one printed earlier in the bar, or held over a bar line by a tie.</summary>
    Carried = 16,
}

/// <summary>
/// A printed note, for its name over it: its id (the sheet's), the key it plays, spelled as printed but in the octave it sounds
/// in (under an 8va, an octave above where it's printed), the clef it's printed in, how many ledger lines it's on or past, and
/// what can make it hard to read.
/// </summary>
public sealed record NamedNote(string Id, int Bar, int Staff, Spelled Plays, Clef Clef, int Ledgers, Hard Hard)
{
    /// <summary>The note's name without its octave: "F♯".</summary>
    public string Letter => Plays.Step + Plays.Alter switch { 2 => "𝄪", 1 => "♯", -1 => "♭", -2 => "𝄫", _ => "" };
}

/// <summary>
/// A piece's notes as a reader meets them, for printing their names over the music: every note that's played (not the end of a
/// tie, which isn't struck again; grace notes too), and what can make each hard to read, so a choice of "the hard ones" can be
/// made of them.
/// </summary>
public static class NoteNames
{
    /// <summary>Every tone that's struck, in the score's order, with its name and what makes it hard.</summary>
    public static IReadOnlyList<NamedNote> Of(Score score)
    {
        var keys = score.WithSignatures().Select(s => s.Key).ToList();
        var named = new List<NamedNote>();
        foreach (var (bar, staff, _, tone, clef) in score.TonesWithClefs())
        {
            if (tone.TiedFromPrevious) continue;

            var ledgers = Ledgers(tone.Written, clef);
            var hard = (ledgers > 0 ? Hard.LedgerLines : Hard.None)
                | (tone.Ottava != 0 ? Hard.OctaveLine : Hard.None)
                | tone.Spelling switch
                {
                    Spelling.Signature => Hard.Signature,
                    Spelling.Written => Hard.Accidental,
                    Spelling.Carried or Spelling.Tied => Hard.Carried,
                    _ => Hard.None,
                };
            named.Add(new NamedNote(tone.Id, bar, staff, tone.Pitch, clef, ledgers, hard));
        }
        return named;
    }

    /// <summary>
    /// The ledger lines a note printed here needs: none on the staff or just past it, one on the first ledger line or the space
    /// beyond it, and so on. Middle C is on the first, below the treble staff and above the bass.
    /// </summary>
    public static int Ledgers(Spelled written, Clef clef)
    {
        // From the staff's bottom line (E4 in the treble, G2 in the bass): 0 the bottom line, 8 the top.
        var p = written.Diatonic - (clef == Clef.Bass ? 18 : 30);
        return p < 0 ? -p / 2 : p > 8 ? (p - 8) / 2 : 0;
    }
}
