using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Routing;

namespace Prima.UI.Layout;

/// <summary>
/// The rail's Routines key, lit on every routine screen: the list (routines), and the routine followed, asking for one, a request
/// and an activity of one (routine, routine/…), an exercise or a drill opened from a routine among them (its address carries the
/// routine's run: exercises/…?execution=…).
/// </summary>
public sealed class RoutinesNavLink : NavLink
{
    [Inject] private NavigationManager Nav { get; set; } = default!;

    protected override bool ShouldMatch(string uriAbsolute)
    {
        if (base.ShouldMatch(uriAbsolute)) return true;

        var path = Nav.ToBaseRelativePath(uriAbsolute).Split('?', '#')[0];
        return path == "routine" || path.StartsWith("routine/", StringComparison.Ordinal) || FromARoutine(Nav, uriAbsolute);
    }

    /// <summary>Whether the address is an exercise's or a drill's opened from a routine: it carries the routine's run.</summary>
    internal static bool FromARoutine(NavigationManager nav, string uriAbsolute)
    {
        var relative = nav.ToBaseRelativePath(uriAbsolute).Split('#')[0];
        var query = relative.IndexOf('?') is >= 0 and var at ? relative[(at + 1)..] : "";
        return (relative.StartsWith("exercises/", StringComparison.Ordinal) || relative.StartsWith("drills/", StringComparison.Ordinal))
            && query.Split('&').Any(p => p.StartsWith("execution=", StringComparison.Ordinal) && p.Length > "execution=".Length);
    }
}

/// <summary>
/// A rail key for a kind of practice (Drills, Exercises), lit on its screens but for one opened from a routine, which lights the
/// Routines key instead: one key is lit at a time.
/// </summary>
public sealed class PracticeNavLink : NavLink
{
    [Inject] private NavigationManager Nav { get; set; } = default!;

    protected override bool ShouldMatch(string uriAbsolute) => base.ShouldMatch(uriAbsolute) && !RoutinesNavLink.FromARoutine(Nav, uriAbsolute);
}
