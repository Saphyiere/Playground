namespace Prima.Core.Timing;

/// <summary>How much of the click is heard. The grid keeps running underneath in every stage; the click never waits.</summary>
public enum ClickStage
{
    /// <summary>Every beat.</summary>
    Throughout,
    /// <summary>The count-in only, then silence.</summary>
    CountInOnly,
    /// <summary>Nothing heard: the count-in shows on the screen only.</summary>
    Silent,
}

/// <summary>
/// What the click plays: a tempo in beats a minute (the beat being the dotted quarter in 6/8), beats to a bar, how many bars of
/// count-in, and how much is heard. Beat 0 is the count-in's first beat; the music's bar 1 starts after the count-in.
/// </summary>
public sealed record ClickPlan(double Bpm, int BeatsPerBar, int CountInBars = 1, ClickStage Stage = ClickStage.Throughout)
{
    /// <summary>The time between beats, in ns.</summary>
    public long BeatNs => (long)Math.Round(60e9 / Bpm);

    /// <summary>The first beat of the music, after the count-in.</summary>
    public int FirstMusicBeat => CountInBars * BeatsPerBar;

    /// <summary>Whether beat <paramref name="beat"/> is heard.</summary>
    public bool Sounds(int beat) => Stage switch
    {
        ClickStage.Throughout => true,
        ClickStage.CountInOnly => beat < FirstMusicBeat,
        _ => false,
    };

    /// <summary>Whether beat <paramref name="beat"/> starts a bar, so it's clicked higher.</summary>
    public bool Accent(int beat) => beat % BeatsPerBar == 0;
}
