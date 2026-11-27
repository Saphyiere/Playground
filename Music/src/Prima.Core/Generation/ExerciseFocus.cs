namespace Prima.Core.Generation;

/// <summary>Something an exercise trains: its key, its name, and a line on it.</summary>
public sealed record Focus(string Key, string Name, string About);

/// <summary>
/// What each exercise to play trains, beside the section it's in. A section says what the notes are (scales, chords, trills); a
/// focus says what playing them is for, and an exercise can have two or three: an arpeggio counted in fours is a broken chord
/// that's for finger strength and the thumb. Kept by exercise id, so the catalogue's entries don't each carry it.
/// </summary>
public static class ExerciseFocus
{
    public static IReadOnlyList<Focus> All { get; } =
    [
        new("reading", "Reading", "From the staff to the keys at once: notes, shapes, chords, keys and rhythms, by eye and by ear."),
        new("strength", "Finger strength", "Quick, sure fingers. What grows is control more than muscle, so stay light."),
        new("independence", "Finger independence", "One finger holds or moves while its neighbours do something else."),
        new("even", "Evenness", "Every note the same length and the same loudness."),
        new("thumb", "The thumb", "Passing under the hand, and staying light."),
        new("wrist", "Arm and wrist", "The arm's weight and a loose wrist: dropping in, bouncing, lifting out."),
        new("rotation", "Rotation", "The forearm's turn behind the fingers."),
        new("double", "Double notes", "Two keys at once in one hand: thirds, sixths, octaves."),
        new("chords", "Chords and landing", "Shapes that land together, and getting to them in time."),
        new("touch", "Touch", "Joined, detached, and how a phrase ends."),
        new("loud", "Loud and soft", "How fast the key goes down."),
        new("together", "Hands together", "Two hands at once: alike, in mirror, or each doing its own thing."),
        new("pedal", "Pedal", "The right pedal, changed cleanly."),
    ];

    private static readonly Dictionary<string, string[]> ByExercise = new()
    {
        // Drills
        ["notes"] = ["reading"],
        ["endless"] = ["reading"],
        ["ledger"] = ["reading"],
        ["intervals"] = ["reading"],
        ["chords"] = ["reading", "chords"],
        ["keys"] = ["reading"],
        ["rhythm"] = ["reading"],
        ["geography"] = ["reading", "chords"],
        ["playback"] = ["reading"],
        // Arm weight and rotation
        ["drop"] = ["wrist"],
        ["doorknob"] = ["rotation"],
        ["small-turns"] = ["rotation"],
        ["wrist-circles"] = ["wrist"],
        ["hold-turn"] = ["rotation"],
        // Five-finger patterns
        ["five"] = ["even", "strength"],
        ["major-minor"] = ["even"],
        ["mirror"] = ["together", "even"],
        ["long-short"] = ["strength"],
        ["five-chord"] = ["even", "chords"],
        ["hanon"] = ["strength", "even"],
        ["hold-one"] = ["independence"],
        // Scales
        ["scales"] = ["even", "thumb"],
        ["contrary"] = ["together", "even"],
        ["chromatic"] = ["even", "thumb"],
        ["scale-parting"] = ["together", "even"],
        // Broken chords and left-hand patterns
        ["arpeggios"] = ["even", "thumb"],
        ["passed"] = ["together", "even"],
        ["broken-inversions"] = ["chords", "even"],
        ["alberti"] = ["rotation", "even"],
        ["arpeggio-groups"] = ["strength", "thumb"],
        // Chords and cadences
        ["triad-walk"] = ["chords"],
        ["inversions"] = ["chords"],
        ["chord-kinds"] = ["chords"],
        ["cadence"] = ["chords"],
        ["four-chords"] = ["chords"],
        // Jumps
        ["octave-jump"] = ["chords"],
        ["jump-ladder"] = ["chords"],
        ["chord-jump"] = ["chords"],
        ["cadence-far"] = ["chords"],
        ["waltz"] = ["chords"],
        // Thumb under
        ["thumb-three"] = ["thumb"],
        // Trills
        ["independence"] = ["independence", "strength"],
        ["trill-2-3"] = ["strength"],
        ["trill-1-2"] = ["strength", "thumb"],
        ["trill-1-3"] = ["strength", "rotation"],
        ["trill-bursts"] = ["strength"],
        // Double notes
        ["thirds"] = ["double", "strength"],
        ["bounce"] = ["double", "wrist"],
        ["octave-scale"] = ["double", "wrist"],
        // Hands doing different things
        ["hold-move"] = ["together"],
        ["two-to-one"] = ["together"],
        ["smooth-detached"] = ["together", "touch"],
        ["two-three"] = ["together"],
        // Touch, loud and soft
        ["slurs"] = ["touch", "wrist"],
        ["detached"] = ["touch"],
        ["soft-loud"] = ["loud"],
        ["swell"] = ["loud"],
        ["slur-walk"] = ["touch", "wrist"],
        // Pedal
        ["pedal-changes"] = ["pedal"],
    };

    static ExerciseFocus()
    {
        foreach (var exercise in Hanon.All)
            ByExercise.Add(exercise.Id, [.. Hanon.Focus(exercise.HanonNumber!.Value)]);
    }

    /// <summary>What an exercise trains, in the list's order; nothing for one to read.</summary>
    public static IReadOnlyList<Focus> Of(string exerciseId) => ByExercise.TryGetValue(exerciseId, out var keys) ? [.. All.Where(f => keys.Contains(f.Key))] : [];

    public static bool Trains(string exerciseId, string focus) => ByExercise.TryGetValue(exerciseId, out var keys) && keys.Contains(focus);

    /// <summary>The ids it knows, for checking against the catalogue.</summary>
    public static IEnumerable<string> Known => ByExercise.Keys;
}
