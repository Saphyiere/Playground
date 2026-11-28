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

    public static string How(PieceRun run) => $"{Short(run)} · {Hands(run.Hands)}";

    public static string Duration(TimeSpan time) => time.TotalHours >= 1 ? $"{(int)time.TotalHours} h {time.Minutes} min"
        : time.TotalMinutes >= 1 ? $"{(int)time.TotalMinutes} min" : $"{(int)time.TotalSeconds} s";

    public static string Hands(string hands) => hands switch { "right" => "Right hand", "left" => "Left hand", _ => "Both hands" };

    /// <summary>What a run played, in what key when it wasn't the one written, and how fast: "Played through the piece in D major at 72 BPM".</summary>
    public static string Short(PieceRun run)
    {
        var passage = run.To is { } to ? $"{(run.Passes.Count > 0 ? "Looped" : "Practiced")} {Bars(run.From, to)}"
            : run.Stopped && run.Bar is { } stopped ? $"Stopped at {Bar(stopped)}"
            : run.Stopped ? $"Started at {Bar(run.From)}" : run.From > 1 ? $"Played from {Bar(run.From)}" : "Played through the piece";
        var key = run.KeyName is { } name ? $" in {name}" : "";
        var pace = run.InTime ? run.Tempo is { } tempo ? $" at {tempo:0} BPM" : " at a set tempo" : " at your own pace";
        return passage + key + pace;
    }

    public static BarTrouble? Highlight(PieceRun run)
    {
        var trouble = run.Passes.LastOrDefault()?.Trouble;
        return (trouble is { Count: > 0 } ? trouble : run.Trouble).Where(b => b.Weight > 0)
            .OrderByDescending(b => b.Weight).ThenBy(b => b.Bar).FirstOrDefault();
    }

    public static string Went(PieceRun run)
    {
        var pass = run.Passes.LastOrDefault();
        var trouble = Highlight(run);
        var when = pass is null ? "" : " on the last pass";
        if (trouble is not null) return Observation(trouble) + when + ".";
        var notes = pass?.Notes ?? run.Notes;
        var right = pass?.Right ?? run.Right;
        if (notes == 0) return "No notes recorded.";
        if (!run.InTime)
        {
            if (right == notes) return $"You played every note correctly on the first try{when}.";
            return $"You played {right} of {notes} correctly on the first try{when}.";
        }
        if ((pass?.RightInTime ?? run.RightInTime) is { } share)
        {
            if (share == 1) return $"Every written note was correct and on time{when}.";
            return $"{share * 100:0}% of the notes were correct and on time{when}.";
        }
        return $"You played {right} of {notes} correctly{when}.";
    }

    public static string Observation(BarTrouble bar)
    {
        var where = $"in {Bar(bar.Bar)}";
        if (bar.Missed > 0) return $"You missed {BarWords.Count(bar.Missed, "note")} {where}";
        if (bar.Wrong > 0) return $"You played {BarWords.Count(bar.Wrong, "wrong note")} {where}";
        if (bar.Slips > 0) return $"You corrected {BarWords.Count(bar.Slips, "note")} {where}";
        if (bar.Apart > 0) return $"The chord notes {where} landed separately";
        if (bar.Off > 0)
        {
            var timing = bar.Ahead == bar.Off ? "early" : "outside the timing window";
            return $"{BarWords.Count(bar.Off, "note")} {where} {(bar.Off == 1 ? "was" : "were")} {timing}";
        }
        return $"You released {BarWords.Count(bar.Early, "note")} early {where}";
    }

    public static string Trouble(BarTrouble bar) => BarWords.Trouble(bar);

    public static string Bar(int n) => n == 0 ? "the pickup" : $"bar {n}";

    public static string Bars(int from, int to) => from == to ? Bar(from) : from == 0 ? $"the opening through bar {to}" : $"bars {from}–{to}";
}
