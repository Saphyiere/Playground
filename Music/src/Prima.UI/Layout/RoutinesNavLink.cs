using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Routing;

namespace Prima.UI.Layout;

/// <summary>
/// The rail's Routines key, lit on every routine screen: the list (routines), and the routine followed, asking for one, a request
/// and an activity of one (routine, routine/…).
/// </summary>
public sealed class RoutinesNavLink : NavLink
{
    [Inject] private NavigationManager Nav { get; set; } = default!;

    protected override bool ShouldMatch(string uriAbsolute)
    {
        if (base.ShouldMatch(uriAbsolute)) return true;

        var path = Nav.ToBaseRelativePath(uriAbsolute).Split('?', '#')[0];
        return path == "routine" || path.StartsWith("routine/", StringComparison.Ordinal);
    }
}
