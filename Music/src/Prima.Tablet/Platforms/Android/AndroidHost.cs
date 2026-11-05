using Android.Views;
using Java.Lang;
using Prima.UI.Host;

namespace Prima.Tablet;

/// <summary>The system's monotonic clock, the one MIDI timestamps and audio timestamps use.</summary>
public sealed class AndroidClock : IClock
{
    public long NowNs => JavaSystem.NanoTime();
}

/// <summary>Keeps the screen on while a practice screen is open.</summary>
public sealed class AndroidScreen : IScreen
{
    public void KeepOn(bool on) => MainThread.BeginInvokeOnMainThread(() =>
    {
        var window = Platform.CurrentActivity?.Window;
        if (on) window?.AddFlags(WindowManagerFlags.KeepScreenOn);
        else window?.ClearFlags(WindowManagerFlags.KeepScreenOn);
    });
}

/// <summary>The tablet's share sheet for backups, and its file picker for restoring and importing.</summary>
public sealed class AndroidFiles : IFiles
{
    public async Task<bool> OfferAsync(string path, string title)
    {
        // A dated copy in the cache, so the share sheet gets a whole file with a name that says what it is.
        var copy = Path.Combine(FileSystem.CacheDirectory, $"prima-{DateTime.Now:yyyy-MM-dd}.db");
        File.Copy(path, copy, overwrite: true);
        await Share.Default.RequestAsync(new ShareFileRequest { Title = title, File = new ShareFile(copy) });
        return true;
    }

    public async Task<(string Name, byte[] Data)?> PickAsync()
    {
        var picked = await FilePicker.Default.PickAsync();
        if (picked is null) return null;

        await using var stream = await picked.OpenReadAsync();
        using var memory = new MemoryStream();
        await stream.CopyToAsync(memory);
        return (picked.FileName, memory.ToArray());
    }
}
