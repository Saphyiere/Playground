using System.Text.Json;

namespace Prima.Core.Library;

/// <summary>
/// Where a board's piano syllabus lists Prima's own pieces: <c>rcm-2022.json</c> beside the bundle's manifest, for The Royal
/// Conservatory's Piano Syllabus, 2022 Edition. Each entry is a fact about one of the bundle's pieces, looked up in the
/// syllabus's free PDF and cited by its page, not a copy of the syllabus's lists; Prima isn't affiliated with the board. A
/// board's level rates a piece learned and played, as teachers place it, where Prima's own level is where its music reads at
/// sight, so the two are shown side by side and never worked out from each other (concept.md, Library).
/// </summary>
/// <param name="Board">The board's short name: "RCM".</param>
/// <param name="Edition">The syllabus's edition: "2022".</param>
/// <param name="Name">The syllabus by its full name, for the credits.</param>
/// <param name="Url">Where its free PDF is.</param>
public sealed record Syllabus(string Board, string Edition, string Name, string Url, IReadOnlyList<SyllabusEntry> Pieces)
{
    /// <summary>The bundle's file for RCM's syllabus, beside its manifest.</summary>
    public const string RcmFile = "rcm-2022.json";

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    /// <summary>The file read, each entry checked: a level from 1 to 10, a page, and either a list from A to D or an étude.</summary>
    public static Syllabus Read(string json)
    {
        var syllabus = JsonSerializer.Deserialize<Syllabus>(json, Json) ?? throw new JsonException("The syllabus file is empty.");
        if (string.IsNullOrWhiteSpace(syllabus.Board) || string.IsNullOrWhiteSpace(syllabus.Edition)) throw new JsonException("A syllabus names its board and edition.");
        foreach (var entry in syllabus.Pieces ?? [])
        {
            var listed = entry.List is "A" or "B" or "C" or "D";
            if (string.IsNullOrWhiteSpace(entry.Id) || entry.Level is < 1 or > 10 || entry.Page < 1 || listed == entry.Etude || (!listed && entry.List is not null))
            {
                throw new JsonException($"\"{entry.Id}\" needs a level from 1 to 10, a page, and either a list (A to D) or to be an étude.");
            }
        }
        return syllabus with { Pieces = syllabus.Pieces ?? [] };
    }

    /// <summary>Each listed piece's place on the syllabus, by the id the Library keeps it under.</summary>
    public IEnumerable<(string Piece, Listing Listing)> Listings => Pieces.Select(p => (p.Id, new Listing(Board, Edition, p.Level, p.Etude ? null : p.List, p.Page)));
}

/// <summary>A listed piece: its id in the bundle, its level, its list or that it's one of the level's études, its page, and what it is in a few words.</summary>
public sealed record SyllabusEntry(string Id, int Level, string? List, bool Etude, int Page, string? What);

/// <summary>A piece's place on a board's syllabus, as Prima shows it: under the piece in the Library's lists, and on its front.</summary>
/// <param name="List">A, B, C or D; none for one of the level's études.</param>
public sealed record Listing(string Board, string Edition, int Level, string? List, int Page)
{
    public bool Etude => List is null;

    /// <summary>Under a piece in a list: "RCM 3", never broken across two lines.</summary>
    public string Short => $"{Board}{NoBreak}{Level}";

    private const char NoBreak = (char)160;

    /// <summary>On a piece's front, in About this piece.</summary>
    public string Sentence => Etude
        ? $"{Board}'s {Edition} piano syllabus has it among the Level {Level} études (page {Page})."
        : $"{Board}'s {Edition} piano syllabus lists it at Level {Level}, List {List} (page {Page}).";
}
