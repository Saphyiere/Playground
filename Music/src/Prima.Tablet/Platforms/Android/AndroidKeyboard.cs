using Android.Content;
using Android.Media.Midi;
using Android.OS;
using AndroidX.Core.Content;
using Java.Lang;
using Prima.Core.Keyboard;
using Prima.Core.Timing;
using Prima.UI.Host;

namespace Prima.Tablet;

/// <summary>
/// The piano over USB MIDI, through Android's MidiManager: it opens the first device that sends MIDI (the P-225B on its USB TO HOST
/// port), follows plugging in and out, and hands each message to the Core's parser with Android's timestamp (System.nanoTime).
/// Active Sensing goes to the jitter measure; notes on Prima's own channel (16) are dropped in case the piano echoes them back.
/// Notes for the piano wait on a thread of their own and go out 10 ms early with their exact timestamp, so they're on time whether
/// or not the device holds timestamped messages until then.
/// </summary>
public sealed class AndroidKeyboard : IKeyboard
{
    private const int OwnChannel = 16;
    private const long SendEarlyNs = 10_000_000;

    private readonly MidiManager manager;
    private readonly Handler handler = new(Looper.MainLooper!);
    private readonly MidiParser parser = new();
    private readonly List<MidiEvent> parsed = [];
    private readonly Lock gate = new();
    private readonly PriorityQueue<byte[], long> outgoing = new();
    private readonly Receiver receiver;
    private MidiDeviceInfo? info;
    private MidiDevice? device;
    private MidiOutputPort? fromPiano;
    private MidiInputPort? toPiano;
    private bool opening;

    public AndroidKeyboard()
    {
        manager = (MidiManager)Platform.AppContext.GetSystemService(Context.MidiService)!;
        receiver = new Receiver(this);
        var callback = new Watcher(this);
        if (OperatingSystem.IsAndroidVersionAtLeast(33)) manager.RegisterDeviceCallback((int)MidiTransport.MidiByteStream, ContextCompat.GetMainExecutor(Platform.AppContext)!, callback);
        else manager.RegisterDeviceCallback(callback, handler);
        new System.Threading.Thread(SendWhenDue) { IsBackground = true, Name = "Prima MIDI out" }.Start();
        handler.Post(OpenFirst);
    }

    public string? Name { get; private set; }

    public bool CanSend => toPiano is not null;

    public SensingJitter Sensing { get; } = new();

    public event Action<MidiEvent>? Received;

    public event Action? Changed;

    public void Send(byte[] message, long atNs)
    {
        lock (outgoing)
        {
            outgoing.Enqueue(message, atNs);
            Monitor.Pulse(outgoing);
        }
    }

    public void Silence()
    {
        lock (outgoing) outgoing.Clear();
        try
        {
            // flush() drops what the device still holds, which can include note-offs, so all notes off follows it.
            toPiano?.Flush();
            toPiano?.Send([0xB0 | (OwnChannel - 1), 123, 0], 0, 3);
        }
        catch (Java.IO.IOException)
        {
        }
    }

    // On the main thread: opens the first device that sends MIDI, a USB one if there's a choice.
    private void OpenFirst()
    {
        if (device is not null || opening) return;

        IEnumerable<MidiDeviceInfo> all;
        if (OperatingSystem.IsAndroidVersionAtLeast(33)) all = manager.GetDevicesForTransport((int)MidiTransport.MidiByteStream) ?? [];
        else all = manager.GetDevices() ?? [];
        var chosen = all.Where(d => d.OutputPortCount > 0).OrderBy(d => d.Type == MidiDeviceType.Usb ? 0 : 1).FirstOrDefault();
        if (chosen is null) return;

        opening = true;
        info = chosen;
        manager.OpenDevice(chosen, new Opened(this), handler);
    }

    private void OnOpened(MidiDevice? opened)
    {
        opening = false;
        if (opened is null || info is null) return;

        device = opened;
        fromPiano = opened.OpenOutputPort(0);
        fromPiano?.Connect(receiver);
        toPiano = info.InputPortCount > 0 ? opened.OpenInputPort(0) : null;
        var props = info.Properties;
        Name = props?.GetString(MidiDeviceInfo.PropertyProduct) ?? props?.GetString(MidiDeviceInfo.PropertyName) ?? "Keyboard";
        Sensing.Clear();
        // Prima's channel starts on the grand piano voice, since a channel other than 1 may start on another one.
        Send([0xC0 | (OwnChannel - 1), 0], 0);
        Changed?.Invoke();
    }

    private void OnRemoved(MidiDeviceInfo? removed)
    {
        if (removed is null || info is null || removed.Id != info.Id) return;

        lock (outgoing) outgoing.Clear();
        fromPiano?.Disconnect(receiver);
        fromPiano?.Close();
        toPiano?.Close();
        device?.Close();
        (fromPiano, toPiano, device, info, Name) = (null, null, null, null, null);
        Changed?.Invoke();
        OpenFirst();
    }

    private void OnBytes(byte[] bytes, int offset, int count, long timestamp)
    {
        MidiEvent[] events;
        lock (gate)
        {
            parser.Read(bytes.AsSpan(offset, count), timestamp, parsed);
            events = [.. parsed];
            parsed.Clear();
        }

        foreach (var e in events)
        {
            if (e.Kind == MidiKind.ActiveSensing) Sensing.Add(e.TimeNs);
            else if (e.Channel != OwnChannel) Received?.Invoke(e);
        }
    }

    // Holds each message until 10 ms before it's due, then sends it with its timestamp.
    private void SendWhenDue()
    {
        while (true)
        {
            byte[] message;
            long at;
            lock (outgoing)
            {
                while (true)
                {
                    if (outgoing.TryPeek(out message!, out at))
                    {
                        var waitMs = (at - SendEarlyNs - JavaSystem.NanoTime()) / 1_000_000;
                        if (waitMs <= 0)
                        {
                            outgoing.Dequeue();
                            break;
                        }
                        Monitor.Wait(outgoing, (int)System.Math.Min(waitMs, 1000));
                    }
                    else Monitor.Wait(outgoing);
                }
            }

            try
            {
                toPiano?.Send(message, 0, message.Length, at);
            }
            catch (Java.IO.IOException)
            {
                // Unplugged as it went out.
            }
        }
    }

    private sealed class Receiver(AndroidKeyboard keyboard) : MidiReceiver
    {
        public override void OnSend(byte[]? msg, int offset, int count, long timestamp)
        {
            if (msg is not null) keyboard.OnBytes(msg, offset, count, timestamp);
        }
    }

    private sealed class Opened(AndroidKeyboard keyboard) : Java.Lang.Object, MidiManager.IOnDeviceOpenedListener
    {
        public void OnDeviceOpened(MidiDevice? device) => keyboard.OnOpened(device);
    }

    private sealed class Watcher(AndroidKeyboard keyboard) : MidiManager.DeviceCallback
    {
        public override void OnDeviceAdded(MidiDeviceInfo? device) => keyboard.handler.Post(keyboard.OpenFirst);

        public override void OnDeviceRemoved(MidiDeviceInfo? device) => keyboard.handler.Post(() => keyboard.OnRemoved(device));
    }
}
