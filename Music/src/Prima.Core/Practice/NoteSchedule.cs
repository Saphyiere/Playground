using Prima.Core.Music;

namespace Prima.Core.Practice;

/// <summary>A note card: a note as written in one clef (middle C reads differently in each), its Leitner box and when it's due.</summary>
public sealed record NoteCard(Clef Clef, int Midi, int Box, DateOnly Due, DateOnly Introduced, DateOnly? LastSeen);

/// <summary>One answer to a note card: right or wrong, and how long from showing it to the first note played.</summary>
public sealed record NoteAnswer(DateTimeOffset At, Clef Clef, int Midi, bool Right, double ResponseMs);

/// <summary>
/// Spaced, mixed review of note reading (plan, Note review). Boxes 1 to 6 come back after 0 (this session), 1, 2, 4, 8 and 16
/// days (ours, in the spirit of expanding, half-life-style schedules). Right and fast moves a card up; right but slow is "not
/// yet": it stays and comes back tomorrow; wrong goes back to box 1 and comes again in the same session. New notes arrive in
/// landmark order (a teaching tradition, untested), one to three a day, only after a good day.
/// </summary>
public static class NoteSchedule
{
    public static readonly int[] Gaps = [0, 1, 2, 4, 8, 16];

    public const double FastFloorMs = 800;

    /// <summary>The upper bound: a teacher's 2-second rule, with no data behind it (research §2).</summary>
    public const double FastCeilingMs = 2000;

    /// <summary>New notes are added only when yesterday's review was this right (ours).</summary>
    public const double NewNotesNeed = 0.85;

    /// <summary>Fast means under 1.25 × your median on right answers over the last two weeks, held between 0.8 and 2 s (ours).</summary>
    public static double FastMs(IEnumerable<NoteAnswer> recentAnswers)
    {
        var right = recentAnswers.Where(a => a.Right).Select(a => a.ResponseMs).ToArray();
        return right.Length == 0 ? FastCeilingMs : Math.Clamp(1.25 * Stats.Median(right), FastFloorMs, FastCeilingMs);
    }

    public static NoteCard Answer(NoteCard card, bool right, bool fast, DateOnly today)
    {
        if (!right) return card with { Box = 1, Due = today, LastSeen = today };
        if (!fast) return card with { Due = today.AddDays(1), LastSeen = today };

        var box = Math.Min(Gaps.Length, card.Box + 1);
        return card with { Box = box, Due = today.AddDays(Math.Max(1, Gaps[box - 1])), LastSeen = today };
    }

    /// <summary>Fluent: right and fast in each of the last three answers, over at least two days (ours).</summary>
    public static bool Fluent(IEnumerable<NoteAnswer> cardAnswers, double fastMs)
    {
        var last = cardAnswers.OrderBy(a => a.At).TakeLast(3).ToList();
        return last.Count == 3 && last.All(a => a.Right && a.ResponseMs <= fastMs) && last.Select(a => DateOnly.FromDateTime(a.At.LocalDateTime)).Distinct().Count() >= 2;
    }

    /// <summary>
    /// The order new notes come in: middle C in both clefs, treble G4 and bass F3, treble C5 and bass C3, C6 and C2; then the
    /// notes between, by step and skip from the landmarks; then the ledger lines, the nearest first. White keys only.
    /// </summary>
    public static IReadOnlyList<(Clef Clef, int Midi)> LandmarkOrder { get; } = BuildOrder();

    private static List<(Clef, int)> BuildOrder()
    {
        (Clef Clef, Spelled Pitch)[] landmarks =
        [
            (Clef.Treble, new(Step.C, 0, 4)), (Clef.Bass, new(Step.C, 0, 4)), (Clef.Treble, new(Step.G, 0, 4)), (Clef.Bass, new(Step.F, 0, 3)),
            (Clef.Treble, new(Step.C, 0, 5)), (Clef.Bass, new(Step.C, 0, 3)), (Clef.Treble, new(Step.C, 0, 6)), (Clef.Bass, new(Step.C, 0, 2)),
        ];
        var order = landmarks.Select(l => (l.Clef, l.Pitch.Diatonic)).ToList();
        int Ledgers(Clef clef, int diatonic) => Generation.PassageGenerator.LedgerLines(Spelled.FromDiatonic(diatonic), clef == Clef.Treble ? 1 : 2);
        // Treble from F3 to E6, bass from A1 to G4: three ledger lines out at most.
        var range = new Dictionary<Clef, (int Low, int High)> { [Clef.Treble] = (3 * 7 + 3, 6 * 7 + 2), [Clef.Bass] = (7 + 5, 4 * 7 + 4) };
        for (var distance = 1; distance <= 30; distance++)
        {
            foreach (var (clef, pitch) in landmarks)
            {
                foreach (var d in new[] { pitch.Diatonic - distance, pitch.Diatonic + distance })
                {
                    if (d >= range[clef].Low && d <= range[clef].High && Ledgers(clef, d) == 0 && !order.Contains((clef, d))) order.Add((clef, d));
                }
            }
        }
        foreach (var ledgers in new[] { 1, 2, 3 })
        {
            foreach (var clef in new[] { Clef.Treble, Clef.Bass })
            {
                for (var d = range[clef].Low; d <= range[clef].High; d++)
                {
                    if (Ledgers(clef, d) == ledgers && !order.Contains((clef, d))) order.Add((clef, d));
                }
            }
        }
        return order.Select(o => (o.Clef, Spelled.FromDiatonic(o.Diatonic).Midi)).ToList();
    }

    /// <summary>
    /// Today's review: due cards first, yesterday's slow and missed ones ahead; then new notes, each in a short block next to known
    /// notes that contrast with it; mixed so confusable notes come near each other, and never the same note twice in a row.
    /// </summary>
    public static List<(Clef Clef, int Midi)> Session(IReadOnlyList<NoteCard> cards, IReadOnlyList<NoteAnswer> answers, DateOnly today, int newNotes, IReadOnlySet<Clef> clefs, Generation.Rng rng)
    {
        var yesterday = answers.Where(a => DateOnly.FromDateTime(a.At.LocalDateTime) == today.AddDays(-1)).ToList();
        var fast = FastMs(answers.Where(a => a.At >= DateTimeOffset.Now.AddDays(-14)));
        var shaky = yesterday.Where(a => !a.Right || a.ResponseMs > fast).Select(a => (a.Clef, a.Midi)).ToHashSet();
        var due = cards.Where(c => c.Due <= today && clefs.Contains(c.Clef)).OrderByDescending(c => shaky.Contains((c.Clef, c.Midi))).ThenBy(c => c.Due).ThenBy(c => c.Box).Select(c => (c.Clef, c.Midi)).ToList();

        var queue = Mix(due, rng);
        var goodDay = yesterday.Count == 0 || yesterday.Count(a => a.Right) / (double)yesterday.Count >= NewNotesNeed;
        if (!goodDay) return queue;

        var known = cards.Select(c => (c.Clef, c.Midi)).ToHashSet();
        foreach (var fresh in LandmarkOrder.Where(n => clefs.Contains(n.Clef) && !known.Contains(n)).Take(newNotes))
        {
            // A new note between notes far from it on the staff, so it stands out, then it joins the mix.
            var contrast = known.Where(k => k.Clef == fresh.Clef && Math.Abs(Diatonic(k.Midi) - Diatonic(fresh.Midi)) >= 4).Take(2).ToList();
            queue.Add(fresh);
            foreach (var other in contrast) queue.AddRange([other, fresh]);
            known.Add(fresh);
        }
        return queue;
    }

    private static int Diatonic(int midi) => new Key(0).Spell(midi).Diatonic;

    // Confusable notes are the same line or space in the other clef, a third apart, or octave twins. Each due note is followed by
    // a due note it can be confused with, where there is one, and no note follows itself.
    private static List<(Clef Clef, int Midi)> Mix(List<(Clef Clef, int Midi)> due, Generation.Rng rng)
    {
        bool Confusable((Clef Clef, int Midi) a, (Clef Clef, int Midi) b)
        {
            var (da, db) = (Diatonic(a.Midi), Diatonic(b.Midi));
            var line = a.Clef == Clef.Treble ? da - 30 : da - 18;
            var otherLine = b.Clef == Clef.Treble ? db - 30 : db - 18;
            return (a.Clef != b.Clef && line == otherLine) || (a.Clef == b.Clef && (Math.Abs(da - db) == 2 || Math.Abs(da - db) == 7));
        }

        var left = new List<(Clef, int)>(due);
        var mixed = new List<(Clef, int)>();
        while (left.Count > 0)
        {
            var next = left[0];
            left.RemoveAt(0);
            mixed.Add(next);
            var partner = left.FindIndex(n => Confusable(next, n));
            if (partner < 0) continue;

            mixed.Add(left[partner]);
            left.RemoveAt(partner);
        }

        for (var i = 1; i < mixed.Count; i++)
        {
            if (mixed[i] != mixed[i - 1]) continue;

            var swap = Enumerable.Range(i + 1, Math.Max(0, mixed.Count - i - 1)).FirstOrDefault(j => mixed[j] != mixed[i - 1], -1);
            if (swap > 0) (mixed[i], mixed[swap]) = (mixed[swap], mixed[i]);
        }
        return mixed;
    }
}
