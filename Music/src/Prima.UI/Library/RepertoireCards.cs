using Prima.Core.Library;
using Prima.Core.Music;
using Prima.Core.Practice;
using Prima.UI.Storage;

namespace Prima.UI.Library;

/// <summary>
/// A piece kept in the repertoire as its card shows it: since when it's kept, what's been done on it, where it stands bar by
/// bar from its saved runs. A piece not played yet, or one with pages only, has no standing.
/// </summary>
public sealed record PieceCard(PieceRecord Piece, DateTimeOffset Since, PieceHistory? Done, PieceStanding? Standing)
{
    /// <summary>The share of its bars whose notes are there In time, both hands.</summary>
    public double There => Share(BarState.InTime, BarState.AtTempo);

    /// <summary>The share of its bars played but not there yet.</summary>
    public double Reading => Share(BarState.Reading);

    /// <summary>
    /// Learned: every bar holds at the piece's own tempo, or In time where its music marks none.
    /// </summary>
    public bool Learned => Standing is { Bars.Count: > 0 } standing
        && standing.Bars.All(b => b.State == BarState.AtTempo || standing.Own is null && b.State == BarState.InTime);

    private double Share(params BarState[] states) => Standing is { Bars.Count: > 0 } standing ? (double)standing.Bars.Count(b => states.Contains(b.State)) / standing.Bars.Count : 0;
}

/// <summary>The repertoire's pieces as cards.</summary>
public static class RepertoireCards
{
    /// <summary>A piece as the catalogue names it: its work, and its part of it where its title gives one (a movement).</summary>
    public static (string Work, string? Part) Named(PieceRecord piece) => Works.NameOf(new ListedPiece(piece.Id, piece.Title, piece.Work, piece.Source, piece.IsPdf));

    /// <summary>
    /// A piece as a line about it calls it: a movement by its work and its own name ("Sonata No. 14 “Moonlight”: I. Adagio
    /// sostenuto", since its stored title may leave the number out), anything else by its title in quotes.
    /// </summary>
    public static string Called(PieceRecord piece) => Named(piece) is (var work, { } part) ? $"{work}: {part}" : $"“{piece.Title}”";

    /// <summary>
    /// A card for each piece: its history from its runs, and for one that plays along and has been played, its standing
    /// from its score, computed off the UI thread.
    /// </summary>
    public static async Task<List<PieceCard>> OfAsync(PrimaStore store, IEnumerable<(PieceRecord Piece, DateTimeOffset Since)> pieces, ILookup<string, PieceRun> runs)
    {
        var cards = new List<PieceCard>();
        foreach (var (piece, since) in pieces)
        {
            var kept = runs[piece.Id].ToList();
            var done = PieceHistory.Of(kept);
            if (done is null || !piece.PlaysAlong || await store.GetMeiAsync(piece.Id) is not { } mei)
            {
                cards.Add(new PieceCard(piece, since, done, null));
                continue;
            }
            cards.Add(await Task.Run(() =>
            {
                try
                {
                    var shape = PieceShape.Of(MeiReader.Read(mei));
                    return new PieceCard(piece, since, done, PieceStanding.Of(kept, shape));
                }
                // Music that can't be read back leaves the card what its runs say.
                catch (Exception e) when (e is FormatException or InvalidOperationException or ArgumentException)
                {
                    return new PieceCard(piece, since, done, null);
                }
            }));
        }
        return cards;
    }
}
