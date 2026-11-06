using Prima.Core.Keyboard;
using Prima.Core.Scoring;
using Prima.Core.Timing;
using Prima.UI.Host;

namespace Prima.UI.Practice;

public enum Phase
{
    Idle,
    CountIn,
    Playing,
    Done,
}

/// <summary>
/// A timed run with the click: a count-in, then the music, the click never waiting. It collects what the piano sends (each note
/// from key down to key up, with its timestamps), plays the piano's own part if there is one (Duet, Hear first, Listen and play
/// back), and ends a beat after the music does. Beats are counted from bar 1's downbeat; a pickup's are negative.
/// </summary>
public sealed class PlaySession : IDisposable
{
    private readonly Click click;
    private readonly IKeyboard keyboard;
    private readonly IClock clock;
    private readonly SessionRegistry registry;
    private readonly Dictionary<int, (long On, int Velocity)> held = [];
    private readonly List<PlayedNote> played = [];
    private readonly List<(double Beat, int Midi, bool On)> pianoToCome = [];
    private Timer? watch;
    private double endBeat;
    private long lateNs;
    private long[]? beatTimes;

    // How far ahead the piano's notes are sent: enough for the tablet's MIDI queue, late enough that the click's beat times are
    // settled by then.
    private const long AheadNs = 200_000_000;

    public PlaySession(Click click, IKeyboard keyboard, IClock clock, SessionRegistry registry)
    {
        (this.click, this.keyboard, this.clock, this.registry) = (click, keyboard, clock, registry);
        keyboard.Received += OnReceived;
    }

    public Phase Phase { get; private set; }

    public ClickPlan? Plan { get; private set; }

    /// <summary>The music's notes as beats, for the preview's simulated player.</summary>
    public IReadOnlyList<(int Midi, double Beat, double Beats)> Expected { get; private set; } = [];

    /// <summary>Raised on a background thread when the phase changes.</summary>
    public event Action? Changed;

    /// <summary>Raised on a background thread for each key that goes down during the run: its key and when, on the system clock.</summary>
    public event Action<int, long>? KeyDown;

    public IReadOnlyList<PlayedNote> Played
    {
        get { lock (played) return [.. played]; }
    }

    /// <summary>
    /// Starts the click and the run: <paramref name="beats"/> of music after the count-in. <paramref name="pianoPart"/> is played
    /// on the piano at its beats, each a key and how many beats it's held, <paramref name="lateMs"/> after the beat: the timing
    /// offset, where your own notes land after the click, so the piano's land with them.
    /// </summary>
    public void Start(ClickPlan plan, double beats, IReadOnlyList<(int Midi, double Beat, double Beats)> expected,
        IReadOnlyList<(int Midi, double Beat, double Beats)>? pianoPart = null, double lateMs = 0)
    {
        Stop();
        lock (played) played.Clear();
        held.Clear();
        beatTimes = null;
        (Plan, Expected, endBeat, lateNs) = (plan, expected, beats, (long)(lateMs * 1e6));
        registry.Current = this;
        click.Start(plan);
        // Each key down at its beat and up just before its last; a key let go where the next starts goes up first.
        lock (pianoToCome)
        {
            pianoToCome.Clear();
            pianoToCome.AddRange((pianoPart ?? []).SelectMany(p => new[] { (p.Beat, p.Midi, On: true), (p.Beat + p.Beats, p.Midi, On: false) }).OrderBy(e => e.Item1).ThenBy(e => e.On));
        }
        SetPhase(Phase.CountIn);
        watch = new Timer(_ => Tick(), null, 20, 20);
    }

    /// <summary>When a beat of the music sounds, on the system clock: interpolated between the click's own beat times.</summary>
    public long TimeAt(double musicBeat)
    {
        if (Plan is null) return 0;

        var beat = Plan.FirstMusicBeat + musicBeat;
        var whole = (int)Math.Floor(beat);
        var (from, to) = (BeatTime(whole), BeatTime(whole + 1));
        return from + (long)((to - from) * (beat - whole));
    }

    // The click's beat times while it runs; once the run ends, the ones it had, kept so the result can be worked out.
    private long BeatTime(int beat)
    {
        if (beatTimes is null) return click.BeatTime(beat);
        if (beat >= 0 && beat < beatTimes.Length) return beatTimes[beat];

        var step = beatTimes.Length > 1 ? beatTimes[^1] - beatTimes[^2] : 0;
        return beat < 0 ? beatTimes[0] + beat * step : beatTimes[^1] + (beat - beatTimes.Length + 1) * step;
    }

    /// <summary>Ms from now until a beat of the music, for the sheet's cursor.</summary>
    public double MsUntil(double musicBeat) => (TimeAt(musicBeat) - clock.NowNs) / 1e6;

    /// <summary>Takes a whole performance at once and ends the run: the preview's simulated player.</summary>
    public void Perform(IEnumerable<PlayedNote> notes)
    {
        lock (played) played.AddRange(notes);
        Finish();
    }

    /// <summary>Ends the run now, keeping what was played.</summary>
    public void Finish()
    {
        if (Phase == Phase.Done) return;

        watch?.Dispose();
        watch = null;
        lock (pianoToCome) pianoToCome.Clear();
        if (Plan is not null) beatTimes = [.. Enumerable.Range(0, Plan.FirstMusicBeat + (int)Math.Ceiling(endBeat) + 4).Select(click.BeatTime)];
        click.Stop();
        keyboard.Silence();
        var now = clock.NowNs;
        lock (played)
        {
            foreach (var (midi, (on, velocity)) in held) played.Add(new PlayedNote(midi, on, now, velocity));
            held.Clear();
        }
        SetPhase(Phase.Done);
    }

    /// <summary>Stops without a result.</summary>
    public void Stop()
    {
        watch?.Dispose();
        watch = null;
        lock (pianoToCome) pianoToCome.Clear();
        if (Phase is Phase.CountIn or Phase.Playing)
        {
            click.Stop();
            keyboard.Silence();
        }
        Phase = Phase.Idle;
    }

    private void Tick()
    {
        var now = clock.NowNs;

        // The piano's part goes out a little ahead of each note, its time worked out then from the click's beats as they stand.
        // Worked out at the start, it was early by the speaker's delay: the tablet's click knows when its beats really sound only
        // once its audio timestamp settles, a moment after it starts. Until then a note waits unless it's nearly due.
        lock (pianoToCome)
        {
            var sent = 0;
            foreach (var (beat, midi, on) in pianoToCome)
            {
                var at = TimeAt(beat) + lateNs - (on ? 0 : 20_000_000);
                if (at > now + AheadNs || click.Approximate && at > now + 50_000_000) break;

                keyboard.Send(on ? [0x9F, (byte)midi, 72] : [0x8F, (byte)midi, 0], at);
                sent++;
            }
            pianoToCome.RemoveRange(0, sent);
        }

        if (Phase == Phase.CountIn && now >= TimeAt(Math.Min(0, Expected.Count == 0 ? 0 : Expected.Min(e => e.Beat))) - 50_000_000) SetPhase(Phase.Playing);
        if (now >= TimeAt(endBeat + 1)) Finish();
    }

    private void OnReceived(MidiEvent e)
    {
        if (Phase is not (Phase.CountIn or Phase.Playing)) return;

        lock (played)
        {
            if (e.Kind == MidiKind.NoteOn) held[e.Note] = (e.TimeNs, e.Velocity);
            else if (e.Kind == MidiKind.NoteOff && held.Remove(e.Note, out var down)) played.Add(new PlayedNote(e.Note, down.On, e.TimeNs, down.Velocity));
        }
        if (e.Kind == MidiKind.NoteOn) KeyDown?.Invoke(e.Note, e.TimeNs);
    }

    private void SetPhase(Phase phase)
    {
        Phase = phase;
        Changed?.Invoke();
    }

    public void Dispose()
    {
        Stop();
        keyboard.Received -= OnReceived;
        if (registry.Current == this) registry.Current = null;
    }
}

/// <summary>
/// The run or drill item in progress, so the preview's simulated player can play it: a timed run's notes, or a drill item's
/// answer (the right one, or a wrong one).
/// </summary>
public sealed class SessionRegistry
{
    public PlaySession? Current { get; set; }

    /// <summary>The current drill item's answer: the notes to play (in order; a chord as one step), given whether to get it right.</summary>
    public Func<bool, IReadOnlyList<IReadOnlyList<int>>>? DrillAnswer { get; set; }
}
