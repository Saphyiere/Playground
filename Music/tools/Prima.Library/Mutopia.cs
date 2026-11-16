using System.IO.Compression;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Prima.Core.Library;

namespace Prima.Library;

/// <summary>
/// The Mutopia Project: engravings made with LilyPond, kept as the A4 PDF and the MIDI made from the same source (LilyPond doesn't
/// convert cleanly to MusicXML). Each piece's licence is read from its .rdf; public domain, CC BY and CC BY-SA are kept.
/// </summary>
internal static class Mutopia
{
    public const string Name = "Mutopia";
    private const string Ftp = "https://www.mutopiaproject.org/ftp/";

    // library.md's tier A2: folders on the Mutopia site, with the work and composer shown in the Library, and about where the
    // collection sits on the ladder (library.md §2's table).
    private static readonly (string Folder, string Work, string Composer, int Anchor)[] Folders =
    [
        ("BurgmullerJFF/O100/", "25 Études Op. 100", "Friedrich Burgmüller", 7),
        ("CzernyC/Op_821/", "160 Short Exercises Op. 821", "Carl Czerny", 5),
        ("KuhlauF/O20/", "Sonatinas Op. 20", "Friedrich Kuhlau", 7),
        ("JoplinS/", "Rags", "Scott Joplin", 9),
        .. Enumerable.Range(772, 15).Select(n => ($"BachJS/BWV{n}/", "Two-part Inventions", "Johann Sebastian Bach", 9)),
        .. Enumerable.Range(787, 15).Select(n => ($"BachJS/BWV{n}/", "Sinfonias", "Johann Sebastian Bach", 11)),
        .. Enumerable.Range(924, 20).Select(n => ($"BachJS/BWV{n}/", "Little Preludes", "Johann Sebastian Bach", 7)),
    ];

    private static readonly XNamespace Mp = "http://www.mutopiaproject.org/piece-data/0.1/";

    // Music for a keyboard alone, as a piece's "for" says it: "Piano", "Harpsichord, Piano", "Piano/Harpsichord/Clavichord".
    private static readonly Regex Keyboard = new(@"^(piano|pianoforte|harpsichord|clavichord|clavier|keyboard|fortepiano|virginals?|spinet)(\s*(,|/|or|and)\s*(piano|pianoforte|harpsichord|clavichord|clavier|keyboard|fortepiano|virginals?|spinet))*\.?$", RegexOptions.IgnoreCase);

    // A header field in a piece's LilyPond source: mutopiainstrument = "Piano".
    private static readonly Regex Header = new(@"^\s*(mutopiainstrument|instrument|license|copyright|composer)\s*=\s*""([^""]*)""", RegexOptions.Multiline);

    // The project's composers, by its own codes, as the other sources name them; any other's name is read from its piece.
    private static readonly Dictionary<string, string> Composers = new()
    {
        ["AdamA"] = "Adolphe Adam", ["AlbenizIMF"] = "Isaac Albéniz", ["AlkanCV"] = "Charles-Valentin Alkan", ["AndreJ"] = "Johann André",
        ["BachCPE"] = "Carl Philipp Emanuel Bach", ["BachJS"] = "Johann Sebastian Bach", ["BartokB"] = "Béla Bartók", ["BeethovenLv"] = "Ludwig van Beethoven",
        ["BehrF"] = "Franz Behr", ["BendaJA"] = "Jiří Antonín Benda", ["BizetG"] = "Georges Bizet", ["BrahmsJ"] = "Johannes Brahms",
        ["BurgmullerJFF"] = "Friedrich Burgmüller", ["ChopinFF"] = "Frédéric Chopin", ["ClementiM"] = "Muzio Clementi", ["CouperinF"] = "François Couperin",
        ["CzernyC"] = "Carl Czerny", ["DebussyC"] = "Claude Debussy", ["DussekJL"] = "Jan Ladislav Dussek", ["FieldJ"] = "John Field",
        ["FischerJKF"] = "Johann Caspar Ferdinand Fischer", ["FrobergerJJ"] = "Johann Jakob Froberger", ["GobbaertsL"] = "Louis Streabbog",
        ["GottschalkLM"] = "Louis Moreau Gottschalk", ["GriegE"] = "Edvard Grieg", ["HandelGF"] = "George Frideric Handel", ["HanonCL"] = "Charles-Louis Hanon",
        ["HaydnFJ"] = "Joseph Haydn", ["JoplinS"] = "Scott Joplin", ["KuhlauF"] = "Friedrich Kuhlau", ["LisztF"] = "Franz Liszt",
        ["MendelssohnF"] = "Felix Mendelssohn", ["MozartWA"] = "Wolfgang Amadeus Mozart", ["MussorgskyM"] = "Modest Mussorgsky", ["PurcellH"] = "Henry Purcell",
        ["RameauJP"] = "Jean-Philippe Rameau", ["SatieE"] = "Erik Satie", ["ScarlattiD"] = "Domenico Scarlatti", ["SchubertF"] = "Franz Schubert",
        ["SchumannR"] = "Robert Schumann", ["ScriabinA"] = "Alexander Scriabin", ["TchaikovskyPI"] = "Pyotr Ilyich Tchaikovsky", ["TelemannGP"] = "Georg Philipp Telemann",
        ["Mendelssohn-BartholdyF"] = "Felix Mendelssohn", ["ChabrierEA"] = "Emmanuel Chabrier", ["DiabelliA"] = "Anton Diabelli", ["DvorakA"] = "Antonín Dvořák",
        ["FaureG"] = "Gabriel Fauré", ["FranckC"] = "César Franck", ["GranadosE"] = "Enrique Granados", ["HaydnJM"] = "Michael Haydn",
        ["PachelbelJ"] = "Johann Pachelbel", ["PleyelIJ"] = "Ignace Pleyel", ["Rimsky-KorsakovN"] = "Nikolai Rimsky-Korsakov", ["Saint-SaensC"] = "Camille Saint-Saëns",
        ["BuxtehudeD"] = "Dieterich Buxtehude", ["KuhnauJ"] = "Johann Kuhnau", ["RachmaninoffS"] = "Sergei Rachmaninoff", ["GershwinG"] = "George Gershwin",
        ["ElgarE"] = "Edward Elgar", ["GlazunovA"] = "Alexander Glazunov", ["RegerM"] = "Max Reger", ["StraussJJ"] = "Johann Strauss II",
        ["TurpinT"] = "Tom Turpin", ["VivaldiA"] = "Antonio Vivaldi", ["WanhalJ"] = "Johann Baptist Vanhal", ["MinkusLA"] = "Ludwig Minkus",
        ["VerdiG"] = "Giuseppe Verdi", ["BlumenthalJ"] = "Jacques Blumenthal", ["StanchinskyAV"] = "Alexei Stanchinsky", ["PeaseAH"] = "Alfred H. Pease",
        ["PejacsevichD"] = "Dora Pejačević", ["Yaniewicz"] = "Feliks Yaniewicz", ["KumarR"] = "Ramana Kumar", ["MuellerAE"] = "August Eberhard Müller",
        ["SousaJP"] = "John Philip Sousa",
        ["Anonymous"] = "Anonymous", ["Traditional"] = "Traditional",
    };

    /// <summary>
    /// The rest of the Mutopia Project's music for a keyboard alone (piano, harpsichord, clavichord), for the local collection: every
    /// piece the project's sources on GitHub say is for one, with a licence that allows it (public domain, CC BY, CC BY-SA, read again
    /// from the piece's .rdf on the site), as its A4 PDF and MIDI. What the repo's bundle has stays out. A piece engraved as several
    /// PDFs (a suite's movements, zipped) comes as a piece a PDF, each with the MIDI of the same name. Each is graded from its MIDI,
    /// written out as notation the way an import is (MidiNotation), so the Library places it at its own level, where the bundle's
    /// carry their collection's. The site's robots.txt allows everything; it's read a file at a time, a quarter second apart.
    /// </summary>
    public static async Task<List<BundledPiece>> FetchMore(HttpClient http, string cache, string output, IReadOnlySet<string> bundled)
    {
        Console.WriteLine("Mutopia: the project's sources, to find its keyboard music…");
        var repo = await Github.Fetch(http, cache, "MutopiaProject", "MutopiaProject", "ftp");
        var ftp = Path.Combine(repo, "ftp");
        var found = new List<(string Path, string Composer)>();
        foreach (var folder in Directory.EnumerateDirectories(ftp, "*", SearchOption.AllDirectories).Order(StringComparer.Ordinal))
        {
            var name = Path.GetFileName(folder);
            if (!File.Exists(Path.Combine(folder, name + ".ly")) && !Directory.Exists(Path.Combine(folder, name + "-lys"))) continue;

            var fields = new Dictionary<string, string>();
            foreach (var source in Directory.EnumerateFiles(folder, "*", SearchOption.AllDirectories).Where(f => f.EndsWith(".ly", StringComparison.Ordinal) || f.EndsWith(".ily", StringComparison.Ordinal)))
            {
                foreach (Match m in Header.Matches(await File.ReadAllTextAsync(source))) fields.TryAdd(m.Groups[1].Value, m.Groups[2].Value);
            }
            var instrument = fields.GetValueOrDefault("mutopiainstrument") ?? fields.GetValueOrDefault("instrument") ?? "";
            var licence = fields.GetValueOrDefault("license") ?? fields.GetValueOrDefault("copyright") ?? "";
            var path = Path.GetRelativePath(ftp, folder).Replace('\\', '/');
            if (!Keyboard.IsMatch(instrument.Trim()) || Licence(licence).Name is null || bundled.Contains(Id(name))) continue;

            // "Johann Sebastian Bach (1685-1750)" is Johann Sebastian Bach; "SCOTT JOPLIN." is Scott Joplin; with no name in the
            // piece, the project's code for it, "DoonanSC", is S. C. Doonan.
            var code = path.Split('/')[0];
            var byCode = Regex.Match(code, "^([A-Z][a-z]+(?:-[A-Z][a-z]+)*)([A-Z]*)$") is { Success: true } c
                ? $"{string.Concat(c.Groups[2].Value.Select(i => $"{i}. "))}{c.Groups[1].Value}" : code;
            var composer = Composers.GetValueOrDefault(code) ?? Regex.Replace(fields.GetValueOrDefault("composer") ?? byCode, @"\s*\([^)]*\d[^)]*\)\s*$|^by\s+|\.$", "", RegexOptions.IgnoreCase).Trim();
            if (composer.Length > 0 && composer.All(c => !char.IsLower(c))) composer = System.Globalization.CultureInfo.InvariantCulture.TextInfo.ToTitleCase(composer.ToLowerInvariant());
            found.Add((path, composer.Length > 0 ? composer : code));
        }
        Console.WriteLine($"Mutopia: {found.Count} pieces for a keyboard not in the bundle; fetching each one's PDF and MIDI (a few minutes, quicker once they're here)…");

        // A piece goes by its folder's name, as the bundle's do, unless another piece's folder has the same name (Bizet's and
        // Scriabin's "prelude"): then each goes by its whole path. Its files in the cache always do.
        var shared = found.GroupBy(f => Path.GetFileName(f.Path), StringComparer.OrdinalIgnoreCase).Where(g => g.Count() > 1).Select(g => g.Key)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var rdfs = Path.Combine(cache, "mutopia");
        Directory.CreateDirectory(rdfs);
        var pieces = new List<BundledPiece>();
        foreach (var (i, (path, composer)) in found.Index())
        {
            var id = shared.Contains(Path.GetFileName(path)) ? Id(path) : Id(Path.GetFileName(path));
            pieces.AddRange(await FetchMorePiece(http, path, id, composer, rdfs, output));
            if ((i + 1) % 50 == 0) Console.WriteLine($"Mutopia: {i + 1} of {found.Count} pieces…");
        }
        Console.WriteLine($"Mutopia: {pieces.Count} pieces, {pieces.Count(p => p.Anchor is not null)} graded from their MIDI.");
        return pieces;
    }

    private static string Id(string folder) => "mutopia-" + Regex.Replace(folder, "[^A-Za-z0-9-]", "-");

    private static async Task<List<BundledPiece>> FetchMorePiece(HttpClient http, string path, string id, string composer, string rdfs, string output)
    {
        // The .rdf says what the site has, and is kept: a second run reads it from the cache.
        var url = Ftp + path + "/";
        var folder = Path.GetFileName(path);
        var key = Path.Combine(rdfs, path.Replace('/', '-'));
        var cached = key + ".rdf";
        var rdfText = File.Exists(cached) ? await File.ReadAllTextAsync(cached) : await Polite(() => Get(http, $"{url}{folder}.rdf"));
        if (rdfText is null) return [];
        if (!File.Exists(cached)) await File.WriteAllTextAsync(cached, rdfText);

        var rdf = XDocument.Parse(rdfText).Descendants(Mp + "title").FirstOrDefault()?.Parent;
        string Field(string name) => rdf?.Element(Mp + name)?.Value.Trim() ?? "";
        var (licence, licenceUrl) = Licence(Field("licence"));
        if (rdf is null || !Keyboard.IsMatch(Field("for")) || licence is null || Field("pdfFileA4") is not { Length: > 0 } pdf) return [];

        var (work, numbered) = Opus(Field("opus"));
        var title = Numbered(Title(Field("title"), Field("opus")), numbered);
        var number = Regex.Match(Field("id"), @"-(\d+)$").Groups[1].Value;
        var maintainer = Field("maintainer");
        var credit = $"Typeset{(maintainer.Length > 0 ? $" by {maintainer}" : "")} for the Mutopia Project{(Field("source") is { Length: > 0 } source ? $", from {source}" : "")}; {licence}.";
        var info = number.Length > 0 ? $"https://www.mutopiaproject.org/cgibin/piece-info.cgi?id={number}" : url;
        BundledPiece Piece(string partId, string partTitle, string file, string? midi) => new(partId, partTitle, composer, work, "pdf", file, midi,
            Pdmx.Hash(Path.Combine(output, file)), Name, info, licence, licenceUrl, credit, TierOf(licence), null, midi is null ? null : Level(Path.Combine(output, midi), partTitle));

        if (!pdf.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
        {
            var file = $"pieces/{id}.pdf";
            if (!await Fetched(http, url + pdf, Path.Combine(output, file))) return [];
            string? midi = null;
            if (Field("midFile") is { Length: > 0 } mid && mid.EndsWith(".mid", StringComparison.OrdinalIgnoreCase) && await Fetched(http, url + mid, Path.Combine(output, "pieces", id + ".mid"))) midi = $"pieces/{id}.mid";
            return [Piece(id, title, file, midi)];
        }

        // Several PDFs, and as many MIDI files: each PDF a piece, with the MIDI of its name ("…-allemande-a4.pdf", "…-allemande.mid").
        var pdfs = key + "-a4-pdfs.zip";
        var mids = key + "-mids.zip";
        if (!await Fetched(http, url + pdf, pdfs)) return [];
        var midiOf = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);
        if (Field("midFile") is { Length: > 0 } midZip && midZip.EndsWith(".zip", StringComparison.OrdinalIgnoreCase) && await Fetched(http, url + midZip, mids))
        {
            using var zip = ZipFile.OpenRead(mids);
            foreach (var entry in zip.Entries.Where(e => e.Name.EndsWith(".mid", StringComparison.OrdinalIgnoreCase)))
            {
                using var stream = entry.Open();
                using var bytes = new MemoryStream();
                await stream.CopyToAsync(bytes);
                midiOf[Path.GetFileNameWithoutExtension(entry.Name)] = bytes.ToArray();
            }
        }

        var parts = new List<BundledPiece>();
        using (var zip = ZipFile.OpenRead(pdfs))
        {
            // In their order, numbers as numbers: "2-…" before "10-…". Not the US Letter copies some zips hold beside the A4 ones,
            // nor a duet's parts beside a piece for two hands. Where the movements come with MIDI, not the PDFs without it either
            // (a set's title page), nor the whole piece beside its movements, the PDF named for the piece itself ("french-suite-3",
            // "BWV-827", "pictures-at-an-exhibition-all"), which comes with a MIDI file of its name too.
            static string Stem(ZipArchiveEntry entry) => Regex.Replace(Path.GetFileNameWithoutExtension(entry.Name), "-a4$", "");
            var entries = zip.Entries.Where(e => e.Name.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase)
                    && !Regex.IsMatch(e.Name, @"(^|[-_])(letter|let)[-_]|four-hands|4-hands|\bduet|primo|secondo", RegexOptions.IgnoreCase))
                .OrderBy(e => Regex.Replace(e.Name, @"\d+", m => m.Value.PadLeft(4, '0')), StringComparer.Ordinal).ToList();
            if (entries.Any(e => midiOf.ContainsKey(Stem(e))))
            {
                entries = [.. entries.Where(e => midiOf.ContainsKey(Stem(e)))];
            }
            if (entries.Count > 1) entries.RemoveAll(e => Stem(e).Equals(folder, StringComparison.OrdinalIgnoreCase) || Stem(e).Equals($"{folder}-all", StringComparison.OrdinalIgnoreCase));
            foreach (var entry in entries)
            {
                var stem = Stem(entry);
                var partId = $"{id}-{Regex.Replace(stem, "[^A-Za-z0-9-]", "-")}";
                var file = $"pieces/{partId}.pdf";
                entry.ExtractToFile(Path.Combine(output, file), overwrite: true);
                string? midi = null;
                if (midiOf.TryGetValue(stem, out var bytes))
                {
                    midi = $"pieces/{partId}.mid";
                    await File.WriteAllBytesAsync(Path.Combine(output, midi), bytes);
                }
                // The part's name is its file's, less the piece's, with the composer's before it: "french-suite-5-allemande" and
                // "bach-french-suite-3-allemande" are "Allemande". A file named for no word ("bwv-827-1", "11title-hub") is the
                // piece's movement by its place: "Partita No. 3, movement 1".
                var piece = Regex.Match(stem, $@"^(\p{{L}}+-)?{Regex.Escape(folder)}", RegexOptions.IgnoreCase);
                var name = Regex.Replace(piece.Success ? stem[piece.Length..] : stem, "[-_]+", " ").Trim();
                var words = Regex.Matches(name, @"\p{L}{3,}").Select(m => m.Value.ToLowerInvariant()).Where(w => w is not ("title" or "hub" or "part" or "page" or "bwv")).ToList();
                var named = words.Count > 0 ? $"{title}: {char.ToUpperInvariant(name[0])}{name[1..]}" : $"{title}, movement {entries.IndexOf(entry) + 1}";
                parts.Add(Piece(partId, named, file, midi));
            }
        }
        return parts;
    }

    // A piece's opus as the work it's listed under, written one way and without the piece's own number, so a set's pieces share it:
    // "Op 28, No 10", "O 68 n°1", "O 15 N 1", "O 37,1", "Opus 34. I." and "Opus 46 no. 1 (Craw 17)" are the works "Op. 28", "Op. 68",
    // "Op. 15", "Op. 37", "Op. 34" and "Op. 46", numbered 10, 1, 1, 1, 1 and 1; "D.899 (Op. 90, No. 1)" is "D.899 (Op. 90)", No. 1;
    // "O 27" is "Op. 27". A catalogue's own number (BWV 825, KV 453a, Hob. XVI:27) is the work.
    private static (string Work, int? Number) Opus(string opus)
    {
        var written = Regex.Replace(opus.Trim(), @"^(O|Op|Opus)\.?\s*(?=\d)", "Op. ", RegexOptions.IgnoreCase);
        var number = Regex.Match(written, @"(,\s*|\s+)(No|Nr|N|n°|nº)\.?\s*(?<n>\d+)\b|(?<=\d),\s*(?<n>\d+)\b|(?<=\d)-(?<n>\d+)(?=\s|$)|\.\s+(?<roman>[IVX]+)\.?$", RegexOptions.IgnoreCase);
        if (!number.Success) return (written, null);

        // What's after the number (another catalogue's number for the piece, "(Craw 17)") is the piece's; a bracket it was in is closed.
        var work = written[..number.Index].TrimEnd(' ', ',', '.');
        if (work.Count(c => c == '(') > work.Count(c => c == ')')) work += ")";
        return (work, number.Groups["n"].Success ? int.Parse(number.Groups["n"].Value, System.Globalization.CultureInfo.InvariantCulture) : Array.IndexOf(Roman, number.Groups["roman"].Value.ToUpperInvariant()));
    }

    private static readonly string[] Roman = ["", "I", "II", "III", "IV", "V", "VI", "VII", "VIII", "IX", "X", "XI", "XII"];

    // A piece's number from its opus, in its title when the title doesn't have it already: "Impromptu" in Op. 11 No. 1 is
    // "Impromptu, No. 1"; "Prélude 10" (Op. 28 No. 10) and "Six sonates faciles (I)" (Op. 46 No. 1) stay as they are.
    private static string Numbered(string title, int? number) =>
        number is not { } n || n <= 0 || Regex.IsMatch(title, $@"\b{n}\b") || n < Roman.Length && Regex.IsMatch(title, $@"\b{Roman[n]}\b") ? title : $"{title}, No. {n}";

    // The Library's tier for a licence: share-alike (CC BY-SA) is one of its own, since what's made from it keeps the licence.
    private static Tier TierOf(string licence) => licence.StartsWith("CC BY-SA", StringComparison.Ordinal) ? Tier.SA : Tier.A;

    // A piece's level from its MIDI written out as notation, as an import of it would be graded; null when it can't be read.
    private static int? Level(string midi, string title)
    {
        try
        {
            var score = MidiNotation.Read(File.ReadAllBytes(midi), title);
            return score.Expected().Count == 0 ? null : Excerpts.PieceGrade(score, Excerpts.Cut(score)).Level;
        }
        catch (Exception e) when (e is FormatException or InvalidOperationException or ArgumentException or IndexOutOfRangeException or EndOfStreamException or InvalidDataException)
        {
            return null;
        }
    }

    // A request to the site, a quarter second after the last.
    private static async Task<T> Polite<T>(Func<Task<T>> request)
    {
        await Task.Delay(250);
        return await request();
    }

    // A file from the site, unless it's here already.
    private static async Task<bool> Fetched(HttpClient http, string url, string path) =>
        File.Exists(path) && new FileInfo(path).Length > 0 || await Polite(() => Save(http, url, path));

    public static async Task<List<BundledPiece>> Fetch(HttpClient http, string output)
    {
        Console.WriteLine("Mutopia: fetching each piece's PDF and MIDI (a few minutes)…");
        var pieces = new List<BundledPiece>();
        foreach (var (folder, work, composer, anchor) in Folders)
        {
            var found = await PieceFolders(http, Ftp + folder, depth: 3);
            foreach (var url in found)
            {
                var piece = await FetchPiece(http, url, work, composer, anchor, output);
                if (piece is not null) pieces.Add(piece);
            }
            if (found.Count > 0) Console.WriteLine($"Mutopia: {folder} {found.Count} pieces.");
        }
        return pieces;
    }

    // The folders under a URL that hold a piece (an .rdf), following the site's directory listings.
    private static async Task<List<string>> PieceFolders(HttpClient http, string url, int depth)
    {
        var listing = await Get(http, url);
        if (listing is null) return [];

        var links = Regex.Matches(listing, "href=\"([^\"?/][^\"]*)\"").Select(m => m.Groups[1].Value).ToList();
        if (links.Any(l => l.EndsWith(".rdf", StringComparison.Ordinal))) return [url];
        if (depth == 0) return [];

        var found = new List<string>();
        foreach (var sub in links.Where(l => l.EndsWith('/')))
        {
            found.AddRange(await PieceFolders(http, url + sub, depth - 1));
        }
        return found;
    }

    private static async Task<BundledPiece?> FetchPiece(HttpClient http, string url, string work, string composer, int anchor, string output)
    {
        var folder = url.TrimEnd('/').Split('/')[^1];
        var rdfText = await Get(http, $"{url}{folder}.rdf");
        if (rdfText is null) return null;

        var rdf = XDocument.Parse(rdfText).Descendants(Mp + "title").FirstOrDefault()?.Parent;
        string Field(string name) => rdf?.Element(Mp + name)?.Value.Trim() ?? "";
        if (rdf is null || Field("for") is not { } instrument || !instrument.Contains("Piano", StringComparison.OrdinalIgnoreCase) && !instrument.Contains("Harpsichord", StringComparison.OrdinalIgnoreCase) && !instrument.Contains("Clavier", StringComparison.OrdinalIgnoreCase)) return null;

        var (licence, licenceUrl) = Licence(Field("licence"));
        if (licence is null || Field("pdfFileA4") is not { Length: > 0 } pdf) return null;

        var id = "mutopia-" + Regex.Replace(folder, "[^A-Za-z0-9-]", "-");
        var pdfPath = Path.Combine("pieces", id + ".pdf");
        if (!await Save(http, url + pdf, Path.Combine(output, pdfPath))) return null;

        string? midiPath = null;
        if (Field("midFile") is { Length: > 0 } mid && await Save(http, url + mid, Path.Combine(output, "pieces", id + ".mid"))) midiPath = Path.Combine("pieces", id + ".mid");

        var number = Regex.Match(Field("id"), @"-(\d+)$").Groups[1].Value;
        var title = Title(Field("title"), Field("opus"));
        var maintainer = Field("maintainer");
        var credit = $"Typeset{(maintainer.Length > 0 ? $" by {maintainer}" : "")} for the Mutopia Project{(Field("source") is { Length: > 0 } source ? $", from {source}" : "")}; {licence}.";
        return new BundledPiece(id, title, composer, work, "pdf", pdfPath.Replace('\\', '/'), midiPath?.Replace('\\', '/'), Pdmx.Hash(Path.Combine(output, pdfPath)), Name,
            number.Length > 0 ? $"https://www.mutopiaproject.org/cgibin/piece-info.cgi?id={number}" : url, licence, licenceUrl, credit, TierOf(licence), null, anchor);
    }

    // "l'Arabesque" becomes "L'Arabesque" (the work beside it names the opus); a Bach piece titled only "Prelude" takes its BWV
    // number, since that's how it's told apart.
    private static string Title(string title, string opus)
    {
        var t = title.Length > 0 ? char.ToUpperInvariant(title[0]) + title[1..] : "Untitled";
        return opus.StartsWith("BWV", StringComparison.OrdinalIgnoreCase) && !t.Any(char.IsDigit) ? $"{t}, {opus.TrimEnd('.', ' ')}" : t;
    }

    private static (string? Name, string Url) Licence(string licence) => licence switch
    {
        "Public Domain" => ("Public domain", "https://www.mutopiaproject.org/legal.html"),
        var l when l.StartsWith("Creative Commons Attribution-ShareAlike", StringComparison.Ordinal) => ($"CC BY-SA {Version(l)}", $"https://creativecommons.org/licenses/by-sa/{Version(l)}/"),
        var l when l.StartsWith("Creative Commons Attribution", StringComparison.Ordinal) && !l.Contains("NonCommercial", StringComparison.Ordinal) && !l.Contains("NoDerivs", StringComparison.Ordinal) => ($"CC BY {Version(l)}", $"https://creativecommons.org/licenses/by/{Version(l)}/"),
        _ => (null, ""),
    };

    private static string Version(string licence) => Regex.Match(licence, @"\d\.\d").Value is { Length: > 0 } v ? v : "4.0";

    private static async Task<string?> Get(HttpClient http, string url)
    {
        for (var attempt = 1; attempt <= 3; attempt++)
        {
            try
            {
                using var response = await http.GetAsync(url);
                if ((int)response.StatusCode == 404) return null;
                response.EnsureSuccessStatusCode();
                return await response.Content.ReadAsStringAsync();
            }
            catch (HttpRequestException) when (attempt < 3)
            {
                await Task.Delay(1000 * attempt);
            }
        }
        return null;
    }

    private static async Task<bool> Save(HttpClient http, string url, string path)
    {
        if (File.Exists(path) && new FileInfo(path).Length > 0) return true;

        for (var attempt = 1; attempt <= 3; attempt++)
        {
            try
            {
                await Pdmx.Download(http, url, path);
                return true;
            }
            catch (HttpRequestException) when (attempt < 3)
            {
                await Task.Delay(1000 * attempt);
            }
            catch (HttpRequestException)
            {
                return false;
            }
        }
        return false;
    }
}
