using Prima.Core.Timing;

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

    /// <summary>The click as notes on the piano rather than on the tablet's speaker.</summary>
    public bool ClickOnPiano { get; set; }

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
}
