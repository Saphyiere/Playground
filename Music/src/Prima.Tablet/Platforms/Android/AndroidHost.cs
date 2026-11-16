using Java.Lang;
using Prima.UI.Host;

namespace Prima.Tablet;

/// <summary>The system's monotonic clock, the one MIDI timestamps and audio timestamps use.</summary>
public sealed class AndroidClock : IClock
{
    public long NowNs => JavaSystem.NanoTime();
}

/// <summary>The tablet's share sheet for exports, and its file picker for importing (one file, or several at once).</summary>
public sealed class AndroidFiles : IFiles
{
    public async Task<bool> OfferAsync(string path, string title)
    {
        // An export: a whole file in the cache already, named for what it is ("Prima 2026-11-15 1530.zip").
        await Share.Default.RequestAsync(new ShareFileRequest { Title = title, File = new ShareFile(path) });
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

    public async Task<IReadOnlyList<(string Name, byte[] Data)>> PickManyAsync()
    {
        var files = new List<(string Name, byte[] Data)>();
        foreach (var picked in (await FilePicker.Default.PickMultipleAsync() ?? []).OfType<FileResult>())
        {
            await using var stream = await picked.OpenReadAsync();
            using var memory = new MemoryStream();
            await stream.CopyToAsync(memory);
            files.Add((picked.FileName, memory.ToArray()));
        }
        return files;
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
