using Android.Media;
using Java.Lang;
using Prima.Core.Timing;
using Prima.UI.Host;

namespace Prima.Tablet;

/// <summary>
/// The click on the tablet's speaker (or wherever Android routes audio). A writer thread streams 48 kHz audio 10 ms at a time,
/// silence with each click placed on its exact frame, so the frame count never stops. The audio timestamp pairs a frame with the
/// moment it sounded on the system clock, so each beat's time is known exactly once it's settled. It's polled every 50 ms until it
/// is, then every 10 s. Until then, beat times are estimated from when the track started, and Approximate says so.
/// </summary>
public sealed class AndroidClick : IClick
{
    private const int Rate = 48_000;
    private const int Chunk = Rate / 100;

    private readonly Lock gate = new();
    private readonly AudioTimestamp stamp = new();
    private AudioTrack? track;
    private System.Threading.Thread? writer;
    private volatile bool running;
    private ClickPlan? plan;
    private float[] accent = [];
    private float[] plain = [];
    private long firstFrame;
    private long startedNs;
    private long stampFrame = -1;
    private long stampNs;

    public bool Running => running;

    public bool Approximate
    {
        get { lock (gate) return stampFrame < 0; }
    }

    public void Start(ClickPlan plan, ClickSoundKind sound, double volume)
    {
        Stop();
        (this.plan, accent, plain) = (plan, ClickSound.Render(sound, true, Rate, volume), ClickSound.Render(sound, false, Rate, volume));
        var attributes = new AudioAttributes.Builder().SetUsage(AudioUsageKind.Media)!.SetContentType(AudioContentType.Sonification)!.Build()!;
        var format = new AudioFormat.Builder().SetSampleRate(Rate)!.SetEncoding(Encoding.PcmFloat)!.SetChannelMask(ChannelOut.Mono)!.Build()!;
        var minimum = AudioTrack.GetMinBufferSize(Rate, ChannelOut.Mono, Encoding.PcmFloat);
        track = new AudioTrack.Builder().SetAudioAttributes(attributes).SetAudioFormat(format).SetTransferMode(AudioTrackMode.Stream).SetBufferSizeInBytes(System.Math.Max(minimum, Rate * 4 / 10)).Build();
        lock (gate) stampFrame = -1;
        // Beat 0 is about a quarter second after the track starts; the timestamp says exactly when.
        firstFrame = Rate * Click.LeadNs / 1_000_000_000L;
        startedNs = JavaSystem.NanoTime();
        track.Play();
        running = true;
        writer = new System.Threading.Thread(() => Write(track)) { IsBackground = true, Name = "Prima click", Priority = ThreadPriority.Highest };
        writer.Start();
    }

    public void Stop()
    {
        if (!running) return;

        running = false;
        writer?.Join(500);
        track?.Stop();
        track?.Release();
        (track, writer) = (null, null);
    }

    public long BeatTime(int beat)
    {
        var frame = FrameOf(beat);
        lock (gate) return stampFrame < 0 ? startedNs + frame * 1_000_000_000L / Rate : AudioClock.TimeOf(frame, stampFrame, stampNs, Rate);
    }

    private long FrameOf(int beat) => firstFrame + (long)System.Math.Round((plan?.Offset(beat) ?? 0) * Rate / 1e9);

    private void Write(AudioTrack output)
    {
        var buffer = new float[Chunk];
        var written = 0L;
        var nextPoll = 0L;
        while (running)
        {
            System.Array.Clear(buffer);
            // Every click that overlaps this chunk: from the beat that may still be ringing to the last that starts in it.
            var from = (int)System.Math.Max(0, System.Math.Floor(plan!.BeatAt((written - accent.Length - firstFrame) * 1e9 / Rate)));
            for (var beat = from; FrameOf(beat) < written + Chunk; beat++)
            {
                if (!plan.Sounds(beat)) continue;

                var click = plan.Accent(beat) ? accent : plain;
                var start = FrameOf(beat) - written;
                for (var i = (int)System.Math.Max(0, start); i < Chunk && i - start < click.Length; i++) buffer[i] += click[i - start];
            }

            if (output.Write(buffer, 0, Chunk, WriteMode.Blocking) < 0) break;
            written += Chunk;

            var now = JavaSystem.NanoTime();
            if (now < nextPoll) continue;

            var settled = output.GetTimestamp(stamp) && stamp.FramePosition > 0;
            if (settled) lock (gate) (stampFrame, stampNs) = (stamp.FramePosition, stamp.NanoTime);
            nextPoll = now + (settled ? 10_000_000_000L : 50_000_000L);
        }
    }
}
