namespace Prima.Core.Routines;

/// <summary>An activity of a routine that comes with Prima: an exercise's or a drill's id in the catalogue, and its minutes.</summary>
public sealed record StandardActivity(string Id, int Minutes);

/// <summary>
/// A routine that comes with Prima: a key that's never reused, its name, who it's for ("beginner", "intermediate", "advanced" or
/// "any"), a line on what it's for, and its activities in order. It's put in once as one of your routines, to change or take away
/// as you like.
/// </summary>
public sealed record StandardRoutine(string Key, string Title, string Level, string Purpose, IReadOnlyList<StandardActivity> Activities)
{
    public int Minutes => Activities.Sum(a => a.Minutes);
}

/// <summary>
/// The routines that come with Prima, in the order they're listed: a day for each level, warm-ups (a quick one, and two for a
/// concert), five with one focus each, and a light day. docs/routines.md has the research each is built on and why each activity
/// is where it is; the safety limits it applied (no technical activity over five minutes, a break every twelve to fifteen at
/// the keys, warm-ups away from the keys first and stretches only at the end, octaves only in the advanced day) hold for any
/// change made here.
/// </summary>
public static class StandardRoutines
{
    private static StandardActivity A(string id, int minutes) => new(id, minutes);

    public static IReadOnlyList<StandardRoutine> All { get; } =
    [
        new("beginner-daily", "Beginner daily", "beginner", "A first-year day: loosen up, a little technique, then notes and rhythm. Then five minutes of Sight-read, and your pieces.",
            [A("swings", 1), A("balance", 1), A("five-chord", 2), A("scales", 3), A("triad-walk", 2), A("shake", 1), A("notes", 3), A("rhythm", 3), A("geography", 2), A("stretches", 1)]),
        new("intermediate-daily", "Intermediate daily", "intermediate", "Scales, cadences and arpeggios, the hands working apart, then the reading drills. Then ten minutes of Sight-read, and your pieces.",
            [A("swings", 1), A("chromatic", 2), A("scales", 4), A("cadence", 3), A("arpeggios", 3), A("alberti", 2), A("shake", 1), A("smooth-detached", 2), A("independence", 2), A("keys", 2), A("intervals", 3), A("rhythm", 3), A("stretches", 1)]),
        new("advanced-daily", "Advanced daily", "advanced", "The longest mix: the Later exercises, every family and the reading drills, with two breaks. Octaves only if your hand spans one without stretching; rest a few minutes before your pieces.",
            [A("swings", 1), A("wrists", 1), A("chromatic", 2), A("scales", 5), A("arpeggios", 4), A("octave-scale", 2), A("shake", 1), A("shoulders", 1), A("hanon", 3), A("cadence-far", 3), A("trill-bursts", 2), A("two-three", 3), A("tension-check", 1), A("keys", 2), A("intervals", 3), A("rhythm", 3), A("geography", 2), A("stretches", 1)]),
        new("quick-warm-up", "Quick warm-up", "any", "Seven minutes to get the arms, wrists and fingers moving before your pieces.",
            [A("swings", 1), A("wrists", 1), A("drop", 1), A("passed", 2), A("five", 2)]),
        new("concert-warm-up", "Concert warm-up", "any", "A calm, familiar warm-up for the day you play: nothing new, never faster than you'll play. Then each piece once near its tempo, once medium, once slow, and stop.",
            [A("swings", 2), A("wrists", 1), A("bench", 1), A("drop", 1), A("five-chord", 2), A("scales", 2), A("arpeggios", 2), A("tension-check", 1)]),
        new("short-concert-warm-up", "Short concert warm-up", "any", "Five minutes for when there's little time before you play.",
            [A("swings", 1), A("drop", 1), A("five-chord", 2), A("tension-check", 1)]),
        new("reading-focus", "Reading focus", "any", "Every reading drill in a mixed order. Then five to fifteen minutes of Sight-read, in time, without stopping.",
            [A("notes", 4), A("ledger", 3), A("intervals", 3), A("shake", 1), A("rhythm", 3), A("keys", 2), A("chords", 2), A("geography", 2)]),
        new("even-scales", "Even scales and arpeggios", "beginner", "For beginners and on: slow, even scales and arpeggios, from the thumb's crossing to the full drills. Move each drill on a key a week.",
            [A("wrists", 1), A("long-short", 2), A("thumb-three", 2), A("scales", 4), A("contrary", 2), A("shake", 1), A("chromatic", 2), A("arpeggio-groups", 2), A("arpeggios", 3), A("stretches", 1)]),
        new("two-hands", "Two hands, two jobs", "intermediate", "Intermediate: the hands doing different things, one difference at a time, from mirror images to two against three.",
            [A("swings", 1), A("mirror", 2), A("contrary", 3), A("hold-move", 3), A("smooth-detached", 3), A("shake", 1), A("passed", 2), A("scale-parting", 3), A("two-to-one", 3), A("two-three", 3), A("stretches", 1)]),
        new("chords-jumps", "Chords and jumps", "intermediate", "Intermediate: chord shapes and the travel between them, near and then far. Arrive early, and keep what you land on.",
            [A("swings", 1), A("triad-walk", 2), A("inversions", 3), A("octave-jump", 2), A("chord-kinds", 2), A("jump-ladder", 3), A("shake", 1), A("cadence", 3), A("chord-jump", 3), A("cadence-far", 2), A("waltz", 3), A("stretches", 1)]),
        new("touch-pedal", "Touch and pedal", "beginner", "For beginners and on: arm weight, soft and loud, joined and detached, then the pedal. Quiet and slow throughout.",
            [A("arm-drop", 1), A("drop", 2), A("soft-loud", 2), A("slurs", 2), A("slur-walk", 2), A("detached", 2), A("shake", 1), A("swell", 3), A("pedal-alone", 1), A("pedal-changes", 3)]),
        new("light-day", "Light day", "any", "A short, easy day that keeps the habit while the hands recover: the day after a heavy one, or a week each month.",
            [A("bench", 1), A("balance", 1), A("side-video", 2), A("wrist-circles", 2), A("slurs", 2), A("notes", 3), A("stretches", 1)]),
        new("hanon-focus", "Hanon Focus", "beginner", "Practise a small forearm turn in No. 6, then use it for the changing reaches in No. 5. Start with one hand and a short group.",
            [A("hanon-06", 3), A("hanon-05", 3)]),
    ];
}
