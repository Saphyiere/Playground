namespace Playground.AI.Assist.Tests;

/// <summary>
/// Asks the real API, when PLAYGROUND_CLAUDE_KEY holds a Claude API key, and is skipped otherwise: that the key works, and that
/// the API takes the framework's request and schema and answers in them, on Haiku and on Opus with its fallback. A few cents a run.
/// </summary>
public class LiveTests
{
    const string Skip = "Set PLAYGROUND_CLAUDE_KEY to a Claude API key to ask the real API.";
    static readonly string? Key = Environment.GetEnvironmentVariable("PLAYGROUND_CLAUDE_KEY");

    /// <summary>The trainer's question in miniature, with a picture so the request carries an image too.</summary>
    sealed class NextLiftsFromHistory(string model) : AssistTask<History, Plan>
    {
        // A white PNG, 2 × 2 pixels.
        static readonly byte[] Blank = Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAIAAAACCAIAAAD91JpzAAAAFklEQVR4nGP8//8/AwMDEwMDAwMDAwAkBgMB/DXemwAAAABJRU5ErkJggg==");

        public override string Name => "test.live-next-lifts";

        public override int Version => 1;

        public override string Model => model;

        public override EffortLevel Effort => EffortLevel.Low;

        public override int MaxTokens => 8_000;

        public override string Instructions => """
            Exercise 12 is the chest press: last time 100 lb for 10, 10, 10. Exercise 14 is rows: last time 120 lb for 10, 9, 8.
            The owner's rule: when every set hit 10 reps, go up 5 lb next time; otherwise stay at the same weight. Give the next
            session's lift for each exercise in the input: 3 sets is implied, so give the weight, the reps to aim for (10), the
            change, and a short why. The image is blank; ignore it.
            """;

        public override IReadOnlyList<Attachment> Attachments(History input) => [Attachment.Png(Blank, "A blank image")];

        public override IReadOnlyList<string> Check(History input, Plan answer) =>
            [.. answer.Lifts.Where(l => !input.ExerciseIds.Contains(l.ExerciseId)).Select(l => $"Exercise {l.ExerciseId} isn't in the input.")];
    }

    static Claude Client() => new(new MemoryKeys(Key), new MemoryAssistLog(), TimeProvider.System);

    [Fact]
    public async Task TestAsync_TheKeyWorks()
    {
        Assert.SkipWhen(Key is null, Skip);

        var answer = await Client().TestAsync(TestContext.Current.CancellationToken);

        Assert.True(answer.Ok, answer.Summary);
    }

    [Theory]
    [InlineData(ClaudeModels.Haiku)]
    [InlineData(ClaudeModels.Opus)]
    public async Task AskAsync_TheApiTakesTheRequestAndAnswersInItsSchema(string model)
    {
        Assert.SkipWhen(Key is null, Skip);

        var answer = await Client().AskAsync(new NextLiftsFromHistory(model), new History([12, 14]), TestContext.Current.CancellationToken);

        Assert.True(answer.Ok, $"{answer.Kind}: {answer.Summary} {string.Join(" ", answer.Notes)}");
        Assert.Equal(105, answer.Value.Lifts.Single(l => l.ExerciseId == 12).Weight);
        Assert.Equal(120, answer.Value.Lifts.Single(l => l.ExerciseId == 14).Weight);
        Assert.True(answer.Usage.Cost > 0);
    }
}
