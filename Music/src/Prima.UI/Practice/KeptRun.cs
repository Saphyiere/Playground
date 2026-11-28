using System.Text.Json;
using Prima.Core.Music;
using Prima.Core.Practice;
using Prima.Core.Scoring;
using Prima.UI.Components;

namespace Prima.UI.Practice;

/// <summary>
/// What a run on a piece keeps of how it went, as its attempt's measures: its figures, where it started and whether it was stopped,
/// the hand, round a loop its last bar and each pass, the practice session it was played in (or that it was Play's), what went
/// wrong in each bar, how long it took, In time how it lay against the click and how what the music marks was played, the marks
/// it left on the music, and the key it was played in when that's not the one it's written in.
/// <see cref="Storage.PrimaStore.GetPieceRunsAsync"/> reads them back.
/// </summary>
public static class KeptRun
{
    /// <param name="how">How the run went.</param>
    /// <param name="from">The bar it started from.</param>
    /// <param name="hands">"right", "left" or "both".</param>
    /// <param name="session">The practice session it was played in, if it was.</param>
    /// <param name="play">Whether it was Play's: the piece through, with nothing on the music.</param>
    /// <param name="barOf">Each written note's bar, by its id on the sheet, for Wait for me's marks.</param>
    /// <param name="took">The seconds it took, its count-in too.</param>
    /// <param name="key">The key it was played in, from the one it's written in, and that key's name: none as written.</param>
    public static string Measures(PlayOutcome how, int from, string hands, long? session, bool play, IReadOnlyDictionary<string, int> barOf, double? took = null,
        (Transposition By, string Name)? key = null)
    {
        // Round a loop, its last bar and each pass, and the run's figures are its last pass's.
        var (to, passes) = (how.Loop?.To, how.Loop is null ? null : how.Passes.Select(p => new { right = p.Right, notes = p.Notes, inTime = p.RightInTime, seconds = p.Seconds is { } s ? Math.Round(s, 1) : (double?)null, trouble = Rows(p.Trouble) }));
        took = took is { } seconds ? Math.Round(seconds, 1) : null;
        var trouble = Rows(how.InTime is { } judged ? Trouble.Of(judged.Notes) : Trouble.Of(how.Marks, barOf));
        var fromPlay = play ? true : (bool?)null;
        var marks = new { right = how.Marks.Where(m => m.Kind == "right").Select(m => m.Id), other = how.Marks.Where(m => m.Kind != "right").Select(m => new object?[] { m.Id, m.Kind, m.Played, m.Steps, m.Written }) };
        // In another key: the letters and semitones from the one it's written in, to draw the music in it again, and its name.
        int[]? transposed = null;
        string? keyName = null;
        if (key is { } moved && moved.By.Moves) (transposed, keyName) = ([moved.By.Steps, moved.By.Semitones], moved.Name);
        object measures = how.InTime is { } r
            ? new { inTime = r.InTimeAccuracy, right = r.Right, wrong = r.Wrong, missed = r.Missed, extra = r.Extra, keptGoing = r.KeptGoing, holds = r.HoldsJudged, letGoEarly = r.LetGoEarly, from, to, passes, stopped = how.Stopped, bar = how.Bar, hands, session, play = fromPlay, took,
                timing = new[] { Math.Round(r.LeanMs, 1), Math.Round(r.SteadinessPercent, 1) }, marked = Marked(r), trouble, marks, transposed, key = keyName }
            : new { rightFirstTime = how.Wait!.RightFirstTime, notes = how.Wait.Notes, wrongKeys = how.Wait.WrongKeys, apart = how.Wait.Apart, seconds = how.Wait.Seconds, from, to, passes, stopped = how.Stopped, bar = how.Bar, hands, session, play = fromPlay, took, trouble, marks, transposed, key = keyName };
        return JsonSerializer.Serialize(measures);
    }

    /// <summary>
    /// The marks the latest of the runs to leave any on a bar left there, of the runs played in a key (<paramref name="key"/>, as
    /// written unless it's given): a wrong key's name is spelled in the key it was played in, and drawn where that key puts it.
    /// </summary>
    public static List<SheetMark> MarksForBar(int bar, IReadOnlyList<ExpectedNote> expected, IReadOnlyList<PieceRun> runs, IReadOnlyDictionary<long, string> marksKept, Transposition key = default) =>
        LatestForBar(bar, expected, [.. runs.Where(r => r.Transposed == key)], marksKept).Marks;

    /// <summary>
    /// The marks the latest of the runs to leave any on a bar left there, in whatever key it was played, and that run: a run kept
    /// is shown in its own key (<see cref="PieceRun.Transposed"/>), its marks with it. None of them left any: none, and no run.
    /// </summary>
    public static (List<SheetMark> Marks, PieceRun? Run) LatestForBar(int bar, IReadOnlyList<ExpectedNote> expected, IReadOnlyList<PieceRun> runs, IReadOnlyDictionary<long, string> marksKept)
    {
        var inBar = expected.Where(n => n.Bar == bar).Select(n => n.Id).ToHashSet();
        foreach (var run in runs.OrderByDescending(r => r.At).ThenByDescending(r => r.Id))
        {
            if (!marksKept.TryGetValue(run.Id, out var json)) continue;

            using var left = JsonDocument.Parse(json);
            var right = left.RootElement.GetProperty("right").EnumerateArray().Select(e => e.GetString() ?? "").Where(inBar.Contains).Select(id => new SheetMark(id, "right"));
            var other = left.RootElement.GetProperty("other").EnumerateArray().Where(e => inBar.Contains(e[0].GetString() ?? ""))
                .Select(e => new SheetMark(e[0].GetString()!, e[1].GetString()!, e[2].ValueKind == JsonValueKind.String ? e[2].GetString() : null, e[3].GetInt32(), Written: e.GetArrayLength() > 4 && e[4].ValueKind == JsonValueKind.String ? e[4].GetString() : null));
            List<SheetMark> marks = [.. right, .. other];
            if (marks.Count > 0) return (marks, run);
        }
        return ([], null);
    }

    /// <summary>The keys played, each with its ms from the first, as an attempt keeps them.</summary>
    public static string Keys(PlayOutcome how)
    {
        var firstNs = how.Played.Count == 0 ? 0 : how.Played[0].OnNs;
        return JsonSerializer.Serialize(how.Played.Select(p => new[] { p.Midi, (p.OnNs - firstNs) / 1e6 }));
    }

    /// <summary>The notes right of those reached, and whether it was clean: every note right (and in Wait for me, to the end).</summary>
    public static (int Right, int Notes, bool Clean) Figures(PlayOutcome how) => how.InTime is { } timed
        ? (timed.Right, timed.Expected, timed.Clean)
        : (how.Wait!.RightFirstTime, how.Wait.Notes, how.Wait.WrongKeys == 0 && how.Wait.Apart == 0 && how.Wait.Finished);

    // What went wrong in each bar, as it's kept: [bar, wrong, missed, off the beat, let go early, slips put right, chord notes not
    // together, and of those off the beat the ones ahead of it].
    private static IEnumerable<int[]> Rows(IReadOnlyList<BarTrouble> trouble) => trouble.Select(t => new[] { t.Bar, t.Wrong, t.Missed, t.Off, t.Early, t.Slips, t.Apart, t.Ahead });

    // What the music marks besides its notes and how each was played, as it's kept: [what (a Marking), its first and last bars, how
    // many were judged, how many weren't as marked, its name]. A dynamic and a hairpin each have a row; staccato, tenuto and accents
    // one for each bar that has them, with the gaps under slurs there (which are all that's known of its slurs).
    private static IEnumerable<object?[]> Marked(PassageResult r)
    {
        foreach (var d in r.Dynamics) yield return [(int)Marking.Dynamic, d.FromBar, d.ToBar, 1, d.Outcome == Loudness.Right ? 0 : 1, d.Level];
        foreach (var h in r.Hairpins) yield return [(int)Marking.Swell, h.FromBar, h.ToBar, 1, h.Outcome == Swell.Right ? 0 : 1, h.Crescendo ? "crescendo" : "diminuendo"];
        foreach (var bar in r.Notes.Where(n => n.Outcome == Outcome.Right).GroupBy(n => n.Bar).OrderBy(g => g.Key))
        {
            (Marking What, int Of, int Off)[] kinds =
            [
                (Marking.Staccato, bar.Count(n => n.Asked == Articulation.Staccato), bar.Count(n => n.Touch == Touch.StaccatoHeldLong)),
                (Marking.Tenuto, bar.Count(n => n.Asked == Articulation.Tenuto), bar.Count(n => n.Touch == Touch.TenutoShort)),
                (Marking.Slur, bar.Count(n => n.Touch == Touch.GapUnderSlur), bar.Count(n => n.Touch == Touch.GapUnderSlur)),
                (Marking.Accent, bar.Count(n => n.Asked == Articulation.Accent || n.Touch == Touch.AccentNotStronger), bar.Count(n => n.Touch == Touch.AccentNotStronger)),
            ];
            foreach (var (what, of, off) in kinds.Where(k => k.Of > 0)) yield return [(int)what, bar.Key, bar.Key, of, off, null];
        }
        foreach (var p in r.Pedal) yield return [(int)Marking.Pedal, p.FromBar, p.ToBar, 1, p.Outcome == Pedalling.Right ? 0 : 1, null];
    }
}
