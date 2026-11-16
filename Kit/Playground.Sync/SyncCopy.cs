using System.IO.Compression;

namespace Playground.Sync;

/// <summary>
/// A copy that can be restored: what it holds (its <see cref="Manifest"/>), where it is, and a short line on what's in it
/// (<see cref="Summary"/>, from <see cref="SyncOptions.Describe"/>). It comes from a store (the app's own, or a folder picked), an
/// export, or a database file on its own.
/// </summary>
public abstract class SyncCopy
{
    private protected SyncCopy(SyncManifest manifest, string where, SyncHeld? held)
    {
        Manifest = manifest;
        Where = where;
        Held = held;
    }

    public SyncManifest Manifest { get; }

    /// <summary>Where it is, for the screen: the store's name, or the file's.</summary>
    public string Where { get; }

    /// <summary>When it's the copy kept from before an update that didn't check out: the update, and why.</summary>
    public SyncHeld? Held { get; }

    /// <summary>A short line on what it holds, such as "1,204 runs over 41 days".</summary>
    public string Summary { get; internal set; } = "";

    public DateTimeOffset Made => Manifest.Made;

    /// <summary>All its files' sizes, together.</summary>
    public long Size => Manifest.Size;

    /// <summary>Reads a file of the copy, as it was kept.</summary>
    internal abstract Task<Stream> OpenAsync(SyncFile file, CancellationToken ct);
}

/// <summary>A copy in a store: its files' content kept under their fingerprints.</summary>
internal sealed class StoreCopy(ISyncStore store, string key, SyncManifest manifest, SyncHeld? held) : SyncCopy(manifest, store.Name, held)
{
    public ISyncStore Store => store;

    /// <summary>Its manifest's key in the store.</summary>
    public string Key => key;

    internal override async Task<Stream> OpenAsync(SyncFile file, CancellationToken ct) =>
        await store.OpenReadAsync(SyncPaths.Blob(file.Sha256), ct) ?? throw new IOException($"{file.Path} is missing from {store.Name}");
}

/// <summary>An export: one archive holding the files under their own paths, and the manifest beside them.</summary>
internal sealed class ArchiveCopy(string path, SyncManifest manifest) : SyncCopy(manifest, Path.GetFileName(path), null)
{
    internal override Task<Stream> OpenAsync(SyncFile file, CancellationToken ct)
    {
        var archive = ZipFile.OpenRead(path);
        try
        {
            var entry = archive.GetEntry(file.Path) ?? throw new IOException($"{file.Path} is missing from {Where}");
            return Task.FromResult<Stream>(new CheckedStream(entry.Open(), file.Size, file.Sha256, () => new IOException($"{file.Path} in {Where} is damaged"), owner: archive));
        }
        catch
        {
            archive.Dispose();
            throw;
        }
    }
}

/// <summary>A database file on its own, such as a backup from before copies were kept whole: it restores the app's database alone.</summary>
internal sealed class DatabaseFileCopy(string path, SyncManifest manifest) : SyncCopy(manifest, Path.GetFileName(path), null)
{
    internal override Task<Stream> OpenAsync(SyncFile file, CancellationToken ct) =>
        Task.FromResult<Stream>(new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, useAsync: true));
}
