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

    public static async Task<List<BundledPiece>> Fetch(HttpClient http, string output)
    {
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
            number.Length > 0 ? $"https://www.mutopiaproject.org/cgibin/piece-info.cgi?id={number}" : url, licence, licenceUrl, credit, Tier.A, null, anchor);
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
