using System.Globalization;
using Dapper;
using Microsoft.Data.Sqlite;
using Prima.Core.Library;
using Prima.Core.Music;

namespace Prima.UI.Storage;

/// <summary>
/// A piece in the Library: bundled or imported, notation (kept as the MEI Verovio made from it) or a PDF (kept as a file, with the
/// MIDI made from the same engraving when there is one). Its level is its hardest excerpt's; PDFs have none. Layout holds how a
/// PDF plays out (its systems, metre and tempo), and Position where you were, both as JSON.
/// </summary>
public sealed record PieceRecord(
    string Id,
    string Origin,
    string Title,
    string Composer,
    string? Work,
    string Format,
    string? File,
    string? Midi,
    int? Level,
    int? TypicalLevel,
    string? Reasons,
    int? Bars,
    string? Source,
    string? SourceUrl,
    string? Licence,
    string? LicenceUrl,
    string? Credit,
    string? Tier,
    double? Tempo,
    string? Layout,
    string? Position,
    DateTimeOffset Added,
    DateTimeOffset? Opened)
{
    public bool IsPdf => Format == "pdf";

    public bool Imported => Origin == "import";

    /// <summary>A PDF whose notes Claude has read, kept as MEI beside it: it plays and is graded as notation, its print a key away.</summary>
    public bool HasNotes { get; init; }

    /// <summary>Notation, or a PDF whose notes are read: it plays along, and its level is graded from its notes.</summary>
    public bool PlaysAlong => !IsPdf || HasNotes;
}

/// <summary>
/// A PDF's reading into notation, as JSON (Transcription's <c>ReadingDraft</c>): waiting on its piece to be reviewed, or kept as its
/// notation, and when.
/// </summary>
public sealed record PieceReading(string Piece, string Result, DateTimeOffset Saved, DateTimeOffset? Kept);

/// <summary>An excerpt of a piece for the sight-reading track: its bars, level, and when it was last read.</summary>
public sealed record ExcerptRecord(long Id, string Piece, int FirstBar, int LastBar, int Level, double Within, int Used, DateOnly? LastUsed);

/// <summary>
/// A piece taken out by hand, as it was just before: every row of it (the piece with its music, its excerpts, its reading, and
/// what converting its PDF kept), by table, and the files a PDF kept beside the imports. Enough to put it back as it was (Undo).
/// </summary>
public sealed record RemovedPiece(PieceRecord Piece, IReadOnlyList<(string Table, IReadOnlyDictionary<string, object?> Row)> Rows, IReadOnlyList<(string Name, byte[] Data)> Files);

public sealed partial class PrimaStore
{
    private const string LibrarySchema = """
        CREATE TABLE IF NOT EXISTS Piece (
            Id TEXT PRIMARY KEY,
            Origin TEXT NOT NULL,
            Title TEXT NOT NULL,
            Composer TEXT NOT NULL,
            Work TEXT,
            Format TEXT NOT NULL,
            File TEXT,
            Midi TEXT,
            Mei TEXT,
            Level INTEGER,
            TypicalLevel INTEGER,
            Reasons TEXT,
            Bars INTEGER,
            Source TEXT,
            SourceUrl TEXT,
            Licence TEXT,
            LicenceUrl TEXT,
            Credit TEXT,
            Tier TEXT,
            Tempo REAL,
            Layout TEXT,
            Position TEXT,
            Added TEXT NOT NULL,
            Opened TEXT
        );
        CREATE TABLE IF NOT EXISTS Excerpt (
            Id INTEGER PRIMARY KEY,
            Piece TEXT NOT NULL REFERENCES Piece (Id) ON DELETE CASCADE,
            FirstBar INTEGER NOT NULL,
            LastBar INTEGER NOT NULL,
            Level INTEGER NOT NULL,
            Within REAL NOT NULL,
            Used INTEGER NOT NULL DEFAULT 0,
            LastUsed TEXT
        );
        CREATE INDEX IF NOT EXISTS ExcerptByLevel ON Excerpt (Level, LastUsed);
        CREATE TABLE IF NOT EXISTS PieceReading (
            Piece TEXT PRIMARY KEY REFERENCES Piece (Id) ON DELETE CASCADE,
            Result TEXT NOT NULL,
            Saved TEXT NOT NULL,
            Kept TEXT
        );
        """;

    private const string PieceColumns = "Id, Origin, Title, Composer, Work, Format, File, Midi, Level, TypicalLevel, Reasons, Bars, Source, SourceUrl, Licence, LicenceUrl, Credit, Tier, Tempo, Layout, Position, Added, Opened";

    // Whether a PDF has notes read from it. It's asked apart from the piece's own columns: a worked-out column has no type of
    // its own, so with no rows to take one from (a new data file, an id that isn't there) the row can't be read with it.
    private const string NotesRead = "Format = 'pdf' AND Mei IS NOT NULL";

    // Prima's own pieces kept out of the Library, which it doesn't put back by itself: all of them once Remove library took them
    // out (Settings › Your data), and one removed by hand under its id. Kept among the settings, so a copy and a restore carry
    // them, and Reset settings, which puts back only what's on the Settings screen, leaves them.
    private const string OwnPiecesOutKey = "Library.OwnPiecesOut";
    private const string RemovedKey = "Library.Removed.";
    // A collection's piece removed by hand, under its id: importing its collection again leaves it out. Not under RemovedKey,
    // whose keys the bundle's own refresh lets go of when the bundle no longer has their piece.
    private const string CollectionRemovedKey = "Library.CollectionRemoved.";

    // A piece's rows, table by table, the piece's own first: what a piece taken out takes with it, and Undo puts back.
    private static readonly (string Table, string Key)[] PieceTables =
        [("Piece", "Id"), ("Excerpt", "Piece"), ("PieceReading", "Piece"), ("ConversionSource", "Piece"), ("ConversionJob", "Piece"), ("NotationReviewSession", "Piece")];

    /// <summary>The folder imported files are kept in, beside the data file.</summary>
    public string FilesPath => Path.Combine(Path.GetDirectoryName(databasePath) ?? ".", "library");

    /// <summary>Every piece, without its music: for the Library's list.</summary>
    public Task<List<PieceRecord>> GetPiecesAsync() => RunAsync(async c =>
    {
        var read = (await c.QueryAsync<string>($"SELECT Id FROM Piece WHERE {NotesRead}")).ToHashSet();
        return (await c.QueryAsync<PieceRow>($"SELECT {PieceColumns} FROM Piece")).Select(r => r.ToRecord(read.Contains(r.Id))).ToList();
    });

    public Task<PieceRecord?> GetPieceAsync(string id) => RunAsync(async c =>
    {
        var row = await c.QuerySingleOrDefaultAsync<PieceRow>($"SELECT {PieceColumns} FROM Piece WHERE Id = @id", new { id });
        return row?.ToRecord(await c.ExecuteScalarAsync<long>($"SELECT COUNT(*) FROM Piece WHERE Id = @id AND {NotesRead}", new { id }) > 0);
    });

    /// <summary>
    /// A notation piece's MEI, or a PDF's read from it; null for a PDF not read. Without the hidden playback tempos (bare numbers)
    /// that a piece stored before the bundle left them out still has, and with the dynamics a research edition typed as lyrics as
    /// marks (<see cref="LyricDynamics"/>), as the Library tool makes them now: a collection's piece stored before that has them as
    /// words. Both away from the screen's thread.
    /// </summary>
    public async Task<string?> GetMeiAsync(string id)
    {
        var mei = await RunAsync(c => c.QuerySingleOrDefaultAsync<string?>("SELECT Mei FROM Piece WHERE Id = @id", new { id }));
        return mei is null ? null : await Task.Run(() => LyricDynamics.Mark(MeiReader.WithoutBareTempos(mei)));
    }

    /// <summary>The pieces already in the Library, by id: so a bundled piece is prepared once.</summary>
    public Task<HashSet<string>> GetPieceIdsAsync() => RunAsync(async c => (await c.QueryAsync<string>("SELECT Id FROM Piece")).ToHashSet());

    /// <summary>
    /// Saves a piece with its MEI (null for a PDF) and its excerpts, replacing any it had: false, and nothing saved, for one of
    /// Prima's own that's kept out of the Library (all of them after Remove library, or this one removed by hand), or a
    /// collection's removed by hand, so nothing that comes after puts it back (the bundle still being stored, its collection
    /// imported again, or a reading of it kept).
    /// </summary>
    public Task<bool> SavePieceAsync(PieceRecord piece, string? mei, IReadOnlyList<BundledExcerpt> excerpts) => RunAsync(async c =>
    {
        await using var transaction = (SqliteTransaction)await c.BeginTransactionAsync();
        if (piece.Origin == "bundle" && await c.ExecuteScalarAsync<long>("SELECT COUNT(*) FROM Setting WHERE Key IN (@all, @one)",
            new { all = OwnPiecesOutKey, one = RemovedKey + piece.Id }, transaction) > 0) return false;
        if (piece.Origin == "collection" && await c.ExecuteScalarAsync<long>("SELECT COUNT(*) FROM Setting WHERE Key = @one",
            new { one = CollectionRemovedKey + piece.Id }, transaction) > 0) return false;

        await c.ExecuteAsync($"""
            INSERT INTO Piece ({PieceColumns}, Mei) VALUES (@Id, @Origin, @Title, @Composer, @Work, @Format, @File, @Midi, @Level, @TypicalLevel, @Reasons, @Bars, @Source, @SourceUrl, @Licence, @LicenceUrl, @Credit, @Tier, @Tempo, @Layout, @Position, @Added, @Opened, @mei)
            ON CONFLICT (Id) DO UPDATE SET Title = excluded.Title, Composer = excluded.Composer, Work = excluded.Work, Format = excluded.Format, File = excluded.File, Midi = excluded.Midi,
                Level = excluded.Level, TypicalLevel = excluded.TypicalLevel, Reasons = excluded.Reasons, Bars = excluded.Bars, Source = excluded.Source, SourceUrl = excluded.SourceUrl,
                Licence = excluded.Licence, LicenceUrl = excluded.LicenceUrl, Credit = excluded.Credit, Tier = excluded.Tier, Mei = excluded.Mei
            """, PieceRow.From(piece, mei), transaction);
        await c.ExecuteAsync("DELETE FROM Excerpt WHERE Piece = @id", new { id = piece.Id }, transaction);
        foreach (var cut in excerpts)
        {
            await c.ExecuteAsync("INSERT INTO Excerpt (Piece, FirstBar, LastBar, Level, Within) VALUES (@piece, @first, @last, @level, @within)",
                new { piece = piece.Id, first = cut.FirstBar, last = cut.LastBar, level = cut.Level, within = cut.Within }, transaction);
        }
        await transaction.CommitAsync();
        return true;
    });

    /// <summary>
    /// The bundle's pieces, by the id and title each is kept under, against those already here, as one piece of work: their
    /// details from the bundle (a PDF's level comes with them, until its notes are read; a notation piece's is its own), and the bundled pieces it no
    /// longer has taken out with their excerpts. One removed by hand that the bundle no longer has isn't kept out any more: there's
    /// nothing of it to put back.
    /// </summary>
    public Task RefreshBundleAsync(IReadOnlyList<(string Id, string Title, BundledPiece Piece)> pieces) => RunAsync(async c =>
    {
        await using var transaction = (SqliteTransaction)await c.BeginTransactionAsync();
        var ids = pieces.Select(p => p.Id).ToList();
        await c.ExecuteAsync("DELETE FROM Excerpt WHERE Piece IN (SELECT Id FROM Piece WHERE Origin = 'bundle' AND Id NOT IN @ids)", new { ids }, transaction);
        await c.ExecuteAsync("DELETE FROM Piece WHERE Origin = 'bundle' AND Id NOT IN @ids", new { ids }, transaction);
        var removed = ids.Select(id => RemovedKey + id).ToList();
        await c.ExecuteAsync("DELETE FROM Setting WHERE substr(Key, 1, @length) = @prefix AND Key NOT IN @removed", new { length = RemovedKey.Length, prefix = RemovedKey, removed }, transaction);
        foreach (var (id, title, p) in pieces)
        {
            await c.ExecuteAsync("""
                UPDATE Piece SET Title = @title, Composer = @Composer, Work = @Work, Credit = @Credit, Licence = @Licence, LicenceUrl = @LicenceUrl, SourceUrl = @SourceUrl, Tier = @tier,
                    Level = CASE WHEN Format = 'pdf' AND Mei IS NULL THEN @pdfLevel ELSE Level END, TypicalLevel = CASE WHEN Format = 'pdf' AND Mei IS NULL THEN @pdfLevel ELSE TypicalLevel END
                WHERE Id = @id AND Origin = 'bundle'
                """, new { id, title, p.Composer, p.Work, p.Credit, p.Licence, p.LicenceUrl, p.SourceUrl, tier = p.Tier.ToString(), pdfLevel = p.Format == "pdf" ? p.Anchor : null }, transaction);
        }
        await transaction.CommitAsync();
    });

    /// <summary>
    /// A collection the Library tool built, as one of its zips is imported, against the collection's pieces already here, as one
    /// piece of work: each of its pieces here takes its details (title, composer, work, credit, licence and tier; its music and
    /// excerpts stay), and, when it names every piece of the whole collection (<paramref name="whole"/>), the collection's pieces
    /// it no longer has go, with their excerpts, readings and files. A piece that goes keeps its runs and practice sessions, as one
    /// removed by hand does: the practice days still count them, and they're its own again if a later collection brings it back.
    /// Bundled pieces and the owner's own imports are never touched. How many pieces' details changed, and how many went.
    /// </summary>
    public async Task<(int Changed, int Removed)> RefreshCollectionAsync(IReadOnlyList<(string Id, string Title, BundledPiece Piece)> pieces, IReadOnlyCollection<string>? whole)
    {
        var (changed, gone) = await RunAsync(async c =>
        {
            await using var transaction = (SqliteTransaction)await c.BeginTransactionAsync();
            var updated = 0;
            foreach (var (id, title, p) in pieces)
            {
                updated += await c.ExecuteAsync("""
                    UPDATE Piece SET Title = @title, Composer = @Composer, Work = @Work, Credit = @Credit, Licence = @Licence, LicenceUrl = @LicenceUrl, SourceUrl = @SourceUrl, Tier = @tier
                    WHERE Id = @id AND Origin = 'collection' AND (Title IS NOT @title OR Composer IS NOT @Composer OR Work IS NOT @Work OR Credit IS NOT @Credit
                        OR Licence IS NOT @Licence OR LicenceUrl IS NOT @LicenceUrl OR SourceUrl IS NOT @SourceUrl OR Tier IS NOT @tier)
                    """, new { id, title, p.Composer, p.Work, p.Credit, p.Licence, p.LicenceUrl, p.SourceUrl, tier = p.Tier.ToString() }, transaction);
            }

            List<(string Id, string Format, string? File, string? Midi)> removed = [];
            if (whole is not null)
            {
                var kept = whole.ToHashSet(StringComparer.Ordinal);
                removed = [.. (await c.QueryAsync<(string Id, string Format, string? File, string? Midi)>("SELECT Id, Format, File, Midi FROM Piece WHERE Origin = 'collection'", transaction: transaction))
                    .Where(p => !kept.Contains(p.Id))];
                foreach (var piece in removed)
                {
                    await c.ExecuteAsync("DELETE FROM Excerpt WHERE Piece = @id; DELETE FROM PieceReading WHERE Piece = @id; DELETE FROM Piece WHERE Id = @id", new { id = piece.Id }, transaction);
                }
            }
            await transaction.CommitAsync();
            return (updated, removed);
        });

        // A PDF's file and its MIDI, kept beside the imports, go with it.
        DeleteFiles(Beside(gone.Where(p => p.Format == "pdf").SelectMany(p => new[] { p.File, p.Midi })));
        return (changed, gone.Count);
    }

    public Task DeletePieceAsync(string id) => RunAsync(c => c.ExecuteAsync("DELETE FROM Excerpt WHERE Piece = @id; DELETE FROM PieceReading WHERE Piece = @id; DELETE FROM Piece WHERE Id = @id", new { id }));

    /// <summary>
    /// Takes a piece out of the Library by hand (its Remove from the Library…), with its excerpts and readings, and an import's or a
    /// collection's PDF and MIDI kept beside the data file. One of Prima's own stays out from then on: the Library doesn't put it
    /// back when it stores Prima's pieces again, until it's asked to put them all back. One of a collection's stays out too: its
    /// collection imported again leaves it out, until it's let back in. Its runs and practice sessions stay, as any piece's that
    /// goes do: the days still count them. What went, to put back with Undo; null when there was no such piece.
    /// </summary>
    public async Task<RemovedPiece?> RemovePieceAsync(string id)
    {
        if (await GetPieceAsync(id) is not { } record) return null;

        // A PDF's file and MIDI beside the imports, read before they go (a bundled piece's are in the bundle, and stay).
        var files = new List<(string Name, byte[] Data)>();
        if (record is { Origin: not "bundle", IsPdf: true })
        {
            foreach (var name in new[] { record.File, record.Midi }.OfType<string>())
            {
                var path = Path.Combine(FilesPath, name);
                if (File.Exists(path)) files.Add((name, await File.ReadAllBytesAsync(path)));
            }
        }

        var rows = await RunAsync(async c =>
        {
            await using var transaction = (SqliteTransaction)await c.BeginTransactionAsync();
            var kept = new List<(string Table, IReadOnlyDictionary<string, object?> Row)>();
            foreach (var (table, key) in PieceTables)
            {
                foreach (IDictionary<string, object?> row in await c.QueryAsync($"SELECT * FROM {table} WHERE {key} = @id", new { id }, transaction))
                {
                    kept.Add((table, new Dictionary<string, object?>(row)));
                }
            }
            var mark = record.Origin switch { "bundle" => RemovedKey, "collection" => CollectionRemovedKey, _ => null };
            if (mark is not null)
            {
                await c.ExecuteAsync("INSERT INTO Setting (Key, Value) VALUES (@key, @at) ON CONFLICT (Key) DO UPDATE SET Value = excluded.Value",
                    new { key = mark + id, at = Iso(DateTimeOffset.Now) }, transaction);
            }
            // A reading's systems and review, and its conversion's jobs, go with their piece (they're its own, by its id).
            await c.ExecuteAsync("DELETE FROM Excerpt WHERE Piece = @id; DELETE FROM PieceReading WHERE Piece = @id; DELETE FROM Piece WHERE Id = @id", new { id }, transaction);
            await transaction.CommitAsync();
            return kept;
        });
        DeleteFiles(Beside(files.Select(f => (string?)f.Name)));
        return new RemovedPiece(record, rows, files);
    }

    /// <summary>
    /// Puts a piece taken out by hand back as it was (Undo): every row it took with it, its files, and in the Library's lists again,
    /// one of Prima's own or a collection's no longer kept out. An excerpt whose number has been taken meanwhile takes another.
    /// </summary>
    public async Task PutPieceBackAsync(RemovedPiece removed)
    {
        if (removed.Files.Count > 0) Directory.CreateDirectory(FilesPath);
        foreach (var (name, data) in removed.Files) await File.WriteAllBytesAsync(Path.Combine(FilesPath, name), data);

        await RunAsync(async c =>
        {
            await using var transaction = (SqliteTransaction)await c.BeginTransactionAsync();
            foreach (var (table, row) in removed.Rows)
            {
                var values = new DynamicParameters(row);
                var put = await c.ExecuteAsync($"INSERT OR IGNORE INTO {table} ({string.Join(", ", row.Keys)}) VALUES ({string.Join(", ", row.Keys.Select(k => "@" + k))})", values, transaction);
                if (put == 0 && table == "Excerpt")
                {
                    var rest = row.Keys.Where(k => k != "Id").ToList();
                    await c.ExecuteAsync($"INSERT INTO Excerpt ({string.Join(", ", rest)}) VALUES ({string.Join(", ", rest.Select(k => "@" + k))})", values, transaction);
                }
            }
            await c.ExecuteAsync("DELETE FROM Setting WHERE Key IN (@own, @collection)",
                new { own = RemovedKey + removed.Piece.Id, collection = CollectionRemovedKey + removed.Piece.Id }, transaction);
            await transaction.CommitAsync();
        });
    }

    /// <summary>The collection's pieces removed by hand, by id: importing their collection again leaves them out.</summary>
    public Task<HashSet<string>> GetCollectionPiecesOutAsync() => RunAsync(async c =>
        (await c.QueryAsync<string>("SELECT Key FROM Setting WHERE substr(Key, 1, @length) = @prefix", new { length = CollectionRemovedKey.Length, prefix = CollectionRemovedKey }))
            .Select(k => k[CollectionRemovedKey.Length..]).ToHashSet());

    /// <summary>Lets the collection's pieces removed by hand back in: they come back the next time their collection is imported.</summary>
    public Task LetCollectionPiecesBackAsync() => RunAsync(c => c.ExecuteAsync("DELETE FROM Setting WHERE substr(Key, 1, @length) = @prefix",
        new { length = CollectionRemovedKey.Length, prefix = CollectionRemovedKey }));

    /// <summary>
    /// Takes every piece out of the Library (Settings › Your data › Remove library), Prima's own too, as one piece of work: their
    /// excerpts, readings and what reading a PDF made, and the files of those imported or from a collection. Prima's own are kept
    /// out from then on: the Library doesn't put them back by itself, as the bundle is stored or a new release brings more, until
    /// it's asked to (<see cref="PutOwnPiecesBackAsync"/>). The runs and practice sessions on them stay, so the days and the streak
    /// are as they were, and so does everything else. How many pieces went.
    /// </summary>
    public async Task<int> RemoveLibraryAsync()
    {
        var (count, files) = await RunAsync(async c =>
        {
            await using var transaction = (SqliteTransaction)await c.BeginTransactionAsync();
            var count = await c.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM Piece", transaction: transaction);
            var files = (await c.QueryAsync<(string? File, string? Midi)>("SELECT File, Midi FROM Piece WHERE Origin <> 'bundle' AND Format = 'pdf'", transaction: transaction))
                .SelectMany(f => new[] { f.File, f.Midi }).ToList();
            // A reading's systems and review, and its conversion's jobs, go with their piece (they're its own, by its id).
            await c.ExecuteAsync("DELETE FROM Excerpt; DELETE FROM PieceReading; DELETE FROM Piece", transaction: transaction);
            await c.ExecuteAsync("INSERT INTO Setting (Key, Value) VALUES (@key, @at) ON CONFLICT (Key) DO UPDATE SET Value = excluded.Value",
                new { key = OwnPiecesOutKey, at = Iso(DateTimeOffset.Now) }, transaction);
            await transaction.CommitAsync();
            return (count, files);
        });
        DeleteFiles(Beside(files));
        // What reading the PDFs made (the systems' pictures and engravings), which no piece left has any use for.
        var made = Path.ChangeExtension(databasePath, ".conversion");
        if (Directory.Exists(made)) DeleteFiles(Directory.GetFiles(made));
        return count;
    }

    /// <summary>
    /// Which of Prima's own pieces are kept out of the Library, which it doesn't put back by itself: all of them (after Remove
    /// library), and those removed by hand, by id.
    /// </summary>
    public Task<(bool All, HashSet<string> Removed)> GetOwnPiecesOutAsync() => RunAsync(async c =>
    {
        var keys = (await c.QueryAsync<string>("SELECT Key FROM Setting WHERE Key = @all OR substr(Key, 1, @length) = @prefix",
            new { all = OwnPiecesOutKey, length = RemovedKey.Length, prefix = RemovedKey })).ToList();
        return (keys.Contains(OwnPiecesOutKey), keys.Where(k => k != OwnPiecesOutKey).Select(k => k[RemovedKey.Length..]).ToHashSet());
    });

    /// <summary>Lets every one of Prima's own pieces back into the Library, those removed by hand too: the Library stores them again.</summary>
    public Task PutOwnPiecesBackAsync() => RunAsync(c => c.ExecuteAsync("DELETE FROM Setting WHERE Key = @all OR substr(Key, 1, @length) = @prefix",
        new { all = OwnPiecesOutKey, length = RemovedKey.Length, prefix = RemovedKey }));

    // Pieces' files in the Library's folder, by their names.
    private IEnumerable<string> Beside(IEnumerable<string?> names) => names.OfType<string>().Select(name => Path.Combine(FilesPath, name));

    // Files that go with what was taken out, each left where it is if it can't go: it takes up room, nothing more.
    private static void DeleteFiles(IEnumerable<string> paths)
    {
        foreach (var path in paths)
        {
            try
            {
                File.Delete(path);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
            }
        }
    }

    /// <summary>A PDF's reading: waiting to be reviewed, or kept; null when it has none.</summary>
    public Task<PieceReading?> GetReadingAsync(string piece) => RunAsync(async c => (await c.QuerySingleOrDefaultAsync<(string Piece, string Result, string Saved, string? Kept)?>(
        "SELECT Piece, Result, Saved, Kept FROM PieceReading WHERE Piece = @piece", new { piece })) is { } r
        ? new PieceReading(r.Piece, r.Result, DateTimeOffset.Parse(r.Saved, CultureInfo.InvariantCulture), r.Kept is null ? null : DateTimeOffset.Parse(r.Kept, CultureInfo.InvariantCulture))
        : null);

    /// <summary>Keeps a reading on its piece to be reviewed, in place of any it had.</summary>
    public Task SaveReadingAsync(string piece, string result) => RunAsync(c => c.ExecuteAsync(
        "INSERT INTO PieceReading (Piece, Result, Saved, Kept) VALUES (@piece, @result, @saved, NULL) ON CONFLICT (Piece) DO UPDATE SET Result = excluded.Result, Saved = excluded.Saved, Kept = NULL",
        new { piece, result, saved = Iso(DateTimeOffset.Now) }));

    /// <summary>Marks the piece's reading kept, now that its MEI is the piece's notation.</summary>
    public Task MarkReadingKeptAsync(string piece) => RunAsync(c => c.ExecuteAsync("UPDATE PieceReading SET Kept = @kept WHERE Piece = @piece", new { piece, kept = Iso(DateTimeOffset.Now) }));

    /// <summary>Throws a reading away. A kept one's notation stays with its piece.</summary>
    public Task DeleteReadingAsync(string piece) => RunAsync(c => c.ExecuteAsync("DELETE FROM PieceReading WHERE Piece = @piece", new { piece }));

    /// <summary>
    /// An uploaded piece under the title, the artist or composer and the arranger it's given (the arranger kept as its work,
    /// "arr. …"): what its file's name or header said is only where they start.
    /// </summary>
    public Task NamePieceAsync(string id, string title, string composer, string? work) => RunAsync(c => c.ExecuteAsync(
        "UPDATE Piece SET Title = @title, Composer = @composer, Work = @work WHERE Id = @id", new { id, title, composer, work }));

    /// <summary>
    /// A piece's pages: the PDF kept beside the data file under <paramref name="file"/>. Its notes stay; how its pages were laid
    /// out doesn't, being the pages' before.
    /// </summary>
    public Task SetPagesAsync(string id, string file) => RunAsync(c => c.ExecuteAsync(
        "UPDATE Piece SET Format = 'pdf', File = @file, Layout = NULL WHERE Id = @id", new { id, file }));

    /// <summary>What you set for a piece: its tempo, its PDF layout, where you were. Null leaves a value as it is.</summary>
    public Task SetPieceAsync(string id, double? tempo = null, string? layout = null, string? position = null) => RunAsync(c => c.ExecuteAsync(
        "UPDATE Piece SET Tempo = COALESCE(@tempo, Tempo), Layout = COALESCE(@layout, Layout), Position = COALESCE(@position, Position), Opened = @opened WHERE Id = @id",
        new { id, tempo, layout, position, opened = Iso(DateTimeOffset.Now) }));

    /// <summary>
    /// Excerpts a reader at <paramref name="level"/> can be given: at or below it (and at least level 4, since levels 1–3 are the
    /// generator's), of about the passage length for the level, and not read in the last <paramref name="restDays"/> days, least
    /// used and hardest first.
    /// </summary>
    public Task<List<ExcerptRecord>> GetExcerptsAsync(int level, int bars, DateOnly today, int restDays = 21) => RunAsync(async c => (await c.QueryAsync<(long Id, string Piece, long FirstBar, long LastBar, long Level, double Within, long Used, string? LastUsed)>(
        "SELECT Id, Piece, FirstBar, LastBar, Level, Within, Used, LastUsed FROM Excerpt WHERE Level <= @level AND Level >= 4 AND (LastUsed IS NULL OR LastUsed < @rested) AND (LastBar - MAX(FirstBar, 1) + 1) = @bars ORDER BY Used, Level DESC, Within DESC, Id",
        new { level, bars, rested = Iso(today.AddDays(-restDays)) }))
        .Select(r => new ExcerptRecord(r.Id, r.Piece, (int)r.FirstBar, (int)r.LastBar, (int)r.Level, r.Within, (int)r.Used, r.LastUsed is null ? null : DateOnly.Parse(r.LastUsed, CultureInfo.InvariantCulture))).ToList());

    public Task<ExcerptRecord?> GetExcerptAsync(long id) => RunAsync(async c => (await c.QuerySingleOrDefaultAsync<(long Id, string Piece, long FirstBar, long LastBar, long Level, double Within, long Used, string? LastUsed)?>(
        "SELECT Id, Piece, FirstBar, LastBar, Level, Within, Used, LastUsed FROM Excerpt WHERE Id = @id", new { id })) is { } r
        ? new ExcerptRecord(r.Id, r.Piece, (int)r.FirstBar, (int)r.LastBar, (int)r.Level, r.Within, (int)r.Used, r.LastUsed is null ? null : DateOnly.Parse(r.LastUsed, CultureInfo.InvariantCulture)) : null);

    public Task MarkExcerptReadAsync(long id, DateOnly day) => RunAsync(c => c.ExecuteAsync("UPDATE Excerpt SET Used = Used + 1, LastUsed = @day WHERE Id = @id", new { id, day = Iso(day) }));

    // Dapper's view of a row: dates as text.
    private sealed record PieceRow(string Id, string Origin, string Title, string Composer, string? Work, string Format, string? File, string? Midi, long? Level, long? TypicalLevel, string? Reasons, long? Bars,
        string? Source, string? SourceUrl, string? Licence, string? LicenceUrl, string? Credit, string? Tier, double? Tempo, string? Layout, string? Position, string Added, string? Opened)
    {
        public PieceRecord ToRecord(bool hasNotes) => new(Id, Origin, Title, Composer, Work, Format, File, Midi, (int?)Level, (int?)TypicalLevel, Reasons, (int?)Bars, Source, SourceUrl, Licence, LicenceUrl, Credit, Tier, Tempo, Layout, Position,
            DateTimeOffset.Parse(Added, CultureInfo.InvariantCulture), Opened is null ? null : DateTimeOffset.Parse(Opened, CultureInfo.InvariantCulture)) { HasNotes = hasNotes };

        public static object From(PieceRecord p, string? mei) => new
        {
            p.Id, p.Origin, p.Title, p.Composer, p.Work, p.Format, p.File, p.Midi, p.Level, p.TypicalLevel, p.Reasons, p.Bars, p.Source, p.SourceUrl, p.Licence, p.LicenceUrl, p.Credit, p.Tier, p.Tempo, p.Layout, p.Position,
            Added = Iso(p.Added), Opened = p.Opened is { } o ? Iso(o) : null, mei,
        };
    }
}
