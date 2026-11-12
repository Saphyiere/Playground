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
/// A beat of the music for the click: where it is, in the run's beats from its beat 0 (the score's first metre's beats), how many of
/// those it lasts, how long it lasts, in ns, and whether it starts a bar, so it's clicked higher.
/// </summary>
public readonly record struct ClickBeat(double At, double Beats, double Ns, bool Accent);

/// <summary>
/// What the click plays: a tempo in beats a minute (the beat being the dotted quarter in 6/8), beats to a bar, how many bars of
/// count-in, and how much is heard. Beat 0 is the count-in's first beat; the music's bar 1 starts after the count-in.
/// </summary>
public sealed record ClickPlan(double Bpm, int BeatsPerBar, int CountInBars = 1, ClickStage Stage = ClickStage.Throughout)
{
    private readonly IReadOnlyList<ClickBeat>? music;
    // Where each of the music's beats starts: in ns from its first, and in the run's beats.
    private double[] starts = [];
    private double[] places = [];

    /// <summary>The time between beats, in ns: the count-in's, and the music's unless it has beats of its own.</summary>
    public long BeatNs => (long)Math.Round(60e9 / Bpm);

    /// <summary>The first beat of the music, after the count-in.</summary>
    public int FirstMusicBeat => CountInBars * BeatsPerBar;

    /// <summary>
    /// The music's beats, for music whose tempo or metre changes: each its length and whether it starts a bar, in place of even
    /// beats at <see cref="Bpm"/> in bars of <see cref="BeatsPerBar"/>, which the count-in keeps. After the last, beats go on as long
    /// as it was. Null: the music's beats are the count-in's.
    /// </summary>
    public IReadOnlyList<ClickBeat>? Music
    {
        get => music;
        init
        {
            music = value;
            starts = new double[value?.Count ?? 0];
            for (var i = 1; i < starts.Length; i++) starts[i] = starts[i - 1] + value![i - 1].Ns;
            places = [.. (value ?? []).Select(b => b.At)];
        }
    }

    /// <summary>When beat <paramref name="beat"/> sounds, in ns after beat 0.</summary>
    public double Offset(int beat)
    {
        var count = beat - FirstMusicBeat;
        if (music is not { Count: > 0 } || count < 0) return beat * 60e9 / Bpm;

        var countIn = FirstMusicBeat * 60e9 / Bpm;
        return count < starts.Length ? countIn + starts[count] : countIn + starts[^1] + (count - starts.Length + 1) * music[^1].Ns;
    }

    /// <summary>The beat sounding <paramref name="ns"/> after beat 0, with how far through it: <see cref="Offset"/> turned round.</summary>
    public double BeatAt(double ns)
    {
        var countIn = FirstMusicBeat * 60e9 / Bpm;
        if (music is not { Count: > 0 } || ns < countIn) return ns * Bpm / 60e9;

        var into = ns - countIn;
        var index = Array.BinarySearch(starts, into);
        if (index < 0) index = ~index - 1;
        return FirstMusicBeat + index + (into - starts[index]) / music[index].Ns;
    }

    /// <summary>
    /// The beat a moment of the music falls in, with how far through it: <paramref name="musicBeat"/> in the run's beats from its
    /// beat 0, where the count-in's last beat ends.
    /// </summary>
    public double BeatOf(double musicBeat)
    {
        if (music is not { Count: > 0 }) return FirstMusicBeat + musicBeat;
        if (musicBeat < places[0]) return FirstMusicBeat + (musicBeat - places[0]) / music[0].Beats;

        var index = Array.BinarySearch(places, musicBeat);
        if (index < 0) index = ~index - 1;
        return FirstMusicBeat + index + (musicBeat - places[index]) / music[index].Beats;
    }

    /// <summary>Whether beat <paramref name="beat"/> is heard.</summary>
    public bool Sounds(int beat) => Stage switch
    {
        ClickStage.Throughout => true,
        ClickStage.CountInOnly => beat < FirstMusicBeat,
        _ => false,
    };

    /// <summary>Whether beat <paramref name="beat"/> starts a bar, so it's clicked higher.</summary>
    public bool Accent(int beat)
    {
        var count = beat - FirstMusicBeat;
        if (music is not { Count: > 0 } || count < 0) return beat % BeatsPerBar == 0;

        return count < music.Count && music[count].Accent;
    }
}
