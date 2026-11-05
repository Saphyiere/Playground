using System.Globalization;

namespace Prima.UI.Looks;

/// <summary>
/// Prima's two looks, its own rather than the Kit's. Studio is a music workstation: warm grey, an amber LCD and one orange key.
/// Vinyl is a record sleeve: deep teal, cream type and mustard. The page follows <see cref="Id"/> in data-theme (prima.css), and
/// the sheet music stays black on paper in both.
/// </summary>
public sealed record Look(string Id, string Name, string Description, bool Dark, string Ground)
{
    public static readonly Look Studio = new("studio", "Studio", "A music workstation: warm grey keys, an amber screen and one orange key.", false, "#DCD9D2");
    public static readonly Look Vinyl = new("vinyl", "Vinyl", "A record sleeve: deep teal, cream type and a mustard needle.", true, "#173B3D");

    public static IReadOnlyList<Look> All { get; } = [Studio, Vinyl];

    /// <summary>The look with this id, or Studio for an unknown or missing one.</summary>
    public static Look FromId(string? id) => All.FirstOrDefault(l => l.Id == id) ?? Studio;

    /// <summary>A day as this look prints it: "Thu 05 Nov" on Studio's panel, "Thursday · 5 November" on Vinyl's sleeve.</summary>
    public string Day(DateOnly day) => day.ToString(Id == Vinyl.Id ? "dddd · d MMMM" : "ddd dd MMM", CultureInfo.InvariantCulture);
}
