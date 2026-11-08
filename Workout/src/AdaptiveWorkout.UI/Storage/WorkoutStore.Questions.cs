using System.Globalization;
using System.Text.Json;
using AdaptiveWorkout.Core.Assist;
using Dapper;
using Playground.AI.Assist;

namespace AdaptiveWorkout.UI.Storage;

/// <summary>
/// A question asked of Claude, as kept: what was asked and when, how the asking went (<see cref="Kind"/>, with Claude's summary and
/// notes), what Claude was told and said, what it cost, and when its plan was taken for the next time.
/// </summary>
public sealed record QuestionRecord(long Id, DateTimeOffset At, string Question, AnswerKind Kind, string Summary, IReadOnlyList<string> Notes, AskInput? Input,
    AskAnswer? Answer, decimal Cost, DateTimeOffset? AppliedAt);

public sealed partial class WorkoutStore
{
    /// <summary>Keeps a question asked, and fills in its Id.</summary>
    public Task<QuestionRecord> AddQuestionAsync(QuestionRecord record) => RunAsync(async c => record with
    {
        Id = await c.ExecuteScalarAsync<long>("""
            INSERT INTO Question (At, Question, Kind, Summary, Notes, Input, Answer, Cost, AppliedAt) VALUES (@At, @Question, @kind, @Summary, @notes, @input, @answer, @cost, @AppliedAt);
            SELECT last_insert_rowid();
            """, new
        {
            record.At, record.Question, kind = record.Kind.ToString(), record.Summary, notes = JsonSerializer.Serialize(record.Notes, AssistSchema.Json),
            input = record.Input is null ? null : JsonSerializer.Serialize(record.Input, AssistSchema.Json),
            answer = record.Answer is null ? null : JsonSerializer.Serialize(record.Answer, AssistSchema.Json), cost = (double)record.Cost, record.AppliedAt,
        }),
    });

    /// <summary>The latest questions asked, newest first; one written in a shape this version can't read is left out.</summary>
    public Task<List<QuestionRecord>> GetQuestionsAsync(int count) => RunAsync(async c =>
    {
        var rows = await c.QueryAsync<(long Id, string At, string Question, string Kind, string Summary, string Notes, string? Input, string? Answer, double Cost, string? AppliedAt)>(
            "SELECT Id, At, Question, Kind, Summary, Notes, Input, Answer, Cost, AppliedAt FROM Question ORDER BY Id DESC LIMIT @count", new { count });
        var questions = new List<QuestionRecord>();
        foreach (var r in rows)
        {
            if (!Enum.TryParse<AnswerKind>(r.Kind, out var kind)) continue;

            try
            {
                questions.Add(new QuestionRecord(r.Id, Time(r.At), r.Question, kind, r.Summary, JsonSerializer.Deserialize<List<string>>(r.Notes, AssistSchema.Json) ?? [],
                    r.Input is null ? null : JsonSerializer.Deserialize<AskInput>(r.Input, AssistSchema.Json),
                    r.Answer is null ? null : JsonSerializer.Deserialize<AskAnswer>(r.Answer, AssistSchema.Json), (decimal)r.Cost, r.AppliedAt is null ? null : Time(r.AppliedAt)));
            }
            catch (JsonException)
            {
            }
        }

        return questions;
    });

    /// <summary>Notes when a question's plan was taken for the next time.</summary>
    public Task SetQuestionAppliedAsync(long id, DateTimeOffset at) =>
        RunAsync(c => c.ExecuteAsync("UPDATE Question SET AppliedAt = @at WHERE Id = @id", new { id, at = at.ToString("O", CultureInfo.InvariantCulture) }));
}
