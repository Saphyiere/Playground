namespace Playground.Sync;

/// <summary>
/// Something an app keeps, copied whole: a SQLite database, a folder of files, one file, or anything else the app writes out
/// itself. Its <see cref="Name"/> is where it sits in a copy ("prima.db", "library"), the same on every device, so a copy taken on
/// one install restores on another.
/// </summary>
public abstract record SyncItem
{
    private protected SyncItem(string name) => Name = SyncPaths.CheckName(name);

    /// <summary>Where it sits in a copy: a relative path with / between its parts.</summary>
    public string Name { get; }

    /// <summary>A SQLite database, copied with SQLite's online backup: whole and consistent while the app has it open.</summary>
    public static DatabaseItem Database(string name, string path) => new(name, path);

    /// <summary>Every file in a folder and the folders in it.</summary>
    public static FolderItem Folder(string name, string path) => new(name, path);

    /// <summary>One file.</summary>
    public static FileItem File(string name, string path) => new(name, path);

    /// <summary>Anything else: the app writes it out as files when a copy is taken, and reads them back when one is restored.</summary>
    public static CustomItem Custom(string name, ISyncExport export) => new(name, export);

    internal abstract SyncItemKind Kind { get; }
}

/// <summary>A SQLite database at <see cref="Path"/>.</summary>
public sealed record DatabaseItem : SyncItem
{
    internal DatabaseItem(string name, string path)
        : base(name) => Path = path;

    public string Path { get; }

    /// <summary>
    /// Tables a copy of it must have to be restored, which tell it's this app's database and not another's (Prima's: Setting,
    /// Track, Attempt, PracticeDay).
    /// </summary>
    public IReadOnlyList<string> RequiredTables { get; init; } = [];

    /// <summary>
    /// What in it is the person's own, counted in each copy: the first start after an update finds none of it with fewer rows than
    /// the copy from before had. What the app makes or takes out itself (Prima's bundled pieces) is left out, so it's never taken
    /// for data lost. Without it, every table is counted.
    /// </summary>
    public IReadOnlyList<SyncCount>? Counted { get; init; }

    internal override SyncItemKind Kind => SyncItemKind.Database;
}

/// <summary>
/// Something in a database that's the person's own, counted in each copy: the rows of a table, or those a condition picks
/// (<paramref name="Where"/>, as SQL: Prima's imported pieces, <c>Origin = 'import'</c>).
/// </summary>
/// <param name="Name">What it's called when one has fewer rows: "Attempt", "Piece (imported)".</param>
public sealed record SyncCount(string Name, string Table, string? Where = null)
{
    /// <summary>Every row of a table.</summary>
    public static SyncCount Rows(string table) => new(table, table);
}

/// <summary>A folder at <see cref="Path"/>: every file in it and in the folders in it.</summary>
public sealed record FolderItem : SyncItem
{
    internal FolderItem(string name, string path)
        : base(name) => Path = path;

    public string Path { get; }

    internal override SyncItemKind Kind => SyncItemKind.Folder;
}

/// <summary>One file at <see cref="Path"/>, which may not be there yet.</summary>
public sealed record FileItem : SyncItem
{
    internal FileItem(string name, string path)
        : base(name) => Path = path;

    public string Path { get; }

    internal override SyncItemKind Kind => SyncItemKind.File;
}

/// <summary>Something the app writes out as files for a copy, and reads back from them on restore (<see cref="ISyncExport"/>).</summary>
public sealed record CustomItem : SyncItem
{
    internal CustomItem(string name, ISyncExport export)
        : base(name) => Export = export;

    public ISyncExport Export { get; }

    internal override SyncItemKind Kind => SyncItemKind.Custom;
}

/// <summary>Something an app keeps that isn't a database or files already, such as its preferences: written out as files for a copy.</summary>
public interface ISyncExport
{
    /// <summary>Writes it as files into <paramref name="folder"/>, which is empty and the app's alone, for a copy.</summary>
    Task WriteAsync(string folder, CancellationToken ct);

    /// <summary>Takes it back from the files in <paramref name="folder"/>, as <see cref="WriteAsync"/> wrote them for the copy being restored.</summary>
    Task ReadAsync(string folder, CancellationToken ct);
}

public enum SyncItemKind
{
    Database,
    Folder,
    File,
    Custom,
}
