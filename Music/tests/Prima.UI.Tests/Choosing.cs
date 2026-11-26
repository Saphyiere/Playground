using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;

namespace Prima.UI.Tests;

/// <summary>Choosing what to play on a practice screen, as a finger would: Change opened, then its keys.</summary>
internal static class Choosing
{
    /// <summary>Opens Change, where it isn't open.</summary>
    public static async Task OpenChange<T>(IRenderedComponent<T> view) where T : IComponent
    {
        if (view.FindAll(".change-panel").Count == 0) await view.InvokeAsync(() => view.Find(".choice-change").ClickAsync(new MouseEventArgs()));
        view.WaitForElement(".change-panel");
    }

    /// <summary>Taps a key in Change, by what it says to a screen reader.</summary>
    public static async Task Tap<T>(IRenderedComponent<T> view, string selector) where T : IComponent
    {
        await OpenChange(view);
        await view.InvokeAsync(() => view.Find($".change-panel {selector}").ClickAsync(new MouseEventArgs()));
    }

    /// <summary>A key ("A minor") on Change's piano: Major or Minor first, then its key.</summary>
    public static async Task ChooseKey<T>(IRenderedComponent<T> view, string name) where T : IComponent
    {
        await OpenChange(view);
        var minor = name.EndsWith(" minor", StringComparison.Ordinal);
        var modes = view.FindAll(".key-mode .mu-toggle");
        if (modes.Count == 2 && modes[minor ? 1 : 0].GetAttribute("aria-pressed") != "true")
        {
            await view.InvokeAsync(() => view.FindAll(".key-mode .mu-toggle")[minor ? 1 : 0].ClickAsync(new MouseEventArgs()));
        }
        await view.InvokeAsync(() => view.Find($".key-piano button[aria-label='{name}']").ClickAsync(new MouseEventArgs()));
    }

    /// <summary>What the card says of the rest of what's chosen: a family's own choices, Through the keys.</summary>
    public static string[] Facts<T>(IRenderedComponent<T> view) where T : IComponent => [.. view.FindAll(".choice-card .choice-fact").Select(f => f.TextContent.Trim())];

    /// <summary>The option chosen of a family's variation, in Change.</summary>
    public static string? Chosen<T>(IRenderedComponent<T> view, string variation) where T : IComponent =>
        view.Find($".change-panel [data-variation={variation}] [aria-pressed=true]").GetAttribute("aria-label");
}
