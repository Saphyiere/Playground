using System.ComponentModel;
using System.Net;
using System.Text;
using System.Text.Json;

namespace Playground.AI.Assist.Tests;

/// <summary>Stands in for the Claude API: keeps each request, and answers with the replies queued for it, in order.</summary>
sealed class FakeApi : HttpMessageHandler
{
    readonly Queue<HttpResponseMessage> replies = new();

    public List<(JsonElement Body, Dictionary<string, string> Headers)> Requests { get; } = [];

    /// <summary>A reply whose text is <paramref name="text"/>, 1,000 tokens in and 500 out.</summary>
    public void Reply(string text, string stopReason = "end_turn", string model = ClaudeModels.Opus)
    {
        var message = new
        {
            id = $"msg_{replies.Count + 1}", type = "message", role = "assistant", model, stop_reason = stopReason, stop_sequence = (string?)null,
            content = new[] { new { type = "text", text } },
            usage = new { input_tokens = 1000, output_tokens = 500, cache_read_input_tokens = 0, cache_creation_input_tokens = 0 },
        };
        replies.Enqueue(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(JsonSerializer.Serialize(message), Encoding.UTF8, "application/json") });
    }

    /// <summary>A reply the API sends for this model's details.</summary>
    public void Model(string id, string name) => replies.Enqueue(new HttpResponseMessage(HttpStatusCode.OK)
    {
        Content = new StringContent(JsonSerializer.Serialize(new { type = "model", id, display_name = name, created_at = "2026-09-01T00:00:00Z" }), Encoding.UTF8, "application/json"),
    });

    /// <summary>An error reply, as the API sends it.</summary>
    public void Error(HttpStatusCode status, string type, string message) => replies.Enqueue(new HttpResponseMessage(status)
    {
        Content = new StringContent(JsonSerializer.Serialize(new { type = "error", error = new { type, message } }), Encoding.UTF8, "application/json"),
    });

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var body = request.Content is null ? "{}" : await request.Content.ReadAsStringAsync(cancellationToken);
        Requests.Add((JsonDocument.Parse(body.Length == 0 ? "{}" : body).RootElement.Clone(), request.Headers.ToDictionary(h => h.Key, h => string.Join(",", h.Value))));
        return replies.Dequeue();
    }
}

sealed class MemoryKeys(string? key = null) : IAssistKeyStore
{
    public string? Key { get; private set; } = key;

    public Task<string?> GetAsync() => Task.FromResult(Key);

    public Task SetAsync(string key)
    {
        Key = key;
        return Task.CompletedTask;
    }

    public Task RemoveAsync()
    {
        Key = null;
        return Task.CompletedTask;
    }
}

sealed class FixedTime(DateTimeOffset now) : TimeProvider
{
    public override DateTimeOffset GetUtcNow() => now;

    public override TimeZoneInfo LocalTimeZone => TimeZoneInfo.Utc;
}

enum Change
{
    Up,
    Same,
    Down,
}

sealed record Lift([property: Description("One of the owner's exercises, by id.")] int ExerciseId, double Weight, int Reps, Change Change, string Why);

sealed record Plan(IReadOnlyList<Lift> Lifts);

sealed record History(IReadOnlyList<int> ExerciseIds);

/// <summary>A task like the trainer's, small: which of the owner's exercises to lift next, and how.</summary>
sealed class NextLifts(string model = ClaudeModels.Opus) : AssistTask<History, Plan>
{
    public override string Name => "test.next-lifts";

    public override int Version => 3;

    public override string Instructions => "Say what to lift next.";

    public override string Model => model;

    public override IReadOnlyList<Attachment> Attachments(History input) => [Attachment.Png([1, 2, 3], "The last workout's page")];

    public override IReadOnlyList<string> Check(History input, Plan answer) =>
        [.. answer.Lifts.Where(l => !input.ExerciseIds.Contains(l.ExerciseId)).Select(l => $"Exercise {l.ExerciseId} isn't one of the owner's.")];
}
