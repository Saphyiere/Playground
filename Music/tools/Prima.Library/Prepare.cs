using System.ComponentModel;
using System.Diagnostics;
using System.IO.Compression;
using System.Text;
using Prima.Core.Library;
using Prima.Core.Music;

namespace Prima.Library;

/// <summary>
/// Prepares the bundle so the app has nothing to do but store it: Verovio (the app's own copy, run in Node) engraves each
/// MusicXML piece as MEI, a collection engraved as one score is split into its pieces, and each gets its bars numbered and its
/// excerpts cut and graded. The MEI goes into the bundle gzipped, beside the MusicXML it was made from. Done on the tablet, this
/// took minutes and froze the screen. The local collection's Humdrum pieces (.krn) are engraved the same way by Verovio's build
/// that reads Humdrum, from the npm packages in <c>modules</c>, and its hymns (.mei, which abc2svg wrote from their ABC) by the
/// app's Verovio, which writes them again as it reads them.
/// </summary>
internal static class Prepare
{
    // The pieces of a collection with names of their own, in their order, for its parts' titles. Tchaikovsky's Op. 39 isn't here:
    // its order differs between editions.
    private static readonly Dictionary<string, string[]> PartNames = new()
    {
        ["Kinderszenen Op. 15"] =
        [
            "Von fremden Ländern und Menschen", "Kuriose Geschichte", "Hasche-Mann", "Bittendes Kind", "Glückes genug", "Wichtige Begebenheit",
            "Träumerei", "Am Kamin", "Ritter vom Steckenpferd", "Fast zu ernst", "Fürchtenmachen", "Kind im Einschlummern", "Der Dichter spricht",
        ],
    };

    /// <param name="grandStaff">Only keyboard music: the local collection's, where a melody alone or one instrument's part (one
    /// staff), or from PDMX a song's voice above its piano and from the hymnal a melody or descant above the parts (more than two),
    /// would ask to be played whole.</param>
    public static async Task<List<BundledPiece>> Run(IReadOnlyList<BundledPiece> pieces, string root, string output, string? modules = null, bool grandStaff = false)
    {
        var notGrand = 0;
        var engraved = Directory.CreateTempSubdirectory("prima-mei");
        try
        {
            var tools = Path.Combine(root, "Music", "tools", "Prima.Library");
            var toolkit = Path.Combine(root, "Music", "src", "Prima.UI", "wwwroot", "verovio", "verovio-toolkit-wasm.js");
            var folder = Path.Combine(output, "pieces");
            (string Kind, string Format, string[] Arguments)[] runs =
            [
                ("MusicXML", "mxl", [Path.Combine(tools, "to-mei.cjs"), toolkit, folder, engraved.FullName]),
                ("Humdrum", "krn", [Path.Combine(tools, "kern-to-mei.mjs"), modules ?? "", folder, engraved.FullName]),
                ("MEI", "mei", [Path.Combine(tools, "to-mei.cjs"), toolkit, folder, engraved.FullName, "mei"]),
            ];
            foreach (var (kind, format, arguments) in runs.Where(r => pieces.Any(p => p.Format == r.Format)))
            {
                var count = pieces.Count(p => p.Format == format);
                Console.WriteLine($"Preparing {count} {kind} pieces: Verovio engraves them ({(count > 1000 ? "several minutes" : "a minute or two")})…");
                Process node;
                try
                {
                    node = Process.Start(new ProcessStartInfo("node", arguments) { RedirectStandardError = true })!;
                }
                catch (Win32Exception)
                {
                    throw new InvalidOperationException("Preparing the music needs Node.js (nodejs.org), which runs Verovio. Install it, then run this again with: -- prepare");
                }
                var errors = node.StandardError.ReadToEndAsync();
                await node.WaitForExitAsync();
                if (node.ExitCode != 0) throw new InvalidOperationException($"Verovio stopped: {await errors}");
            }

            var prepared = new List<BundledPiece>();
            foreach (var piece in pieces)
            {
                if (piece.Format is not ("mxl" or "krn" or "mei"))
                {
                    prepared.Add(piece);
                    continue;
                }

                var raw = Path.Combine(engraved.FullName, Path.GetFileNameWithoutExtension(piece.File) + ".mei");
                if (!File.Exists(raw))
                {
                    Console.WriteLine($"  Left out {piece.Title}: Verovio couldn't read it.");
                    continue;
                }

                // A score's hidden playback tempos (bare numbers) are left out: Verovio would print them on the music. Dynamics an
                // edition typed as lyrics (the DCML's Beethoven sonatas and some of its Chopin mazurkas) are made into the marks they
                // are, or left out, rather than printed as words under the staff (LyricDynamics). A score the Core can't read (a note
                // without a pitch, say) is left out, named, rather than stopping the rest.
                // The local collection's pieces go without their instrument's name, as its MuseScore and Humdrum ones are made: it
                // would print as "Piano" beside the first system and "Pno." beside the rest.
                IReadOnlyList<string> parts;
                try
                {
                    var mei = LyricDynamics.Mark(MeiReader.WithoutBareTempos(await File.ReadAllTextAsync(raw)));
                    if (grandStaff) mei = System.Text.RegularExpressions.Regex.Replace(mei, @"<(label|labelAbbr)\b[^>]*>[\s\S]*?</\1>", "");
                    parts = Collections.Split(mei);
                }
                catch (Exception e) when (Unreadable(e))
                {
                    Console.WriteLine($"  Left out {piece.Title} ({piece.Id}): Prima couldn't read Verovio's MEI ({e.GetType().Name}).");
                    continue;
                }
                var made = new List<PreparedPiece>();
                var names = PartNames.GetValueOrDefault(piece.Work) is { } known && known.Length == parts.Count ? known : null;
                for (var i = 0; i < parts.Count; i++)
                {
                    // A part with a name of its own goes by it ("Träumerei, No. 7"), the work being shown beside it.
                    var name = names is null ? piece.Title : names[i];
                    var (id, title) = parts.Count == 1 ? (piece.Id, piece.Title) : ($"{piece.Id}-{i + 1:00}", $"{name}, No. {i + 1}");
                    try
                    {
                        var score = MeiReader.Read(parts[i]);
                        if (score.Expected().Count == 0) continue;
                        // A keyboard edition's three staves (Debussy, Liszt) are a pianist's; PDMX's, more often a voice above a piano,
                        // and a hymn's, its melody or a descant above the four parts, or the parts above an accompaniment.
                        if (grandStaff && Staves(parts[i]) is var staves && (staves < 2 || staves > 2 && piece.Source is Pdmx.Name or Hymns.Name))
                        {
                            notGrand++;
                            continue;
                        }

                        var cuts = Excerpts.Cut(score);
                        var (level, typical, reasons) = Excerpts.PieceGrade(score, cuts);
                        var file = $"pieces/{id}.mei.gz";
                        await using (var gzip = new GZipStream(File.Create(Path.Combine(output, file)), CompressionLevel.SmallestSize))
                        {
                            await gzip.WriteAsync(Encoding.UTF8.GetBytes(MeiReader.Renumber(parts[i], score)));
                        }
                        var excerpts = cuts.Select(c => new BundledExcerpt(c.FirstBar, c.LastBar, c.Grade.Level, Math.Round(c.Grade.Within, 3))).ToList();
                        made.Add(new PreparedPiece(id, title, file, level, typical, reasons, score.Bars.Count(b => b.Number > 0), excerpts));
                    }
                    catch (Exception e) when (Unreadable(e))
                    {
                        Console.WriteLine($"  Left out {title} ({id}): Prima couldn't read Verovio's MEI ({e.GetType().Name}).");
                    }
                }
                if (parts.Count > 1) Console.WriteLine($"  {piece.Title}: one score holding {parts.Count} pieces, split into them.");
                if (made.Count > 0) prepared.Add(piece with { Prepared = made });
            }
            if (notGrand > 0) Console.WriteLine($"  Left out {notGrand} pieces that aren't keyboard music (one staff, or from PDMX or the hymnal more than two).");
            return prepared;
        }
        finally
        {
            // A temporary folder: if something still has a file in it open (a virus scanner, on Windows), it's left for the system.
            try
            {
                engraved.Delete(true);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                Console.WriteLine($"  Left {engraved.FullName} behind: {e.Message}");
            }
        }
    }

    // What reading a score's MEI throws when the score has something the Core doesn't expect.
    private static bool Unreadable(Exception e) => e is NullReferenceException or FormatException or InvalidOperationException or ArgumentException
        or IndexOutOfRangeException or KeyNotFoundException or System.Xml.XmlException;

    // How many staves a piece's MEI has, by their numbers: a later scoreDef can name the same staff again.
    private static int Staves(string mei) =>
        System.Text.RegularExpressions.Regex.Matches(mei, @"<staffDef\b[^>]*\bn=""(\d+)""").Select(m => m.Groups[1].Value).Distinct().Count();
}
