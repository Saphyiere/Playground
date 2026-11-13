using Playground.AI.Assist;
using Prima.Core.Transcription;

namespace Prima.UI.Transcription;

/// <summary>
/// A PDF's reading kept on its piece between visits (PieceReading, as JSON in the framework's form): all a
/// <see cref="ReadingResult"/> holds but its score, which is built again from the readings, the pages it covered, and what stopped it.
/// </summary>
public sealed record ReadingDraft(bool Ok, string Summary, string Mei, IReadOnlyList<Doubt> Doubts, IReadOnlyList<SystemStatus> Systems, decimal Cost, TimeSpan Took,
    IReadOnlyList<SystemReading?> Readings, int FromPage, int ToPage, AnswerKind? StoppedBy = null, string? SourceRevision = null,
    string? ConversionJobId = null, int? ConversionRevision = null, bool CostKnown = true)
{
    public ReadingResult ToResult() => new(Ok, Summary, null, Mei, Doubts, Systems, Cost, Took) { Readings = Readings, StoppedBy = StoppedBy };

    public static ReadingDraft Of(ReadingResult result, int fromPage, int toPage) =>
        new(result.Ok, result.Summary, result.Mei, result.Doubts, result.Systems, result.Cost, result.Took, result.Readings, fromPage, toPage, result.StoppedBy);

    /// <summary>Rejoins completed chunks by their owned source identities, then restores each printed system's local indexes.</summary>
    public static ReadingDraft FromJob(ConversionJob job)
    {
        if (job.IsRecheck || job.State != ConversionState.Ready || job.Chunks.Any(c => c.Phase != ConversionPhase.Complete || c.Reading is null))
            throw new ArgumentException("The conversion is not ready for review.", nameof(job));
        var owned = new Dictionary<string, (ReadBar Bar, List<ReadDoubt> Doubts)>();
        foreach (var chunk in job.Chunks)
        {
            var reading = chunk.Reading!;
            if (reading.Bars.Count != chunk.Chunk.BarIds.Count) throw new ArgumentException("A chunk does not cover its source bars.", nameof(job));
            for (var i = 0; i < reading.Bars.Count; i++)
                owned.Add(chunk.Chunk.BarIds[i], (reading.Bars[i], reading.Doubts.Where(d => d.Bar == i + 1).ToList()));
        }
        if (owned.Count != job.Source.Bars.Count || job.Source.Bars.Any(b => !owned.ContainsKey(b.Id)))
            throw new ArgumentException("The conversion does not cover its source exactly once.", nameof(job));

        var groups = job.Source.Bars.OrderBy(b => b.Order).GroupBy(b => (b.Page, b.System)).ToArray();
        var systems = groups.Select(group => new SystemReading(
            group.Select((bar, index) => owned[bar.Id].Bar with { Index = index + 1 }).ToArray(),
            group.SelectMany((bar, index) => owned[bar.Id].Doubts.Select(d => d with { Bar = index + 1 })).ToArray())).ToArray();
        var (score, starts) = ReadingScore.Build(systems, job.Title);
        var doubts = systems.SelectMany((reading, index) => reading.Doubts.Select(d =>
            new Doubt(groups[index].Key.Page, groups[index].Key.System, starts[index] + d.Bar - 1, d.Staff, d.Note) { BarInSystem = d.Bar })).ToArray();
        var statuses = groups.Select((group, index) => new SystemStatus(group.Key.Page, group.Key.System,
            systems[index].Doubts.Count == 0 ? SystemStep.Done : SystemStep.Doubtful, "")).ToArray();
        var took = TimeSpan.FromTicks(job.Attempts.Sum(a => a.Usage.Took.Ticks));
        return new ReadingDraft(true, "", job.Mei ?? ReadingScore.Mei(score), doubts, statuses, job.KnownCost, took, systems,
            groups.Min(g => g.Key.Page), groups.Max(g => g.Key.Page), SourceRevision: job.Source.Revision, ConversionJobId: job.Id, ConversionRevision: job.Revision,
            CostKnown: !job.HasUnknownCost);
    }

    /// <summary>The current human/model review, rebuilt in immutable source order for preview, playback and Keep.</summary>
    public ReadingDraft WithReview(NotationReview review, string title = "")
    {
        if (SourceRevision != review.Source.Revision) throw new ArgumentException("The review belongs to a different source revision.", nameof(review));
        var current = review.Current.Bars.ToDictionary(b => b.SourceBarId);
        var groups = review.Source.Bars.OrderBy(b => b.Order).GroupBy(b => (b.Page, b.System)).ToArray();
        var readings = groups.Select(group => new SystemReading(
            group.Select((bar, index) => (current[bar.Id].Written ?? throw new ArgumentException("The review has an unread bar.", nameof(review))) with { Index = index + 1 }).ToArray(),
            group.SelectMany((bar, index) =>
            {
                var state = current[bar.Id];
                var doubts = state.Accepted ? [] : state.ModelDoubts.Select(d => new ReadDoubt(index + 1, d.Staff ?? 0, d.Note));
                return doubts.Concat(state.UserFlags.Select(f => new ReadDoubt(index + 1, 0, f.Note)));
            }).ToArray())).ToArray();
        var (score, starts) = ReadingScore.Build(readings, title);
        var doubts = readings.SelectMany((reading, index) => reading.Doubts.Select(d =>
            new Doubt(groups[index].Key.Page, groups[index].Key.System, starts[index] + d.Bar - 1, d.Staff, d.Note) { BarInSystem = d.Bar })).ToArray();
        return this with
        {
            Mei = ReadingScore.Mei(score), Readings = readings, Doubts = doubts,
            Systems = groups.Select((group, index) => new SystemStatus(group.Key.Page, group.Key.System,
                readings[index].Doubts.Count == 0 ? SystemStep.Done : SystemStep.Doubtful, "")).ToArray(),
        };
    }
}
