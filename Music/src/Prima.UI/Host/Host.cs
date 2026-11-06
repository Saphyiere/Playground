using System.Diagnostics;
using Prima.Core.Keyboard;
using Prima.Core.Timing;

namespace Prima.UI.Host;

/// <summary>The system's monotonic clock, in nanoseconds: the one MIDI timestamps and the click's audio timestamps use.</summary>
public interface IClock
{
    long NowNs { get; }
}

/// <summary>
/// The piano over MIDI, as the host provides it: the tablet's USB MIDI, or the preview's simulated keyboard. Messages arrive on a
/// background thread with their timestamps on <see cref="IClock"/>.
/// </summary>
public interface IKeyboard
{
    /// <summary>The connected piano's name, or null when none is plugged in.</summary>
    string? Name { get; }

    /// <summary>Whether Prima can send notes to the piano (its MIDI in), for Duet, Hear first, Listen and play back and the click on the piano.</summary>
    bool CanSend { get; }

    /// <summary>Active Sensing's arrivals, for the MIDI path's jitter.</summary>
    SensingJitter Sensing { get; }

    /// <summary>Raised on a background thread for each note and controller message from the piano.</summary>
    event Action<MidiEvent>? Received;

    /// <summary>Raised when the piano is plugged in or out.</summary>
    event Action? Changed;

    /// <summary>Sends a message to the piano to sound at <paramref name="atNs"/> on the system clock (at once if that's past).</summary>
    void Send(byte[] message, long atNs);

    /// <summary>Drops what's waiting to be sent and silences Prima's notes on the piano.</summary>
    void Silence();
}

/// <summary>
/// The click on the tablet's speaker, as the host renders it. Beat 0 is the count-in's first beat. Beat times are on
/// <see cref="IClock"/>, as best known now: the audio timestamp makes them exact once the audio has settled.
/// </summary>
public interface IClick
{
    bool Running { get; }

    /// <summary>True while beat times are estimates, before the audio has a timestamp.</summary>
    bool Approximate { get; }

    /// <summary>Starts clicking to <paramref name="plan"/>, beat 0 a moment from now, at <paramref name="volume"/> (0 to 1).</summary>
    void Start(ClickPlan plan, ClickSoundKind sound, double volume);

    void Stop();

    /// <summary>When beat <paramref name="beat"/> sounds, or sounded, in ns on the system clock.</summary>
    long BeatTime(int beat);
}

/// <summary>A clock for hosts without a system one: a stopwatch from when the app started.</summary>
public sealed class StopwatchClock : IClock
{
    private readonly Stopwatch watch = Stopwatch.StartNew();

    public long NowNs => watch.Elapsed.Ticks * 100;
}

/// <summary>No piano: for a host that can't reach one.</summary>
public sealed class NoKeyboard : IKeyboard
{
    public string? Name => null;

    public bool CanSend => false;

    public SensingJitter Sensing { get; } = new();

    public event Action<MidiEvent>? Received { add { } remove { } }

    public event Action? Changed { add { } remove { } }

    public void Send(byte[] message, long atNs)
    {
    }

    public void Silence()
    {
    }
}

/// <summary>A click with no sound: the grid only, from the clock. The preview uses it, and it's the fallback without audio.</summary>
public sealed class GridClick(IClock clock) : IClick
{
    private long startNs;
    private long beatNs;

    public bool Running { get; private set; }

    public bool Approximate => false;

    public void Start(ClickPlan plan, ClickSoundKind sound, double volume) => (startNs, beatNs, Running) = (clock.NowNs + Click.LeadNs, plan.BeatNs, true);

    public void Stop() => Running = false;

    public long BeatTime(int beat) => startNs + beat * beatNs;
}

/// <summary>Files outside the app: backing the database up somewhere, and picking a file (a backup to restore, music to import).</summary>
public interface IFiles
{
    /// <summary>Offers a file to be saved or sent elsewhere (the tablet's share sheet); false where the host can't.</summary>
    Task<bool> OfferAsync(string path, string title);

    /// <summary>Lets the owner pick a file; its name and bytes, or null if none was picked or the host can't.</summary>
    Task<(string Name, byte[] Data)?> PickAsync();
}

/// <summary>No files outside the app: for a host without them, like the preview.</summary>
public sealed class NoFiles : IFiles
{
    public Task<bool> OfferAsync(string path, string title) => Task.FromResult(false);

    public Task<(string Name, byte[] Data)?> PickAsync() => Task.FromResult<(string, byte[])?>(null);
}

/// <summary>The Library's bundled music: the manifest and the files it names (built by Music/tools/Prima.Library).</summary>
public interface ILibraryBundle
{
    /// <summary>The bundle's manifest, or null when the app was built without one.</summary>
    Task<Prima.Core.Library.LibraryManifest?> ManifestAsync();

    /// <summary>A bundled file's bytes, by its path in the manifest.</summary>
    Task<byte[]?> ReadAsync(string path);
}

/// <summary>No bundle: the Library holds only what's imported.</summary>
public sealed class NoBundle : ILibraryBundle
{
    public Task<Prima.Core.Library.LibraryManifest?> ManifestAsync() => Task.FromResult<Prima.Core.Library.LibraryManifest?>(null);

    public Task<byte[]?> ReadAsync(string path) => Task.FromResult<byte[]?>(null);
}

/// <summary>A bundle in a folder on disk: the preview's, from artifacts/library.</summary>
public sealed class FolderBundle(string folder) : ILibraryBundle
{
    public async Task<Prima.Core.Library.LibraryManifest?> ManifestAsync()
    {
        var path = Path.Combine(folder, "manifest.json");
        return File.Exists(path) ? Prima.Core.Library.LibraryManifest.Read(await File.ReadAllTextAsync(path)) : null;
    }

    public async Task<byte[]?> ReadAsync(string path)
    {
        var full = Path.GetFullPath(Path.Combine(folder, path));
        return full.StartsWith(Path.GetFullPath(folder), StringComparison.Ordinal) && File.Exists(full) ? await File.ReadAllBytesAsync(full) : null;
    }
}
