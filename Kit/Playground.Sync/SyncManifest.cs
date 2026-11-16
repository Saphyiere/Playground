using System.Text.Json;
using System.Text.Json.Serialization;

namespace Playground.Sync;

/// <summary>Why a copy was taken: what the screen says of it, and what the app's next start does.</summary>
public enum SyncReason
{
    /// <summary>The person asked for it: Back up now.</summary>
    Asked,

    /// <summary>The app's start found no copy, or the last one too old.</summary>
    Scheduled,

    /// <summary>Just before an update installed: the first start after it checks the app against it before taking another.</summary>
    BeforeUpdate,

    /// <summary>The first start after an update, once the app was found to hold all the copy before it did.</summary>
    AfterUpdate,

    /// <summary>Just after a copy was restored.</summary>
    AfterRestore,
}

/// <summary>
/// What a copy holds, kept with it: the app, its version, when and why the copy was taken, each item (a database with its tables'
/// rows), and each file with its size, time and SHA-256, the fingerprint its content is kept by.
/// </summary>
public sealed record SyncManifest
{
    public int Format { get; init; } = 1;

    public required string App { get; init; }

    public string Version { get; init; } = "";

    public DateTimeOffset Made { get; init; }

    public SyncReason Reason { get; init; }

    /// <summary>For a copy taken before an update, the release it was for ("Prima 1.0.13").</summary>
    public string? Update { get; init; }

    public IReadOnlyList<SyncManifestItem> Items { get; init; } = [];

    public IReadOnlyList<SyncFile> Files { get; init; } = [];

    /// <summary>All its files' sizes, together.</summary>
    [JsonIgnore]
    public long Size => Files.Sum(f => f.Size);

    /// <summary>The files of the item named <paramref name="item"/>.</summary>
    public IEnumerable<SyncFile> FilesOf(string item) => Files.Where(f => SyncPaths.Within(f.Path, item));

    /// <summary>The rows in a database's table when the copy was taken, or null when the copy has no such table.</summary>
    public long? Rows(string database, string table) => Items.FirstOrDefault(i => i.Name == database)?.Tables?.GetValueOrDefault(table);

    internal byte[] ToJson() => JsonSerializer.SerializeToUtf8Bytes(this, SyncJson.Default.SyncManifest);

    internal static SyncManifest? FromJson(Stream json)
    {
        try
        {
            // One without its lists, or with a file or an item that has no name, isn't a copy (a file edited by hand, or another
            // program's): it's left out like one that can't be read.
            return JsonSerializer.Deserialize(json, SyncJson.Default.SyncManifest) is { Items: not null, Files: not null } manifest
                && manifest.Items.All(i => i is { Name: not null }) && manifest.Files.All(f => f is { Path: not null, Sha256: not null }) ? manifest : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }
}

/// <summary>
/// An item as the copy has it: a database's tables, each with how many rows it had, and what in it was counted as the person's own
/// (<see cref="DatabaseItem.Counted"/>), by name.
/// </summary>
public sealed record SyncManifestItem(string Name, SyncItemKind Kind)
{
    public IReadOnlyDictionary<string, long>? Tables { get; init; }

    public IReadOnlyDictionary<string, long>? Counts { get; init; }
}

/// <summary>A file in a copy: where it sits in it, its size and when it last changed, and the SHA-256 of its content.</summary>
public sealed record SyncFile(string Path, long Size, DateTimeOffset Modified, string Sha256);

/// <summary>
/// A copy kept from before an update, after the app's first start with the update didn't hold all of it: kept, whatever copies
/// come after, until it's restored or let go. A store can hold more than one, each in <c>held.json</c>.
/// </summary>
public sealed record SyncHeld(string Copy, string Update, string Why, DateTimeOffset Since);

[JsonSourceGenerationOptions(JsonSerializerDefaults.Web, WriteIndented = true, UseStringEnumConverter = true)]
[JsonSerializable(typeof(SyncManifest))]
[JsonSerializable(typeof(SyncHeld))]
[JsonSerializable(typeof(List<SyncHeld>))]
internal sealed partial class SyncJson : JsonSerializerContext;
