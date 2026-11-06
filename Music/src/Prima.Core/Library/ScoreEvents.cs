using Prima.Core.Music;

namespace Prima.Core.Library;

/// <summary>One moment of a score: the notes that start together, when (in quarter notes from the first full bar), and their bar.</summary>
public sealed record ScoreEvent(int Index, double Beat, IReadOnlyList<int> Pitches, IReadOnlyList<string> Ids, int Bar, bool BarStart);

public static class ScoreEvents
{
    /// <summary>The score's onsets in order, chords grouped; tied continuations and grace notes aren't played, so aren't here.</summary>
    public static IReadOnlyList<ScoreEvent> From(Score score)
    {
        var groups = score.Expected().GroupBy(n => n.Tick).OrderBy(g => g.Key).ToList();
        var events = new List<ScoreEvent>();
        var lastBar = int.MinValue;
        foreach (var g in groups)
        {
            var bar = g.Min(n => n.Bar);
            events.Add(new ScoreEvent(events.Count, g.Key / (double)Ticks.Quarter, [.. g.Select(n => n.Midi).Distinct().Order()], [.. g.Select(n => n.Id)], bar, bar != lastBar));
            lastBar = bar;
        }
        return events;
    }
}
