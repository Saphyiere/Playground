using Prima.Core.Generation;
using Prima.Core.Music;
using Prima.Core.Practice;
using Prima.Core.Scoring;
using Prima.UI;
using Prima.UI.Components;
using Prima.UI.Practice;
using Prima.UI.Storage;

namespace Prima.Preview;

/// <summary>
/// Practice sessions on a piece as someone would have played them, for looking at what Finish practice says of each: the
/// Minuet in G, by four kinds of player (one practicing it as most sessions go, one just learning it, one who knows it and is
/// working on its timing and phrasing, one polishing it), for 20, 40 or 60 minutes, with the days before behind them. Every
/// run is played by a pretend pianist (<see cref="Pianist"/>) and scored by the app's own scoring, then kept as the piece's
/// page keeps a run, so the look back reads what it would read on the tablet. The session is left going, paused: open the
/// piece, and Finish practice is on its cover.
/// </summary>
static class PracticeSeeds
{
    public const string Minuet = "pdmx-QmUq8P8pvoDes682mBCKFiSSdvmfdAXak31oEPnxVNBAqv";

    public static readonly string[] Players = ["general", "learning", "phrasing", "mastering", "first"];

    /// <summary>Fills the data with <paramref name="who"/>'s sessions on the Minuet, today's of <paramref name="minutes"/>: what was kept, in a line.</summary>
    public static async Task<string> Fill(string who, int minutes, PrimaStore store, PrimaSettings settings)
    {
        if (await store.GetMeiAsync(Minuet) is not { } mei) return "The Minuet isn't in the Library yet: open the Library once, then seed again.";

        var pianist = new Pianist(MeiReader.Read(mei), new Rng((ulong)(Array.IndexOf(Players, who) * 1000 + minutes + 7)), who);
        var now = DateTimeOffset.Now;
        var kept = new List<string>();

        // The days before, each a finished session.
        foreach (var (daysAgo, script) in Before(who))
        {
            var sitting = new Sitting(pianist);
            script(sitting);
            var began = new DateTimeOffset(now.Date.AddDays(-daysAgo).AddHours(19).AddMinutes(10), now.Offset);
            kept.Add(await sitting.Keep(store, began, sitting.Natural, finished: true));
        }

        // Today's, as long as asked, still going: its cover has Finish practice.
        var today = new Sitting(pianist);
        Today(who, minutes)(today);
        kept.Add(await today.Keep(store, now.AddMinutes(-minutes).AddSeconds(-20), TimeSpan.FromMinutes(minutes), finished: false));
        await store.SetPieceAsync(Minuet, tempo: today.LastTempo);
        return string.Join("\n", kept);
    }

    // ——— What each player did on the days before ———

    private static IEnumerable<(int DaysAgo, Action<Sitting> Script)> Before(string who) => who switch
    {
        // Yesterday was the first look: the first eight bars, a hand at a time, then the first four together.
        "learning" =>
        [
            (1, s =>
            {
                s.Loop(1, 8, "right", 0, 4, new Waited(0.30, 0, 1.5), new Waited(0.12, 0, 1.0), 50);
                s.Loop(1, 8, "left", 0, 3, new Waited(0.26, 0, 1.6), new Waited(0.10, 0, 1.1), 70);
                s.Loop(1, 4, "both", 0, 3, new Waited(0.36, 0.30, 2.2), new Waited(0.18, 0.15, 1.6), 90);
            }),
        ],
        // Nine days in: the notes found hands together, and In time slowly, a half at a time.
        "general" =>
        [
            (9, s =>
            {
                s.Loop(1, 8, "both", 0, 4, new Waited(0.30, 0.25, 2.0), new Waited(0.12, 0.10, 1.4), 60);
                s.Loop(9, 16, "both", 0, 4, new Waited(0.32, 0.25, 2.1), new Waited(0.14, 0.10, 1.5), 80);
            }),
            (7, s =>
            {
                s.Loop(17, 24, "both", 0, 5, new Waited(0.36, 0.25, 2.2), new Waited(0.15, 0.10, 1.5), 60);
                s.Loop(25, 32, "both", 0, 4, new Waited(0.30, 0.22, 2.0), new Waited(0.12, 0.08, 1.4), 80);
                s.Loop(1, 8, "both", 60, 5, General with { Wrong = 0.10, Missed = 0.08, SpreadMs = 55 }, General with { Wrong = 0.05, Missed = 0.04, SpreadMs = 45 }, 90);
            }),
            (5, s =>
            {
                s.Loop(1, 8, "both", 60, 4, General with { Wrong = 0.07, Missed = 0.05, SpreadMs = 48 }, General with { Wrong = 0.04, Missed = 0.03, SpreadMs = 42 }, 50);
                s.Loop(9, 16, "both", 60, 6, General with { Wrong = 0.10, Missed = 0.07, SpreadMs = 52 }, General with { Wrong = 0.05, Missed = 0.03, SpreadMs = 44 }, 80);
                s.Run(1, "both", 60, General with { Wrong = 0.07, Missed = 0.05, SpreadMs = 48 }, 100, stopAt: 16);
            }),
            (3, s =>
            {
                s.Loop(17, 24, "both", 60, 6, General with { Wrong = 0.11, Missed = 0.07, SpreadMs = 52 }, General with { Wrong = 0.05, Missed = 0.03, SpreadMs = 44 }, 50);
                s.Loop(25, 32, "both", 60, 5, General with { Wrong = 0.09, Missed = 0.06, SpreadMs = 50 }, General with { Wrong = 0.05, Missed = 0.03, SpreadMs = 44 }, 80);
                s.Run(1, "both", 60, General with { Wrong = 0.075, Missed = 0.05, SpreadMs = 48 }, 120);
            }),
            (1, s =>
            {
                s.Loop(5, 8, "both", 66, 6, General with { Wrong = 0.08, Missed = 0.05 }, General with { Wrong = 0.035, Missed = 0.02 }, 50);
                s.Loop(21, 24, "both", 60, 5, General with { Wrong = 0.10, Missed = 0.06 }, General with { Wrong = 0.05, Missed = 0.03 }, 70);
                s.Loop(21, 24, "both", 66, 4, General with { Wrong = 0.08, Missed = 0.05 }, General with { Wrong = 0.05, Missed = 0.03 }, 40);
                s.Run(1, "both", 66, General with { Wrong = 0.06, Missed = 0.04 }, 120);
            }),
        ],
        // Three weeks in: the notes are there, and it's been played through most days, a little faster each week.
        "phrasing" =>
        [
            (12, s => s.Run(1, "both", 80, Phrasing with { Wrong = 0.03, Missed = 0.015, Rush = 8 }, 60)),
            (10, s => s.Run(1, "both", 80, Phrasing with { Wrong = 0.025, Missed = 0.012, Rush = 8 }, 60)),
            (8, s => s.Run(1, "both", 84, Phrasing with { Wrong = 0.022, Missed = 0.01, Rush = 8 }, 60)),
            (6, s => s.Run(1, "both", 84, Phrasing with { Wrong = 0.018, Missed = 0.008, Rush = 7.5 }, 60)),
            (4, s =>
            {
                s.Run(1, "both", 88, Phrasing with { Wrong = 0.018, Rush = 7.5 }, 40);
                s.Loop(5, 8, "both", 80, 6, Phrasing with { Rush = 8 }, Phrasing with { Rush = 4 }, 60);
                s.Run(1, "both", 88, Phrasing with { Wrong = 0.015, Rush = 7 }, 90);
            }),
            (2, s =>
            {
                s.Run(1, "both", 88, Phrasing with { Rush = 7 }, 40);
                s.Loop(20, 24, "both", 80, 6, Phrasing with { Rush = 7.5 }, Phrasing with { Rush = 4 }, 60);
                s.Run(1, "both", 92, Phrasing with { Rush = 7.5 }, 90);
            }),
            (1, s =>
            {
                s.Run(1, "both", 92, Phrasing with { Rush = 7.2 }, 40);
                s.Loop(13, 16, "both", 84, 5, Phrasing with { Rush = 7 }, Phrasing with { Rush = 3.5 }, 60);
                s.Loop(5, 8, "both", 84, 5, Phrasing with { Rush = 7 }, Phrasing with { Rush = 4 }, 60);
                s.Run(1, "both", 92, Phrasing with { Rush = 6.8 }, 90);
            }),
        ],
        // Six weeks in: at the piece's own tempo for the last week.
        "mastering" =>
        [
            .. new[] { (21, 96.0), (18, 100), (16, 100), (14, 104), (12, 104), (10, 108), (9, 108), (7, 112), (6, 112) }.Select(d => (d.Item1, (Action<Sitting>)(s =>
                s.Run(1, "both", d.Item2, Mastering with { Wrong = 0.012, Missed = 0.004, SpreadMs = 20, Rush = 4 }, 60)))),
            (4, s =>
            {
                s.Run(1, "both", 115, Mastering with { Wrong = 0.012, Missed = 0.004, SpreadMs = 20, Rush = 4 }, 40);
                s.Loop(20, 23, "both", 108, 6, Mastering with { Wrong = 0.02, Rush = 4 }, Mastering, 60);
                s.Run(1, "both", 115, Mastering with { Wrong = 0.009, SpreadMs = 19, Rush = 3.5 }, 90);
            }),
            (2, s =>
            {
                s.Run(1, "both", 115, Mastering with { Wrong = 0.009, SpreadMs = 19, Rush = 3.5 }, 40);
                s.Loop(28, 29, "both", 115, 6, Mastering with { Wrong = 0.02, Rush = 4 }, Mastering, 60);
                s.Run(1, "both", 115, Mastering with { Wrong = 0.007, SpreadMs = 18, Rush = 3 }, 90);
            }),
            (1, s =>
            {
                s.Run(1, "both", 115, Mastering with { Wrong = 0.008, SpreadMs = 18, Rush = 3 }, 40);
                s.Run(1, "both", 115, Mastering with { Wrong = 0.006, SpreadMs = 17, Rush = 3 }, 70);
                s.Run(1, "both", 115, Mastering with { Wrong = 0.006, SpreadMs = 17, Rush = 2.5 }, 70);
            }),
        ],
        // "first": the first sitting on the piece, with nothing before it.
        _ => [],
    };

    // ——— Today's session, as long as asked ———

    private static Action<Sitting> Today(string who, int minutes) => who switch
    {
        "learning" => s =>
        {
            // The first eight bars again, a hand at a time (how much of yesterday stayed), then together, then In time, very slowly.
            s.Loop(1, 8, "right", 0, 2, new Waited(0.14, 0, 1.1), new Waited(0.08, 0, 0.9), 40);
            s.Loop(1, 8, "left", 0, 2, new Waited(0.14, 0, 1.2), new Waited(0.07, 0, 1.0), 50);
            s.Loop(1, 4, "both", 0, 4, new Waited(0.28, 0.25, 1.9), new Waited(0.10, 0.08, 1.2), 70);
            s.Loop(5, 8, "both", 0, 5, new Waited(0.40, 0.30, 2.3), new Waited(0.16, 0.12, 1.5), 90);
            s.Loop(1, 8, "both", 0, 3, new Waited(0.22, 0.15, 1.6), new Waited(0.12, 0.08, 1.3), 80);
            s.Loop(1, 4, "both", 50, 5, Learning, Learning with { Wrong = 0.05, Missed = 0.06, SpreadMs = 50 }, 110);
            if (minutes < 40) return;

            // The next eight bars, the same way.
            s.Loop(9, 12, "right", 0, 3, new Waited(0.30, 0, 1.5), new Waited(0.10, 0, 1.0), 80);
            s.Loop(9, 12, "left", 0, 3, new Waited(0.30, 0, 1.6), new Waited(0.12, 0, 1.1), 50);
            s.Loop(9, 12, "both", 0, 5, new Waited(0.42, 0.30, 2.4), new Waited(0.15, 0.10, 1.5), 80);
            s.Loop(13, 16, "right", 0, 3, new Waited(0.32, 0, 1.5), new Waited(0.12, 0, 1.0), 70);
            s.Loop(13, 16, "left", 0, 2, new Waited(0.24, 0, 1.4), new Waited(0.12, 0, 1.1), 40);
            s.Loop(13, 16, "both", 0, 5, new Waited(0.44, 0.30, 2.4), new Waited(0.18, 0.12, 1.6), 80);
            s.Loop(9, 16, "both", 0, 3, new Waited(0.26, 0.15, 1.8), new Waited(0.16, 0.10, 1.5), 90);
            s.Loop(1, 8, "both", 50, 4, Learning with { Wrong = 0.12, Missed = 0.14 }, Learning with { Wrong = 0.06, Missed = 0.07, SpreadMs = 52 }, 110);
            s.Wait(1, "both", new Waited(0.20, 0.12, 1.5), 100, stopAt: 16);
            if (minutes < 60) return;

            // On into the second half, tiring: the last things go worse than they started.
            s.Loop(17, 20, "right", 0, 3, new Waited(0.30, 0, 1.5), new Waited(0.12, 0, 1.1), 90);
            s.Loop(17, 20, "left", 0, 2, new Waited(0.22, 0, 1.4), new Waited(0.12, 0, 1.1), 40);
            s.Loop(17, 20, "both", 0, 4, new Waited(0.40, 0.28, 2.3), new Waited(0.20, 0.14, 1.7), 70);
            s.Tired = 1.5;
            s.Loop(21, 24, "right", 0, 4, new Waited(0.42, 0, 1.8), new Waited(0.28, 0, 1.5), 80);
            s.Loop(21, 24, "both", 0, 4, new Waited(0.50, 0.30, 2.6), new Waited(0.38, 0.24, 2.3), 80);
            s.Tired = 1.9;
            s.Loop(1, 8, "both", 56, 4, Learning with { Wrong = 0.07, Missed = 0.08, SpreadMs = 55 }, Learning with { Wrong = 0.10, Missed = 0.12, SpreadMs = 66 }, 100);
            s.Run(1, "both", 50, Learning with { Wrong = 0.10, Missed = 0.14 }, 60, stopAt: 9);
        },
        "phrasing" => s =>
        {
            // Through it, then the bars that run away, slower and back up; then through again.
            s.Run(1, "both", 92, Phrasing, 30);
            s.Loop(5, 8, "both", 80, 6, Phrasing with { Rush = 7 }, Phrasing with { Rush = 2, Early = 0.10 }, 70);
            s.Loop(5, 8, "both", 92, 5, Phrasing with { Rush = 6 }, Phrasing with { Rush = 3.5, Early = 0.10 }, 40);
            s.Loop(13, 16, "both", 92, 5, Phrasing with { Rush = 6.5 }, Phrasing with { Rush = 3, Shape = 0.45 }, 70);
            s.Loop(20, 24, "both", 84, 5, Phrasing with { Rush = 5, Shape = 0.3 }, Phrasing with { Rush = 2.5, Shape = 0.75, Touch = 0.8 }, 90);
            s.Run(1, "both", 92, Phrasing with { Rush = 4.2, Early = 0.13, Shape = 0.45, Touch = 0.65 }, 100);
            if (minutes < 40) return;

            // The left hand's long notes, the end's loud and soft, and a step faster.
            s.Run(1, "left", 92, Phrasing with { Early = 0.26, Rush = 2 }, 90, stopAt: 16);
            s.Run(1, "left", 92, Phrasing with { Early = 0.08, Rush = 2 }, 50, stopAt: 16);
            s.Loop(28, 31, "both", 84, 5, Phrasing with { Rush = 7 }, Phrasing with { Rush = 3 }, 70);
            s.Loop(28, 31, "both", 92, 4, Phrasing with { Rush = 5.5 }, Phrasing with { Rush = 3.2 }, 40);
            s.Loop(25, 32, "both", 92, 5, Phrasing with { Rush = 4, Shape = 0.3 }, Phrasing with { Rush = 3, Shape = 0.8, Touch = 0.8 }, 80);
            s.Run(1, "both", 96, Phrasing with { Rush = 5.6, Early = 0.12, Shape = 0.5, Touch = 0.65 }, 110);
            s.Run(1, "both", 92, Phrasing with { Rush = 3.2, Early = 0.09, Shape = 0.7, Touch = 0.8, Wrong = 0.006 }, 100);
            if (minutes < 60) return;

            // Faster than it holds yet, and tiring.
            s.Loop(17, 24, "both", 96, 4, Phrasing with { Rush = 5.5, Shape = 0.6 }, Phrasing with { Rush = 3.5, Shape = 0.75 }, 80);
            s.Loop(17, 24, "both", 100, 4, Phrasing with { Rush = 6.5, Shape = 0.6 }, Phrasing with { Rush = 4.8, Shape = 0.7 }, 40);
            s.Loop(5, 8, "both", 100, 5, Phrasing with { Rush = 8.5 }, Phrasing with { Rush = 6 }, 70);
            s.Run(17, "both", 100, Phrasing with { Rush = 6.5, Shape = 0.6, Wrong = 0.016 }, 80);
            s.Tired = 1.5;
            s.Run(1, "both", 100, Phrasing with { Rush = 7.5, Wrong = 0.02, Missed = 0.008, Shape = 0.5, Early = 0.16 }, 100);
            s.Tired = 1.8;
            s.Run(1, "both", 92, Phrasing with { Rush = 5, Wrong = 0.014, Shape = 0.55, Early = 0.15, Touch = 0.7 }, 110);
        },
        "mastering" => s =>
        {
            // Through it cold, the one spot, through it again, and a notch over.
            s.Run(1, "both", 115, Mastering with { Wrong = 0.008, SpreadMs = 18, Rush = 3 }, 20);
            s.Loop(20, 23, "both", 115, 6, Mastering with { Wrong = 0.02, Rush = 3.5 }, Mastering with { Wrong = 0.002 }, 60);
            s.Run(1, "both", 115, Mastering, 80);
            s.Run(1, "both", 115, Mastering with { Wrong = 0.002, Missed = 0 }, 70);
            s.Run(1, "both", 120, Mastering with { Wrong = 0.009, SpreadMs = 19, Rush = 3.6 }, 90);
            s.Loop(28, 29, "both", 120, 5, Mastering with { Wrong = 0.02, Rush = 4.5 }, Mastering with { Rush = 2.5 }, 60);
            if (minutes < 40) return;

            s.Run(1, "both", 115, Mastering with { Wrong = 0.001, Missed = 0 }, 80);
            s.Run(1, "left", 115, Mastering with { Wrong = 0.004 }, 80);
            s.Loop(5, 8, "both", 126, 5, Mastering with { Wrong = 0.02, Rush = 5.5, SpreadMs = 21 }, Mastering with { Wrong = 0.008, Rush = 3.8, SpreadMs = 18 }, 70);
            s.Run(1, "both", 120, Mastering with { Wrong = 0.006, Rush = 3.2 }, 100);
            s.Run(1, "both", 115, Mastering, 90);
            s.Run(1, "both", 115, Mastering with { Wrong = 0.001, Missed = 0 }, 80);
            if (minutes < 60) return;

            s.Loop(13, 16, "both", 120, 4, Mastering with { Wrong = 0.012, Rush = 4 }, Mastering with { Rush = 2.8 }, 70);
            s.Run(1, "both", 126, Mastering with { Wrong = 0.016, Missed = 0.004, Rush = 5.2, SpreadMs = 22 }, 90);
            s.Run(17, "both", 120, Mastering with { Wrong = 0.005, Rush = 3.2 }, 80);
            s.Run(1, "both", 115, Mastering with { Wrong = 0.001, Missed = 0 }, 90);
            s.Tired = 1.6;
            s.Run(1, "both", 115, Mastering with { Wrong = 0.006, Rush = 3 }, 100);
            s.Tired = 2.2;
            s.Run(1, "both", 115, Mastering with { Wrong = 0.008, Rush = 3.4, SpreadMs = 19 }, 100);
        },
        "first" => s =>
        {
            // The first sitting: reading it a hand at a time, the first four bars together.
            s.Loop(1, 8, "right", 0, 4, new Waited(0.32, 0, 1.6), new Waited(0.12, 0, 1.0), 50);
            s.Loop(1, 8, "left", 0, 3, new Waited(0.28, 0, 1.6), new Waited(0.10, 0, 1.1), 70);
            s.Loop(1, 4, "both", 0, 4, new Waited(0.38, 0.30, 2.3), new Waited(0.16, 0.12, 1.5), 90);
        },
        _ => s =>
        {
            // Through it, the bars that went wrong, and through it again.
            s.Run(1, "both", 72, General, 30);
            s.Loop(5, 8, "both", 66, 6, General with { Wrong = 0.07, Missed = 0.04, Rush = 5 }, General with { Wrong = 0.02, Missed = 0.01, Rush = 2 }, 70);
            s.Loop(5, 8, "both", 72, 4, General with { Wrong = 0.05, Rush = 4.5 }, General with { Wrong = 0.025, Rush = 3 }, 40);
            s.Loop(21, 24, "both", 0, 2, new Waited(0.22, 0.12, 1.5), new Waited(0.10, 0.05, 1.2), 80);
            s.Loop(21, 24, "both", 60, 6, General with { Wrong = 0.10, Missed = 0.05 }, General with { Wrong = 0.03, Missed = 0.015 }, 40);
            s.Loop(21, 24, "both", 72, 3, General with { Wrong = 0.07, Missed = 0.03 }, General with { Wrong = 0.05, Missed = 0.02 }, 40);
            s.Loop(13, 16, "both", 72, 4, General with { Wrong = 0.06 }, General with { Wrong = 0.025 }, 70);
            s.Run(1, "both", 72, General with { Wrong = 0.035, Missed = 0.018, Rush = 3.2 }, 110);
            if (minutes < 40) return;

            // A hand at a time where it's hardest, the last line, the second half, and a step faster.
            s.Loop(20, 24, "left", 72, 4, General with { Wrong = 0.05, Missed = 0.03 }, General with { Wrong = 0.01, Missed = 0.005 }, 90);
            s.Loop(5, 8, "right", 72, 4, General with { Wrong = 0.04 }, General with { Wrong = 0.01, Rush = 2 }, 60);
            s.Loop(28, 31, "both", 66, 6, General with { Wrong = 0.09, Missed = 0.05, Rush = 5 }, General with { Wrong = 0.03, Missed = 0.015, Rush = 2.5 }, 80);
            s.Loop(28, 31, "both", 72, 4, General with { Wrong = 0.06, Rush = 4 }, General with { Wrong = 0.03, Rush = 3 }, 40);
            s.Run(17, "both", 72, General with { Wrong = 0.045, Missed = 0.02 }, 90);
            s.Run(17, "both", 72, General with { Wrong = 0.03, Missed = 0.012 }, 60);
            s.Loop(5, 8, "both", 80, 4, General with { Wrong = 0.08, Missed = 0.04, Rush = 6 }, General with { Wrong = 0.06, Missed = 0.03, Rush = 5 }, 70);
            s.Run(1, "both", 76, General with { Wrong = 0.04, Missed = 0.02, Rush = 3.6 }, 110);
            if (minutes < 60) return;

            // More of it, faster than it holds, and tiring.
            s.Loop(9, 12, "both", 72, 5, General with { Wrong = 0.06, Missed = 0.03 }, General with { Wrong = 0.02, Missed = 0.01 }, 80);
            s.Loop(1, 4, "both", 80, 4, General with { Wrong = 0.04 }, General with { Wrong = 0.02 }, 60);
            s.Tired = 1.4;
            s.Run(1, "both", 80, General with { Wrong = 0.055, Missed = 0.03, Rush = 5 }, 100);
            s.Tired = 1.8;
            s.Loop(21, 24, "both", 72, 5, General with { Wrong = 0.06, Missed = 0.03 }, General with { Wrong = 0.07, Missed = 0.04 }, 80);
            s.Run(1, "both", 84, General with { Wrong = 0.07, Missed = 0.05, Rush = 6 }, 70, stopAt: 7);
            s.Tired = 2.0;
            s.Run(1, "both", 72, General with { Wrong = 0.04, Missed = 0.022, Rush = 3.6 }, 110);
        },
    };

    // How each plays In time, to start from: the share of notes wrong and missed, slips, a lean and a spread in ms, how much
    // of a beat is gained each beat through a bar of running eighths, the share of notes let go early, and how much of the
    // loud and soft and the touch is played as marked.
    private static readonly Timed Learning = new(0.14, 0.16, 0.05, 30, 70, 0, 0.30, 0, 0.2);
    private static readonly Timed General = new(0.05, 0.03, 0.03, -15, 36, 4, 0.15, 0.2, 0.4);
    private static readonly Timed Phrasing = new(0.012, 0.004, 0.01, -22, 27, 6.5, 0.20, 0.25, 0.5);
    private static readonly Timed Mastering = new(0.004, 0.001, 0.004, -8, 15, 2, 0.04, 0.85, 0.92);
}

/// <summary>How a pretend pianist plays In time.</summary>
sealed record Timed(double Wrong, double Missed, double Slip, double LeanMs, double SpreadMs, double Rush, double Early, double Shape, double Touch)
{
    public static Timed Between(Timed a, Timed b, double t)
    {
        double At(double from, double to) => from + (to - from) * t;
        return new(At(a.Wrong, b.Wrong), At(a.Missed, b.Missed), At(a.Slip, b.Slip), At(a.LeanMs, b.LeanMs), At(a.SpreadMs, b.SpreadMs), At(a.Rush, b.Rush), At(a.Early, b.Early), At(a.Shape, b.Shape), At(a.Touch, b.Touch));
    }
}

/// <summary>How one plays in Wait for me: the chance of a wrong key at a step, of a chord played a key at a time, and the seconds a step.</summary>
sealed record Waited(double Wrong, double Apart, double StepSeconds)
{
    public static Waited Between(Waited a, Waited b, double t) => new(a.Wrong + (b.Wrong - a.Wrong) * t, a.Apart + (b.Apart - a.Apart) * t, a.StepSeconds + (b.StepSeconds - a.StepSeconds) * t);
}

/// <summary>
/// A pretend pianist on a piece: plays a stretch of it as a <see cref="Timed"/> or a <see cref="Waited"/> says, keys, times,
/// lengths and loudness, and has the app's own scoring judge it. Bars with more notes go worse, the player's own hard bars
/// worse still, and bars of running eighths are the ones that run away.
/// </summary>
sealed class Pianist
{
    private readonly Score score;
    private readonly Rng rng;
    private readonly Dictionary<int, double> hard;
    private readonly HashSet<int> runs;
    private readonly Dictionary<string, Spelled> pitches;

    public Pianist(Score score, Rng rng, string who)
    {
        (this.score, this.rng) = (score, rng);
        var written = score.Expected();
        BarOf = written.ToDictionary(n => n.Id, n => n.Bar);
        pitches = score.Bars.SelectMany(b => b.Voices.SelectMany(v => v.Events.OfType<Notes>().SelectMany(n => n.Tones))).GroupBy(t => t.Id).ToDictionary(g => g.Key, g => g.First().Pitch);
        var counts = written.GroupBy(n => n.Bar).ToDictionary(g => g.Key, g => g.Count());
        var (least, most) = (counts.Values.Min(), counts.Values.Max());
        // The bars each player finds hard: where the hands both move, and the turn to D major with its C sharps.
        int[] own = who switch { "mastering" => [21, 29], "phrasing" => [7, 21, 22], "learning" or "first" => [7, 8, 12, 21, 22, 23], _ => [7, 14, 21, 22, 29] };
        hard = counts.ToDictionary(c => c.Key, c => (0.6 + 0.8 * (c.Value - least) / Math.Max(1, most - least)) * (own.Contains(c.Key) ? 2.4 : 1));
        // The bars that run away: the right hand's running eighths, where the left hand moves under them or the line turns.
        runs = [.. new[] { 5, 6, 7, 13, 14, 21, 28, 29 }.Where(counts.ContainsKey)];
    }

    public Score Score => score;

    public IReadOnlyDictionary<string, int> BarOf { get; }

    public int FirstBar => score.Bars[0].Number;

    public int LastBar => score.Bars[^1].Number;

    /// <summary>The music of a stretch, as the piece's page makes it: from a bar on through the repeats, or a loop's bars once round.</summary>
    public PlayMusic Music(int from, int? to, string hands)
    {
        var first = from - FirstBar;
        IReadOnlyList<int> order = to is { } last ? [.. Enumerable.Range(first, last - from + 1)] : score.PlayOrder(first);
        var start = score.BarStarts().First(b => b.Bar.Number == from).Start;
        int? staff = hands switch { "right" => 1, "left" => 2, _ => null };
        return new PlayMusic(score, [.. score.Performed(order).Where(n => staff is null || n.Staff == staff)], Math.Max(0, start)) { Order = order, LoopBars = to is null ? null : order.Count };
    }

    /// <summary>A stretch played In time and scored: its result, the keys played, the marks it left, and the seconds it took with its count-in.</summary>
    public (PassageResult Result, List<PlayedNote> Played, List<SheetMark> Marks, double Seconds) InTime(PlayMusic music, double tempo, Timed how, double tired, bool expression, int? stopAt = null)
    {
        var (beatTicks, beatNs) = (music.BeatTicks, 60e9 / tempo);
        var from = music.FromTick / (double)beatTicks;
        const long start = 5_000_000_000;
        Func<double, long> timeAt = beat => start + (long)((beat - from) * beatNs);
        // Stopped at a bar: what was due before its first time round.
        var notes = music.Notes;
        if (stopAt is { } bar && notes.FirstOrDefault(n => n.Bar >= bar) is { } reached) notes = [.. notes.Where(n => n.Tick < reached.Tick)];
        var markings = Markings.Of(score, notes);
        var barTicks = score.Meter.BarTicks;
        var played = new List<PlayedNote>();
        foreach (var note in notes)
        {
            var harder = hard.GetValueOrDefault(note.Bar, 1) * tired;
            var into = (note.Tick - music.FromTick) % barTicks / (double)beatTicks;
            var rush = runs.Contains(note.Bar) ? -how.Rush / 100 * into * beatNs * Math.Min(1.6, tired) : 0;
            var on = timeAt(note.Tick / (double)beatTicks) + (long)(how.LeanMs * 1e6 + Gauss() * how.SpreadMs * 1e6 * Math.Sqrt(Math.Min(2, harder)) + rush);
            var written = note.Length / (double)beatTicks * beatNs;
            // How long it's held: a staccato note short (or not), a note let go early, a slurred note into the next (or not).
            var held = note.Articulation == Articulation.Staccato ? rng.Chance(how.Touch) ? Math.Min(0.35 * written, 0.2e9) : 0.92 * written
                : rng.Chance(how.Early * (note.Staff == 2 ? 1.5 : 0.7)) ? written * (0.5 + 0.2 * rng.Double())
                : note.Slur is not null ? rng.Chance(how.Touch) ? written * 1.02 : written * 0.84
                : written * (0.9 + 0.08 * rng.Double());
            // How hard: every note much the same, or nearer what the dynamic and the hairpin there ask.
            var level = markings.Dynamics.LastOrDefault(d => d.From <= note.Tick)?.Level;
            var asked = level switch { "pp" => 34, "p" => 45, "mp" => 58, "mf" => 71, "f" => 86, "ff" => 97, _ => 64 };
            var swell = markings.Hairpins.FirstOrDefault(h => h.From <= note.Tick && note.Tick <= h.To);
            var grown = swell is null ? 0 : (swell.Crescendo ? 1 : -1) * 16 * (note.Tick - swell.From) / (double)Math.Max(1, swell.To - swell.From);
            var velocity = (int)Math.Clamp(Math.Round(64 + (asked - 64 + grown) * how.Shape + Gauss() * 3.5), 8, 124);

            var chance = rng.Double();
            var (missed, wrong) = (Math.Min(0.3, how.Missed * harder), Math.Min(0.3, how.Wrong * harder));
            if (chance < missed) continue;
            if (chance < missed + wrong)
            {
                // Mostly a key or two away, else another note altogether.
                var away = rng.Chance(0.65) ? rng.Chance(0.5) ? 1 : 2 : 3 + rng.Int(3);
                played.Add(new PlayedNote(note.Midi + (rng.Chance(0.5) ? away : -away), on, on + (long)held, velocity));
                continue;
            }
            if (rng.Chance(how.Slip * harder)) played.Add(new PlayedNote(note.Midi + (rng.Chance(0.5) ? 1 : -1), on - 70_000_000, on - 20_000_000, velocity));
            played.Add(new PlayedNote(note.Midi, on, on + (long)held, velocity));
        }

        var result = PassageScorer.Score(notes, timeAt, beatTicks, beatNs, beatNs * score.Meter.Beats, played, 0, music.Ornamented, [], expression ? markings : null, null, music.Rolled);
        List<SheetMark> marks = [.. result.Notes.Select(n => n.Outcome switch
        {
            Outcome.Wrong when n.Played is { } key => Mark(n.Id, LiveKind.Wrong, key),
            Outcome.Missed => Mark(n.Id, LiveKind.Missed, null),
            _ when n.Band == Band.Off => Mark(n.Id, n.ErrorPercent < 0 ? LiveKind.Early : LiveKind.Late, null),
            _ => Mark(n.Id, LiveKind.Right, null),
        })];
        var beats = notes.Count == 0 ? 0 : notes.Max(n => n.Tick + n.Length - music.FromTick) / (double)beatTicks;
        return (result, played, marks, (beats + score.Meter.Beats) * 60 / tempo);
    }

    /// <summary>A stretch played in Wait for me and judged: its result, the keys, the marks it left, and the seconds it took.</summary>
    public (WaitResult Result, List<PlayedNote> Played, List<SheetMark> Marks, double Seconds) Wait(PlayMusic music, Waited how, double tired, int? stopAt = null)
    {
        var judge = new WaitJudge(music.Notes, music.Ornamented, music.Rolled);
        var drawn = new Dictionary<string, SheetMark>();
        var heard = new List<PlayedNote>();
        long at = 1_000_000_000;
        void Key(int midi)
        {
            var (marks, _) = judge.Play(midi, at);
            foreach (var mark in marks) drawn[PlayMusic.SheetId(mark.Id)] = Mark(mark.Id, mark.Kind, mark.Played);
            heard.Add(new PlayedNote(midi, at, at, 70));
        }

        var began = at;
        // Stopped at a bar: its first time through, up to that bar and no further (a repeat isn't gone back for).
        while (!judge.Done && (stopAt is not { } last || judge.Current[0].Bar <= last && !judge.Current[0].Id.Contains('~')))
        {
            var step = judge.Current;
            var harder = hard.GetValueOrDefault(step[0].Bar, 1) * tired;
            at += (long)(how.StepSeconds * 0.75 * (0.7 + 0.6 * rng.Double()) * Math.Sqrt(Math.Min(2.5, harder)) * 1e9);
            if (rng.Chance(Math.Min(0.6, how.Wrong * harder)))
            {
                var wrong = step[0].Midi + (rng.Chance(0.5) ? 1 : -1);
                while (step.Any(n => n.Midi == wrong)) wrong++;
                Key(wrong);
                at += 700_000_000;
            }
            if (step.Count > 1 && rng.Chance(Math.Min(0.6, how.Apart * harder)))
            {
                foreach (var note in step)
                {
                    Key(note.Midi);
                    at += 450_000_000;
                }
                at += 300_000_000;
            }
            foreach (var note in step)
            {
                Key(note.Midi);
                at += 12_000_000;
            }
        }
        return (judge.Result(), heard, [.. drawn.Values], (at - began) / 1e9);
    }

    // A note's mark on the sheet, as the play bar makes it: a wrong one with the key played, named, and how far from the written one.
    private SheetMark Mark(string id, LiveKind kind, int? key)
    {
        var (name, sheetId) = (kind.ToString().ToLowerInvariant(), PlayMusic.SheetId(id));
        if (kind != LiveKind.Wrong || key is not { } played || !pitches.TryGetValue(sheetId, out var written)) return new SheetMark(sheetId, name);

        var spelled = score.Key.Spell(played);
        return new SheetMark(sheetId, name, spelled.ToString(), spelled.Diatonic - written.Diatonic, Written: written.ToString());
    }

    private double Gauss() => Math.Sqrt(-2 * Math.Log(Math.Max(1e-12, rng.Double()))) * Math.Cos(2 * Math.PI * rng.Double());
}

/// <summary>
/// One sitting at the piece: its runs in order, each with the rest before it (looking at the music, a breath) and the time it
/// took, to be kept as a practice session of a given length: the rests stretch or shrink to fit it.
/// </summary>
sealed class Sitting(Pianist pianist)
{
    private readonly List<(double Pause, double Seconds, PlayOutcome How, int From, string Hands, double? Tempo)> runs = [];

    /// <summary>How tired the player is: what goes wrong goes wrong this much more.</summary>
    public double Tired { get; set; } = 1;

    public double LastTempo { get; private set; } = 72;

    /// <summary>The sitting's length with its rests as written.</summary>
    public TimeSpan Natural => TimeSpan.FromSeconds(runs.Sum(r => r.Pause + r.Seconds) + 30);

    /// <summary>Round a loop: In time at a tempo, or in Wait for me with tempo 0; its passes go from how the first is played to how the last is.</summary>
    public void Loop(int from, int to, string hands, double tempo, int passes, object first, object last, double rest)
    {
        var music = pianist.Music(from, to, hands);
        var each = new List<PiecePass>();
        var seconds = 0.0;
        PlayOutcome? how = null;
        for (var i = 0; i < passes; i++)
        {
            var t = passes == 1 ? 1 : i / (double)(passes - 1);
            if (tempo > 0)
            {
                var (result, played, marks, took) = pianist.InTime(music, tempo, Timed.Between((Timed)first, (Timed)last, t), Tired, expression: i == passes - 1);
                each.Add(new PiecePass(result.Right, result.Expected, result.InTimeAccuracy) { Trouble = Trouble.Of(result.Notes) });
                // One count-in for the loop, then round and round.
                seconds += took - (i == 0 ? 0 : pianist.Score.Meter.Beats * 60 / tempo);
                how = new PlayOutcome(PlayMode.InTime, result, null, played, true, to) { Loop = (from, to), Passes = [.. each], Marks = marks };
            }
            else
            {
                var (result, played, marks, took) = pianist.Wait(music, Waited.Between((Waited)first, (Waited)last, t), Tired);
                each.Add(new PiecePass(result.RightFirstTime, result.Notes, null) { Trouble = Trouble.Of(marks, pianist.BarOf), Seconds = took });
                seconds += took;
                how = new PlayOutcome(PlayMode.WaitForMe, null, result, played, true, to) { Loop = (from, to), Passes = [.. each], Marks = marks };
            }
        }
        Add(rest, seconds, how!, from, hands, tempo > 0 ? tempo : null);
    }

    /// <summary>A run In time from a bar to the end, through the repeats, or stopped at a bar.</summary>
    public void Run(int from, string hands, double tempo, Timed how, double rest, int? stopAt = null)
    {
        var (result, played, marks, seconds) = pianist.InTime(pianist.Music(from, null, hands), tempo, how, Tired, expression: true, stopAt);
        Add(rest, seconds, new PlayOutcome(PlayMode.InTime, result, null, played, stopAt is not null, stopAt ?? pianist.LastBar) { Marks = marks }, from, hands, tempo);
    }

    /// <summary>A run in Wait for me from a bar to the end, or stopped at a bar.</summary>
    public void Wait(int from, string hands, Waited how, double rest, int? stopAt = null)
    {
        var (result, played, marks, seconds) = pianist.Wait(pianist.Music(from, null, hands), how, Tired, stopAt);
        Add(rest, seconds, new PlayOutcome(PlayMode.WaitForMe, null, result, played, stopAt is not null, stopAt is { } last ? last + 1 : pianist.LastBar) { Marks = marks }, from, hands, null);
    }

    private void Add(double rest, double seconds, PlayOutcome how, int from, string hands, double? tempo)
    {
        runs.Add((rest, seconds, how, from, hands, tempo));
        if (tempo is { } at) LastTempo = at;
    }

    /// <summary>
    /// Keeps the sitting as a practice session that began at <paramref name="began"/> and lasted <paramref name="length"/>: each
    /// run as the piece's page keeps one, and the session's clocks, its playing time the runs' own, a little after each, and a
    /// fifth of each rest (a bar gone over with the music stopped). Finished, or left going under its cover.
    /// </summary>
    public async Task<string> Keep(PrimaStore store, DateTimeOffset began, TimeSpan length, bool finished)
    {
        var piece = (await store.GetPieceAsync(PracticeSeeds.Minuet))!;
        var id = await store.StartSessionAsync(piece.Id, "standard", began);
        var stretch = Math.Max(0.15, (length.TotalSeconds - runs.Sum(r => r.Seconds)) / Math.Max(1, runs.Sum(r => r.Pause) + 30));
        var (at, playing) = (began, 0.0);
        foreach (var (rest, seconds, how, from, hands, tempo) in runs)
        {
            var paused = rest * stretch;
            playing += seconds + Math.Min(10, paused) + Math.Max(0, paused - 10) * 0.2;
            at = at.AddSeconds(paused + seconds);
            var (right, notes, clean) = KeptRun.Figures(how);
            await store.AddAttemptAsync(new AttemptRecord(0, at, DateOnly.FromDateTime(at.LocalDateTime), "piece", how.Mode == PlayMode.InTime ? "in time" : "wait", piece.Id, piece.Level, tempo, null, null,
                KeptRun.Keys(how), null, KeptRun.Measures(how, from, hands, id, false, pianist.BarOf, seconds), notes == 0 ? null : right / (double)notes, clean, null));
        }
        var total = (at - began).TotalSeconds + 30 * stretch;
        var session = new PracticeSession(id, piece.Id, began, TimeSpan.FromSeconds(playing), TimeSpan.FromSeconds(Math.Max(0, total - playing)), true, at, "standard", finished ? at : null);
        await store.SaveSessionAsync(session);
        await store.AddMinutesAsync(DateOnly.FromDateTime(began.LocalDateTime), playing / 60);
        return $"{began.LocalDateTime:ddd d MMM HH:mm} · {runs.Count} runs · {playing / 60:0.0} min playing, {(total - playing) / 60:0.0} idle{(finished ? "" : " · still going")}";
    }
}
