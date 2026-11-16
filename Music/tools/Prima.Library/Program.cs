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
// non-commercial use but not a place in a public repo, and more public-domain and openly licensed music than the repo keeps. Its
// sources, each with its own file here:
//
//   humdrum   Craig Sapp's Humdrum editions (Mozart's, Haydn's and Scarlatti's sonatas, Joplin, Bach's chorales), CC BY-NC-SA 4.0
//   dcml      the DCML's MuseScore corpora (Beethoven's sonatas, Chopin's mazurkas, Debussy, Grieg and more), CC BY-NC-SA 4.0
//   asap      ASAP's concert scores (Bach's Well-Tempered Clavier, Chopin's études and ballades, Liszt, Schubert), CC BY-NC-SA 4.0
//   pdmx      more of PDMX: public-domain and CC0 piano music whose own composer field names a composer who died before 1931
//   mutopia   the rest of the Mutopia Project's keyboard music, as PDF and MIDI: public domain, CC BY and CC BY-SA (share-alike)
//   hymns     the Open Hymnal's public-domain hymns, four voices on two staves with their verses (ABC, written as MEI by abc2svg)
//
// They're prepared the same way into artifacts/library-local and packed into artifacts/prima-collection, a zip or a few for each
// part, which the app imports (Library, Import files); contents.txt there says what's in each. It also needs npm, which comes with
// Node.js, and git (without git, each repository comes as a zip).
//
//   dotnet run --project Music/tools/Prima.Library -- local           all its sources, then prepare and pack
//   dotnet run --project Music/tools/Prima.Library -- local pdmx      one source again (or humdrum, dcml, asap, mutopia, hymns), or prepare
//
// What it downloads is kept in artifacts/library-cache, so a second run only fetches what's missing. --artifacts <folder> puts
// that, the collection and its zips in another folder (the cache is over 2 GB).
using System.Diagnostics;
using System.IO.Compression;
using System.Text;
using System.Text.RegularExpressions;
using Prima.Core.Library;
using Prima.Library;

var root = FindRoot(AppContext.BaseDirectory) ?? FindRoot(Directory.GetCurrentDirectory()) ?? throw new InvalidOperationException("Run this inside the Playground repo.");
var at = Array.IndexOf(args, "--artifacts");
var artifacts = at >= 0 && at + 1 < args.Length ? Path.GetFullPath(args[at + 1]) : Path.Combine(root, "artifacts");
args = at >= 0 ? [.. args.Take(at), .. args.Skip(at + 2)] : args;

var local = args.FirstOrDefault()?.ToLowerInvariant() == "local";
var output = local ? Path.Combine(artifacts, "library-local") : Path.Combine(root, "Music", "library");
var cache = Path.Combine(artifacts, "library-cache");
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
    // Verovio's build that reads Humdrum (the app's version of Verovio), webmscore and abc2svg, from npm, installed once. abc2svg
    // 1.22.1 is the last its author put on npm (2022; npm now calls it unsupported), and does what's needed.
    modules = Path.Combine(cache, "node", "node_modules");
    if (new[] { "verovio", "webmscore", "abc2svg" }.Any(m => !File.Exists(Path.Combine(modules, m, "package.json"))))
    {
        Console.WriteLine("Installing Verovio's Humdrum build, webmscore and abc2svg from npm…");
        string[] install = ["install", "--prefix", Path.Combine(cache, "node"), "--no-audit", "--no-fund", "verovio@6.3.0", "webmscore@1.2.1", "abc2svg@1.22.1"];
        using var npm = Process.Start(OperatingSystem.IsWindows() ? new ProcessStartInfo("cmd.exe", ["/c", "npm", .. install]) : new ProcessStartInfo("npm", install))!;
        await npm.WaitForExitAsync();
        if (npm.ExitCode != 0) throw new InvalidOperationException("npm couldn't install them. The local collection needs Node.js with npm (nodejs.org).");
    }

    // What the repo's bundle has already (Music/library) stays out of the collection.
    var bundle = LibraryManifest.Read(await File.ReadAllTextAsync(Path.Combine(root, "Music", "library", "manifest.json"))).Pieces;
    var bundled = bundle.SelectMany(p => p.Prepared?.Select(x => x.Id).Prepend(p.Id) ?? [p.Id]).ToHashSet(StringComparer.Ordinal);

    // Each source fetched again, or as the last run left it.
    async Task Source(string name, string source, Func<Task<List<BundledPiece>>> fetch) =>
        pieces.AddRange(only is null || only == name ? await fetch() : previous.Where(p => p.Source == source));
    await Source("humdrum", Humdrum.Name, () => Humdrum.Fetch(http, cache, output));
    await Source("dcml", Dcml.Name, () => Dcml.Fetch(http, cache, output, modules, root));
    await Source("asap", Asap.Name, () => Asap.Fetch(http, cache, output));
    await Source("pdmx", Pdmx.Name, () => Pdmx.FetchMore(http, cache, output));
    await Source("mutopia", Mutopia.Name, () => Mutopia.FetchMore(http, cache, output, bundled));
    await Source("hymns", Hymns.Name, () => Hymns.Fetch(http, cache, output, modules, root));
    pieces.RemoveAll(p => bundled.Contains(p.Id));

    // Scarlatti's sonatas are in both; where they meet, Sapp's edition stays.
    string Kirkpatrick(BundledPiece p) => Regex.Match(p.Title, @"K\.\s?(\d+)").Groups[1].Value;
    var sapp = pieces.Where(p => p.Source == Humdrum.Name && p.Composer == "Domenico Scarlatti").Select(Kirkpatrick).ToHashSet();
    pieces.RemoveAll(p => p.Source == Dcml.Name && p.Composer == "Domenico Scarlatti" && sapp.Contains(Kirkpatrick(p)));

    // A piece the scholarly editions have (Sapp's, the DCML's) comes from them alone, and one ASAP has comes from it before PDMX:
    // the same composer and catalogue number, or name ("Clair de lune"). Mutopia's PDFs stay beside them, being a different thing.
    var known = new HashSet<string>(StringComparer.Ordinal);
    foreach (var source in new[] { Humdrum.Name, Dcml.Name, Asap.Name, Pdmx.Name })
    {
        var copies = pieces.Where(p => p.Source == source && Catalogue.Keys(p.Title).Any(k => known.Contains($"{p.Composer}|{k}"))).ToHashSet();
        if (copies.Count > 0) Console.WriteLine($"{source}: {copies.Count} pieces left out, being in an edition above ({string.Join("; ", copies.Take(3).Select(p => $"{p.Composer}, {p.Title}"))}…).");
        pieces.RemoveAll(copies.Contains);
        known.UnionWith(pieces.Where(p => p.Source == source).SelectMany(p => Catalogue.Keys(p.Title).Select(k => $"{p.Composer}|{k}")));
    }
}
else
{
    if (only is null or "pdmx") pieces.AddRange(await Pdmx.Fetch(http, cache, output));
    else pieces.AddRange(previous.Where(p => p.Source == Pdmx.Name));

    if (only is null or "mutopia") pieces.AddRange(await Mutopia.Fetch(http, output));
    else pieces.AddRange(previous.Where(p => p.Source == Mutopia.Name));
}

var chosen = pieces.Select(p => p.File).ToList();
pieces = await Prepare.Run(pieces, root, output, modules, grandStaff: local);

// Files no piece names any more (a piece dropped from the selection) are removed, so the bundle holds only what's listed. A
// source that preparing left out stays, so the next run doesn't fetch it again only to leave it out.
var named = pieces.SelectMany(p => new[] { p.File, p.Midi }.Concat(p.Prepared?.Select(x => x.Mei) ?? [])).Concat(chosen).OfType<string>()
    .Select(f => Path.GetFullPath(Path.Combine(output, f))).ToHashSet();
foreach (var file in Directory.EnumerateFiles(Path.Combine(output, "pieces")).Where(f => !named.Contains(Path.GetFullPath(f)))) File.Delete(file);

var built = DateTime.UtcNow.ToString("yyyy-MM-dd");
var manifest = new LibraryManifest(1, built, [.. pieces.OrderBy(p => p.Composer).ThenBy(p => p.Work).ThenBy(p => p.Title, StringComparer.OrdinalIgnoreCase)]);
await File.WriteAllTextAsync(manifestPath, manifest.Write());
var kinds = string.Join(", ", manifest.Pieces.GroupBy(p => p.Format).Select(g => $"{g.Count()} {(g.Key switch { "mxl" => "MusicXML", "krn" => "Humdrum", "mei" => "ABC", _ => "PDF" })}"));
if (!local)
{
    Console.WriteLine($"{manifest.Pieces.Count} pieces in {output} ({kinds}), in {clock.Elapsed:m\\:ss}. Commit them, then build the app.");
    return;
}

// The collection as files for the tablet, a zip or a few for each part: the manifest of what's in it and what the app reads (the
// MEI, any PDFs and MIDI), not the sources. The tablet reads a zip it imports whole into memory, so none is much over 40 MB; a part
// that would be is split, its pieces in order. The scholarly editions (CC BY-NC-SA 4.0, for this tablet only) are the zip of old,
// prima-collection.zip; PDMX, Mutopia and the hymns have one each.
var folder = Path.Combine(artifacts, "prima-collection");
Directory.CreateDirectory(folder);
foreach (var old in Directory.GetFiles(folder, "prima-collection*.zip")) File.Delete(old);
(string Name, string Holds, string[] Sources)[] parts =
[
    ("prima-collection", "Sapp's Humdrum editions, the DCML's corpora and ASAP's scores, as notation", [Humdrum.Name, Dcml.Name, Asap.Name]),
    ("prima-collection-pdmx", "more of PDMX: MuseScore users' engravings of public-domain piano music, as notation", [Pdmx.Name]),
    ("prima-collection-mutopia", "the rest of the Mutopia Project's keyboard music, as PDF with its MIDI", [Mutopia.Name]),
    ("prima-collection-hymns", "the Open Hymnal's public-domain hymns, four voices on the grand staff with their verses, as notation", [Hymns.Name]),
];
const long Most = 40_000_000;
string[] Files(BundledPiece p) => [.. (p.Format == "pdf" ? [p.File, p.Midi] : p.Prepared?.Select(x => (string?)x.Mei) ?? []).OfType<string>()];
long Size(BundledPiece p) => Files(p).Sum(f => new FileInfo(Path.Combine(output, f)).Length);
var zips = new List<(string Name, string Holds, List<BundledPiece> Pieces, long Bytes)>();
foreach (var (name, holds, sources) in parts)
{
    var inPart = manifest.Pieces.Where(p => sources.Contains(p.Source)).ToList();
    var groups = new List<List<BundledPiece>>();
    long size = 0;
    foreach (var piece in inPart)
    {
        if (groups.Count == 0 || size + Size(piece) > Most && groups[^1].Count > 0)
        {
            groups.Add([]);
            size = 0;
        }
        groups[^1].Add(piece);
        size += Size(piece);
    }
    for (var i = 0; i < groups.Count; i++)
    {
        var zipName = groups.Count == 1 ? $"{name}.zip" : $"{name}-{i + 1}.zip";
        var path = Path.Combine(folder, zipName);
        using (var zip = ZipFile.Open(path, ZipArchiveMode.Create))
        {
            await using (var writer = new StreamWriter(zip.CreateEntry("manifest.json", CompressionLevel.SmallestSize).Open()))
            {
                await writer.WriteAsync(new LibraryManifest(1, built, groups[i]).Write());
            }
            foreach (var file in groups[i].SelectMany(Files)) zip.CreateEntryFromFile(Path.Combine(output, file), file, CompressionLevel.NoCompression);
        }
        zips.Add((zipName, groups.Count == 1 ? holds : $"{holds} ({i + 1} of {groups.Count}: {groups[i][0].Composer} to {groups[i][^1].Composer})", groups[i], new FileInfo(path).Length));
    }
}

// What's in each, by source, with its terms: printed, and kept beside the zips.
static int Count(IEnumerable<BundledPiece> some) => some.Sum(p => p.Prepared?.Count ?? 1);
var contents = new StringBuilder();
contents.AppendLine($"Prima's local collection, built {DateTime.Now:d MMMM yyyy, HH:mm}: {Count(manifest.Pieces)} pieces. For this tablet only; never commit it.").AppendLine();
contents.AppendLine($"{"Source",-11} {"Pieces",7} {"MB",6}  Licence");
foreach (var source in manifest.Pieces.GroupBy(p => p.Source))
{
    var licences = string.Join(", ", source.GroupBy(p => p.Licence).OrderByDescending(g => g.Count()).Select(g => $"{g.Key} ({Count(g)})"));
    contents.AppendLine($"{source.Key,-11} {Count(source),7} {source.Sum(Size) / 1_000_000.0,6:0.0}  {licences}");
}
contents.AppendLine().AppendLine("Zips (Library, Import files, one at a time):");
foreach (var (name, holds, inZip, bytes) in zips) contents.AppendLine($"  {name}: {Count(inZip)} pieces, {bytes / 1_000_000.0:0.0} MB. {char.ToUpperInvariant(holds[0])}{holds[1..]}.");
await File.WriteAllTextAsync(Path.Combine(folder, "contents.txt"), contents.ToString());
Console.WriteLine();
Console.Write(contents);
Console.WriteLine($"{Count(manifest.Pieces)} pieces in {folder} ({kinds}), in {clock.Elapsed:h\\:mm\\:ss}. Copy the zips to the tablet and open them in the " +
    "Library with Import files, one at a time. They stay out of the repo.");

static string? FindRoot(string from)
{
    for (var dir = new DirectoryInfo(from); dir is not null; dir = dir.Parent)
    {
        if (File.Exists(Path.Combine(dir.FullName, "Directory.Build.props")) && Directory.Exists(Path.Combine(dir.FullName, "Music"))) return dir.FullName;
    }
    return null;
}
