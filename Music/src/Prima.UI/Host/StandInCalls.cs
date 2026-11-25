#if DEBUG
using System.Text.Json;
using Microsoft.JSInterop;

namespace Prima.UI.Host;

/// <summary>
/// The stand-in piano from DevTools, in a build from the PC (never a release): what's played comes back in words. The second
/// argument is always given, {} when there's nothing to say: a call is matched to its parameters by number.
/// <code>
/// DotNet.invokeMethodAsync('Prima.UI', 'StandIn', 'on', {})
/// DotNet.invokeMethodAsync('Prima.UI', 'StandIn', 'perform', { right: 1, spread: 4, lean: 0 })
/// DotNet.invokeMethodAsync('Prima.UI', 'StandIn', 'answer', { right: true, apart: 250 })
/// DotNet.invokeMethodAsync('Prima.UI', 'StandIn', 'play', { notes: [60, 64, 67], hold: 300 })
/// </code>
/// </summary>
public static class StandInCalls
{
    /// <summary>The stand-in this build plays: the last one made.</summary>
    internal static StandInPlayer? Player { get; set; }

    [JSInvokable("StandIn")]
    public static async Task<string> Call(string what, JsonElement args = default)
    {
        if (Player is not { } player) return "no stand-in piano in this build";

        double? Number(string name) => args.ValueKind == JsonValueKind.Object && args.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetDouble() : null;
        bool? Flag(string name) => args.ValueKind == JsonValueKind.Object && args.TryGetProperty(name, out var v) && v.ValueKind is JsonValueKind.True or JsonValueKind.False ? v.GetBoolean() : null;
        return what switch
        {
            "on" => player.Plug(true, StandInKeyboard.Called),
            "off" => player.Plug(false),
            "play" => player.Play(args.ValueKind == JsonValueKind.Object && args.TryGetProperty("notes", out var notes) ? [.. notes.EnumerateArray().Select(n => n.GetInt32())] : [], (int)(Number("velocity") ?? 80), (int)(Number("hold") ?? 300)),
            "perform" => player.Perform(new(Number("right") ?? 0.95, Number("lean") ?? -15, Number("spread") ?? 12, (int?)Number("stop"), Flag("live") ?? false, Number("slips"), Number("early"), (int?)Number("roll"))),
            "answer" => await player.Answer(Flag("right") ?? true, (int?)Number("apart")),
            _ => $"the stand-in doesn't know \"{what}\": on, off, play, perform or answer",
        };
    }
}
#endif
