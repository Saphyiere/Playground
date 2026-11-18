namespace Prima.UI.Components;

/// <summary>
/// How a whole piece's drawing stands on its sheet: its lines still coming (the first are there to read), all of it there,
/// stopped part-way (what's drawn stays, and the rest can be asked for), or failed.
/// </summary>
public enum SheetDrawing { Coming, Done, Stopped, Failed }

/// <summary>
/// How far a whole piece's drawing has got: the lines of music drawn, of the lines the piece has at this size (0 where they
/// couldn't be counted), and how it stands.
/// </summary>
public sealed record SheetProgress(int Lines, int Total, SheetDrawing Drawing);
