using Android.Media;
using Java.Lang;
using Prima.Core.Timing;
using Prima.UI.Host;

namespace Prima.Tablet;

/// <summary>
/// The click on the tablet's speaker (or wherever Android routes audio), or on the piano's own audio over USB, heard in its
/// headphones, when that's asked for and Android lists the piano as an audio output. A writer thread streams 48 kHz audio 10 ms at a time,
/// silence with each click placed on its exact frame, so the frame count never stops. The audio timestamp pairs a frame with the
/// moment it sounded on the system clock, so each beat's time is known exactly once it's settled. It's polled every 50 ms until it
/// is, then every 10 s. Until then, beat times are estimated from when the track started, and Approximate says so.
/// </summary>
public sealed class AndroidClick : IClick
{
    private const int Rate = 48_000;
    private const int Chunk = Rate / 100;
    private const long FirstFrame = Rate * Click.LeadNs / 1_000_000_000L;

    private readonly Lock gate = new();
    private volatile AudioTrack? track;
    private ClickPlan? plan;
    private long startedNs;
    private long stampFrame = -1;
    private long stampNs;

    public bool Running => track is not null;

    public bool Approximate
    {
        get { lock (gate) return stampFrame < 0; }
    }

    public bool CanPlayOnPiano => PianoOutput() is not null;

    // The piano as an audio output: plugged in by USB, it's a USB audio interface as well as MIDI (the P-225 is), so what's played
    // to it is heard in its headphones.
    private static AudioDeviceInfo? PianoOutput()
    {
        var audio = (AudioManager?)Android.App.Application.Context.GetSystemService(Android.Content.Context.AudioService);
        return audio?.GetDevices(GetDevicesTargets.Outputs)?.FirstOrDefault(d => d.Type == AudioDeviceType.UsbDevice);
    }

    public void Start(ClickPlan plan, ClickSoundKind sound, double volume, bool onPiano)
    {
        Stop();
        var accent = ClickSound.Render(sound, true, Rate, volume);
        var plain = ClickSound.Render(sound, false, Rate, volume);
        var attributes = new AudioAttributes.Builder().SetUsage(AudioUsageKind.Media)!.SetContentType(AudioContentType.Sonification)!.Build()!;
        var format = new AudioFormat.Builder().SetSampleRate(Rate)!.SetEncoding(Encoding.PcmFloat)!.SetChannelMask(ChannelOut.Mono)!.Build()!;
        var minimum = AudioTrack.GetMinBufferSize(Rate, ChannelOut.Mono, Encoding.PcmFloat);
        var output = new AudioTrack.Builder().SetAudioAttributes(attributes).SetAudioFormat(format).SetTransferMode(AudioTrackMode.Stream).SetBufferSizeInBytes(System.Math.Max(minimum, Rate * 4 / 10)).Build();
        // On the piano: this track to its audio, whatever the rest of the tablet's sound goes to.
        if (onPiano && PianoOutput() is { } piano) output.SetPreferredDevice(piano);
        lock (gate)
        {
            (this.plan, stampFrame, startedNs) = (plan, -1, JavaSystem.NanoTime());
            output.Play();
            track = output;
        }
        new System.Threading.Thread(() => Write(output, plan, accent, plain)) { IsBackground = true, Name = "Prima click", Priority = ThreadPriority.Highest }.Start();
    }

    public void Stop()
    {
        lock (gate)
        {
            if (track is not { } output) return;
            track = null;
            // Pause interrupts a blocking Write. The owning writer releases its own track.
            output.Pause();
        }
    }

    public long BeatTime(int beat)
    {
        lock (gate)
        {
            var frame = FrameOf(plan, beat);
            return stampFrame < 0 ? startedNs + frame * 1_000_000_000L / Rate : AudioClock.TimeOf(frame, stampFrame, stampNs, Rate);
        }
    }

    private static long FrameOf(ClickPlan? plan, int beat) => FirstFrame + (long)System.Math.Round((plan?.Offset(beat) ?? 0) * Rate / 1e9);

    private void Write(AudioTrack output, ClickPlan plan, float[] accent, float[] plain)
    {
        using var stamp = new AudioTimestamp();
        var buffer = new float[Chunk];
        var written = 0L;
        var nextPoll = 0L;
        try
        {
            while (track == output)
            {
                System.Array.Clear(buffer);
                // Every click that overlaps this chunk: from the beat that may still be ringing to the last that starts in it.
                var from = (int)System.Math.Max(0, System.Math.Floor(plan.BeatAt((written - accent.Length - FirstFrame) * 1e9 / Rate)));
                for (var beat = from; FrameOf(plan, beat) < written + Chunk; beat++)
                {
                    if (!plan.Sounds(beat)) continue;

                    var click = plan.Accent(beat) ? accent : plain;
                    var start = FrameOf(plan, beat) - written;
                    for (var i = (int)System.Math.Max(0, start); i < Chunk && i - start < click.Length; i++) buffer[i] += click[i - start];
                }

                var sent = output.Write(buffer, 0, Chunk, WriteMode.Blocking);
                if (sent <= 0 || track != output) break;
                written += sent;

                var now = JavaSystem.NanoTime();
                if (now < nextPoll) continue;

                var settled = output.GetTimestamp(stamp) && stamp.FramePosition > 0;
                if (settled)
                {
                    lock (gate) { if (track == output) (stampFrame, stampNs) = (stamp.FramePosition, stamp.NanoTime); }
                }
                nextPoll = now + (settled ? 10_000_000_000L : 50_000_000L);
            }
        }
        finally
        {
            lock (gate) { if (track == output) track = null; }
            output.Release();
            output.Dispose();
        }
    }
}
