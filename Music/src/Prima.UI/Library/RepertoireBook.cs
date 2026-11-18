using Prima.Core.Library;
using Prima.UI.Storage;

namespace Prima.UI.Library;

/// <summary>
/// The repertoire as the screens share it: the pieces starred in the Library and the collections they're put in. Read from
/// Prima's data each time a screen opens (so data put back from a copy is what shows), and saved as it changes.
/// </summary>
public sealed class RepertoireBook(PrimaStore store)
{
    /// <summary>The track Prima's data keeps it under.</summary>
    public const string Track = "repertoire";

    /// <summary>The repertoire as it stands.</summary>
    public Repertoire Current { get; private set; } = Repertoire.Empty;

    /// <summary>The repertoire changed: a screen showing stars draws them again.</summary>
    public event Action? Changed;

    /// <summary>Reads the repertoire as Prima's data has it.</summary>
    public async Task LoadAsync() => Current = await store.GetTrackAsync<Repertoire>(Track) is { Pieces: not null, Collections: not null } kept ? kept : Repertoire.Empty;

    /// <summary>The repertoire as it is now, kept; nothing is written when it's the same.</summary>
    public async Task SetAsync(Repertoire next)
    {
        if (ReferenceEquals(next, Current)) return;

        Current = next;
        await store.SaveTrackAsync(Track, next);
        Changed?.Invoke();
    }

    /// <summary>A piece starred.</summary>
    public Task StarAsync(string piece) => SetAsync(Current.With(piece, DateTimeOffset.Now));

    /// <summary>A piece taken out of the repertoire and its collections.</summary>
    public Task DropAsync(string piece) => SetAsync(Current.Without(piece));

    /// <summary>A piece put in a collection, or taken out of it.</summary>
    public Task PutAsync(string collection, string piece, bool there) => SetAsync(Current.In(collection, piece, there, DateTimeOffset.Now));

    /// <summary>A new collection of that name; its id, or null when the name is blank or taken.</summary>
    public async Task<string?> AddAsync(string name)
    {
        var id = Guid.NewGuid().ToString("N")[..12];
        var next = Current.Named(name, id);
        if (ReferenceEquals(next, Current)) return null;

        await SetAsync(next);
        return id;
    }
}
