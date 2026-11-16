using Android.Content;
using Android.Provider;
using AndroidUri = Android.Net.Uri;

namespace Playground.Sync;

/// <summary>
/// A folder of copies picked with the system's folder picker, read through Android's documents provider: how a reinstalled app
/// opens the copies the install before it kept, which it can't read by path. It's only read: the app keeps its own copies in its
/// own folder.
/// </summary>
public sealed class DocumentTreeStore : ISyncStore
{
    private readonly Context context;
    private readonly AndroidUri tree;
    private readonly string root;
    private readonly Dictionary<string, List<Child>> listed = [];

    private DocumentTreeStore(Context context, AndroidUri tree, string root, string name)
    {
        this.context = context;
        this.tree = tree;
        this.root = root;
        Name = name;
    }

    public string Name { get; }

    public bool SurvivesUninstall => true;

    /// <summary>
    /// The stores in a folder picked: the folder itself when it holds copies, or else each folder in it that does (one for each
    /// install, as Documents/Prima backups has).
    /// </summary>
    internal static IReadOnlyList<ISyncStore> Find(Context context, AndroidUri tree)
    {
        var top = DocumentsContract.GetTreeDocumentId(tree)!;
        var children = Children(context, tree, top);
        if (children.Any(IsCopies)) return [new DocumentTreeStore(context, tree, top, Label(top, null))];
        return [.. children.Where(c => c.Folder && Children(context, tree, c.Id).Any(IsCopies)).Select(c => new DocumentTreeStore(context, tree, c.Id, Label(c.Id, c.Name)))];
    }

    /// <summary>A picked document's name, as the picker showed it.</summary>
    internal static string? DisplayName(Context context, AndroidUri document)
    {
        using var cursor = context.ContentResolver!.Query(document, [IOpenableColumns.DisplayName], null, null, null);
        return cursor is not null && cursor.MoveToFirst() && !cursor.IsNull(0) ? cursor.GetString(0) : null;
    }

    public Task<bool> ConnectAsync(CancellationToken ct) => Task.FromResult(true);

    public Task<IReadOnlyList<StoredObject>> ListAsync(string prefix, CancellationToken ct) => Task.Run<IReadOnlyList<StoredObject>>(() =>
    {
        var folder = prefix[..(prefix.LastIndexOf('/') + 1)];
        if (Resolve(folder.TrimEnd('/')) is not { Folder: true } start) return [];

        var found = new List<StoredObject>();
        var pending = new Stack<(string Key, Child Folder)>([(folder, start)]);
        while (pending.TryPop(out var at))
        {
            ct.ThrowIfCancellationRequested();
            foreach (var child in ChildrenOf(at.Folder.Id))
            {
                var key = at.Key + child.Name;
                if (child.Folder) pending.Push((key + "/", child));
                else if (key.StartsWith(prefix, StringComparison.Ordinal)) found.Add(new StoredObject(key, child.Size));
            }
        }
        return found;
    }, ct);

    public Task<Stream?> OpenReadAsync(string key, CancellationToken ct) => Task.Run(() =>
        Resolve(key) is { Folder: false } document ? context.ContentResolver!.OpenInputStream(DocumentsContract.BuildDocumentUriUsingTree(tree, document.Id)!) : null, ct);

    public Task WriteAsync(string key, Stream content, CancellationToken ct) => throw new NotSupportedException("A folder picked to restore from is only read.");

    public Task DeleteAsync(string key, CancellationToken ct) => throw new NotSupportedException("A folder picked to restore from is only read.");

    /// <summary>The document at a key, going down the folders by their names; the top folder for an empty key.</summary>
    private Child? Resolve(string key)
    {
        var at = new Child(root, "", true, 0);
        if (key.Length == 0) return at;
        foreach (var part in key.Split('/'))
        {
            if (!at.Folder || ChildrenOf(at.Id).FirstOrDefault(c => c.Name == part) is not { } next) return null;
            at = next;
        }
        return at;
    }

    private List<Child> ChildrenOf(string id)
    {
        lock (listed)
        {
            if (!listed.TryGetValue(id, out var children)) listed[id] = children = Children(context, tree, id);
            return children;
        }
    }

    private static List<Child> Children(Context context, AndroidUri tree, string parent)
    {
        string[] columns = [DocumentsContract.Document.ColumnDocumentId, DocumentsContract.Document.ColumnDisplayName, DocumentsContract.Document.ColumnMimeType, DocumentsContract.Document.ColumnSize];
        using var cursor = context.ContentResolver!.Query(DocumentsContract.BuildChildDocumentsUriUsingTree(tree, parent)!, columns, null, null, null);
        var children = new List<Child>();
        while (cursor is not null && cursor.MoveToNext())
        {
            children.Add(new Child(cursor.GetString(0)!, cursor.GetString(1) ?? "", cursor.GetString(2) == DocumentsContract.Document.MimeTypeDir, cursor.IsNull(3) ? 0 : cursor.GetLong(3)));
        }
        return children;
    }

    private static bool IsCopies(Child child) => child.Folder && child.Name == "copies";

    /// <summary>Where it is, for the screen: a folder on the device's own storage by its path ("Documents/Prima backups/…").</summary>
    private static string Label(string id, string? name) => id.IndexOf(':') is var colon and >= 0 && id.StartsWith("primary:", StringComparison.Ordinal) ? id[(colon + 1)..] : name ?? id;

    private sealed record Child(string Id, string Name, bool Folder, long Size);
}
