namespace Prima.Tablet;

/// <summary>Holds a screen to open, a notification's or the one Android closed Prima on, until the app's Blazor view is ready.</summary>
internal static class ConversionNavigation
{
    internal static string? PendingPath { get; private set; }
    internal static event Action? Requested;

    internal static void Request(string piece)
    {
        RequestPath("/library/" + Uri.EscapeDataString(piece) + "?conversion=1");
    }

    internal static void RequestPath(string path)
    {
        PendingPath = path;
        Requested?.Invoke();
    }

    internal static void Consumed(string path)
    {
        if (PendingPath == path) PendingPath = null;
    }
}
