namespace Prima.UI.Components;

/// <summary>A note's name to print over it on the sheet (or under it, on a lower staff): the note's id and its name, "F♯" or "C7".</summary>
public sealed record NoteLabel(string Id, string Text);
