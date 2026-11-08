using System.Globalization;
using System.Text.Json;
using AdaptiveWorkout.Core.Assist;
using Dapper;
using Playground.AI.Assist;

namespace AdaptiveWorkout.UI.Storage;

/// <summary>
/// Claude's look back at a week, as kept, by the week's Monday: how the asking went (<see cref="Kind"/>, with Claude's summary and notes),
/// what it was told and said, and what it cost.
/// </summary>
public sealed record WeekLookRecord(DateOnly Monday, DateTimeOffset At, AnswerKind Kind, string Summary, IReadOnlyList<string> Notes, WeekInput? Input, WeekLook? Answer,
    decimal Cost);

public sealed partial class WorkoutStore
{
    /// <summary>Keeps a week's look back, in place of any before it.</summary>
    public Task SaveWeekLookAsync(WeekLookRecord record) => RunAsync(c => c.ExecuteAsync("""
        INSERT INTO WeekLook (Monday, At, Kind, Summary, Notes, Input, Answer, Cost) VALUES (@monday, @At, @kind, @Summary, @notes, @input, @answer, @cost)
        ON CONFLICT (Monday) DO UPDATE SET At = excluded.At, Kind = excluded.Kind, Summary = excluded.Summary, Notes = excluded.Notes, Input = excluded.Input,
            Answer = excluded.Answer, Cost = excluded.Cost
        """, new
    {
        monday = record.Monday.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), record.At, kind = record.Kind.ToString(), record.Summary,
        notes = JsonSerializer.Serialize(record.Notes, AssistSchema.Json), input = record.Input is null ? null : JsonSerializer.Serialize(record.Input, AssistSchema.Json),
        answer = record.Answer is null ? null : JsonSerializer.Serialize(record.Answer, AssistSchema.Json), cost = (double)record.Cost,
    }));

    /// <summary>Every week's look back kept, newest first; one written in a shape this version can't read is left out.</summary>
    public Task<List<WeekLookRecord>> GetWeekLooksAsync() => RunAsync(async c =>
    {
        var rows = await c.QueryAsync<(string Monday, string At, string Kind, string Summary, string Notes, string? Input, string? Answer, double Cost)>(
            "SELECT Monday, At, Kind, Summary, Notes, Input, Answer, Cost FROM WeekLook ORDER BY Monday DESC");
        var weeks = new List<WeekLookRecord>();
        foreach (var r in rows)
        {
            if (!Enum.TryParse<AnswerKind>(r.Kind, out var kind) || !DateOnly.TryParseExact(r.Monday, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var monday)) continue;

            try
            {
                weeks.Add(new WeekLookRecord(monday, Time(r.At), kind, r.Summary, JsonSerializer.Deserialize<List<string>>(r.Notes, AssistSchema.Json) ?? [],
                    r.Input is null ? null : JsonSerializer.Deserialize<WeekInput>(r.Input, AssistSchema.Json),
                    r.Answer is null ? null : JsonSerializer.Deserialize<WeekLook>(r.Answer, AssistSchema.Json), (decimal)r.Cost));
            }
            catch (JsonException)
            {
            }
        }

        return weeks;
    });
}
