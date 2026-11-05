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
}
