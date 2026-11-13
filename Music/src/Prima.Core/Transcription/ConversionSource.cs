namespace Prima.Core.Transcription;

/// <summary>A rectangle in original-page coordinates, independent of zoom and rendering resolution.</summary>
public sealed record SourceRectangle(double X, double Y, double Width, double Height)
{
    public bool IsValid => double.IsFinite(X) && double.IsFinite(Y) && double.IsFinite(Width) && double.IsFinite(Height) &&
        X >= 0 && Y >= 0 && Width > 0 && Height > 0 && X + Width <= 1.000001 && Y + Height <= 1.000001;
}

/// <summary>An owned printed bar. Its identity does not depend on a model's output or the printed bar number.</summary>
public sealed record SourceBar(string Id, int Page, int System, int Order, string DisplayNumber, SourceRectangle Bounds);

/// <summary>A reviewed segmentation of one immutable PDF. A changed segmentation is a new revision.</summary>
public sealed record ConversionSource(string Sha256, string Revision, IReadOnlyList<SourceBar> Bars, bool Confirmed)
{
    public IReadOnlyList<string> Problems()
    {
        var problems = new List<string>();
        if (Sha256.Length != 64 || Sha256.Any(c => !char.IsAsciiHexDigit(c))) problems.Add("The source PDF hash is invalid.");
        if (string.IsNullOrWhiteSpace(Revision)) problems.Add("The source segmentation has no revision.");
        if (!Confirmed) problems.Add("Confirm the source bar boundaries before conversion.");
        if (Bars.Count == 0) problems.Add("The source has no bars.");
        var ids = new HashSet<string>(StringComparer.Ordinal);
        for (var i = 0; i < Bars.Count; i++)
        {
            var bar = Bars[i];
            if (string.IsNullOrWhiteSpace(bar.Id) || !ids.Add(bar.Id)) problems.Add($"Source bar {i + 1} has a missing or duplicate identity.");
            if (bar.Order != i || bar.Page < 1 || bar.System < 1 || !bar.Bounds.IsValid) problems.Add($"Source bar {bar.Id} has invalid position data.");
            if (i == 0) continue;
            var before = Bars[i - 1];
            if (bar.Page < before.Page || bar.Page == before.Page && bar.System < before.System) problems.Add("Source bars are not in written order.");
            if (bar.Page == before.Page && bar.System == before.System && bar.Bounds.X < before.Bounds.X + before.Bounds.Width - 0.000001)
                problems.Add($"Source bars {before.Id} and {bar.Id} overlap or are out of order.");
        }
        return problems;
    }
}

/// <summary>A complete-bar request, with stable ownership and a separate preceding context bar.</summary>
public sealed record ConversionChunk(string Id, string SourceRevision, IReadOnlyList<string> BarIds, string? ContextBefore, int EstimatedOutputTokens);

public sealed record ChunkLimits(int MaxBars = 4, int TargetOutputTokens = 12_000, int DefaultTokensPerBar = 2_000, int ContextTokens = 1_000);

public static class ConversionChunks
{
    /// <summary>Plans in written order; never crosses a printed system or spends the whole output allowance on owned bars.</summary>
    public static IReadOnlyList<ConversionChunk> Plan(ConversionSource source, ChunkLimits limits, IReadOnlyDictionary<string, int>? estimatedTokens = null)
    {
        var problems = source.Problems();
        if (problems.Count > 0) throw new ArgumentException(string.Join(" ", problems), nameof(source));
        if (limits.MaxBars < 1 || limits.DefaultTokensPerBar < 1 || limits.ContextTokens < 0 || limits.TargetOutputTokens <= limits.ContextTokens)
            throw new ArgumentOutOfRangeException(nameof(limits));

        var chunks = new List<ConversionChunk>();
        for (var at = 0; at < source.Bars.Count;)
        {
            var start = at;
            var tokens = limits.ContextTokens;
            var owned = new List<string>();
            while (at < source.Bars.Count && owned.Count < limits.MaxBars)
            {
                var bar = source.Bars[at];
                if (bar.Page != source.Bars[start].Page || bar.System != source.Bars[start].System) break;
                var estimate = estimatedTokens?.GetValueOrDefault(bar.Id, limits.DefaultTokensPerBar) ?? limits.DefaultTokensPerBar;
                if (estimate < 1) throw new ArgumentOutOfRangeException(nameof(estimatedTokens));
                if (estimate > limits.TargetOutputTokens - limits.ContextTokens)
                    throw new InvalidOperationException($"Source bar {bar.Id} exceeds the single-bar output allowance; it needs review or a larger explicit allowance.");
                if (tokens + estimate > limits.TargetOutputTokens) break;
                owned.Add(bar.Id);
                tokens += estimate;
                at++;
            }
            chunks.Add(new ConversionChunk(Guid.NewGuid().ToString("N"), source.Revision, owned, start > 0 ? source.Bars[start - 1].Id : null, tokens));
        }
        return chunks;
    }

    /// <summary>Splits only a request that reached its output limit. Completed neighbours retain their identities and artifacts.</summary>
    public static IReadOnlyList<ConversionChunk> Split(ConversionChunk chunk)
    {
        if (chunk.BarIds.Count < 2) throw new InvalidOperationException("A single bar reached the output limit and needs review.");
        var middle = chunk.BarIds.Count / 2;
        return [
            new(Guid.NewGuid().ToString("N"), chunk.SourceRevision, chunk.BarIds.Take(middle).ToArray(), chunk.ContextBefore, chunk.EstimatedOutputTokens),
            new(Guid.NewGuid().ToString("N"), chunk.SourceRevision, chunk.BarIds.Skip(middle).ToArray(), chunk.BarIds[middle - 1], chunk.EstimatedOutputTokens)
        ];
    }
}
