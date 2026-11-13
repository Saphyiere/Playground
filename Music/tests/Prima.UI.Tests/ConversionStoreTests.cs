using Prima.Core.Transcription;

namespace Prima.UI.Tests;

public class ConversionStoreTests
{
    [Fact]
    public async Task ConfirmedSourceMapsRoundTripAndCannotBeRewrittenInPlace()
    {
        await using var host = new ComponentHost();
        var piece = await host.AddPiece(pdf: true);
        var source = new ConversionSource(new string('a', 64), "r1", [new("bar-a", 1, 1, 0, "1", new(0, 0.1, 0.5, 0.3))], true);

        await host.Store.SaveConversionSourceAsync(piece.Id, source);
        await host.Store.SaveConversionSourceAsync(piece.Id, source);
        var loaded = await host.Store.GetConversionSourceAsync(piece.Id, source.Sha256);

        Assert.NotNull(loaded);
        Assert.Equal(source.Revision, loaded.Revision);
        Assert.Equal(source.Bars, loaded.Bars);
        Assert.Null(await host.Store.GetConversionSourceAsync(piece.Id, new string('b', 64)));
        var changed = source with { Bars = [source.Bars[0] with { Bounds = new(0, 0.1, 0.6, 0.3) }] };
        await Assert.ThrowsAsync<InvalidOperationException>(() => host.Store.SaveConversionSourceAsync(piece.Id, changed));

        await host.Store.SaveConversionSourceAsync(piece.Id, changed with { Revision = "r2" });
        Assert.Equal("r2", (await host.Store.GetConversionSourceAsync(piece.Id, source.Sha256))!.Revision);
    }

    [Fact]
    public async Task UnconfirmedMapsAreRejectedAndDeletingThePieceDeletesItsMaps()
    {
        await using var host = new ComponentHost();
        var piece = await host.AddPiece(pdf: true);
        var source = new ConversionSource(new string('a', 64), "r1", [new("b1", 1, 1, 0, "1", new(0, 0, 1, 1))], false);
        await Assert.ThrowsAsync<ArgumentException>(() => host.Store.SaveConversionSourceAsync(piece.Id, source));

        await host.Store.SaveConversionSourceAsync(piece.Id, source with { Confirmed = true });
        await host.Store.DeletePieceAsync(piece.Id);
        Assert.Null(await host.Store.GetConversionSourceAsync(piece.Id, source.Sha256));
    }
}
