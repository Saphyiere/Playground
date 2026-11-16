using Prima.Core.Library;
using Prima.Core.Music;
using Prima.UI.Host;
using Prima.UI.Storage;

namespace Prima.UI.Library;

/// <summary>
/// Plays a piece's opening from the Library, about half a minute of it, to hear it before choosing it: on the piano when one's
/// plugged in and takes notes, on Prima's own channel as Listen plays, otherwise on the tablet's speaker (<see cref="ISpeaker"/>).
/// One at a time: it stops by itself at the opening's end, when another starts, or when asked. Its notes come from the piece's
/// notation, or from the MIDI a PDF came with; a PDF with neither has nothing to play.
/// </summary>
public sealed class Openings(PrimaStore store, ILibraryBundle bundle, IKeyboard keyboard, ISpeaker speaker, IClock clock) : IDisposable
{
    // Notes go to the piano this far ahead of when they sound, a little at a time, as a run's do.
    private const long AheadNs = 250_000_000;

    private readonly Lock gate = new();
    private CancellationTokenSource? playing;
    private bool onThePiano;
    // Each opening asked for, and each stop, counts one: an opening plays only if nothing was asked since its own ask while its
    // notes were read, so two taps close together leave the later one playing, and ■ or leaving the Library before the first
    // note leaves none.
    private long asks;

    /// <summary>The piece whose opening is playing, by its id, or null.</summary>
    public string? Playing { get; private set; }

    /// <summary>Where the opening playing is heard: on the piano, or on the tablet.</summary>
    public string? Where => Playing is null ? null : onThePiano ? "on the piano" : "on the tablet";

    /// <summary>Whether an opening can be heard at all: the piano takes notes, or the tablet plays MIDI files.</summary>
    public bool CanPlay => keyboard.CanSend || speaker.CanPlay;

    /// <summary>Raised when an opening starts or stops; it may be on a background thread.</summary>
    public event Action? Changed;

    /// <summary>Plays the piece's opening, or stops it if it's the one playing. Null when it went; otherwise why it couldn't.</summary>
    public async Task<string?> ToggleAsync(PieceRecord piece)
    {
        if (Playing == piece.Id)
        {
            Stop();
            return null;
        }

        Stop();
        if (!CanPlay) return "Plug in the piano to hear it.";

        long ask;
        lock (gate) ask = ++asks;
        var notes = await NotesAsync(piece);
        CancellationTokenSource mine;
        CancellationToken stop;
        bool piano;
        lock (gate)
        {
            // Another opening asked for, or a stop, while these notes were read: that's what's wanted now, so this one says nothing.
            if (ask != asks) return null;
            if (notes.Count == 0) return piece.IsPdf && !piece.HasNotes ? $"{piece.Title} came without MIDI, so there's nothing to play until its notes are read." : $"{piece.Title} has no notes to play.";

            mine = playing = new CancellationTokenSource();
            stop = mine.Token;
            Playing = piece.Id;
            piano = onThePiano = keyboard.CanSend;
        }
        if (piano) _ = OnThePiano(notes, stop);
        else speaker.Play(Opening.ToMidi(notes));
        _ = EndAfter(notes.Max(n => n.End), mine);
        Changed?.Invoke();
        return null;
    }

    /// <summary>Stops the opening playing, if one is.</summary>
    public void Stop()
    {
        bool piano;
        lock (gate)
        {
            asks++;
            if (playing is null) return;

            playing.Cancel();
            playing.Dispose();
            playing = null;
            Playing = null;
            piano = onThePiano;
        }
        if (piano) keyboard.Silence();
        else speaker.Stop();
        Changed?.Invoke();
    }

    public void Dispose() => Stop();

    // The opening's notes: as written, for a piece in notation (or a PDF whose notes are read); a PDF's otherwise from the MIDI it
    // came with. None when there's nothing to read them from, or it can't be read.
    private async Task<IReadOnlyList<FileNote>> NotesAsync(PieceRecord piece)
    {
        try
        {
            if ((!piece.IsPdf || piece.HasNotes) && await store.GetMeiAsync(piece.Id) is { } mei) return await Task.Run(() => Opening.Of(MeiReader.Read(mei)));
            if (piece.Midi is not { } file) return [];

            var bytes = piece.Origin == "bundle" ? await bundle.ReadAsync(file)
                : File.Exists(Path.Combine(store.FilesPath, file)) ? await File.ReadAllBytesAsync(Path.Combine(store.FilesPath, file)) : null;
            return bytes is null ? [] : await Task.Run(() => Opening.Of(MidiFile.Read(bytes)));
        }
        catch (Exception e) when (e is FormatException or InvalidOperationException or ArgumentException or NullReferenceException or System.Xml.XmlException)
        {
            return [];
        }
    }

    // Each note on the piano at its moment, sent a quarter second ahead; a note let go just before it's struck again. Each is sent
    // under the gate, so none goes once Stop has let the opening go: a note struck after the piano was silenced would hang.
    private async Task OnThePiano(IReadOnlyList<FileNote> notes, CancellationToken stop)
    {
        var start = clock.NowNs + 200_000_000;
        var messages = notes
            .SelectMany(n => new[]
            {
                (At: start + (long)(n.Start * 1e9), Message: new byte[] { 0x9F, (byte)n.Midi, (byte)Math.Clamp(n.Velocity, 1, 127) }),
                (At: start + (long)(n.End * 1e9) - 20_000_000, Message: new byte[] { 0x8F, (byte)n.Midi, 0 }),
            })
            .OrderBy(m => m.At)
            .ToList();
        var next = 0;
        using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(50));
        try
        {
            do
            {
                for (var now = clock.NowNs; next < messages.Count && messages[next].At <= now + AheadNs; next++)
                {
                    lock (gate)
                    {
                        if (stop.IsCancellationRequested) return;
                        keyboard.Send(messages[next].Message, messages[next].At);
                    }
                }
            }
            while (next < messages.Count && await timer.WaitForNextTickAsync(stop));
        }
        catch (OperationCanceledException)
        {
        }
    }

    // The opening ends by itself once its last note has rung.
    private async Task EndAfter(double seconds, CancellationTokenSource mine)
    {
        try
        {
            await Task.Delay(TimeSpan.FromSeconds(seconds + 0.5), mine.Token);
        }
        catch (Exception e) when (e is OperationCanceledException or ObjectDisposedException)
        {
            return;
        }
        if (playing == mine) Stop();
    }
}
