using System.Globalization;

namespace Prima.Core.Practice;

/// <summary>Bars, and what went wrong in them, in words: the same wherever a piece's practice is talked about.</summary>
public static class BarWords
{
    /// <summary>A bar in words: "bar 7", or the pickup before bar 1, "the upbeat".</summary>
    public static string Bar(int n) => n == 0 ? "the upbeat" : $"bar {n}";

    /// <summary>Bars in words: "bar 7", "bars 13–16", or from the pickup, "the upbeat to bar 4".</summary>
    public static string Bars(int from, int to) => from == to ? Bar(from) : from == 0 ? $"the upbeat to bar {to}" : $"bars {from}–{to}";

    /// <summary>
    /// Bars named by their runs: "bar 21", "bars 5–7", "bars 5–7, 13–14 and 21"; the pickup first, as "the upbeat", alone or
    /// with bars after it: "the upbeat and bars 1–2 and 5". Past <paramref name="most"/> runs, the first of them "and 4 more".
    /// </summary>
    public static string Runs(IEnumerable<int> bars, int most = 4)
    {
        var runs = new List<(int From, int To)>();
        foreach (var bar in bars.Distinct().Order())
        {
            if (runs.Count > 0 && runs[^1].To == bar - 1) runs[^1] = (runs[^1].From, bar);
            else runs.Add((bar, bar));
        }
        if (runs.Count == 0) return "no bars";
        if (runs is [var only]) return Bars(only.From, only.To);

        // The pickup has no number to give ("bars 0–2"): it's named on its own, and a run it starts goes on from bar 1.
        var upbeat = runs[0].From == 0;
        if (upbeat && runs[0].To == 0) runs.RemoveAt(0);
        else if (upbeat) runs[0] = (1, runs[0].To);
        var named = runs.Select(r => r.From == r.To ? $"{r.From}" : $"{r.From}–{r.To}").ToList();
        var more = named.Count > most ? named.Count - most : 0;
        var list = more > 0 ? $"{string.Join(", ", named.Take(most))} and {more} more" : And(named);
        var numbers = named.Count == 1 && !named[0].Contains('–') ? $"bar {list}" : $"bars {list}";
        return upbeat ? $"the upbeat and {numbers}" : numbers;
    }

    /// <summary>Things in a row: "a", "a and b", "a, b and c".</summary>
    public static string And(IReadOnlyList<string> items) => items.Count switch
    {
        0 => "",
        1 => items[0],
        _ => $"{string.Join(", ", items.Take(items.Count - 1))} and {items[^1]}",
    };

    /// <summary>
    /// What went wrong in a bar, in words: "2 wrong, 1 slip put right, 3 let go early", or with nothing wrong "nothing to fix".
    /// </summary>
    public static string Trouble(BarTrouble bar)
    {
        var parts = new[]
        {
            (bar.Wrong, "wrong", "wrong"), (bar.Missed, "missed", "missed"), (bar.Slips, "slip put right", "slips put right"),
            (bar.Apart, "chord note not together", "chord notes not together"), (bar.Off, "off the beat", "off the beat"), (bar.Early, "let go early", "let go early"),
        };
        var said = parts.Where(p => p.Item1 > 0).Select(p => $"{p.Item1} {(p.Item1 == 1 ? p.Item2 : p.Item3)}").ToList();
        return said.Count == 0 ? "nothing to fix" : string.Join(", ", said);
    }

    /// <summary>Words starting a sentence: "the upbeat" as "The upbeat".</summary>
    public static string Capital(string words) => words.Length == 0 ? words : char.ToUpperInvariant(words[0]) + words[1..];

    /// <summary>A share as a whole percent: "87%".</summary>
    public static string Percent(double share) => (share * 100).ToString("0", CultureInfo.InvariantCulture) + "%";

    /// <summary>A tempo as its figure: "72".</summary>
    public static string Tempo(double? tempo) => (tempo ?? 0).ToString("0", CultureInfo.InvariantCulture);

    /// <summary>How many of something: "1 pass", "3 passes".</summary>
    public static string Count(int n, string one, string? many = null) => n == 1 ? $"1 {one}" : $"{n} {many ?? one + "s"}";

    /// <summary>How often: "once", "twice", "3 times".</summary>
    public static string Times(int n) => n switch { 1 => "once", 2 => "twice", _ => $"{n} times" };

    /// <summary>What a comparison with a day before is headed: "Earlier today", "Since yesterday", "Since Tuesday", "Since 3 November".</summary>
    public static string Since(DateOnly day, DateOnly today) => (today.DayNumber - day.DayNumber) switch
    {
        <= 0 => "Earlier today",
        1 => "Since yesterday",
        < 7 => $"Since {day.DayOfWeek}",
        _ => $"Since {day.ToString("d MMMM", CultureInfo.InvariantCulture)}",
    };
}
