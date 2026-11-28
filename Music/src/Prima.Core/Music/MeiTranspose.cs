using System.Globalization;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace Prima.Core.Music;

/// <summary>
/// A piece's MEI moved to another key in place (<see cref="Transposition"/>), every id kept, so the music Verovio draws, the notes
/// <see cref="MeiReader"/> reads from it and the marks kept on them by id all agree. It moves the notes (as printed, and as they
/// sound under an 8va line), every key signature, the chord symbols over the music, an ornament's sharp or flat, and rests placed
/// to make room for another voice. A note keeps its place in its key: a sharp, flat or natural printed on it stays printed (as the
/// one it now needs), one its key gave is one the new key gives, and a note tied over a bar line still takes its accidental from
/// the note before. Clefs and octave lines stay where they are, so a key far off can take the music onto ledger lines.
/// </summary>
public static partial class MeiTranspose
{
    private static readonly XNamespace Mei = "http://www.music-encoding.org/ns/mei";

    public static string Apply(string mei, Transposition by)
    {
        if (!by.Moves) return mei;

        var document = XDocument.Parse(mei);
        if (document.Descendants(Mei + "score").FirstOrDefault() is not { } score) return mei;

        // Notes held over by a tie, which take their sharp or flat from the note before.
        var tiedInto = score.Descendants(Mei + "tie").Select(t => Id((string?)t.Attribute("endid"))).ToHashSet();
        // The key written for each staff; a staff without one of its own has the score's.
        var keys = new Dictionary<int, Key>();
        var everywhere = new Key(0);
        Key KeyOf(int staff) => keys.TryGetValue(staff, out var key) ? key : everywhere;
        // Each note as printed, before and after, by its id, for an ornament on it.
        var notes = new Dictionary<string, (Spelled Was, Spelled Now)>();

        foreach (var element in score.Descendants().Where(e => e.Name == Mei + "scoreDef" || e.Name == Mei + "measure").ToList())
        {
            if (element.Name == Mei + "scoreDef")
            {
                // The score's own key (a signature on the scoreDef itself) holds for every staff; then each staff's own.
                if (MoveKey(element, by) is { } all)
                {
                    everywhere = all;
                    keys.Clear();
                }
                foreach (var staffDef in element.Descendants(Mei + "staffDef"))
                {
                    if (MoveKey(staffDef, by) is { } own) keys[(int?)staffDef.Attribute("n") ?? 1] = own;
                }
                continue;
            }

            foreach (var staff in element.Elements(Mei + "staff"))
            {
                var n = (int?)staff.Attribute("n") ?? 1;
                // Rests beside another voice are placed to keep out of its way, and move with its notes; a voice's own stay mid-staff.
                var voices = staff.Elements(Mei + "layer").Count(l => l.HasElements) > 1;
                // The accidentals so far in the bar, by line or space (letter and printed octave): as written, and as moved.
                var (before, after) = (new Dictionary<(Step, int), int>(), new Dictionary<(Step, int), int>());
                foreach (var item in staff.Descendants().ToList())
                {
                    switch (item.Name.LocalName)
                    {
                        case "note":
                            if (MoveNote(item, KeyOf(n), by, before, after, tiedInto) is { } note) notes[note.Id] = (note.Was, note.Now);
                            break;
                        case "rest" or "mRest" when voices:
                            MoveRest(item, by.StepsIn(KeyOf(n)));
                            break;
                        case "keySig" when MoveKey(item, by) is { } changed:
                            keys[n] = changed;
                            break;
                    }
                }
            }
            foreach (var harm in element.Elements(Mei + "harm")) MoveHarm(harm, KeyOf(Staff(harm)), by);
            foreach (var ornament in element.Elements().Where(e => e.Name.LocalName is "trill" or "mordent" or "turn"))
            {
                if (notes.TryGetValue(Id((string?)ornament.Attribute("startid")), out var on)) MoveOrnament(ornament, on.Was, on.Now, by);
            }
        }
        return document.ToString(SaveOptions.DisableFormatting);
    }

    /// <summary>The MEI without its fingering: a key it's played in that it wasn't written in would want fingers of its own.</summary>
    public static string WithoutFingering(string mei)
    {
        if (!mei.Contains("<fing", StringComparison.Ordinal)) return mei;

        var document = XDocument.Parse(mei);
        document.Descendants(Mei + "fing").Remove();
        return document.ToString(SaveOptions.DisableFormatting);
    }

    // A key signature written on the element (a scoreDef's or staffDef's key.sig, or MEI 5's keysig, as the Open Hymnal's hymns
    // have it; its keySig; or a keySig itself), moved: the key it was, or none where it writes none.
    private static Key? MoveKey(XElement element, Transposition by)
    {
        var attribute = element.Name.LocalName == "keySig" ? null : new[] { "key.sig", "keysig" }.FirstOrDefault(a => element.Attribute(a) is not null);
        var holder = element.Name.LocalName == "keySig" || attribute is not null ? element : element.Element(Mei + "keySig");
        if (holder is null) return null;

        var (sig, mode, pname, accid) = attribute is not null ? (attribute, "key.mode", "key.pname", "key.accid") : ("sig", "mode", "pname", "accid");
        if (Fifths((string?)holder.Attribute(sig)) is not { } fifths) return null;

        var key = new Key(fifths, (string?)holder.Attribute(mode) == "minor");
        var moved = by.Of(key);
        holder.SetAttributeValue(sig, moved.Fifths == 0 ? "0" : $"{Math.Abs(moved.Fifths)}{(moved.Fifths > 0 ? 's' : 'f')}");
        // Its tonic, where it names one.
        if ((string?)holder.Attribute(pname) is { } letter && Enum.TryParse<Step>(letter, ignoreCase: true, out var step))
        {
            var tonic = by.Of(new Spelled(step, Alter((string?)holder.Attribute(accid)), 4), key);
            holder.SetAttributeValue(pname, tonic.Step.ToString().ToLowerInvariant());
            holder.SetAttributeValue(accid, tonic.Alter == 0 ? null : Written(tonic.Alter));
        }
        return key;
    }

    // A note moved, its accidentals with it: its id and the note as printed before and after, or none for a note with no pitch (a
    // drum's).
    private static (string Id, Spelled Was, Spelled Now)? MoveNote(XElement note, Key key, Transposition by, Dictionary<(Step, int), int> before,
        Dictionary<(Step, int), int> after, HashSet<string> tiedInto)
    {
        if ((string?)note.Attribute("pname") is not { } pname || !Enum.TryParse<Step>(pname, ignoreCase: true, out var step) || (int?)note.Attribute("oct") is not { } octave)
            return null;

        // As MeiReader reads it: the sharp or flat printed, or the one it sounds (gestural), or the bar's earlier one on its line or
        // space, or the key's.
        var accid = note.Element(Mei + "accid");
        var written = (string?)note.Attribute("accid") ?? (string?)accid?.Attribute("accid");
        var gestural = (string?)note.Attribute("accid.ges") ?? (string?)accid?.Attribute("accid.ges");
        var implied = before.TryGetValue((step, octave), out var earlier) ? earlier : key.AlterOf(step);
        var alter = written is not null ? Alter(written) : gestural is not null ? Alter(gestural) : implied;
        if (written is not null) before[(step, octave)] = alter;

        var was = new Spelled(step, alter, octave);
        var moved = by.Of(was, key);
        var given = after.TryGetValue((moved.Step, moved.Octave), out var earlierMoved) ? earlierMoved : by.Of(key).AlterOf(moved.Step);
        var tied = tiedInto.Contains(Id((string?)note.Attribute(XNamespace.Xml + "id"))) || ((string?)note.Attribute("tie") ?? (string?)note.Parent?.Attribute("tie")) is "t" or "m";
        // Printed if it was; or where it read as its key and bar gave it and now wouldn't (a note spelled anew, after one that was).
        var print = written is not null || (!tied && alter == implied && moved.Alter != given);
        if (print) after[(moved.Step, moved.Octave)] = moved.Alter;

        note.SetAttributeValue("pname", moved.Step.ToString().ToLowerInvariant());
        note.SetAttributeValue("oct", moved.Octave.ToString(CultureInfo.InvariantCulture));
        // Under an octave line it sounds the same octaves from where it's printed.
        if ((int?)note.Attribute("oct.ges") is { } sounds) note.SetAttributeValue("oct.ges", (moved.Octave + sounds - octave).ToString(CultureInfo.InvariantCulture));

        // A printed accidental where the note had one (on itself, or its accid); where none is printed, the one it sounds, where it
        // was kept (or on the note), whenever it isn't a natural its key and bar give anyway.
        var printedOn = note.Attribute("accid") is not null || accid is null ? note : accid;
        var soundsOn = accid?.Attribute("accid.ges") is not null ? accid : note;
        note.SetAttributeValue("accid.ges", null);
        accid?.SetAttributeValue("accid.ges", null);
        if (print) printedOn.SetAttributeValue("accid", Written(moved.Alter));
        else if (moved.Alter != 0 || given != 0) soundsOn.SetAttributeValue("accid.ges", Gestural(moved.Alter));
        return (Id((string?)note.Attribute(XNamespace.Xml + "id")), was, moved);
    }

    // A rest placed on the staff, by letter and octave or by line and space, moved as its voice's notes are.
    private static void MoveRest(XElement rest, int steps)
    {
        if ((string?)rest.Attribute("ploc") is { } ploc && Enum.TryParse<Step>(ploc, ignoreCase: true, out var step) && (int?)rest.Attribute("oloc") is { } oloc)
        {
            var at = Spelled.FromDiatonic(new Spelled(step, 0, oloc).Diatonic + steps);
            rest.SetAttributeValue("ploc", at.Step.ToString().ToLowerInvariant());
            rest.SetAttributeValue("oloc", at.Octave.ToString(CultureInfo.InvariantCulture));
        }
        if ((int?)rest.Attribute("loc") is { } loc) rest.SetAttributeValue("loc", (loc + steps).ToString(CultureInfo.InvariantCulture));
    }

    // A chord symbol's root ("B♭7") and its bass ("/D"), moved; one that doesn't start with a letter A–G ("N.C.") stays as it is.
    private static void MoveHarm(XElement harm, Key key, Transposition by)
    {
        var texts = harm.DescendantNodes().OfType<XText>().Where(t => t.Value.Length > 0).ToList();
        if (texts.Count == 0) return;

        // Sharps and flats as the symbol writes them: # and b, or ♯ and ♭ (as Verovio writes a MusicXML file's).
        var plain = texts.Any(t => t.Value.Contains('#') || PlainFlat().IsMatch(t.Value));
        string Name(Match m)
        {
            var alter = m.Groups["accid"].Value switch { "#" or "♯" => 1, "b" or "♭" => -1, "##" or "𝄪" => 2, "bb" or "𝄫" => -2, _ => 0 };
            var moved = by.Of(new Spelled(Enum.Parse<Step>(m.Groups["root"].Value), alter, 4), key);
            var sign = moved.Alter switch { 1 => plain ? "#" : "♯", -1 => plain ? "b" : "♭", 2 => plain ? "##" : "𝄪", -2 => plain ? "bb" : "𝄫", _ => "" };
            return $"{moved.Step}{sign}";
        }

        texts[0].Value = Root().Replace(texts[0].Value, m => m.Groups["lead"].Value + Name(m), 1);
        foreach (var text in texts) text.Value = Bass().Replace(text.Value, m => "/" + Name(m));
    }

    // An ornament's sharp or flat on the note above or below its note: that note's sound moved, on the letter next to where its
    // note went, so its accidental is whatever that takes.
    private static void MoveOrnament(XElement ornament, Spelled was, Spelled now, Transposition by)
    {
        foreach (var (name, side) in new[] { ("accidupper", 1), ("accidlower", -1) })
        {
            if ((string?)ornament.Attribute(name) is not { } accid) continue;

            var sounds = Spelled.FromDiatonic(was.Diatonic + side, Alter(accid)).Midi + by.Semitones;
            var letter = Spelled.FromDiatonic(now.Diatonic + side);
            ornament.SetAttributeValue(name, Written(Math.Clamp(sounds - letter.Midi, -2, 2)));
        }
    }

    private static int Staff(XElement control) =>
        int.TryParse(((string?)control.Attribute("staff") ?? "1").Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault(), out var staff) ? staff : 1;

    private static int? Fifths(string? sig) => sig switch
    {
        null or "mixed" => null,
        "0" or "" => 0,
        _ when int.TryParse(sig[..^1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var count) => sig.EndsWith('f') ? -count : count,
        _ => null,
    };

    private static int Alter(string? accid) => accid switch { "s" => 1, "f" => -1, "x" or "ss" => 2, "ff" => -2, _ => 0 };

    // As printed: a double sharp's sign is an x.
    private static string Written(int alter) => alter switch { 1 => "s", -1 => "f", 2 => "x", -2 => "ff", _ => "n" };

    // As it sounds, where nothing's printed.
    private static string Gestural(int alter) => alter switch { 1 => "s", -1 => "f", 2 => "ss", -2 => "ff", _ => "n" };

    private static string Id(string? reference) => reference?.TrimStart('#') ?? "";

    [GeneratedRegex("[A-G]b")]
    private static partial Regex PlainFlat();

    [GeneratedRegex(@"^(?<lead>\s*)(?<root>[A-G])(?<accid>##|bb|𝄪|𝄫|#|b|♯|♭)?")]
    private static partial Regex Root();

    [GeneratedRegex("/(?<root>[A-G])(?<accid>##|bb|𝄪|𝄫|#|b|♯|♭)?")]
    private static partial Regex Bass();
}
