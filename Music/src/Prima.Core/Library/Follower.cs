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

/// <summary>
/// Follows a player through a score from the notes they play (library.md §6): a windowed chord matcher that moves on once half an
/// event's notes have arrived, never moves back on a wrong note, and re-anchors when the playing matches somewhere else (a skip or
/// a restart). The tempo comes from the last matched events. It only moves the view: marks come afterwards, from
/// <see cref="Alignment"/>.
/// </summary>
public sealed class Follower
{
    /// <summary>Notes this close together are one chord: wider than the papers' 35 ms, for learners (tune it).</summary>
    public const long ChordNs = 70_000_000;

    private readonly IReadOnlyList<ScoreEvent> events;
    private readonly double setTempo;
    private readonly HashSet<int> arrived = [];
    private readonly List<(double Beat, long Ns)> matched = [];
    private readonly List<List<int>> groups = [];
    private long groupStart;
    private long lastNote = long.MinValue;
    private int failures;

    /// <param name="setTempo">The tempo it starts from, in quarter notes a minute: the passage's, not the printed one.</param>
    public Follower(IReadOnlyList<ScoreEvent> events, double setTempo)
    {
        this.events = events;
        this.setTempo = setTempo;
        Tempo = setTempo;
    }

    /// <summary>The next event to be played; the count of events when the end is reached.</summary>
    public int Position { get; private set; }

    /// <summary>The followed tempo, in quarter notes a minute, held between a quarter and one and a half times the set tempo.</summary>
    public double Tempo { get; private set; }

    /// <summary>Notes that matched nothing nearby.</summary>
    public int Extra { get; private set; }

    public bool Finished => Position >= events.Count;

    /// <summary>Takes a note-on. True when the position moved (on, or to a new place on a re-anchor).</summary>
    public bool NoteOn(int midi, long timeNs)
    {
        if (events.Count == 0) return false;

        var before = Position;
        var silence = lastNote != long.MinValue && timeNs - lastNote > TwoBarsNs();
        lastNote = timeNs;
        if (groups.Count == 0 || timeNs - groupStart > ChordNs)
        {
            groups.Add([]);
            if (groups.Count > 4) groups.RemoveAt(0);
            groupStart = timeNs;
        }
        groups[^1].Add(midi);

        // After a long silence a learner usually starts this bar again, or the one before: a note that opens either is taken there.
        if (silence && RestartAt(midi) is { } restart)
        {
            MoveTo(restart);
            Arrive(restart, midi, timeNs);
            failures = 0;
            return true;
        }

        var best = -1;
        var bestScore = 0.0;
        for (var i = Math.Max(0, Position - 1); i < Math.Min(events.Count, Position + 6); i++)
        {
            var score = Credit(events[i].Pitches, midi) - (i < Position ? 0.1 : 0.15 * (i - Position)) - TimingPenalty(i, timeNs);
            if (score > bestScore)
            {
                (best, bestScore) = (i, score);
            }
        }

        if (best >= 0 && bestScore >= 0.5)
        {
            failures = 0;
            if (best >= Position)
            {
                // Past a note or two that weren't played: on, keeping the tempo.
                if (best > Position) (Position, _) = (best, arrived.RemoveWhere(_ => true));
                Arrive(best, midi, timeNs);
            }
            return Position != before;
        }

        Extra++;
        if (++failures >= 3 && Reanchor() is { } at)
        {
            MoveTo(at);
            failures = 0;
        }
        return Position != before;
    }

    /// <summary>Where the cursor is, in quarter notes: on from the last matched event at the tempo, never past the next one unplayed.</summary>
    public double BeatAt(long timeNs)
    {
        if (events.Count == 0) return 0;
        if (matched.Count == 0) return events[Math.Min(Position, events.Count - 1)].Beat;

        var (beat, ns) = matched[^1];
        var on = beat + (timeNs - ns) / 1e9 * Tempo / 60;
        return Position < events.Count ? Math.Min(on, events[Position].Beat) : on;
    }

    private static double Credit(IReadOnlyList<int> pitches, int midi)
    {
        if (pitches.Contains(midi)) return 1;
        if (pitches.Any(p => (Math.Abs(p - midi) % 12) == 0)) return 0.5;
        return pitches.Any(p => Math.Abs(p - midi) <= 2) ? 0.4 : 0;
    }

    // How far a candidate is from where the tempo says the player should be, in beats, a little at a time.
    private double TimingPenalty(int index, long timeNs)
    {
        if (matched.Count == 0) return 0;

        var (beat, ns) = matched[^1];
        var expected = beat + (timeNs - ns) / 1e9 * Tempo / 60;
        return Math.Min(0.3, Math.Abs(events[index].Beat - expected) * 0.05);
    }

    private void Arrive(int index, int midi, long timeNs)
    {
        arrived.Add(midi);
        var e = events[index];
        if (arrived.Count(p => e.Pitches.Contains(p)) * 2 < e.Pitches.Count) return;

        matched.Add((e.Beat, timeNs));
        if (matched.Count > 8) matched.RemoveAt(0);
        UpdateTempo();
        Position = index + 1;
        arrived.Clear();
    }

    // A jump elsewhere (a re-anchor or a restart): the tempo's history belongs to the old place, so it starts afresh from the tempo so far.
    private void MoveTo(int index)
    {
        Position = index;
        arrived.Clear();
        matched.Clear();
    }

    // A weighted slope through the last matched events, recent ones counting more.
    private void UpdateTempo()
    {
        if (matched.Count < 3) return;

        double sw = 0, sx = 0, sy = 0, sxx = 0, sxy = 0;
        for (var i = 0; i < matched.Count; i++)
        {
            var w = i + 1.0;
            var (x, y) = (matched[i].Beat, matched[i].Ns / 1e9);
            (sw, sx, sy, sxx, sxy) = (sw + w, sx + w * x, sy + w * y, sxx + w * x * x, sxy + w * x * y);
        }
        var denominator = sw * sxx - sx * sx;
        if (denominator <= 1e-9) return;

        var secondsPerBeat = (sw * sxy - sx * sy) / denominator;
        if (secondsPerBeat <= 0) return;

        Tempo = Math.Clamp(60 / secondsPerBeat, 0.25 * setTempo, 1.5 * setTempo);
    }

    private long TwoBarsNs()
    {
        var bar = events.Where(e => e.BarStart).Select(e => e.Beat).Take(3).ToList();
        var barBeats = bar.Count >= 2 ? bar[1] - bar[0] : 4;
        return (long)(2 * barBeats * 60 / Tempo * 1e9);
    }

    // The start of the current bar or the one before, if the note opens it.
    private int? RestartAt(int midi)
    {
        var current = Math.Min(Position, events.Count - 1);
        var starts = events.Take(current + 1).Where(e => e.BarStart).Select(e => e.Index).Reverse().Take(2);
        return starts.Cast<int?>().FirstOrDefault(s => Credit(events[s!.Value].Pitches, midi) >= 1);
    }

    // When the last three chords played match three events in a row elsewhere, the player is there: a bar or phrase start is
    // preferred, then the closest to where they were, looking back first (where learners restart).
    private int? Reanchor()
    {
        if (groups.Count < 3) return null;

        var recent = groups.TakeLast(3).ToList();
        int? best = null;
        var bestCost = double.MaxValue;
        for (var s = 0; s + 3 <= events.Count; s++)
        {
            var fits = true;
            for (var k = 0; k < 3 && fits; k++) fits = recent[k].Count(p => events[s + k].Pitches.Contains(p)) * 2 >= recent[k].Count;
            if (!fits) continue;

            var distance = s + 3 - Position;
            var cost = Math.Abs(distance) * (distance < 0 ? 0.5 : 1.0) - (events[s].BarStart ? 4 : 0);
            if (cost < bestCost) (best, bestCost) = (s + 3, cost);
        }
        return best;
    }
}

/// <summary>What became of each written note, after a take: played right, played wrong (and what), or not played.</summary>
public enum Played { Right, Wrong, Missed }

public sealed record AlignedNote(string Id, int Midi, Played Outcome, int? Instead);

/// <summary>
/// Lines a take up with the score once it's over, by the order of the notes (there's no click to time it against): a right note
/// costs nothing, a near miss (a key or two, or an octave) a little, a wrong note more, a note left out or added the most. Give it
/// the stretch that was played: music that comes round again would otherwise match just as well.
/// </summary>
public static class Alignment
{
    public static (IReadOnlyList<AlignedNote> Notes, int Extra) Align(IReadOnlyList<ExpectedNote> expected, IReadOnlyList<(int Midi, long OnNs)> played)
    {
        var score = expected.OrderBy(n => n.Tick).ThenBy(n => n.Midi).ToList();
        // Chords as the player struck them come in any order: within one, low to high, as the score is.
        var take = new List<int>();
        foreach (var chord in Chords(played.OrderBy(p => p.OnNs).ToList())) take.AddRange(chord.Order());

        const double Gap = 0.8;
        var (n, m) = (score.Count, take.Count);
        var cost = new double[n + 1, m + 1];
        for (var i = 0; i <= n; i++) cost[i, 0] = i * Gap;
        for (var j = 0; j <= m; j++) cost[0, j] = j * Gap;
        for (var i = 1; i <= n; i++)
        {
            for (var j = 1; j <= m; j++)
            {
                cost[i, j] = Math.Min(cost[i - 1, j - 1] + Substitution(score[i - 1].Midi, take[j - 1]), Math.Min(cost[i - 1, j] + Gap, cost[i, j - 1] + Gap));
            }
        }

        var notes = new AlignedNote[n];
        var extra = 0;
        var (a, b) = (n, m);
        while (a > 0 || b > 0)
        {
            if (a > 0 && b > 0 && Math.Abs(cost[a, b] - (cost[a - 1, b - 1] + Substitution(score[a - 1].Midi, take[b - 1]))) < 1e-9)
            {
                var right = score[a - 1].Midi == take[b - 1];
                notes[a - 1] = new AlignedNote(score[a - 1].Id, score[a - 1].Midi, right ? Played.Right : Played.Wrong, right ? null : take[b - 1]);
                (a, b) = (a - 1, b - 1);
            }
            else if (a > 0 && Math.Abs(cost[a, b] - (cost[a - 1, b] + Gap)) < 1e-9)
            {
                notes[a - 1] = new AlignedNote(score[a - 1].Id, score[a - 1].Midi, Played.Missed, null);
                a--;
            }
            else
            {
                extra++;
                b--;
            }
        }
        return (notes, extra);
    }

    private static double Substitution(int written, int played) => written == played ? 0 : Math.Abs(written - played) <= 2 || Math.Abs(written - played) % 12 == 0 ? 0.6 : 1.2;

    private static IEnumerable<List<int>> Chords(List<(int Midi, long OnNs)> played)
    {
        var chord = new List<int>();
        var start = long.MinValue;
        foreach (var (midi, on) in played)
        {
            if (chord.Count > 0 && on - start > Follower.ChordNs)
            {
                yield return chord;
                chord = [];
            }
            if (chord.Count == 0) start = on;
            chord.Add(midi);
        }
        if (chord.Count > 0) yield return chord;
    }
}
