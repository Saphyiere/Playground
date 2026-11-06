namespace Prima.UI.Practice;

/// <summary>A drill: its route key, name, group (reading or technique), what you do, and how many steps its own ladder has.</summary>
public sealed record DrillInfo(string Key, string Name, string Group, string Does, int Top, bool NeedsPiano = false);

/// <summary>A drill's place on its ladder and its last result, kept as a track ("drill:{key}").</summary>
public sealed record DrillTrack(int Step, double? LastShare, double? LastResult, DateOnly? LastDay)
{
    public static DrillTrack Start { get; } = new(1, null, null, null);
}

/// <summary>The twelve drills (concept.md, Drills), in the order the list shows them.</summary>
public static class DrillCatalog
{
    public static IReadOnlyList<DrillInfo> All { get; } =
    [
        new("notes", "Notes", "Reading", "A note on the staff: play its key. Spaced review, landmarks first.", 1),
        new("endless", "Endless notes", "Reading", "One note after another until you stop: read them faster and faster, sharps and flats too.", 1),
        new("ledger", "Ledger lines", "Reading", "Walk out from the staff past it, up or down, and back; then single notes out there. One ledger line, two, three.", 3),
        new("intervals", "Intervals and shapes", "Reading", "A figure from a named note: play it by its distances.", 4),
        new("chords", "Chords", "Reading", "A stack of notes: play it at once.", 4),
        new("keys", "Key signatures", "Reading", "A signature: play its tonic, then a phrase with its sharps or flats.", 6),
        new("rhythm", "Rhythm", "Reading", "A bar of beat, then the rhythm on any key; the click fades.", 12),
        new("geography", "Keyboard geography", "Reading", "A note or a leap: play it without looking down.", 4),
        new("playback", "Listen and play back", "Reading", "Hear a phrase on the piano, play it back, then see it written.", 3, NeedsPiano: true),
        new("scales", "Scales", "Technique", "A scale with its fingering, in time with the click; evenness after each run.", 1),
        new("arpeggios", "Arpeggios", "Technique", "An arpeggio, the same way, with a close look at the crossings.", 1),
        new("independence", "Finger independence", "Technique", "Five-finger patterns, then trills on the weaker fingers.", 3),
    ];

    public static DrillInfo? Find(string key) => All.FirstOrDefault(d => d.Key == key);
}
