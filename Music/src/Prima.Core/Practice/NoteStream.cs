using Prima.Core.Generation;
using Prima.Core.Music;

namespace Prima.Core.Practice;

/// <summary>
/// Endless notes: one note after another until you stop, for reading them faster. Notes come from each clef chosen, on its staff
/// and the spaces just outside it, or out to two ledger lines; with sharps and flats, about a third carry one (never E♯, B♯, C♭
/// or F♭, which are white keys). The same line or space never comes twice running, and a missed note comes back three to five
/// notes later. It keeps the pace: the median time of the last ten right answers, and the best of those so far.
/// </summary>
public sealed class NoteStream
{
    private readonly List<(Clef Clef, Spelled Pitch)> pool;
    private readonly bool accidentals;
    private readonly Rng rng;
    private readonly List<(int Due, Clef Clef, Spelled Pitch)> comeBack = [];
    private readonly List<double> rightMs = [];
    private readonly List<(Clef Clef, Spelled Pitch, bool Right, double Ms)> answers = [];
    private int shown;

    public NoteStream(IReadOnlyCollection<Clef> clefs, bool ledgerLines, bool accidentals, Rng rng)
    {
        pool = [.. clefs.SelectMany(c =>
        {
            var (low, high) = Range(c, ledgerLines);
            return Enumerable.Range(low, high - low + 1).Select(d => (c, Spelled.FromDiatonic(d)));
        })];
        (this.accidentals, this.rng) = (accidentals, rng);
    }

    /// <summary>A clef's notes as diatonic numbers (C4 is 28): its staff and the spaces just outside it, or out to two ledger lines.</summary>
    public static (int Low, int High) Range(Clef clef, bool ledgerLines) => (clef, ledgerLines) switch
    {
        (Clef.Treble, false) => (29, 39),
        (Clef.Treble, true) => (26, 42),
        (Clef.Bass, false) => (17, 27),
        _ => (14, 30),
    };

    /// <summary>The note showing.</summary>
    public (Clef Clef, Spelled Pitch) Current { get; private set; }

    /// <summary>Every answer, in order: the note, whether it was right, and how long it took.</summary>
    public IReadOnlyList<(Clef Clef, Spelled Pitch, bool Right, double Ms)> Answers => answers;

    public int Right => rightMs.Count;

    /// <summary>The last answer's time, right or wrong.</summary>
    public double? LastMs => answers.Count == 0 ? null : answers[^1].Ms;

    /// <summary>The median time of the last ten right answers (of all of them, before there are ten).</summary>
    public double? RecentMs => rightMs.Count == 0 ? null : Stats.Median([.. rightMs.TakeLast(10)]);

    /// <summary>The best of those medians so far, once there have been ten right answers.</summary>
    public double? BestMs { get; private set; }

    /// <summary>The next note: one missed a few notes ago if it's due, otherwise one from the range.</summary>
    public (Clef Clef, Spelled Pitch) Next()
    {
        shown++;
        var last = Current;
        bool Repeats((Clef Clef, Spelled Pitch) n) => shown > 1 && n.Clef == last.Clef && n.Pitch.Diatonic == last.Pitch.Diatonic;
        var due = comeBack.FirstOrDefault(c => c.Due <= shown && !Repeats((c.Clef, c.Pitch)));
        if (due.Due > 0)
        {
            comeBack.Remove(due);
            return Current = (due.Clef, due.Pitch);
        }

        var note = rng.Pick(pool);
        while (pool.Count > 1 && Repeats(note)) note = rng.Pick(pool);
        if (accidentals && rng.Chance(1 / 3.0))
        {
            // A sharp on C, D, F, G or A; a flat on D, E, G, A or B.
            var step = note.Pitch.Step;
            var sharp = step is not (Step.E or Step.B);
            var flat = step is not (Step.C or Step.F);
            var alter = sharp && flat ? (rng.Chance(0.5) ? 1 : -1) : sharp ? 1 : -1;
            note = (note.Clef, note.Pitch with { Alter = alter });
        }
        return Current = note;
    }

    /// <summary>The answer to the note showing: a wrong one brings the note back three to five notes later.</summary>
    public void Answer(bool right, double ms)
    {
        answers.Add((Current.Clef, Current.Pitch, right, ms));
        if (!right)
        {
            comeBack.Add((shown + 3 + rng.Int(3), Current.Clef, Current.Pitch));
            return;
        }

        rightMs.Add(ms);
        if (rightMs.Count >= 10 && RecentMs is { } recent && (BestMs is null || recent < BestMs)) BestMs = recent;
    }
}
