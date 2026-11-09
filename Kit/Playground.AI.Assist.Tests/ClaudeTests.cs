using System.Net;
using System.Text.Json;

namespace Playground.AI.Assist.Tests;

public class ClaudeTests
{
    const string Key = "sk-ant-api03-test-key-0123456789abcdef";
    static readonly History Owners = new([12, 14]);
    static readonly string Good = """{"status":"answered","summary":"Up to 105 on the press.","notes":[],"answer":{"lifts":[{"exerciseId":12,"weight":105,"reps":10,"change":"up","why":"10, 10, 10 at 100"}]}}""";
    static readonly string Stranger = """{"status":"answered","summary":"Try the leg press.","notes":[],"answer":{"lifts":[{"exerciseId":99,"weight":200,"reps":10,"change":"same","why":"New"}]}}""";

    readonly FakeApi api = new();
    readonly MemoryAssistLog log = new();

    Claude Client(string? key = Key) => new(new MemoryKeys(key), log, new FixedTime(new DateTimeOffset(2026, 11, 8, 17, 0, 0, TimeSpan.Zero)), api, maxRetries: 0);

    static string Question(JsonElement body)
    {
        var content = body.GetProperty("messages")[0].GetProperty("content");
        return content[content.GetArrayLength() - 1].GetProperty("text").GetString()!;
    }

    [Fact]
    public async Task AskAsync_AnAnswerThatPassesItsChecksIsOk()
    {
        api.Reply(Good);

        var answer = await Client().AskAsync(new NextLifts(), Owners, TestContext.Current.CancellationToken);

        Assert.True(answer.Ok);
        Assert.Equal(new Lift(12, 105, 10, Change.Up, "10, 10, 10 at 100"), Assert.Single(answer.Value.Lifts));
        Assert.Equal("Up to 105 on the press.", answer.Summary);
        Assert.Equal(0.014m, answer.Usage.Cost);
        Assert.Equal(AnswerKind.Answered, Assert.Single(log.Calls).Kind);
    }

    [Fact]
    public async Task AskAsync_SendsThePreambleInstructionsAttachmentsInputAndSchema()
    {
        api.Reply(Good);

        await Client().AskAsync(new NextLifts(), Owners, TestContext.Current.CancellationToken);

        var (body, headers) = Assert.Single(api.Requests);
        Assert.Equal(ClaudeModels.Opus, body.GetProperty("model").GetString());
        Assert.Equal(Claude.Preamble + "\n\nSay what to lift next.", body.GetProperty("system").GetString());
        var content = body.GetProperty("messages")[0].GetProperty("content");
        Assert.Equal("The last workout's page", content[0].GetProperty("text").GetString());
        Assert.Equal("AQID", content[1].GetProperty("source").GetProperty("data").GetString());
        Assert.Equal("image/png", content[1].GetProperty("source").GetProperty("media_type").GetString());
        Assert.Equal("""{"task":"test.next-lifts","version":3,"today":"2026-11-08","input":{"exerciseIds":[12,14]}}""", Question(body));
        Assert.Equal("high", body.GetProperty("output_config").GetProperty("effort").GetString());
        Assert.Equal(JsonSerializer.Serialize(AssistSchema.For(typeof(Reply<Plan>))), body.GetProperty("output_config").GetProperty("format").GetProperty("schema").GetRawText());
        Assert.Equal("default", body.GetProperty("fallbacks").GetString());
        Assert.Contains("server-side-fallback-2026-07-01", headers["anthropic-beta"]);
        Assert.True(body.GetProperty("stream").GetBoolean());
    }

    [Fact]
    public async Task AskAsync_HaikuGoesWithoutAFallback()
    {
        api.Reply(Good, model: ClaudeModels.Haiku);

        await Client().AskAsync(new NextLifts(ClaudeModels.Haiku), Owners, TestContext.Current.CancellationToken);

        var (body, headers) = Assert.Single(api.Requests);
        Assert.False(body.TryGetProperty("fallbacks", out _));
        Assert.False(headers.ContainsKey("anthropic-beta"));
    }

    [Fact]
    public async Task AskAsync_AnAnswerThatFailsItsChecksIsAskedForAgainWithTheProblems()
    {
        api.Reply(Stranger);
        api.Reply(Good);

        var answer = await Client().AskAsync(new NextLifts(), Owners, TestContext.Current.CancellationToken);

        Assert.True(answer.Ok);
        Assert.Equal(2, api.Requests.Count);
        var again = Question(api.Requests[1].Body);
        Assert.Contains(Stranger, again);
        Assert.Contains("- Exercise 99 isn't one of the owner's.", again);
        Assert.Equal([AnswerKind.CheckFailed, AnswerKind.Answered], log.Calls.Select(c => c.Kind));
        Assert.Equal(0.028m, answer.Usage.Cost);
    }

    [Fact]
    public async Task AskAsync_AnAnswerThatFailsItsChecksTwiceIsNotOk()
    {
        api.Reply(Stranger);
        api.Reply(Stranger);

        var answer = await Client().AskAsync(new NextLifts(), Owners, TestContext.Current.CancellationToken);

        Assert.False(answer.Ok);
        Assert.Equal(AnswerKind.CheckFailed, answer.Kind);
        Assert.Equal(["Exercise 99 isn't one of the owner's."], answer.Notes);
        Assert.Equal(2, api.Requests.Count);
    }

    [Theory]
    [InlineData("needMore", AnswerKind.NeedMore)]
    [InlineData("cannot", AnswerKind.Cannot)]
    public async Task AskAsync_NeedMoreOrCannotCarriesClaudesSummaryAndNotes(string status, AnswerKind kind)
    {
        api.Reply($$"""{"status":"{{status}}","summary":"There's only one workout to go on.","notes":["Two more workouts would do."],"answer":null}""");

        var answer = await Client().AskAsync(new NextLifts(), Owners, TestContext.Current.CancellationToken);

        Assert.False(answer.Ok);
        Assert.Equal(kind, answer.Kind);
        Assert.Equal("There's only one workout to go on.", answer.Summary);
        Assert.Equal(["Two more workouts would do."], answer.Notes);
    }

    [Theory]
    [InlineData("refusal", AnswerKind.Refused)]
    [InlineData("max_tokens", AnswerKind.TooLong)]
    public async Task AskAsync_ARefusalOrACutOffAnswerIsNotOk(string stopReason, AnswerKind kind)
    {
        api.Reply("""{"status":"answ""", stopReason);

        var answer = await Client().AskAsync(new NextLifts(), Owners, TestContext.Current.CancellationToken);

        Assert.Equal(kind, answer.Kind);
        Assert.Null(answer.Value);
    }

    [Fact]
    public async Task AskAsync_WithoutAKeySendsNothing()
    {
        var answer = await Client(key: null).AskAsync(new NextLifts(), Owners, TestContext.Current.CancellationToken);

        Assert.Equal(AnswerKind.NoKey, answer.Kind);
        Assert.Empty(api.Requests);
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized, "authentication_error", "invalid x-api-key", AnswerKind.KeyRejected)]
    [InlineData(HttpStatusCode.BadRequest, "invalid_request_error", "Your credit balance is too low to access the Anthropic API.", AnswerKind.OutOfCredit)]
    [InlineData(HttpStatusCode.TooManyRequests, "rate_limit_error", "Number of requests has exceeded your rate limit", AnswerKind.Busy)]
    [InlineData((HttpStatusCode)529, "overloaded_error", "Overloaded", AnswerKind.Busy)]
    [InlineData(HttpStatusCode.BadRequest, "invalid_request_error", "output_config.format.schema: not supported", AnswerKind.Failed)]
    public async Task AskAsync_AnErrorFromTheApiBecomesAKindToShow(HttpStatusCode status, string type, string message, AnswerKind kind)
    {
        api.Error(status, type, message);

        var answer = await Client().AskAsync(new NextLifts(), Owners, TestContext.Current.CancellationToken);

        Assert.Equal(kind, answer.Kind);
        Assert.Equal(kind, Assert.Single(log.Calls).Kind);
    }

    [Fact]
    public async Task AskAsync_AStreamThatBreaksOffIsBusyNotACrash()
    {
        api.BrokenStream("overloaded_error", "Overloaded");

        var answer = await Client().AskAsync(new NextLifts(), Owners, TestContext.Current.CancellationToken);

        Assert.Equal(AnswerKind.Busy, answer.Kind);
        Assert.Equal(AnswerKind.Busy, Assert.Single(log.Calls).Kind);
    }

    [Fact]
    public async Task TestAsync_NamesTheModelWhenTheKeyWorks()
    {
        api.Model(ClaudeModels.Opus, "Claude Opus 5.5");

        var answer = await Client().TestAsync(TestContext.Current.CancellationToken);

        Assert.True(answer.Ok);
        Assert.Equal("Claude Opus 5.5", answer.Value);
    }

    [Fact]
    public async Task TestAsync_SaysWhenTheKeyIsRejected()
    {
        api.Error(HttpStatusCode.Unauthorized, "authentication_error", "invalid x-api-key");

        Assert.Equal(AnswerKind.KeyRejected, (await Client().TestAsync(TestContext.Current.CancellationToken)).Kind);
    }
}
