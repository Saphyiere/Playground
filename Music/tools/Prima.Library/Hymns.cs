using System.Diagnostics;
using System.IO.Compression;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Prima.Core.Library;

namespace Prima.Library;

/// <summary>
/// The Open Hymnal (openhymnal.org, edited by Brian J. Dumont), for the local collection: about 350 hymns as ABC, most set the way
/// a hymnal prints them, soprano and alto on the upper staff and tenor and bass on the lower, which a pianist plays as written.
/// Each file gives its terms part by part (tune, setting, words, translation); only the hymns public domain in every part are
/// kept. The app's Verovio reads only one voice of an ABC file, so abc2svg (npm, LGPL/GPL, used only by the tool) writes each hymn
/// as MEI, its verses under the notes, which Prepare.cs engraves with the app's Verovio like any other piece.
/// </summary>
internal static class Hymns
{
    public const string Name = "Open Hymnal";
    private const string Site = "http://openhymnal.org/";
    private const string Licence = "Public domain";
    private const string LicenceUrl = Site + "copying.html";

    // Most of the files are Latin-1 ("Crüger", "bless-èd"), a few UTF-8.
    private static readonly Encoding Utf8 = new UTF8Encoding(false, throwOnInvalidBytes: true);

    public static async Task<List<BundledPiece>> Fetch(HttpClient http, string cache, string output, string modules, string root)
    {
        Console.WriteLine("Open Hymnal: the current edition's ABC…");
        var folder = Path.Combine(cache, "hymns");
        Directory.CreateDirectory(folder);
        if (await Edition(http, folder) is not { } zip)
        {
            Console.WriteLine("Open Hymnal: no edition to read, so left out.");
            return [];
        }
        var edition = Regex.Match(Path.GetFileName(zip), @"OpenHymnal([\d.]+)-abc").Groups[1].Value;

        var chosen = new List<(BundledPiece Piece, string Abc, string Tune)>();
        var metres = new List<string>();
        var hymns = 0;
        using (var archive = ZipFile.OpenRead(zip))
        {
            foreach (var entry in archive.Entries.Where(e => e.Name.EndsWith(".abc", StringComparison.OrdinalIgnoreCase)).OrderBy(e => e.Name, StringComparer.Ordinal))
            {
                hymns++;
                using var stream = entry.Open();
                using var bytes = new MemoryStream();
                await stream.CopyToAsync(bytes);
                var abc = Text(bytes.ToArray());

                // The header, up to the key: T: its title (the first; more say what else it's called), C: its credits and then its
                // terms ("C: copyright: public domain."), %OHCOMPOSER its tune's composer.
                var header = abc[..(Regex.Match(abc, @"^K:", RegexOptions.Multiline) is { Success: true } k ? k.Index : abc.Length)];
                string[] Lines(string field) => [.. Regex.Matches(header, $@"^{field}[ \t]*(.*?)\s*$", RegexOptions.Multiline).Select(m => Regex.Replace(m.Groups[1].Value, @"\s+", " "))];
                var credits = Lines("C:").ToList();
                var at = credits.FindIndex(c => c.Contains("copyright", StringComparison.OrdinalIgnoreCase));
                if (at < 0 || !PublicDomain(string.Join(" ", credits[at..]))) continue;

                var stem = Path.GetFileNameWithoutExtension(entry.Name);
                var id = $"hymn-{Regex.Replace(stem, "[^A-Za-z0-9]+", "-").Trim('-').ToLowerInvariant()}";
                var title = Lines("T:").FirstOrDefault(t => t.Length > 0) ?? stem.Replace('_', ' ');

                // One that changes its metre partway ("One Thing's Needful" goes into 3/4) stays out: tomei.js doesn't write the
                // change, so its bars would print under the first metre. Going into none ([M:none]) prints nothing anyway.
                if (Regex.IsMatch(abc[header.Length..], @"\[M:\s*(?!none)[^\]\s]|^M:\s*(?!none)\S", RegexOptions.Multiline | RegexOptions.IgnoreCase))
                {
                    metres.Add(title);
                    continue;
                }
                var tune = Regex.Match(string.Join(" ", credits[..at]), @"Music[^:']*:\s*'([^']+)'").Groups[1].Value.Replace('`', '\'');
                var composer = Composer(Lines("%OHCOMPOSER").FirstOrDefault() ?? "");
                var credit = $"{string.Join(" ", credits[..at])} From the Open Hymnal ({edition}), edited by Brian J. Dumont; public domain.".TrimStart();
                var piece = new BundledPiece(id, title, composer, "Hymns", "mei", $"pieces/{id}.mei", null, "", Name, $"{Site}Lyrics/{Uri.EscapeDataString(stem)}.html",
                    Licence, LicenceUrl, credit, Tier.A, null);
                chosen.Add((piece, abc, tune));
            }
        }
        Console.WriteLine($"Open Hymnal: {edition}, {hymns} hymns; {chosen.Count + metres.Count} public domain in tune, setting, words and translation, the " +
            "rest under copyright in some part (most of them free to copy for worship, which isn't enough here).");
        foreach (var title in metres) Console.WriteLine($"  Left out {title}: it changes its metre partway, which abc2svg's MEI leaves out.");

        // A hymn set twice to the same tune is the same music (only its verses differ: "From All Thy Saints in Warfare" has two
        // sets of saints' days), so the first stays. A title set to two tunes takes its tune's name: "Blest Be the Tie That Binds
        // (Dennis)", "A Mighty Fortress Is Our God (Ein Feste Burg, Rhythmic)", "Christ the Lord Is Risen Today (Lyra, Llanfair)".
        chosen = [.. chosen.GroupBy(c => (c.Piece.Title, c.Tune.Length > 0 ? c.Tune : c.Piece.Id)).Select(g => g.First())];
        var shared = chosen.GroupBy(c => c.Piece.Title).Where(g => g.Count() > 1).Select(g => g.Key).ToHashSet();
        string Named(string title, string tune)
        {
            var name = Regex.Replace(tune, @"\s*\(([^)]*)\)", ", $1");
            return title.EndsWith(')') ? $"{title[..^1]}, {name})" : $"{title} ({name})";
        }
        chosen = [.. chosen.Select(c => shared.Contains(c.Piece.Title) && c.Tune.Length > 0 ? c with { Piece = c.Piece with { Title = Named(c.Piece.Title, c.Tune) } } : c)];

        // abc2svg writes each as MEI, a hymn a run (its MEI backend reads one tune a run), a few at once: abc2svg-1.js, cmdline.js and
        // tomei.js from the package and the tool's abc-to-mei.js after them in one script, as the package's own abcnode does. Its
        // folder holds no default.abc, which abc2svg would read first.
        var script = Path.Combine(Path.GetDirectoryName(modules)!, "abc-to-mei.js");
        var package = Path.Combine(modules, "abc2svg");
        await File.WriteAllTextAsync(script, string.Concat(await Task.WhenAll(new[] { "abc2svg-1.js", "cmdline.js", "tomei.js" }.Select(f => File.ReadAllTextAsync(Path.Combine(package, f)))
            .Append(File.ReadAllTextAsync(Path.Combine(root, "Music", "tools", "Prima.Library", "abc-to-mei.js"))))));
        var sources = Path.Combine(folder, "abc");
        Directory.CreateDirectory(sources);
        Console.WriteLine($"Open Hymnal: {chosen.Count} hymns; abc2svg writes each as MEI (half a minute)…");
        var failed = new List<string>();
        await Parallel.ForEachAsync(chosen, new ParallelOptions { MaxDegreeOfParallelism = Math.Clamp(Environment.ProcessorCount, 1, 4) }, async (c, _) =>
        {
            var abc = Path.Combine(sources, c.Piece.Id + ".abc");
            await File.WriteAllTextAsync(abc, OneFont(c.Abc));
            var start = new ProcessStartInfo("node", [script, abc]) { RedirectStandardOutput = true, RedirectStandardError = true, WorkingDirectory = Path.GetDirectoryName(script)!, StandardOutputEncoding = Encoding.UTF8 };
            using var node = Process.Start(start)!;
            var mei = node.StandardOutput.ReadToEndAsync();
            var errors = node.StandardError.ReadToEndAsync();
            using var limit = new CancellationTokenSource(TimeSpan.FromMinutes(1));
            try
            {
                await node.WaitForExitAsync(limit.Token);
            }
            catch (OperationCanceledException)
            {
                node.Kill();
                await node.WaitForExitAsync();
            }
            var file = Path.Combine(output, c.Piece.File);
            if (node.ExitCode == 0 && Tidy(await mei) is { } tidy)
            {
                await File.WriteAllTextAsync(file, tidy);
            }
            else
            {
                if (File.Exists(file)) File.Delete(file);
                var why = Regex.Match(await errors, @"javascript error: [^\r\n]*").Value;
                lock (failed) failed.Add($"{c.Piece.Title}{(why.Length > 0 ? $" ({why})" : "")}");
            }
        });
        foreach (var hymn in failed.Order(StringComparer.Ordinal)) Console.WriteLine($"  Left out {hymn}: abc2svg couldn't write it as MEI.");

        return [.. chosen.Where(c => File.Exists(Path.Combine(output, c.Piece.File))).Select(c => c.Piece with { Sha256 = Pdmx.Hash(Path.Combine(output, c.Piece.File)) })];
    }

    // The current edition's ABC, one zip, as the site's home page links it (OpenHymnal2026.11-abc.zip): robots.txt bars only the
    // older editions' files, which it names. Kept in the cache, so a run fetches only a new edition. Without the site, the edition
    // already here.
    private static async Task<string?> Edition(HttpClient http, string folder)
    {
        string? name = null;
        try
        {
            name = Regex.Match(await http.GetStringAsync(Site), @"href=""(OpenHymnal[\d.]+-abc\.zip)""").Groups[1].Value is { Length: > 0 } found ? found : null;
            var robots = await http.GetStringAsync(Site + "robots.txt");
            if (name is not null && Regex.Matches(robots, @"^\s*Disallow:\s*(\S+)", RegexOptions.Multiline | RegexOptions.IgnoreCase).Any(m => $"/{name}".StartsWith(m.Groups[1].Value, StringComparison.Ordinal)))
            {
                Console.WriteLine($"Open Hymnal: robots.txt bars {name} now.");
                return null;
            }
        }
        catch (HttpRequestException e)
        {
            Console.WriteLine($"Open Hymnal: the site couldn't be read ({e.Message}), so the edition already here.");
        }

        if (name is null) return Directory.EnumerateFiles(folder, "OpenHymnal*-abc.zip").Order(StringComparer.Ordinal).LastOrDefault();
        var zip = Path.Combine(folder, name);
        if (!File.Exists(zip)) await Pdmx.Download(http, Site + name, zip);
        return zip;
    }

    private static string Text(byte[] bytes)
    {
        try
        {
            return Utf8.GetString(bytes).TrimStart('\uFEFF');
        }
        catch (DecoderFallbackException)
        {
            return Encoding.Latin1.GetString(bytes);
        }
    }

    // Public domain in every part: the terms say so, and nothing in them is under copyright or reserved ("Words: Copyright 2009,
    // … All other rights reserved", "Setting: CPDL"). Most of those may be copied freely for worship, which isn't enough for this.
    private static bool PublicDomain(string terms) =>
        terms.Contains("public domain", StringComparison.OrdinalIgnoreCase) &&
        !Regex.IsMatch(terms, @"copyright\W*\d|rights reserved|freely reproduced|CPDL", RegexOptions.IgnoreCase);

    // The tune's composer as %OHCOMPOSER names it: "Monk, William Henry (1823-1889)" is William Henry Monk, and of several
    // ("Genevan Psalter;Bourgeois, Louis (1510-1561)") the first person. A tune that's no one's ("English", "from Klug's Geistliche
    // Lieder, Wittenberg, 1533", "Proper Sarum Melody") is Traditional, as the Library lists its composers by surname; its credit
    // says where it's from.
    private static string Composer(string field)
    {
        foreach (var name in field.Split(';', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
        {
            var person = Regex.Match(name, @"^(?!from\s)([^,()\d]+),\s*([^,()\d]+?)\s*(\([^)]*\))?$");
            if (person.Success) return $"{person.Groups[2].Value} {person.Groups[1].Value}" switch
            {
                // As the rest of the collection names them.
                "Franz Joseph Haydn" => "Joseph Haydn",
                "George Frederick Handel" => "George Frideric Handel",
                var named => named,
            };
        }
        return "Traditional";
    }

    private static readonly XNamespace Mei = "http://www.music-encoding.org/ns/mei";

    // The verses in one font. "$1D$2" in a verse is a D in font 1, which these files make Symbol: Δ, marking a doxology. abc2svg
    // would write the change of font into the MEI as SVG, and Verovio would drop it with the words in it.
    private static string OneFont(string abc)
    {
        var symbol = Regex.IsMatch(abc, @"^%%setfont-1\s+Symbol\b", RegexOptions.Multiline);
        return Regex.Replace(abc, @"^w:.*$", line => Regex.Replace(line.Value, @"\$(\d)([^$]*)", m => m.Groups[1].Value == "1" && symbol ? Greek(m.Groups[2].Value) : m.Groups[2].Value),
            RegexOptions.Multiline);
    }

    // Letters in the Symbol font, which sets Latin letters' places with Greek ones.
    private static string Greek(string text) => string.Concat(text.Select(c => c switch
    {
        >= 'A' and <= 'Z' => "ΑΒΧΔΕΦΓΗΙϑΚΛΜΝΟΠΘΡΣΤΥςΩΞΨΖ"[c - 'A'],
        >= 'a' and <= 'z' => "αβχδεφγηιϕκλμνοπθρστυϖωξψζ"[c - 'a'],
        _ => c,
    }));

    // abc2svg's MEI put right, or null when it isn't MEI:
    // - An "&" in a credit is escaped (it writes "Verses 1 & 4" as it is).
    // - A dotted note gets its own value: tomei.js names a length by its lowest bit, so it writes a dotted half (three quarters)
    //   as a dotted quarter, each dot halving the value it should have named.
    // - A bar whose notes don't fill its metre says so (metcon="false"), as the Core's own MEI does, so the Core takes its length
    //   from them: a hymn written without a time signature (Old 100th's bars are of six quarters and of seven) is read as 4/4,
    //   which would lay a long bar's last notes over the next bar's first.
    // - A hymn with one verse has its syllables straight under the notes, where Verovio draws none: they go into verse 1.
    // - Each verse's syllables say where they fall in their words: it marks a syllable that a hyphen follows (con="d"), but without
    //   a word's start and end (wordpos) Verovio draws no hyphens.
    // - Its metronome marks get their beat: it writes quarter notes a minute (mm="100") without the unit, so Prima would read none.
    // - The fermata under the bass, the inverted one, goes below the staff, where it writes it above.
    private static string? Tidy(string mei)
    {
        XDocument doc;
        try
        {
            doc = XDocument.Parse(Regex.Replace(mei, @"&(?!#\d+;|#x[0-9A-Fa-f]+;|[A-Za-z]+;)", "&amp;"));
        }
        catch (System.Xml.XmlException)
        {
            return null;
        }
        if (doc.Root?.Name != Mei + "mei") return null;

        foreach (var dotted in doc.Descendants().Where(e => e.Attribute("dots") is not null && e.Attribute("dur") is not null))
        {
            var value = Array.IndexOf(Values, (string)dotted.Attribute("dur")!);
            var dots = (int)dotted.Attribute("dots")!;
            if (value >= dots) dotted.SetAttributeValue("dur", Values[value - dots]);
        }

        // A bar with nothing in it, which tomei writes where a line ends with a bar line and the next starts with a repeat ("| |:"),
        // goes: the Core would read it as a bar's rest. Its repeat sign goes on the bar after it, where the Core reads one.
        foreach (var empty in doc.Descendants(Mei + "measure").Where(m => !m.Descendants().Any(e => e.Name.LocalName is "note" or "chord" or "rest" or "space" or "mRest" or "mSpace")).ToList())
        {
            var right = (string?)empty.Attribute("right");
            if (right is "rptstart" or "rptboth") empty.ElementsAfterSelf(Mei + "measure").FirstOrDefault()?.SetAttributeValue("left", "rptstart");
            if (right is "rptend" or "rptboth") empty.ElementsBeforeSelf(Mei + "measure").LastOrDefault()?.SetAttributeValue("right", "rptend");
            empty.Remove();
        }

        // A bar the hymnal carries over a line break (a phrase ending on its third beat, the next starting on its fourth) is one bar
        // again: tomei writes each part as a bar of its own, with a bar line between that a reader would take for a downbeat.
        var bars = Bars(doc);
        for (var i = 1; i + 2 < bars.Count; i++)
        {
            var (first, second) = (bars[i], bars[i + 1]);
            if (!(first.Length < first.Bar - 1e-6 && second.Length < second.Bar - 1e-6 && Math.Abs(first.Length + second.Length - first.Bar) < 1e-6)) continue;
            if (first.Measure.Attribute("right") is not null || second.Measure.Attribute("left") is not null || second.Measure.Parent != first.Measure.Parent) continue;
            if (first.Measure.ElementsAfterSelf().TakeWhile(e => e != second.Measure).Any(e => e.Name != Mei + "sb")) continue;
            var layers = first.Measure.Elements(Mei + "staff").SelectMany(s => s.Elements(Mei + "layer")).ToDictionary(l => ((string?)l.Parent!.Attribute("n"), (string?)l.Attribute("n")));
            var others = second.Measure.Elements(Mei + "staff").SelectMany(s => s.Elements(Mei + "layer")).ToList();
            if (!others.All(l => layers.ContainsKey(((string?)l.Parent!.Attribute("n"), (string?)l.Attribute("n"))))) continue;

            foreach (var layer in others) layers[((string?)layer.Parent!.Attribute("n"), (string?)layer.Attribute("n"))].Add(layer.Elements());
            first.Measure.Add(second.Measure.Elements().Where(e => e.Name != Mei + "staff"));
            if (second.Measure.Attribute("right") is { } end) first.Measure.SetAttributeValue("right", end.Value);
            second.Measure.Remove();
            bars.RemoveAt(i + 1);
            bars[i] = first with { Length = first.Bar };
        }

        // Then a bar that doesn't fill its metre says so (one of a whole bar's rest, mRest, has no length here, and fills it).
        foreach (var (measure, length, bar) in bars)
        {
            if (length > 0 && Math.Abs(length - bar) > 1e-6) measure.SetAttributeValue("metcon", "false");
        }

        foreach (var syl in doc.Descendants(Mei + "syl").Where(s => s.Parent?.Name != Mei + "verse").ToList()) syl.ReplaceWith(new XElement(Mei + "verse", new XAttribute("n", "1"), new XElement(syl)));
        var verses = doc.Descendants(Mei + "syl").GroupBy(s => (
            Staff: (string?)s.Ancestors(Mei + "staff").FirstOrDefault()?.Attribute("n"),
            Layer: (string?)s.Ancestors(Mei + "layer").FirstOrDefault()?.Attribute("n"),
            Verse: s.Parent?.Name == Mei + "verse" ? (string?)s.Parent.Attribute("n") : null));
        foreach (var verse in verses)
        {
            var inWord = false;
            foreach (var syl in verse)
            {
                var hyphen = (string?)syl.Attribute("con") == "d";
                if (inWord || hyphen) syl.SetAttributeValue("wordpos", !inWord ? "i" : hyphen ? "m" : "t");
                inWord = hyphen;
            }
        }
        foreach (var tempo in doc.Descendants(Mei + "tempo").Where(t => t.Attribute("mm") is not null && t.Attribute("mm.unit") is null)) tempo.SetAttributeValue("mm.unit", "4");
        foreach (var fermata in doc.Descendants(Mei + "fermata").Where(f => (string?)f.Attribute("form") == "inv")) fermata.SetAttributeValue("place", "below");
        return $"{doc.Declaration}\n{doc.Root}";
    }

    // Written values, longest first: each is half the one before.
    private static readonly string[] Values = ["long", "breve", "1", "2", "4", "8", "16", "32", "64", "128"];

    // Each bar, how long its music lasts and how long its metre's bar is, in whole notes. With no metre written, the Core reads 4/4.
    private static List<(XElement Measure, double Length, double Bar)> Bars(XDocument doc)
    {
        var bars = new List<(XElement Measure, double Length, double Bar)>();
        var bar = 1.0;
        foreach (var e in doc.Descendants().Where(e => e.Name == Mei + "scoreDef" || e.Name == Mei + "measure"))
        {
            if (e.Name == Mei + "measure")
            {
                bars.Add((e, e.Descendants(Mei + "layer").Select(l => Length(l)).DefaultIfEmpty(0).Max(), bar));
                continue;
            }
            var meter = e.Attribute("meter.count") is not null ? e : e.Descendants(Mei + "meterSig").FirstOrDefault(m => m.Attribute("count") is not null);
            if (meter is not null) bar = (double)(meter.Attribute("meter.count") ?? meter.Attribute("count"))! / (double)(meter.Attribute("meter.unit") ?? meter.Attribute("unit"))!;
        }
        return bars;
    }

    // How long a layer's music lasts, in whole notes: its notes, chords, rests and spaces, through beams and tuplets, without
    // grace notes.
    private static double Length(XElement within, double scale = 1) => within.Elements().Sum(e => e.Name.LocalName switch
    {
        "note" or "chord" or "rest" or "space" when e.Attribute("grace") is null && Array.IndexOf(Values, (string?)e.Attribute("dur")) is >= 0 and var value =>
            scale * Math.Pow(2, 2 - value) * (2 - Math.Pow(0.5, (int?)e.Attribute("dots") ?? 0)),
        "beam" => Length(e, scale),
        "tuplet" => Length(e, scale * ((double?)e.Attribute("numbase") ?? 2) / ((double?)e.Attribute("num") ?? 3)),
        _ => 0,
    });
}
