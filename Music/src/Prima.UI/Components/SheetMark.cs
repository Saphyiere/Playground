namespace Prima.UI.Components;

/// <summary>
/// A mark on a note, during the run or after it: "right", "wrong", "missed", "early" or "late". A wrong note names what was played
/// and how many staff positions from the written note it is, so the sheet can show it there as a small grey note head; an early
/// or late one when it was played, in quarter notes from the start of the music, so a ring shows there on its line.
/// </summary>
public sealed record SheetMark(string Id, string Kind, string? Played = null, int Steps = 0, double? Quarter = null);
