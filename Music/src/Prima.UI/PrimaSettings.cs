using Prima.Core.Timing;
using Prima.UI.Host;

namespace Prima.UI;

/// <summary>
/// Settings kept with the app's data (PrimaStore reads and writes them by key). Loaded once at start by MainLayout; screens change
/// them and save.
/// </summary>
public sealed class PrimaSettings
{
    /// <summary>The timing calibration's offset, in ms, taken off every note played. Positive means notes arrive late.</summary>
    public double TimingOffsetMs { get; set; }

    /// <summary>The calibration's tapping spread (interquartile range), in ms: mostly the player's, not the keyboard's.</summary>
    public double? TappingSpreadMs { get; set; }

    /// <summary>When the timing was last calibrated, or null if never.</summary>
    public DateOnly? CalibratedOn { get; set; }

    /// <summary>The click heard on the piano, in its headphones, rather than on the tablet's speaker.</summary>
    public bool ClickOnPiano { get; set; }

    /// <summary>What the click on the piano sounds like: Prima's own click as audio over USB, or one of the piano's own sounds.</summary>
    public PianoClickSound PianoClick { get; set; } = PianoClickSound.Audio;

    public ClickSoundKind ClickSound { get; set; } = ClickSoundKind.Wood;

    /// <summary>0 to 100.</summary>
    public int ClickVolume { get; set; } = 80;

    /// <summary>Whether Prima may send notes to the piano (Duet, Hear first, Listen and play back).</summary>
    public bool SendToPiano { get; set; } = true;

    /// <summary>Whether the C♯ and D♯ above middle C press the screen's main button and Again, between runs.</summary>
    public bool PianoButtons { get; set; } = true;

    /// <summary>The music font Verovio engraves with: Leipzig, Bravura or Leland.</summary>
    public string MusicFont { get; set; } = "Leipzig";

    /// <summary>The sheet's size: -1 smaller, 0 the printed 7 mm staff, 1 larger.</summary>
    public int SheetSize { get; set; }

    /// <summary>Whether a song's words (its lyrics, or its lines typed as text) are printed under its notes.</summary>
    public bool Lyrics { get; set; } = true;

    public string? Name { get; set; }

    /// <summary>The daily goal. The app never raises it by itself.</summary>
    public int DailyMinutes { get; set; } = 20;

    /// <summary>The practice cue, "When I ___, I'll open the app": an if-then plan (Gollwitzer &amp; Sheeran).</summary>
    public string? Cue { get; set; }

    /// <summary>The clefs the note review drills: "both", "treble" or "bass".</summary>
    public string Clefs { get; set; } = "both";

    public int CountInBars { get; set; } = 1;

    /// <summary>How long Sight-read's look lasts before the count-in starts by itself, in seconds; 0 waits for Start.</summary>
    public int LookSeconds { get; set; } = 30;

    /// <summary>Letter names after a wrong answer in drills.</summary>
    public bool LetterNames { get; set; } = true;

    /// <summary>Whether the first run's welcome is done.</summary>
    public bool Welcomed { get; set; }

    /// <summary>How hard you strike a key soft and loud (velocity), measured in Keyboard check; dynamics are judged across it.</summary>
    public int? SoftVelocity { get; set; }

    public int? LoudVelocity { get; set; }

    /// <summary>Whether a run's summary opens its bars, as it was left last time.</summary>
    public bool BarMapOpen { get; set; }

    /// <summary>Whether a run's summary shows its details (the panels and the bars) under its few lines, as it was left last time.</summary>
    public bool SummaryDetails { get; set; }

    /// <summary>Whether the cursor shows while a run plays In time, or Listen plays. Off, the music still scrolls with the run.</summary>
    public bool Cursor { get; set; } = true;

    /// <summary>Whether notes are marked as they're played. Off, they're judged all the same, and marked when the run ends.</summary>
    public bool LiveMarks { get; set; } = true;

    /// <summary>
    /// The ways of practicing a piece, each a set of the settings after it, which can then be changed one by one: what shows after a
    /// run, each pass's figure round a loop, and whether to pause between repeats. Automatic repeat is a separate playback preference.
    /// </summary>
    public static readonly IReadOnlyDictionary<string, (string AfterRun, bool PassFigure, bool PassWaits)> Ways = new Dictionary<string, (string, bool, bool)>
    {
        ["simple"] = ("nothing", false, false),
        ["standard"] = ("summary", true, false),
        ["hands-on"] = ("review", true, true),
    };

    /// <summary>The ways' names, in order, for choosing one.</summary>
    public static readonly (string Way, string Name)[] WayNames = [("simple", "Minimal"), ("standard", "Standard"), ("hands-on", "Detailed")];

    /// <summary>What shows after a run in Practice: "nothing" (it's ready to go again), "summary" (under the music) or "review" (taking the screen).</summary>
    public string AfterRun { get; set; } = "summary";

    /// <summary>Round a loop in Practice, each pass's figure in the bar as the next begins.</summary>
    public bool PassFigure { get; set; } = true;

    /// <summary>Whether a loop in Practice stops after each pass, to look at it and go again, or goes straight on.</summary>
    public bool PassWaits { get; set; }

    /// <summary>Whether a run in Practice that comes to its end goes again by itself, after its count-in.</summary>
    public bool RunAgain { get; set; }

    /// <summary>Lock in's clean times round in a row, for every piece: 3, 5 or 7 (the owner's habit).</summary>
    public int LockClean { get; set; } = Prima.Core.Practice.Slips.Habit;

    /// <summary>Whether Lock in counts a note off the beat as spoiling a time round (never as a slip: timing never stops a run).</summary>
    public bool SlipTiming { get; set; }

    /// <summary>The way of practicing the settings make up, or null when they've been changed from all three.</summary>
    public string? Way => Ways.FirstOrDefault(w => w.Value == (AfterRun, PassFigure, PassWaits)).Key;

    public string FeedbackDescription => Way switch
    {
        "simple" => "Stay on the score after each run.",
        "standard" => "See a short summary after each run and a result after each loop.",
        "hands-on" => "Open the full review after each run and pause between loop repeats.",
        _ => "Custom feedback settings.",
    };

    /// <summary>Apply a feedback preset.</summary>
    public void UseWay(string way) => (AfterRun, PassFigure, PassWaits) = Ways[way];
}
