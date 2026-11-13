using System.Globalization;
using System.Text.Json;
using Dapper;
using Microsoft.Data.Sqlite;
using Prima.Core.Music;
using Prima.Core.Practice;

namespace Prima.UI.Storage;

/// <summary>
/// One attempt: a passage, a rhythm, a technique run or a drill set. It keeps its settings, the seed (or piece and bars), the notes
/// played and the beat times (both in ms from beat 0), and the measures worked out from them, so a result can be worked out again
/// if the scoring changes.
/// </summary>
public sealed record AttemptRecord(long Id, DateTimeOffset At, DateOnly Day, string Kind, string? Variant, string? SetId, int? Level, double? Tempo, long? Seed, string? Settings, string Played, string? Beats, string Measures, double? Headline, bool Clean, bool? Looked);

/// <summary>
/// How often something has been done: its attempts' kind, what was played where they're counted for each thing (an exercise's
/// id), the times, the days, the last day, and the fastest tempo of a clean run In time.
/// </summary>
public sealed record DoneRecord(string Kind, string? SetId, int Times, int Days, DateOnly Last, double? CleanTempo);

/// <summary>A practised day: its minutes, the plan made for it, the steps done, and the five-minute plan.</summary>
public sealed record DayRecord(DateOnly Day, double Minutes, string? Plan, string StepsDone, bool ShortDone);

/// <summary>
/// A practice session on a piece: when it started, its clock as last kept (playing and idle time, whether it's paused, when
/// something was last played), the way it was practised ("simple", "standard", "hands-on", or "custom" for a mix of their
/// settings), and when it finished, if it has.
/// </summary>
public sealed record PracticeSession(long Id, string Piece, DateTimeOffset Started, TimeSpan Playing, TimeSpan Idle, bool Paused, DateTimeOffset LastPlayed, string Way, DateTimeOffset? Finished);

public sealed partial class PrimaStore
{
    private const string PracticeSchema = """
        CREATE TABLE IF NOT EXISTS Track (
            Name TEXT PRIMARY KEY,
            State TEXT NOT NULL,
            UpdatedAt TEXT NOT NULL
        );
        CREATE TABLE IF NOT EXISTS NoteCard (
            Clef INTEGER NOT NULL,
            Midi INTEGER NOT NULL,
            Box INTEGER NOT NULL,
            Due TEXT NOT NULL,
            Introduced TEXT NOT NULL,
            LastSeen TEXT,
            PRIMARY KEY (Clef, Midi)
        );
        CREATE TABLE IF NOT EXISTS NoteAnswer (
            Id INTEGER PRIMARY KEY,
            At TEXT NOT NULL,
            Clef INTEGER NOT NULL,
            Midi INTEGER NOT NULL,
            Played INTEGER,
            Right INTEGER NOT NULL,
            ResponseMs REAL NOT NULL,
            Drill TEXT NOT NULL
        );
        CREATE TABLE IF NOT EXISTS Attempt (
            Id INTEGER PRIMARY KEY,
            At TEXT NOT NULL,
            Day TEXT NOT NULL,
            Kind TEXT NOT NULL,
            Variant TEXT,
            SetId TEXT,
            Level INTEGER,
            Tempo REAL,
            Seed INTEGER,
            Settings TEXT,
            Played TEXT NOT NULL,
            Beats TEXT,
            Measures TEXT NOT NULL,
            Headline REAL,
            Clean INTEGER NOT NULL,
            Looked INTEGER
        );
        CREATE INDEX IF NOT EXISTS AttemptByDay ON Attempt (Day, Kind);
        CREATE TABLE IF NOT EXISTS PracticeDay (
            Day TEXT PRIMARY KEY,
            Minutes REAL NOT NULL DEFAULT 0,
            Plan TEXT,
            StepsDone TEXT NOT NULL DEFAULT '',
            ShortDone INTEGER NOT NULL DEFAULT 0
        );
        CREATE TABLE IF NOT EXISTS PracticeSession (
            Id INTEGER PRIMARY KEY,
            Piece TEXT NOT NULL,
            Started TEXT NOT NULL,
            Playing REAL NOT NULL DEFAULT 0,
            Idle REAL NOT NULL DEFAULT 0,
            Paused INTEGER NOT NULL DEFAULT 0,
            LastPlayed TEXT NOT NULL,
            Way TEXT NOT NULL,
            Finished TEXT
        );
        CREATE INDEX IF NOT EXISTS SessionByPiece ON PracticeSession (Piece, Started);
        """;

    /// <summary>Tuples and records with fields, as JSON.</summary>
    public static readonly JsonSerializerOptions Json = new() { IncludeFields = true };

    private static string Iso(DateOnly day) => day.ToString("O", CultureInfo.InvariantCulture);

    private static string Iso(DateTimeOffset at) => at.ToString("O", CultureInfo.InvariantCulture);

    /// <summary>A track's state (reading, rhythm, each drill, each scale and arpeggio), or null if it hasn't started.</summary>
    public Task<T?> GetTrackAsync<T>(string name) => RunAsync(async c =>
    {
        var json = await c.QuerySingleOrDefaultAsync<string>("SELECT State FROM Track WHERE Name = @name", new { name });
        return json is null ? default : JsonSerializer.Deserialize<T>(json, Json);
    });

    public Task<Dictionary<string, string>> GetTracksAsync(string prefix) => RunAsync(async c =>
        (await c.QueryAsync<(string Name, string State)>("SELECT Name, State FROM Track WHERE Name LIKE @like", new { like = prefix + "%" })).ToDictionary(t => t.Name, t => t.State));

    public Task SaveTrackAsync<T>(string name, T state) => RunAsync(c => c.ExecuteAsync(
        "INSERT INTO Track (Name, State, UpdatedAt) VALUES (@name, @state, @at) ON CONFLICT (Name) DO UPDATE SET State = excluded.State, UpdatedAt = excluded.UpdatedAt",
        new { name, state = JsonSerializer.Serialize(state, Json), at = Iso(DateTimeOffset.Now) }));

    public Task<List<NoteCard>> GetCardsAsync() => RunAsync(async c => (await c.QueryAsync<(long Clef, long Midi, long Box, string Due, string Introduced, string? LastSeen)>("SELECT Clef, Midi, Box, Due, Introduced, LastSeen FROM NoteCard"))
        .Select(r => new NoteCard((Clef)r.Clef, (int)r.Midi, (int)r.Box, DateOnly.Parse(r.Due, CultureInfo.InvariantCulture), DateOnly.Parse(r.Introduced, CultureInfo.InvariantCulture), r.LastSeen is null ? null : DateOnly.Parse(r.LastSeen, CultureInfo.InvariantCulture))).ToList());

    /// <summary>Saves a card and its answer, as one piece of work.</summary>
    public Task SaveAnswerAsync(NoteCard card, NoteAnswer answer, int? played, string drill) => RunAsync(async c =>
    {
        await using var transaction = (SqliteTransaction)await c.BeginTransactionAsync();
        await c.ExecuteAsync("INSERT INTO NoteCard (Clef, Midi, Box, Due, Introduced, LastSeen) VALUES (@Clef, @Midi, @Box, @Due, @Introduced, @LastSeen) ON CONFLICT (Clef, Midi) DO UPDATE SET Box = excluded.Box, Due = excluded.Due, LastSeen = excluded.LastSeen",
            new { Clef = (int)card.Clef, card.Midi, card.Box, Due = Iso(card.Due), Introduced = Iso(card.Introduced), LastSeen = card.LastSeen is { } seen ? Iso(seen) : null }, transaction);
        await c.ExecuteAsync("INSERT INTO NoteAnswer (At, Clef, Midi, Played, Right, ResponseMs, Drill) VALUES (@At, @Clef, @Midi, @played, @Right, @ResponseMs, @drill)",
            new { At = Iso(answer.At), Clef = (int)answer.Clef, answer.Midi, played, Right = answer.Right ? 1 : 0, answer.ResponseMs, drill }, transaction);
        await transaction.CommitAsync();
    });

    public Task<List<NoteAnswer>> GetAnswersAsync(DateTimeOffset since) => RunAsync(async c => (await c.QueryAsync<(string At, long Clef, long Midi, long Right, double ResponseMs)>("SELECT At, Clef, Midi, Right, ResponseMs FROM NoteAnswer WHERE At >= @since", new { since = Iso(since) }))
        .Select(r => new NoteAnswer(DateTimeOffset.Parse(r.At, CultureInfo.InvariantCulture), (Clef)r.Clef, (int)r.Midi, r.Right == 1, r.ResponseMs)).ToList());

    public Task<long> AddAttemptAsync(AttemptRecord a) => RunAsync(c => c.ExecuteScalarAsync<long>(
        "INSERT INTO Attempt (At, Day, Kind, Variant, SetId, Level, Tempo, Seed, Settings, Played, Beats, Measures, Headline, Clean, Looked) VALUES (@At, @Day, @Kind, @Variant, @SetId, @Level, @Tempo, @Seed, @Settings, @Played, @Beats, @Measures, @Headline, @Clean, @Looked); SELECT last_insert_rowid();",
        new { At = Iso(a.At), Day = Iso(a.Day), a.Kind, a.Variant, a.SetId, a.Level, a.Tempo, a.Seed, a.Settings, a.Played, a.Beats, a.Measures, a.Headline, Clean = a.Clean ? 1 : 0, Looked = a.Looked is null ? (int?)null : a.Looked.Value ? 1 : 0 }));

    /// <summary>Attempts since a day, of the given kinds (all kinds if none given), oldest first.</summary>
    public Task<List<AttemptRecord>> GetAttemptsAsync(DateOnly since, params string[] kinds) => RunAsync(async c =>
    {
        var rows = await c.QueryAsync<(long Id, string At, string Day, string Kind, string? Variant, string? SetId, long? Level, double? Tempo, long? Seed, string? Settings, string Played, string? Beats, string Measures, double? Headline, long Clean, long? Looked)>(
            "SELECT Id, At, Day, Kind, Variant, SetId, Level, Tempo, Seed, Settings, Played, Beats, Measures, Headline, Clean, Looked FROM Attempt WHERE Day >= @since ORDER BY At", new { since = Iso(since) });
        return rows.Where(r => kinds.Length == 0 || kinds.Contains(r.Kind)).Select(r => new AttemptRecord(r.Id, DateTimeOffset.Parse(r.At, CultureInfo.InvariantCulture), DateOnly.Parse(r.Day, CultureInfo.InvariantCulture), r.Kind, r.Variant, r.SetId, (int?)r.Level, r.Tempo, r.Seed, r.Settings, r.Played, r.Beats, r.Measures, r.Headline, r.Clean == 1, r.Looked is null ? null : r.Looked == 1)).ToList();
    });

    /// <summary>
    /// The runs kept on pieces, oldest first, each with its piece: one piece's, or every piece's. Read without the keys played,
    /// which what's shown of a piece's history has no use for.
    /// </summary>
    public Task<List<(string Piece, PieceRun Run)>> GetPieceRunsAsync(string? piece = null) => RunAsync(async c =>
    {
        var rows = await c.QueryAsync<(long Id, string SetId, string At, string Day, string? Variant, double? Tempo, string Measures)>(
            "SELECT Id, SetId, At, Day, Variant, Tempo, Measures FROM Attempt WHERE Kind = 'piece' AND SetId IS NOT NULL AND (@piece IS NULL OR SetId = @piece) ORDER BY At", new { piece });
        return rows.Select(r =>
        {
            // The measures as a run kept them: In time's or Wait for me's, with where it started and whether it was stopped.
            using var kept = JsonDocument.Parse(r.Measures);
            var measures = kept.RootElement;
            int Count(string name) => measures.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Number ? value.GetInt32() : 0;
            var inTime = r.Variant == "in time";
            var right = inTime ? Count("right") : Count("rightFirstTime");
            var notes = inTime ? right + Count("wrong") + Count("missed") : Count("notes");
            var hands = measures.TryGetProperty("hands", out var hand) ? hand.GetString() ?? "both" : "both";
            var from = measures.TryGetProperty("from", out _) ? Count("from") : 1;
            var stopped = measures.TryGetProperty("stopped", out var early) && early.ValueKind == JsonValueKind.True;
            var share = inTime && measures.TryGetProperty("inTime", out var accuracy) && accuracy.ValueKind == JsonValueKind.Number ? accuracy.GetDouble() : (double?)null;
            var bar = measures.TryGetProperty("bar", out var at) && at.ValueKind == JsonValueKind.Number ? at.GetInt32() : (int?)null;
            // Round a loop: its last bar, and each pass played to its end, with what went wrong in each of its bars.
            var to = measures.TryGetProperty("to", out var last) && last.ValueKind == JsonValueKind.Number ? last.GetInt32() : (int?)null;
            List<PiecePass> passes = measures.TryGetProperty("passes", out var each) && each.ValueKind == JsonValueKind.Array
                ? [.. each.EnumerateArray().Select(p => new PiecePass(p.GetProperty("right").GetInt32(), p.GetProperty("notes").GetInt32(), p.TryGetProperty("inTime", out var timed) && timed.ValueKind == JsonValueKind.Number ? timed.GetDouble() : null) { Trouble = TroubleOf(p) })]
                : [];
            var session = measures.TryGetProperty("session", out var of) && of.ValueKind == JsonValueKind.Number ? of.GetInt64() : (long?)null;
            var play = measures.TryGetProperty("play", out var through) && through.ValueKind == JsonValueKind.True;
            var day = DateOnly.Parse(r.Day, CultureInfo.InvariantCulture);
            var run = new PieceRun(DateTimeOffset.Parse(r.At, CultureInfo.InvariantCulture), day, inTime, r.Tempo, hands, from, stopped, bar, right, notes, share)
            {
                Id = r.Id, Session = session, Play = play, Trouble = TroubleOf(measures), To = to, Passes = passes,
            };
            return (r.SetId, run);
        }).ToList();
    });

    // What went wrong in each bar of a run or a pass, as it was kept: [bar, wrong, missed, off the beat, let go early, slips put
    // right, chord notes not together] for each (the last two kept since 13 November).
    private static List<BarTrouble> TroubleOf(JsonElement kept) => kept.TryGetProperty("trouble", out var bars) && bars.ValueKind == JsonValueKind.Array
        ? [.. bars.EnumerateArray().Select(b => new BarTrouble(b[0].GetInt32(), b[1].GetInt32(), b[2].GetInt32(), b[3].GetInt32(), b[4].GetInt32(),
            b.GetArrayLength() > 5 ? b[5].GetInt32() : 0, b.GetArrayLength() > 6 ? b[6].GetInt32() : 0))]
        : [];

    /// <summary>The marks a run left on the music, as it kept them, for each run of those asked for that kept them.</summary>
    public Task<Dictionary<long, string>> GetRunMarksAsync(IReadOnlyCollection<long> runs) => RunAsync(async c =>
    {
        var rows = await c.QueryAsync<(long Id, string Measures)>("SELECT Id, Measures FROM Attempt WHERE Id IN @runs", new { runs });
        var marks = new Dictionary<long, string>();
        foreach (var (id, measures) in rows)
        {
            using var kept = JsonDocument.Parse(measures);
            if (kept.RootElement.TryGetProperty("marks", out var left)) marks[id] = left.GetRawText();
        }
        return marks;
    });

    /// <summary>Starts a practice session on a piece: its id.</summary>
    public Task<long> StartSessionAsync(string piece, string way, DateTimeOffset at) => RunAsync(c => c.ExecuteScalarAsync<long>(
        "INSERT INTO PracticeSession (Piece, Started, LastPlayed, Way) VALUES (@piece, @at, @at, @way); SELECT last_insert_rowid();", new { piece, at = Iso(at), way }));

    /// <summary>
    /// Keeps a session's clock as it stands, and whether and when it finished. A finished session stays as it was finished: only
    /// carrying it on (<paramref name="reopen"/>) opens it again, so a save that comes late (a clock's tick as it finishes) can't.
    /// </summary>
    public Task SaveSessionAsync(PracticeSession s, bool reopen = false) => RunAsync(c => c.ExecuteAsync(
        "UPDATE PracticeSession SET Playing = @playing, Idle = @idle, Paused = @paused, LastPlayed = @lastPlayed, Way = @Way, Finished = @finished WHERE Id = @Id AND (Finished IS NULL OR @reopen)",
        new { s.Id, playing = s.Playing.TotalSeconds, idle = s.Idle.TotalSeconds, paused = s.Paused ? 1 : 0, lastPlayed = Iso(s.LastPlayed), s.Way, finished = s.Finished is { } at ? Iso(at) : null, reopen }));

    /// <summary>A piece's sessions, oldest first.</summary>
    public Task<List<PracticeSession>> GetSessionsAsync(string piece) => RunAsync(async c =>
        (await c.QueryAsync<(long Id, string Piece, string Started, double Playing, double Idle, long Paused, string LastPlayed, string Way, string? Finished)>(
            "SELECT Id, Piece, Started, Playing, Idle, Paused, LastPlayed, Way, Finished FROM PracticeSession WHERE Piece = @piece ORDER BY Started", new { piece }))
        .Select(r => new PracticeSession(r.Id, r.Piece, DateTimeOffset.Parse(r.Started, CultureInfo.InvariantCulture), TimeSpan.FromSeconds(r.Playing), TimeSpan.FromSeconds(r.Idle), r.Paused == 1,
            DateTimeOffset.Parse(r.LastPlayed, CultureInfo.InvariantCulture), r.Way, r.Finished is null ? null : DateTimeOffset.Parse(r.Finished, CultureInfo.InvariantCulture))).ToList());

    /// <summary>
    /// What's been done: the attempts of one kind counted for each thing played (each exercise), and those of the other kinds
    /// given each counted whole (a drill, whichever scale was played in it).
    /// </summary>
    public Task<List<DoneRecord>> GetDoneAsync(string each, params string[] whole) => RunAsync(async c =>
        (await c.QueryAsync<(string Kind, string? SetId, long Times, long Days, string Last, double? CleanTempo)>(
            "SELECT Kind, CASE WHEN Kind = @each THEN SetId END AS Played, COUNT(*), COUNT(DISTINCT Day), MAX(Day), MAX(CASE WHEN Clean = 1 AND Variant = 'in time' THEN Tempo END) FROM Attempt WHERE Kind = @each OR Kind IN @whole GROUP BY Kind, Played", new { each, whole }))
        .Select(r => new DoneRecord(r.Kind, r.SetId, (int)r.Times, (int)r.Days, DateOnly.Parse(r.Last, CultureInfo.InvariantCulture), r.CleanTempo)).ToList());

    public Task<List<DayRecord>> GetDaysAsync(DateOnly since) => RunAsync(async c => (await c.QueryAsync<(string Day, double Minutes, string? Plan, string StepsDone, long ShortDone)>("SELECT Day, Minutes, Plan, StepsDone, ShortDone FROM PracticeDay WHERE Day >= @since ORDER BY Day", new { since = Iso(since) }))
        .Select(r => new DayRecord(DateOnly.Parse(r.Day, CultureInfo.InvariantCulture), r.Minutes, r.Plan, r.StepsDone, r.ShortDone == 1)).ToList());

    /// <summary>Adds practice time to a day.</summary>
    public Task AddMinutesAsync(DateOnly day, double minutes) => RunAsync(c => c.ExecuteAsync(
        "INSERT INTO PracticeDay (Day, Minutes) VALUES (@day, @minutes) ON CONFLICT (Day) DO UPDATE SET Minutes = Minutes + excluded.Minutes", new { day = Iso(day), minutes }));

    /// <summary>Keeps the day's plan, made once a day.</summary>
    public Task SavePlanAsync(DateOnly day, string plan) => RunAsync(c => c.ExecuteAsync(
        "INSERT INTO PracticeDay (Day, Plan) VALUES (@day, @plan) ON CONFLICT (Day) DO UPDATE SET Plan = excluded.Plan", new { day = Iso(day), plan }));

    /// <summary>Marks a step of the day's plan done, and the five-minute plan when both its steps are.</summary>
    public Task MarkStepAsync(DateOnly day, StepKind step, bool shortPlanDone = false) => RunAsync(c => c.ExecuteAsync(
        "INSERT INTO PracticeDay (Day, StepsDone, ShortDone) VALUES (@day, @step, @done) ON CONFLICT (Day) DO UPDATE SET StepsDone = CASE WHEN instr(StepsDone, @step) > 0 THEN StepsDone ELSE StepsDone || @step END, ShortDone = MAX(ShortDone, excluded.ShortDone)",
        new { day = Iso(day), step = step + ";", done = shortPlanDone ? 1 : 0 }));

    /// <summary>Deletes everything practised, keeping the settings and the Library's pieces: "start over".</summary>
    public Task StartOverAsync() => RunAsync(c => c.ExecuteAsync("DELETE FROM RoutineAttempt; DELETE FROM RoutineExecution; DELETE FROM RoutineDay; DELETE FROM RoutineDefinition; DELETE FROM RoutineGeneration; DELETE FROM Setting WHERE Key = 'Routine.ActiveRevision'; DELETE FROM Track; DELETE FROM NoteCard; DELETE FROM NoteAnswer; DELETE FROM Attempt; DELETE FROM PracticeDay; DELETE FROM PracticeSession; UPDATE Excerpt SET Used = 0, LastUsed = NULL;"));

    /// <summary>The database file, for backing up.</summary>
    public string DatabasePath => databasePath;

    /// <summary>Waits for every piece of work started so far, so the file is complete (before a backup).</summary>
    public Task FlushAsync() => RunAsync(_ => Task.CompletedTask);

    /// <summary>
    /// Puts a backup in place of the data, after checking it's one of Prima's: false, and nothing changed, when it isn't. Runs on the
    /// lane between pieces of work, with the pooled connections closed so the file can be replaced.
    /// </summary>
    public async Task<bool> RestoreAsync(byte[] backup)
    {
        var header = "SQLite format 3\0"u8;
        if (backup.Length < 100 || !backup.AsSpan(0, header.Length).SequenceEqual(header)) return false;

        var candidate = databasePath + ".restore";
        await File.WriteAllBytesAsync(candidate, backup);
        try
        {
            await using (var check = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = candidate, Mode = SqliteOpenMode.ReadOnly, Pooling = false }.ToString()))
            {
                await check.OpenAsync();
                var tables = (await check.QueryAsync<string>("SELECT name FROM sqlite_master WHERE type = 'table'")).ToHashSet();
                if (!tables.IsSupersetOf(["Setting", "Track", "Attempt", "PracticeDay"])) return false;
            }

            // Its own place on the lane, without a connection: after everything before it, and before anything after.
            Task replace;
            lock (laneGate)
            {
                replace = lane.ContinueWith(_ =>
                {
                    SqliteConnection.ClearAllPools();
                    File.Copy(candidate, databasePath, overwrite: true);
                    schemaReady = false;
                }, CancellationToken.None, TaskContinuationOptions.DenyChildAttach, TaskScheduler.Default);
                lane = replace;
            }
            await replace;
            return true;
        }
        catch (SqliteException)
        {
            return false;
        }
        finally
        {
            File.Delete(candidate);
        }
    }
}
