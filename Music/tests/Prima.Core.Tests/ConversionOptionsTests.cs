using Playground.AI.Assist;
using Prima.Core.Transcription;

namespace Prima.Core.Tests;

public class ConversionOptionsTests
{
    [Fact]
    public void FiftyBarsHaveEnoughAllowanceForEveryBranchOfTheSplitTree()
    {
        var source = new ConversionSource(new string('a', 64), "source-r1", Enumerable.Range(0, 50)
            .Select(i => new SourceBar($"b{i}", 1, 1, i, (i + 1).ToString(), new(i / 50d, 0, 1 / 50d, 0.5))).ToArray(), true);
        var options = ConversionOptions.ForSource("provider", new InferenceSettings("model"), source);
        var pending = new Queue<ConversionChunk>(ConversionChunks.Plan(source, options.Chunks));
        var planned = pending.Count;
        var requests = 0;
        var leaves = new List<string>();
        while (pending.TryDequeue(out var chunk))
        {
            requests++;
            if (chunk.BarIds.Count == 1) leaves.Add(chunk.BarIds[0]);
            else foreach (var child in ConversionChunks.Split(chunk)) pending.Enqueue(child);
        }

        Assert.Equal(2 * 50 - planned, requests);
        Assert.Equal(requests, options.MaxRequests);
        Assert.Equal(50, leaves.Distinct().Count());
        Assert.Equal((long)requests * options.Chunks.TargetOutputTokens, options.OutputTokenBudget);
        Assert.True(options.OutputTokenBudget > 96_000);
        var recheck = ConversionOptions.ForSource("provider", new InferenceSettings("model"), source, targetBarIds: ["b2", "b40"]);
        Assert.Equal(1, recheck.Chunks.MaxBars);
        Assert.Equal(2, recheck.MaxRequests);
        Assert.Equal(24_000, recheck.OutputTokenBudget);
    }
}
