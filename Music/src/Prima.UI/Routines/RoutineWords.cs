using System.Text.RegularExpressions;

namespace Prima.UI.Routines;

/// <summary>
/// A routine's words, as Claude wrote them, spelled as Prima spells: practice, practiced, practicing. Routines asked for before
/// the planner was asked for American spelling say "practise"; what's kept stays as it came, and only what's shown changes.
/// </summary>
public static partial class RoutineWords
{
    public static string American(string? text) => text is null ? "" : Practise().Replace(text, m =>
        m.Value == "PRACTIS" ? "PRACTIC" : char.IsUpper(m.Value[0]) ? "Practic" : "practic");

    [GeneratedRegex("practis", RegexOptions.IgnoreCase)]
    private static partial Regex Practise();
}
