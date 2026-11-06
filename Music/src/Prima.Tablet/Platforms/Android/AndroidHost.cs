using Java.Lang;
using Prima.UI.Host;

namespace Prima.Tablet;

/// <summary>The system's monotonic clock, the one MIDI timestamps and audio timestamps use.</summary>
public sealed class AndroidClock : IClock
{
    public long NowNs => JavaSystem.NanoTime();
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

/// <summary>The Library's bundle, packed into the app under library/ when it was built.</summary>
public sealed class AndroidBundle : ILibraryBundle
{
    public async Task<Prima.Core.Library.LibraryManifest?> ManifestAsync() => await ReadAsync("manifest.json") is { } bytes ? Prima.Core.Library.LibraryManifest.Read(System.Text.Encoding.UTF8.GetString(bytes)) : null;

    public async Task<byte[]?> ReadAsync(string path)
    {
        try
        {
            await using var stream = await FileSystem.OpenAppPackageFileAsync("library/" + path);
            using var memory = new MemoryStream();
            await stream.CopyToAsync(memory);
            return memory.ToArray();
        }
        catch (System.Exception e) when (e is FileNotFoundException or Java.IO.FileNotFoundException)
        {
            return null;
        }
    }
}

/// <summary>Android's status and navigation bars, hidden for full screen: a swipe from the edge shows them for a moment.</summary>
public sealed class AndroidSystemBars : ISystemBars
{
    public void Show(bool shown)
    {
        if (Platform.CurrentActivity?.Window is not { } window) return;
        if (AndroidX.Core.View.WindowCompat.GetInsetsController(window, window.DecorView) is not { } bars) return;

        bars.SystemBarsBehavior = AndroidX.Core.View.WindowInsetsControllerCompat.BehaviorShowTransientBarsBySwipe;
        if (shown) bars.Show(AndroidX.Core.View.WindowInsetsCompat.Type.SystemBars());
        else bars.Hide(AndroidX.Core.View.WindowInsetsCompat.Type.SystemBars());
    }
}
