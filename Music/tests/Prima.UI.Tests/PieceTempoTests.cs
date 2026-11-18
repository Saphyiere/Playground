using Bunit;
using Prima.Core.Music;
using Prima.UI.Components;
using Prima.UI.Pages;
using Prima.UI.Storage;

namespace Prima.UI.Tests;

public class PieceTempoTests
{
    // A piece opens at its own tempo, the one its score marks, until a tempo has been set for that piece; then at the one set,
    // with its own beside it to go back to. Only a score that marks none starts at 72. Opening it sets nothing: the tempo kept
    // is still only what was chosen.
    [Theory]
    [InlineData(96.0, null, 96.0, false)]
    [InlineData(96.0, 60.0, 60.0, true)]
    [InlineData(null, null, 72.0, false)]
    [InlineData(null, 84.0, 84.0, false)]
    public async Task APieceOpensAtItsOwnTempoUntilOneIsSetForIt(double? marked, double? set, double opensAt, bool offersItsOwn)
    {
        await using var host = new ComponentHost();
        var piece = await Add(host, new Meter(4, 4), marked, set);

        var view = host.Context.Render<Piece>(p => p.Add(c => c.Id, piece.Id));

        view.WaitForAssertion(() => Assert.Equal(opensAt, view.FindComponent<PlayAlong>().Instance.Tempo));
        Assert.Equal(offersItsOwn ? ["96"] : [], view.FindAll(".play-tempo-original .mu-readout-value").Select(v => v.TextContent));
        Assert.Equal(set, (await host.Store.GetPieceAsync(piece.Id))!.Tempo);
    }

    // The tempo is in the metre's beats: a score in 6/8 marked at 120 quarter notes a minute goes at 80 dotted quarters.
    [Fact]
    public async Task ItsOwnTempoIsInTheMetresBeats()
    {
        await using var host = new ComponentHost();
        var piece = await Add(host, new Meter(6, 8), 120, null);

        var view = host.Context.Render<Piece>(p => p.Add(c => c.Id, piece.Id));

        view.WaitForAssertion(() => Assert.Equal(80, view.FindComponent<PlayAlong>().Instance.Tempo));
    }

    // A piece being practiced, so the page opens on its play bar: its score marks a tempo or none, and one is set for it or not.
    private static async Task<PieceRecord> Add(ComponentHost host, Meter meter, double? marked, double? set)
    {
        var piece = (await host.AddPiece()) with { Id = "own-tempo", Tempo = set };
        var score = new Score(new Prima.Core.Music.Key(0), meter, [.. Enumerable.Range(1, 2).Select(n =>
            new Bar(n, [new Voice(1, [new Notes($"n{n}", 0, meter.BarTicks, [new Tone($"t{n}", new Spelled(Step.C, 0, 4))])])])
            {
                Tempos = n == 1 && marked is { } perMinute ? [(0, perMinute)] : [],
            })]);
        await host.Store.SavePieceAsync(piece, MeiWriter.Write(score), []);
        await host.Store.StartSessionAsync(piece.Id, "custom", DateTimeOffset.Now);
        return piece;
    }
}
