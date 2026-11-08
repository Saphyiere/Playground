using Prima.Core.Music;

namespace Prima.Core.Scoring;

/// <summary>How a note's articulation was played, when it wasn't as asked.</summary>
public enum Touch
{
    /// <summary>A staccato note whose key stayed down longer than a short note's.</summary>
    StaccatoHeldLong,
    /// <summary>A tenuto note not held its whole length.</summary>
    TenutoShort,
    /// <summary>Under a slur, let go before the next note came: the line broken.</summary>
    GapUnderSlur,
    /// <summary>An accented note (or a sforzando) struck no harder than the notes around it.</summary>
    AccentNotStronger,
}

/// <summary>How a stretch marked with a dynamic was played, from how hard its keys were struck.</summary>
public enum Loudness
{
    Right,
    TooLoud,
    TooSoft,
    /// <summary>Marked louder or softer than the stretch before it, and played no differently.</summary>
    NoChange,
}

/// <summary>How a crescendo or diminuendo was played.</summary>
public enum Swell
{
    Right,
    /// <summary>It neither grew nor faded.</summary>
    Flat,
    /// <summary>It went the other way.</summary>
    WrongWay,
}

/// <summary>How a stretch marked for the damper pedal was pedalled.</summary>
public enum Pedalling
{
    Right,
    NotPressed,
    /// <summary>Pressed more than half a beat after its mark.</summary>
    Late,
    /// <summary>Let up for much of the stretch.</summary>
    LiftedEarly,
    /// <summary>Held on past where it's lifted (or changed), blurring into what comes next.</summary>
    NotLifted,
}

/// <summary>A stretch marked with a dynamic: the bars, the mark, where in your soft-to-loud range it was played, and how.</summary>
public sealed record DynamicResult(string Level, int FromBar, int ToBar, double LevelPercent, Loudness Outcome);

/// <summary>A crescendo or diminuendo: its bars, how much harder (velocity) the keys were struck across it, and how.</summary>
public sealed record HairpinResult(bool Crescendo, int FromBar, int ToBar, double Change, Swell Outcome);

/// <summary>A stretch marked for the damper pedal, from its Ped. to its release, and how it was pedalled.</summary>
public sealed record PedalResult(int FromBar, int ToBar, Pedalling Outcome);

/// <summary>
/// What the music asks for besides its notes, in the ticks of the notes played (<see cref="ExpectedNote.Tick"/>, a repeat's
/// second time included): the dynamic in force over each stretch, crescendos and diminuendos, where the damper pedal is held, and
/// the notes marked sforzando. From the score's own marks and its file's.
/// </summary>
public sealed record Markings(IReadOnlyList<Markings.Dynamic> Dynamics, IReadOnlyList<Markings.Hairpin> Hairpins, IReadOnlyList<Markings.Pedalled> Pedal, IReadOnlySet<string> Stressed)
{
    /// <summary>A dynamic in force from a tick until the next one (or the end).</summary>
    public sealed record Dynamic(string Level, long From, long To);

    public sealed record Hairpin(bool Crescendo, long From, long To);

    /// <summary>The damper pedal held from a tick until another.</summary>
    public sealed record Pedalled(long Down, long Up);

    public static readonly Markings None = new([], [], [], new HashSet<string>());

    /// <summary>The dynamics that set a level, softest first, and the share of your soft-to-loud range each is played in (ours).</summary>
    public static readonly IReadOnlyDictionary<string, (int Rank, double Low, double High)> Levels = new Dictionary<string, (int, double, double)>
    {
        ["ppp"] = (0, 0, 15),
        ["pp"] = (1, 0, 25),
        ["p"] = (2, 10, 40),
        ["mp"] = (3, 30, 55),
        ["mf"] = (4, 45, 70),
        ["f"] = (5, 60, 90),
        ["ff"] = (6, 75, 100),
        ["fff"] = (7, 85, 100),
        // Loud then at once soft: the stretch after it is soft.
        ["fp"] = (2, 10, 40),
    };

    /// <summary>
    /// The markings of <paramref name="notes"/> (a run's: some of the score's, maybe played twice through a repeat) from
    /// <paramref name="score"/>'s marks: each mark at its note's tick (and its offset) each time that note is played. The dynamic
    /// in force when the notes start comes from the last one before them in the score.
    /// </summary>
    public static Markings Of(Score score, IReadOnlyList<ExpectedNote> notes)
    {
        if (notes.Count == 0) return None;

        // Each written note and chord, each time it's played: its tick and how long it sounds.
        var played = new Dictionary<string, Dictionary<string, (long Tick, int Length)>>();
        foreach (var n in notes)
        {
            foreach (var id in n.ChordId is { } chord ? [n.WrittenId, chord.Split('~')[0]] : new[] { n.WrittenId })
            {
                if (!played.TryGetValue(id, out var passes)) played[id] = passes = [];
                passes[n.Pass] = passes.TryGetValue(n.Pass, out var had) ? (had.Tick, Math.Max(had.Length, n.Length)) : (n.Tick, n.Length);
            }
        }

        var marks = score.AllMarks.ToList();
        var start = notes.Min(n => n.Tick);
        var end = notes.Max(n => n.Tick + n.Length);
        var dynamics = new List<(long Tick, string Level)>();
        var hairpins = new List<(bool Crescendo, long From, long? To)>();
        var pedal = new List<(long Tick, bool Down, long AnchorEnd)>();
        var stressed = new HashSet<string>();
        foreach (var mark in marks)
        {
            if (!played.TryGetValue(mark.StartId, out var starts)) continue;

            foreach (var (pass, at) in starts)
            {
                var tick = at.Tick + mark.StartOffset;
                long? to = mark.EndId is { } endId && played.TryGetValue(endId, out var ends) && ends.TryGetValue(pass, out var e) ? e.Tick + mark.EndOffset : null;
                switch (mark.Kind)
                {
                    case MarkKind.Dynamic when mark.Text is { } text && Levels.ContainsKey(text):
                        dynamics.Add((tick, text));
                        break;
                    case MarkKind.Dynamic when mark.Text is "sf" or "sfz" or "fz" or "rf" or "rfz":
                        stressed.UnionWith(notes.Where(n => n.Tick == tick && n.Pass == pass).Select(n => n.Id));
                        break;
                    case MarkKind.Crescendo or MarkKind.Diminuendo:
                        hairpins.Add((mark.Kind == MarkKind.Crescendo, tick, to));
                        break;
                    case MarkKind.PedalDown or MarkKind.PedalUp:
                        pedal.Add((tick, mark.Kind == MarkKind.PedalDown, at.Tick + at.Length));
                        break;
                }
            }
        }

        // The dynamic in force as the notes start, when none is marked at their first: the score's last before them.
        var written = new Dictionary<string, long>();
        foreach (var n in score.Expected())
        {
            written[n.Id] = n.Tick;
            if (n.ChordId is { } chord) written[chord] = n.Tick;
        }
        if (written.TryGetValue(notes.MinBy(n => n.Tick)!.WrittenId, out var firstWritten) && !dynamics.Any(d => d.Tick <= start))
        {
            var before = marks.Where(m => m.Kind == MarkKind.Dynamic && m.Text is { } t && Levels.ContainsKey(t) && written.TryGetValue(m.StartId, out var w) && w + m.StartOffset <= firstWritten)
                .MaxBy(m => written[m.StartId] + m.StartOffset);
            if (before is not null) dynamics.Add((start, before.Text!));
        }

        var levels = dynamics.GroupBy(d => d.Tick).Select(g => g.Last()).OrderBy(d => d.Tick).ToList();
        var stretches = levels.Select((d, i) => new Dynamic(d.Level, d.Tick, i + 1 < levels.Count ? levels[i + 1].Tick : Math.Max(end, d.Tick + 1))).ToList();
        // A crescendo or diminuendo written as a word runs to the next dynamic.
        var swells = hairpins.Select(h => new Hairpin(h.Crescendo, h.From, h.To ?? levels.Select(l => l.Tick).Where(t => t > h.From).DefaultIfEmpty(end).Min())).Where(h => h.To > h.From).ToList();

        // The pedal: down to up; down again while it's held is a change. Marks at one moment keep the order they're written in: up
        // then down is a change (a bounce), down then up on one note holds it for the note.
        var spans = new List<Pedalled>();
        long? held = null;
        foreach (var p in pedal.OrderBy(p => p.Tick))
        {
            if (p.Down)
            {
                if (held is { } was && p.Tick > was) spans.Add(new Pedalled(was, p.Tick));
                held = p.Tick;
            }
            else if (held is { } down)
            {
                var up = p.Tick > down ? p.Tick : Math.Max(p.Tick, p.AnchorEnd);
                if (up > down) spans.Add(new Pedalled(down, up));
                held = null;
            }
        }
        if (held is { } last && end > last) spans.Add(new Pedalled(last, end));

        return new Markings(stretches, swells, spans, stressed);
    }
}
