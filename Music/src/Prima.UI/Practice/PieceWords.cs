using System.Globalization;
using Prima.Core.Practice;

namespace Prima.UI.Practice;

/// <summary>A piece's runs in words, the same wherever they show: on the piece, in the Library, on You and in the day's line.</summary>
public static class PieceWords
{
    /// <summary>A day looked back on from today: "today", "yesterday", "3 days ago", "on 3 November".</summary>
    public static string When(DateOnly day, DateOnly today) => (today.DayNumber - day.DayNumber) switch
    {
        <= 0 => "today",
        1 => "yesterday",
        < 7 and var days => $"{days} days ago",
        _ => $"on {day.ToString("d MMMM", CultureInfo.InvariantCulture)}",
    };

    /// <summary>How a run was played: "In time at 84, right hand, from bar 13, stopped at bar 16", or "Wait for me, looping bars 13–16".</summary>
    public static string How(PieceRun run)
    {
        var mode = run.InTime ? $"In time at {run.Tempo:0}" : "Wait for me";
        var hand = run.Hands is "right" or "left" ? $", {run.Hands} hand" : "";
        if (run.To is { } to) return $"{mode}{hand}, looping {Bars(run.From, to)}";

        var from = run.From > 1 ? $", from bar {run.From}" : "";
        var stopped = run.Stopped && run.Bar is { } bar ? $", stopped at {Bar(bar)}" : "";
        return mode + hand + from + stopped;
    }

    /// <summary>
    /// How a run went: "88 of 95 right, 91% right in time", or "22 of 23 right first time". Round a loop, how its passes went, from
    /// the first to the last: "5 passes, right in time from 70% to 90%".
    /// </summary>
    public static string Went(PieceRun run)
    {
        var went = run.InTime ? $"{run.Right} of {run.Notes} right, {Share(run)} right in time" : $"{run.Right} of {run.Notes} right first time";
        return run.Passes switch
        {
            [var first, .., var last] when run.InTime => $"{run.Passes.Count} passes, right in time from {first.RightInTime * 100:0}% to {last.RightInTime * 100:0}%",
            [var first, .., var last] => $"{run.Passes.Count} passes, right first time from {first.Right} to {last.Right} of {last.Notes}",
            [_] => $"one pass, {went}",
            _ => went,
        };
    }

    /// <summary>A run in a few words, for a list: "91% right in time at 84", "22 of 23 right first time, looping bars 13–16".</summary>
    public static string Short(PieceRun run)
    {
        var went = run.InTime ? $"{Share(run)} right in time at {run.Tempo:0}" : $"{run.Right} of {run.Notes} right first time";
        return run.To is { } to ? $"{went}, looping {Bars(run.From, to)}" : went;
    }

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

    /// <summary>A bar in words: "bar 7", or the pickup before bar 1, "the upbeat".</summary>
    public static string Bar(int n) => n == 0 ? "the upbeat" : $"bar {n}";

    /// <summary>Bars in words: "bar 7", "bars 13–16", or from the pickup, "the upbeat to bar 4".</summary>
    public static string Bars(int from, int to) => from == to ? Bar(from) : from == 0 ? $"the upbeat to bar {to}" : $"bars {from}–{to}";

    private static string Share(PieceRun run) => $"{(run.RightInTime ?? 0) * 100:0}%";
}
