using Prima.Core.Timing;

namespace Prima.UI.Host;

/// <summary>What the click on the piano sounds like, heard in the piano's headphones.</summary>
public enum PianoClickSound
{
    /// <summary>
    /// Prima's own click (Wood or Beep), as audio to the piano over USB: the P-225 is a USB audio interface as well as MIDI. Where
    /// the tablet doesn't list the piano as an audio output, a piano key is played instead, as the click on the piano first was.
    /// </summary>
    Audio,

    /// <summary>The piano's own drum sounds over MIDI, on channel 10: a metronome's click, its bell on each bar's first beat.</summary>
    MetronomeClick,

    /// <summary>The piano's own drum sounds over MIDI, on channel 10: a high wood block on the first beat, a low one on the others.</summary>
    WoodBlock,

    /// <summary>High notes in the piano's own voice, as Prima first had it.</summary>
    PianoKey,
}

/// <summary>
/// The click Prima plays, where Settings says: on the tablet's speaker (the host's <see cref="IClick"/>), or on the piano, heard in
/// its headphones: Prima's own click as audio over USB, or the piano's own sounds over MIDI. Either way the beats are on the
/// system clock, so notes compare with them.
/// </summary>
public sealed class Click(IClick speaker, IKeyboard keyboard, IClock clock, PrimaSettings settings)
{
    /// <summary>How far ahead beat 0 is set, so the first click isn't late while the audio starts.</summary>
    public const long LeadNs = 250_000_000;

    /// <summary>The channel the piano's drum sounds are on, as General MIDI has them; notes on it from the piano aren't played keys.</summary>
    public const int DrumChannel = 10;

    private IClick? current;
    private bool throughPiano;

    public bool Running => current?.Running ?? false;

    /// <summary>True while beat times are estimates, before the audio has a timestamp.</summary>
    public bool Approximate => current?.Approximate ?? false;

    /// <summary>Whether this run is heard on the piano.</summary>
    public bool OnPiano => current is PianoClick || throughPiano;

    public ClickPlan? Plan { get; private set; }

    public void Start(ClickPlan plan) => Start(plan, settings.ClickOnPiano ? settings.PianoClick : null);

    /// <summary>Starts the click to <paramref name="plan"/> on the piano with <paramref name="onPiano"/>'s sound, or on the tablet with none.</summary>
    public void Start(ClickPlan plan, PianoClickSound? onPiano)
    {
        Stop();
        Plan = plan;
        throughPiano = onPiano == PianoClickSound.Audio && speaker.CanPlayOnPiano;
        var sound = onPiano is null || throughPiano ? null : onPiano == PianoClickSound.Audio ? PianoClickSound.PianoKey : onPiano;
        current = sound is { } own && keyboard.CanSend ? new PianoClick(keyboard, clock, own) : speaker;
        current.Start(plan, settings.ClickSound, settings.ClickVolume / 100.0, throughPiano);
    }

    public void Stop()
    {
        current?.Stop();
        (current, throughPiano) = (null, false);
    }

    /// <summary>When beat <paramref name="beat"/> sounds (beat 0 is the count-in's first), in ns on the system clock.</summary>
    public long BeatTime(int beat) => current?.BeatTime(beat) ?? 0;

    /// <summary>The beat sounding now (or last sounded), or -1 before beat 0.</summary>
    public int BeatNow()
    {
        if (current is null || Plan is null) return -1;

        var since = clock.NowNs - current.BeatTime(0);
        return since < 0 ? -1 : (int)Plan.BeatAt(since);
    }

    /// <summary>
    /// The click as notes on the piano: its drum sounds on channel 10 (General MIDI's metronome click and bell, or its wood
    /// blocks), or high notes in its piano voice on Prima's own channel, a high C on the downbeat and a G below it on the others.
    /// Each lasts 40 ms. A timer sends the next half second of beats at a time, each with its exact timestamp.
    /// </summary>
    private sealed class PianoClick(IKeyboard keyboard, IClock clock, PianoClickSound sound) : IClick
    {
        private const int OwnChannel = 15;
        private Timer? timer;
        private ClickPlan? plan;
        private long startNs;
        private int nextBeat;
        private double volume;

        public bool Running => timer is not null;

        public bool Approximate => false;

        public bool CanPlayOnPiano => false;

        // The channel (from 0) and the keys for the first beat of a bar and the others.
        private (int Channel, byte Accent, byte Plain) Keys => sound switch
        {
            PianoClickSound.WoodBlock => (DrumChannel - 1, 76, 77),
            PianoClickSound.PianoKey => (OwnChannel, 96, 91),
            _ => (DrumChannel - 1, 34, 33),
        };

        public void Start(ClickPlan plan, ClickSoundKind sound, double volume, bool onPiano)
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

        public long BeatTime(int beat) => startNs + (long)Math.Round(plan?.Offset(beat) ?? 0);

        private void SendAhead()
        {
            if (plan is null) return;

            var (channel, accent, plain) = Keys;
            while (BeatTime(nextBeat) < clock.NowNs + 500_000_000)
            {
                var at = BeatTime(nextBeat);
                if (plan.Sounds(nextBeat))
                {
                    var key = plan.Accent(nextBeat) ? accent : plain;
                    var velocity = (byte)Math.Clamp((int)Math.Round(volume * (plan.Accent(nextBeat) ? 110 : 85)), 1, 127);
                    keyboard.Send([(byte)(0x90 | channel), key, velocity], at);
                    keyboard.Send([(byte)(0x80 | channel), key, 0], at + 40_000_000);
                }
                nextBeat++;
            }
        }
    }
}
