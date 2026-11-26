using System.Globalization;
using Prima.Core.Generation;
using Prima.Core.Music;

namespace Prima.Core.Routines;

/// <summary>
/// RCM's technique from Preparatory A to Level 4 (The Royal Conservatory's Piano Syllabus, 2022 Edition, pages 9 to 38): what
/// each level asks of scales, chords and arpeggios, as Prima's own drills and exercises play them, with RCM's keys and its
/// minimum tempo as the target (the owner, 25 November 2026: "Do the two", RCM's technique up to Level 4 being the second). Facts
/// restated in Prima's words, not a copy of the syllabus; Prima isn't affiliated with RCM. A level's technique is one routine of
/// yours, made when you ask: each activity a key a day, and where RCM asks for hands separately, each hand on a day of its own,
/// so a day holds a few minutes of each. A tempo here is in the activity's own beat: a drill writes RCM's notes (its scales in
/// eighths, as RCM does), so it's RCM's number; an exercise keeps its own notes, so RCM's is turned into its beat at the same
/// number of notes a minute (RCM's triplets at ♩ 50 are this music's quarters at ♩ 150).
/// </summary>
public static class RcmTechnique
{
    private static readonly Key C = new(0), G = new(1), D = new(2), A = new(3), F = new(-1), Bb = new(-2), Eb = new(-3);
    private static readonly Key Am = new(0, true), Em = new(1, true), Bm = new(2, true), Dm = new(-1, true), Gm = new(-2, true), Cm = new(-3, true);

    private const string OneOctave = "RCM asks two octaves; Prima's goes one.";

    /// <summary>The levels, Preparatory A to Level 4, each with its technique.</summary>
    public static IReadOnlyList<RcmLevel> Levels { get; } =
    [
        new("prep-a", "Preparatory A", 9, "the triad sequence broken, in triplets", [
            Exercise("prep-a", "pentascales", "Pentascales, joined", "five-chord", Keys(C, G, D, Am), true, 100, "in quarters", 100, 2,
                "Prima's plays the chord broken before it's played solid."),
            Exercise("prep-a", "detached", "Pentascales, detached", "detached", Keys(C, G, D, Am), true, 100, "in quarters", 100, 2,
                "Prima's ends on the tonic, not on a chord."),
            Exercise("prep-a", "triads", "The triad sequence, solid", "triad-walk", Keys(C), true, 72, "with a chord every two beats", 72, 2,
                "Prima's goes up the scale and back; RCM's goes up."),
        ]),
        new("prep-b", "Preparatory B", 14, null, [
            Exercise("prep-b", "pentascales", "Pentascales, joined", "five-chord", Keys(D, A, F, Em, Dm), true, 60, "in eighths", 120, 2,
                "Prima's plays the chord broken before it's played solid."),
            Exercise("prep-b", "detached", "Pentascales, detached", "detached", Keys(D, A, F, Em, Dm), true, 60, "in eighths", 120, 2,
                "Prima's ends on the tonic, not on a chord."),
            Scales("prep-b", "Scales, one octave", [(C, ScaleKind.Major), (G, ScaleKind.Major), (Am, ScaleKind.NaturalMinor)], true, 1, 60, 3),
            Exercise("prep-b", "contrary", "Contrary motion", "contrary", Keys(C), false, 60, "in eighths", 60, 2, null),
            Exercise("prep-b", "broken", "Tonic triads, broken", "broken-inversions", Keys(C, G, Am), true, 50, "in triplets", 150, 2, null),
        ]),
        new("1", "Level 1", 19, null, [
            Scales("1", "Scales, two octaves", [.. Keys(C, G, F), .. Forms([Am, Em, Dm], ScaleKind.NaturalMinor, ScaleKind.HarmonicMinor)], true, 2, 69, 3),
            Exercise("1", "contrary", "Contrary motion", "contrary", Keys(C), false, 69, "in eighths", 69, 2, OneOctave),
            Exercise("1", "chromatic", "Chromatic scale", "chromatic", Keys(C), true, 69, "in eighths", 69, 2, null),
            Exercise("1", "broken", "Tonic triads, broken", "broken-inversions", Keys(C, G, F, Am, Em, Dm), true, 50, "in triplets", 150, 2, null),
            Exercise("1", "solid", "Tonic triads, solid", "inversions", Keys(C, G, F, Am, Em, Dm), true, 100, "with a chord every two beats", 100, 2, null),
        ]),
        new("2", "Level 2", 25, "the formula pattern", [
            Scales("2", "Scales, two octaves", [.. Keys(G, F, Bb), .. Forms([Em, Dm, Gm], ScaleKind.HarmonicMinor, ScaleKind.MelodicMinor)], true, 2, 80, 3),
            Exercise("2", "chromatic", "Chromatic scale", "chromatic", Keys(G), true, 80, "in eighths", 80, 2, null),
            Exercise("2", "broken", "Tonic triads, broken", "broken-inversions", Keys(G, F, Bb, Em, Dm, Gm), true, 60, "in triplets", 180, 2, null),
            Exercise("2", "solid", "Tonic triads, solid", "inversions", Keys(G, F, Bb, Em, Dm, Gm), true, 112, "with a chord every two beats", 112, 2, null),
        ]),
        new("3", "Level 3", 31, "the formula pattern", [
            Scales("3", "Scales, two octaves", [.. Keys(D, F, Bb), .. Forms([Bm, Dm, Gm], ScaleKind.HarmonicMinor, ScaleKind.MelodicMinor)], false, 2, 80, 3),
            Exercise("3", "chromatic", "Chromatic scale", "chromatic", Keys(D), true, 80, "in eighths", 80, 2, null),
            Exercise("3", "broken", "Tonic triads, broken", "broken-inversions", Keys(D, F, Bb, Bm, Dm, Gm), true, 69, "in triplets", 207, 2, OneOctave),
            Exercise("3", "solid", "Tonic triads, solid", "inversions", Keys(D, F, Bb, Bm, Dm, Gm), true, 120, "with a chord every two beats", 120, 2, OneOctave),
        ]),
        new("4", "Level 4", 38, "the formula pattern", [
            Scales("4", "Scales, two octaves", [.. Keys(D, A, Bb, Eb), .. Forms([Bm, Gm, Cm], ScaleKind.HarmonicMinor, ScaleKind.MelodicMinor)], false, 2, 92, 3),
            Exercise("4", "chromatic", "Chromatic scale", "chromatic", Keys(C), true, 104, "in eighths", 104, 2, null),
            Exercise("4", "broken", "Tonic triads, broken", "broken-inversions", Keys(D, A, Bb, Eb, Bm, Gm, Cm), false, 60, "in triplets", 180, 2, OneOctave),
            Exercise("4", "solid", "Tonic triads, solid", "inversions", Keys(D, A, Bb, Eb, Bm, Gm, Cm), false, 120, "with a chord every two beats", 120, 2,
                "RCM asks two octaves with both hands playing the chords; Prima's goes one, the left hand playing their roots."),
            new("rcm-4-arpeggios", "4", "Arpeggios, two octaves", "drill", "arpeggios", Keys(D, A, Bb, Eb, Bm, Gm, Cm), true, 2, 2, 72, "in eighths", 72, 3, null),
        ]),
    ];

    /// <summary>A level by its key ("prep-a", "1"); null for one there isn't.</summary>
    public static RcmLevel? Level(string key) => Levels.FirstOrDefault(l => l.Key == key);

    /// <summary>The activity a routine's activity is, by its id; null for one that isn't RCM's.</summary>
    public static RcmActivity? Find(string id) => id.StartsWith("rcm-", StringComparison.Ordinal) ? Levels.SelectMany(l => l.Activities).FirstOrDefault(a => a.Id == id) : null;

    private static RcmActivity Exercise(string level, string name, string title, string exercise, IReadOnlyList<(Key, ScaleKind)> keys, bool separately, double rcm, string note,
        double tempo, int minutes, string? differs) =>
        new($"rcm-{level}-{name}", level, title, "exercise", exercise, keys, separately, 0, 0, rcm, note, tempo, minutes, differs);

    private static RcmActivity Scales(string level, string title, IReadOnlyList<(Key, ScaleKind)> keys, bool separately, int octaves, double rcm, int minutes) =>
        new($"rcm-{level}-scales", level, title, "drill", "scales", keys, separately, octaves, 2, rcm, "in eighths", rcm, minutes, null);

    private static (Key, ScaleKind)[] Keys(params Key[] keys) => [.. keys.Select(k => (k, k.Minor ? ScaleKind.NaturalMinor : ScaleKind.Major))];

    // Minor keys each in two forms, one after the other.
    private static IEnumerable<(Key, ScaleKind)> Forms(Key[] keys, ScaleKind first, ScaleKind second) => keys.SelectMany(k => new[] { (k, first), (k, second) });
}

/// <summary>
/// A level of RCM's technique: its key ("prep-a", "1"), its name, the syllabus's page, what of it Prima hasn't yet (null for
/// nothing), and its activities.
/// </summary>
public sealed record RcmLevel(string Key, string Name, int Page, string? Missing, IReadOnlyList<RcmActivity> Activities)
{
    /// <summary>The routine's name: "RCM Level 1 technique".</summary>
    public string Title => $"RCM {Name} technique";

    /// <summary>The line over the routine's tracks.</summary>
    public string Summary =>
        $"What RCM's 2022 piano syllabus asks of {Name}'s technique (page {Page}), a key a day" +
        (Activities.Any(a => a.Separately) ? " and, where it asks for hands separately, each hand on a day of its own" : "") +
        ", with RCM's minimum tempo as the target." +
        (Missing is null ? "" : $" Not in Prima yet: {Missing}.");

    /// <summary>The routine's activities, each as RCM asks it, in its first key.</summary>
    public IReadOnlyList<PreparedRoutineItem> Items => [.. Activities.Select(a => new PreparedRoutineItem(a.Recipe, "", null))];
}

/// <summary>
/// One of a level's activities: its id in a routine ("rcm-1-scales"), its level's key, its name, a drill or an exercise and which,
/// its keys in the order they come round (a minor scale's form with it), whether RCM asks for each hand separately, a drill's
/// octaves and the notes to a beat it's written in, RCM's minimum tempo and the notes it's in, the same tempo in the activity's
/// own beat, its minutes, and how Prima's music differs from what RCM asks, where it does.
/// </summary>
public sealed record RcmActivity(string Id, string Level, string Name, string Kind, string Target, IReadOnlyList<(Key Key, ScaleKind Form)> Keys, bool Separately,
    int Octaves, int PerBeat, double Rcm, string Note, double Tempo, int Minutes, string? Differs)
{
    /// <summary>The level's name: "Level 1".</summary>
    public string LevelName => RcmTechnique.Level(Level)?.Name ?? Level;

    // Each key, and each hand where RCM asks for them separately: what comes round, a day each.
    private IReadOnlyList<(Key Key, ScaleKind Form, HandUse Hands)> Round => Separately
        ? [.. Keys.SelectMany(k => new[] { (k.Key, k.Form, HandUse.Right), (k.Key, k.Form, HandUse.Left) })]
        : [.. Keys.Select(k => (k.Key, k.Form, HandUse.Together))];

    /// <summary>What it is on a day: the key, its form and the hands that come round to that day.</summary>
    public RcmToday On(DateOnly day)
    {
        var round = Round;
        var (key, form, hands) = round[day.DayNumber % round.Count];
        return new(this, key, form, hands);
    }

    /// <summary>The routine's activity, played as RCM asks it in its first key: a drill or an exercise of the catalogue.</summary>
    public RoutineItem Recipe
    {
        get
        {
            var first = Round[0];
            return new RoutineItem
            {
                Id = Id,
                Kind = Kind,
                TargetId = Target,
                Title = Name,
                Purpose = Purpose,
                Minutes = Minutes,
                Hands = first.Hands,
                Mode = "time",
                Tempo = Tempo,
                Fifths = first.Key.Fifths,
                Minor = first.Key.Minor,
                Step = 1,
            };
        }
    }

    /// <summary>
    /// What it's for, in a line: "RCM Level 1: scales, two octaves, a key and a hand a day, in C, G and F major; A, E and D minor,
    /// natural and harmonic."
    /// </summary>
    public string Purpose
    {
        get
        {
            var round = (Keys.Count > 1, Separately) switch { (true, true) => ", a key and a hand a day", (true, false) => ", a key a day", (false, true) => ", a hand a day", _ => "" };
            var keys = Target == "chromatic" ? $"from {Keys[0].Key.Name[..Keys[0].Key.Name.IndexOf(' ')]}" : $"in {KeysSaid}";
            return $"RCM {LevelName}: {char.ToLowerInvariant(Name[0])}{Name[1..]}{round}, {keys}.";
        }
    }

    /// <summary>Its keys in words: "C, G and F major; A, E and D minor, natural and harmonic".</summary>
    public string KeysSaid
    {
        get
        {
            static string Tonic(Key key) => key.Name[..key.Name.IndexOf(' ')];
            static string And(IReadOnlyList<string> words) => words.Count <= 1 ? string.Concat(words) : $"{string.Join(", ", words.Take(words.Count - 1))} and {words[^1]}";
            var majors = Keys.Where(k => !k.Key.Minor).Select(k => k.Key).Distinct().Select(Tonic).ToList();
            var minors = Keys.Where(k => k.Key.Minor).Select(k => k.Key).Distinct().Select(Tonic).ToList();
            var forms = Kind == "drill" && Target == "scales" ? Keys.Where(k => k.Key.Minor).Select(k => k.Form).Distinct().Select(FormName).ToList() : [];
            var parts = new List<string>();
            if (majors.Count > 0) parts.Add($"{And(majors)} major");
            if (minors.Count > 0) parts.Add($"{And(minors)} minor{(forms.Count > 0 ? $", {And(forms)}" : "")}");
            return string.Join("; ", parts);
        }
    }

    /// <summary>RCM's target in a line: "RCM Level 1 asks at least ♩ 50 in triplets: ♩ 150 in this music's beat."</summary>
    public string Asks => $"RCM {LevelName} asks at least ♩ {Number(Rcm)} {Note}{(Tempo != Rcm ? $": ♩ {Number(Tempo)} in this music's beat" : "")}.";

    internal static string FormName(ScaleKind form) => form switch { ScaleKind.HarmonicMinor => "harmonic", ScaleKind.MelodicMinor => "melodic", _ => "natural" };

    internal static string Number(double tempo) => tempo.ToString("0", CultureInfo.InvariantCulture);
}

/// <summary>An activity on a day: the key, its form and the hands that day.</summary>
public sealed record RcmToday(RcmActivity Activity, Key Key, ScaleKind Form, HandUse Hands)
{
    /// <summary>How it's played that day: "A minor, natural, right hand".</summary>
    public string Played
    {
        get
        {
            var key = Activity.Target == "chromatic" ? $"From {Key.Name[..Key.Name.IndexOf(' ')]}" : Key.Name;
            var form = Activity.Kind == "drill" && Activity.Target == "scales" && Key.Minor ? $", {RcmActivity.FormName(Form)}" : "";
            return $"{key}{form}, {(Hands switch { HandUse.Right => "right hand", HandUse.Left => "left hand", _ => "both hands" })}";
        }
    }
}
