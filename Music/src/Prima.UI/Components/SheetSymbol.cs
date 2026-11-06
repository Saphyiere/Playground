namespace Prima.UI.Components;

/// <summary>
/// A symbol tapped on the sheet in What's this: its id (the MEI's, or one Verovio made for what it draws itself), its kind as
/// the sheet saw it (Verovio's class: "slur", "dynam", "note"…, with what the drawing shows after a colon: "clef:F",
/// "barLine:rpt"), and the id of the note or bar it's part of, for what Verovio draws from an attribute (an accidental, a
/// staccato dot) or around a bar (its bar lines).
/// </summary>
public sealed record SheetSymbol(string Id, string Kind, string? Within);
