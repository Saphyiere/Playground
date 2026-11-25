namespace Prima.UI.Components;

/// <summary>The shape a <c>Waiting</c> list's faint items take: the rows and cards of the list it stands in for.</summary>
public enum WaitShape
{
    /// <summary>A row of a column of the Library's: a name and its count.</summary>
    Line,

    /// <summary>A work in a composer's column: its name, a line under it, and its levels.</summary>
    Work,

    /// <summary>A piece in a list of them: its key, its name over a line, and its level.</summary>
    Piece,

    /// <summary>A card of Repertoire's or Routines': its kicker, its name, its strip and its lines.</summary>
    Card,

    /// <summary>A row under the cards: its key and its name over a line.</summary>
    Row,

    /// <summary>A routine's track: its number, its shape, its name over a line, and how it's going.</summary>
    Track,
}
