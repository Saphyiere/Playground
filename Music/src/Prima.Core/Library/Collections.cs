using System.Xml.Linq;

namespace Prima.Core.Library;

/// <summary>
/// A collection engraved as one score (Chopin's 24 preludes, say) is split into its pieces at their final barlines, so a piece
/// opens, and its excerpts engrave, without the whole collection behind it. Each part keeps the file's header, numbered, and
/// starts with the key signature, metre and clefs in force where it begins. Only a long score whose pieces each end at a final
/// barline is split, and none of the parts may be a scrap. A score whose pieces each start with settings of their own and a
/// metronome mark (Kinderszenen, whose pieces end at repeats and double bars as often as at final barlines) is split there.
/// </summary>
public static class Collections
{
    private static readonly XNamespace Mei = "http://www.music-encoding.org/ns/mei";

    /// <summary>A collection is longer than this many bars, with at least this many pieces of at least this many bars.</summary>
    public const int LongBars = 600, FewestPieces = 12, ShortestPiece = 4;

    /// <summary>The MEI of each piece in <paramref name="mei"/>, in order; the MEI alone when it isn't a collection.</summary>
    public static IReadOnlyList<string> Split(string mei, int longBars = LongBars, int fewestPieces = FewestPieces, int shortestPiece = ShortestPiece)
    {
        var doc = XDocument.Parse(mei);
        var measures = doc.Descendants(Mei + "measure").ToList();
        var ends = measures.Select((m, i) => (string?)m.Attribute("right") == "end" ? i : -1).Where(i => i >= 0).ToList();
        if (ends.Count == 0 || ends[^1] != measures.Count - 1) ends.Add(measures.Count - 1);
        var firsts = ends.Prepend(-1).SkipLast(1).Select(e => e + 1).ToList();
        bool Whole(List<int> from, List<int> to) => from.Count < fewestPieces || to.Zip(from).Any(p => p.First - p.Second + 1 < shortestPiece);
        if (measures.Count > longBars && !Whole(firsts, ends)) return [.. firsts.Zip(ends).Select((range, i) => Part(doc, range.First, range.Second, i + 1))];

        // Or each piece starts with settings of its own (a key, a metre) and a metronome mark in its first bar.
        var settled = new HashSet<XElement>();
        var pending = false;
        foreach (var e in doc.Descendants().Where(e => e.Name == Mei + "scoreDef" || e.Name == Mei + "measure"))
        {
            if (e.Name == Mei + "scoreDef") pending = true;
            else if (pending && settled.Add(e)) pending = false;
        }
        var metronomes = doc.Descendants(Mei + "tempo").Where(t => t.Attribute("mm") is not null || t.Value.Contains('=') || t.Value.Any(c => c is >= '\uECA0' and <= '\uECBF'));
        var marked = metronomes.Select(t => t.Ancestors(Mei + "measure").FirstOrDefault()).OfType<XElement>().ToHashSet();
        var starts = measures.Select((m, i) => i == 0 || settled.Contains(m) && marked.Contains(m) ? i : -1).Where(i => i >= 0).ToList();
        var lasts = starts.Skip(1).Select(s => s - 1).Append(measures.Count - 1).ToList();
        return Whole(starts, lasts) ? [mei] : [.. starts.Zip(lasts).Select((range, i) => Part(doc, range.First, range.Second, i + 1))];
    }

    private static string Part(XDocument whole, int first, int last, int number)
    {
        var doc = new XDocument(whole);
        var measures = doc.Descendants(Mei + "measure").ToList();
        var (start, end) = (measures[first], measures[last]);

        // What's in force where the part starts: each staff's clef, key signature and metre, from every definition and clef
        // change before it.
        var staves = doc.Descendants(Mei + "staffDef").Select(s => (string?)s.Attribute("n")).OfType<string>().Distinct().ToList();
        var state = staves.ToDictionary(n => n, _ => new Dictionary<string, XElement>());
        string[] kinds = ["clef", "keySig", "meterSig"];
        foreach (var e in doc.Descendants().TakeWhile(e => e != start))
        {
            if (e.Name == Mei + "scoreDef")
            {
                foreach (var change in e.Elements().Where(c => kinds.Contains(c.Name.LocalName))) foreach (var n in staves) state[n][change.Name.LocalName] = change;
            }
            else if (e.Name == Mei + "staffDef" && (string?)e.Attribute("n") is { } n && state.TryGetValue(n, out var staff))
            {
                foreach (var change in e.Elements().Where(c => kinds.Contains(c.Name.LocalName))) staff[change.Name.LocalName] = change;
            }
            else if (e.Name == Mei + "clef" && e.Ancestors(Mei + "staff").FirstOrDefault()?.Attribute("n")?.Value is { } held && state.TryGetValue(held, out var changed))
            {
                changed["clef"] = e;
            }
        }

        // The collection's title page and staff labels (often the first piece's name) aren't the part's.
        var top = doc.Descendants(Mei + "score").First().Element(Mei + "scoreDef")!;
        top.Elements().Where(c => kinds.Contains(c.Name.LocalName)).Remove();
        top.Descendants().Where(e => e.Name == Mei + "pgHead" || e.Name == Mei + "pgFoot" || e.Name == Mei + "label" || e.Name == Mei + "labelAbbr").Remove();
        foreach (var staffDef in top.Descendants(Mei + "staffDef"))
        {
            if ((string?)staffDef.Attribute("n") is not { } n || !state.TryGetValue(n, out var held)) continue;
            staffDef.Elements().Where(c => kinds.Contains(c.Name.LocalName)).Remove();
            staffDef.AddFirst(kinds.Where(held.ContainsKey).Select(k =>
            {
                var copy = new XElement(held[k]);
                copy.Attribute(XNamespace.Xml + "id")?.Remove();
                return copy;
            }));
        }

        // Only the part's bars, with the breaks and changes between them; the sections and endings left empty go.
        foreach (var measure in measures.Where((_, i) => i < first || i > last)) measure.Remove();
        foreach (var e in doc.Descendants(Mei + "section").SelectMany(s => s.Elements()).Where(e => e.Name != Mei + "measure" && e.Name != Mei + "section" && e.Name != Mei + "ending").ToList())
        {
            if (e.IsBefore(start) || e.IsAfter(end)) e.Remove();
        }
        foreach (var empty in doc.Descendants().Where(e => (e.Name == Mei + "section" || e.Name == Mei + "ending") && !e.Descendants(Mei + "measure").Any()).ToList())
        {
            if (empty.Parent is not null) empty.Remove();
        }

        if (doc.Descendants(Mei + "titleStmt").Elements(Mei + "title").FirstOrDefault() is { } title) title.Value = $"{title.Value.Trim()}, No. {number}";
        return doc.ToString();
    }
}
