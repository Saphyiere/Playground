using System.IO.Compression;
using Prima.Core.Library;

namespace Prima.Library;

/// <summary>
/// ASAP (Foscarin, McLeod, Rigaux, Jacquemard and Sakai, ISMIR 2020; github.com/fosfrancesco/asap-dataset, CC BY-NC-SA 4.0), for
/// the local collection: the scores of a dataset of piano performances, MusicXML written by amateurs and corrected by the
/// dataset's authors. It brings concert pieces the other sources lack: Bach's Well-Tempered Clavier, Chopin's études, ballades
/// and scherzos, Liszt, Schubert's impromptus, Schumann's Kreisleriana, Rachmaninoff's preludes. All were published before 1931.
/// Its Beethoven, Haydn and Mozart sonatas, and the Debussy and Ravel the DCML has, stay out; so do its copies with a repeat
/// written out or left out to match a performance, where the score as printed is there too.
/// </summary>
internal static class Asap
{
    public const string Name = "ASAP";
    private const string Licence = "CC BY-NC-SA 4.0";
    private const string LicenceUrl = "https://creativecommons.org/licenses/by-nc-sa/4.0/";

    // Each score kept, by its folder: its title, its work and its composer.
    private static readonly Dictionary<string, (string Title, string Work, string Composer)> Scores = new()
    {
        ["Bach/Italian_concerto"] = ("Italian Concerto, BWV 971: II. Andante", "Clavier-Übung II", "Johann Sebastian Bach"),
        ["Balakirev/Islamey"] = ("Islamey, Oriental Fantasy", "Piano pieces", "Mily Balakirev"),
        ["Brahms/Six_Pieces_op_118/2"] = ("Intermezzo in A major, Op. 118 No. 2", "Six Pieces, Op. 118", "Johannes Brahms"),
        ["Chopin/Ballades/1"] = ("Ballade No. 1 in G minor, Op. 23", "Ballades", "Frédéric Chopin"),
        ["Chopin/Ballades/2"] = ("Ballade No. 2 in F major, Op. 38", "Ballades", "Frédéric Chopin"),
        ["Chopin/Ballades/3"] = ("Ballade No. 3 in A♭ major, Op. 47", "Ballades", "Frédéric Chopin"),
        ["Chopin/Ballades/4"] = ("Ballade No. 4 in F minor, Op. 52", "Ballades", "Frédéric Chopin"),
        ["Chopin/Barcarolle"] = ("Barcarolle in F♯ major, Op. 60", "Piano pieces", "Frédéric Chopin"),
        ["Chopin/Berceuse_op_57"] = ("Berceuse in D♭ major, Op. 57", "Piano pieces", "Frédéric Chopin"),
        ["Chopin/Etudes_op_10/1"] = ("Étude in C major, Op. 10 No. 1", "Études", "Frédéric Chopin"),
        ["Chopin/Etudes_op_10/2"] = ("Étude in A minor, Op. 10 No. 2", "Études", "Frédéric Chopin"),
        ["Chopin/Etudes_op_10/3"] = ("Étude in E major, Op. 10 No. 3", "Études", "Frédéric Chopin"),
        ["Chopin/Etudes_op_10/4"] = ("Étude in C♯ minor, Op. 10 No. 4", "Études", "Frédéric Chopin"),
        ["Chopin/Etudes_op_10/5"] = ("Étude in G♭ major, Op. 10 No. 5", "Études", "Frédéric Chopin"),
        ["Chopin/Etudes_op_10/7"] = ("Étude in C major, Op. 10 No. 7", "Études", "Frédéric Chopin"),
        ["Chopin/Etudes_op_10/8"] = ("Étude in F major, Op. 10 No. 8", "Études", "Frédéric Chopin"),
        ["Chopin/Etudes_op_10/10"] = ("Étude in A♭ major, Op. 10 No. 10", "Études", "Frédéric Chopin"),
        ["Chopin/Etudes_op_10/12"] = ("Étude in C minor, Op. 10 No. 12", "Études", "Frédéric Chopin"),
        ["Chopin/Etudes_op_25/1"] = ("Étude in A♭ major, Op. 25 No. 1", "Études", "Frédéric Chopin"),
        ["Chopin/Etudes_op_25/2"] = ("Étude in F minor, Op. 25 No. 2", "Études", "Frédéric Chopin"),
        ["Chopin/Etudes_op_25/4"] = ("Étude in A minor, Op. 25 No. 4", "Études", "Frédéric Chopin"),
        ["Chopin/Etudes_op_25/5"] = ("Étude in E minor, Op. 25 No. 5", "Études", "Frédéric Chopin"),
        ["Chopin/Etudes_op_25/8"] = ("Étude in D♭ major, Op. 25 No. 8", "Études", "Frédéric Chopin"),
        ["Chopin/Etudes_op_25/10"] = ("Étude in B minor, Op. 25 No. 10", "Études", "Frédéric Chopin"),
        ["Chopin/Etudes_op_25/11"] = ("Étude in A minor, Op. 25 No. 11", "Études", "Frédéric Chopin"),
        ["Chopin/Etudes_op_25/12"] = ("Étude in C minor, Op. 25 No. 12", "Études", "Frédéric Chopin"),
        ["Chopin/Polonaises/53"] = ("Polonaise in A♭ major, Op. 53", "Polonaises", "Frédéric Chopin"),
        ["Chopin/Scherzos/20"] = ("Scherzo No. 1 in B minor, Op. 20", "Scherzos", "Frédéric Chopin"),
        ["Chopin/Scherzos/31"] = ("Scherzo No. 2 in B♭ minor, Op. 31", "Scherzos", "Frédéric Chopin"),
        ["Chopin/Scherzos/39"] = ("Scherzo No. 3 in C♯ minor, Op. 39", "Scherzos", "Frédéric Chopin"),
        ["Chopin/Sonata_2/1st"] = ("Sonata No. 2 in B♭ minor, Op. 35: I. Grave – Doppio movimento", "Piano sonatas", "Frédéric Chopin"),
        ["Chopin/Sonata_2/2nd"] = ("Sonata No. 2 in B♭ minor, Op. 35: II. Scherzo", "Piano sonatas", "Frédéric Chopin"),
        ["Chopin/Sonata_2/3rd"] = ("Sonata No. 2 in B♭ minor, Op. 35: III. Marche funèbre", "Piano sonatas", "Frédéric Chopin"),
        ["Chopin/Sonata_2/4th"] = ("Sonata No. 2 in B♭ minor, Op. 35: IV. Finale: Presto", "Piano sonatas", "Frédéric Chopin"),
        ["Chopin/Sonata_3/2nd"] = ("Sonata No. 3 in B minor, Op. 58: II. Scherzo", "Piano sonatas", "Frédéric Chopin"),
        ["Chopin/Sonata_3/3rd"] = ("Sonata No. 3 in B minor, Op. 58: III. Largo", "Piano sonatas", "Frédéric Chopin"),
        ["Chopin/Sonata_3/4th"] = ("Sonata No. 3 in B minor, Op. 58: IV. Finale: Presto non tanto", "Piano sonatas", "Frédéric Chopin"),
        ["Glinka/The_Lark"] = ("The Lark (arranged by Balakirev)", "Piano pieces", "Mikhail Glinka"),
        ["Liszt/Annees_de_pelerinage_2/1_Gondoliera"] = ("Gondoliera, S. 162 No. 1", "Venezia e Napoli", "Franz Liszt"),
        ["Liszt/Ballade_2"] = ("Ballade No. 2 in B minor, S. 171", "Piano pieces", "Franz Liszt"),
        ["Liszt/Concert_Etude_S145/1"] = ("Waldesrauschen, S. 145 No. 1", "Two Concert Études", "Franz Liszt"),
        ["Liszt/Concert_Etude_S145/2"] = ("Gnomenreigen, S. 145 No. 2", "Two Concert Études", "Franz Liszt"),
        ["Liszt/Gran_Etudes_de_Paganini/2_La_campanella"] = ("La campanella, S. 141 No. 3", "Grandes études de Paganini", "Franz Liszt"),
        ["Liszt/Gran_Etudes_de_Paganini/6_Theme_and_Variations"] = ("Theme and Variations in A minor, S. 141 No. 6", "Grandes études de Paganini", "Franz Liszt"),
        ["Liszt/Hungarian_Rhapsodies/6"] = ("Hungarian Rhapsody No. 6, S. 244 No. 6", "Hungarian Rhapsodies", "Franz Liszt"),
        ["Liszt/Mephisto_Waltz"] = ("Mephisto Waltz No. 1, S. 514", "Piano pieces", "Franz Liszt"),
        ["Liszt/Sonata"] = ("Sonata in B minor, S. 178", "Piano pieces", "Franz Liszt"),
        ["Liszt/Transcendental_Etudes/1"] = ("Preludio, S. 139 No. 1", "Transcendental Études", "Franz Liszt"),
        ["Liszt/Transcendental_Etudes/3"] = ("Paysage, S. 139 No. 3", "Transcendental Études", "Franz Liszt"),
        ["Liszt/Transcendental_Etudes/4"] = ("Mazeppa, S. 139 No. 4", "Transcendental Études", "Franz Liszt"),
        ["Liszt/Transcendental_Etudes/5"] = ("Feux follets, S. 139 No. 5", "Transcendental Études", "Franz Liszt"),
        ["Liszt/Transcendental_Etudes/9"] = ("Ricordanza, S. 139 No. 9", "Transcendental Études", "Franz Liszt"),
        ["Liszt/Transcendental_Etudes/10"] = ("Allegro agitato molto, S. 139 No. 10", "Transcendental Études", "Franz Liszt"),
        ["Liszt/Transcendental_Etudes/11"] = ("Harmonies du soir, S. 139 No. 11", "Transcendental Études", "Franz Liszt"),
        ["Mozart/Fantasie_475"] = ("Fantasia in C minor, K. 475", "Piano pieces", "Wolfgang Amadeus Mozart"),
        ["Prokofiev/Toccata"] = ("Toccata in D minor, Op. 11", "Piano pieces", "Sergei Prokofiev"),
        ["Rachmaninoff/Preludes_op_23/4"] = ("Prelude in D major, Op. 23 No. 4", "Preludes", "Sergei Rachmaninoff"),
        ["Rachmaninoff/Preludes_op_23/6"] = ("Prelude in E♭ major, Op. 23 No. 6", "Preludes", "Sergei Rachmaninoff"),
        ["Rachmaninoff/Preludes_op_32/5"] = ("Prelude in G major, Op. 32 No. 5", "Preludes", "Sergei Rachmaninoff"),
        ["Rachmaninoff/Preludes_op_32/10"] = ("Prelude in B minor, Op. 32 No. 10", "Preludes", "Sergei Rachmaninoff"),
        ["Ravel/Gaspard_de_la_Nuit/1_Ondine"] = ("Ondine", "Gaspard de la nuit", "Maurice Ravel"),
        ["Ravel/Pavane"] = ("Pavane pour une infante défunte", "Piano pieces", "Maurice Ravel"),
        ["Schubert/Impromptu_op.90_D.899/1"] = ("Impromptu in C minor, D. 899 No. 1", "Impromptus", "Franz Schubert"),
        ["Schubert/Impromptu_op.90_D.899/2"] = ("Impromptu in E♭ major, D. 899 No. 2", "Impromptus", "Franz Schubert"),
        ["Schubert/Impromptu_op.90_D.899/3"] = ("Impromptu in G♭ major, D. 899 No. 3", "Impromptus", "Franz Schubert"),
        ["Schubert/Impromptu_op.90_D.899/4"] = ("Impromptu in A♭ major, D. 899 No. 4", "Impromptus", "Franz Schubert"),
        ["Schubert/Impromptu_op142/1"] = ("Impromptu in F minor, D. 935 No. 1", "Impromptus", "Franz Schubert"),
        ["Schubert/Impromptu_op142/3"] = ("Impromptu in B♭ major, D. 935 No. 3", "Impromptus", "Franz Schubert"),
        ["Schubert/Moment_Musical_no_1"] = ("Moment musical in C major, D. 780 No. 1", "Moments musicaux", "Franz Schubert"),
        ["Schubert/Moment_musical_no_3"] = ("Moment musical in F minor, D. 780 No. 3", "Moments musicaux", "Franz Schubert"),
        ["Schubert/Piano_Sonatas/664-1"] = ("Sonata in A major, D. 664: I. Allegro moderato", "Piano sonatas", "Franz Schubert"),
        ["Schubert/Piano_Sonatas/664-2"] = ("Sonata in A major, D. 664: II. Andante", "Piano sonatas", "Franz Schubert"),
        ["Schubert/Piano_Sonatas/664-3"] = ("Sonata in A major, D. 664: III. Allegro", "Piano sonatas", "Franz Schubert"),
        ["Schubert/Piano_Sonatas/894-2"] = ("Sonata in G major, D. 894: II. Andante", "Piano sonatas", "Franz Schubert"),
        ["Schubert/Wanderer_fantasie"] = ("Wanderer Fantasy in C major, D. 760", "Piano pieces", "Franz Schubert"),
        ["Schumann/Arabeske"] = ("Arabeske in C major, Op. 18", "Piano pieces", "Robert Schumann"),
        ["Schumann/Kreisleriana/1"] = ("Äußerst bewegt, Op. 16 No. 1", "Kreisleriana", "Robert Schumann"),
        ["Schumann/Kreisleriana/2"] = ("Sehr innig und nicht zu rasch, Op. 16 No. 2", "Kreisleriana", "Robert Schumann"),
        ["Schumann/Kreisleriana/3"] = ("Sehr aufgeregt, Op. 16 No. 3", "Kreisleriana", "Robert Schumann"),
        ["Schumann/Kreisleriana/4"] = ("Sehr langsam, Op. 16 No. 4", "Kreisleriana", "Robert Schumann"),
        ["Schumann/Kreisleriana/5"] = ("Sehr lebhaft, Op. 16 No. 5", "Kreisleriana", "Robert Schumann"),
        ["Schumann/Kreisleriana/6"] = ("Sehr langsam, Op. 16 No. 6", "Kreisleriana", "Robert Schumann"),
        ["Schumann/Kreisleriana/7"] = ("Sehr rasch, Op. 16 No. 7", "Kreisleriana", "Robert Schumann"),
        ["Schumann/Toccata"] = ("Toccata in C major, Op. 7", "Piano pieces", "Robert Schumann"),
        ["Scriabin/Etudes_op_8/11"] = ("Étude in B♭ minor, Op. 8 No. 11", "Études", "Alexander Scriabin"),
        ["Scriabin/Sonatas/5"] = ("Sonata No. 5, Op. 53", "Piano sonatas", "Alexander Scriabin"),
    };

    // The Well-Tempered Clavier's keys, in its order: BWV 846–869 are Book I, 870–893 Book II.
    private static readonly string[] Keys =
    [
        "C major", "C minor", "C♯ major", "C♯ minor", "D major", "D minor", "E♭ major", "D♯ minor", "E major", "E minor", "F major", "F minor",
        "F♯ major", "F♯ minor", "G major", "G minor", "A♭ major", "G♯ minor", "A major", "A minor", "B♭ major", "B♭ minor", "B major", "B minor",
    ];

    public static async Task<List<BundledPiece>> Fetch(HttpClient http, string cache, string output)
    {
        Console.WriteLine("ASAP: the dataset's scores…");
        var folder = await Github.Fetch(http, cache, "fosfrancesco", "asap-dataset", "xml_score.musicxml", everywhere: true);
        var licence = Path.Combine(folder, "LICENSE.md");
        if (!File.Exists(licence) || !(await File.ReadAllTextAsync(licence)).Contains("Attribution-NonCommercial-ShareAlike 4.0", StringComparison.Ordinal))
        {
            Console.WriteLine($"ASAP: left out, its licence isn't {Licence} any more.");
            return [];
        }

        var scores = Directory.EnumerateFiles(folder, "xml_score.musicxml", SearchOption.AllDirectories)
            .ToDictionary(s => Path.GetRelativePath(folder, Path.GetDirectoryName(s)!).Replace('\\', '/'), StringComparer.Ordinal);
        var pieces = new List<BundledPiece>();
        foreach (var (path, score) in scores.OrderBy(s => s.Key, StringComparer.Ordinal))
        {
            // A copy with its repeats changed to match a performance stands in only where the score as printed isn't there
            // (Chopin's second sonata's first movement).
            var plain = Variant.Replace(path, "");
            if (plain != path && scores.ContainsKey(plain) || Named(plain) is not { } named) continue;
            var (title, work, composer) = named;

            // Kept as compressed MusicXML (.mxl), the way Prepare reads it: the score in a zip with its container.
            var id = $"asap-{plain.Replace('/', '-').Replace('.', '-').Replace('_', '-').ToLowerInvariant()}";
            var file = $"pieces/{id}.mxl";
            var mxl = Path.Combine(output, file);
            if (!File.Exists(mxl))
            {
                using var zip = ZipFile.Open(mxl, ZipArchiveMode.Create);
                await using (var writer = new StreamWriter(zip.CreateEntry("META-INF/container.xml").Open()))
                {
                    await writer.WriteAsync("""<?xml version="1.0" encoding="UTF-8"?><container><rootfiles><rootfile full-path="score.musicxml" media-type="application/vnd.recordare.musicxml+xml"/></rootfiles></container>""");
                }
                zip.CreateEntryFromFile(score, "score.musicxml", CompressionLevel.SmallestSize);
            }
            var credit = $"From ASAP, a dataset of aligned scores and performances (Foscarin, McLeod, Rigaux, Jacquemard and Sakai, 2020), {Licence}: " +
                "a score written by amateurs and corrected by the dataset's authors.";
            pieces.Add(new BundledPiece(id, title, composer, work, "mxl", file, null, Pdmx.Hash(mxl), Name,
                $"https://github.com/fosfrancesco/asap-dataset/tree/master/{path}", Licence, LicenceUrl, credit, Tier.B, null));
        }
        Console.WriteLine($"ASAP: {pieces.Count} scores.");
        return pieces;
    }

    // A folder for a copy of a score with its repeats written out or left out.
    private static readonly System.Text.RegularExpressions.Regex Variant = new("_(no_repeat|no_first_repeat|repeat)$");

    // A score's title, work and composer, by its folder; null for one left out.
    private static (string Title, string Work, string Composer)? Named(string path)
    {
        if (Scores.TryGetValue(path, out var named)) return named;

        // Bach's preludes and fugues, by their BWV numbers.
        var bwv = System.Text.RegularExpressions.Regex.Match(path, @"^Bach/(Prelude|Fugue)/bwv_(\d+)$");
        if (!bwv.Success) return null;
        var n = int.Parse(bwv.Groups[2].Value);
        var (book, number) = n < 870 ? ("I", n - 845) : ("II", n - 869);
        return number is < 1 or > 24 ? null
            : ($"{bwv.Groups[1].Value} No. {number} in {Keys[number - 1]}, BWV {n}", $"The Well-Tempered Clavier, Book {book}", "Johann Sebastian Bach");
    }
}
