using Microsoft.Extensions.DependencyInjection;

namespace Playground.AI.Assist.Tests;

public class ProviderTests
{
    private const string Good = """{"status":"answered","summary":"Done.","notes":[],"answer":{"lifts":[{"exerciseId":12,"weight":105,"reps":10,"change":"up","why":"Measured history"}]}}""";
    private const string Invalid = """{"status":"answered","summary":"Done.","notes":[],"answer":{"lifts":[{"exerciseId":99,"weight":105,"reps":10,"change":"up","why":"Unowned exercise"}]}}""";
    private readonly FakeApi api = new();
    private readonly MemoryAssistLog log = new();

    private IInferenceProvider Provider() => new Claude(new MemoryKeys("test-api-key"), log, TimeProvider.System, api, maxRetries: 0);

    private sealed class LegacyClaude : IClaude, IServiceProvider
    {
        public Task<Answer<TAnswer>> AskAsync<TInput, TAnswer>(AssistTask<TInput, TAnswer> task, TInput input, CancellationToken ct = default) =>
            Task.FromResult(new Answer<TAnswer>(AnswerKind.NoKey, default, "", [], Usage.None));

        public Task<Answer<string>> TestAsync(CancellationToken ct = default) =>
            Task.FromResult(new Answer<string>(AnswerKind.NoKey, null, "", [], Usage.None));

        public object? GetService(Type type) => type == typeof(IClaude) ? this : null;
    }

    [Fact]
    public async Task LegacyConsumersRemainUsableWithoutClaimingCapabilities()
    {
        IClaude legacy = new LegacyClaude();
        IInferenceProvider neutral = legacy;

        Assert.Equal(AnswerKind.NoKey, (await neutral.AskAsync(new NextLifts(), new History([12]), TestContext.Current.CancellationToken)).Kind);
        Assert.Equal(AnswerKind.NoKey, (await legacy.TestAsync(TestContext.Current.CancellationToken)).Kind);
        Assert.Null(neutral.Capabilities.Images);
        Assert.Null(neutral.Capabilities.DurableRetrieval);
    }

    [Fact]
    public void RegistrationAliasesTheExistingClaudeInsteadOfCreatingAnotherProvider()
    {
        var legacy = new LegacyClaude();
        var services = new ServiceCollection();
        services.AddSingleton<IClaude>(legacy);
        services.AddAiAssist();

        var descriptor = Assert.Single(services, d => d.ServiceType == typeof(IInferenceProvider));
        Assert.Same(legacy, descriptor.ImplementationFactory!(legacy));
        Assert.Same(legacy, Assert.Single(services, d => d.ServiceType == typeof(IClaude)).ImplementationInstance);
    }

    [Fact]
    public void CapabilitiesDescribeImplementedOperationsAndUnknownModelLimits()
    {
        var provider = Provider();

        Assert.Equal("anthropic", provider.ProviderId);
        Assert.True(provider.Capabilities.Images);
        Assert.True(provider.Capabilities.StructuredOutput);
        Assert.True(provider.Capabilities.Streaming);
        Assert.True(provider.Capabilities.Cancellation);
        Assert.False(provider.Capabilities.TokenCounting);
        Assert.False(provider.Capabilities.DurableRetrieval);
        Assert.Null(provider.Capabilities.MaxInputTokens);
        Assert.Null(provider.Capabilities.MaxOutputTokens);
    }

    [Fact]
    public async Task ExecutionOverridesLegacyTaskDefaultsAndRetainsResponseIdentity()
    {
        api.Reply(Good, model: ClaudeModels.Sonnet);
        var settings = new InferenceSettings(Model: ClaudeModels.Sonnet, Effort: EffortLevel.Low, MaxOutputTokens: 4000, AllowModelFallback: false);

        var answer = await Provider().AskAsync(new NextLifts(settings: settings), new History([12]), TestContext.Current.CancellationToken);

        var request = Assert.Single(api.Requests).Body;
        Assert.Equal(ClaudeModels.Sonnet, request.GetProperty("model").GetString());
        Assert.Equal(4000, request.GetProperty("max_tokens").GetInt32());
        Assert.Equal("low", request.GetProperty("output_config").GetProperty("effort").GetString());
        Assert.False(request.TryGetProperty("fallbacks", out _));
        Assert.True(request.GetProperty("stream").GetBoolean());
        Assert.NotNull(answer.Response);
        Assert.Equal(ClaudeModels.Sonnet, answer.Response.RequestedModel);
        Assert.Equal(ClaudeModels.Sonnet, answer.Response.ServedModel);
        Assert.Equal(InferenceFinish.Completed, answer.Response.Finish);
        Assert.NotEmpty(answer.Response.ResponseId);
        Assert.Equal(Good, answer.Response.Text);
        Assert.True(answer.Usage.CostKnown);
    }

    [Fact]
    public async Task ModelIdentifiersRemainProviderOwnedStrings()
    {
        const string custom = "custom/provider-model";
        api.Reply(Good, model: custom);

        var answer = await Provider().AskAsync(new NextLifts(settings: new(Model: custom)), new History([12]), TestContext.Current.CancellationToken);

        Assert.True(answer.Ok);
        Assert.Equal(custom, Assert.Single(api.Requests).Body.GetProperty("model").GetString());
        Assert.Equal(custom, answer.Response!.ServedModel);
        Assert.False(answer.Usage.CostKnown);
        Assert.False(ClaudeModels.HasPrice(custom));
        Assert.Equal(0, answer.Usage.Cost);
    }

    [Fact]
    public void CombiningUsageRetainsThePricedSubtotalAndAnyUnknownPrice()
    {
        var known = new Usage(1, 2, 0, 0, 0.12m, TimeSpan.FromSeconds(1));
        var unknown = new Usage(3, 4, 0, 0, 0, TimeSpan.FromSeconds(2)) { CostKnown = false };

        var combined = known.Plus(unknown);

        Assert.Equal(0.12m, combined.Cost);
        Assert.Equal(6, combined.OutputTokens);
        Assert.False(combined.CostKnown);
        Assert.False(unknown.Plus(known).CostKnown);
        Assert.True(Usage.None.Plus(known).CostKnown);
        var legacy = System.Text.Json.JsonSerializer.Deserialize<Usage>("""{"inputTokens":1,"outputTokens":2,"cacheReadTokens":0,"cacheWriteTokens":0,"cost":0.12,"took":"00:00:01"}""", AssistSchema.Json);
        Assert.True(legacy!.CostKnown);
    }

    [Fact]
    public async Task CoordinatorCanDisableAutomaticValidationRepairs()
    {
        api.Reply(Invalid);

        var answer = await Provider().AskAsync(new NextLifts(settings: new(ValidationRetries: 0)), new History([12]), TestContext.Current.CancellationToken);

        Assert.Equal(AnswerKind.CheckFailed, answer.Kind);
        Assert.Single(api.Requests);
        Assert.Single(log.Calls);
        Assert.Equal(InferenceFinish.Completed, answer.Response!.Finish);
    }

    [Fact]
    public async Task OutputLimitIsNormalizedWithoutAcceptingPartialStructuredData()
    {
        const string partial = "{\"status\":\"answ";
        api.Reply(partial, stopReason: "max_tokens");

        var answer = await Provider().AskAsync(new NextLifts(), new History([12]), TestContext.Current.CancellationToken);

        Assert.Equal(AnswerKind.TooLong, answer.Kind);
        Assert.False(answer.Ok);
        Assert.Null(answer.Value);
        Assert.Equal(InferenceFinish.OutputLimit, answer.Response!.Finish);
        Assert.Equal("max_tokens", answer.Response.RawFinishReason);
        Assert.Equal(partial, answer.Response.Text);
        Assert.Equal(500, answer.Usage.OutputTokens);
    }

    [Fact]
    public async Task NeutralProviderPropagatesCallerCancellation()
    {
        api.Reply(Good);
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Provider().AskAsync(new NextLifts(), new History([12]), cancelled.Token));
        Assert.Empty(log.Calls);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void NonpositiveOutputLimitsAreRejectedBeforeDispatch(int limit) =>
        Assert.Throws<ArgumentOutOfRangeException>(() => new NextLifts(settings: new(MaxOutputTokens: limit)));
}
