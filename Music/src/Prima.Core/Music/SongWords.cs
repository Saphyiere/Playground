using System.Globalization;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;

namespace Prima.Core.Music;

/// <summary>
/// A song's words in its MEI, made ready for the sheet. Verovio prints lyrics (verses of syllables, one under each note) a line
/// for each verse number, and spaces the notes so the syllables on a line never run into each other. Some files put words where
/// they'd still collide: two voices on a staff each with lyrics numbered alike, which Verovio puts on one line; verses an
/// exporter names ("part1verse1", "part1verse2") rather than numbers, which Verovio reads as one verse; or a song's lines typed as
/// text over or under the music (directions), which Verovio places without spacing the notes for them, so they pile up on each
/// other and crush their bars into a corner of the line. <see cref="Prepare"/> gives each voice's lyrics and each verse a line of
/// its own, and moves words typed as text onto a line under the notes they start at. A whole verse typed as text (a direction as
/// long as a verse, printed after the music) isn't for playing and is left off; so are all the words when they're hidden.
/// </summary>
public static class SongWords
{
    private static readonly XNamespace Mei = "http://www.music-encoding.org/ns/mei";
    private static readonly XNamespace Xml = "http://www.w3.org/XML/1998/namespace";

    /// <summary>Longer than this, a direction is a whole verse typed as text, not words under the notes or a player's direction.</summary>
    public const int VerseLength = 60;

    private static readonly Regex Directions = new(@"<dir\b(?:[^>]*?/>|[^>]*>(?<text>[\s\S]*?)</dir>)", RegexOptions.Compiled);
    private static readonly Regex Tags = new("<[^>]+>", RegexOptions.Compiled);
    private static readonly Regex Space = new(@"\s+", RegexOptions.Compiled);

    /// <summary>Whether the music has a song's words to show: lyrics, or its lines typed as text.</summary>
    public static bool Has(string mei) => HasLyrics(mei) || Scan(mei).Typed;

    /// <summary>
    /// The MEI for the sheet: with the song's words <paramref name="shown"/>, each voice's lyrics and each verse on lines of their
    /// own and words typed as text on a line under the notes they start at; or with no words at all. A whole verse typed as text is
    /// left off either way. Music with nothing of the kind comes back as it is.
    /// </summary>
    public static string Prepare(string mei, bool shown)
    {
        var scan = Scan(mei);
        var lyrics = HasLyrics(mei);
        if (!lyrics && !scan.Typed && scan.Verses == 0) return mei;

        XDocument document;
        try
        {
            document = XDocument.Parse(mei, LoadOptions.PreserveWhitespace);
        }
        catch (XmlException)
        {
            // What isn't MEI goes to Verovio as it is, which draws nothing of it.
            return mei;
        }
        var directions = document.Descendants(Mei + "dir").Select(d => (Dir: d, Text: TextOf(d))).ToList();
        directions.Where(d => d.Text.Length > VerseLength).Select(d => d.Dir).Remove();
        var typed = scan.Typed ? directions.Where(d => IsLine(d.Text)).ToList() : [];

        if (!shown)
        {
            typed.Select(d => d.Dir).Remove();
            document.Descendants().Where(e => e.Name == Mei + "verse" || e.Name == Mei + "refrain" || e.Name == Mei + "syl" && e.Parent?.Name.LocalName is "note" or "chord").Remove();
            return document.ToString(SaveOptions.DisableFormatting);
        }

        OwnLines(document);
        if (typed.Count > 0) OntoTheNotes(document, [.. typed.Select(t => (t.Dir, t.Text))]);
        return document.ToString(SaveOptions.DisableFormatting);
    }

    private static bool HasLyrics(string mei) => mei.Contains("<verse", StringComparison.Ordinal) || mei.Contains("<refrain", StringComparison.Ordinal) || mei.Contains("<syl", StringComparison.Ordinal);

    // The directions, read from the text without parsing it: how many are whole verses, and whether the music's lines of words are
    // typed as text. They are when there are many directions of a few words (four at least, and one every four bars), as a song's
    // lines run through it; a player's directions (dolce, a tempo, poco rit.) are a word or two, or few.
    private static (int Verses, bool Typed) Scan(string mei)
    {
        if (!mei.Contains("<dir", StringComparison.Ordinal)) return (0, false);

        var texts = Directions.Matches(mei).Select(m => Space.Replace(System.Net.WebUtility.HtmlDecode(Tags.Replace(m.Groups["text"].Value, t => t.Value.StartsWith("<lb", StringComparison.Ordinal) ? " " : "")), " ").Trim()).ToList();
        var lines = texts.Count(IsLine);
        var bars = Regex.Count(mei, @"<measure\b");
        return (texts.Count(t => t.Length > VerseLength), lines >= 4 && lines * 4 >= bars);
    }

    // A line of a song's words: three words or more, shorter than a verse. Dashes between a word's parts ("ri - tar - dan - do")
    // aren't words.
    private static bool IsLine(string text) => text.Length <= VerseLength && text.Split(' ', StringSplitOptions.RemoveEmptyEntries).Count(w => w.Any(char.IsLetter)) >= 3;

    // A direction's words, a line break between two of its lines read as a space.
    private static string TextOf(XElement element) =>
        Space.Replace(string.Concat(element.DescendantNodes().Select(n => n is XText text ? text.Value : n is XElement { Name.LocalName: "lb" } ? " " : "")), " ").Trim();

    private static int N(XElement verse) => int.TryParse((string?)verse.Attribute("n"), NumberStyles.Integer, CultureInfo.InvariantCulture, out var n) && n > 0 ? n : 1;

    private static void SetN(XElement verse, int n) => verse.SetAttributeValue("n", n.ToString(CultureInfo.InvariantCulture));

    private static string StaffOf(XElement element) => (string?)element.Ancestors(Mei + "staff").FirstOrDefault()?.Attribute("n") ?? "1";

    private static int LayerOf(XElement element) => (int?)element.Ancestors(Mei + "layer").FirstOrDefault()?.Attribute("n") ?? 1;

    // Each verse a line of its own: verses numbered alike on one note (an exporter's names, all read as 1) take the next numbers
    // free on it, in order; then on each staff, a second voice's lyrics go on the lines after the first voice's, and so on, so two
    // voices' syllables never share a line.
    private static void OwnLines(XDocument document)
    {
        var verses = document.Descendants(Mei + "verse").ToList();
        foreach (var holder in verses.GroupBy(v => v.Parent))
        {
            var taken = new HashSet<int>();
            foreach (var verse in holder)
            {
                var n = N(verse);
                while (!taken.Add(n)) n++;
                SetN(verse, n);
            }
        }

        foreach (var staff in verses.GroupBy(StaffOf))
        {
            var below = 0;
            foreach (var layer in staff.GroupBy(LayerOf).OrderBy(l => l.Key))
            {
                var lines = layer.Max(N);
                if (below > 0) foreach (var verse in layer) SetN(verse, N(verse) + below);
                below += lines;
            }
        }
    }

    // Lines of words typed as text, each moved onto a line of its own under its staff's notes, as one syllable on the note it starts
    // at: the note it names, or the first note at or after its beat in the staff's first voice (the last before it, at a bar's end).
    // Verovio then spaces the notes for it, as it does for lyrics. One with no note to go on stays as it was.
    private static void OntoTheNotes(XDocument document, List<(XElement Dir, string Text)> typed)
    {
        var byId = document.Descendants().Where(e => e.Name.LocalName is "note" or "chord").Select(e => (Id: (string?)e.Attribute(Xml + "id"), Element: e))
            .Where(e => e.Id is not null).GroupBy(e => e.Id!).ToDictionary(g => g.Key, g => g.First().Element);
        var lines = document.Descendants(Mei + "verse").GroupBy(StaffOf).ToDictionary(g => g.Key, g => g.Max(N));
        var unit = 4.0;
        var places = typed.Select(t => t.Dir).ToHashSet();
        var found = new List<(XElement Dir, string Text, XElement Note, string Staff)>();
        foreach (var element in document.Descendants().ToList())
        {
            if (element.Name.LocalName is "scoreDef" or "staffDef" or "meterSig" && (double?)(element.Attribute("meter.unit") ?? element.Attribute("unit")) is { } u and > 0) unit = u;
            if (!places.Contains(element)) continue;

            var staff = ((string?)element.Attribute("staff") ?? "1").Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? "1";
            var start = ((string?)element.Attribute("startid"))?.TrimStart('#');
            var note = start is not null && byId.TryGetValue(start, out var named) ? named : null;
            if (note is null && element.Ancestors(Mei + "measure").FirstOrDefault() is { } measure)
            {
                var layer = measure.Elements(Mei + "staff").FirstOrDefault(s => ((string?)s.Attribute("n") ?? "1") == staff)?.Elements(Mei + "layer").FirstOrDefault();
                var beat = double.TryParse((string?)element.Attribute("tstamp"), NumberStyles.Float, CultureInfo.InvariantCulture, out var t) ? t : 1;
                var at = (beat - 1) * 4 / unit;
                var notes = layer is null ? [] : Onsets(layer).Where(o => o.Element.Name.LocalName is "note" or "chord").ToList();
                note = notes.FirstOrDefault(o => o.Quarters >= at - 1e-6).Element ?? notes.LastOrDefault().Element;
            }
            if (note?.Parent?.Name == Mei + "chord") note = note.Parent;
            if (note is not null) found.Add((element, typed.First(t => t.Dir == element).Text, note, staff));
        }

        foreach (var (dir, text, note, staff) in found)
        {
            var n = lines.GetValueOrDefault(staff) + 1;
            note.Add(new XElement(Mei + "verse", new XAttribute("n", n.ToString(CultureInfo.InvariantCulture)), new XElement(Mei + "syl", text)));
            dir.Remove();
        }
    }

    // Where each note, chord and rest of a layer starts, in quarter notes from the bar's start, through beams, tuplets and grace
    // groups; grace notes take no time.
    private static List<(XElement Element, double Quarters)> Onsets(XElement layer)
    {
        var onsets = new List<(XElement, double)>();
        var at = 0.0;
        void Walk(XElement parent, double ratio, bool grace)
        {
            foreach (var child in parent.Elements())
            {
                switch (child.Name.LocalName)
                {
                    case "beam" or "graceGrp" or "fTrem" or "bTrem" or "ftrem" or "btrem":
                        Walk(child, ratio, grace || child.Name.LocalName == "graceGrp");
                        break;
                    case "tuplet":
                        var (num, numbase) = ((double?)child.Attribute("num") ?? 3, (double?)child.Attribute("numbase") ?? 2);
                        Walk(child, ratio * numbase / num, grace);
                        break;
                    case "note" or "chord" or "rest" or "space":
                        onsets.Add((child, at));
                        if (!grace && child.Attribute("grace") is null) at += Quarters(child) * ratio;
                        break;
                }
            }
        }
        Walk(layer, 1, false);
        return onsets;
    }

    private static double Quarters(XElement element)
    {
        var dur = (string?)element.Attribute("dur") ?? (string?)element.Elements(Mei + "note").FirstOrDefault()?.Attribute("dur") ?? "4";
        var value = dur switch { "long" => 16.0, "breve" => 8.0, _ => double.TryParse(dur, NumberStyles.Float, CultureInfo.InvariantCulture, out var d) && d > 0 ? 4 / d : 1 };
        var dots = (int?)element.Attribute("dots") ?? 0;
        return value * (2 - Math.Pow(0.5, dots));
    }
}
