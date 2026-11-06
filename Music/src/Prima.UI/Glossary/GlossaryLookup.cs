using System.Xml.Linq;
using Prima.Core.Music;
using Prima.UI.Components;

namespace Prima.UI.Glossary;

/// <summary>Finding things in the glossary: what a tap on the sheet found, and where a note sits on the staff.</summary>
public static class GlossaryLookup
{
    private static readonly XNamespace Xml = "http://www.w3.org/XML/1998/namespace";

    /// <summary>
    /// The entry for a symbol tapped on the sheet. Its MEI element (by id, or the note or bar it's part of, for what Verovio draws
    /// from an attribute) is read for its detail, and the most particular entry wins: a staccato before any articulation, "rit."
    /// in a direction before directions. Without an element, the kind the sheet saw decides.
    /// </summary>
    public static GlossaryEntry? Find(XDocument? mei, SheetSymbol symbol)
    {
        var kind = symbol.Kind.Split(':')[0];
        var element = ById(mei, symbol.Id);
        var within = ById(mei, symbol.Within);
        // The element's own keys end with its plain name, which waits until the particular ones, the sheet's included, are tried.
        var own = element is null ? [] : KeysOf(element).ToList();
        List<string> keys = [.. own.SkipLast(1)];
        // Drawn from the note's own attributes: its accidental, articulation or dots.
        if (element is null && within is { Name.LocalName: "note" or "chord" } note) keys.AddRange(kind switch
        {
            "accid" when (string?)note.Attribute("accid") is { } accid => [$"accid:{accid}"],
            "artic" => ((string?)note.Attribute("artic") ?? "").Split(' ', StringSplitOptions.RemoveEmptyEntries).Select(a => $"artic:{a}"),
            "dots" => (string?)note.Attribute("dots") == "2" ? ["dots:2", "dots"] : ["dots"],
            _ => [],
        });
        // A bar line is the bar's: a repeat sign, a final or a double bar line, by the side the bar says.
        if (kind == "barLine" && within is { Name.LocalName: "measure" } bar)
        {
            var sides = $"{bar.Attribute("left")?.Value} {bar.Attribute("right")?.Value}";
            if (sides.Contains("rpt")) keys.Add("barLine:rpt");
            else if (sides.Contains("end")) keys.Add("barLine:end");
            else if (sides.Contains("dbl")) keys.Add("barLine:dbl");
        }
        keys.Add(symbol.Kind);
        keys.AddRange(own.TakeLast(1));
        keys.Add(kind);

        // Words in a direction or tempo mark: the entry whose word is the longest found in its text ("d.c. al fine" before "fine").
        if (element is { Name.LocalName: "dir" or "tempo" })
        {
            var text = Text(element);
            var worded = GlossaryEntries.All.SelectMany(e => e.Marks.Where(m => m.StartsWith("dir:") || m.StartsWith("tempo:")).Select(m => (Entry: e, Word: m[(m.IndexOf(':') + 1)..])))
                .Where(w => w.Word.Length > 0 && text.Contains(w.Word, StringComparison.Ordinal)).OrderByDescending(w => w.Word.Length).FirstOrDefault();
            if (worded.Entry is not null) return worded.Entry;
        }
        return keys.Select(k => GlossaryEntries.All.FirstOrDefault(e => e.Marks.Contains(k))).FirstOrDefault(e => e is not null);
    }

    /// <summary>A note on the sheet by its id: its name and where it sits ("F♯5, on the top line of the treble staff").</summary>
    public static string? NoteLine(Score score, string id)
    {
        var clefs = new Dictionary<int, Clef> { [1] = Clef.Treble, [2] = Clef.Bass };
        foreach (var bar in score.Bars)
        {
            foreach (var voice in bar.Voices)
            {
                foreach (var e in voice.Events)
                {
                    if (e is ClefChange change) clefs[voice.Staff] = change.Clef;
                    else if (e is Notes notes && notes.Tones.FirstOrDefault(t => t.Id == id) is { } tone) return $"{tone.Pitch}, {Place(tone.Pitch, clefs.GetValueOrDefault(voice.Staff, Clef.Treble))}.";
                }
            }
        }
        return null;
    }

    /// <summary>
    /// Where a note sits: on a line or in a space of the staff, counted from the bottom, or above or below it, on a ledger line or
    /// past one; middle C said as such.
    /// </summary>
    public static string Place(Spelled pitch, Clef clef)
    {
        var staff = clef == Clef.Bass ? "bass" : "treble";
        // From the staff's bottom line (E4 in the treble, G2 in the bass): 0 the bottom line, 1 the first space … 8 the top line.
        var p = pitch.Diatonic - (clef == Clef.Bass ? 18 : 30);
        string[] lines = ["bottom", "second", "middle", "fourth", "top"];
        string[] spaces = ["first", "second", "third", "top"];
        string[] ordinals = ["first", "second", "third", "fourth", "fifth", "sixth"];
        var place = p switch
        {
            >= 0 and <= 8 when p % 2 == 0 => $"on the {lines[p / 2]} line of the {staff} staff",
            >= 0 and <= 8 => $"in the {spaces[p / 2]} space of the {staff} staff",
            _ => Beyond(p < 0 ? -p : p - 8, p < 0 ? "below" : "above"),
        };
        return pitch.Midi == 60 ? $"middle C, {place}" : place;

        // So many positions past the staff's outer line: just past it, on a ledger line, or past one.
        string Beyond(int steps, string side)
        {
            var ledger = ordinals[Math.Min(ordinals.Length - 1, (steps - 1) / 2)];
            return steps == 1 ? $"just {side} the {staff} staff" : steps % 2 == 0 ? $"on the {ordinals[Math.Min(ordinals.Length - 1, steps / 2 - 1)]} ledger line {side} the {staff} staff" : $"{side} the {ledger} ledger line, {side} the {staff} staff";
        }
    }

    /// <summary>Entries whose name, words or summary have the search in them, by group and how common they are.</summary>
    public static IEnumerable<GlossaryEntry> Search(string query)
    {
        var words = query.Trim().ToLowerInvariant();
        return GlossaryEntries.All.Where(e => words.Length == 0 || e.Name.Contains(words, StringComparison.OrdinalIgnoreCase) || e.Words.Any(w => w.Contains(words, StringComparison.OrdinalIgnoreCase)) || e.Summary.Contains(words, StringComparison.OrdinalIgnoreCase))
            .OrderBy(e => e.Group).ThenBy(e => e.Rank);
    }

    private static XElement? ById(XDocument? mei, string? id) => mei is null || string.IsNullOrEmpty(id) ? null : mei.Descendants().FirstOrDefault(e => (string?)e.Attribute(Xml + "id") == id);

    private static string Text(XElement e) => string.Concat(e.DescendantNodes().OfType<XText>().Select(t => t.Value)).Trim().ToLowerInvariant();

    // The keys an element is, most particular first: a quarter note's "note:4" before "note".
    private static IEnumerable<string> KeysOf(XElement e)
    {
        string? A(string name) => (string?)e.Attribute(name);
        switch (e.Name.LocalName)
        {
            case "note":
                if (A("grace") is not null) yield return "note:grace";
                if ((A("dur") ?? (string?)e.Parent?.Attribute("dur")) is { } dur) yield return $"note:{dur}";
                if ((A("dots") ?? (string?)e.Parent?.Attribute("dots")) is { } dots)
                {
                    if (dots == "2") yield return "dots:2";
                    yield return "dots";
                }
                break;
            case "rest":
                if (A("dur") is { } length) yield return $"rest:{length}";
                break;
            case "artic":
                if ((A("artic") ?? "").Contains(' ')) yield return $"artic:{A("artic")}";
                foreach (var artic in (A("artic") ?? "").Split(' ', StringSplitOptions.RemoveEmptyEntries)) yield return $"artic:{artic}";
                break;
            case "accid":
                if (A("func") == "caution") yield return "accid:caution";
                if (A("accid") is { } accid) yield return $"accid:{accid}";
                break;
            case "meterSig":
                if (A("sym") is { } sym) yield return $"meterSig:{sym}";
                if (A("count") is { } count && A("unit") is { } unit) yield return $"meterSig:{count}/{unit}";
                break;
            case "tuplet":
                if (A("num") is { } num) yield return $"tuplet:{num}";
                break;
            case "repeatMark":
                yield return $"repeatMark:{A("func")}";
                break;
            case "arpeg":
                if (A("order") == "nonarp") yield return "arpeg:nonarp";
                break;
            case "dynam":
                yield return $"dynam:{Text(e).Replace(".", "")}";
                break;
            case "hairpin":
                yield return $"hairpin:{A("form")}";
                break;
            case "clef":
                yield return $"clef:{A("shape")}";
                break;
            case "pedal":
                if (A("func") == "sostenuto") yield return "pedal:sostenuto";
                yield return $"pedal:{A("dir")}";
                break;
            case "octave":
                yield return $"octave:{A("dis")}{A("dis.place")}";
                break;
            case "mordent":
                yield return $"mordent:{A("form") ?? "upper"}";
                break;
        }
        yield return e.Name.LocalName;
    }
}
