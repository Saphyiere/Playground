using System.Globalization;

namespace Playground.Sync;

/// <summary>
/// How a store is laid out, and the paths in it: <c>copies/{when}.json</c>, a copy's manifest, named by when it was taken (UTC)
/// so the newest sorts last; <c>blobs/{sha256}</c>, each file's content by its fingerprint, so content kept already is never written
/// again; and <c>held.json</c>, the copy kept from before an update that didn't check out.
/// </summary>
internal static class SyncPaths
{
    public const string Copies = "copies/";
    public const string Blobs = "blobs/";
    public const string Held = "held.json";

    /// <summary>An export's manifest, at the top of its archive beside the files.</summary>
    public const string ExportManifest = "manifest.json";

    public static string Blob(string sha256) => Blobs + sha256;

    public static string Copy(DateTimeOffset made) => Copies + made.UtcDateTime.ToString("yyyyMMdd'T'HHmmssfff'Z'", CultureInfo.InvariantCulture) + ".json";

    public static bool IsCopy(string key) => key.StartsWith(Copies, StringComparison.Ordinal) && key.EndsWith(".json", StringComparison.Ordinal) && key.IndexOf('/', Copies.Length) < 0;

    /// <summary>When a copy was taken, from its manifest's key; null for a key that isn't one.</summary>
    public static DateTimeOffset? TimeOf(string key) =>
        IsCopy(key) && DateTimeOffset.TryParseExact(key[Copies.Length..^".json".Length], "yyyyMMdd'T'HHmmssfff'Z'", CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var made) ? made : null;

    /// <summary>Whether a path in a copy stays inside it: relative, with no part that's empty, . or ..</summary>
    public static bool IsPath(string path) => Relative(path);

    /// <summary>Whether <paramref name="path"/> in a copy is the item named <paramref name="item"/>, or in it.</summary>
    public static bool Within(string path, string item) => path == item || (path.Length > item.Length && path.StartsWith(item, StringComparison.Ordinal) && path[item.Length] == '/');

    /// <summary>An item's name: a relative path with / between its parts, none of them empty, . or .., and not the export's manifest.</summary>
    public static string CheckName(string name)
    {
        if (!Relative(name) || name == ExportManifest) throw new ArgumentException($"\"{name}\" can't name an item: it takes a relative path with / between its parts, none of them . or ..", nameof(name));
        return name;
    }

    /// <summary>A key in a store: a relative path, checked so it can't reach outside the store.</summary>
    public static string CheckKey(string key)
    {
        if (!Relative(key)) throw new ArgumentException($"\"{key}\" isn't a key in a store", nameof(key));
        return key;
    }

    /// <summary>A file's path in a copy, from where it is under an item's folder.</summary>
    public static string InCopy(string item, string folder, string file) => item + "/" + Path.GetRelativePath(folder, file).Replace('\\', '/');

    private static bool Relative(string path) =>
        path.Length > 0 && !path.Contains('\\') && !path.Contains(':') && path.Split('/').All(part => part.Length > 0 && part != "." && part != "..");
}
