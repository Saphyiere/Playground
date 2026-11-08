using System.IO.Compression;
using Microsoft.JSInterop;
using Prima.Core.Library;
using Prima.Core.Music;
using Prima.UI.Host;
using Prima.UI.Storage;

namespace Prima.UI.Library;

/// <summary>
/// Brings music into the Library. Bundled pieces come prepared (the Library tool engraved, split, graded and cut them when the
/// bundle was built), so the first time the Library opens they're only stored, in the background; a PDF is listed as it is. A
/// collection the tool built for this tablet alone (its local collection, a zip) is stored the same way when it's imported. An
/// imported file goes through Verovio to MEI in the web view, and is then numbered, cut and graded away from the screen's thread.
/// </summary>
public sealed class LibraryPrep(PrimaStore store, ILibraryBundle bundle, IJSRuntime js)
{
    private Task? running;
    private IJSObjectReference? module;

    /// <summary>Pieces stored so far, out of how many, while it runs.</summary>
    public (int Done, int Total) Progress { get; private set; }

    public bool Running { get; private set; }

    /// <summary>The collection being added, by its file's name, while it's added; null while it's the bundle.</summary>
    public string? Adding { get; private set; }

    /// <summary>Pieces that couldn't be read, by title.</summary>
    public List<string> Skipped { get; } = [];

    public event Action? Changed;

    /// <summary>Starts preparing the bundled pieces that aren't in the Library yet; while it runs, a second call is the same run.</summary>
    public Task EnsureAsync() => running ??= Run();

    private async Task Run()
    {
        var manifest = await bundle.ManifestAsync();
        if (manifest is null) return;

        var entries = Entries(manifest);
        if (entries.Count == 0) return;

        // A newer bundle's credits, titles and levels for PDFs reach pieces already here (their music and excerpts stay), and
        // bundled pieces it no longer has go: a collection that's now its separate pieces, say.
        var have = await store.GetPieceIdsAsync();
        await store.RefreshBundleAsync([.. entries.Select(e => (e.Part?.Id ?? e.Piece.Id, e.Part?.Title ?? e.Piece.Title, e.Piece))]);

        var missing = entries.Where(e => !have.Contains(e.Part?.Id ?? e.Piece.Id)).ToList();
        if (missing.Count > 0) await StoreAll(missing, "bundle", bundle.ReadAsync, null);
    }

    // What a manifest holds, as the Library keeps it: a PDF as it is, a MusicXML piece as prepared (a collection as its pieces).
    private static List<(BundledPiece Piece, PreparedPiece? Part)> Entries(LibraryManifest manifest) =>
        [.. manifest.Pieces.SelectMany(p => p.Format == "pdf" ? [(Piece: p, Part: (PreparedPiece?)null)] : (p.Prepared ?? []).Select(x => (Piece: p, Part: (PreparedPiece?)x)))];

    // Stores pieces one at a time, with the progress shown: a prepared piece with its MEI and excerpts; a PDF as it is, or from a
    // collection copied in beside the imported files with its MIDI.
    private async Task StoreAll(List<(BundledPiece Piece, PreparedPiece? Part)> entries, string origin, Func<string, Task<byte[]?>> read, string? adding)
    {
        (Running, Adding, Progress) = (true, adding, (0, entries.Count));
        Changed?.Invoke();
        foreach (var (piece, part) in entries)
        {
            var id = part?.Id ?? piece.Id;
            var record = new PieceRecord(id, origin, part?.Title ?? piece.Title, piece.Composer, piece.Work, part is null ? "pdf" : "mei", piece.File, piece.Midi,
                part?.Level ?? piece.Anchor, part?.TypicalLevel ?? piece.Anchor, part?.Reasons, part?.Bars ?? piece.Bars, piece.Source, piece.SourceUrl,
                piece.Licence, piece.LicenceUrl, piece.Credit, piece.Tier.ToString(), null, null, null, DateTimeOffset.Now, null);
            try
            {
                if (part is null && origin == "bundle") await store.SavePieceAsync(record, null, []);
                else if (part is null)
                {
                    var pdf = await read(piece.File) ?? throw new InvalidOperationException($"{piece.File} isn't in it.");
                    var midi = piece.Midi is { } named ? await read(named) : null;
                    Directory.CreateDirectory(store.FilesPath);
                    await File.WriteAllBytesAsync(Path.Combine(store.FilesPath, id + ".pdf"), pdf);
                    if (midi is not null) await File.WriteAllBytesAsync(Path.Combine(store.FilesPath, id + ".mid"), midi);
                    await store.SavePieceAsync(record with { File = id + ".pdf", Midi = midi is null ? null : id + ".mid" }, null, []);
                }
                else
                {
                    var bytes = await read(part.Mei) ?? throw new InvalidOperationException($"{part.Mei} isn't in it.");
                    var mei = await Task.Run(() =>
                    {
                        using var reader = new StreamReader(new GZipStream(new MemoryStream(bytes), CompressionMode.Decompress));
                        return reader.ReadToEnd();
                    });
                    await store.SavePieceAsync(record, mei, part.Excerpts);
                }
            }
            catch (Exception e) when (e is InvalidDataException or InvalidOperationException or IOException)
            {
                Skipped.Add(record.Title);
            }
            Progress = (Progress.Done + 1, Progress.Total);
            Changed?.Invoke();
        }
        (Running, Adding) = (false, null);
        Changed?.Invoke();
    }

    /// <summary>
    /// Imports the files the owner picked, one or several: MusicXML (.musicxml, .xml, .mxl), MEI or ABC as notation, MIDI written
    /// out as notation, PDFs as pages, and zips holding any of them. A PDF takes the MIDI file of the same name, when there's one,
    /// to be heard on the piano and scrolled at its tempo (the MIDI comes in as notation as well). A collection from the Library
    /// tool (a zip with its manifest) is stored in the background with its progress shown. The piece to open when one came in, and
    /// a line about what came in and what couldn't.
    /// </summary>
    public async Task<(string? Id, string? Note)> ImportAsync(IReadOnlyList<(string Name, byte[] Data)> picked)
    {
        // A zip is opened up and its music taken like any file picked; anything else in it (a text file, a picture) is passed over.
        var files = new List<(string Name, byte[] Data)>();
        var notes = new List<string>();
        foreach (var (name, data) in picked)
        {
            if (!name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
            {
                files.Add((name, data));
                continue;
            }

            ZipArchive zip;
            try
            {
                zip = new ZipArchive(new MemoryStream(data), ZipArchiveMode.Read);
            }
            catch (InvalidDataException)
            {
                notes.Add($"{name} isn't a zip that opens.");
                continue;
            }

            // The Library tool's collection: its manifest says what's in it.
            LibraryManifest? collection = null;
            if (zip.GetEntry("manifest.json") is { } manifest)
            {
                try
                {
                    await using var stream = manifest.Open();
                    collection = LibraryManifest.Read(await new StreamReader(stream).ReadToEndAsync());
                }
                catch (Exception e) when (e is InvalidDataException or System.Text.Json.JsonException)
                {
                }
            }
            if (collection is { Pieces.Count: > 0 })
            {
                var have = await store.GetPieceIdsAsync();
                var adding = Entries(collection).Where(e => !have.Contains(e.Part?.Id ?? e.Piece.Id)).ToList();
                if (adding.Count == 0)
                {
                    zip.Dispose();
                    notes.Add($"Everything in {name} is in the Library already.");
                    continue;
                }

                // After the bundle's own run (started here if the Library hasn't been opened yet), so one thing is stored at a time.
                var before = EnsureAsync();
                running = AddCollection(before, zip, adding, name);
                continue;
            }

            using (zip)
            {
                var music = zip.Entries.Where(e => e.Name.Length > 0 && !e.Name.StartsWith('.') && !e.FullName.StartsWith("__MACOSX/", StringComparison.Ordinal)
                    && Path.GetExtension(e.Name).ToLowerInvariant() is ".mxl" or ".musicxml" or ".xml" or ".mei" or ".abc" or ".mid" or ".midi" or ".pdf").ToList();
                if (music.Count == 0) notes.Add($"{name} has no music in it that Prima reads.");
                foreach (var entry in music)
                {
                    await using var stream = entry.Open();
                    using var bytes = new MemoryStream();
                    await stream.CopyToAsync(bytes);
                    files.Add((entry.Name, bytes.ToArray()));
                }
            }
        }

        // A PDF's MIDI is the MIDI file of the same name beside it: "Numb.pdf" and "Numb.mid".
        var midiOf = files.Where(f => Path.GetExtension(f.Name).ToLowerInvariant() is ".mid" or ".midi")
            .GroupBy(f => Path.GetFileNameWithoutExtension(f.Name), StringComparer.OrdinalIgnoreCase).ToDictionary(g => g.Key, g => g.First().Data, StringComparer.OrdinalIgnoreCase);
        var imported = new List<string>();
        foreach (var (name, data) in files)
        {
            var extension = Path.GetExtension(name).ToLowerInvariant();
            var title = Path.GetFileNameWithoutExtension(name).Replace('_', ' ');
            var id = $"import-{Guid.NewGuid():N}";
            var record = new PieceRecord(id, "import", title, "", null, "mei", name, null, null, null, null, null, null, null, null, null, null, null, null, null, null, DateTimeOffset.Now, null);
            switch (extension)
            {
                case ".pdf":
                    if (data.Length < 5 || data[0] != '%' || data[1] != 'P' || data[2] != 'D' || data[3] != 'F')
                    {
                        notes.Add($"{name} isn't a PDF.");
                        continue;
                    }
                    var midi = midiOf.GetValueOrDefault(Path.GetFileNameWithoutExtension(name));
                    Directory.CreateDirectory(store.FilesPath);
                    await File.WriteAllBytesAsync(Path.Combine(store.FilesPath, id + ".pdf"), data);
                    if (midi is not null) await File.WriteAllBytesAsync(Path.Combine(store.FilesPath, id + ".mid"), midi);
                    await store.SavePieceAsync(record with { Format = "pdf", File = id + ".pdf", Midi = midi is null ? null : id + ".mid" }, null, []);
                    imported.Add(id);
                    continue;
                case ".mxl" or ".musicxml" or ".xml" or ".mei" or ".abc" or ".mid" or ".midi":
                    try
                    {
                        // A MIDI file has no notation of its own: the Core writes it out (MidiNotation says how), and it's read as MEI.
                        var score = extension is ".mid" or ".midi" ? await Task.Run(() => MidiNotation.Read(data, title)) : null;
                        var bytes = score is null ? data : System.Text.Encoding.UTF8.GetBytes(MeiWriter.Write(score));
                        var (mei, graded, excerpts) = await ReadMusic(bytes, score is null ? extension.TrimStart('.') : "mei", record);
                        var (fileTitle, composer) = Header(mei);
                        await store.SavePieceAsync(graded with { Title = fileTitle ?? title, Composer = composer ?? "" }, mei, excerpts);
                        imported.Add(id);
                    }
                    catch (Exception e) when (e is JSException or System.Xml.XmlException or FormatException or InvalidOperationException or ArgumentException)
                    {
                        notes.Add($"{name} couldn't be read as music.");
                    }
                    continue;
                case ".mscz" or ".mscx":
                    notes.Add($"{name}: export it from MuseScore as MusicXML (File, Export), then import that.");
                    continue;
                case ".krn":
                    notes.Add($"{name}: Humdrum files need converting to MusicXML first (Verovio's own site does it).");
                    continue;
                default:
                    notes.Add($"{name}: Prima reads MusicXML, MEI, ABC, MIDI and PDF files, and zips of them, not {extension} ones.");
                    continue;
            }
        }

        if (imported.Count == 1 && notes.Count == 0) return (imported[0], null);
        var count = imported.Count switch { 0 => null, 1 => "1 piece imported.", _ => $"{imported.Count} pieces imported." };
        return (null, notes.Count == 0 && count is null ? null : string.Join(" ", new[] { count }.Concat(notes).OfType<string>()));
    }

    private async Task AddCollection(Task before, ZipArchive zip, List<(BundledPiece Piece, PreparedPiece? Part)> adding, string name)
    {
        await before;
        using (zip)
        {
            // An entry's bytes, read away from the screen's thread; one at a time, as the zip allows.
            await StoreAll(adding, "collection", path => Task.Run(() =>
            {
                if (zip.GetEntry(path) is not { } entry) return (byte[]?)null;
                using var stream = entry.Open();
                using var bytes = new MemoryStream();
                stream.CopyTo(bytes);
                return bytes.ToArray();
            }), name);
        }
    }

    /// <summary>
    /// Keeps a PDF's notes as Claude read them (Transcription's MEI) as the piece's notation: graded and cut into excerpts like an
    /// import, its PDF and layout kept beside it for its print.
    /// </summary>
    public async Task KeepReadingAsync(PieceRecord piece, string mei)
    {
        var (prepared, graded, excerpts) = await ReadMusic(System.Text.Encoding.UTF8.GetBytes(mei), "mei", piece);
        await store.SavePieceAsync(graded, prepared, excerpts);
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
