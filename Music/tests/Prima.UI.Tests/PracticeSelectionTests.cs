using System.Text.Json;
using Bunit;
using Prima.Core.Music;
using Prima.UI.Pages;

namespace Prima.UI.Tests;

public class PracticeSelectionTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ClearRemovesTheSelectedBarOrLoopAndPreservesTheViewedLocation(bool loop)
    {
        await using var host = new ComponentHost();
        var piece = await host.AddPiece();
        var score = new Score(new Prima.Core.Music.Key(0), new Meter(4, 4), Enumerable.Range(1, 3)
            .Select(n => new Bar(n, [new Voice(1, [new Notes($"n{n}", 0, 1920, [new Tone($"t{n}", new Spelled(Step.C, 0, 4))])])])).ToArray());
        var mei = MeiWriter.Write(score);
        await host.Store.SavePieceAsync(piece, mei, []);
        await host.Store.StartSessionAsync(piece.Id, "custom", DateTimeOffset.Now);
        var view = host.Context.Render<Piece>(p => p.Add(c => c.Id, piece.Id));
        view.WaitForAssertion(() => Assert.NotEmpty(host.Browser.BarChoices));
        await view.InvokeAsync(() => view.Instance.Seen(2));
        await view.InvokeAsync(() => view.Instance.Chose(2));
        if (loop) await view.InvokeAsync(() => view.Instance.Looped(2, 3));
        view.WaitForAssertion(() => Assert.True(host.Browser.BarChoices[^1].GetProperty("lit").GetBoolean()));

        await view.InvokeAsync(view.Instance.ClearSelection);

        view.WaitForAssertion(() => Assert.False(host.Browser.BarChoices[^1].GetProperty("lit").GetBoolean()));
        var cleared = host.Browser.BarChoices[^1];
        Assert.Equal(1, cleared.GetProperty("from").GetInt32());
        Assert.Equal(JsonValueKind.Null, cleared.GetProperty("to").ValueKind);
        Assert.Equal(JsonValueKind.Null, cleared.GetProperty("at").ValueKind);
        using var position = JsonDocument.Parse((await host.Store.GetPieceAsync(piece.Id))!.Position!);
        Assert.Equal(2, position.RootElement.GetProperty("bar").GetInt32());
        Assert.Equal(1, position.RootElement.GetProperty("from").GetInt32());
        Assert.False(position.RootElement.TryGetProperty("to", out _));
        Assert.Equal(mei, await host.Store.GetMeiAsync(piece.Id));

        await view.InvokeAsync(() => view.Instance.Chose(3));
        view.WaitForAssertion(() => Assert.True(host.Browser.BarChoices[^1].GetProperty("lit").GetBoolean()));
        Assert.Equal(3, host.Browser.BarChoices[^1].GetProperty("from").GetInt32());
    }
}
