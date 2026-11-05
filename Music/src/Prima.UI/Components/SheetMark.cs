namespace Prima.UI.Components;

/// <summary>
/// A mark on a note after the passage: "wrong", "missed", "early" or "late". A wrong note names what was played and how many
/// staff positions from the written note it is, so the sheet can show it there as a small grey note head.
/// </summary>
public sealed record SheetMark(string Id, string Kind, string? Played = null, int Steps = 0);
