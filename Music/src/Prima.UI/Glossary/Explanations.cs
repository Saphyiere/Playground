namespace Prima.UI.Glossary;

/// <summary>
/// What What's this says of a control tapped while it's on, by the key in the control's data-explain: its name and a line or
/// two on what it does. The rail, every screen's head, the play bar and its foot, and the Library's own keys; Settings' rows say
/// their own notes.
/// </summary>
public static class Explanations
{
    public static (string Name, IReadOnlyList<string> Lines)? Of(string key) => All.TryGetValue(key, out var said) ? said : null;

    private static readonly Dictionary<string, (string Name, IReadOnlyList<string> Lines)> All = new()
    {
        // The rail.
        ["you"] = ("You", ["How your practice is going: your pieces, and the trends over the last twelve weeks.", "Its lamp is lit while the piano is plugged in."]),
        ["today"] = ("Today", ["What to do now: the day's plan, with the next step ready to start, and this week's minutes."]),
        ["sight-read"] = ("Sight-read", ["A passage at your level to play at sight, in time with the click, and the next one at a level to suit."]),
        ["exercises"] = ("Exercises", ["The drills and the exercises, from scales to the pedal, each with what to do and how its notes move."]),
        ["routines"] = ("Routines", ["Practice plans: a list of exercises and pieces with a time for each, yours or Prima's."]),
        ["library"] = ("Library", ["Every piece: Prima's own, your collection and what you've imported, by composer, kind or level."]),
        ["repertoire"] = ("Repertoire", ["The pieces you've starred, in the collections you've put them in."]),
        ["settings"] = ("Settings", ["How Prima looks and plays, the keyboard and the click, your data, and the tools."]),

        // Every screen's head.
        ["back"] = ("The way back", ["Back to the screen it names, as you left it."]),
        ["about"] = ("About", ["This one's words in place of the music: what it is, what's been played of it, and where it's from. Tap again for the music."]),
        ["full-screen"] = ("Full screen", ["Only the music and the bar: the rail, the head and the foot go, and the tablet's own bars. Tap again to have them back."]),
        ["lamp"] = ("The piano's lamp", ["Lit while the piano is plugged in and Prima hears it; hollow when it isn't."]),
        ["whats-this"] = ("What's this", ["Tap anything marked to see what it is, on the music or the screen. Tap ? again to play."]),

        // The play bar.
        ["more"] = ("More", ["The rest of what a run can do, opened over the music: the click, the music's view, and what to do after a run. Its lamp is lit while the click goes on its own."]),
        ["pages"] = ("Pages", ["Turn the music's pages: these, a swipe, or the soft and middle pedals."]),
        ["hands"] = ("Hands", ["Which hand you play: the left, the right, or both. The other hand's notes aren't judged."]),
        ["mode"] = ("Timed or Wait for notes", ["Timed plays to the click and marks each note on time or not.", "Wait for notes has no click: the cursor waits at each note until you play it."]),
        ["tempo"] = ("Tempo", ["Beats a minute. Tap to change it with − and +, or type it; Original goes back to the music's own."]),
        ["loop"] = ("Loop", ["Lit, the run goes round and round until you stop it, each time round a pass. Choose the bars on the music or the strip."]),
        ["listen"] = ("Listen", ["The piano plays it to you at the tempo, the cursor following. Nothing is judged."]),
        ["play"] = ("Play", ["Starts a run: a bar of count-in, then the click. The C♯ above middle C presses it too."]),
        ["stop"] = ("Stop", ["Ends the run where it is. Only what you played up to then is counted."]),
        ["bar-back"] = ("Back a bar", ["Starts again from the bar before, counting in."]),
        ["bar-now"] = ("Bar", ["The bar being played."]),
        ["pass"] = ("Pass", ["Which time round the loop this is, or which run of the session."]),
        ["last-pass"] = ("Last pass", ["How the time round before went: in Timed the notes right and on time, in Wait for notes the notes right first time. The arrow says up or down on the one before."]),
        ["streak"] = ("In a row", ["Clean times round in a row, a dot each. A slip puts them out."]),
        ["finish"] = ("Finish practice", ["Ends the session and shows how it went, with what to practice next."]),

        // The foot.
        ["bar-strip"] = ("The bars", ["Every bar of the piece: tap one to start there, or drag along them to loop them."]),
        ["clocks"] = ("The session's clocks", ["Playing counts the time your hands are on the keys; Idle the rest, which pauses the session after a while."]),

        // The Library.
        ["library-search"] = ("Find", ["Type part of a title or a composer's name."]),
        ["import"] = ("Import files", ["Add music of your own: MusicXML, MIDI, ABC, MEI or a PDF, or a collection's zip."]),
    };
}
