// Builds the Library's music: public-domain and openly licensed piano music, chosen by the named works in Music/docs/library.md,
// into Music/library with the manifest the app reads. The result is kept in the repo, so the app builds with it as it is; run
// this again only to change the selection. Each MusicXML piece is then prepared (Prepare.cs), which needs Node.js for Verovio.
//
//   dotnet run --project Music/tools/Prima.Library            both sources, then prepare
//   dotnet run --project Music/tools/Prima.Library -- pdmx    PDMX only (MusicXML from MuseScore users, about 10 minutes)
//   dotnet run --project Music/tools/Prima.Library -- mutopia the Mutopia Project only (PDF and MIDI)
//   dotnet run --project Music/tools/Prima.Library -- prepare only prepare what's there again (after a change to the grader)
//
// The local collection is more music, for your own tablet only, and never committed: music whose terms allow personal,
// non-commercial use but not a place in a public repo. Craig Sapp's Humdrum editions and the DCML's MuseScore corpora, both
// CC BY-NC-SA 4.0 (Humdrum.cs, Dcml.cs), are prepared the same way into artifacts/library-local and packed as
// artifacts/prima-collection.zip, which the app imports (Library, Import a file). It also needs npm, which comes with Node.js,
// and git (without git, each repository comes as a zip).
//
//   dotnet run --project Music/tools/Prima.Library -- local           both its sources, then prepare and pack
//   dotnet run --project Music/tools/Prima.Library -- local humdrum   one source again (or dcml, or prepare)
//
// What it downloads is kept in artifacts/library-cache, so a second run only fetches what's missing.
using System.Diagnostics;
using System.IO.Compression;
using System.Text.RegularExpressions;
using Prima.Core.Library;
using Prima.Library;

var root = FindRoot(AppContext.BaseDirectory) ?? FindRoot(Directory.GetCurrentDirectory()) ?? throw new InvalidOperationException("Run this inside the Playground repo.");
var local = args.FirstOrDefault()?.ToLowerInvariant() == "local";
var output = local ? Path.Combine(root, "artifacts", "library-local") : Path.Combine(root, "Music", "library");
var cache = Path.Combine(root, "artifacts", "library-cache");
Directory.CreateDirectory(Path.Combine(output, "pieces"));
Directory.CreateDirectory(cache);

var only = args.Skip(local ? 1 : 0).FirstOrDefault()?.ToLowerInvariant();
using var http = new HttpClient { Timeout = Timeout.InfiniteTimeSpan };
http.DefaultRequestHeaders.UserAgent.ParseAdd("Prima-Library/1.0");

var manifestPath = Path.Combine(output, "manifest.json");
var previous = File.Exists(manifestPath) ? LibraryManifest.Read(await File.ReadAllTextAsync(manifestPath)).Pieces : [];
var pieces = new List<BundledPiece>();
var clock = Stopwatch.StartNew();
string? modules = null;

if (local)
{
    // Verovio's build that reads Humdrum (the app's version of Verovio) and webmscore, from npm, installed once.
    modules = Path.Combine(cache, "node", "node_modules");
    if (!File.Exists(Path.Combine(modules, "verovio", "package.json")) || !File.Exists(Path.Combine(modules, "webmscore", "package.json")))
    {
        Console.WriteLine("Installing Verovio's Humdrum build and webmscore from npm…");
        string[] install = ["install", "--prefix", Path.Combine(cache, "node"), "--no-audit", "--no-fund", "verovio@6.3.0", "webmscore@1.2.1"];
        using var npm = Process.Start(OperatingSystem.IsWindows() ? new ProcessStartInfo("cmd.exe", ["/c", "npm", .. install]) : new ProcessStartInfo("npm", install))!;
        await npm.WaitForExitAsync();
        if (npm.ExitCode != 0) throw new InvalidOperationException("npm couldn't install them. The local collection needs Node.js with npm (nodejs.org).");
    }

    if (only is null or "humdrum") pieces.AddRange(await Humdrum.Fetch(http, cache, output));
    else pieces.AddRange(previous.Where(p => p.Source == Humdrum.Name));

    if (only is null or "dcml") pieces.AddRange(await Dcml.Fetch(http, cache, output, modules, root));
    else pieces.AddRange(previous.Where(p => p.Source == Dcml.Name));

    // Scarlatti's sonatas are in both; where they meet, Sapp's edition stays.
    string Kirkpatrick(BundledPiece p) => Regex.Match(p.Title, @"K\.\s?(\d+)").Groups[1].Value;
    var sapp = pieces.Where(p => p.Source == Humdrum.Name && p.Composer == "Domenico Scarlatti").Select(Kirkpatrick).ToHashSet();
    pieces.RemoveAll(p => p.Source == Dcml.Name && p.Composer == "Domenico Scarlatti" && sapp.Contains(Kirkpatrick(p)));
}
else
{
    if (only is null or "pdmx") pieces.AddRange(await Pdmx.Fetch(http, cache, output));
    else pieces.AddRange(previous.Where(p => p.Source == Pdmx.Name));

    if (only is null or "mutopia") pieces.AddRange(await Mutopia.Fetch(http, output));
    else pieces.AddRange(previous.Where(p => p.Source == Mutopia.Name));
}

pieces = await Prepare.Run(pieces, root, output, modules);

// Files no piece names any more (a piece dropped from the selection) are removed, so the bundle holds only what's listed.
var named = pieces.SelectMany(p => new[] { p.File, p.Midi }.Concat(p.Prepared?.Select(x => x.Mei) ?? [])).OfType<string>().Select(f => Path.GetFullPath(Path.Combine(output, f))).ToHashSet();
foreach (var file in Directory.EnumerateFiles(Path.Combine(output, "pieces")).Where(f => !named.Contains(Path.GetFullPath(f)))) File.Delete(file);

var manifest = new LibraryManifest(1, DateTime.UtcNow.ToString("yyyy-MM-dd"), [.. pieces.OrderBy(p => p.Composer).ThenBy(p => p.Work).ThenBy(p => p.Title, StringComparer.OrdinalIgnoreCase)]);
await File.WriteAllTextAsync(manifestPath, manifest.Write());
var kinds = string.Join(", ", manifest.Pieces.GroupBy(p => p.Format).Select(g => $"{g.Count()} {(g.Key switch { "mxl" => "MusicXML", "krn" => "Humdrum", _ => "PDF" })}"));
if (!local)
{
    Console.WriteLine($"{manifest.Pieces.Count} pieces in {output} ({kinds}), in {clock.Elapsed:m\\:ss}. Commit them, then build the app.");
    return;
}

// The collection as one file for the tablet: the manifest and what the app reads (the MEI, any PDFs and MIDI), not the sources.
var collection = Path.Combine(root, "artifacts", "prima-collection.zip");
using (var zip = ZipFile.Open(collection + ".part", ZipArchiveMode.Create))
{
    zip.CreateEntryFromFile(manifestPath, "manifest.json", CompressionLevel.SmallestSize);
    foreach (var file in manifest.Pieces.SelectMany(p => p.Format == "pdf" ? [p.File, p.Midi] : p.Prepared?.Select(x => (string?)x.Mei) ?? []).OfType<string>())
    {
        zip.CreateEntryFromFile(Path.Combine(output, file), file, CompressionLevel.NoCompression);
    }
}
File.Move(collection + ".part", collection, overwrite: true);
var parts = manifest.Pieces.Sum(p => p.Prepared?.Count ?? 1);
Console.WriteLine($"{parts} pieces in {collection} ({new FileInfo(collection).Length / 1_000_000} MB; {kinds}), in {clock.Elapsed:m\\:ss}. " +
    "Copy it to the tablet and open it in the Library with Import a file. It stays out of the repo.");

static string? FindRoot(string from)
{
    for (var dir = new DirectoryInfo(from); dir is not null; dir = dir.Parent)
    {
        if (File.Exists(Path.Combine(dir.FullName, "Directory.Build.props")) && Directory.Exists(Path.Combine(dir.FullName, "Music"))) return dir.FullName;
    }
    return null;
}
