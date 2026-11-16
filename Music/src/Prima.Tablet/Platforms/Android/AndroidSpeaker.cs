using Android.Media;
using Prima.UI.Host;

namespace Prima.Tablet;

/// <summary>
/// The tablet's speaker for music: a MIDI file played by Android's own synthesizer, through its media player, as a piece's opening
/// is heard from the Library when no piano is plugged in. The file is written to the app's cache first, since the media player
/// plays files; it's on the main thread, where the media player's callbacks come.
/// </summary>
public sealed class AndroidSpeaker : ISpeaker
{
    private MediaPlayer? player;

    public bool CanPlay => true;

    public void Play(byte[] midi)
    {
        var path = Path.Combine(FileSystem.CacheDirectory, "opening.mid");
        MainThread.BeginInvokeOnMainThread(() =>
        {
            Release();
            File.WriteAllBytes(path, midi);
            var started = new MediaPlayer();
            started.SetAudioAttributes(new AudioAttributes.Builder()
                .SetUsage(AudioUsageKind.Media)!
                .SetContentType(AudioContentType.Music)!
                .Build()!);
            started.SetDataSource(path);
            started.Prepare();
            started.Start();
            player = started;
        });
    }

    public void Stop() => MainThread.BeginInvokeOnMainThread(Release);

    private void Release()
    {
        if (player is not { } stopping) return;

        player = null;
        try
        {
            if (stopping.IsPlaying) stopping.Stop();
        }
        catch (Java.Lang.IllegalStateException)
        {
        }
        stopping.Release();
    }
}
