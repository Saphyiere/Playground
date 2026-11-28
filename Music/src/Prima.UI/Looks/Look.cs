using System.Globalization;

namespace Prima.UI.Looks;

/// <summary>
/// Prima's looks, its own rather than the Kit's. Studio is a music workstation: warm grey, an amber LCD and one orange key.
/// Vinyl is a record sleeve: deep teal, cream type and mustard. Porcelain is white cards on soft shadows, serif names and
/// figures, black for what's chosen and an accent colour you choose; Night is Porcelain in the dark, with the same accent. The
/// page follows <see cref="Id"/> in data-theme (prima.css), and the sheet music stays black on paper in every look.
/// </summary>
public sealed record Look(string Id, string Name, string Description, bool Dark, string Ground)
{
    public static readonly Look Studio = new("studio", "Studio", "A music workstation: warm grey keys, an amber screen and one orange key.", false, "#DCD9D2");
    public static readonly Look Vinyl = new("vinyl", "Vinyl", "A record sleeve: deep teal, cream type and a mustard needle.", true, "#173B3D");
    public static readonly Look Porcelain = new("porcelain", "Porcelain", "White cards on soft shadows, serif titles, and an accent colour you choose.", false, "#F2F2F2");
    public static readonly Look Night = new("porcelain-night", "Night", "Porcelain in the dark: near-black layers, the paper a shade softer, the same accent.", true, "#101114");

    public static IReadOnlyList<Look> All { get; } = [Studio, Vinyl, Porcelain, Night];

    /// <summary>The look with this id, or Studio for an unknown or missing one.</summary>
    public static Look FromId(string? id) => All.FirstOrDefault(l => l.Id == id) ?? Studio;

    /// <summary>Whether its accent is the one chosen in Settings: Porcelain's and Night's are; Studio and Vinyl have their own.</summary>
    public bool TakesAccent => this == Porcelain || this == Night;

    /// <summary>A day as this look prints it: "Thu 05 Nov" on Studio's panel (and Porcelain's), "Thursday · 5 November" on Vinyl's sleeve.</summary>
    public string Day(DateOnly day) => day.ToString(Id == Vinyl.Id ? "dddd · d MMMM" : "ddd dd MMM", CultureInfo.InvariantCulture);
}

/// <summary>
/// Porcelain's and Night's accent: the key to press, where you are, what's lit. Red unless another is chosen; the page follows
/// <see cref="Id"/> in data-accent (prima.css). A wrong note stays red whichever it is.
/// </summary>
public sealed record Accent(string Id, string Name, string Color)
{
    public static readonly Accent Red = new("red", "Red", "#E2140C");
    public static readonly Accent Persimmon = new("persimmon", "Persimmon", "#E2491C");
    public static readonly Accent Amber = new("amber", "Amber", "#E9A00E");
    public static readonly Accent Green = new("green", "Green", "#1D8A4F");
    public static readonly Accent Blue = new("blue", "Blue", "#2F5BD3");
    public static readonly Accent Violet = new("violet", "Violet", "#6B4FE0");

    public static IReadOnlyList<Accent> All { get; } = [Red, Persimmon, Amber, Green, Blue, Violet];

    /// <summary>The accent with this id, or red for an unknown or missing one.</summary>
    public static Accent FromId(string? id) => All.FirstOrDefault(a => a.Id == id) ?? Red;
}
