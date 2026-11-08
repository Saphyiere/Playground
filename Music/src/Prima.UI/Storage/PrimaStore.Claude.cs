using System.Globalization;
using Dapper;
using Playground.AI.Assist;

namespace Prima.UI.Storage;

/// <summary>What Claude cost this month for one piece (or for calls about no piece, like a key's test): its title and PDF layout when it's still in the Library.</summary>
public sealed record ClaudeSpend(string? Piece, string? Title, string? Layout, decimal Cost, DateTimeOffset Last);

/// <summary>Prima's calls to Claude, kept in its data file with the piece each was for, so Settings can show what a month cost.</summary>
public sealed class ClaudeLog(PrimaStore store) : IAssistLog
{
    /// <summary>The piece the calls being made are for: set around a reading, it goes with every call the reading makes.</summary>
    public static readonly AsyncLocal<string?> Piece = new();

    public Task WriteAsync(AssistCall call) => store.AddClaudeCallAsync(call, Piece.Value);
}

public sealed partial class PrimaStore
{
    // Each call to Claude: when, for which piece, the task and model, how it went, what was asked and answered (never the key), and
    // what it used and cost. A piece taken out of the Library keeps its calls, so the month's cost stays true.
    private const string ClaudeSchema = """
        CREATE TABLE IF NOT EXISTS ClaudeCall (
            Id INTEGER PRIMARY KEY,
            At TEXT NOT NULL,
            Piece TEXT,
            Task TEXT NOT NULL,
            Version INTEGER NOT NULL,
            Model TEXT NOT NULL,
            Attempt INTEGER NOT NULL,
            Kind TEXT NOT NULL,
            Summary TEXT NOT NULL,
            Question TEXT NOT NULL,
            Reply TEXT NOT NULL,
            Attachments INTEGER NOT NULL,
            InputTokens INTEGER NOT NULL,
            OutputTokens INTEGER NOT NULL,
            CacheReadTokens INTEGER NOT NULL,
            CacheWriteTokens INTEGER NOT NULL,
            Cost REAL NOT NULL,
            Ms INTEGER NOT NULL,
            MessageId TEXT NOT NULL,
            Error TEXT NOT NULL
        );
        CREATE INDEX IF NOT EXISTS ClaudeCallByTime ON ClaudeCall (At);
        """;

    public Task AddClaudeCallAsync(AssistCall call, string? piece) => RunAsync(c => c.ExecuteAsync("""
        INSERT INTO ClaudeCall (At, Piece, Task, Version, Model, Attempt, Kind, Summary, Question, Reply, Attachments, InputTokens, OutputTokens, CacheReadTokens, CacheWriteTokens, Cost, Ms, MessageId, Error)
        VALUES (@at, @piece, @Task, @Version, @Model, @Attempt, @kind, @Summary, @Question, @Reply, @Attachments, @input, @output, @cacheRead, @cacheWrite, @cost, @ms, @MessageId, @Error)
        """, new
    {
        at = Utc(call.At), piece, call.Task, call.Version, call.Model, call.Attempt, kind = call.Kind.ToString(), call.Summary, call.Question, call.Reply, call.Attachments,
        input = call.Usage.InputTokens, output = call.Usage.OutputTokens, cacheRead = call.Usage.CacheReadTokens, cacheWrite = call.Usage.CacheWriteTokens,
        cost = (double)call.Usage.Cost, ms = (long)call.Usage.Took.TotalMilliseconds, call.MessageId, call.Error,
    }));

    /// <summary>What Claude has cost since <paramref name="since"/>, by piece, the latest first.</summary>
    public Task<List<ClaudeSpend>> GetClaudeSpendAsync(DateTimeOffset since) => RunAsync(async c => (await c.QueryAsync<(string? Piece, string? Title, string? Layout, double Cost, string Last)>("""
        SELECT ClaudeCall.Piece, Piece.Title, Piece.Layout, SUM(ClaudeCall.Cost), MAX(ClaudeCall.At) AS Last
        FROM ClaudeCall LEFT JOIN Piece ON Piece.Id = ClaudeCall.Piece
        WHERE ClaudeCall.At >= @since
        GROUP BY ClaudeCall.Piece
        ORDER BY Last DESC
        """, new { since = Utc(since) }))
        .Select(r => new ClaudeSpend(r.Piece, r.Title, r.Layout, (decimal)r.Cost, DateTimeOffset.Parse(r.Last, CultureInfo.InvariantCulture))).ToList());

    // In UTC, so times taken in different offsets compare as text.
    private static string Utc(DateTimeOffset at) => at.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture);
}
