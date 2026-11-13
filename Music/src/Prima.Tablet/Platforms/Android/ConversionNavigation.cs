namespace Prima.Tablet;

/// <summary>Holds notification navigation until the app's Blazor view is ready.</summary>
internal static class ConversionNavigation
{
    internal static string? PendingPath { get; private set; }
    internal static event Action? Requested;

    internal static void Request(string piece)
    {
        PendingPath = "/library/" + Uri.EscapeDataString(piece) + "?conversion=1";
        Requested?.Invoke();
    }

    internal static void Consumed(string path)
    {
        if (PendingPath == path) PendingPath = null;
    }
}
