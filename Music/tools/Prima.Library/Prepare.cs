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
/// that reads Humdrum, from the npm packages in <c>modules</c>.
/// </summary>
internal static class Prepare
{
    public static async Task<List<BundledPiece>> Run(IReadOnlyList<BundledPiece> pieces, string root, string output, string? modules = null)
    {
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
            ];
            foreach (var (kind, format, arguments) in runs.Where(r => pieces.Any(p => p.Format == r.Format)))
            {
                Console.WriteLine($"Preparing {pieces.Count(p => p.Format == format)} {kind} pieces: Verovio engraves them (a minute or two)…");
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
                if (piece.Format is not ("mxl" or "krn"))
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

                var parts = Collections.Split(await File.ReadAllTextAsync(raw));
                var made = new List<PreparedPiece>();
                for (var i = 0; i < parts.Count; i++)
                {
                    var (id, title) = parts.Count == 1 ? (piece.Id, piece.Title) : ($"{piece.Id}-{i + 1:00}", $"{piece.Title}, No. {i + 1}");
                    var score = MeiReader.Read(parts[i]);
                    if (score.Expected().Count == 0) continue;

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
                if (parts.Count > 1) Console.WriteLine($"  {piece.Title}: one score holding {parts.Count} pieces, split into them.");
                if (made.Count > 0) prepared.Add(piece with { Prepared = made });
            }
            return prepared;
        }
        finally
        {
            engraved.Delete(true);
        }
    }
}
