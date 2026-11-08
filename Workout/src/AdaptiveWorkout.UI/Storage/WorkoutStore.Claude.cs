using System.Globalization;
using System.Text.Json;
using AdaptiveWorkout.Core.Assist;
using Dapper;
using Playground.AI.Assist;

namespace AdaptiveWorkout.UI.Storage;

/// <summary>
/// The trainer's look back at a workout, as kept: how the asking went (<see cref="Kind"/>, with Claude's summary and notes), what it
/// was asked, what it said, what it cost, and when its plan was applied.
/// </summary>
public sealed record LookBackRecord(long SessionId, DateTimeOffset At, AnswerKind Kind, string Summary, IReadOnlyList<string> Notes, LookBackInput? Input,
    LookBack? Answer, decimal Cost, DateTimeOffset? AppliedAt);

/// <summary>What asking Claude has cost since a day: the dollars, and how many workouts got a look back.</summary>
public sealed record ClaudeSpend(decimal Cost, int LookBacks);

public sealed partial class WorkoutStore
{
    /// <summary>Keeps one call to Claude (never the key), for the workout it was for when it was for one.</summary>
    public Task AddClaudeCallAsync(AssistCall call, long? sessionId) => RunAsync(c => c.ExecuteAsync("""
        INSERT INTO ClaudeCall (At, SessionId, Task, Version, Model, Attempt, Kind, Summary, Question, Reply, InputTokens, OutputTokens, CacheReadTokens, CacheWriteTokens, Cost, Ms, MessageId, Error)
        VALUES (@At, @sessionId, @Task, @Version, @Model, @Attempt, @kind, @Summary, @Question, @Reply, @input, @output, @cacheRead, @cacheWrite, @cost, @ms, @MessageId, @Error)
        """, new
    {
        call.At, sessionId, call.Task, call.Version, call.Model, call.Attempt, kind = call.Kind.ToString(), call.Summary, call.Question, call.Reply,
        input = call.Usage.InputTokens, output = call.Usage.OutputTokens, cacheRead = call.Usage.CacheReadTokens, cacheWrite = call.Usage.CacheWriteTokens,
        cost = (double)call.Usage.Cost, ms = (long)call.Usage.Took.TotalMilliseconds, call.MessageId, call.Error,
    }));

    /// <summary>What Claude has cost since <paramref name="since"/>, every call counted, and how many workouts it answered a look back for.</summary>
    public Task<ClaudeSpend> GetClaudeSpendAsync(DateTimeOffset since) => RunAsync(async c =>
    {
        // Times are kept with their own offsets, so they're compared as moments, not as text.
        var calls = (await c.QueryAsync<(string At, long? SessionId, string Kind, double Cost)>("SELECT At, SessionId, Kind, Cost FROM ClaudeCall")).Where(r => Time(r.At) >= since).ToList();
        var answered = calls.Where(r => r.SessionId is not null && r.Kind == nameof(AnswerKind.Answered)).Select(r => r.SessionId).Distinct().Count();
        return new ClaudeSpend((decimal)calls.Sum(r => r.Cost), answered);
    });

    /// <summary>Keeps a workout's look back, in place of any before it.</summary>
    public Task SaveLookBackAsync(LookBackRecord record) => RunAsync(c => c.ExecuteAsync("""
        INSERT INTO LookBack (SessionId, At, Kind, Summary, Notes, Input, Answer, Cost, AppliedAt) VALUES (@SessionId, @At, @kind, @Summary, @notes, @input, @answer, @cost, @AppliedAt)
        ON CONFLICT (SessionId) DO UPDATE SET At = excluded.At, Kind = excluded.Kind, Summary = excluded.Summary, Notes = excluded.Notes, Input = excluded.Input,
            Answer = excluded.Answer, Cost = excluded.Cost, AppliedAt = excluded.AppliedAt
        """, new
    {
        record.SessionId, record.At, kind = record.Kind.ToString(), record.Summary, notes = JsonSerializer.Serialize(record.Notes, AssistSchema.Json),
        input = record.Input is null ? null : JsonSerializer.Serialize(record.Input, AssistSchema.Json),
        answer = record.Answer is null ? null : JsonSerializer.Serialize(record.Answer, AssistSchema.Json), cost = (double)record.Cost, record.AppliedAt,
    }));

    /// <summary>A workout's look back; null when it has none, or when it was written in a shape this version can't read.</summary>
    public Task<LookBackRecord?> GetLookBackAsync(long sessionId) => RunAsync(async c =>
    {
        var rows = (await c.QueryAsync<(long SessionId, string At, string Kind, string Summary, string Notes, string? Input, string? Answer, double Cost, string? AppliedAt)>(
            "SELECT SessionId, At, Kind, Summary, Notes, Input, Answer, Cost, AppliedAt FROM LookBack WHERE SessionId = @sessionId", new { sessionId })).AsList();
        if (rows is not [var r] || !Enum.TryParse<AnswerKind>(r.Kind, out var kind)) return null;

        try
        {
            return new LookBackRecord(r.SessionId, Time(r.At), kind, r.Summary, JsonSerializer.Deserialize<List<string>>(r.Notes, AssistSchema.Json) ?? [],
                r.Input is null ? null : JsonSerializer.Deserialize<LookBackInput>(r.Input, AssistSchema.Json),
                r.Answer is null ? null : JsonSerializer.Deserialize<LookBack>(r.Answer, AssistSchema.Json), (decimal)r.Cost, r.AppliedAt is null ? null : Time(r.AppliedAt));
        }
        catch (JsonException)
        {
            return null;
        }
    });

    /// <summary>Notes when a look back's plan was applied for the next time.</summary>
    public Task SetLookBackAppliedAsync(long sessionId, DateTimeOffset at) =>
        RunAsync(c => c.ExecuteAsync("UPDATE LookBack SET AppliedAt = @at WHERE SessionId = @sessionId", new { sessionId, at = at.ToString("O", CultureInfo.InvariantCulture) }));

    private static DateTimeOffset Time(string text) => DateTimeOffset.Parse(text, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);
}
