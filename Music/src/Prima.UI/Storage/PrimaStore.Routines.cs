using System.Text.Json;
using Dapper;
using Microsoft.Data.Sqlite;
using Prima.Core.Routines;
using Prima.UI.Routines;

namespace Prima.UI.Storage;

public sealed partial class PrimaStore
{
    private const string RoutineSchema = """
        CREATE TABLE IF NOT EXISTS RoutineGeneration (Id TEXT PRIMARY KEY, Revision INTEGER NOT NULL, State INTEGER NOT NULL, Updated TEXT NOT NULL, Data TEXT NOT NULL);
        CREATE TABLE IF NOT EXISTS RoutineDefinition (Revision TEXT PRIMARY KEY, Accepted TEXT NOT NULL, Data TEXT NOT NULL);
        CREATE TABLE IF NOT EXISTS RoutineDay (Id TEXT PRIMARY KEY, Revision TEXT NOT NULL REFERENCES RoutineDefinition(Revision), Day TEXT NOT NULL, Data TEXT NOT NULL, UNIQUE(Revision, Day));
        CREATE TABLE IF NOT EXISTS RoutineExecution (Id TEXT PRIMARY KEY, DayId TEXT NOT NULL REFERENCES RoutineDay(Id), ItemId TEXT NOT NULL, Data TEXT NOT NULL, UNIQUE(DayId, ItemId));
        CREATE TABLE IF NOT EXISTS RoutineAttempt (Submission TEXT PRIMARY KEY, Execution TEXT NOT NULL REFERENCES RoutineExecution(Id), Attempt INTEGER NOT NULL REFERENCES Attempt(Id) ON DELETE CASCADE, UNIQUE(Attempt));
        """;

    private const string ActiveRoutineKey = "Routine.ActiveRevision";

    private static string RoutineJson<T>(T value) => JsonSerializer.Serialize(value, Json);
    private static T? ReadRoutine<T>(string? value) => value is null ? default : JsonSerializer.Deserialize<T>(value, Json);

    public Task<RoutineGenerationJob?> GetRoutineJobAsync(string id) => RunAsync(async c =>
        ReadRoutine<RoutineGenerationJob>(await c.QuerySingleOrDefaultAsync<string>("SELECT Data FROM RoutineGeneration WHERE Id = @id", new { id })));

    public Task<List<RoutineGenerationJob>> GetRoutineJobsAsync() => RunAsync(async c =>
        (await c.QueryAsync<string>("SELECT Data FROM RoutineGeneration ORDER BY Updated DESC LIMIT 30")).Select(d => ReadRoutine<RoutineGenerationJob>(d)!).ToList());

    public Task CreateRoutineJobAsync(RoutineGenerationJob job) => RunAsync(c => c.ExecuteAsync(
        "INSERT INTO RoutineGeneration(Id,Revision,State,Updated,Data) VALUES(@Id,@Revision,@state,@updated,@data)",
        new { job.Id, job.Revision, state = (int)job.State, updated = Iso(job.Updated), data = RoutineJson(job) }));

    public Task<bool> SaveRoutineJobAsync(RoutineGenerationJob job, int expectedRevision) => RunAsync(async c =>
    {
        if (job.Revision != expectedRevision + 1) throw new ArgumentException("Invalid routine revision.", nameof(job));
        return await c.ExecuteAsync("UPDATE RoutineGeneration SET Revision=@Revision,State=@state,Updated=@updated,Data=@data WHERE Id=@Id AND Revision=@expectedRevision AND State NOT IN (@cancelled,@accepted)",
            new { job.Id, job.Revision, state = (int)job.State, updated = Iso(job.Updated), data = RoutineJson(job), expectedRevision,
                cancelled = (int)RoutineGenerationState.Cancelled, accepted = (int)RoutineGenerationState.Accepted }) == 1;
    });

    public Task RecoverRoutineJobsAsync() => RunAsync(async c =>
    {
        await using var transaction = (SqliteTransaction)await c.BeginTransactionAsync();
        foreach (var data in await c.QueryAsync<string>("SELECT Data FROM RoutineGeneration WHERE State IN (@queued,@running)", new { queued = (int)RoutineGenerationState.Queued, running = (int)RoutineGenerationState.Running }, transaction))
        {
            var job = ReadRoutine<RoutineGenerationJob>(data)!;
            job = job with { Revision = job.Revision + 1, State = RoutineGenerationState.Interrupted, Updated = DateTimeOffset.UtcNow,
                Problem = job.Dispatched ? "The request was interrupted and may have been charged. Start a new request only when you are ready." : "The saved request was interrupted before sending." };
            if (job.Draft is { } received)
            {
                try { job = job with { State = RoutineGenerationState.Ready, Prepared = RoutineCompiler.Prepare(received, job.Request, job.Source), Problem = null }; }
                catch (Exception error) when (error is ArgumentException or InvalidOperationException or FormatException)
                {
                    job = job with { State = RoutineGenerationState.Failed, Problem = error.Message };
                }
            }
            await c.ExecuteAsync("UPDATE RoutineGeneration SET Revision=@Revision,State=@state,Updated=@updated,Data=@data WHERE Id=@Id",
                new { job.Id, job.Revision, state = (int)job.State, updated = Iso(job.Updated), data = RoutineJson(job) }, transaction);
        }
        foreach (var data in await c.QueryAsync<string>("SELECT Data FROM RoutineExecution", transaction: transaction))
        {
            var execution = ReadRoutine<RoutineExecution>(data)!;
            if (execution.State == RoutineItemState.InProgress) await WriteRoutineExecutionAsync(c, execution with { State = RoutineItemState.Paused }, transaction);
        }
        await transaction.CommitAsync();
    });

    public Task<RoutineDefinition?> GetRoutineDefinitionAsync(string revision) => RunAsync(async c =>
        ReadRoutine<RoutineDefinition>(await c.QuerySingleOrDefaultAsync<string>("SELECT Data FROM RoutineDefinition WHERE Revision=@revision", new { revision })));

    public Task<RoutineDefinition?> GetActiveRoutineAsync() => RunAsync(async c =>
        ReadRoutine<RoutineDefinition>(await c.QuerySingleOrDefaultAsync<string>("SELECT d.Data FROM RoutineDefinition d JOIN Setting s ON s.Value=d.Revision WHERE s.Key=@key", new { key = ActiveRoutineKey })));

    public Task DeactivateRoutineAsync() => SetSettingAsync(ActiveRoutineKey, null);

    /// <summary>Only an explicitly accepted, prepared draft becomes active. An accepted revision is never overwritten.</summary>
    public Task<RoutineDefinition> AcceptRoutineAsync(string jobId) => RunAsync(async c =>
    {
        var job = ReadRoutine<RoutineGenerationJob>(await c.QuerySingleOrDefaultAsync<string>("SELECT Data FROM RoutineGeneration WHERE Id=@jobId", new { jobId })) ?? throw new InvalidOperationException("The routine request was not found.");
        if (job.AcceptedRevision is { } accepted)
            return ReadRoutine<RoutineDefinition>(await c.QuerySingleAsync<string>("SELECT Data FROM RoutineDefinition WHERE Revision=@accepted", new { accepted }))!;
        if (job.State != RoutineGenerationState.Ready || job.Draft is null || job.Prepared is not { Count: > 0 }) throw new InvalidOperationException("Review a valid prepared routine before accepting it.");
        var validated = RoutineCompiler.Prepare(job.Draft, job.Request, job.Source);
        if (RoutineJson(validated) != RoutineJson(job.Prepared)) throw new InvalidOperationException("The saved preview no longer matches the routine draft.");
        var definition = new RoutineDefinition(job.Id, Guid.NewGuid().ToString("N"), DateTimeOffset.UtcNow, job.Draft.Title, job.Draft.Summary,
            job.Request, job.Evidence, job.Source, job.Provider, job.Model, job.Prepared);
        var saved = job with { Revision = job.Revision + 1, State = RoutineGenerationState.Accepted, Updated = DateTimeOffset.UtcNow, AcceptedRevision = definition.Revision };
        await using var transaction = (SqliteTransaction)await c.BeginTransactionAsync();
        await c.ExecuteAsync("INSERT INTO RoutineDefinition(Revision,Accepted,Data) VALUES(@Revision,@accepted,@data)", new { definition.Revision, accepted = Iso(definition.Accepted), data = RoutineJson(definition) }, transaction);
        await c.ExecuteAsync("INSERT INTO Setting(Key,Value) VALUES(@key,@value) ON CONFLICT(Key) DO UPDATE SET Value=excluded.Value", new { key = ActiveRoutineKey, value = definition.Revision }, transaction);
        await c.ExecuteAsync("UPDATE RoutineGeneration SET Revision=@Revision,State=@state,Updated=@updated,Data=@data WHERE Id=@Id", new { saved.Id, saved.Revision, state = (int)saved.State, updated = Iso(saved.Updated), data = RoutineJson(saved) }, transaction);
        await transaction.CommitAsync();
        return definition;
    });

    public Task<RoutineDay?> GetRoutineDayAsync(string id) => RunAsync(async c =>
        ReadRoutine<RoutineDay>(await c.QuerySingleOrDefaultAsync<string>("SELECT Data FROM RoutineDay WHERE Id=@id", new { id })));

    public Task<RoutineDay?> GetOrCreateRoutineDayAsync(DateOnly day) => RunAsync(async c =>
    {
        var definition = ReadRoutine<RoutineDefinition>(await c.QuerySingleOrDefaultAsync<string>("SELECT d.Data FROM RoutineDefinition d JOIN Setting s ON s.Value=d.Revision WHERE s.Key=@key", new { key = ActiveRoutineKey }));
        if (definition is null) return null;
        var existing = await c.QuerySingleOrDefaultAsync<string>("SELECT Data FROM RoutineDay WHERE Revision=@Revision AND Day=@day", new { definition.Revision, day = Iso(day) });
        if (existing is not null) return ReadRoutine<RoutineDay>(existing);
        var progress = new RoutineDay(Guid.NewGuid().ToString("N"), definition.Revision, day, definition.Items.Select(i => new RoutineItemProgress(i.Recipe.Id)).ToArray());
        await c.ExecuteAsync("INSERT INTO RoutineDay(Id,Revision,Day,Data) VALUES(@Id,@Revision,@day,@data)", new { progress.Id, progress.Revision, day = Iso(day), data = RoutineJson(progress) });
        return progress;
    });

    public Task<RoutineExecution?> GetRoutineExecutionAsync(string id) => RunAsync(async c =>
        ReadRoutine<RoutineExecution>(await c.QuerySingleOrDefaultAsync<string>("SELECT Data FROM RoutineExecution WHERE Id=@id", new { id })));

    public Task<HashSet<long>> GetRoutineAttemptIdsAsync(string revision, string itemId) => RunAsync(async c =>
        (await c.QueryAsync<long>("SELECT a.Attempt FROM RoutineAttempt a JOIN RoutineExecution e ON e.Id=a.Execution JOIN RoutineDay d ON d.Id=e.DayId WHERE d.Revision=@revision AND e.ItemId=@itemId", new { revision, itemId })).ToHashSet());

    public Task<List<RoutineExecution>> GetRoutineExecutionsAsync(string revision) => RunAsync(async c =>
        (await c.QueryAsync<string>("SELECT e.Data FROM RoutineExecution e JOIN RoutineDay d ON d.Id=e.DayId WHERE d.Revision=@revision ORDER BY d.Day DESC", new { revision })).Select(d => ReadRoutine<RoutineExecution>(d)!).ToList());

    public Task SaveRoutineFeedbackAsync(string id, string feedback) => RunAsync(async c =>
    {
        if (feedback is not ("easy" or "right" or "hard")) throw new ArgumentException("Unsupported practice feedback.", nameof(feedback));
        var execution = ReadRoutine<RoutineExecution>(await c.QuerySingleOrDefaultAsync<string>("SELECT Data FROM RoutineExecution WHERE Id=@id", new { id })) ?? throw new InvalidOperationException("The routine execution was not found.");
        await using var transaction = (SqliteTransaction)await c.BeginTransactionAsync();
        await WriteRoutineExecutionAsync(c, execution with { Feedback = feedback }, transaction);
        await transaction.CommitAsync();
    });

    public Task<RoutineExecution> BeginRoutineExecutionAsync(string dayId, string itemId) => RunAsync(async c =>
    {
        var day = ReadRoutine<RoutineDay>(await c.QuerySingleOrDefaultAsync<string>("SELECT Data FROM RoutineDay WHERE Id=@dayId", new { dayId })) ?? throw new InvalidOperationException("The practice day was not found.");
        var definition = ReadRoutine<RoutineDefinition>(await c.QuerySingleAsync<string>("SELECT Data FROM RoutineDefinition WHERE Revision=@Revision", new { day.Revision }))!;
        var item = definition.Items.SingleOrDefault(i => i.Recipe.Id == itemId) ?? throw new InvalidOperationException("The routine item was not found.");
        var existing = ReadRoutine<RoutineExecution>(await c.QuerySingleOrDefaultAsync<string>("SELECT Data FROM RoutineExecution WHERE DayId=@dayId AND ItemId=@itemId", new { dayId, itemId }));
        var execution = existing ?? new RoutineExecution(Guid.NewGuid().ToString("N"), dayId, day.Revision, itemId, day.Day, item, RoutineItemState.InProgress);
        if (execution.State is RoutineItemState.NotStarted or RoutineItemState.Paused) execution = execution with { State = RoutineItemState.InProgress };
        await using var transaction = (SqliteTransaction)await c.BeginTransactionAsync();
        await WriteRoutineExecutionAsync(c, execution, transaction);
        await transaction.CommitAsync();
        return execution;
    });

    /// <summary>Credit each submission once. Time is cumulative for the execution and cannot move backwards.</summary>
    public Task<long> RecordRoutineAttemptAsync(string executionId, string submissionId, AttemptRecord attempt, double totalActiveSeconds, int completedPasses = 0) => RunAsync(async c =>
    {
        if (string.IsNullOrWhiteSpace(submissionId) || !double.IsFinite(totalActiveSeconds) || totalActiveSeconds < 0 || completedPasses < 0) throw new ArgumentException("Invalid routine attempt.");
        var existing = await c.QuerySingleOrDefaultAsync<(string Execution, long Attempt)?>("SELECT Execution,Attempt FROM RoutineAttempt WHERE Submission=@submissionId", new { submissionId });
        if (existing is { } duplicate)
        {
            if (duplicate.Execution != executionId) throw new InvalidOperationException("The submission belongs to another routine execution.");
            return duplicate.Attempt;
        }
        var execution = ReadRoutine<RoutineExecution>(await c.QuerySingleOrDefaultAsync<string>("SELECT Data FROM RoutineExecution WHERE Id=@executionId", new { executionId })) ?? throw new InvalidOperationException("The routine execution was not found.");
        var a = attempt with { Id = 0, Day = execution.Day };
        await using var transaction = (SqliteTransaction)await c.BeginTransactionAsync();
        var id = await c.ExecuteScalarAsync<long>("""
            INSERT INTO Attempt(At,Day,Kind,Variant,SetId,Level,Tempo,Seed,Settings,Played,Beats,Measures,Headline,Clean,Looked)
            VALUES(@At,@Day,@Kind,@Variant,@SetId,@Level,@Tempo,@Seed,@Settings,@Played,@Beats,@Measures,@Headline,@Clean,@Looked); SELECT last_insert_rowid();
            """, new { At = Iso(a.At), Day = Iso(a.Day), a.Kind, a.Variant, a.SetId, a.Level, a.Tempo, a.Seed, a.Settings, a.Played, a.Beats, a.Measures, a.Headline, Clean = a.Clean ? 1 : 0, Looked = a.Looked is null ? (int?)null : a.Looked == true ? 1 : 0 }, transaction);
        await c.ExecuteAsync("INSERT INTO RoutineAttempt(Submission,Execution,Attempt) VALUES(@submissionId,@executionId,@id)", new { submissionId, executionId, id }, transaction);
        execution = execution with { ActiveSeconds = Math.Max(execution.ActiveSeconds, totalActiveSeconds), CompletedPasses = checked(execution.CompletedPasses + completedPasses) };
        if (completedPasses > 0 && execution.ActiveSeconds >= execution.Item.Recipe.Minutes * 60 && execution.State is not (RoutineItemState.Done or RoutineItemState.Skipped))
            execution = execution with { State = RoutineItemState.Done };
        await WriteRoutineExecutionAsync(c, execution, transaction);
        await transaction.CommitAsync();
        return id;
    });

    public Task CheckpointRoutineExecutionAsync(string id, double totalActiveSeconds, bool pause = false) => RunAsync(async c =>
    {
        if (!double.IsFinite(totalActiveSeconds) || totalActiveSeconds < 0) throw new ArgumentOutOfRangeException(nameof(totalActiveSeconds));
        var execution = ReadRoutine<RoutineExecution>(await c.QuerySingleOrDefaultAsync<string>("SELECT Data FROM RoutineExecution WHERE Id=@id", new { id })) ?? throw new InvalidOperationException("The routine execution was not found.");
        execution = execution with { ActiveSeconds = Math.Max(execution.ActiveSeconds, totalActiveSeconds), State = pause && execution.State == RoutineItemState.InProgress ? RoutineItemState.Paused : execution.State };
        await using var transaction = (SqliteTransaction)await c.BeginTransactionAsync();
        await WriteRoutineExecutionAsync(c, execution, transaction);
        await transaction.CommitAsync();
    });

    public Task CompleteRoutineExecutionAsync(string id, bool skipped = false) => RunAsync(async c =>
    {
        var execution = ReadRoutine<RoutineExecution>(await c.QuerySingleOrDefaultAsync<string>("SELECT Data FROM RoutineExecution WHERE Id=@id", new { id })) ?? throw new InvalidOperationException("The routine execution was not found.");
        await using var transaction = (SqliteTransaction)await c.BeginTransactionAsync();
        await WriteRoutineExecutionAsync(c, execution with { State = skipped ? RoutineItemState.Skipped : RoutineItemState.Done }, transaction);
        await transaction.CommitAsync();
    });

    private static async Task WriteRoutineExecutionAsync(SqliteConnection c, RoutineExecution execution, SqliteTransaction? transaction = null)
    {
        var before = ReadRoutine<RoutineExecution>(await c.QuerySingleOrDefaultAsync<string>("SELECT Data FROM RoutineExecution WHERE Id=@Id", new { execution.Id }, transaction));
        var credited = Math.Max(0, execution.ActiveSeconds - (before?.ActiveSeconds ?? 0));
        await c.ExecuteAsync("INSERT INTO RoutineExecution(Id,DayId,ItemId,Data) VALUES(@Id,@DayId,@ItemId,@data) ON CONFLICT(Id) DO UPDATE SET Data=excluded.Data", new { execution.Id, execution.DayId, execution.ItemId, data = RoutineJson(execution) }, transaction);
        if (credited > 0) await c.ExecuteAsync("INSERT INTO PracticeDay(Day,Minutes) VALUES(@day,@minutes) ON CONFLICT(Day) DO UPDATE SET Minutes=Minutes+excluded.Minutes", new { day = Iso(execution.Day), minutes = credited / 60 }, transaction);
        var day = ReadRoutine<RoutineDay>(await c.QuerySingleAsync<string>("SELECT Data FROM RoutineDay WHERE Id=@DayId", new { execution.DayId }, transaction))!;
        day = day with { Items = day.Items.Select(i => i.ItemId == execution.ItemId ? new RoutineItemProgress(i.ItemId, execution.State, execution.ActiveSeconds, execution.CompletedPasses, execution.Id) : i).ToArray() };
        await c.ExecuteAsync("UPDATE RoutineDay SET Data=@data WHERE Id=@Id", new { day.Id, data = RoutineJson(day) }, transaction);
    }
}
