using Prima.Core.Transcription;

namespace Prima.Core.Tests;

public class ConversionSourceTests
{
    [Fact]
    public void RepeatedDisplayNumbersDoNotConfuseOwnershipOrContext()
    {
        var bars = Enumerable.Range(0, 6).Select(i => new SourceBar($"source-{i}", 1, i / 3 + 1, i, "1",
            new SourceRectangle(i % 3 / 3.0, i / 3 * 0.4, 1 / 3.0, 0.3))).ToArray();
        var source = new ConversionSource(new string('a', 64), "segmentation-1", bars, true);

        var chunks = ConversionChunks.Plan(source, new ChunkLimits(MaxBars: 4));

        Assert.Equal(2, chunks.Count);
        Assert.Equal(bars.Select(b => b.Id), chunks.SelectMany(c => c.BarIds));
        Assert.Null(chunks[0].ContextBefore);
        Assert.Equal("source-2", chunks[1].ContextBefore);
        Assert.All(chunks, c => Assert.Equal(source.Revision, c.SourceRevision));
    }

    [Fact]
    public void DenseBarsReduceChunkSizeWithinTheReservedOutputAllowance()
    {
        var bars = Enumerable.Range(0, 4).Select(i => new SourceBar($"b{i}", 1, 1, i, $"{i + 1}", new(i * 0.25, 0, 0.25, 0.5))).ToArray();
        var source = new ConversionSource(new string('b', 64), "r1", bars, true);
        var chunks = ConversionChunks.Plan(source, new ChunkLimits(TargetOutputTokens: 9_000), new Dictionary<string, int> { ["b1"] = 6_000 });

        Assert.Equal(new[] { 2, 2 }, chunks.Select(c => c.BarIds.Count));
        Assert.All(chunks, c => Assert.InRange(c.EstimatedOutputTokens, 1, 9_000));
        Assert.Equal("b1", chunks[1].ContextBefore);
    }

    [Fact]
    public void UncertainOverlappingOrDuplicateSourceMapsCannotDispatch()
    {
        var bars = new[] { new SourceBar("same", 1, 1, 0, "1", new(0, 0, 0.7, 0.5)), new SourceBar("same", 1, 1, 1, "2", new(0.6, 0, 0.4, 0.5)) };
        var source = new ConversionSource(new string('c', 64), "r1", bars, false);

        Assert.Equal(3, source.Problems().Count);
        Assert.Throws<ArgumentException>(() => ConversionChunks.Plan(source, new ChunkLimits()));
    }

    [Fact]
    public void TruncatedChunkSplitsWithoutChangingOwnershipOrReplayingNeighbours()
    {
        var chunk = new ConversionChunk("original", "r2", ["b5", "b6", "b7"], "b4", 10_000);

        var split = ConversionChunks.Split(chunk);

        Assert.Equal(chunk.BarIds, split.SelectMany(c => c.BarIds));
        Assert.Equal("b4", split[0].ContextBefore);
        Assert.Equal("b5", split[1].ContextBefore);
        Assert.All(split, c => Assert.NotEqual(chunk.Id, c.Id));
        Assert.Equal(2, split.Select(c => c.Id).Distinct().Count());
    }

    [Fact]
    public void SingleBarOverBudgetRequiresAnExplicitDecision()
    {
        var source = new ConversionSource(new string('d', 64), "r1", [new("b1", 1, 1, 0, "1", new(0, 0, 1, 0.5))], true);
        Assert.Throws<InvalidOperationException>(() => ConversionChunks.Plan(source, new ChunkLimits(), new Dictionary<string, int> { ["b1"] = 20_000 }));
        Assert.Throws<InvalidOperationException>(() => ConversionChunks.Split(new("c1", "r1", ["b1"], null, 20_000)));
    }
}
