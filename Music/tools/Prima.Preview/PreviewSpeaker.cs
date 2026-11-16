using Prima.UI.Host;

namespace Prima.Preview;

/// <summary>
/// A stand-in for the tablet's speaker: silent, it keeps what it was given to play and how often it was stopped, for checking with
/// /speaker/played (a piece's opening heard from the Library with the piano unplugged, /keys/disconnect).
/// </summary>
public sealed class PreviewSpeaker : ISpeaker
{
    public bool CanPlay => true;

    /// <summary>Each MIDI file played: when, and its size.</summary>
    public List<(DateTimeOffset At, int Bytes)> Played { get; } = [];

    public int Stops { get; private set; }

    public void Play(byte[] midi)
    {
        lock (Played) Played.Add((DateTimeOffset.Now, midi.Length));
    }

    public void Stop() => Stops++;
}
