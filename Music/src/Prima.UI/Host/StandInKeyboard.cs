using Prima.Core.Keyboard;
using Prima.Core.Timing;

namespace Prima.UI.Host;

/// <summary>
/// A stand-in for the piano, for trying Prima without one: plugged in or out, notes played now or at set times, and a minute of
/// Active Sensing at once, its timestamps on Prima's clock so runs score as they do from the piano, its messages through
/// <see cref="OneVoice"/> as the piano's are. The preview plays it from its /keys routes; a build from the PC puts it in front of
/// the tablet's real piano, turned on in Settings or from DevTools, where the piano's own keys still come through and what Prima
/// sends still reaches it. What Prima sends is kept, for checking.
/// </summary>
public sealed class StandInKeyboard : IKeyboard
{
    /// <summary>What the stand-in is called on the tablet, where the lamp shows it.</summary>
    public const string Called = "Stand-in piano";

    private readonly IClock clock;
    private readonly IKeyboard? piano;
    private readonly OneVoice voice = new();
    private readonly SensingJitter sensing = new();
    private string? standIn;

    public StandInKeyboard(IClock clock, IKeyboard? piano = null)
    {
        (this.clock, this.piano) = (clock, piano);
        if (piano is null) return;

        piano.Received += e => Received?.Invoke(e);
        piano.Changed += () => Changed?.Invoke();
    }

    /// <summary>Whether the stand-in is plugged in, as a piano would be.</summary>
    public bool On => standIn is not null;

    /// <summary>The real piano's name when it's plugged in, else the stand-in's while it's on.</summary>
    public string? Name => piano?.Name ?? standIn;

    public bool CanSend => piano?.CanSend == true || On;

    /// <summary>The real piano's Active Sensing while it's plugged in, else the stand-in's.</summary>
    public SensingJitter Sensing => piano?.Name is not null ? piano.Sensing : sensing;

    public List<(long AtNs, byte[] Message)> Sent { get; } = [];

    public event Action<MidiEvent>? Received;

    public event Action? Changed;

    /// <summary>The stand-in plugged in, under a name, or out.</summary>
    public void Plug(bool connected, string name = "P-225B")
    {
        standIn = connected ? name : null;
        sensing.Clear();
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
        var keys = notes.ToList();
        foreach (var note in keys) Say(MidiKind.NoteOn, note, velocity, now);
        foreach (var note in keys) Say(MidiKind.NoteOff, note, 0, now + holdMs * 1_000_000L);
    }

    /// <summary>A note played at a given time, which may be ahead: a session takes it as if it arrived then.</summary>
    public void PlayAt(int note, int velocity, long onNs, long offNs)
    {
        Say(MidiKind.NoteOn, note, velocity, onNs);
        Say(MidiKind.NoteOff, note, 0, offNs);
    }

    public void Pedal(int controller, int value) => Say(MidiKind.Control, controller, value, clock.NowNs);

    // One message from the stand-in, on each of its voices' channels in turn.
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
        for (var i = 0; i < seconds * 10 / 3; i++) sensing.Add(start + i * 300_000_000L + (long)(Gaussian(random) * jitterMs * 1e6));
    }

    public void Send(byte[] message, long atNs)
    {
        lock (Sent) Sent.Add((atNs, message));
        if (piano?.CanSend == true) piano.Send(message, atNs);
    }

    public void Silence() => piano?.Silence();

    private static double Gaussian(Random random) => Math.Sqrt(-2 * Math.Log(1 - random.NextDouble())) * Math.Cos(2 * Math.PI * random.NextDouble());
}
