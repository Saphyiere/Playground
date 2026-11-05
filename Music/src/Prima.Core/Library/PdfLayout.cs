namespace Prima.Core.Library;

/// <summary>A system on a PDF page, in pixels from the top of the page as drawn, and the bars it holds (set by the owner, or guessed).</summary>
public sealed record PageSystem(double Top, double Bottom, int Bars);

/// <summary>
/// Finding a PDF page's systems without reading its notes (library.md §5): a row of pixels is a staff line when more than half of
/// it is dark; five evenly spaced lines are a staff; and staves are paired two by two into grand staves, since the gap inside a
/// grand staff is too close to the gap between systems to tell them apart.
/// </summary>
public static class PdfLayout
{
    /// <param name="darkShare">For each row of the page as drawn, the share of its pixels that are dark (0 to 1).</param>
    /// <param name="barsPerSystem">The bars guessed for each system until the owner sets them.</param>
    public static IReadOnlyList<PageSystem> FindSystems(IReadOnlyList<double> darkShare, int barsPerSystem = 4)
    {
        // Runs of dark rows are lines; each line is its run's middle.
        var lines = new List<double>();
        for (var i = 0; i < darkShare.Count; i++)
        {
            if (darkShare[i] <= 0.5) continue;

            var start = i;
            while (i + 1 < darkShare.Count && darkShare[i + 1] > 0.5) i++;
            lines.Add((start + i) / 2.0);
        }

        // Staves: five lines in a row whose gaps are all about the same.
        var staves = new List<(double Top, double Bottom, double Space)>();
        for (var i = 0; i + 4 < lines.Count;)
        {
            var gaps = Enumerable.Range(i, 4).Select(k => lines[k + 1] - lines[k]).ToList();
            var space = gaps.Average();
            if (space > 0 && gaps.All(g => Math.Abs(g - space) <= Math.Max(1.5, 0.2 * space)))
            {
                staves.Add((lines[i], lines[i + 4], space));
                i += 5;
            }
            else i++;
        }

        // Two by two into grand staves; a staff left over stands on its own. Each system takes the room above and below its staves,
        // up to halfway to its neighbours, so notes on ledger lines stay with it.
        var pairs = new List<(double Top, double Bottom, double Space)>();
        for (var i = 0; i < staves.Count; i += 2)
        {
            var bottom = i + 1 < staves.Count ? staves[i + 1].Bottom : staves[i].Bottom;
            pairs.Add((staves[i].Top, bottom, staves[i].Space));
        }

        var systems = new List<PageSystem>();
        for (var i = 0; i < pairs.Count; i++)
        {
            var margin = 4 * pairs[i].Space;
            var top = i == 0 ? Math.Max(0, pairs[i].Top - margin) : (pairs[i - 1].Bottom + pairs[i].Top) / 2;
            var bottom = i == pairs.Count - 1 ? Math.Min(darkShare.Count, pairs[i].Bottom + margin) : (pairs[i].Bottom + pairs[i + 1].Top) / 2;
            systems.Add(new PageSystem(top, bottom, barsPerSystem));
        }
        return systems;
    }

    /// <summary>
    /// Seconds to play a system at a tempo, with the beat as the time signature's counting unit: bars × beats a bar × 60 ÷ BPM.
    /// The scroll's speed for that system is the distance to the next system's top ÷ these seconds.
    /// </summary>
    public static double SecondsFor(PageSystem system, int beatsPerBar, double bpm) => system.Bars * beatsPerBar * 60 / bpm;

    /// <summary>
    /// A half-page turn's two lines, in px down the page: this page shows from <c>Cover</c> down (the gap nearest its middle), and
    /// above it the next page's top shows down to <c>Shown</c>, its last gap that fits, so every system on screen is whole. The
    /// space between the two is left blank.
    /// </summary>
    public static (double Cover, double Shown) HalfTurn(IReadOnlyList<PageSystem> current, double currentHeight, IReadOnlyList<PageSystem> next)
    {
        var cover = HalfAt(current, currentHeight);
        var gaps = next.Zip(next.Skip(1), (a, b) => (a.Bottom + b.Top) / 2).Append(next.Count > 0 ? next[^1].Bottom : cover).ToList();
        var shown = gaps.Where(g => g <= cover).DefaultIfEmpty(0).Max();
        // When even the next page's first system reaches below the middle, it shows whole and covers a little more.
        return shown > 0 ? (cover, shown) : (Math.Max(cover, gaps[0]), gaps[0]);
    }

    /// <summary>Half-page turns: where the page splits, at the gap between the systems nearest its middle.</summary>
    public static double HalfAt(IReadOnlyList<PageSystem> systems, double pageHeight)
    {
        if (systems.Count < 2) return pageHeight / 2;

        var middle = pageHeight / 2;
        return systems.Zip(systems.Skip(1), (a, b) => (a.Bottom + b.Top) / 2).MinBy(gap => Math.Abs(gap - middle));
    }
}
