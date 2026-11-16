namespace Playground.Sync;

/// <summary>
/// A place copies are kept: a folder on the device, a folder a PC has, a cloud drive. It holds named objects, each written whole or
/// not at all, and the <see cref="Syncer"/> lays copies out in it: each file's content under its SHA-256, so content it holds already
/// is never written again, and a copy's manifest last, so a copy that stopped part way leaves the one before as it was.
/// <para>
/// A cloud drive fits the same shape: <see cref="ConnectAsync"/> signs in (and may ask the person), <see cref="ListAsync"/> lists
/// a folder, <see cref="WriteAsync"/> uploads, <see cref="OpenReadAsync"/> downloads.
/// </para>
/// </summary>
public interface ISyncStore
{
    /// <summary>Where it is, for the screen: "Documents/Prima backups", "Google Drive".</summary>
    string Name { get; }

    /// <summary>Whether what's kept here outlives the app being uninstalled.</summary>
    bool SurvivesUninstall { get; }

    /// <summary>
    /// Gets it ready: nothing to do for a folder; signing in for a cloud drive, which may ask the person. False when it can't be used
    /// now (not signed in, say): a copy then leaves it out.
    /// </summary>
    Task<bool> ConnectAsync(CancellationToken ct);

    /// <summary>The objects whose keys start with <paramref name="prefix"/> ("blobs/"), with their sizes.</summary>
    Task<IReadOnlyList<StoredObject>> ListAsync(string prefix, CancellationToken ct);

    /// <summary>Reads an object, or null when there's none by that key.</summary>
    Task<Stream?> OpenReadAsync(string key, CancellationToken ct);

    /// <summary>
    /// Writes an object from <paramref name="content"/>, in place of any by that key. Once this returns it's there whole; if it
    /// throws, it's not there at all (and whatever was there before still is).
    /// </summary>
    Task WriteAsync(string key, Stream content, CancellationToken ct);

    /// <summary>Deletes an object; nothing happens when there's none.</summary>
    Task DeleteAsync(string key, CancellationToken ct);
}

/// <summary>An object in a store: its key (<c>blobs/…</c>) and size.</summary>
public sealed record StoredObject(string Key, long Size);

/// <summary>
/// A store in a folder: on the device (Documents/Prima backups/…), on a PC, or a test's. Each object is a file, written under a
/// temporary name and then renamed, so it's there whole or not at all.
/// </summary>
public sealed class FolderStore(string root, string? name = null, bool survivesUninstall = true) : ISyncStore
{
    private const string Partial = ".partial";

    /// <summary>The folder, in full.</summary>
    public string Root => root;

    public string Name { get; } = name ?? root;

    public bool SurvivesUninstall => survivesUninstall;

    public Task<bool> ConnectAsync(CancellationToken ct) => Task.FromResult(true);

    public Task<IReadOnlyList<StoredObject>> ListAsync(string prefix, CancellationToken ct)
    {
        var folder = Path.Combine(root, prefix[..(prefix.LastIndexOf('/') + 1)]);
        if (!Directory.Exists(folder)) return Task.FromResult<IReadOnlyList<StoredObject>>([]);

        var found = new List<StoredObject>();
        foreach (var file in new DirectoryInfo(folder).EnumerateFiles("*", SearchOption.AllDirectories))
        {
            ct.ThrowIfCancellationRequested();
            var key = Path.GetRelativePath(root, file.FullName).Replace('\\', '/');
            if (key.StartsWith(prefix, StringComparison.Ordinal) && !key.EndsWith(Partial, StringComparison.Ordinal)) found.Add(new StoredObject(key, file.Length));
        }
        return Task.FromResult<IReadOnlyList<StoredObject>>(found);
    }

    public Task<Stream?> OpenReadAsync(string key, CancellationToken ct)
    {
        try
        {
            return Task.FromResult<Stream?>(new FileStream(PathOf(key), FileMode.Open, FileAccess.Read, FileShare.Read, 81920, useAsync: true));
        }
        catch (Exception e) when (e is FileNotFoundException or DirectoryNotFoundException)
        {
            return Task.FromResult<Stream?>(null);
        }
    }

    public async Task WriteAsync(string key, Stream content, CancellationToken ct)
    {
        var path = PathOf(key);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var partial = path + Partial;
        try
        {
            await using (var output = new FileStream(partial, FileMode.Create, FileAccess.Write, FileShare.None, 81920, useAsync: true))
            {
                await content.CopyToAsync(output, 81920, ct);
                // On the disk before it takes the real name, so a copy that's there survives the device stopping a moment later.
                output.Flush(flushToDisk: true);
            }
            File.Move(partial, path, overwrite: true);
        }
        catch
        {
            TryDelete(partial);
            throw;
        }
    }

    public Task DeleteAsync(string key, CancellationToken ct)
    {
        var path = PathOf(key);
        if (File.Exists(path)) File.Delete(path);
        return Task.CompletedTask;
    }

    private string PathOf(string key) => Path.Combine(root, SyncPaths.CheckKey(key).Replace('/', Path.DirectorySeparatorChar));

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path)) File.Delete(path);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
        }
    }
}
