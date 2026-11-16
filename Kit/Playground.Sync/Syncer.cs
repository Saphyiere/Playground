using System.Globalization;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;

namespace Playground.Sync;

/// <summary>
/// An app's copies of its data: one per app, holding the <see cref="Status"/> its screens show.
/// <list type="bullet">
/// <item><see cref="CopyAsync"/> takes a copy of every item into the app's stores: databases snapshotted while open, each file
/// fingerprinted (SHA-256, the last copy's kept when its size and time haven't changed), and only content a store doesn't hold yet
/// written; the copy's manifest last, then whatever no copy kept needs any more goes. A store keeps its three newest copies, the
/// newest at least a week old (with the oldest younger than that, which comes to be it), and every one held.</item>
/// <item><see cref="StartAsync"/>, as the app starts: the first time after an update, checks at once that the app holds all of the
/// person's the copy from before it did, and only then takes a fresh one (a copy that doesn't check out is held); otherwise takes
/// one when the last is more than <see cref="SyncOptions.RefreshAfter"/> old, as <see cref="CopyIfDueAsync"/> does after practice,
/// once the app is <see cref="SyncOptions.Quiet"/>.</item>
/// <item><see cref="RestoreAsync(SyncCopy, CancellationToken)"/> puts a copy's data in place of the app's, all of it or none, what
/// was there kept first and held; <see cref="KeepAsync"/> holds a copy before the app changes its data itself;
/// <see cref="ExportAsync"/> writes everything into one archive, to hand on.</item>
/// </list>
/// Secrets never go in a copy: excluded names are never read, and a copy that would hold a secret stops before it writes anything.
/// One piece of work runs at a time, in the background, and <see cref="Changed"/> is raised from whatever thread it's on.
/// </summary>
public sealed class Syncer
{
    private const string Restoring = ".sync-restore";
    private const string Replaced = ".sync-old";
    private static readonly TimeSpan ProgressEvery = TimeSpan.FromMilliseconds(100);
    private static readonly TimeSpan Week = TimeSpan.FromDays(7);
    private static readonly EnumerationOptions Everything = new() { RecurseSubdirectories = true, AttributesToSkip = 0 };

    private readonly SyncOptions options;
    private readonly IReadOnlyList<ISyncStore> stores;
    private readonly TimeProvider time;
    private readonly ILogger? logger;
    private readonly SemaphoreSlim gate = new(1, 1);
    private readonly Lock statusGate = new();
    private long shownAt;

    public Syncer(SyncOptions options, IReadOnlyList<ISyncStore> stores, TimeProvider? time = null, ILogger<Syncer>? logger = null)
    {
        if (stores.Count == 0) throw new ArgumentException("Copies need a store to go in.", nameof(stores));
        var names = options.Items.Select(i => i.Name).ToList();
        var inside = names.FirstOrDefault(n => names.Any(o => o != n && SyncPaths.Within(n, o)));
        if (inside is not null || names.Count != names.Distinct().Count())
        {
            throw new ArgumentException($"Each item needs a name of its own, not inside another's{(inside is null ? "" : $" ({inside})")}.", nameof(options));
        }
        this.options = options;
        this.stores = stores;
        this.time = time ?? TimeProvider.System;
        this.logger = logger;
        Status = new SyncStatus(SyncStep.Idle, false, null, null, null, "", false);
    }

    public SyncStatus Status { get; private set; }

    /// <summary>Raised on each change of <see cref="Status"/>, from whatever thread the work is on.</summary>
    public event Action<SyncStatus>? Changed;

    public SyncOptions Options => options;

    /// <summary>The app's own store: copies go here first, and its newest is the one the status shows.</summary>
    public ISyncStore Store => stores[0];

    /// <summary>Reads the newest copy in the app's own store, and those held there, into <see cref="Status"/>.</summary>
    public async Task RefreshAsync(CancellationToken ct = default)
    {
        try
        {
            Show(await CopiesInAsync(Store, ct));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            Set(s => s with { Known = true, Message = $"The copies in {Store.Name} couldn't be read: {e.Message}", Failed = true });
        }
    }

    /// <summary>The copies in a store that are this app's, newest first; one that can't be read is left out.</summary>
    public async Task<IReadOnlyList<SyncCopy>> FindCopiesAsync(ISyncStore store, CancellationToken ct = default) =>
        await store.ConnectAsync(ct) ? await CopiesInAsync(store, ct) : [];

    /// <summary>
    /// Takes a copy of everything the app keeps, into each of its stores: only what changed since the last copy is read again, and
    /// only content a store doesn't hold yet is written. Throws when the app's own store couldn't take it; the copy before is then
    /// as it was.
    /// </summary>
    /// <param name="update">For a copy taken before an update, the release it's for.</param>
    /// <param name="progress">How far it is, 0 to 1, reported as it goes on its own thread.</param>
    public Task<SyncCopy> CopyAsync(SyncReason reason = SyncReason.Asked, string? update = null, IProgress<double>? progress = null, CancellationToken ct = default) =>
        RunAsync<SyncCopy>(SyncStep.Copying, "Getting ready", async report =>
        {
            if (options.BeforeCopy is { } before) await before();
            // Just after a restore, what the screen says is the restore.
            return await CopyWithinAsync(reason, update, stores, say: reason != SyncReason.AfterRestore, report, progress, ct);
        }, keepMessage: reason == SyncReason.AfterRestore, ct);

    /// <summary>
    /// Keeps a copy of the data as it is, held, before the app changes the data itself: <paramref name="what"/> names the change
    /// as the screen says it ("Start over"). It's kept whatever copies come after, until it's restored or let go. Throws when it
    /// couldn't be kept, and the app should then leave its data as it is.
    /// </summary>
    public Task<SyncCopy> KeepAsync(string what, CancellationToken ct = default) =>
        RunAsync<SyncCopy>(SyncStep.Copying, "Getting ready", async report =>
        {
            if (options.BeforeCopy is { } before) await before();
            var kept = await KeepWithinAsync(SyncHeldFrom.Change, what, SyncReason.BeforeChange, report, ct);
            Set(s => s with { Message = $"Kept a copy from before {what}, held until you restore it or let it go." });
            return kept;
        }, keepMessage: false, ct);

    /// <summary>
    /// Takes a copy into each of <paramref name="into"/> (the app's own store first) within a piece of work already running:
    /// everything the app keeps gathered once, then written into each. Says what it wrote (when <paramref name="say"/>), and
    /// shows the app's own copies after.
    /// </summary>
    private async Task<StoreCopy> CopyWithinAsync(SyncReason reason, string? update, IReadOnlyList<ISyncStore> into, bool say, Action<string, double> report, IProgress<double>? progress, CancellationToken ct)
    {
        var previous = (await CopiesInAsync(Store, ct)).FirstOrDefault();

        // Reading counts for the first fifth of the bar, writing for the rest.
        await using var gathering = await GatherAsync(previous?.Manifest, (doing, part) =>
        {
            report(doing, part);
            progress?.Report(part * 0.2);
        }, ct);

        StoreCopy? copy = null;
        var (files, bytes) = (0, 0L);
        foreach (var store in into)
        {
            try
            {
                if (!await store.ConnectAsync(ct)) throw new SyncException($"{store.Name} can't be used just now");
                var written = await WriteCopyAsync(store, gathering, reason, update, (doing, part) =>
                {
                    report(doing, part);
                    if (store == Store) progress?.Report(0.2 + part * 0.8);
                }, ct);
                if (store != Store) continue;
                (copy, files, bytes) = written;
            }
            catch (Exception e) when (store != Store && e is not OperationCanceledException)
            {
                // Another store (a cloud drive, say) that couldn't take it this time: the app's own has it.
                logger?.LogWarning(e, "A copy couldn't go to {Store}: {Message}", store.Name, e.Message);
            }
        }

        progress?.Report(1);
        await ShowOwnAsync(copy, ct);
        if (say)
        {
            var said = files == 0 ? "Copied: nothing had changed since the copy before."
                : $"Copied: {files} changed {(files == 1 ? "file" : "files")}, {SyncText.Size(bytes)} written.";
            Set(s => s with { Message = said });
        }
        return copy!;
    }

    /// <summary>A copy of the data as it is into the app's own store alone, held: kept from before a restore, or a change the app makes.</summary>
    private async Task<StoreCopy> KeepWithinAsync(SyncHeldFrom from, string why, SyncReason reason, Action<string, double> report, CancellationToken ct)
    {
        var copy = await CopyWithinAsync(reason, null, [Store], say: false, report, null, ct);
        var held = new SyncHeld(copy.Key, "", why, time.GetUtcNow()) { From = from };
        await HoldAsync(Store, held, ct);
        await ShowOwnAsync(copy, ct);
        return Described(new StoreCopy(Store, copy.Key, copy.Manifest, held));
    }

    /// <summary>
    /// What the app does as it starts, in the background. The first time after an update, it checks at once that the app's data
    /// holds all of the person's the copy from before the update did (each database opens cleanly and has at least the rows of
    /// what's counted as theirs, and every file in its folders is there), and only then, after <see cref="SyncOptions.StartDelay"/>,
    /// takes a fresh copy; one that doesn't check out is held, kept whatever copies come after, until it's restored or let go.
    /// Otherwise, after the delay, it takes a copy when one is due (<see cref="Due"/>). What goes wrong is in the status, and the log.
    /// </summary>
    public async Task StartAsync(CancellationToken ct = default)
    {
        try
        {
            await RefreshAsync(ct);
            var updated = Status.Last is StoreCopy { Manifest: { Reason: SyncReason.BeforeUpdate } before } last && before.Version != options.Version ? last : null;
            // The check after an update runs at once, before the app's screens can change its data. When it couldn't hold the copy
            // from before, no copy is taken: it would take that one's place.
            if (updated is not null && !await CheckAfterUpdateAsync(updated, ct)) return;

            if (options.StartDelay > TimeSpan.Zero) await Task.Delay(options.StartDelay, time, ct);
            // What an earlier run left in the work folder, once nothing is using it.
            await gate.WaitAsync(ct);
            try
            {
                await Task.Run(ClearWork, ct);
            }
            finally
            {
                gate.Release();
            }
            if (updated is null)
            {
                await CopyIfDueAsync(SyncReason.Scheduled, ct);
                return;
            }
            if (options.Quiet is { } quiet) await quiet(ct);
            await CopyAsync(SyncReason.AfterUpdate, ct: ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
        }
        catch (Exception e) when (e is SyncException or IOException or UnauthorizedAccessException or SqliteException)
        {
            // Said in the status, and logged where it happened.
        }
    }

    /// <summary>
    /// Takes a copy in the background when one is due (<see cref="Due"/>), once the app is quiet (<see cref="SyncOptions.Quiet"/>):
    /// as the app starts, and after practice. What goes wrong is in the status, and the log.
    /// </summary>
    public async Task CopyIfDueAsync(SyncReason reason = SyncReason.Scheduled, CancellationToken ct = default)
    {
        try
        {
            if (options.Quiet is { } quiet) await quiet(ct);
            if (!Status.Known) await RefreshAsync(ct);
            await RunAsync<SyncCopy?>(SyncStep.Copying, "Getting ready", async report =>
            {
                if (options.BeforeCopy is { } before) await before();
                return await CopyWithinAsync(reason, null, stores, say: true, report, null, ct);
            }, keepMessage: false, ct, unless: () => !Due());
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
        }
        catch (Exception e) when (e is SyncException or IOException or UnauthorizedAccessException or SqliteException)
        {
            // Said in the status, and logged where it happened.
        }
    }

    /// <summary>
    /// Whether a copy is due: there's none yet, the last is older than <see cref="SyncOptions.RefreshAfter"/>, or it's dated after now
    /// (taken with the clock ahead, which would otherwise hold off every copy until the real date caught up with it).
    /// </summary>
    private bool Due()
    {
        var now = time.GetUtcNow();
        return Status.Last is not { } last || last.Made > now || now - last.Made > options.RefreshAfter;
    }

    /// <summary>Puts a copy's data in place of the app's, keeping what's there first (<see cref="RestoreAsync(SyncCopy, bool, CancellationToken)"/>).</summary>
    public Task RestoreAsync(SyncCopy copy, CancellationToken ct = default) => RestoreAsync(copy, keepWhatsHere: true, ct);

    /// <summary>
    /// Puts a copy's data in place of the app's. Every file is fetched beside where it goes and checked against its fingerprint, and
    /// each database is checked (it opens cleanly, is the app's, and holds what its manifest says), before anything of the app's is
    /// touched. Then, while the app leaves its data alone (<see cref="SyncOptions.WhileRestoring"/>), a copy of the data as it is
    /// goes into the app's own store, held until it's restored or let go, so a wrong restore can be undone: unless
    /// <paramref name="keepWhatsHere"/> is false, or nothing here is the person's yet (a fresh install). If it can't be kept,
    /// nothing is restored (<see cref="SyncKeepException"/>). Each item then goes in at once, and if one can't, those before it go
    /// back. Only the items the copy has are replaced. A fresh copy is taken after.
    /// </summary>
    public async Task RestoreAsync(SyncCopy copy, bool keepWhatsHere, CancellationToken ct = default)
    {
        await RunAsync(SyncStep.Restoring, "Fetching the copy", async report =>
        {
            var plan = PlanRestore(copy.Manifest);
            var total = Math.Max(1, plan.Sum(p => copy.Manifest.FilesOf(p.Copied.Name).Sum(f => f.Size)));
            long fetched = 0;
            var placements = new List<Placement>();
            var (keep, placed) = (false, false);
            try
            {
                foreach (var (live, copied) in plan)
                {
                    placements.Add(await StageAsync(copy, live, copied, n => report($"Fetching the copy: {SyncText.Size(fetched += n)} of {SyncText.Size(total)}", (double)fetched / total), ct));
                }
                keep = keepWhatsHere && !await Task.Run(HoldsNothing, ct);
                if (keep && options.BeforeCopy is { } before) await before();
                await (options.WhileRestoring ?? (work => work()))(async () =>
                {
                    // Kept with the app's data held still, so nothing it writes falls between what's kept and what's restored.
                    if (keep) await KeepWhatsHereAsync(copy, report, ct);
                    report("Putting it in place", 1);
                    await PlaceAsync(placements);
                    placed = true;
                });
            }
            catch (Exception e) when (placed && e is not OperationCanceledException)
            {
                // Everything was in place when the app's side of it failed: the data is the copy's, whatever came after.
                logger?.LogWarning(e, "A restore was in place when the app's side of it failed: {Message}", e.Message);
            }
            finally
            {
                foreach (var placement in placements) placement.Clean();
            }

            // The copy restored isn't held any more when it's this install's own; restoring another, or one held in another folder,
            // leaves this install's held. Letting it go comes after the data is the copy's, so if it can't be, that's all it says.
            var said = $"Restored the copy from {SyncText.When(copy.Made, time)}." + (keep ? " What was here is kept, held until you restore it or let it go." : "");
            if (copy.Held is { } held)
            {
                try
                {
                    await ReleaseAsync(Store, held, CancellationToken.None);
                }
                catch (Exception e) when (e is IOException or UnauthorizedAccessException)
                {
                    logger?.LogWarning(e, "A copy restored couldn't be let go: {Message}", e.Message);
                    said += $" It's still held: it couldn't be let go ({Reason(e)}).";
                }
            }
            await ShowOwnAsync(null, CancellationToken.None);
            Set(s => s with { Message = said });
            return true;
        }, keepMessage: false, ct);

        // This install's own store gets the data as it now is, in the background.
        _ = CopyQuietlyAsync(SyncReason.AfterRestore);
    }

    /// <summary>What's here, into the app's own store and held, before a restore puts <paramref name="restoring"/> in its place.</summary>
    private async Task KeepWhatsHereAsync(SyncCopy restoring, Action<string, double> report, CancellationToken ct)
    {
        try
        {
            await KeepWithinAsync(SyncHeldFrom.Restore, $"the copy from {SyncText.When(restoring.Made, time)} was restored in its place", SyncReason.BeforeRestore,
                (_, part) => report("Keeping what's here first", part), ct);
        }
        catch (Exception e) when (e is SyncException or IOException or UnauthorizedAccessException or SqliteException)
        {
            throw new SyncKeepException($"What's here couldn't be kept first ({Reason(e)}), so nothing was restored.", e);
        }
    }

    /// <summary>
    /// Whether nothing here is the person's yet (a fresh install): no rows in what each database counts as theirs, settings aside,
    /// and no file in its folders. A restore then has nothing to keep first. A database that can't be read holds something.
    /// </summary>
    private bool HoldsNothing()
    {
        try
        {
            foreach (var item in options.Items)
            {
                switch (item)
                {
                    case DatabaseItem database when File.Exists(database.Path):
                        var rows = database.Counted is { } counted
                            ? SqliteFiles.Count(database.Path, counted.Where(c => !c.Settings)).Values
                            : SqliteFiles.Tables(database.Path).Values.Select(v => (long?)v);
                        if (rows.Any(r => r > 0)) return false;
                        break;
                    case FolderItem folder when FilesIn(folder.Path).Any():
                        return false;
                }
            }
            return true;
        }
        catch (SqliteException)
        {
            return false;
        }
    }

    /// <summary>
    /// Writes everything the app keeps (but its secrets and excluded files) into one archive in <paramref name="folder"/>, its files
    /// under their own paths and the manifest beside them, to hand on: its path. It restores like any copy.
    /// </summary>
    public Task<string> ExportAsync(string folder, CancellationToken ct = default) =>
        RunAsync(SyncStep.Exporting, "Getting ready", async report =>
        {
            if (options.BeforeCopy is { } before) await before();
            await using var gathering = await GatherAsync(null, (doing, part) => report(doing, part * 0.3), ct);
            Directory.CreateDirectory(folder);
            // Named for the time where the device is, written the same way whatever its calendar.
            var path = Path.Combine(folder, $"{options.App} {SyncText.Local(time.GetUtcNow(), time).ToString("yyyy-MM-dd HHmm", CultureInfo.InvariantCulture)}.zip");
            var partial = path + ".partial";
            var total = Math.Max(1, gathering.Files.Sum(f => f.Size));
            long done = 0;
            try
            {
                await Task.Run(() =>
                {
                    using var output = new FileStream(partial, FileMode.Create, FileAccess.Write, FileShare.None, 81920);
                    using var archive = new ZipArchive(output, ZipArchiveMode.Create);
                    var written = new List<Gathered>();
                    foreach (var file in gathering.Files)
                    {
                        // Each file is fingerprinted and searched again as it goes in, as it is then: one written to since it was
                        // looked at (the problem log, say) goes in as it is now, and one gone since is left out.
                        FileStream input;
                        try
                        {
                            input = Open(file.Source);
                        }
                        catch (Exception e) when (e is FileNotFoundException or DirectoryNotFoundException)
                        {
                            continue;
                        }
                        using (input)
                        {
                            var modified = file.Fresh ? file.Modified : new DateTimeOffset(File.GetLastWriteTimeUtc(file.Source));
                            var entry = archive.CreateEntry(file.Path, CompressionLevel.Fastest);
                            // A zip keeps times from 1980 on.
                            entry.LastWriteTime = modified.Year < 1980 ? new DateTimeOffset(1980, 1, 1, 0, 0, 0, TimeSpan.Zero) : modified;
                            using var content = entry.Open();
                            var (size, sha256) = Scan(input, content, file.Path, gathering.Secrets,
                                n => report($"Writing the archive: {SyncText.Size(done += n)} of {SyncText.Size(total)}", 0.3 + 0.7 * Math.Min(1, (double)done / total)), ct);
                            written.Add(file with { Size = size, Modified = modified, Sha256 = sha256 });
                        }
                    }
                    gathering.Files.Clear();
                    gathering.Files.AddRange(written);
                    var manifest = Manifest(gathering, SyncReason.Asked, null, time.GetUtcNow());
                    using var json = archive.CreateEntry(SyncPaths.ExportManifest, CompressionLevel.Fastest).Open();
                    json.Write(manifest.ToJson());
                }, ct);
                File.Move(partial, path, overwrite: true);
            }
            catch
            {
                TryDelete(partial);
                throw;
            }

            Set(s => s with { Message = $"Exported everything to {Path.GetFileName(path)}, {SyncText.Size(new FileInfo(path).Length)}." });
            return path;
        }, keepMessage: false, ct);

    /// <summary>
    /// Opens a file picked to restore from: an export (an archive with a manifest) or one of the app's databases on its own (which
    /// restores that database alone). Null when it's neither, or another app's.
    /// </summary>
    public async Task<SyncCopy?> OpenFileAsync(string path, CancellationToken ct = default) => await Task.Run<SyncCopy?>(() =>
    {
        var start = new byte[16];
        using (var head = File.OpenRead(path)) head.ReadAtLeast(start, start.Length, throwOnEndOfStream: false);

        if (start.AsSpan().StartsWith("PK\x03\x04"u8))
        {
            try
            {
                using var archive = ZipFile.OpenRead(path);
                if (archive.GetEntry(SyncPaths.ExportManifest) is not { } entry) return null;
                using var json = entry.Open();
                return SyncManifest.FromJson(json) is { } manifest && manifest.App == options.App && Valid(manifest) ? Described(new ArchiveCopy(path, manifest)) : null;
            }
            catch (InvalidDataException)
            {
                return null;
            }
        }

        if (!SqliteFiles.IsDatabase(start) || options.Items.OfType<DatabaseItem>().ToList() is not [var database]) return null;
        if (SqliteFiles.Problem(path) is not null) return null;
        var file = Fingerprint(database.Name, path, new DateTimeOffset(File.GetLastWriteTimeUtc(path)), false, [], ct);
        var alone = new SyncManifest
        {
            App = options.App,
            Made = file.Modified,
            Reason = SyncReason.Asked,
            Items = [new SyncManifestItem(database.Name, SyncItemKind.Database) { Tables = SqliteFiles.Tables(path) }],
            Files = [new SyncFile(file.Path, file.Size, file.Modified, file.Sha256)],
        };
        return Described(new DatabaseFileCopy(path, alone));
    }, ct);

    /// <summary>Lets go of the newest copy held (the one <see cref="SyncStatus.Held"/> is), when there's one.</summary>
    public async Task ReleaseHeldAsync(CancellationToken ct = default)
    {
        if (!Status.Known) await RefreshAsync(ct);
        if (Status.Holding.FirstOrDefault() is { } newest) await ReleaseHeldAsync(newest, ct);
    }

    /// <summary>Lets go of a copy held in the app's own store: newer copies take its place from then on, as with any other.</summary>
    public async Task ReleaseHeldAsync(SyncCopy copy, CancellationToken ct = default)
    {
        if (copy.Held is not { } held) return;
        await gate.WaitAsync(ct);
        try
        {
            await ReleaseAsync(Store, held, ct);
            await ShowOwnAsync(null, ct);
            Set(s => s with { Message = "Let go: newer copies take its place, as with any other." });
        }
        finally
        {
            gate.Release();
        }
    }

    // ---- Taking a copy ----

    /// <summary>
    /// Everything the app keeps, as files ready to go in a copy: each database snapshotted into the work folder, each item's
    /// files listed, and each one fingerprinted: with the last copy's fingerprint when its size and time are the same, or read
    /// now and searched for the secrets. Nothing is written to a store yet, so a secret stops it all before anything is.
    /// </summary>
    private async Task<Gathering> GatherAsync(SyncManifest? before, Action<string, double> report, CancellationToken ct)
    {
        var secrets = options.Secrets is null ? [] : (await options.Secrets(ct)).Select(s => s.Trim()).Where(s => s.Length >= 8).Select(Encoding.UTF8.GetBytes).ToList();
        var gathering = new Gathering(Directory.CreateDirectory(Path.Combine(options.WorkFolder, Guid.NewGuid().ToString("N"))).FullName, secrets);
        try
        {
            var known = new Dictionary<string, SyncFile>(StringComparer.Ordinal);
            foreach (var file in before?.Files ?? []) known.TryAdd(file.Path, file);
            var listed = new List<(string Path, string Source, DateTimeOffset? Modified, bool Fresh)>();
            foreach (var item in options.Items)
            {
                ct.ThrowIfCancellationRequested();
                switch (item)
                {
                    case DatabaseItem database:
                        var tables = new Dictionary<string, long>();
                        Dictionary<string, long>? counts = null;
                        if (File.Exists(database.Path))
                        {
                            report($"Copying {item.Name} as it is now", 0);
                            var snapshot = Path.Combine(gathering.Work, Guid.NewGuid().ToString("N") + ".db");
                            var changed = new DateTimeOffset(File.GetLastWriteTimeUtc(database.Path));
                            (tables, counts) = await Task.Run(() =>
                            {
                                SqliteFiles.Snapshot(database.Path, snapshot);
                                var counted = database.Counted is null ? null
                                    : SqliteFiles.Count(snapshot, database.Counted).Where(c => c.Value is not null).ToDictionary(c => c.Key, c => c.Value!.Value, StringComparer.Ordinal);
                                return (SqliteFiles.Tables(snapshot), counted);
                            }, ct);
                            listed.Add((item.Name, snapshot, changed, true));
                        }
                        gathering.Items.Add(new SyncManifestItem(item.Name, SyncItemKind.Database) { Tables = tables, Counts = counts });
                        break;
                    case FolderItem folder:
                        gathering.Items.Add(new SyncManifestItem(item.Name, SyncItemKind.Folder));
                        foreach (var file in FilesIn(folder.Path)) listed.Add((SyncPaths.InCopy(item.Name, folder.Path, file), file, null, false));
                        break;
                    case FileItem one:
                        gathering.Items.Add(new SyncManifestItem(item.Name, SyncItemKind.File));
                        if (File.Exists(one.Path) && !Excluded(one.Path)) listed.Add((item.Name, one.Path, null, false));
                        break;
                    case CustomItem custom:
                        var written = Directory.CreateDirectory(Path.Combine(gathering.Work, Guid.NewGuid().ToString("N"))).FullName;
                        await custom.Export.WriteAsync(written, ct);
                        gathering.Items.Add(new SyncManifestItem(item.Name, SyncItemKind.Custom));
                        foreach (var file in FilesIn(written)) listed.Add((SyncPaths.InCopy(item.Name, written, file), file, null, true));
                        break;
                }
            }

            for (var i = 0; i < listed.Count; i++)
            {
                var (path, source, changedAt, fresh) = listed[i];
                try
                {
                    var info = new FileInfo(source);
                    var modified = changedAt ?? new DateTimeOffset(info.LastWriteTimeUtc);
                    gathering.Files.Add(!fresh && known.TryGetValue(path, out var was) && was.Size == info.Length && was.Modified == modified
                        ? new Gathered(path, source, was.Size, modified, was.Sha256, fresh)
                        : await Task.Run(() => Fingerprint(path, source, modified, fresh, secrets, ct), ct));
                }
                catch (Exception e) when (e is FileNotFoundException or DirectoryNotFoundException)
                {
                    // Gone since it was listed (the problem log turned over, a piece taken out of the Library): not in the copy.
                }
                report($"Looking at what changed: {i + 1} of {listed.Count} files", (i + 1.0) / listed.Count);
            }
            return gathering;
        }
        catch
        {
            await gathering.DisposeAsync();
            throw;
        }
    }

    /// <summary>
    /// Writes a copy into a store: the content it doesn't hold yet (a file that changed since it was fingerprinted is fingerprinted
    /// again and written as it is now), then the manifest, then whatever no copy kept needs any more goes. The copy, and how many
    /// files and bytes were written.
    /// </summary>
    private async Task<(StoreCopy Copy, int Files, long Bytes)> WriteCopyAsync(ISyncStore store, Gathering gathering, SyncReason reason, string? update, Action<string, double> report, CancellationToken ct)
    {
        var have = (await store.ListAsync(SyncPaths.Blobs, ct)).ToDictionary(o => o.Key, o => o.Size, StringComparer.Ordinal);
        var (files, bytes, total) = (0, 0L, 0L);
        for (var round = 0; ; round++)
        {
            var missing = gathering.Files.GroupBy(f => f.Sha256).Select(g => g.First())
                .Where(f => !have.TryGetValue(SyncPaths.Blob(f.Sha256), out var size) || size != f.Size).ToList();
            if (missing.Count == 0) break;
            if (round == 3) throw new SyncException($"{missing[0].Path} kept changing while it was copied");

            total += missing.Sum(f => f.Size);
            foreach (var file in missing)
            {
                try
                {
                    try
                    {
                        var name = file.Path;
                        await using var input = new CheckedStream(Open(file.Source), file.Size, file.Sha256, () => new SyncChangedException(name),
                            n => report($"Copying {(total == 0 ? "" : $"{SyncText.Size(bytes += n)} of {SyncText.Size(total)}")}", total == 0 ? 1 : Math.Min(1, (double)bytes / total)));
                        await store.WriteAsync(SyncPaths.Blob(file.Sha256), input, ct);
                        have[SyncPaths.Blob(file.Sha256)] = file.Size;
                        files++;
                    }
                    catch (SyncChangedException)
                    {
                        // Changed since it was fingerprinted (a log written to, say): fingerprinted again, to be written as it is now.
                        var again = await Task.Run(() => Fingerprint(file.Path, file.Source, new DateTimeOffset(File.GetLastWriteTimeUtc(file.Source)), file.Fresh, gathering.Secrets, ct), ct);
                        gathering.Files[gathering.Files.FindIndex(f => f.Path == file.Path)] = again;
                    }
                }
                catch (Exception e) when (e is FileNotFoundException or DirectoryNotFoundException)
                {
                    // Gone since it was looked at (the problem log turned over, a piece taken out of the Library): the copy has the rest.
                    gathering.Files.RemoveAll(f => f.Path == file.Path);
                }
            }
        }

        // The manifest last, under a name later than every copy's before it, so the newest always sorts last, then what's no longer
        // needed. It's dated now all the same: one taken with the clock ahead names the next ones after it, but doesn't date them.
        var copies = (await store.ListAsync(SyncPaths.Copies, ct)).Select(o => o.Key).Where(SyncPaths.IsCopy).ToList();
        var made = time.GetUtcNow();
        var named = made.AddTicks(-(made.UtcTicks % TimeSpan.TicksPerMillisecond));
        if (copies.Count > 0 && SyncPaths.TimeOf(copies.Max(StringComparer.Ordinal)!) is { } newest && newest >= named) named = newest.AddMilliseconds(1);
        var manifest = Manifest(gathering, reason, update, made);
        var key = SyncPaths.Copy(named);
        using (var json = new MemoryStream(manifest.ToJson())) await store.WriteAsync(key, json, ct);
        report("Tidying up", 1);
        await TidyAsync(store, key, manifest, copies, ct);
        return (Described(new StoreCopy(store, key, manifest, null)), files, bytes);
    }

    /// <summary>
    /// Deletes the copies a store keeps no longer, then each piece of content no copy left needs. A store keeps its three newest
    /// copies; besides them, the newest at least a week older than <paramref name="key"/>'s, and the oldest younger than that, which
    /// comes to be it (taken every day, the copies would otherwise all go before any was a week old); and every one held. When a
    /// held copy can't be read, nothing goes: better kept than lost. The copy is made by then, so what can't be tidied away is
    /// left for the next one, and logged.
    /// </summary>
    private async Task TidyAsync(ISyncStore store, string key, SyncManifest manifest, List<string> before, CancellationToken ct)
    {
        try
        {
            var holds = await ReadHoldsAsync(store, ct);
            var copies = new List<(string Key, SyncManifest Manifest)> { (key, manifest) };
            foreach (var other in before.Where(k => k != key))
            {
                // One that can't be read isn't a copy, and goes (but a held one).
                if (await ReadManifestAsync(store, other, ct) is { } read && read.App == options.App && Valid(read)) copies.Add((other, read));
                else if (holds.Any(h => h.Copy == other)) return;
            }
            copies.Sort((a, b) => string.CompareOrdinal(b.Key, a.Key));

            var kept = copies.Take(3).Select(c => c.Key).ToHashSet(StringComparer.Ordinal);
            var weekBefore = manifest.Made - Week;
            if (copies.FirstOrDefault(c => c.Manifest.Made <= weekBefore) is { Key: { } weekOld }) kept.Add(weekOld);
            if (copies.LastOrDefault(c => c.Manifest.Made > weekBefore && !kept.Contains(c.Key)) is { Key: { } coming }) kept.Add(coming);
            kept.UnionWith(holds.Select(h => h.Copy));

            var needed = copies.Where(c => kept.Contains(c.Key)).SelectMany(c => c.Manifest.Files).Select(f => SyncPaths.Blob(f.Sha256)).ToHashSet(StringComparer.Ordinal);
            foreach (var old in before.Where(k => !kept.Contains(k))) await store.DeleteAsync(old, ct);
            foreach (var blob in await store.ListAsync(SyncPaths.Blobs, ct))
            {
                if (!needed.Contains(blob.Key)) await store.DeleteAsync(blob.Key, ct);
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            logger?.LogWarning(e, "A copy in {Store} couldn't tidy away what no copy needs any more: {Message}", store.Name, e.Message);
        }
    }

    private SyncManifest Manifest(Gathering gathering, SyncReason reason, string? update, DateTimeOffset made) => new()
    {
        App = options.App,
        Version = options.Version,
        Made = made,
        Reason = reason,
        Update = update,
        Items = [.. gathering.Items],
        Files = [.. gathering.Files.Select(f => new SyncFile(f.Path, f.Size, f.Modified, f.Sha256))],
    };

    /// <summary>Reads a file whole for its SHA-256, searching it for each secret as it goes.</summary>
    private static Gathered Fingerprint(string path, string source, DateTimeOffset modified, bool fresh, IReadOnlyList<byte[]> secrets, CancellationToken ct)
    {
        using var input = Open(source);
        var (size, sha256) = Scan(input, null, path, secrets, null, ct);
        return new Gathered(path, source, size, modified, sha256, fresh);
    }

    /// <summary>
    /// Reads <paramref name="input"/> to its end for its size and SHA-256, searching it for each secret as it goes (across the joins
    /// between reads too), and writing what's been searched on to <paramref name="output"/> when there's one. A secret found stops it.
    /// </summary>
    private static (long Size, string Sha256) Scan(Stream input, Stream? output, string path, IReadOnlyList<byte[]> secrets, Action<int>? read, CancellationToken ct)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var overlap = secrets.Count == 0 ? 0 : secrets.Max(s => s.Length) - 1;
        var buffer = new byte[81920 + overlap];
        var carried = 0;
        long size = 0;
        int count;
        while ((count = input.Read(buffer, carried, 81920)) > 0)
        {
            ct.ThrowIfCancellationRequested();
            hash.AppendData(buffer, carried, count);
            size += count;
            var seen = buffer.AsSpan(0, carried + count);
            foreach (var secret in secrets)
            {
                if (seen.IndexOf(secret) >= 0) throw new SyncSecretException(path);
            }
            output?.Write(buffer, carried, count);
            read?.Invoke(count);
            carried = Math.Min(overlap, seen.Length);
            seen[^carried..].CopyTo(buffer);
        }
        return (size, Convert.ToHexStringLower(hash.GetHashAndReset()));
    }

    // ---- After an update ----

    /// <summary>
    /// Checks the app's data holds all the copy from before an update did; when it doesn't, holds that copy (kept whatever copies
    /// come after, beside any held already) and says why. False when it couldn't check, or couldn't hold the copy: that's said in
    /// the status and the log, and no copy may take its place until the next start checks again.
    /// </summary>
    private async Task<bool> CheckAfterUpdateAsync(StoreCopy before, CancellationToken ct)
    {
        var update = before.Manifest.Update ?? $"version {options.Version}";
        await gate.WaitAsync(ct);
        string? why = null;
        try
        {
            Set(s => s with { Step = SyncStep.Checking, Progress = new SyncProgress($"Checking your data after the update to {update}", 0), Message = "", Failed = false });
            if ((why = await Task.Run(() => Missing(before.Manifest), ct)) is null) return true;

            await HoldAsync(before.Store, new SyncHeld(before.Key, update, why, time.GetUtcNow()), ct);
            logger?.LogWarning("After the update to {Update}, the data had less than the copy taken just before it: {Why}. That copy is kept until it's restored or let go.", update, why);
            await ShowOwnAsync(null, ct);
            return true;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or SqliteException)
        {
            var said = why is null
                ? $"Your data couldn't be checked after the update to {update}: {Reason(e)}. It's checked again as {options.App} next starts."
                : $"After the update to {update}, the data had less than the copy taken just before it ({why}), but that copy couldn't be held: {Reason(e)}. It's tried again as {options.App} next starts.";
            Set(s => s with { Message = said, Failed = true });
            logger?.LogWarning(e, "After the update to {Update}, {What}: {Message}", update, why is null ? "the data couldn't be checked" : $"the copy from before ({why}) couldn't be held", e.Message);
            return false;
        }
        finally
        {
            Set(s => s with { Step = SyncStep.Idle, Progress = null });
            gate.Release();
        }
    }

    /// <summary>
    /// What the app's data lacks of a copy, or null when it holds all of it: each database opens cleanly and has at least the rows
    /// of what's counted as the person's own (<see cref="DatabaseItem.Counted"/>; without it, every table), and every file in its
    /// folders is there. A file on its own (a log) can come and go, and isn't asked after.
    /// </summary>
    internal string? Missing(SyncManifest copy)
    {
        foreach (var copied in copy.Items)
        {
            switch (options.Items.FirstOrDefault(i => i.Name == copied.Name))
            {
                case null:
                    return $"{copied.Name} isn't kept any more";
                case DatabaseItem database when copied.Tables is { Count: > 0 } tables:
                    if (!File.Exists(database.Path)) return $"{copied.Name} is gone";
                    if (SqliteFiles.Problem(database.Path) is { } problem) return $"{copied.Name} doesn't open cleanly: {problem}";
                    if (database.Counted is not { } counted)
                    {
                        var now = SqliteFiles.Tables(database.Path);
                        foreach (var (table, rows) in tables)
                        {
                            if (!now.TryGetValue(table, out var has)) return $"{copied.Name} has lost its {table} table";
                            if (has < rows) return Fewer(copied.Name, table, rows, has);
                        }
                        break;
                    }
                    // Only what's the person's own: by its count in the copy, or in a copy taken before counts were kept, by its
                    // table's rows when it's counted whole (rows a condition picks can't be told from such a copy).
                    var found = SqliteFiles.Count(database.Path, counted);
                    foreach (var count in counted)
                    {
                        long? had = copied.Counts is { } kept ? (kept.TryGetValue(count.Name, out var rows) ? rows : null)
                            : count.Where is null && tables.TryGetValue(count.Table, out var whole) ? whole : null;
                        if (had is null) continue;
                        if (found[count.Name] is not { } has) return $"{copied.Name} has lost its {count.Table} table";
                        if (has < had) return Fewer(copied.Name, count.Name, had.Value, has);
                    }
                    break;
                case FolderItem folder:
                    if (copy.FilesOf(copied.Name).FirstOrDefault(f => !File.Exists(Path.Combine(folder.Path, Inside(f.Path, copied.Name)))) is { } gone) return $"{gone.Path} is gone";
                    break;
            }
        }
        return null;

        static string Fewer(string database, string what, long had, long has) =>
            $"{database}'s {what} had {had.ToString("N0", CultureInfo.InvariantCulture)} rows and has {has.ToString("N0", CultureInfo.InvariantCulture)}";
    }

    // ---- Restoring ----

    /// <summary>Each item the copy has that the app keeps, paired; throws when the copy holds nothing of the app's, or a file it lists can't be.</summary>
    private List<(SyncItem Live, SyncManifestItem Copied)> PlanRestore(SyncManifest copy)
    {
        if (!Valid(copy)) throw new SyncException("That copy's list of files isn't one this app can restore from, so nothing was restored.");
        var plan = new List<(SyncItem, SyncManifestItem)>();
        foreach (var copied in copy.Items)
        {
            if (options.Items.FirstOrDefault(i => i.Name == copied.Name && i.Kind == copied.Kind) is { } live) plan.Add((live, copied));
            else logger?.LogWarning("A copy's {Item} isn't kept by this app any more, so it isn't restored.", copied.Name);
        }
        return plan.Count > 0 ? plan : throw new SyncException($"That copy holds nothing {options.App} keeps, so nothing was restored.");
    }

    /// <summary>Whether a copy's files all have paths inside its items and real fingerprints, so restoring can't reach elsewhere.</summary>
    private static bool Valid(SyncManifest copy) => copy.Files.All(f =>
        SyncPaths.IsPath(f.Path) && f.Size >= 0 && f.Sha256.Length == 64 && f.Sha256.All(char.IsAsciiHexDigit) && copy.Items.Any(i => SyncPaths.Within(f.Path, i.Name)));

    /// <summary>
    /// Fetches an item's files beside where they go, each checked against its fingerprint, and a database checked too. What was
    /// fetched goes again if any of it can't be.
    /// </summary>
    private async Task<Placement> StageAsync(SyncCopy copy, SyncItem live, SyncManifestItem copied, Action<int> read, CancellationToken ct)
    {
        var files = copy.Manifest.FilesOf(copied.Name).ToList();
        // A file the copy didn't have isn't there after.
        if (live is FileItem absent && files.Count == 0) return new PathPlacement(absent.Path, null, []);

        var staged = live switch
        {
            DatabaseItem database => Fresh(database.Path + Restoring),
            FileItem one => Fresh(one.Path + Restoring),
            FolderItem folder => Directory.CreateDirectory(Fresh(folder.Path + Restoring)).FullName,
            _ => Directory.CreateDirectory(Path.Combine(options.WorkFolder, "restore-" + Guid.NewGuid().ToString("N"))).FullName,
        };
        try
        {
            switch (live)
            {
                case DatabaseItem database:
                    var file = files.SingleOrDefault(f => f.Path == copied.Name) ?? throw new SyncException($"That copy has no {copied.Name}, so nothing was restored.");
                    await FetchAsync(copy, file, staged, read, ct);
                    await Task.Run(() => CheckDatabase(database, copied, staged), ct);
                    return new PathPlacement(database.Path, staged, [database.Path + "-journal", database.Path + "-wal", database.Path + "-shm"]);
                case FileItem one:
                    await FetchAsync(copy, files.Single(f => f.Path == copied.Name), staged, read, ct);
                    return new PathPlacement(one.Path, staged, []);
                case FolderItem folder:
                    foreach (var each in files) await FetchAsync(copy, each, Path.Combine(staged, Inside(each.Path, copied.Name)), read, ct);
                    return new PathPlacement(folder.Path, staged, []);
                case CustomItem custom:
                    foreach (var each in files) await FetchAsync(copy, each, Path.Combine(staged, Inside(each.Path, copied.Name)), read, ct);
                    return new CustomPlacement(custom.Export, staged);
                default:
                    throw new SyncException($"{copied.Name} can't be restored.");
            }
        }
        catch
        {
            TryDelete(staged);
            throw;
        }
    }

    private static async Task FetchAsync(SyncCopy copy, SyncFile file, string target, Action<int> read, CancellationToken ct)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        await using (var input = new CheckedStream(await copy.OpenAsync(file, ct), file.Size, file.Sha256, () => new SyncException($"{file.Path} in that copy is damaged, so nothing was restored."), read))
        await using (var output = new FileStream(target, FileMode.Create, FileAccess.Write, FileShare.None, 81920, useAsync: true))
        {
            await input.CopyToAsync(output, 81920, ct);
        }
        // Its own time back, so the next copy finds it unchanged without reading it.
        File.SetLastWriteTimeUtc(target, file.Modified.UtcDateTime);
    }

    private void CheckDatabase(DatabaseItem database, SyncManifestItem copied, string staged)
    {
        if (SqliteFiles.Problem(staged) is { } problem) throw new SyncException($"{copied.Name} in that copy doesn't open cleanly ({problem}), so nothing was restored.");
        var tables = SqliteFiles.Tables(staged);
        if (database.RequiredTables.FirstOrDefault(t => !tables.ContainsKey(t)) is { } absent) throw new SyncException($"{copied.Name} in that copy isn't {options.App}'s (it has no {absent} table), so nothing was restored.");
        if (copied.Tables?.FirstOrDefault(t => tables.GetValueOrDefault(t.Key, -1) != t.Value) is { Key: { } table }) throw new SyncException($"{copied.Name} in that copy doesn't hold what its list says ({table}), so nothing was restored.");
    }

    /// <summary>Puts every item in place, the files before anything the app reads back itself; if one can't go in, those before it go back.</summary>
    private async Task PlaceAsync(List<Placement> placements)
    {
        var placed = new List<Placement>();
        try
        {
            foreach (var placement in placements.OrderBy(p => p is CustomPlacement ? 1 : 0))
            {
                placed.Add(placement);
                await placement.PlaceAsync();
            }
        }
        catch
        {
            for (var i = placed.Count - 1; i >= 0; i--)
            {
                try
                {
                    placed[i].Undo();
                }
                catch (Exception e) when (e is IOException or UnauthorizedAccessException)
                {
                    logger?.LogError(e, "A restore couldn't put {Item} back as it was: {Message}", placed[i], e.Message);
                }
            }
            throw;
        }
    }

    // ---- Shared ----

    private async Task<List<StoreCopy>> CopiesInAsync(ISyncStore store, CancellationToken ct)
    {
        var holds = await ReadHoldsAsync(store, ct);
        var copies = new List<StoreCopy>();
        foreach (var key in (await store.ListAsync(SyncPaths.Copies, ct)).Select(o => o.Key).Where(SyncPaths.IsCopy).OrderByDescending(k => k, StringComparer.Ordinal))
        {
            // One that can't be read is a copy that stopped as its manifest was written, or not this app's.
            if (await ReadManifestAsync(store, key, ct) is { } manifest && manifest.App == options.App && Valid(manifest)) copies.Add(Described(new StoreCopy(store, key, manifest, holds.FirstOrDefault(h => h.Copy == key))));
        }
        return copies;
    }

    /// <summary>
    /// The app's own copies, newest first, into the status: the newest, those held (<see cref="SyncStatus.Held"/> the newest one's),
    /// how many there are, and what they take together: content they share (the library's files, mostly) is kept once, so it's
    /// counted once.
    /// </summary>
    private void Show(IReadOnlyList<StoreCopy> copies)
    {
        List<SyncCopy> holding = [.. copies.Where(c => c.Held is not null)];
        var stored = copies.SelectMany(c => c.Manifest.Files).DistinctBy(f => f.Sha256).Sum(f => f.Size);
        Set(s => s with { Known = true, Last = copies.FirstOrDefault(), Held = holding.FirstOrDefault()?.Held, Holding = holding, Copies = copies.Count, Stored = stored });
    }

    /// <summary>The app's own copies as they now are, into the status; when they can't be read again, at least the one just taken.</summary>
    private async Task ShowOwnAsync(StoreCopy? taken, CancellationToken ct)
    {
        try
        {
            Show(await CopiesInAsync(Store, ct));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            if (taken is not null) Set(s => s with { Known = true, Last = taken });
        }
    }

    private static async Task<SyncManifest?> ReadManifestAsync(ISyncStore store, string key, CancellationToken ct)
    {
        await using var json = await store.OpenReadAsync(key, ct);
        return json is null ? null : SyncManifest.FromJson(json);
    }

    /// <summary>The copies a store holds (held.json): a list of them, or one alone, as a store an install before this one kept has it.</summary>
    private static async Task<List<SyncHeld>> ReadHoldsAsync(ISyncStore store, CancellationToken ct)
    {
        await using var json = await store.OpenReadAsync(SyncPaths.Held, ct);
        if (json is null) return [];
        try
        {
            using var document = await JsonDocument.ParseAsync(json, cancellationToken: ct);
            List<SyncHeld?> holds = document.RootElement.ValueKind == JsonValueKind.Array
                ? [.. document.RootElement.Deserialize(SyncJson.Default.ListSyncHeld) ?? []]
                : [document.RootElement.Deserialize(SyncJson.Default.SyncHeld)];
            return [.. holds.OfType<SyncHeld>().Where(h => h.Copy is not null && SyncPaths.IsCopy(h.Copy))];
        }
        catch (JsonException)
        {
            return [];
        }
    }

    /// <summary>Writes the copies a store holds, or deletes held.json when it holds none.</summary>
    private static async Task WriteHoldsAsync(ISyncStore store, List<SyncHeld> holds, CancellationToken ct)
    {
        if (holds.Count == 0)
        {
            await store.DeleteAsync(SyncPaths.Held, ct);
            return;
        }
        using var json = new MemoryStream(JsonSerializer.SerializeToUtf8Bytes(holds, SyncJson.Default.ListSyncHeld));
        await store.WriteAsync(SyncPaths.Held, json, ct);
    }

    /// <summary>
    /// Holds a copy in a store, beside those held already. A copy held already stays held as it was; one whose copy has gone (taken
    /// away by hand) is dropped.
    /// </summary>
    private static async Task HoldAsync(ISyncStore store, SyncHeld held, CancellationToken ct)
    {
        var holds = await ReadHoldsAsync(store, ct);
        if (holds.Any(h => h.Copy == held.Copy)) return;
        var listed = (await store.ListAsync(SyncPaths.Copies, ct)).Select(o => o.Key).ToHashSet(StringComparer.Ordinal);
        await WriteHoldsAsync(store, [.. holds.Where(h => listed.Contains(h.Copy)), held], ct);
    }

    /// <summary>Lets go of a hold in a store: that very one, so a copy held in another store (another install's) lets go of nothing here.</summary>
    private static async Task ReleaseAsync(ISyncStore store, SyncHeld held, CancellationToken ct)
    {
        var holds = await ReadHoldsAsync(store, ct);
        if (holds.Remove(held)) await WriteHoldsAsync(store, holds, ct);
    }

    private T Described<T>(T copy)
        where T : SyncCopy
    {
        try
        {
            copy.Summary = options.Describe?.Invoke(copy.Manifest) ?? $"{copy.Manifest.Files.Count} files";
        }
        catch (Exception e) when (e is not OutOfMemoryException)
        {
            copy.Summary = $"{copy.Manifest.Files.Count} files";
        }
        return copy;
    }

    /// <summary>
    /// Runs one piece of work at a time, on the thread pool, with the status saying what it's doing and how far it is; a failure
    /// becomes the status's sentence, is logged, and is thrown on.
    /// </summary>
    private async Task<T> RunAsync<T>(SyncStep step, string doing, Func<Action<string, double>, Task<T>> work, bool keepMessage, CancellationToken ct, Func<bool>? unless = null)
    {
        await gate.WaitAsync(ct);
        try
        {
            // Work that's no longer wanted once its turn comes (a copy another has made unneeded) leaves the status as it is.
            if (unless?.Invoke() == true) return default!;
            Set(s => s with { Step = step, Progress = new SyncProgress(doing, 0), Message = keepMessage ? s.Message : "", Failed = keepMessage && s.Failed });
            Interlocked.Exchange(ref shownAt, 0);
            return await Task.Run(() => work(Report), ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            Set(s => s with { Message = "Stopped." });
            throw;
        }
        catch (Exception e)
        {
            Set(s => s with { Message = Trouble(step, e), Failed = true });
            logger?.LogWarning(e, "{Step} stopped: {Message}", step, e.Message);
            throw;
        }
        finally
        {
            Set(s => s with { Step = SyncStep.Idle, Progress = null });
            gate.Release();
        }
    }

    /// <summary>How far the work is: the first of each piece of work, then at most every tenth of a second, and always at its end.</summary>
    private void Report(string doing, double fraction)
    {
        var now = time.GetTimestamp();
        var shown = Interlocked.Read(ref shownAt);
        if (fraction < 1 && shown != 0 && time.GetElapsedTime(shown, now) < ProgressEvery) return;
        Interlocked.Exchange(ref shownAt, now);
        Set(s => s.Busy ? s with { Progress = new SyncProgress(doing, Math.Clamp(fraction, 0, 1)) } : s);
    }

    private async Task CopyQuietlyAsync(SyncReason reason)
    {
        try
        {
            await CopyAsync(reason);
        }
        catch (Exception e) when (e is SyncException or IOException or UnauthorizedAccessException or SqliteException)
        {
            // Said in the status, and logged.
        }
    }

    /// <summary>Why something failed, as part of a sentence.</summary>
    private static string Reason(Exception e) => e switch
    {
        SyncSecretException secret => $"{secret.File} holds a secret, which copies never take",
        SqliteException sqlite => $"the database couldn't be read ({sqlite.Message})",
        _ => e.Message.TrimEnd('.'),
    };

    private static string Trouble(SyncStep step, Exception e)
    {
        var why = Reason(e);
        return e is SyncException && why.EndsWith("restored", StringComparison.Ordinal) ? why + "." : step switch
        {
            SyncStep.Copying => $"No copy was made: {why}. The last one is as it was.",
            SyncStep.Restoring => $"Nothing was restored: {why}. Your data is as it was.",
            SyncStep.Exporting => $"Nothing was exported: {why}.",
            _ => $"{why}.",
        };
    }

    private void Set(Func<SyncStatus, SyncStatus> change)
    {
        SyncStatus status;
        lock (statusGate)
        {
            status = change(Status);
            if (status == Status) return;
            Status = status;
        }
        Changed?.Invoke(status);
    }

    private IEnumerable<string> FilesIn(string folder) => Directory.Exists(folder)
        ? Directory.EnumerateFiles(folder, "*", Everything).Where(f => !Excluded(f)).Order(StringComparer.Ordinal)
        : [];

    /// <summary>A file never copied: one the app excludes by name, or one half made (being written, or a restore's).</summary>
    private bool Excluded(string file)
    {
        var name = Path.GetFileName(file);
        return options.Exclude.Contains(name, StringComparer.OrdinalIgnoreCase)
            || name.EndsWith(".tmp", StringComparison.OrdinalIgnoreCase) || name.EndsWith(".partial", StringComparison.OrdinalIgnoreCase)
            || name.EndsWith(Restoring, StringComparison.Ordinal) || name.EndsWith(Replaced, StringComparison.Ordinal);
    }

    /// <summary>Where a file of an item's copy goes under the item's folder.</summary>
    private static string Inside(string path, string item) => path[(item.Length + 1)..].Replace('/', Path.DirectorySeparatorChar);

    private static FileStream Open(string path) => new(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 81920);

    /// <summary>A path cleared of what an earlier restore that stopped left there.</summary>
    private static string Fresh(string path)
    {
        if (Directory.Exists(path)) Directory.Delete(path, recursive: true);
        else if (File.Exists(path)) File.Delete(path);
        return path;
    }

    private void ClearWork()
    {
        try
        {
            if (Directory.Exists(options.WorkFolder)) Directory.Delete(options.WorkFolder, recursive: true);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (Directory.Exists(path)) Directory.Delete(path, recursive: true);
            else if (File.Exists(path)) File.Delete(path);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
        }
    }

    /// <summary>
    /// A file to go in a copy: where it sits in it, where it's read from now, and its size, time and fingerprint. A fresh one (a
    /// database's snapshot, what the app wrote out) is the copy's own, made in the work folder, and keeps the time it was given.
    /// </summary>
    private sealed record Gathered(string Path, string Source, long Size, DateTimeOffset Modified, string Sha256, bool Fresh);

    /// <summary>A copy's files as gathered, and the work folder its snapshots are in, deleted with it.</summary>
    private sealed class Gathering(string work, IReadOnlyList<byte[]> secrets) : IAsyncDisposable
    {
        public string Work => work;

        public IReadOnlyList<byte[]> Secrets => secrets;

        public List<SyncManifestItem> Items { get; } = [];

        public List<Gathered> Files { get; } = [];

        public ValueTask DisposeAsync()
        {
            TryDelete(work);
            return ValueTask.CompletedTask;
        }
    }

    /// <summary>An item fetched and ready: put in place of the app's, put back if a later one can't go, and its leftovers cleared.</summary>
    private abstract class Placement
    {
        public abstract Task PlaceAsync();

        public abstract void Undo();

        public abstract void Clean();
    }

    /// <summary>
    /// A file or folder: what's there now (and the files that go with a database, its journal) moves aside, the copy's moves in.
    /// Undone, they swap back.
    /// </summary>
    private sealed class PathPlacement(string live, string? staged, string[] companions) : Placement
    {
        private readonly List<(string From, string To)> moves = [];

        public override Task PlaceAsync()
        {
            foreach (var path in companions.Prepend(live).Where(Exists))
            {
                Fresh(path + Replaced);
                Move(path, path + Replaced);
            }
            if (staged is not null) Move(staged, live);
            return Task.CompletedTask;
        }

        public override void Undo()
        {
            for (var i = moves.Count - 1; i >= 0; i--)
            {
                var (from, to) = moves[i];
                if (Exists(from)) Fresh(from);
                Relocate(to, from);
            }
            moves.Clear();
        }

        public override void Clean()
        {
            foreach (var path in companions.Prepend(live)) TryDelete(path + Replaced);
            if (staged is not null) TryDelete(staged);
        }

        public override string ToString() => live;

        private void Move(string from, string to)
        {
            Relocate(from, to);
            moves.Add((from, to));
        }

        private static bool Exists(string path) => File.Exists(path) || Directory.Exists(path);

        private static void Relocate(string from, string to)
        {
            if (Directory.Exists(from)) Directory.Move(from, to);
            else File.Move(from, to);
        }
    }

    /// <summary>Something the app reads back itself: done last, and can't be undone.</summary>
    private sealed class CustomPlacement(ISyncExport export, string staged) : Placement
    {
        public override Task PlaceAsync() => export.ReadAsync(staged, CancellationToken.None);

        public override void Undo()
        {
        }

        public override void Clean() => TryDelete(staged);
    }
}
