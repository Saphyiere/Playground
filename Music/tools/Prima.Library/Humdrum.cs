using System.Net;
using System.Text.RegularExpressions;
using Prima.Core.Library;

namespace Prima.Library;

/// <summary>
/// Craig Stuart Sapp's Humdrum editions (github.com/craigsapp, CC BY-NC-SA 4.0), for the local collection: scholarly encodings of
/// Mozart's, Haydn's and Scarlatti's sonatas, Joplin's piano music and Bach's four-part chorales, each from a named public-domain
/// edition. Kept as the Humdrum (.krn) and engraved by Prepare.cs; the chorales are set on a grand staff, the way a keyboard
/// player reads them. Each repository's licence is checked as it's read. His Beethoven, Chopin and Hummel editions carry no
/// licence, so they stay out.
/// </summary>
internal static class Humdrum
{
    public const string Name = "Humdrum";
    private const string Licence = "CC BY-NC-SA 4.0";
    private const string LicenceUrl = "https://creativecommons.org/licenses/by-nc-sa/4.0/";

    private static readonly (string Repo, string Work, string Composer, string Edition)[] Repos =
    [
        ("joplin", "Rags, marches and waltzes", "Scott Joplin", "Scott Joplin's music"),
        ("mozart-piano-sonatas", "Piano sonatas", "Wolfgang Amadeus Mozart", "Mozart's sonatas, from the Alte Mozart-Ausgabe of 1878"),
        ("haydn-piano-sonatas", "Piano sonatas", "Joseph Haydn", "Haydn's sonatas, from the Universal Edition of 1901"),
        ("scarlatti-keyboard-sonatas", "Keyboard sonatas", "Domenico Scarlatti", "Scarlatti's sonatas, from Longo's Ricordi edition of 1906–1913"),
        ("bach-370-chorales", "Chorales", "Johann Sebastian Bach", "Bach's 370 chorales, from Dörffel's Breitkopf & Härtel edition"),
    ];

    private static readonly string[] Roman = ["", "I", "II", "III", "IV", "V", "VI", "VII", "VIII", "IX", "X"];

    public static async Task<List<BundledPiece>> Fetch(HttpClient http, string cache, string output)
    {
        Console.WriteLine("Humdrum: Craig Sapp's editions…");
        var pieces = new List<BundledPiece>();
        foreach (var (repo, work, composer, edition) in Repos)
        {
            var folder = await Github.Fetch(http, cache, "craigsapp", repo, "kern");
            var licence = Path.Combine(folder, "LICENSE.txt");
            if (!File.Exists(licence) || !(await File.ReadAllTextAsync(licence)).Contains(Licence, StringComparison.Ordinal))
            {
                Console.WriteLine($"Humdrum: left out {repo}, whose licence isn't {Licence} any more.");
                continue;
            }

            var before = pieces.Count;
            foreach (var path in Directory.EnumerateFiles(Path.Combine(folder, "kern"), "*.krn").Order(StringComparer.Ordinal))
            {
                var text = await File.ReadAllTextAsync(path);

                // The reference records: OTL the title, SCT the catalogue number, OMV and OMD the movement, YEM the licence.
                var references = Regex.Matches(text, @"^!!!([A-Za-z0-9@]+):[ \t]*(.*?)\s*$", RegexOptions.Multiline);
                var records = references.GroupBy(m => m.Groups[1].Value).ToDictionary(g => g.Key, g => WebUtility.HtmlDecode(g.First().Groups[2].Value));
                if (records.TryGetValue("YEM", out var terms) && !terms.Contains("BY-NC-SA", StringComparison.Ordinal)) continue;

                // "Piano Sonata No. 11 in A major, K. 331: I. Variation II"; a chorale by its German title; a rag by its own.
                var original = records.FirstOrDefault(r => r.Key.StartsWith("OTL@@", StringComparison.Ordinal)).Value;
                var title = original ?? records.GetValueOrDefault("OTL") ?? Path.GetFileNameWithoutExtension(path);
                var sct = records.GetValueOrDefault("SCT1") ?? records.GetValueOrDefault("SCT") ?? "";
                var catalogue = Regex.Replace(Regex.Replace(sct, "<sup>[^<]*</sup>", ""), @"^K\s+", "K. ");
                if (catalogue.Length > 0 && !title.Replace(" ", "").Contains(catalogue.Replace(" ", ""), StringComparison.Ordinal)) title += $", {catalogue}";
                var movement = Regex.Match(records.GetValueOrDefault("OMV") ?? "", @"^\d+").Value;
                if (movement.Length > 0 && int.Parse(movement) < Roman.Length)
                {
                    title = $"{title}: {Roman[int.Parse(movement)]}. {records.GetValueOrDefault("OMD")}".TrimEnd('.', ' ');
                }

                var id = $"humdrum-{repo}-{Regex.Replace(Path.GetFileNameWithoutExtension(path), "[^A-Za-z0-9-]", "-")}";
                var file = $"pieces/{id}.krn";
                // Verovio's satb2gs filter sets a chorale's four voices on the grand staff: soprano and alto above, tenor and bass below.
                var music = repo == "bach-370-chorales" ? text.TrimEnd() + "\n!!!filter: satb2gs\n" : text;
                await File.WriteAllTextAsync(Path.Combine(output, file), music);
                var credit = $"Encoded by Craig Stuart Sapp, in his digital edition of {edition}; {Licence}.";
                pieces.Add(new BundledPiece(id, title, composer, work, "krn", file, null, Pdmx.Hash(Path.Combine(output, file)), Name,
                    $"https://github.com/craigsapp/{repo}", Licence, LicenceUrl, credit, Tier.B, null));
            }
            Console.WriteLine($"Humdrum: {repo}, {pieces.Count - before} pieces.");
        }
        return pieces;
    }
}
