using Prima.Core.Music;

namespace Prima.Core.Library;

/// <summary>A stretch of a piece for the sight-reading track: its bars (as numbered in the piece) and its grade.</summary>
public sealed record ExcerptCut(int FirstBar, int LastBar, Grade Grade)
{
    public int Bars => LastBar - Math.Max(1, FirstBar) + 1;
}

/// <summary>
/// Cuts a piece into 2-, 4- and 8-bar excerpts at good places (library.md §3): only at barlines, starting where a phrase can
/// start (the piece's first bar, every fourth bar from it, or after a rest or a long note), never in the middle of a tie or a
/// slur, and never a stretch that repeats one already cut.
/// </summary>
public static class Excerpts
{
    public static readonly int[] Lengths = [2, 4, 8];

    /// <summary>
    /// A piece's grade from its excerpts: its level is its hardest excerpt's, most of it reads at their median, and the reasons are
    /// the hardest ones' commonest. A piece too short to cut is graded whole.
    /// </summary>
    public static (int Level, int Typical, string Reasons) PieceGrade(Score score, IReadOnlyList<ExcerptCut> cuts)
    {
        var whole = cuts.Count == 0 ? Grader.Grade(score) : null;
        var level = whole?.Level ?? cuts.Max(c => c.Grade.Level);
        var typical = whole?.Level ?? cuts.Select(c => c.Grade.Level).Order().ElementAt(cuts.Count / 2);
        var reasons = whole?.Reasons ?? [.. cuts.Where(c => c.Grade.Level == level).SelectMany(c => c.Grade.Reasons).GroupBy(r => r).OrderByDescending(g => g.Count()).Select(g => g.Key).Take(3)];
        return (level, typical, string.Join(", ", reasons));
    }

    public static IReadOnlyList<ExcerptCut> Cut(Score score)
    {
        var bars = score.Bars.Where(b => b.Number > 0).ToList();
        if (bars.Count == 0) return [];

        var fingerprints = bars.ToDictionary(b => b.Number, Fingerprint);
        var starts = bars.Where((b, i) => i == 0 || (b.Number - bars[0].Number) % 4 == 0 || EndsPhrase(bars[i - 1])).Select(b => b.Number).ToHashSet();
        var tiedInto = bars.Where(b => b.Voices.Any(v => v.Events.OfType<Notes>().Any(n => n.Start == 0 && n.Tones.Any(t => t.TiedFromPrevious)))).Select(b => b.Number).ToHashSet();
        var slurredInto = SlurredInto(score);

        var cuts = new List<ExcerptCut>();
        foreach (var length in Lengths)
        {
            var seen = new HashSet<string>();
            var next = bars[0].Number;
            foreach (var bar in bars)
            {
                var first = bar.Number;
                var last = first + length - 1;
                if (first < next || !starts.Contains(first) || tiedInto.Contains(first) || slurredInto.Contains(first) || !fingerprints.ContainsKey(last)) continue;

                // The same bars again (a repeat written out) aren't cut twice; a stretch with too little in it isn't cut at all.
                var content = string.Join('|', Enumerable.Range(first, length).Select(n => fingerprints.GetValueOrDefault(n, "")));
                var notes = Enumerable.Range(first, length).Sum(n => bars.FirstOrDefault(b => b.Number == n)?.Voices.Sum(v => v.Events.OfType<Notes>().Count()) ?? 0);
                if (notes < 2 * length || !seen.Add(content)) continue;

                // A cut at bar 1 takes the pickup with it.
                var from = first == bars[0].Number && score.Bars[0].Number == 0 ? 0 : first;
                cuts.Add(new ExcerptCut(from, last, Grader.Grade(score.Excerpt(from, last))));
                next = last + 1;
            }
        }
        return cuts;
    }

    // A bar's music, as text: each voice's onsets, lengths and pitches.
    private static string Fingerprint(Bar bar) => string.Join(';', bar.Voices.OrderBy(v => v.Staff).ThenBy(v => v.Layer).Select(v =>
        string.Join(',', v.Events.Select(e => e switch
        {
            Notes n => $"{n.Start}:{n.Length}:{string.Join('.', n.Tones.Select(t => t.Pitch.Midi))}",
            Rest r => $"{r.Start}:{r.Length}:r",
            _ => "",
        }))));

    // A bar that ends a phrase: its top voice ends on a rest, or on a note a quarter or longer that isn't tied on.
    private static bool EndsPhrase(Bar bar)
    {
        var top = bar.Voices.Where(v => v.Staff == bar.Voices.Min(x => x.Staff)).OrderBy(v => v.Layer).FirstOrDefault();
        var last = top?.Events.Where(e => e is Notes or Rest).MaxBy(e => e.Start);
        return last is Rest || last is Notes n && n.Length >= Ticks.Quarter && n.Tones.All(t => !t.TieToNext);
    }

    // Bars that a slur runs into from the bar before: a cut can't start there.
    private static HashSet<int> SlurredInto(Score score)
    {
        var barOf = new Dictionary<string, int>();
        foreach (var bar in score.Bars)
        {
            foreach (var n in bar.Voices.SelectMany(v => v.Events.OfType<Notes>()))
            {
                barOf[n.Id] = bar.Number;
                foreach (var t in n.Tones) barOf[t.Id] = bar.Number;
            }
        }

        var into = new HashSet<int>();
        foreach (var slur in score.Marks.Where(m => m.Kind == MarkKind.Slur && m.EndId is not null))
        {
            if (!barOf.TryGetValue(slur.StartId, out var from) || !barOf.TryGetValue(slur.EndId!, out var to)) continue;
            for (var b = from + 1; b <= to; b++) into.Add(b);
        }
        return into;
    }
}
