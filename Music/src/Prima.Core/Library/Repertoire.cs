namespace Prima.Core.Library;

/// <summary>A piece kept in the repertoire, and since when.</summary>
public sealed record RepertoirePiece(string Piece, DateTimeOffset Since);

/// <summary>A collection of the repertoire's pieces under a name of the player's own ("Learning pieces 5"), in the order they were put in.</summary>
public sealed record RepertoireCollection(string Id, string Name, IReadOnlyList<string> Pieces);

/// <summary>
/// The repertoire: the pieces the player keeps to come back to, starred in the Library, and the collections some of them are
/// put in. A piece is in the repertoire once it's starred or put in a collection; taken out of the repertoire, it leaves every
/// collection too. It holds pieces by their ids, whether or not the Library still has them: a piece removed and put back is
/// still starred.
/// </summary>
public sealed record Repertoire(IReadOnlyList<RepertoirePiece> Pieces, IReadOnlyList<RepertoireCollection> Collections)
{
    /// <summary>Nothing kept yet.</summary>
    public static Repertoire Empty { get; } = new([], []);

    /// <summary>Whether a piece is in the repertoire.</summary>
    public bool Has(string piece) => Pieces.Any(p => p.Piece == piece);

    /// <summary>The collections a piece is in.</summary>
    public IEnumerable<RepertoireCollection> Holding(string piece) => Collections.Where(c => c.Pieces.Contains(piece));

    /// <summary>With a piece starred (as it is, when it already is).</summary>
    public Repertoire With(string piece, DateTimeOffset at) => Has(piece) ? this : this with { Pieces = [.. Pieces, new RepertoirePiece(piece, at)] };

    /// <summary>With a piece taken out: of the repertoire and of every collection.</summary>
    public Repertoire Without(string piece) =>
        new([.. Pieces.Where(p => p.Piece != piece)], [.. Collections.Select(c => c with { Pieces = [.. c.Pieces.Where(p => p != piece)] })]);

    /// <summary>
    /// With a new collection of that name, empty; as it is when the name is blank or one already has it (big and small letters
    /// aside). <paramref name="id"/> is the new collection's.
    /// </summary>
    public Repertoire Named(string name, string id) =>
        Clean(name) is not { Length: > 0 } clean || Called(clean) is not null ? this : this with { Collections = [.. Collections, new RepertoireCollection(id, clean, [])] };

    /// <summary>The collection of that name, big and small letters aside.</summary>
    public RepertoireCollection? Called(string name) => Collections.FirstOrDefault(c => string.Equals(c.Name, Clean(name), StringComparison.OrdinalIgnoreCase));

    /// <summary>With a piece put in a collection (and so in the repertoire), or taken out of that collection alone.</summary>
    public Repertoire In(string collection, string piece, bool there, DateTimeOffset at)
    {
        if (Collections.All(c => c.Id != collection)) return this;

        var kept = there ? With(piece, at) : this;
        return kept with
        {
            Collections = [.. kept.Collections.Select(c => c.Id != collection ? c
                : there ? (c.Pieces.Contains(piece) ? c : c with { Pieces = [.. c.Pieces, piece] })
                : c with { Pieces = [.. c.Pieces.Where(p => p != piece)] })],
        };
    }

    /// <summary>With a collection given another name; as it is when the name is blank or another collection has it.</summary>
    public Repertoire Renamed(string collection, string name) =>
        Clean(name) is not { Length: > 0 } clean || Called(clean) is { } other && other.Id != collection ? this
            : this with { Collections = [.. Collections.Select(c => c.Id == collection ? c with { Name = clean } : c)] };

    /// <summary>With a collection gone; its pieces stay in the repertoire.</summary>
    public Repertoire Dropped(string collection) => this with { Collections = [.. Collections.Where(c => c.Id != collection)] };

    private static string Clean(string name) => string.Join(' ', name.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
}
