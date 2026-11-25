using Prima.Core.Scoring;
using Prima.UI.Practice;

namespace Prima.UI.Host;

/// <summary>
/// What the stand-in piano plays when Prima is tried without one: the run in progress played through, a drill's item or Wait for
/// me's next notes answered, or keys now. The preview's /keys routes and a build from the PC (from DevTools) both play it.
/// </summary>
public sealed class StandInPlayer
{
    private readonly StandInKeyboard keys;
    private readonly SessionRegistry registry;
    private readonly IClock clock;

    public StandInPlayer(StandInKeyboard keys, SessionRegistry registry, IClock clock)
    {
        (this.keys, this.registry, this.clock) = (keys, registry, clock);
#if DEBUG
        // Reached from DevTools in a build from the PC.
        StandInCalls.Player = this;
#endif
    }

    /// <summary>
    /// How a run is played: a share of its notes right (the rest a key or two away), a lean and a spread in ms, and optionally
    /// nothing from a bar on (a stop), a share of notes with a slip (the key above, just before the right one), a share let go at
    /// 60% of their length, and every chord rolled (the ms between its keys). In one go, ending the run at once; or live, each note
    /// when it's due, as from the piano, so the music is marked as it goes.
    /// </summary>
    public sealed record Performance(double Right = 0.95, double Lean = -15, double Spread = 12, int? Stop = null, bool Live = false,
        double? Slips = null, double? Early = null, int? Roll = null);

    /// <summary>The stand-in plugged in, or out.</summary>
    public string Plug(bool connected, string name = "P-225B")
    {
        keys.Plug(connected, name);
        return connected ? "connected" : "disconnected";
    }

    /// <summary>Keys played now, held this long.</summary>
    public string Play(IReadOnlyList<int> notes, int velocity = 80, int holdMs = 300)
    {
        keys.Play(notes, velocity, holdMs);
        return $"played {string.Join(',', notes)}";
    }

    /// <summary>The run in progress, played as <paramref name="how"/> says.</summary>
    public string Perform(Performance how)
    {
        var session = registry.Current;
        if (session?.Plan is null) return "no run in progress";

        var random = new Random(9);
        var beatsPerBar = session.Plan.BeatsPerBar;
        var notes = session.Expected
            .Where(e => how.Stop is null || e.Beat < (how.Stop.Value - 1) * beatsPerBar)
            .SelectMany(e =>
            {
                var midi = random.NextDouble() < how.Right ? e.Midi : e.Midi + (random.NextDouble() < 0.5 ? 1 : 2);
                var rolledBy = how.Roll is { } gap ? session.Expected.Count(o => o.Beat == e.Beat && o.Midi < e.Midi) * gap : 0;
                var on = session.TimeAt(e.Beat) + (long)((how.Lean + rolledBy + (random.NextDouble() - 0.5) * 2 * how.Spread) * 1e6);
                var (start, end) = (session.TimeAt(e.Beat), session.TimeAt(e.Beat + e.Beats));
                var off = how.Early is { } share && random.NextDouble() < share ? on + (long)((end - start) * 0.6) : end - 30_000_000;
                var note = new PlayedNote(midi, on, off, 60 + random.Next(20));
                return how.Slips is { } slip && midi == e.Midi && random.NextDouble() < slip ? [new(midi + 1, on - 60_000_000, on - 20_000_000, 60), note] : new[] { note };
            }).ToList();
        if (!how.Live)
        {
            session.Perform(notes);
            return $"performed {notes.Count} notes";
        }

        _ = Task.Run(async () =>
        {
            foreach (var note in notes.OrderBy(n => n.OnNs))
            {
                var wait = (note.OnNs - clock.NowNs) / 1e6;
                if (wait > 0) await Task.Delay(TimeSpan.FromMilliseconds(wait));
                // The run stopped, or another begun (a looped run is half an hour of notes): the stand-in stops with it, and plays
                // nothing into whatever comes next.
                if (registry.Current != session || session.Phase is not (Phase.CountIn or Phase.Playing)) return;
                keys.PlayAt(note.Midi, note.Velocity, note.OnNs, note.OffNs);
            }
        });
        return $"playing {notes.Count} notes live";
    }

    /// <summary>
    /// Answers what's waited for on the keys (a drill's item, or Wait for me's next notes): right, or each key a semitone up. A
    /// step's keys go down together, or with <paramref name="apartMs"/> one at a time, that many ms after the one before, as a
    /// chord played a note at a time.
    /// </summary>
    public async Task<string> Answer(bool right = true, int? apartMs = null)
    {
        if (registry.DrillAnswer is not { } answer) return "no drill item";

        foreach (var step in answer(right))
        {
            if (apartMs is not { } ms)
            {
                keys.Play(step, 70, 200);
                continue;
            }
            foreach (var key in step)
            {
                keys.Play([key], 70, 200);
                await Task.Delay(ms);
            }
        }
        return "answered";
    }
}
