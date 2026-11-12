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

    /// <summary>How a run was played: "In time at 84, right hand, from bar 13, stopped at bar 16".</summary>
    public static string How(PieceRun run)
    {
        var mode = run.InTime ? $"In time at {run.Tempo:0}" : "Wait for me";
        var hand = run.Hands is "right" or "left" ? $", {run.Hands} hand" : "";
        var from = run.From > 1 ? $", from bar {run.From}" : "";
        var stopped = run.Stopped && run.Bar is { } bar ? $", stopped at bar {bar}" : "";
        return mode + hand + from + stopped;
    }

    /// <summary>How a run went: "88 of 95 right, 91% right in time", or "22 of 23 right first time".</summary>
    public static string Went(PieceRun run) => run.InTime ? $"{run.Right} of {run.Notes} right, {Share(run)} right in time" : $"{run.Right} of {run.Notes} right first time";

    /// <summary>A run in a few words, for a list: "91% right in time at 84", or "22 of 23 right first time".</summary>
    public static string Short(PieceRun run) => run.InTime ? $"{Share(run)} right in time at {run.Tempo:0}" : $"{run.Right} of {run.Notes} right first time";

    private static string Share(PieceRun run) => $"{(run.RightInTime ?? 0) * 100:0}%";
}
