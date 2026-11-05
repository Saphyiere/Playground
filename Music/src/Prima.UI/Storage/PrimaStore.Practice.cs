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

/// <summary>A practised day: its minutes, the plan made for it, the steps done, and the five-minute plan.</summary>
public sealed record DayRecord(DateOnly Day, double Minutes, string? Plan, string StepsDone, bool ShortDone);

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

    /// <summary>Deletes everything practised, keeping the settings: "start over".</summary>
    public Task StartOverAsync() => RunAsync(c => c.ExecuteAsync("DELETE FROM Track; DELETE FROM NoteCard; DELETE FROM NoteAnswer; DELETE FROM Attempt; DELETE FROM PracticeDay;"));

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
