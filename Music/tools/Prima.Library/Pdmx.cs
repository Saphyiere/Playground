using System.Formats.Tar;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Prima.Core.Library;

namespace Prima.Library;

/// <summary>
/// PDMX (Long et al., Zenodo 15571083, CC BY 4.0): MusicXML from MuseScore users. Rows are kept when they're solo piano, tagged
/// public domain or CC0 without a licence conflict, the best unique arrangement, and one of the named works below, all published
/// before 1931 (which also keeps out recent music wrongly tagged public domain). Arrangements are dropped by their titles, and
/// copies of the same piece by different users are reduced to the best rated.
/// </summary>
internal static class Pdmx
{
    public const string Name = "PDMX";
    private const string Files = "https://zenodo.org/api/records/15571083/files";

    /// <summary>A named work: its name in the Library, the composer shown, and how its rows are recognised. A single piece keeps one copy.</summary>
    private sealed record Work(string Name, string Composer, Regex Who, Regex What, bool Single);

    private static Work W(string name, string composer, string who, string what, bool single = false) => new(name, composer, new Regex(who, RegexOptions.IgnoreCase), new Regex(what, RegexOptions.IgnoreCase), single);

    // library.md's tier A1, and a few more teaching classics from before 1931.
    private static readonly Work[] Works =
    [
        W("Études enfantines Op. 37", "Henry Lemoine", "lemoine", @"\b37\b"),
        W("Elementary Studies Op. 176", "Jean-Baptiste Duvernoy", "duvernoy", @"\b176\b"),
        W("Studies Op. 821, 599 and 139", "Carl Czerny", "czerny", @"\b(821|599|139)\b"),
        W("First pieces and the London Sketchbook", "Wolfgang Amadeus Mozart", "mozart", @"\bK\.?\s?V?\.?\s?(1[a-f]|[1-5]|15[a-z]{1,2})\b"),
        W("Notebook for Anna Magdalena Bach", "Bach and others", "bach|petzold", @"\b(anh|anna magdalena|notebook|notenb)"),
        W("Little Preludes", "Johann Sebastian Bach", "bach", @"\b(BWV\.?\s?(9[2-3][0-9]|94[0-3]|999)|little prelude|kleine pr|pr\\?(ä|a|ae)ludium|praeambulum)"),
        W("Two-part Inventions", "Johann Sebastian Bach", "bach", @"\b(BWV\.?\s?(77[2-9]|78[0-6])|invention|inventio)"),
        W("Minuets", "Christian Petzold", "petzold", "minuet|menuet"),
        W("Album for the Young Op. 68", "Robert Schumann", "schumann", @"(\b68\b|album f(ü|u)r die jugend|album for the young|jugendalbum)"),
        W("Kinderszenen Op. 15", "Robert Schumann", "schumann", @"(kinderszenen|scenes from childhood|tr(ä|a)umerei|\bop\.?\s?15\b)"),
        W("Album for the Young Op. 39", "Pyotr Ilyich Tchaikovsky", "tchaikovsky|tschaikowsky|chaikovsky|tchaikowsky", @"(\b39\b|children|kinder|album for the young|jugend)"),
        W("Sonatinas Op. 36", "Muzio Clementi", "clementi", @"\b36\b|sonat"),
        W("Teaching pieces", "Cornelius Gurlitt", "gurlitt", "."),
        W("Sonatinas Anh. 5", "Ludwig van Beethoven", "beethoven", @"(anh|ahn\.|sonatina in (g|f))"),
        W("Für Elise", "Ludwig van Beethoven", "beethoven", @"(elise|woo\.?\s?59)", single: true),
        W("Sonatinas Op. 168", "Anton Diabelli", "diabelli", @"\b168\b"),
        W("25 Études Op. 100", "Friedrich Burgmüller", "burgm", @"(\b100\b|arabesque|candeur|pastorale|ballade|tarentelle|progr|courante|clart|r(é|e)union|innocence|babillarde|inqui|harmonie des anges|barcarolle|chevaleresque|hirondelle|adieu|consolation|styrienne|douce plainte|agitato|chasse|sainte)"),
        W("Teaching pieces", "Louis Streabbog", "streabbog", "."),
        W("Pieces for beginners", "Daniel Gottlob Türk", @"(daniel|gottlob).*t(ü|u)rk|t(ü|u)rk.*(daniel|gottlob)", "."),
        W("Sonatinas", "Friedrich Kuhlau", "kuhlau", "sonatin"),
        W("Lyric Pieces Op. 12", "Edvard Grieg", "grieg", @"(\bop\.?\s?12\b|arietta|watchman|elves|elfin|folk ?(song|melody)|norwegian|album ?leaf|national song|waltz)"),
        W("Gymnopédies and Gnossiennes", "Erik Satie", "satie", "(gymnop|gnossien)"),
        W("Preludes Op. 28", "Frédéric Chopin", "chopin", @"(prelude|pr(é|e)lude|\bop\.?\s?28\b)"),
        W("Rags", "Scott Joplin", "joplin", "(rag|entertainer|maple leaf)"),
    ];

    // Arrangements, other instruments and remixes, by words in the title or the composer field ("arranged by").
    private static readonly Regex Arranged = new(@"\b(arr\b|arr\.|arrang\w*|easy|easier|simplif\w*|beginners?\b|jazz\w*|retrograde|christmas|yeehaw|2 pianos|two pianos|duet|4 hands|four hands|flute|violin|cello|clarinet|guitar|orchestra\w*|choir|satb|accordion|organ|harp|ukulele|remix|cover|version|transcri\w*|reduction|theme from|medley|lo-?fi|trap|meme|ringtone|sped up|slowed|variations?|qrs|with ?out the|ties and rests|composer\b)", RegexOptions.IgnoreCase);

    // Names taken out of titles, longest first, since the composer is shown on its own.
    private static readonly string[] Names = [.. new[]
    {
        "Johann Sebastian Bach", "J. S. Bach", "J.S. Bach", "js bach", "C.P.E. Bach", "Wolfgang Amadeus Mozart", "W. A. Mozart", "W.A. Mozart", "Carl Czerny",
        "Henry Lemoine", "by Jean Baptiste Duvernoy", "Jean Baptiste Duvernoy", "Robert Schumann", "Frédéric Chopin", "F. Choppin", "Erik Satie", "by Edvard Grieg",
        "Edvard Grieg", "E. Grieg", "Ludwig van Beethoven", "Beethoven", "Muzio CLEMENTI", "Cornelius Gurlitt", "Anton Diabelli", "Friedrich Burgmüller",
        "Friedrich Kuhlau", "Scott Joplin", "P. I. Tchaikovsky", "Tchaikovsky", "Christian Petzold", "Mozart", "Czerny", "Lemoine", "Duvernoy", "Schumann",
        "Chopin", "Satie", "Grieg", "Clementi", "Gurlitt", "Diabelli", "Burgmüller", "Kuhlau", "Joplin", "Petzold", "Bach",
    }.OrderByDescending(n => n.Length)];

    private sealed record Row(string Mxl, string Title, string WhoText, string Licence, string LicenceUrl, double Rating, int Ratings, int Bars, string Metadata, Work Work);

    public static async Task<List<BundledPiece>> Fetch(HttpClient http, string cache, string output)
    {
        var csv = Path.Combine(cache, "PDMX.csv");
        if (!File.Exists(csv))
        {
            Console.WriteLine("PDMX: downloading its index (225 MB)…");
            await Download(http, $"{Files}/PDMX.csv/content", csv);
        }

        var chosen = Choose(csv);
        Console.WriteLine($"PDMX: {chosen.Count} pieces chosen.");
        var missing = chosen.Where(r => !File.Exists(Path.Combine(output, PieceFile(r)))).ToDictionary(r => Entry(r.Mxl), r => r);
        if (missing.Count > 0)
        {
            // One pass through the 1.9 GB archive, writing only the chosen files; it stops once it has them all.
            Console.WriteLine($"PDMX: reading the MusicXML archive for {missing.Count} files (1.9 GB, read once)…");
            using var response = await http.GetAsync($"{Files}/mxl.tar.gz/content", HttpCompletionOption.ResponseHeadersRead);
            response.EnsureSuccessStatusCode();
            var total = response.Content.Headers.ContentLength;
            var wanted = missing.Count;
            await using var body = new Progress(await response.Content.ReadAsStreamAsync(), total, 100, read => $"PDMX: {read} of {total / 1_000_000 ?? 0} MB read, {wanted - missing.Count} of {wanted} found…");
            await using var gzip = new GZipStream(body, CompressionMode.Decompress);
            using var tar = new TarReader(gzip);
            while (missing.Count > 0 && await tar.GetNextEntryAsync() is { } entry)
            {
                if (entry.DataStream is null || !missing.Remove(Entry(entry.Name), out var row)) continue;

                await using var file = File.Create(Path.Combine(output, PieceFile(row)));
                await entry.DataStream.CopyToAsync(file);
            }
            if (missing.Count > 0) Console.WriteLine($"PDMX: {missing.Count} files weren't in the archive: {string.Join(", ", missing.Keys.Take(5))}");
        }

        return [.. chosen.Where(r => File.Exists(Path.Combine(output, PieceFile(r)))).Select(r => ToPiece(r, output))];
    }

    private static List<Row> Choose(string csvPath)
    {
        using var reader = new StreamReader(csvPath, Encoding.UTF8);
        var header = Csv.Read(reader).First();
        var at = header.Select((name, i) => (name, i)).ToDictionary(p => p.name, p => p.i);
        string F(string[] row, string name) => row[at[name]];

        var rows = new List<Row>();
        foreach (var row in Csv.Read(reader))
        {
            if (row.Length < header.Length) continue;
            if (F(row, "subset:no_license_conflict") != "True" || F(row, "license") is not ("publicdomain" or "cc-zero")) continue;
            if (F(row, "tracks") is not ("0" or "0-0") || F(row, "is_best_unique_arrangement") != "True") continue;

            var title = Repair(F(row, "title"));
            var subtitle = Repair(F(row, "subtitle"));
            var composer = Repair(F(row, "composer_name"));
            if (title is null || subtitle is null || composer is null) continue;

            var who = $"{composer} {Repair(F(row, "artist_name")) ?? ""}";
            var text = $"{title} {(subtitle == "NA" ? "" : subtitle)}";
            if (Arranged.IsMatch(text) || Arranged.IsMatch(composer)) continue;

            var work = Works.FirstOrDefault(w => w.Who.IsMatch(who) && w.What.IsMatch(text));
            if (work is null) continue;

            var rating = double.TryParse(F(row, "rating"), System.Globalization.CultureInfo.InvariantCulture, out var r) ? r : 0;
            var ratings = int.TryParse(F(row, "n_ratings"), out var n) ? n : 0;
            var bars = int.TryParse(F(row, "song_length.bars"), out var b) ? b : 0;
            rows.Add(new Row(F(row, "mxl"), Clean(title), who, F(row, "license"), F(row, "license_url"), rating, ratings, bars, F(row, "metadata"), work));
        }

        // Copies of one piece by different users: the same work and catalogue numbers (or, without numbers, the same title and
        // length). The best rated stays.
        return [.. rows.GroupBy(r => (r.Work.Name, r.Work.Composer, Key(r))).Select(g => g.OrderByDescending(r => r.Rating).ThenByDescending(r => r.Ratings).ThenBy(r => r.Mxl, StringComparer.Ordinal).First())];
    }

    private static string Key(Row row)
    {
        if (row.Work.Single) return "";

        var numbers = Regex.Matches(row.Title, @"\d+[a-z]{0,2}\b", RegexOptions.IgnoreCase).Select(m => m.Value.ToLowerInvariant()).Order().ToArray();
        return numbers.Length > 0 ? string.Join(' ', numbers) : $"{Regex.Replace(row.Title.ToLowerInvariant(), "[^a-zà-ÿ]", "")} {row.Bars}";
    }

    // Text read with the wrong encoding upstream ("Ã©" for "é") is put right when it can be; null when it can't.
    private static string? Repair(string text)
    {
        if (!Regex.IsMatch(text, "[ÃÐÑâ]")) return text;

        var repaired = Encoding.UTF8.GetString(Encoding.Latin1.GetBytes(text));
        return repaired.Contains('�') || Regex.IsMatch(repaired, "[ÃÐ]") ? null : repaired;
    }

    private static string Clean(string title)
    {
        var t = Names.Aggregate(title, (s, name) => Regex.Replace(s, $@"(?<![\p{{L}}]){Regex.Escape(name)}(?![\p{{L}}])", "", RegexOptions.IgnoreCase));
        t = Regex.Replace(t, @"\(\s*\)|\(piano solo\)|\(real audio\)|for piano\b.*$|this one!", "", RegexOptions.IgnoreCase);
        t = Regex.Replace(t, @"\s{2,}", " ");
        t = Regex.Replace(t, @"^[\s\-–:.,/]+|[\s\-–:,/]+$", "");
        t = Regex.Replace(t, @"\s+by\s*$", "", RegexOptions.IgnoreCase);
        return t.Length == 0 ? title : char.ToUpperInvariant(t[0]) + t[1..];
    }

    private static string Entry(string path) => path.TrimStart('.', '/');

    private static string Id(Row row) => "pdmx-" + Path.GetFileNameWithoutExtension(row.Mxl);

    private static string PieceFile(Row row) => Path.Combine("pieces", Id(row) + ".mxl");

    private static BundledPiece ToPiece(Row row, string output)
    {
        var file = PieceFile(row);
        var score = Path.GetFileNameWithoutExtension(row.Metadata);
        var licence = row.Licence == "cc-zero" ? "CC0 1.0" : "Public Domain Mark 1.0";
        var credit = $"Encoded by a MuseScore user (score {score}), {licence.Replace(" 1.0", "")}; from PDMX by Long et al., CC BY 4.0.";
        return new BundledPiece(Id(row), row.Title, row.Work.Composer, row.Work.Name, "mxl", file.Replace('\\', '/'), null, Hash(Path.Combine(output, file)), Name,
            $"https://musescore.com/score/{score}", licence, row.LicenceUrl, credit, Tier.A, row.Bars > 0 ? row.Bars : null);
    }

    internal static string Hash(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexStringLower(SHA256.HashData(stream));
    }

    internal static async Task Download(HttpClient http, string url, string path)
    {
        var part = path + ".part";
        using (var response = await http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead))
        {
            response.EnsureSuccessStatusCode();
            var total = response.Content.Headers.ContentLength;
            await using var file = File.Create(part);
            await using var body = new Progress(await response.Content.ReadAsStreamAsync(), total, 25, read => $"  {read} of {total / 1_000_000 ?? 0} MB…");
            await body.CopyToAsync(file);
        }
        File.Move(part, path, overwrite: true);
    }
}

/// <summary>A download read through, printing a line every <c>everyMb</c> megabytes, so a long one is seen to be moving.</summary>
internal sealed class Progress(Stream inner, long? total, int everyMb, Func<long, string> line) : Stream
{
    private long read;
    private long nextMb = everyMb;

    public override bool CanRead => true;
    public override bool CanSeek => false;
    public override bool CanWrite => false;
    public override long Length => total ?? 0;
    public override long Position { get => read; set => throw new NotSupportedException(); }

    public override int Read(byte[] buffer, int offset, int count) => Count(inner.Read(buffer, offset, count));

    public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) => Count(await inner.ReadAsync(buffer, cancellationToken));

    private int Count(int n)
    {
        read += n;
        if (read / 1_000_000 >= nextMb)
        {
            Console.WriteLine(line(read / 1_000_000));
            nextMb += everyMb;
        }
        return n;
    }

    public override void Flush() { }
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

    protected override void Dispose(bool disposing)
    {
        if (disposing) inner.Dispose();
        base.Dispose(disposing);
    }
}

/// <summary>Comma-separated values with quoted fields (RFC 4180): quotes inside a field are doubled, and a field may hold newlines.</summary>
internal static class Csv
{
    public static IEnumerable<string[]> Read(TextReader reader)
    {
        var fields = new List<string>();
        var field = new StringBuilder();
        var quoted = false;
        int c;
        while ((c = reader.Read()) != -1)
        {
            if (quoted)
            {
                if (c == '"')
                {
                    if (reader.Peek() == '"') field.Append((char)reader.Read());
                    else quoted = false;
                }
                else field.Append((char)c);
                continue;
            }

            switch (c)
            {
                case '"':
                    quoted = true;
                    break;
                case ',':
                    fields.Add(field.ToString());
                    field.Clear();
                    break;
                case '\r':
                    break;
                case '\n':
                    fields.Add(field.ToString());
                    field.Clear();
                    yield return [.. fields];
                    fields.Clear();
                    break;
                default:
                    field.Append((char)c);
                    break;
            }
        }
        if (field.Length > 0 || fields.Count > 0)
        {
            fields.Add(field.ToString());
            yield return [.. fields];
        }
    }
}
