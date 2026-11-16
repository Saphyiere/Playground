namespace Playground.Sync;

/// <summary>
/// The device's own pickers, for restoring a copy the app can't reach by itself. On Android 11 and later a reinstalled app can't
/// read what its old install wrote, even in shared storage, so a copy from before is opened through the system's picker, which
/// gives the app the folder or file the person chooses. Android's is in Playground.Sync.Android.
/// </summary>
public interface ISyncPicker
{
    /// <summary>
    /// Asks for a folder with the system's folder picker: the stores in it (the folder itself, when it holds copies, or each folder
    /// in it that does, one for each install). None when nothing was picked, or nothing in it holds copies.
    /// </summary>
    Task<IReadOnlyList<ISyncStore>> PickFolderAsync(CancellationToken ct);

    /// <summary>
    /// Asks for one file with the system's file picker (an export, or a database file on its own) and copies it into
    /// <paramref name="folder"/>: its path there, or null when nothing was picked.
    /// </summary>
    Task<string?> PickFileAsync(string folder, CancellationToken ct);
}

/// <summary>No pickers: for a host without them. A copy is restored from the app's own store only.</summary>
public sealed class NoPicker : ISyncPicker
{
    public Task<IReadOnlyList<ISyncStore>> PickFolderAsync(CancellationToken ct) => Task.FromResult<IReadOnlyList<ISyncStore>>([]);

    public Task<string?> PickFileAsync(string folder, CancellationToken ct) => Task.FromResult<string?>(null);
}
