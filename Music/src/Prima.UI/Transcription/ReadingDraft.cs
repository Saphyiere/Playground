using Playground.AI.Assist;
using Prima.Core.Transcription;

namespace Prima.UI.Transcription;

/// <summary>
/// A PDF's reading kept on its piece between visits (PieceReading, as JSON in the framework's form): all a
/// <see cref="ReadingResult"/> holds but its score, which is built again from the readings, the pages it covered, and what stopped it.
/// </summary>
public sealed record ReadingDraft(bool Ok, string Summary, string Mei, IReadOnlyList<Doubt> Doubts, IReadOnlyList<SystemStatus> Systems, decimal Cost, TimeSpan Took,
    IReadOnlyList<SystemReading?> Readings, int FromPage, int ToPage, AnswerKind? StoppedBy = null)
{
    public ReadingResult ToResult() => new(Ok, Summary, null, Mei, Doubts, Systems, Cost, Took) { Readings = Readings, StoppedBy = StoppedBy };

    public static ReadingDraft Of(ReadingResult result, int fromPage, int toPage) =>
        new(result.Ok, result.Summary, result.Mei, result.Doubts, result.Systems, result.Cost, result.Took, result.Readings, fromPage, toPage, result.StoppedBy);
}
