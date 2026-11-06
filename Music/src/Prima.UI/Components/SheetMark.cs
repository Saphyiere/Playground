namespace Prima.UI.Components;

/// <summary>
/// A mark on a note, during the run or after it: "right", "wrong", "missed", "early" or "late". A wrong note names what was played
/// and how many staff positions from the written note it is, so the sheet shows it there as a red note head named in red, and the
/// written note grey, named in grey when Written is given; an early or late one when it was played, in quarter notes from the
/// start of the music, so a ring shows there on its line. "extra" is a key played that the music didn't ask for, named and placed
/// the same way beside the note before it, which keeps its own mark.
/// </summary>
public sealed record SheetMark(string Id, string Kind, string? Played = null, int Steps = 0, double? Quarter = null, string? Written = null);
