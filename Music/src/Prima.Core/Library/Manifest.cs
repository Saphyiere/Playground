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
/// from its notes, so it carries <see cref="Anchor"/>: about where its collection sits on the ladder (library.md, §2).
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
    int? Anchor = null);

/// <summary>The bundle's index: <c>manifest.json</c> at the root of the bundle.</summary>
public sealed record LibraryManifest(int Version, string Built, IReadOnlyList<BundledPiece> Pieces)
{
    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { WriteIndented = true, Converters = { new JsonStringEnumConverter() } };

    public static LibraryManifest Read(string json) => JsonSerializer.Deserialize<LibraryManifest>(json, Json) ?? new LibraryManifest(1, "", []);

    public string Write() => JsonSerializer.Serialize(this, Json);
}
