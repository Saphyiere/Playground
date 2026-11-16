using System.Globalization;

namespace Playground.Sync;

/// <summary>Sizes and times as the Syncer's sentences say them, for an app's screen to say them the same way.</summary>
public static class SyncText
{
    /// <summary>A size in kilobytes under a megabyte, else megabytes: "640 KB", "48.2 MB".</summary>
    public static string Size(long bytes) => bytes < 1_000_000
        ? Math.Max(1, (long)Math.Round(bytes / 1000.0)).ToString(CultureInfo.InvariantCulture) + " KB"
        : (bytes / 1_000_000.0).ToString(bytes < 100_000_000 ? "0.#" : "0", CultureInfo.InvariantCulture) + " MB";

    /// <summary>A time where the device is: "15 Nov 2026, 15:30".</summary>
    public static string When(DateTimeOffset at, TimeProvider? time = null) =>
        Local(at, time).ToString("d MMM yyyy, HH:mm", CultureInfo.InvariantCulture);

    /// <summary>A time in the device's own zone.</summary>
    public static DateTimeOffset Local(DateTimeOffset at, TimeProvider? time = null) => TimeZoneInfo.ConvertTime(at, (time ?? TimeProvider.System).LocalTimeZone);
}
