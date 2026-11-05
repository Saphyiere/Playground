using System.IO.Compression;
using Microsoft.JSInterop;
using Prima.Core.Library;
using Prima.Core.Music;
using Prima.UI.Host;
using Prima.UI.Storage;

namespace Prima.UI.Library;

/// <summary>
/// Brings music into the Library. Bundled pieces come prepared (the Library tool engraved, split, graded and cut them when the
/// bundle was built), so the first time the Library opens they're only stored, in the background; a PDF is listed as it is. An
/// imported file goes through Verovio to MEI in the web view, and is then numbered, cut and graded away from the screen's thread.
/// </summary>
public sealed class LibraryPrep(PrimaStore store, ILibraryBundle bundle, IJSRuntime js)
{
    private Task? running;
    private IJSObjectReference? module;

    /// <summary>Pieces prepared so far, out of how many, while it runs.</summary>
    public (int Done, int Total) Progress { get; private set; }

    public bool Running { get; private set; }

    /// <summary>Pieces that couldn't be read, by title.</summary>
    public List<string> Skipped { get; } = [];

    public event Action? Changed;

    /// <summary>Starts preparing the bundled pieces that aren't in the Library yet; while it runs, a second call is the same run.</summary>
    public Task EnsureAsync() => running ??= Run();

    private async Task Run()
    {
        var manifest = await bundle.ManifestAsync();
        if (manifest is null) return;

        // What the bundle holds, as the Library keeps it: a PDF as it is, a MusicXML piece as prepared (a collection as its pieces).
        var entries = manifest.Pieces.SelectMany(p => p.Format == "pdf" ? [(Piece: p, Part: (PreparedPiece?)null)] : (p.Prepared ?? []).Select(x => (Piece: p, Part: (PreparedPiece?)x))).ToList();
        if (entries.Count == 0) return;

        // A newer bundle's credits, titles and levels for PDFs reach pieces already here (their music and excerpts stay), and
        // bundled pieces it no longer has go: a collection that's now its separate pieces, say.
        var have = await store.GetPieceIdsAsync();
        await store.RefreshBundleAsync([.. entries.Select(e => (e.Part?.Id ?? e.Piece.Id, e.Part?.Title ?? e.Piece.Title, e.Piece))]);

        var missing = entries.Where(e => !have.Contains(e.Part?.Id ?? e.Piece.Id)).ToList();
        if (missing.Count == 0) return;

        (Running, Progress) = (true, (0, missing.Count));
        Changed?.Invoke();
        foreach (var (piece, part) in missing)
        {
            try
            {
                await Prepare(piece, part);
            }
            catch (Exception e) when (e is InvalidDataException or InvalidOperationException)
            {
                Skipped.Add(part?.Title ?? piece.Title);
            }
            Progress = (Progress.Done + 1, Progress.Total);
            Changed?.Invoke();
        }
        Running = false;
        Changed?.Invoke();
    }

    private async Task Prepare(BundledPiece piece, PreparedPiece? part)
    {
        var record = new PieceRecord(part?.Id ?? piece.Id, "bundle", part?.Title ?? piece.Title, piece.Composer, piece.Work, part is null ? "pdf" : "mei", piece.File, piece.Midi,
            part?.Level ?? piece.Anchor, part?.TypicalLevel ?? piece.Anchor, part?.Reasons, part?.Bars ?? piece.Bars, piece.Source, piece.SourceUrl, piece.Licence, piece.LicenceUrl, piece.Credit, piece.Tier.ToString(), null, null, null, DateTimeOffset.Now, null);
        if (part is null)
        {
            await store.SavePieceAsync(record, null, []);
            return;
        }

        var bytes = await bundle.ReadAsync(part.Mei) ?? throw new InvalidOperationException($"{part.Mei} isn't in the bundle.");
        var mei = await Task.Run(() =>
        {
            using var reader = new StreamReader(new GZipStream(new MemoryStream(bytes), CompressionMode.Decompress));
            return reader.ReadToEnd();
        });
        await store.SavePieceAsync(record, mei, part.Excerpts);
    }

    /// <summary>
    /// Imports a file the owner picked: MusicXML (.musicxml, .xml, .mxl), MEI or ABC as notation, a PDF as pages. The piece's id,
    /// or why it couldn't be imported.
    /// </summary>
    public async Task<(string? Id, string? Problem)> ImportAsync(string name, byte[] data)
    {
        var extension = Path.GetExtension(name).ToLowerInvariant();
        var title = Path.GetFileNameWithoutExtension(name).Replace('_', ' ');
        var id = $"import-{Guid.NewGuid():N}";
        var record = new PieceRecord(id, "import", title, "", null, "mei", name, null, null, null, null, null, null, null, null, null, null, null, null, null, null, DateTimeOffset.Now, null);
        switch (extension)
        {
            case ".pdf":
                if (data.Length < 5 || data[0] != '%' || data[1] != 'P' || data[2] != 'D' || data[3] != 'F') return (null, $"{name} isn't a PDF.");
                Directory.CreateDirectory(store.FilesPath);
                await File.WriteAllBytesAsync(Path.Combine(store.FilesPath, id + ".pdf"), data);
                await store.SavePieceAsync(record with { Format = "pdf", File = id + ".pdf" }, null, []);
                return (id, null);
            case ".mxl" or ".musicxml" or ".xml" or ".mei" or ".abc":
                try
                {
                    var (mei, graded, excerpts) = await ReadMusic(data, extension.TrimStart('.'), record);
                    var (fileTitle, composer) = Header(mei);
                    await store.SavePieceAsync(graded with { Title = fileTitle ?? title, Composer = composer ?? "" }, mei, excerpts);
                    return (id, null);
                }
                catch (Exception e) when (e is JSException or System.Xml.XmlException or FormatException or InvalidOperationException or ArgumentException)
                {
                    return (null, $"{name} couldn't be read as music.");
                }
            case ".mid" or ".midi":
                return (null, "A MIDI file has no notation to read from. Open it in MuseScore on the PC and export MusicXML, then import that.");
            case ".mscz" or ".mscx":
                return (null, "Export it from MuseScore as MusicXML (File, Export), then import that.");
            case ".krn":
                return (null, "Humdrum files need converting to MusicXML first (Verovio's own site does it).");
            default:
                return (null, $"Prima reads MusicXML, MEI, ABC and PDF files, not {extension} ones.");
        }
    }

    // Verovio makes the MEI; the Core reads it back, numbers its bars, cuts its excerpts and grades them, off the screen's thread.
    private async Task<(string Mei, PieceRecord Graded, IReadOnlyList<BundledExcerpt> Excerpts)> ReadMusic(byte[] bytes, string kind, PieceRecord record)
    {
        module ??= await js.InvokeAsync<IJSObjectReference>("import", "./_content/Prima.UI/Components/Sheet.razor.js");
        var raw = await module.InvokeAsync<string?>("toMei", bytes, kind) ?? throw new FormatException("Verovio couldn't read it.");
        return await Task.Run(() =>
        {
            var score = MeiReader.Read(raw);
            if (score.Expected().Count == 0) throw new FormatException("No notes.");

            var cuts = Excerpts.Cut(score);
            var (level, typical, reasons) = Excerpts.PieceGrade(score, cuts);
            var graded = record with { Level = level, TypicalLevel = typical, Reasons = reasons, Bars = score.Bars.Count(b => b.Number > 0) };
            return (MeiReader.Renumber(raw, score), graded, (IReadOnlyList<BundledExcerpt>)[.. cuts.Select(c => new BundledExcerpt(c.FirstBar, c.LastBar, c.Grade.Level, c.Grade.Within))]);
        });
    }

    // The title and composer the file gives, when it gives them: a title of only a number ("1", a movement's) isn't one.
    private static (string? Title, string? Composer) Header(string mei)
    {
        var head = System.Xml.Linq.XDocument.Parse(mei).Descendants().FirstOrDefault(e => e.Name.LocalName == "meiHead");
        string? Text(Func<System.Xml.Linq.XElement, bool> where) => head?.Descendants().Where(where).Select(e => e.Value.Trim()).FirstOrDefault(v => v.Length > 1 && v.Any(char.IsLetter));
        var title = Text(e => e.Name.LocalName == "title");
        var composer = Text(e => e.Name.LocalName == "composer" || e.Name.LocalName == "persName" && (string?)e.Attribute("role") == "composer");
        return (title, composer);
    }
}
