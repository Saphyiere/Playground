using System.Text.Json;
using Anthropic;
using Anthropic.Exceptions;
using Anthropic.Models.Beta.Messages;

namespace Playground.AI.Assist;

/// <summary>The Claude models the apps use, by API id, and what they cost.</summary>
public static class ClaudeModels
{
    public const string Opus = "claude-opus-5-5";
    public const string Sonnet = "claude-sonnet-5-5";
    public const string Haiku = "claude-haiku-5-5";

    /// <summary>
    /// US dollars per million tokens, from the API's price list (November 2026): input, output, cache reads and five-minute
    /// cache writes. A refused request can be answered by another model, so those are here too.
    /// </summary>
    static readonly Dictionary<string, (decimal In, decimal Out, decimal CacheRead, decimal CacheWrite)> Prices = new()
    {
        [Opus] = (4m, 20m, 0.20m, 5m),
        [Sonnet] = (2m, 10m, 0.20m, 2.50m),
        [Haiku] = (0.10m, 0.50m, 0.01m, 0.125m),
        ["claude-fable-5-1"] = (10m, 50m, 0.25m, 12.50m),
        ["claude-opus-5"] = (5m, 25m, 0.50m, 6.25m),
        ["claude-opus-4-8"] = (5m, 25m, 0.50m, 6.25m),
        ["claude-sonnet-5"] = (2m, 10m, 0.20m, 2.50m),
    };

    /// <summary>What these tokens cost on <paramref name="model"/>; nothing for a model the list doesn't have.</summary>
    public static decimal Cost(string model, long input, long output, long cacheRead, long cacheWrite) =>
        Prices.TryGetValue(model, out var p) ? (input * p.In + output * p.Out + cacheRead * p.CacheRead + cacheWrite * p.CacheWrite) / 1_000_000m : 0m;
}

/// <summary>Asks Claude a task's question. An interface, so a preview host can answer from saved replies instead.</summary>
public interface IClaude
{
    Task<Answer<TAnswer>> AskAsync<TInput, TAnswer>(AssistTask<TInput, TAnswer> task, TInput input, CancellationToken ct = default);

    /// <summary>Whether the key on this device works, without spending anything. When it does, the value is the model's name.</summary>
    Task<Answer<string>> TestAsync(CancellationToken ct = default);
}

/// <summary>
/// Asks Claude through the API with the key on this device. Each question is one request: the task's instructions after the
/// shared preamble, then its attachments and its input as JSON, with the reply held to the schema of
/// <see cref="Reply{TAnswer}"/>. The answer is checked, asked for once more if the checks fail, logged call by call, and handed
/// back as an <see cref="Answer{TAnswer}"/>. A refused request is answered by another model where the API can do that.
/// </summary>
/// <param name="handler">Stands in for the network, for tests.</param>
/// <param name="maxRetries">How often the API client retries a request that failed on the way (busy, overloaded, dropped).</param>
public sealed class Claude(IAssistKeyStore keys, IAssistLog log, TimeProvider time, HttpMessageHandler? handler = null, int maxRetries = 2) : IClaude
{
    /// <summary>What every task is told before its own instructions.</summary>
    public const string Preamble = """
        You answer questions from the owner's own apps: AdaptiveWorkout, a workout tracker on their phone, and Prima, a piano
        practice app on their tablet. Each question is one task. The user message holds any images first, then the task's name
        and version, today's date and the task's input, as JSON. The task's own instructions follow this.

        Reply in the JSON shape you're given:
        - status: answered when you answered; needMore when the input isn't enough to answer well; cannot when the task can't be
          done with this input.
        - summary: one or two plain sentences for the app's screen.
        - notes: what the owner should know, like data you didn't trust and what you did with it, or what's missing; empty when
          there's nothing.
        - answer: the task's answer when status is answered; null otherwise.

        Work only from the input and the images, and never make up data they don't hold.
        """;

    const string NoKeySummary = "There's no Claude key on this device yet.";

    (string Key, AnthropicClient Client)? current;

    public async Task<Answer<TAnswer>> AskAsync<TInput, TAnswer>(AssistTask<TInput, TAnswer> task, TInput input, CancellationToken ct = default)
    {
        var client = await ClientAsync();
        if (client is null) return new(AnswerKind.NoKey, default, NoKeySummary, [], Usage.None);
        var today = DateOnly.FromDateTime(time.GetLocalNow().DateTime);
        var question = JsonSerializer.Serialize(new { task = task.Name, version = task.Version, today, input }, AssistSchema.Json);
        var attachments = task.Attachments(input);
        var shown = new List<BetaContentBlockParam>();
        foreach (var attachment in attachments)
        {
            if (attachment.Caption.Length > 0) shown.Add(new BetaTextBlockParam { Text = attachment.Caption });
            var data = Convert.ToBase64String(attachment.Data);
            shown.Add(attachment.Kind switch
            {
                AttachmentKind.Pdf => new BetaRequestDocumentBlock { Source = new BetaBase64PdfSource { Data = data } },
                AttachmentKind.Jpeg => new BetaImageBlockParam { Source = new BetaBase64ImageSource { Data = data, MediaType = MediaType.ImageJpeg } },
                _ => new BetaImageBlockParam { Source = new BetaBase64ImageSource { Data = data, MediaType = MediaType.ImagePng } },
            });
        }

        var effort = task.Effort switch
        {
            EffortLevel.Low => Effort.Low,
            EffortLevel.Medium => Effort.Medium,
            EffortLevel.XHigh => Effort.Xhigh,
            EffortLevel.Max => Effort.Max,
            _ => Effort.High,
        };
        // A refusal is answered by another model on the API's side; Haiku has no such fallback.
        var fallback = task.Model is ClaudeModels.Opus or ClaudeModels.Sonnet;
        var schema = AssistSchema.For(typeof(Reply<TAnswer>));
        var usage = Usage.None;
        var again = "";
        for (var attempt = 1; ; attempt++)
        {
            var started = time.GetTimestamp();
            var request = new MessageCreateParams
            {
                Model = task.Model,
                MaxTokens = task.MaxTokens,
                System = Preamble + "\n\n" + task.Instructions,
                Messages = [new BetaUserMessageParam { Content = (List<BetaContentBlockParam>)[.. shown, new BetaTextBlockParam { Text = question + again }] }],
                OutputConfig = new BetaOutputConfig { Effort = effort, Format = new BetaJsonOutputFormat { Schema = schema } },
            };
            // Set only when wanted: a null here would still be sent.
            if (fallback) request = request with { Betas = ["server-side-fallback-2026-07-01"], Fallbacks = new Default() };
            BetaMessage message;
            try
            {
                // Streamed, and put back together by the SDK: a long answer (thinking and all, tens of thousands of tokens) can
                // take longer than a request may wait for its whole reply, and a stream keeps coming the whole time.
                message = await client.Beta.Messages.CreateStreaming(request, ct).Aggregate();
            }
            catch (Exception e) when (Failure(e, ct) is { } failure)
            {
                await log.WriteAsync(new AssistCall(time.GetUtcNow(), task.Name, task.Version, task.Model, attempt, failure.Kind, failure.Summary,
                    question + again, "", attachments.Count, Usage.None, "", e.Message));
                return new(failure.Kind, default, failure.Summary, [], usage);
            }

            var served = message.Model.Raw();
            var cacheRead = message.Usage.CacheReadInputTokens ?? 0;
            var cacheWrite = message.Usage.CacheCreationInputTokens ?? 0;
            var cost = ClaudeModels.Cost(served, message.Usage.InputTokens, message.Usage.OutputTokens, cacheRead, cacheWrite);
            var used = new Usage(message.Usage.InputTokens, message.Usage.OutputTokens, cacheRead, cacheWrite, cost, time.GetElapsedTime(started));
            usage = usage.Plus(used);
            var text = string.Concat(message.Content.Select(block => block.TryPickText(out var t) ? t.Text : ""));
            Reply<TAnswer>? reply = null;
            var unreadable = "";
            if (message.StopReason != BetaStopReason.Refusal && message.StopReason != BetaStopReason.MaxTokens)
            {
                try { reply = JsonSerializer.Deserialize<Reply<TAnswer>>(text, AssistSchema.Json); }
                catch (JsonException e) { unreadable = e.Message; }
            }

            var problems = reply is { Status: ReplyStatus.Answered, Answer: { } given } ? task.Check(input, given) : [];
            Answer<TAnswer> answer = message.StopReason == BetaStopReason.Refusal ? new(AnswerKind.Refused, default, "Claude declined to answer this.", [], usage)
                : message.StopReason == BetaStopReason.MaxTokens ? new(AnswerKind.TooLong, default, $"The answer ran past {task.MaxTokens:N0} tokens.", [], usage)
                : reply is null ? new(AnswerKind.Failed, default, "Claude's reply didn't match the task's shape.", [unreadable], usage)
                : reply.Status == ReplyStatus.NeedMore ? new(AnswerKind.NeedMore, default, reply.Summary, reply.Notes, usage)
                : reply.Status == ReplyStatus.Cannot ? new(AnswerKind.Cannot, default, reply.Summary, reply.Notes, usage)
                : reply.Answer is null ? new(AnswerKind.Failed, default, "Claude said it answered, but sent no answer.", reply.Notes, usage)
                : problems.Count > 0 ? new(AnswerKind.CheckFailed, default, "Claude's answer didn't pass the task's checks.", problems, usage)
                : new(AnswerKind.Answered, reply.Answer, reply.Summary, reply.Notes, usage);
            await log.WriteAsync(new AssistCall(time.GetUtcNow(), task.Name, task.Version, served, attempt, answer.Kind, answer.Summary, question + again, text,
                attachments.Count, used, message.ID, unreadable));
            if (answer.Kind != AnswerKind.CheckFailed || attempt == 2) return answer;
            again = $"\n\nYour last answer to this question was:\n{text}\n\nIt had these problems:\n- {string.Join("\n- ", problems)}\n\nAnswer the whole question again, with them fixed.";
        }
    }

    public async Task<Answer<string>> TestAsync(CancellationToken ct = default)
    {
        var client = await ClientAsync();
        if (client is null) return new(AnswerKind.NoKey, null, NoKeySummary, [], Usage.None);
        try
        {
            var model = await client.Models.Retrieve(ClaudeModels.Opus, cancellationToken: ct);
            return new(AnswerKind.Answered, model.DisplayName, $"The key works, and {model.DisplayName} is ready.", [], Usage.None);
        }
        catch (Exception e) when (Failure(e, ct) is { } failure)
        {
            return new(failure.Kind, null, failure.Summary, [e.Message], Usage.None);
        }
    }

    /// <summary>The API client for the key on this device, made again when the key changes; none without a key.</summary>
    async Task<AnthropicClient?> ClientAsync()
    {
        var key = await keys.GetAsync();
        if (string.IsNullOrWhiteSpace(key)) return null;
        if (current is { } made && made.Key == key) return made.Client;
        var client = handler is null ? new AnthropicClient { ApiKey = key, MaxRetries = maxRetries }
            : new AnthropicClient { ApiKey = key, MaxRetries = maxRetries, HttpClient = new HttpClient(handler, disposeHandler: false) };
        current = (key, client);
        return client;
    }

    /// <summary>What a failed request means for the app; null for an error that isn't the API's or the network's, which is a bug and goes up.</summary>
    static (AnswerKind Kind, string Summary)? Failure(Exception e, CancellationToken ct) => e switch
    {
        AnthropicUnauthorizedException or AnthropicForbiddenException => (AnswerKind.KeyRejected, "Claude didn't accept the key on this device: it may be wrong, disabled or expired."),
        Anthropic.Models.AnthropicEnforcedSpendLimitReachedException => (AnswerKind.SpendLimit, "The spend limit for this key's workspace is reached."),
        AnthropicBadRequestException when e.Message.Contains("credit balance", StringComparison.OrdinalIgnoreCase) => (AnswerKind.OutOfCredit, "This month's API credits are used up."),
        AnthropicRateLimitException or Anthropic5xxException => (AnswerKind.Busy, "Claude is busy right now; try again in a minute."),
        AnthropicIOException or HttpRequestException => (AnswerKind.Offline, "Claude couldn't be reached: check the connection."),
        TaskCanceledException when !ct.IsCancellationRequested => (AnswerKind.Offline, "Claude took too long to answer."),
        AnthropicSseException => (AnswerKind.Busy, "Claude had a problem partway through its answer; try again in a minute."),
        AnthropicInvalidDataException => (AnswerKind.Failed, "Claude's answer came back incomplete."),
        AnthropicApiException => (AnswerKind.Failed, $"The request failed: {e.Message}"),
        _ => null,
    };
}
