using System.Globalization;
using System.Net;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;

namespace Prima.Core.Music;

/// <summary>
/// Dynamics an edition typed into the lyrics line, made into the marks they are. The DCML's research editions (Beethoven's
/// sonatas, some of Chopin's mazurkas) give many of a piece's dynamics as syllables under its notes, one sign to a note: its
/// letters ("p", "sf", or the music font's own), "&lt;" or "&gt;" where a crescendo or diminuendo starts, "(" or ")" on each note
/// under one, "[" or "]" where one ends. Verovio prints them as words: stray signs under the staff. A piece whose every syllable
/// is such a sign, with no song's words among them, gets them as marks instead, on the notes that held them, below their staff:
/// <list type="bullet">
/// <item>letters are a dynamic ("(p)", an editor's, as it is);</item>
/// <item>a start whose notes or end are marked is a hairpin from it to the last of them. One without (a lone "&lt;") has no end to
/// draw it to, and is the word instead: "cresc." or "dim.". A diminuendo's notes or end marked after a crescendo's end, without
/// a start of its own ("&lt;" "[" ")"), starts where the crescendo ended;</item>
/// <item>"dolce" is a direction; a pedal's "*", and a sign with nothing to end or go on, are left out;</item>
/// <item>a sign the score marks already is a copy, and left out: the score's own dynamic, or crescendo or diminuendo, nearest it
/// (on either staff, at most four quarter notes away) says the same, or for a dynamic, another stands at that very moment. Each
/// of the score's marks stands for one copy, the nearest.</item>
/// </list>
/// The lyrics line goes. Music with no syllables, or with a song's words among them, comes back as it is.
/// </summary>
public static class LyricDynamics
{
    private static readonly XNamespace Mei = "http://www.music-encoding.org/ns/mei";
    private static readonly XNamespace Xml = "http://www.w3.org/XML/1998/namespace";

    /// <summary>How far from the score's own mark, in quarter notes, a sign can be its copy.</summary>
    public const double Near = 4;

    private static readonly Regex Verses = new(@"<verse\b[^>]*>(?<inner>[\s\S]*?)</verse>|<verse\b[^>]*/>", RegexOptions.Compiled);
    private static readonly Regex Tags = new("<[^>]+>", RegexOptions.Compiled);
    private static readonly Regex Letters = new(@"^(\(?(pppp|ppp|pp|p|mp|mf|ffff|fff|ff|f|fp|ffp|sfpp|sfp|sffz|sfz|sff|sf|fz|rfz|rf)\)?)+$", RegexOptions.Compiled);

    // The music font's dynamic letters, which a MuseScore file's lyrics can be typed in.
    private static readonly Dictionary<char, char> Glyphs = new()
    {
        ['\uE520'] = 'p', ['\uE521'] = 'm', ['\uE522'] = 'f', ['\uE523'] = 'r', ['\uE524'] = 's', ['\uE525'] = 'z', ['\uE526'] = 'n',
    };

    private enum Way { Up, Down }

    // A note's (or chord's) signs, where it is: its staff and layer, its bar, and its time from the piece's start in quarter notes.
    private sealed record Sign(XElement Note, string Staff, string Layer, XElement Bar, double Time, string[] Tokens);

    // A crescendo or diminuendo the signs mark, from the note it starts on to the last that marks it (none: a lone start).
    private sealed class Wedge(Way way, Sign start)
    {
        public Way Way { get; } = way;
        public Sign Start { get; } = start;
        public Sign? End { get; set; }
    }

    // What the signs come to, before copies are left out: a dynamic's letters, a word for a crescendo or diminuendo, a direction,
    // or a hairpin.
    private sealed record Made(Sign At, string Kind, string Text, Wedge? Wedge = null)
    {
        // What it's compared with among the score's own marks: a dynamic's letters with its dynamics, a hairpin or the word for one
        // with its crescendos and diminuendos.
        public string Family => Kind == "hairpin" || Text is "cresc." or "dim." ? "wedge" : "dynam";
    }

    /// <summary>The MEI with its signs typed as lyrics made into dynamics and hairpins, or as it is when it has none.</summary>
    public static string Mark(string mei)
    {
        if (!mei.Contains("<verse", StringComparison.Ordinal) || mei.Contains("<refrain", StringComparison.Ordinal)) return mei;
        // Every syllable a sign, read from the text before anything is parsed: a song's words leave the music as it is.
        var any = false;
        foreach (Match verse in Verses.Matches(mei))
        {
            if (Tokens(WebUtility.HtmlDecode(Tags.Replace(verse.Groups["inner"].Value, ""))) is null) return mei;
            any = true;
        }
        if (!any) return mei;

        XDocument document;
        try
        {
            document = XDocument.Parse(mei, LoadOptions.PreserveWhitespace);
        }
        catch (XmlException)
        {
            return mei;
        }
        // A syllable outside a verse is something else's: left as it is.
        if (document.Descendants(Mei + "syl").Any(s => s.Parent?.Name != Mei + "verse")) return mei;

        var (signs, own) = Read(document);
        var made = new List<Made>();
        foreach (var group in signs.GroupBy(s => (s.Staff, s.Layer)))
        {
            made.AddRange(Marks([.. group]));
        }

        var copies = Copies(made, own);
        document.Descendants(Mei + "verse").ToList().Remove();
        var ids = document.Descendants().Select(e => (string?)e.Attribute(Xml + "id")).OfType<string>().ToHashSet(StringComparer.Ordinal);
        var next = 0;
        string NewId()
        {
            string id;
            do id = $"lyric-mark-{++next}"; while (!ids.Add(id));
            return id;
        }

        foreach (var mark in made.Where(m => !copies.Contains(m)))
        {
            var at = mark.At;
            var element = new XElement(Mei + mark.Kind,
                new XAttribute(Xml + "id", NewId()), new XAttribute("staff", at.Staff), new XAttribute("startid", "#" + IdOf(at.Note, NewId)));
            if (mark.Kind == "hairpin")
            {
                element.Add(new XAttribute("endid", "#" + IdOf(mark.Wedge!.End!.Note, NewId)), new XAttribute("form", mark.Wedge.Way == Way.Up ? "cres" : "dim"));
            }
            element.Add(new XAttribute("place", "below"));
            if (mark.Kind == "dir") element.Add(new XElement(Mei + "rend", new XAttribute("fontstyle", "italic"), mark.Text));
            else if (mark.Kind == "dynam") element.Add(mark.Text);
            at.Bar.Add(element);
        }
        return document.ToString(SaveOptions.DisableFormatting);
    }

    // A verse's signs, in order, with the music font's letters read as letters; null when any of it isn't a sign.
    private static string[]? Tokens(string text)
    {
        var plain = string.Concat(text.Select(c => Glyphs.TryGetValue(c, out var letter) ? letter : c));
        var tokens = plain.Split((char[])[' ', '\t', '\r', '\n', '\u00A0'], StringSplitOptions.RemoveEmptyEntries);
        return tokens.All(t => t is "<" or ">" or "(" or ")" or "[" or "]" or "*" or "dolce" || Letters.IsMatch(t)) ? tokens : null;
    }

    // The signs on the notes, and the score's own dynamics, crescendos and diminuendos with their times, walking the bars in order.
    private static (List<Sign> Signs, List<(double Time, string Family, string Says)> Own) Read(XDocument document)
    {
        var signs = new List<Sign>();
        var own = new List<(double, string, string)>();
        var (count, unit) = (4.0, 4.0);
        var start = 0.0;
        foreach (var element in document.Descendants())
        {
            if (element.Name.LocalName is "scoreDef" or "staffDef" or "meterSig")
            {
                if (Number(element.Attribute("meter.count") ?? element.Attribute("count")) is { } c) count = c;
                if (Number(element.Attribute("meter.unit") ?? element.Attribute("unit")) is { } u and > 0) unit = u;
                continue;
            }
            if (element.Name != Mei + "measure") continue;

            // Where each note and chord starts, its id's time, and the bar's length: its longest layer, or the metre's.
            var times = new Dictionary<string, double>(StringComparer.Ordinal);
            var length = 0.0;
            foreach (var staff in element.Elements(Mei + "staff"))
            {
                var staffN = (string?)staff.Attribute("n") ?? "1";
                foreach (var layer in staff.Elements(Mei + "layer"))
                {
                    var (onsets, total) = Onsets(layer);
                    length = Math.Max(length, total);
                    foreach (var (note, quarters) in onsets)
                    {
                        foreach (var id in note.DescendantsAndSelf().Select(e => (string?)e.Attribute(Xml + "id")).OfType<string>()) times[id] = start + quarters;
                        var verses = note.Descendants(Mei + "verse").ToList();
                        if (verses.Count == 0) continue;
                        var tokens = verses.SelectMany(v => Tokens(v.Value) ?? []).ToArray();
                        signs.Add(new Sign(note, staffN, (string?)layer.Attribute("n") ?? "1", element, start + quarters, tokens));
                    }
                }
            }

            foreach (var mark in element.Elements().Where(e => e.Name.LocalName is "dynam" or "hairpin" or "dir"))
            {
                var time = (string?)mark.Attribute("startid") is { } startId && times.TryGetValue(startId.TrimStart('#'), out var t) ? t
                    : start + ((Number(mark.Attribute("tstamp")) ?? 1) - 1) * 4 / unit;
                var text = Regex.Replace(mark.Value, @"\s+", " ").Trim();
                var letters = text.Replace("(", "").Replace(")", "");
                if (mark.Name.LocalName == "hairpin") own.Add((time, "wedge", (string?)mark.Attribute("form") == "dim" ? "dim" : "cres"));
                else if (text.StartsWith("cresc", StringComparison.OrdinalIgnoreCase)) own.Add((time, "wedge", "cres"));
                else if (text.StartsWith("dim", StringComparison.OrdinalIgnoreCase) || text.StartsWith("decresc", StringComparison.OrdinalIgnoreCase)) own.Add((time, "wedge", "dim"));
                else if (mark.Name.LocalName == "dynam" && Letters.IsMatch(letters)) own.Add((time, "dynam", letters));
            }
            start += length > 0 ? length : count * 4 / unit;
        }
        return (signs, own);
    }

    // What one voice's signs come to, in its order: at each note its ends first, then the notes under a wedge, its dynamics and
    // words, and the wedges it starts.
    private static List<Made> Marks(List<Sign> signs)
    {
        var made = new List<Made>();
        Wedge? open = null;
        Wedge? closed = null;
        Sign? previous = null;

        void Close()
        {
            if (open is null) return;
            made.Add(open.End is null
                ? new Made(open.Start, "dynam", open.Way == Way.Up ? "cresc." : "dim.")
                : new Made(open.Start, "hairpin", "", open));
            closed = open.End is null ? null : open;
            open = null;
        }

        // A wedge the other way, marked right after one ended, without a start of its own, starts where that one ended.
        Wedge? AfterPeak(Way way) => closed is { } peak && peak.End == previous && peak.Way != way && previous is not null ? new Wedge(way, previous) : null;

        foreach (var sign in signs)
        {
            foreach (var token in sign.Tokens.Where(t => t is "[" or "]"))
            {
                if (open is not null)
                {
                    open.End = sign;
                    Close();
                }
                else if (AfterPeak(token == "[" ? Way.Up : Way.Down) is { } wedge)
                {
                    (open, wedge.End) = (wedge, sign);
                    Close();
                }
            }
            foreach (var token in sign.Tokens.Where(t => t is "(" or ")"))
            {
                if (open is not null) open.End = sign;
                else if (AfterPeak(token == "(" ? Way.Up : Way.Down) is { } wedge) (open, wedge.End) = (wedge, sign);
            }
            foreach (var token in sign.Tokens.Where(t => t is not ("<" or ">" or "(" or ")" or "[" or "]" or "*")))
            {
                if (token == "dolce")
                {
                    made.Add(new Made(sign, "dir", token));
                    continue;
                }
                // A dynamic ends the wedge before it, where its own marks end.
                Close();
                made.Add(new Made(sign, "dynam", token));
            }
            foreach (var token in sign.Tokens.Where(t => t is "<" or ">"))
            {
                Close();
                open = new Wedge(token == "<" ? Way.Up : Way.Down, sign);
            }
            previous = sign;
        }
        Close();
        return made;
    }

    // The marks the score has already: at the very moment of the score's own mark that says the same (for a dynamic, of any of its
    // dynamics), or else the copy of the score's own mark nearest it, of its family and within reach, when that says the same and
    // isn't another's copy (the nearest first).
    private static HashSet<Made> Copies(List<Made> made, List<(double Time, string Family, string Says)> own)
    {
        var copies = new HashSet<Made>();
        var pairs = new List<(Made Mark, int Own, double Distance)>();
        foreach (var mark in made.Where(m => m.Kind != "dir"))
        {
            var says = Says(mark);
            var near = own.Select((o, i) => (o, i, Distance: Math.Abs(o.Time - mark.At.Time))).Where(x => x.o.Family == mark.Family && x.Distance <= Near).ToList();
            if (near.Count == 0) continue;
            var nearest = near.Min(x => x.Distance);
            var same = near.Where(x => Math.Abs(x.Distance - nearest) < 1e-6 && x.o.Says == says).Select(x => x.i).ToList();
            if (same.Count > 0) pairs.Add((mark, same[0], nearest));
            else if (nearest < 1e-6 && mark.Family == "dynam") copies.Add(mark);
        }

        // At the very moment, a copy whether or not another sign there was one too (a dynamic typed under both staves); a step away,
        // only of a mark no nearer sign is the copy of (an accent the next beat is an accent of its own).
        var taken = new HashSet<int>();
        foreach (var (mark, index, distance) in pairs.OrderBy(p => p.Distance))
        {
            if (taken.Add(index) || distance < 1e-6) copies.Add(mark);
        }
        return copies;
    }

    // What a mark says, to compare with the score's own: a dynamic's letters, or the way a crescendo or diminuendo goes.
    private static string Says(Made mark) => mark.Kind switch
    {
        "hairpin" => mark.Wedge!.Way == Way.Up ? "cres" : "dim",
        _ when mark.Text == "cresc." => "cres",
        _ when mark.Text == "dim." => "dim",
        _ => mark.Text.Replace("(", "").Replace(")", ""),
    };

    private static string IdOf(XElement note, Func<string> newId)
    {
        if ((string?)note.Attribute(Xml + "id") is { } id) return id;
        id = newId();
        note.SetAttributeValue(Xml + "id", id);
        return id;
    }

    private static double? Number(XAttribute? attribute) =>
        double.TryParse((string?)attribute, NumberStyles.Float, CultureInfo.InvariantCulture, out var value) ? value : null;

    // Where each note and chord of a layer starts, in quarter notes from the bar's start, through beams, tuplets and grace groups
    // (grace notes take no time), and the layer's length.
    private static (List<(XElement Note, double Quarters)> Onsets, double Length) Onsets(XElement layer)
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
                        var (num, numbase) = (Number(child.Attribute("num")) ?? 3, Number(child.Attribute("numbase")) ?? 2);
                        Walk(child, ratio * numbase / num, grace);
                        break;
                    case "note" or "chord":
                        onsets.Add((child, at));
                        if (!grace && child.Attribute("grace") is null) at += Quarters(child) * ratio;
                        break;
                    case "rest" or "space":
                        if (!grace) at += Quarters(child) * ratio;
                        break;
                }
            }
        }
        Walk(layer, 1, false);
        return (onsets, at);
    }

    private static double Quarters(XElement element)
    {
        var dur = (string?)element.Attribute("dur") ?? (string?)element.Elements(Mei + "note").FirstOrDefault()?.Attribute("dur") ?? "4";
        var value = dur switch { "long" => 16.0, "breve" => 8.0, _ => double.TryParse(dur, NumberStyles.Float, CultureInfo.InvariantCulture, out var d) && d > 0 ? 4 / d : 1 };
        var dots = (int?)element.Attribute("dots") ?? 0;
        return value * (2 - Math.Pow(0.5, dots));
    }
}
