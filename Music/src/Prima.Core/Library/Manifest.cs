using System.Text.Json;
using System.Text.Json.Serialization;

namespace Prima.Core.Library;

/// <summary>Where a piece came from, and the terms it's used on.</summary>
public enum Tier
{
    /// <summary>Public domain, CC0 or CC BY: no strings beyond credit.</summary>
    A,

    /// <summary>Non-commercial, share-alike: fine for this personal app with credit shown, never in anything sold.</summary>
    B,
}

/// <summary>
/// A piece bundled with the app: its file (MusicXML zipped as .mxl, or a PDF with the MIDI made from the same engraving), and
/// everything the credits need. Written by the Library tool (Music/tools/Prima.Library), read by the app. A PDF can't be graded
/// from its notes, so it carries <see cref="Anchor"/>: about where its collection sits on the ladder (library.md, §2). A MusicXML
/// piece is <see cref="Prepared"/> when the bundle is built, so the app only stores it.
/// </summary>
public sealed record BundledPiece(
    string Id,
    string Title,
    string Composer,
    string Work,
    string Format,
    string File,
    string? Midi,
    string Sha256,
    string Source,
    string SourceUrl,
    string Licence,
    string LicenceUrl,
    string Credit,
    Tier Tier,
    int? Bars,
    int? Anchor = null,
    IReadOnlyList<PreparedPiece>? Prepared = null);

/// <summary>
/// A MusicXML piece as the app stores it, made when the bundle is built: its engraving as Verovio makes it, with the bars
/// numbered (<see cref="Mei"/>, a gzipped file in the bundle), its grade and its excerpts. A collection engraved as one score
/// has one for each of its pieces, numbered after it ("Children's Album, No. 5").
/// </summary>
public sealed record PreparedPiece(string Id, string Title, string Mei, int Level, int TypicalLevel, string Reasons, int Bars, IReadOnlyList<BundledExcerpt> Excerpts);

/// <summary>An excerpt of a prepared piece for the sight-reading track: its bars, its level, and how far into the level it sits.</summary>
public sealed record BundledExcerpt(int FirstBar, int LastBar, int Level, double Within);

/// <summary>The bundle's index: <c>manifest.json</c> at the root of the bundle.</summary>
public sealed record LibraryManifest(int Version, string Built, IReadOnlyList<BundledPiece> Pieces)
{
    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { WriteIndented = true, Converters = { new JsonStringEnumConverter() } };

    public static LibraryManifest Read(string json) => JsonSerializer.Deserialize<LibraryManifest>(json, Json) ?? new LibraryManifest(1, "", []);

    public string Write() => JsonSerializer.Serialize(this, Json);
}
