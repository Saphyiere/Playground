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
    /// that a piece stored before the bundle left them out still has.
    /// </summary>
    public async Task<string?> GetMeiAsync(string id)
    {
        var mei = await RunAsync(c => c.QuerySingleOrDefaultAsync<string?>("SELECT Mei FROM Piece WHERE Id = @id", new { id }));
        return mei is null ? null : MeiReader.WithoutBareTempos(mei);
    }

    /// <summary>The pieces already in the Library, by id: so a bundled piece is prepared once.</summary>
    public Task<HashSet<string>> GetPieceIdsAsync() => RunAsync(async c => (await c.QueryAsync<string>("SELECT Id FROM Piece")).ToHashSet());

    /// <summary>Saves a piece with its MEI (null for a PDF) and its excerpts, replacing any it had.</summary>
    public Task SavePieceAsync(PieceRecord piece, string? mei, IReadOnlyList<BundledExcerpt> excerpts) => RunAsync(async c =>
    {
        await using var transaction = (SqliteTransaction)await c.BeginTransactionAsync();
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
    });

    /// <summary>
    /// The bundle's pieces, by the id and title each is kept under, against those already here, as one piece of work: their
    /// details from the bundle (a PDF's level comes with them, until its notes are read; a notation piece's is its own), and the bundled pieces it no
    /// longer has taken out with their excerpts.
    /// </summary>
    public Task RefreshBundleAsync(IReadOnlyList<(string Id, string Title, BundledPiece Piece)> pieces) => RunAsync(async c =>
    {
        await using var transaction = (SqliteTransaction)await c.BeginTransactionAsync();
        var ids = pieces.Select(p => p.Id).ToList();
        await c.ExecuteAsync("DELETE FROM Excerpt WHERE Piece IN (SELECT Id FROM Piece WHERE Origin = 'bundle' AND Id NOT IN @ids)", new { ids }, transaction);
        await c.ExecuteAsync("DELETE FROM Piece WHERE Origin = 'bundle' AND Id NOT IN @ids", new { ids }, transaction);
        foreach (var (id, title, p) in pieces)
        {
            await c.ExecuteAsync("""
                UPDATE Piece SET Title = @title, Composer = @Composer, Work = @Work, Credit = @Credit, Licence = @Licence, LicenceUrl = @LicenceUrl, SourceUrl = @SourceUrl,
                    Level = CASE WHEN Format = 'pdf' AND Mei IS NULL THEN @pdfLevel ELSE Level END, TypicalLevel = CASE WHEN Format = 'pdf' AND Mei IS NULL THEN @pdfLevel ELSE TypicalLevel END
                WHERE Id = @id AND Origin = 'bundle'
                """, new { id, title, p.Composer, p.Work, p.Credit, p.Licence, p.LicenceUrl, p.SourceUrl, pdfLevel = p.Format == "pdf" ? p.Anchor : null }, transaction);
        }
        await transaction.CommitAsync();
    });

    public Task DeletePieceAsync(string id) => RunAsync(c => c.ExecuteAsync("DELETE FROM Excerpt WHERE Piece = @id; DELETE FROM PieceReading WHERE Piece = @id; DELETE FROM Piece WHERE Id = @id", new { id }));

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
