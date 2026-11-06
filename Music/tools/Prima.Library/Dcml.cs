using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;
using Prima.Core.Library;

namespace Prima.Library;

/// <summary>
/// The DCML's annotated corpora (EPFL's Digital and Cognitive Musicology Lab, github.com/DCMLab, CC BY-NC-SA 4.0), for the local
/// collection: research editions of keyboard music as MuseScore files. Each is converted to MusicXML with webmscore
/// (mscx-to-mxl.cjs), then prepared like any MusicXML, and named from its corpus's metadata. Works first published after 1930,
/// still in copyright in the US, stay out; so do the corpora that aren't keyboard music (the solo violin and cello works), the
/// Mozart sonatas (Sapp's edition has them) and Rachmaninoff's Corelli Variations (1931).
/// </summary>
internal static class Dcml
{
    public const string Name = "DCML";
    private const string Licence = "CC BY-NC-SA 4.0";
    private const string LicenceUrl = "https://creativecommons.org/licenses/by-nc-sa/4.0/";

    private static readonly (string Repo, string Composer)[] Corpora =
    [
        ("bach_en_fr_suites", "Johann Sebastian Bach"),
        ("beethoven_piano_sonatas", "Ludwig van Beethoven"),
        ("chopin_mazurkas", "Frédéric Chopin"),
        ("couperin_clavecin", "François Couperin"),
        ("cpe_bach_keyboard", "Carl Philipp Emanuel Bach"),
        ("debussy_childrens_corner", "Claude Debussy"),
        ("debussy_estampes", "Claude Debussy"),
        ("debussy_images", "Claude Debussy"),
        ("debussy_other_piano_pieces", "Claude Debussy"),
        ("debussy_pour_le_piano", "Claude Debussy"),
        ("debussy_preludes", "Claude Debussy"),
        ("debussy_suite_bergamasque", "Claude Debussy"),
        ("dvorak_silhouettes", "Antonín Dvořák"),
        ("grieg_lyric_pieces", "Edvard Grieg"),
        ("handel_keyboard", "George Frideric Handel"),
        ("jc_bach_sonatas", "Johann Christian Bach"),
        ("liszt_pelerinage", "Franz Liszt"),
        ("medtner_tales", "Nikolai Medtner"),
        ("ravel_piano", "Maurice Ravel"),
        ("scarlatti_sonatas", "Domenico Scarlatti"),
        ("schumann_kinderszenen", "Robert Schumann"),
        ("tchaikovsky_seasons", "Pyotr Ilyich Tchaikovsky"),
        ("wf_bach_sonatas", "Wilhelm Friedemann Bach"),
    ];

    // Debussy's metadata is mostly dedications and engravers' headings, so his pieces are named here, by their Lesure numbers.
    // Images oubliées (L. 87, first published 1977), the Étude retrouvée and Les soirs illuminés (L. 0, 1980 and 2003), the Danse
    // bohémienne (L. 9, 1932) and the Page d'album (L. 133, 1933) aren't here, and so are left out.
    private static readonly Dictionary<string, (string Title, string Work)> Debussy = new()
    {
        ["l067"] = ("Mazurka", "Piano pieces"), ["l068"] = ("Rêverie", "Piano pieces"), ["l069"] = ("Danse (Tarentelle styrienne)", "Piano pieces"),
        ["l070"] = ("Ballade", "Piano pieces"), ["l071"] = ("Valse romantique", "Piano pieces"), ["l082"] = ("Nocturne", "Piano pieces"),
        ["l099"] = ("D'un cahier d'esquisses", "Piano pieces"), ["l105"] = ("Masques", "Piano pieces"), ["l106"] = ("L'isle joyeuse", "Piano pieces"),
        ["l108"] = ("Morceau de concours", "Piano pieces"), ["l114"] = ("Le petit nègre", "Piano pieces"), ["l115"] = ("Hommage à Haydn", "Piano pieces"),
        ["l121"] = ("La plus que lente", "Piano pieces"), ["l132"] = ("Berceuse héroïque", "Piano pieces"), ["l138"] = ("Élégie", "Piano pieces"),
        ["l075-01"] = ("Prélude", "Suite bergamasque"), ["l075-02"] = ("Menuet", "Suite bergamasque"), ["l075-03"] = ("Clair de lune", "Suite bergamasque"),
        ["l075-04"] = ("Passepied", "Suite bergamasque"),
        ["l095-01"] = ("Prélude", "Pour le piano"), ["l095-02"] = ("Sarabande", "Pour le piano"), ["l095-03"] = ("Toccata", "Pour le piano"),
        ["l100-01"] = ("Pagodes", "Estampes"), ["l100-02"] = ("La soirée dans Grenade", "Estampes"), ["l100-03"] = ("Jardins sous la pluie", "Estampes"),
        ["l110-01"] = ("Reflets dans l'eau", "Images, Book 1"), ["l110-02"] = ("Hommage à Rameau", "Images, Book 1"), ["l110-03"] = ("Mouvement", "Images, Book 1"),
        ["l111-01"] = ("Cloches à travers les feuilles", "Images, Book 2"), ["l111-02"] = ("Et la lune descend sur le temple qui fut", "Images, Book 2"),
        ["l111-03"] = ("Poissons d'or", "Images, Book 2"),
        ["l113-01"] = ("Doctor Gradus ad Parnassum", "Children's Corner"), ["l113-02"] = ("Jimbo's Lullaby", "Children's Corner"),
        ["l113-03"] = ("Serenade for the Doll", "Children's Corner"), ["l113-04"] = ("The Snow Is Dancing", "Children's Corner"),
        ["l113-05"] = ("The Little Shepherd", "Children's Corner"), ["l113-06"] = ("Golliwogg's Cakewalk", "Children's Corner"),
        ["l117-01"] = ("Danseuses de Delphes", "Préludes, Book 1"), ["l117-02"] = ("Voiles", "Préludes, Book 1"), ["l117-03"] = ("Le vent dans la plaine", "Préludes, Book 1"),
        ["l117-04"] = ("Les sons et les parfums tournent dans l'air du soir", "Préludes, Book 1"), ["l117-05"] = ("Les collines d'Anacapri", "Préludes, Book 1"),
        ["l117-06"] = ("Des pas sur la neige", "Préludes, Book 1"), ["l117-07"] = ("Ce qu'a vu le vent d'ouest", "Préludes, Book 1"),
        ["l117-08"] = ("La fille aux cheveux de lin", "Préludes, Book 1"), ["l117-09"] = ("La sérénade interrompue", "Préludes, Book 1"),
        ["l117-10"] = ("La cathédrale engloutie", "Préludes, Book 1"), ["l117-11"] = ("La danse de Puck", "Préludes, Book 1"), ["l117-12"] = ("Minstrels", "Préludes, Book 1"),
        ["l123-01"] = ("Brouillards", "Préludes, Book 2"), ["l123-02"] = ("Feuilles mortes", "Préludes, Book 2"), ["l123-03"] = ("La puerta del Vino", "Préludes, Book 2"),
        ["l123-04"] = ("Les fées sont d'exquises danseuses", "Préludes, Book 2"), ["l123-05"] = ("Bruyères", "Préludes, Book 2"),
        ["l123-06"] = ("General Lavine – eccentric", "Préludes, Book 2"), ["l123-07"] = ("La terrasse des audiences du clair de lune", "Préludes, Book 2"),
        ["l123-08"] = ("Ondine", "Préludes, Book 2"), ["l123-09"] = ("Hommage à S. Pickwick Esq. P.P.M.P.C.", "Préludes, Book 2"), ["l123-10"] = ("Canope", "Préludes, Book 2"),
        ["l123-11"] = ("Les tierces alternées", "Préludes, Book 2"), ["l123-12"] = ("Feux d'artifice", "Préludes, Book 2"),
    };

    private static readonly string[] Roman = ["", "I", "II", "III", "IV", "V", "VI", "VII", "VIII", "IX", "X", "XI", "XII"];

    public static async Task<List<BundledPiece>> Fetch(HttpClient http, string cache, string output, string modules, string root)
    {
        Console.WriteLine("DCML: the lab's corpora…");
        var script = Path.Combine(root, "Music", "tools", "Prima.Library", "mscx-to-mxl.cjs");
        var chosen = new List<(BundledPiece Piece, string Mscx)>();
        foreach (var (repo, composer) in Corpora)
        {
            var folder = await Github.Fetch(http, cache, "DCMLab", repo);
            var readme = Path.Combine(folder, "README.md");
            if (!File.Exists(readme) || !(await File.ReadAllTextAsync(readme)).Contains(Licence, StringComparison.Ordinal))
            {
                Console.WriteLine($"DCML: left out {repo}, whose licence isn't {Licence} any more.");
                continue;
            }

            using var reader = new StreamReader(Path.Combine(folder, "metadata.tsv"), Encoding.UTF8);
            var rows = Csv.Read(reader, '\t').ToList();
            var at = rows[0].Select((name, i) => (name, i)).ToDictionary(p => p.name, p => p.i);
            foreach (var row in rows.Skip(1).Where(r => r.Length == rows[0].Length))
            {
                // A field as text: no MuseScore font tags, one line.
                string F(string name) => at.TryGetValue(name, out var i) ? Regex.Replace(Regex.Replace(row[i], "<[^>]*>", ""), @"\s+", " ").Trim() : "";
                var piece = F(at.ContainsKey("piece") ? "piece" : "fname");
                var mscx = Path.Combine(folder, F("rel_path") is { Length: > 0 } rel ? rel : $"MS3/{piece}.mscx");
                if (!File.Exists(mscx) || int.TryParse(F("composed_end").Split('-')[0], out var year) && year > 1930) continue;

                var number = F("movementNumber");
                var movement = $"{(int.TryParse(number, out var n) && n < Roman.Length ? Roman[n] : number)}. {F("movementTitle").Replace(" - ", " – ")}";
                var opus = Regex.Replace(F("workNumber"), @"^(op|Op|S|Wq|K)\.\s*", m => $"{(m.Groups[1].Value == "op" ? "Op" : m.Groups[1].Value)}. ");
                var debussy = Debussy.GetValueOrDefault(Regex.Match(piece, @"^l\d{3}(-\d\d)?").Value);
                var title = repo switch
                {
                    "bach_en_fr_suites" => $"{F("workTitle")}, {opus}: {F("movementTitle")}",
                    "beethoven_piano_sonatas" => $"{F("workTitle").Replace("no.", "No.")}, {opus}: {movement}",
                    // "Mazurka in b, Op. 30, no. 2" is "Mazurka in B minor, Op. 30 No. 2": a small letter is a minor key. "BI 71" is "B. 71".
                    "chopin_mazurkas" => Regex.Replace(Regex.Replace(Regex.Replace(F("subtitle_text"), @"\bin ([A-Ga-g])(b|#)?(?=[ ,]|$)", m =>
                        $"in {char.ToUpperInvariant(m.Groups[1].Value[0])}{m.Groups[2].Value.Replace("b", "♭").Replace("#", "♯")} {(char.IsLower(m.Groups[1].Value[0]) ? "minor" : "major")}"),
                        @",\s*no\.\s*", " No. "), @"\bBI\s?(\d+)", "B. $1"),
                    "cpe_bach_keyboard" => $"{Regex.Replace(F("workTitle"), "Major|Minor", m => m.Value.ToLowerInvariant())}, {opus}{(number.Length > 0 ? $": {movement}" : "")}",
                    "dvorak_silhouettes" => $"No. {number}, {F("movementTitle")}",
                    "grieg_lyric_pieces" or "liszt_pelerinage" => $"{F("movementTitle")}, {opus} No. {number}",
                    "jc_bach_sonatas" => $"Sonata in {Regex.Match(F("workTitle"), @"in ([^,]+)").Groups[1].Value}, {Regex.Replace(piece, @"^.*op0*(\d+)no(\d+).*$", "Op. $1 No. $2")}: {movement}",
                    "medtner_tales" => $"Tale, {opus} No. {number} ({F("movementTitle")})",
                    "ravel_piano" => number.Length == 0 ? F("workTitle") : $"{F("workTitle")}: {movement}",
                    "scarlatti_sonatas" => $"Sonata, {opus}",
                    "schumann_kinderszenen" => $"{F("movementTitle")}, Op. 15 No. {number}",
                    "wf_bach_sonatas" => $"{F("workTitle").Replace("Keyboard ", "")}, {opus}: {movement}",
                    "couperin_clavecin" or "handel_keyboard" or "tchaikovsky_seasons" => F("movementTitle"),
                    _ => debussy.Title,
                };
                var work = repo switch
                {
                    "bach_en_fr_suites" => F("workTitle").StartsWith("English", StringComparison.Ordinal) ? "English Suites" : "French Suites",
                    "beethoven_piano_sonatas" => "Piano sonatas",
                    "chopin_mazurkas" => "Mazurkas",
                    "couperin_clavecin" => "L'Art de toucher le clavecin",
                    "cpe_bach_keyboard" => "Keyboard works",
                    "dvorak_silhouettes" => "Silhouettes, Op. 8",
                    "grieg_lyric_pieces" => "Lyric Pieces",
                    "handel_keyboard" => "Suite in E major, HWV 430",
                    "jc_bach_sonatas" or "scarlatti_sonatas" or "wf_bach_sonatas" => "Keyboard sonatas",
                    "liszt_pelerinage" => F("workTitle"),
                    "medtner_tales" => "Tales",
                    "ravel_piano" => "Piano pieces",
                    "schumann_kinderszenen" => "Kinderszenen",
                    "tchaikovsky_seasons" => "The Seasons, Op. 37a",
                    _ => debussy.Work,
                };
                if (string.IsNullOrWhiteSpace(title)) continue;

                var id = $"dcml-{Regex.Replace($"{repo}-{piece}", "[^A-Za-z0-9-]", "-")}";
                var file = $"pieces/{id}.mxl";
                var credit = $"From {repo}, an annotated corpus of EPFL's Digital and Cognitive Musicology Lab (DCML), {Licence}; " +
                    "converted from MuseScore without the lab's harmony labels.";
                chosen.Add((new BundledPiece(id, title, composer, work, "mxl", file, null, "", Name, $"https://github.com/DCMLab/{repo}", Licence, LicenceUrl, credit, Tier.B, null), mscx));
            }
        }

        // Each file in its own run of webmscore (mscx-to-mxl.cjs says why), a few at once; those already converted stay.
        var missing = chosen.Where(c => !File.Exists(Path.Combine(output, c.Piece.File))).ToList();
        Console.WriteLine($"DCML: {chosen.Count} pieces; converting {missing.Count} from MuseScore (about a second each)…");
        var done = 0;
        await Parallel.ForEachAsync(missing, new ParallelOptions { MaxDegreeOfParallelism = Math.Clamp(Environment.ProcessorCount, 1, 4) }, async (c, _) =>
        {
            var mxl = Path.Combine(output, c.Piece.File);
            using var node = Process.Start(new ProcessStartInfo("node", [script, modules, c.Mscx, mxl + ".part"]) { RedirectStandardError = true })!;
            var errors = node.StandardError.ReadToEndAsync();
            using var limit = new CancellationTokenSource(TimeSpan.FromMinutes(2));
            try
            {
                await node.WaitForExitAsync(limit.Token);
            }
            catch (OperationCanceledException)
            {
                node.Kill();
            }
            if (node.HasExited && node.ExitCode == 0 && File.Exists(mxl + ".part")) File.Move(mxl + ".part", mxl, overwrite: true);
            else Console.WriteLine($"  Left out {c.Piece.Title}: webmscore couldn't convert it. {(await errors).Trim()}");
            if (Interlocked.Increment(ref done) % 50 == 0) Console.WriteLine($"DCML: {done} of {missing.Count} converted…");
        });

        return [.. chosen.Where(c => File.Exists(Path.Combine(output, c.Piece.File))).Select(c => c.Piece with { Sha256 = Pdmx.Hash(Path.Combine(output, c.Piece.File)) })];
    }
}
