using System.Globalization;

namespace Prima.UI.Looks;

/// <summary>
/// Prima's looks, its own rather than the Kit's: Porcelain, white cards on soft shadows, serif names and figures, black for
/// what's chosen and an accent colour you choose; and Night, Porcelain in the dark, with the same accent. The page follows
/// <see cref="Id"/> in data-theme (prima.css), and the sheet music stays black on paper in both. Studio and Vinyl, the first
/// two, are gone (the owner, 28 November 2026: "We're only doing porcelain and it's dark mode").
/// </summary>
public sealed record Look(string Id, string Name, string Description, bool Dark, string Ground)
{
    public static readonly Look Porcelain = new("porcelain", "Porcelain", "White cards on soft shadows, serif titles, and an accent colour you choose.", false, "#F2F2F2");
    public static readonly Look Night = new("porcelain-night", "Night", "Porcelain in the dark: near-black layers, the paper a shade softer, the same accent.", true, "#101114");

    public static IReadOnlyList<Look> All { get; } = [Porcelain, Night];

    /// <summary>The look with this id, or Porcelain for an unknown or missing one (Studio's or Vinyl's, kept from before).</summary>
    public static Look FromId(string? id) => All.FirstOrDefault(l => l.Id == id) ?? Porcelain;

    /// <summary>A day as the looks print it: "Thu 05 Nov".</summary>
    public string Day(DateOnly day) => day.ToString("ddd dd MMM", CultureInfo.InvariantCulture);
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
