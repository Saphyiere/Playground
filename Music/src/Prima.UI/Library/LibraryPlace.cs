using Prima.UI.Storage;

namespace Prima.UI.Library;

/// <summary>
/// Where the Library was left: the address of the list shown last, its search and filter in it. The rail's Library key goes back
/// there from a piece or another screen, and the list puts back the place it was scrolled to. And the piece just taken out, which
/// the list it's come back to can put back (Undo) until the Library is left.
/// </summary>
public sealed class LibraryPlace
{
    /// <summary>The list's address, relative to the app's (<c>library?by=composer&amp;of=…&amp;q=sonata</c>); the home to begin with.</summary>
    public string Last { get; set; } = "library";

    /// <summary>The piece taken out last, as it was, for Undo; null once it's put back or the Library is left.</summary>
    public RemovedPiece? Removed { get; set; }
}
