using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace Prima.Library;

/// <summary>
/// Tells the local collection's copies of one piece from different pieces, so a piece the scholarly editions have (Sapp's, the
/// DCML's) comes from them alone. A title is known by its catalogue numbers ("Op. 27 No. 2", "K. 331", "BWV 806", "D. 899",
/// "S. 139", "L. 75", "Hob. XVI:31"), or, with none, by the words of its name ("Clair de lune"). Compared under one composer
/// only: Scarlatti's K. numbers aren't Mozart's.
/// </summary>
internal static class Catalogue
{
    // A catalogue and its number, and the number within it when there is one: "Op. 10, No. 3" is op10/3, never op10 alone, so a
    // set's other pieces aren't taken for it.
    // "Op. 10/3" and "Hob. XVI/37" are written that way too, and a chorale's number has its cantata's movement after a point
    // (BWV 113.8, not the minuet BWV Anh. 113 with its "Anh." left out).
    private static readonly Regex Numbered = new(
        @"\b(?<cat>op|opus|kv|k|bwv|hwv|woo|wq|hob\.?\s*xvi|d|s|l|sz|fp|swwv)\.?\s*[:/]?\s*(?<n>\d+(?:\.\d+)?[a-z]?)\b(?:\s*(?:,?\s*(?:no|nr|n°|nº|#)\.?|/)\s*(?<m>\d+))?",
        RegexOptions.IgnoreCase);

    // Words that don't tell one piece from another.
    private static readonly HashSet<string> Common =
    [
        "piano", "pianoforte", "sonata", "sonate", "sonatina", "no", "nr", "op", "opus", "in", "major", "minor", "dur", "moll", "the", "a",
        "an", "of", "for", "and", "de", "la", "le", "les", "du", "des", "et", "mvt", "movement", "first", "second", "third", "fourth",
        "1st", "2nd", "3rd", "4th", "i", "ii", "iii", "iv", "v", "vi", "flat", "sharp", "solo", "complete", "full", "original", "version",
    ];

    // A name of one of these words alone names a kind of piece, not a piece.
    private static readonly HashSet<string> Kinds =
    [
        "prelude", "praeludium", "fugue", "fuga", "invention", "waltz", "valse", "minuet", "menuet", "march", "marche", "etude", "study",
        "nocturne", "mazurka", "polonaise", "rondo", "allegro", "andante", "adagio", "presto", "scherzo", "impromptu", "bagatelle",
        "sarabande", "allemande", "courante", "gigue", "gavotte", "bourree", "aria", "toccata", "fantasia", "fantasy", "romance",
        "song", "dance", "lied", "ballade", "berceuse", "chorale", "intermezzo", "capriccio", "rag", "sonatina", "untitled",
    ];

    public static IReadOnlySet<string> Keys(string title)
    {
        var keys = new HashSet<string>(StringComparer.Ordinal);
        foreach (Match m in Numbered.Matches(title))
        {
            var catalogue = Regex.Replace(m.Groups["cat"].Value.ToLowerInvariant(), @"[\s.]", "") switch { "kv" => "k", "opus" => "op", var c => c };
            keys.Add(m.Groups["m"].Success ? $"{catalogue}{m.Groups["n"].Value.ToLowerInvariant()}/{m.Groups["m"].Value}" : $"{catalogue}{m.Groups["n"].Value.ToLowerInvariant()}");
        }
        if (keys.Count > 0) return keys;

        var words = Plain(title).Split(' ', StringSplitOptions.RemoveEmptyEntries).Where(w => !Common.Contains(w) && !w.All(char.IsDigit)).ToList();
        if (words.Count > 1 || words.Count == 1 && !Kinds.Contains(words[0]) && words[0].Length > 3) keys.Add("name:" + string.Join(' ', words));
        return keys;
    }

    // Lower case, without accents or punctuation: "Rêverie" is "reverie".
    private static string Plain(string text)
    {
        var plain = new StringBuilder();
        foreach (var c in text.Normalize(NormalizationForm.FormD))
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) == UnicodeCategory.NonSpacingMark) continue;
            plain.Append(char.IsLetterOrDigit(c) ? char.ToLowerInvariant(c) : ' ');
        }
        return plain.ToString();
    }
}
