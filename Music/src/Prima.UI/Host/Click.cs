using Prima.Core.Timing;

namespace Prima.UI.Host;

/// <summary>
/// The click Prima plays, where Settings says: on the tablet's speaker (the host's <see cref="IClick"/>), or as short high notes
/// on the piano over MIDI, heard in the piano's headphones. Either way the beats are on the system clock, so notes compare with them.
/// </summary>
public sealed class Click(IClick speaker, IKeyboard keyboard, IClock clock, PrimaSettings settings)
{
    /// <summary>How far ahead beat 0 is set, so the first click isn't late while the audio starts.</summary>
    public const long LeadNs = 250_000_000;

    private IClick? current;

    public bool Running => current?.Running ?? false;

    /// <summary>True while beat times are estimates, before the audio has a timestamp.</summary>
    public bool Approximate => current?.Approximate ?? false;

    /// <summary>Whether this run is on the piano.</summary>
    public bool OnPiano => current is PianoClick;

    public ClickPlan? Plan { get; private set; }

    public void Start(ClickPlan plan)
    {
        Stop();
        Plan = plan;
        current = settings.ClickOnPiano && keyboard.CanSend ? new PianoClick(keyboard, clock) : speaker;
        current.Start(plan, settings.ClickSound, settings.ClickVolume / 100.0);
    }

    public void Stop()
    {
        current?.Stop();
        current = null;
    }

    /// <summary>When beat <paramref name="beat"/> sounds (beat 0 is the count-in's first), in ns on the system clock.</summary>
    public long BeatTime(int beat) => current?.BeatTime(beat) ?? 0;

    /// <summary>The beat sounding now (or last sounded), or -1 before beat 0.</summary>
    public int BeatNow()
    {
        if (current is null || Plan is null) return -1;

        var since = clock.NowNs - current.BeatTime(0);
        return since < 0 ? -1 : (int)(since / Plan.BeatNs);
    }

    /// <summary>
    /// The click as notes on the piano, on Prima's own channel: a high C on the downbeat and a G below it on the others, each 40 ms
    /// long. A timer sends the next half second of beats at a time, each with its exact timestamp.
    /// </summary>
    private sealed class PianoClick(IKeyboard keyboard, IClock clock) : IClick
    {
        private const int Channel = 15;
        private Timer? timer;
        private ClickPlan? plan;
        private long startNs;
        private int nextBeat;
        private double volume;

        public bool Running => timer is not null;

        public bool Approximate => false;

        public void Start(ClickPlan plan, ClickSoundKind sound, double volume)
        {
            (this.plan, this.volume, startNs, nextBeat) = (plan, volume, clock.NowNs + LeadNs, 0);
            timer = new Timer(_ => SendAhead(), null, 0, 100);
        }

        public void Stop()
        {
            timer?.Dispose();
            timer = null;
            keyboard.Silence();
        }

        public long BeatTime(int beat) => startNs + beat * (plan?.BeatNs ?? 0);

        private void SendAhead()
        {
            if (plan is null) return;

            while (BeatTime(nextBeat) < clock.NowNs + 500_000_000)
            {
                var at = BeatTime(nextBeat);
                if (plan.Sounds(nextBeat))
                {
                    var key = (byte)(plan.Accent(nextBeat) ? 96 : 91);
                    var velocity = (byte)Math.Clamp((int)Math.Round(volume * (plan.Accent(nextBeat) ? 110 : 85)), 1, 127);
                    keyboard.Send([0x90 | Channel, key, velocity], at);
                    keyboard.Send([0x80 | Channel, key, 0], at + 40_000_000);
                }
                nextBeat++;
            }
        }
    }
}
