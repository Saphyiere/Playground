using Microsoft.JSInterop;
using Prima.Core.Library;
using Prima.Core.Music;
using Prima.UI.Host;
using Prima.UI.Storage;

namespace Prima.UI.Library;

/// <summary>
/// Brings music into the Library. Bundled pieces are prepared once, in the background the first time the Library opens: each
/// MusicXML file goes through Verovio to MEI (in the web view, where Verovio runs), gets its bars numbered, is cut into excerpts
/// and graded, and is saved; a PDF is listed as it is. Imports go the same way.
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

        // A newer bundle's credits, titles and levels for PDFs reach pieces already here; their music and excerpts stay.
        var have = await store.GetPieceIdsAsync();
        foreach (var piece in manifest.Pieces.Where(p => have.Contains(p.Id))) await store.RefreshPieceAsync(piece.Id, piece.Title, piece.Composer, piece.Work, piece.Credit, piece.Licence, piece.LicenceUrl, piece.SourceUrl, piece.Format == "pdf" ? piece.Anchor : null);

        var missing = manifest.Pieces.Where(p => !have.Contains(p.Id)).ToList();
        if (missing.Count == 0) return;

        (Running, Progress) = (true, (0, missing.Count));
        Changed?.Invoke();
        foreach (var piece in missing)
        {
            try
            {
                await Prepare(piece);
            }
            catch (Exception e) when (e is JSException or System.Xml.XmlException or FormatException or InvalidOperationException or ArgumentException)
            {
                Skipped.Add(piece.Title);
            }
            Progress = (Progress.Done + 1, Progress.Total);
            Changed?.Invoke();
        }
        Running = false;
        Changed?.Invoke();
    }

    private async Task Prepare(BundledPiece piece)
    {
        var record = new PieceRecord(piece.Id, "bundle", piece.Title, piece.Composer, piece.Work, piece.Format == "pdf" ? "pdf" : "mei", piece.File, piece.Midi,
            piece.Anchor, piece.Anchor, null, piece.Bars, piece.Source, piece.SourceUrl, piece.Licence, piece.LicenceUrl, piece.Credit, piece.Tier.ToString(), null, null, null, DateTimeOffset.Now, null);
        if (piece.Format == "pdf")
        {
            await store.SavePieceAsync(record, null, []);
            return;
        }

        var bytes = await bundle.ReadAsync(piece.File) ?? throw new InvalidOperationException($"{piece.File} isn't in the bundle.");
        var (mei, score, cuts) = await ReadMusic(bytes, piece.Format);
        await store.SavePieceAsync(Graded(record, score, cuts), mei, cuts);
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
                    var (mei, score, cuts) = await ReadMusic(data, extension.TrimStart('.'));
                    var (fileTitle, composer) = Header(mei);
                    var named = record with { Title = fileTitle ?? title, Composer = composer ?? "" };
                    await store.SavePieceAsync(Graded(named, score, cuts), mei, cuts);
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

    // Verovio makes the MEI; the Core reads it back, numbers its bars, cuts its excerpts and grades them.
    private async Task<(string Mei, Score Score, IReadOnlyList<ExcerptCut> Cuts)> ReadMusic(byte[] bytes, string kind)
    {
        module ??= await js.InvokeAsync<IJSObjectReference>("import", "./_content/Prima.UI/Components/Sheet.razor.js");
        var raw = await module.InvokeAsync<string?>("toMei", bytes, kind) ?? throw new FormatException("Verovio couldn't read it.");
        var score = MeiReader.Read(raw);
        if (score.Expected().Count == 0) throw new FormatException("No notes.");

        var mei = MeiReader.Renumber(raw, score);
        return (mei, score, Excerpts.Cut(score));
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

    // A piece's level is its hardest excerpt's; most of it reads at its excerpts' median.
    private static PieceRecord Graded(PieceRecord record, Score score, IReadOnlyList<ExcerptCut> cuts)
    {
        var whole = cuts.Count == 0 ? Grader.Grade(score) : null;
        var level = whole?.Level ?? cuts.Max(c => c.Grade.Level);
        var typical = whole?.Level ?? cuts.Select(c => c.Grade.Level).Order().ElementAt(cuts.Count / 2);
        var reasons = whole?.Reasons ?? [.. cuts.Where(c => c.Grade.Level == level).SelectMany(c => c.Grade.Reasons).GroupBy(r => r).OrderByDescending(g => g.Count()).Select(g => g.Key).Take(3)];
        return record with { Level = level, TypicalLevel = typical, Reasons = string.Join(", ", reasons), Bars = score.Bars.Count(b => b.Number > 0) };
    }
}
