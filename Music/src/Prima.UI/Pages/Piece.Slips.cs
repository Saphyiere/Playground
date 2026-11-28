using Prima.Core.Practice;
using Prima.UI.Practice;

namespace Prima.UI.Pages;

// When I slip, on a piece's Practice: the way a run takes a slip, chosen for the piece under its bar's More (Keep going, Back a
// bar), and what a slip does to a run then. A go is a run from Play to its end. A slip cuts it, and it counts in again where the
// way says; what it played up to each slip is a part of the go, kept with its end as one run once the go is over.
public partial class Piece
{
    private SlipWay slipWay;
    // The go's parts so far, each up to a slip.
    private readonly List<PlayOutcome> parts = [];

    private string SlipKey => $"Library.Slip.{Id}";

    private static SlipWay SlipWayOf(string? kept) => kept switch { "back" => SlipWay.BackABar, _ => SlipWay.KeepGoing };

    private async Task SetSlipWay(SlipWay way)
    {
        slipWay = way;
        await Store.SetSettingAsync(SlipKey, way switch { SlipWay.BackABar => "back", _ => null });
    }

    // A slip stops the run with Back a bar.
    private Func<PlayOutcome, Task<PlayMusic?>>? SlipStops => slipWay == SlipWay.KeepGoing ? null : Slipped;

    // A slip: the go's part up to it kept, and the music it counts in again with, from the bar before (inside the loop, if one is
    // set). Away from Practice, nothing: the run ends at the slip.
    private Task<PlayMusic?> Slipped(PlayOutcome part)
    {
        if (stage != Stage.Practice || score is null) return Task.FromResult<PlayMusic?>(null);

        parts.Add(part);
        return Task.FromResult(MusicAt(Slips.Back(part.Bar, loopTo is null ? FirstBar : from)));
    }

    // Round a loop, each time round as it's scored.
    private void Passed(PiecePass pass)
    {
    }

    // The clean times round in a row on the bar, while they're being counted.
    private (int Clean, int Of)? StreakShown => null;

    // The run's music from a bar without moving the piece's own start: round its loop from there, or from there to the end.
    private PlayMusic? MusicAt(int bar) => loopTo is not null && wholeLoop is not null ? wholeLoop.EnterLoopAt(bar) : MusicOf(bar, null);

    // A go just started anew, not counted in again after a slip or moved to another bar: nothing of the last one is left.
    private void NewGo(bool continuing)
    {
        if (!continuing) parts.Clear();
    }

    // The go as one run: its parts up to each slip, and this, its end; on the music, the worst each note was played. Stopped in the
    // moment after a slip, the go's end is the part up to that slip, which it has already.
    private async Task<PlayOutcome> WholeGo(PlayOutcome end)
    {
        if (parts.Count > 0 && ReferenceEquals(parts[^1], end)) parts.RemoveAt(parts.Count - 1);
        if (parts.Count == 0) return end;

        var go = Go.Join(parts, end);
        parts.Clear();
        if (sheet is not null) await sheet.ShowMarks(go.Marks);
        return go;
    }
}
