// Builds the Library's music: public-domain and openly licensed piano music, chosen by the named works in Music/docs/library.md,
// into Music/library with the manifest the app reads. The result is kept in the repo, so the app builds with it as it is; run
// this again only to change the selection. Each MusicXML piece is then prepared (Prepare.cs), which needs Node.js for Verovio.
//
//   dotnet run --project Music/tools/Prima.Library            both sources, then prepare
//   dotnet run --project Music/tools/Prima.Library -- pdmx    PDMX only (MusicXML from MuseScore users, about 10 minutes)
//   dotnet run --project Music/tools/Prima.Library -- mutopia the Mutopia Project only (PDF and MIDI)
//   dotnet run --project Music/tools/Prima.Library -- prepare only prepare what's there again (after a change to the grader)
//
// What it downloads is kept in artifacts/library-cache, so a second run only fetches what's missing.
using System.Diagnostics;
using Prima.Core.Library;
using Prima.Library;

var root = FindRoot(AppContext.BaseDirectory) ?? FindRoot(Directory.GetCurrentDirectory()) ?? throw new InvalidOperationException("Run this inside the Playground repo.");
var output = Path.Combine(root, "Music", "library");
var cache = Path.Combine(root, "artifacts", "library-cache");
Directory.CreateDirectory(Path.Combine(output, "pieces"));
Directory.CreateDirectory(cache);

var only = args.FirstOrDefault()?.ToLowerInvariant();
using var http = new HttpClient { Timeout = Timeout.InfiniteTimeSpan };
http.DefaultRequestHeaders.UserAgent.ParseAdd("Prima-Library/1.0");

var manifestPath = Path.Combine(output, "manifest.json");
var previous = File.Exists(manifestPath) ? LibraryManifest.Read(await File.ReadAllTextAsync(manifestPath)).Pieces : [];
var pieces = new List<BundledPiece>();
var clock = Stopwatch.StartNew();

if (only is null or "pdmx") pieces.AddRange(await Pdmx.Fetch(http, cache, output));
else pieces.AddRange(previous.Where(p => p.Source == Pdmx.Name));

if (only is null or "mutopia") pieces.AddRange(await Mutopia.Fetch(http, output));
else pieces.AddRange(previous.Where(p => p.Source == Mutopia.Name));

pieces = await Prepare.Run(pieces, root, output);

// Files no piece names any more (a piece dropped from the selection) are removed, so the bundle holds only what's listed.
var named = pieces.SelectMany(p => new[] { p.File, p.Midi }.Concat(p.Prepared?.Select(x => x.Mei) ?? [])).OfType<string>().Select(f => Path.GetFullPath(Path.Combine(output, f))).ToHashSet();
foreach (var file in Directory.EnumerateFiles(Path.Combine(output, "pieces")).Where(f => !named.Contains(Path.GetFullPath(f)))) File.Delete(file);

var manifest = new LibraryManifest(1, DateTime.UtcNow.ToString("yyyy-MM-dd"), [.. pieces.OrderBy(p => p.Composer).ThenBy(p => p.Work).ThenBy(p => p.Title, StringComparer.OrdinalIgnoreCase)]);
await File.WriteAllTextAsync(manifestPath, manifest.Write());
Console.WriteLine($"{manifest.Pieces.Count} pieces in {output} ({manifest.Pieces.Count(p => p.Format == "mxl")} MusicXML, {manifest.Pieces.Count(p => p.Format == "pdf")} PDF), in {clock.Elapsed:m\\:ss}. Commit them, then build the app.");

static string? FindRoot(string from)
{
    for (var dir = new DirectoryInfo(from); dir is not null; dir = dir.Parent)
    {
        if (File.Exists(Path.Combine(dir.FullName, "Directory.Build.props")) && Directory.Exists(Path.Combine(dir.FullName, "Music"))) return dir.FullName;
    }
    return null;
}
