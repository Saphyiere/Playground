using System.Globalization;
using Prima.Core.Library;
using Prima.Core.Music;

namespace Prima.UI.Pages;

/// <summary>
/// The key a notation piece is played in: as written, or another chosen in More (its Key), kept for the piece. Its music is moved
/// there (<see cref="MeiTranspose"/>) before anything is read from it, so the sheet, the notes a run expects, Listen, the other
/// hand played along, the names of wrong keys and the runs kept all go by the key on the stand. A piece in a key it isn't written
/// in has no fingering (the fingers were the written key's) and a level of its own. It's one piece all the same: one history,
/// each run saying its key.
/// </summary>
public partial class Piece
{
    // The music as written (its MEI, and the score read from it, with its signatures as printed), the key it's in, the twelve keys
    // it can be played in, the way from its own to the one on the stand, and the level it reads at there once that's graded: its
    // hardest, most of it, and why.
    private string? writtenMei;
    private Score? written;
    private Key ownKey;
    private IReadOnlyList<Key> keysOffered = [];
    private Transposition transposed;
    private (int Level, int Typical, string Reasons)? keyGrade;

    // Kept for each piece, as the view and the hand are: the key's sharps (over 0) or flats (under), none as written.
    private string KeyKey => $"Library.Key.{Id}";

    // The key it's written in, as KeyFinder tells it from where it starts: a minor piece's minor, though its file says major, and
    // a key its signature doesn't write (Bach's C minor in two flats). Its signatures are the score's.
    private Key OwnKey => ownKey;

    // The key on the stand.
    private Key KeyNow => transposed.Of(OwnKey);

    // As it opens: the music as written kept, then moved to the key chosen for the piece, if one was (one offered on the same
    // keynote, should the piece have changed since), off the screen's thread.
    private async Task<(string Mei, Score Score)> InItsKey(string mei, Score score)
    {
        var titles = new[] { piece?.Title, piece?.Work };
        var (found, offered) = await Task.Run(() =>
        {
            var own = KeyFinder.Find(score, titles).FirstOrDefault()?.Key ?? score.Key;
            return (own, Transposition.Keys(score, own));
        });
        (writtenMei, written, ownKey, keysOffered) = (mei, score, found, offered);
        var kept = (await Store.GetSettingsAsync()).GetValueOrDefault(KeyKey);
        if (!int.TryParse(kept, NumberStyles.Integer, CultureInfo.InvariantCulture, out var fifths)) return (mei, score);

        var keynote = (OwnKey with { Fifths = Math.Clamp(fifths, -7, 7) }).TonicPitchClass;
        transposed = Transposition.To(score, keysOffered.FirstOrDefault(k => k.TonicPitchClass == keynote, OwnKey), OwnKey);
        if (!transposed.Moves) return (mei, score);

        var by = transposed;
        var moved = await Task.Run(() => Moved(mei, by));
        _ = GradeKey(moved.Score, by);
        return moved;
    }

    // The music moved, its fingering left out, and read.
    private static (string Mei, Score Score) Moved(string mei, Transposition by)
    {
        var moved = MeiTranspose.WithoutFingering(MeiTranspose.Apply(mei, by));
        return (moved, MeiReader.Read(moved));
    }

    // A key chosen in More (or As written): kept for the piece, the music moved there, and all that was read from the music read
    // again from it. What a run left on the music, and a loop's passes so far, were in the key before: they go.
    private async Task ChooseKey(Key key)
    {
        if (playing || written is null || writtenMei is null) return;
        var to = Transposition.To(written, key, OwnKey);
        if (to == transposed) return;

        transposed = to;
        keyGrade = null;
        await Store.SetSettingAsync(KeyKey, to.Moves ? key.Fifths.ToString(CultureInfo.InvariantCulture) : null);
        var writtenText = writtenMei;
        (mei, score) = to.Moves ? await Task.Run(() => Moved(writtenText, to)) : (writtenText, written);
        expected = score.Expected();
        (meiDocument, measureIds, outcome, waitedLoop) = (null, null, null, null);
        waited = [];
        // What's this read the music in the key before: its keys, notes and names are read again.
        ForgetMusic();
        StartAt(from);
        if (to.Moves) _ = GradeKey(score, to);
    }

    // The level in a key, graded as an import is (the hardest of its excerpts) away from the screen's thread, for as long as the
    // piece is in that key: one with more sharps or flats, or more accidentals, reads harder.
    private async Task GradeKey(Score moved, Transposition by)
    {
        var graded = await Task.Run(() => Excerpts.PieceGrade(moved, Excerpts.Cut(moved)));
        if (by != transposed) return;

        keyGrade = graded;
        await InvokeAsync(StateHasChanged);
    }

    // The piece's level in the key on the stand: its own as written, or the key's once graded.
    private int? LevelNow => transposed.Moves && keyGrade is { } grade ? grade.Level : piece?.Level;

    // A run's key as it's kept: the way there from the key it's written in, and its name; none as written.
    private (Transposition, string)? RunKey => transposed.Moves ? (transposed, KeyNow.Name) : null;

    // The words under More's key: what it does, and in another key, where it's from and what's left out.
    private string KeyNote()
    {
        if (!transposed.Moves) return "Tap a key to play the piece in it: the music, Listen and the marks all follow. It stays in that key until you change it.";

        var level = keyGrade is { } grade && piece?.Level is { } own && grade.Level != own ? $" It reads at level {grade.Level} in {KeyNow.Name}, {own} as written." : "";
        var fingers = writtenMei?.Contains("<fing", StringComparison.Ordinal) == true ? " Its finger numbers are left out: they were for the key it's written in." : "";
        return $"Written in {OwnKey.Name}.{level}{fingers}";
    }
}
