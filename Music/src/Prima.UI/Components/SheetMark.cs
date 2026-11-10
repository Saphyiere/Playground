namespace Prima.UI.Components;

/// <summary>
/// A mark on a note, during the run or after it: "right", "wrong", "missed", "early" or "late". A wrong note names what was played
/// and how many staff positions from the written note it is, so the sheet shows it there as a red note head named in red, and the
/// written note grey, named in grey when Written is given; an early or late one when it was played, in quarter notes from the
/// start of the music, so a ring shows there on its line. "extra" is a key played that the music didn't ask for, named and placed
/// the same way beside the note before it, which keeps its own mark.
/// </summary>
public sealed record SheetMark(string Id, string Kind, string? Played = null, int Steps = 0, double? Quarter = null, string? Written = null);

/// <summary>
/// A run's way through the music on a sheet, for its cursor: each bar as it's played (its place in the score, the quarter it
/// starts at and how many it lasts) and what's printed in those bars (the bar's place, a note's or rest's id, and its quarters
/// from the bar's start).
/// </summary>
public sealed record CursorPath(IReadOnlyList<(int Index, double Quarter, double Quarters)> Bars, IReadOnlyList<(int Index, string Id, double Quarter)> Printed);
