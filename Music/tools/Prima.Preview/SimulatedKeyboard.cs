using Prima.Core.Keyboard;
using Prima.Core.Timing;
using Prima.UI.Host;

namespace Prima.Preview;

/// <summary>
/// A stand-in for the piano, driven by the preview's /keys endpoints: plugged in or out, notes played now or at set times, and a
/// minute of Active Sensing at once. Its timestamps are on the same clock as the silent click, so sessions score as on the tablet.
/// Its messages pass through <see cref="OneVoice"/>, as the tablet's do. What Prima sends to it is kept, for checking.
/// </summary>
public sealed class SimulatedKeyboard(IClock clock) : IKeyboard
{
    private readonly OneVoice voice = new();

    public string? Name { get; private set; }

    public bool CanSend => Name is not null;

    public SensingJitter Sensing { get; } = new();

    public List<(long AtNs, byte[] Message)> Sent { get; } = [];

    public event Action<MidiEvent>? Received;

    public event Action? Changed;

    public void Plug(bool connected)
    {
        Name = connected ? "P-225B" : null;
        Sensing.Clear();
        voice.Clear();
        Changed?.Invoke();
    }

    /// <summary>
    /// How many voices are layered on the keys: with 2, as in the P-225's Dual, each key and pedal sends its message on channel 1
    /// and again on channel 2.
    /// </summary>
    public int Voices { get; set; } = 1;

    /// <summary>Plays notes now: each a note-on, and a note-off <paramref name="holdMs"/> later.</summary>
    public void Play(IEnumerable<int> notes, int velocity, int holdMs)
    {
        var now = clock.NowNs;
        foreach (var note in notes) Say(MidiKind.NoteOn, note, velocity, now);
        foreach (var note in notes) Say(MidiKind.NoteOff, note, 0, now + holdMs * 1_000_000L);
    }

    /// <summary>A note played at a given time, which may be ahead: a session takes it as if it arrived then.</summary>
    public void PlayAt(int note, int velocity, long onNs, long offNs)
    {
        Say(MidiKind.NoteOn, note, velocity, onNs);
        Say(MidiKind.NoteOff, note, 0, offNs);
    }

    public void Pedal(int controller, int value) => Say(MidiKind.Control, controller, value, clock.NowNs);

    // One message from the piano, on each of its voices' channels in turn.
    private void Say(MidiKind kind, int data1, int data2, long timeNs)
    {
        for (var channel = 1; channel <= Voices; channel++)
        {
            var said = new MidiEvent(kind, channel, data1, data2, timeNs);
            if (voice.Hears(said)) Received?.Invoke(said);
        }
    }

    /// <summary>Active Sensing for <paramref name="seconds"/>, every 300 ms with this much jitter (ms, spread), all at once.</summary>
    public void Sense(int seconds, double jitterMs)
    {
        var random = new Random(3);
        var start = clock.NowNs - seconds * 1_000_000_000L;
        for (var i = 0; i < seconds * 10 / 3; i++) Sensing.Add(start + i * 300_000_000L + (long)(Gaussian(random) * jitterMs * 1e6));
    }

    public void Send(byte[] message, long atNs)
    {
        lock (Sent) Sent.Add((atNs, message));
    }

    public void Silence()
    {
    }

    private static double Gaussian(Random random) => Math.Sqrt(-2 * Math.Log(1 - random.NextDouble())) * Math.Cos(2 * Math.PI * random.NextDouble());
}
