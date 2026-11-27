using Bunit;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Prima.Core.Music;
using Prima.UI.Components;
using Prima.UI.Pages;
using Prima.UI.Practice;

namespace Prima.UI.Tests;

public class BarSeekTests
{
    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public async Task Resuming_midway_scores_only_complete_passes(int repeats)
    {
        var piano = new TestPiano(plugged: true);
        var clock = new SetClock();
        await using var host = new ComponentHost(keyboard: piano, clock: clock);
        var key = new Prima.Core.Music.Key(0);
        var score = new Score(key, new Meter(3,4), [.. Enumerable.Range(1,3).Select(b =>
            new Bar(b, [new Voice(1, [new Notes($"n{b}", 0, 1440, [new Tone($"n{b}", key.Spell(60+b))])])]))]);
        var entered = new PlayMusic(score, score.Expected()).Looped(repeats).EnterLoopAt(3);
        var sheet = host.Context.Render<Sheet>(p => p.Add(c => c.Mei, MeiWriter.Write(score)));
        var finished = new TaskCompletionSource<PlayOutcome>(TaskCreationOptions.RunContinuationsAsynchronously);
        var view = host.Context.Render<PlayAlong>(p => p.Add(c => c.Name, "seek-pass").Add(c => c.Music, entered)
            .Add(c => c.Ask, PlayMode.WaitForMe).Add(c => c.SheetOf, () => sheet.Instance).Add(c => c.OnFinished, result => finished.TrySetResult(result)));
        view.WaitForAssertion(() => Assert.Equal(PlayMode.WaitForMe, view.Instance.Mode));
        await view.InvokeAsync(() => view.Instance.Start());
        foreach (var note in entered.Notes)
        {
            clock.NowNs += 300_000_000;
            await view.InvokeAsync(() => piano.Play(note.Midi, clock.NowNs));
        }
        var result = await finished.Task.WaitAsync(TimeSpan.FromSeconds(5), Xunit.TestContext.Current.CancellationToken);
        Assert.Equal((1,3), result.Loop);
        Assert.Equal(repeats - 1, result.Passes.Count);
        Assert.All(result.Passes, pass => Assert.Equal(3, pass.Notes));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Rapid_seeks_keep_the_selected_loop_wrap_to_its_pickup_and_let_Stop_win(bool wait)
    {
        var piano = new TestPiano(plugged: true);
        await using var host = new ComponentHost(keyboard: piano, clock: new SetClock());
        var piece = await host.AddPiece();
        var key = new Prima.Core.Music.Key(0);
        var bars = Enumerable.Range(0, 19).Select(b =>
        {
            var length = b == 0 ? 480 : 1440;
            var notes = new Notes($"n{b}", 0, length, [new Tone($"n{b}", key.Spell(48 + b))]);
            return new Bar(b, [new Voice(1, [notes]), new Voice(2, [new Rest($"r{b}", 0, length)])]) { Length = b == 0 ? length : null };
        }).ToList();
        await host.Store.SavePieceAsync(piece, MeiWriter.Write(new Score(key, new Meter(3, 4), bars)), []);
        await host.Store.SetSettingAsync(PlayAlong.ModeKeyOf("library"), wait ? "wait" : "time");
        await host.Store.StartSessionAsync(piece.Id, "custom", DateTimeOffset.Now);
        var view = host.Context.Render<Piece>(p => p.Add(c => c.Id, piece.Id));
        view.WaitForElement(".pause-keys .mu-primary");
        await view.InvokeAsync(() => view.Find(".pause-keys .mu-primary").ClickAsync(new MouseEventArgs()));
        view.WaitForAssertion(() => Assert.NotNull(view.FindComponent<PlayAlong>().Instance.Music));
        await view.InvokeAsync(() => view.Instance.Looped(0, 16));
        var player = view.FindComponent<PlayAlong>().Instance;
        await view.InvokeAsync(() => player.Start());
        var clears = host.Browser.Marks.Count(m => m == "clearMarks");
        await view.InvokeAsync(() => Task.WhenAll(new[] { 2, 3, 4, 5, 6, 6, 7, 7 }.Select(view.Instance.Chose)));
        view.WaitForAssertion(() => Assert.Equal(7, player.Music!.EntryIndex));
        Assert.True(player.Running);
        Assert.Equal(17, player.Music!.LoopBars);
        Assert.Equal(Enumerable.Range(7, 10).Concat([0, 1, 2]), player.Music.Notes.Take(13).Select(n => n.Bar));
        Assert.InRange(host.Browser.Marks.Count(m => m == "clearMarks") - clears, 1, 2);
        var selection = host.Browser.BarChoices[^1];
        Assert.Equal(0, selection.GetProperty("from").GetInt32());
        Assert.Equal(16, selection.GetProperty("to").GetInt32());
        if (wait)
        {
            var answer = host.Context.Services.GetRequiredService<SessionRegistry>().DrillAnswer!;
            Assert.Equal(55, answer(true)[0][0]);
        }
        else
        {
            var path = host.Browser.CursorPaths[^1];
            Assert.Equal(10, path.GetProperty("introBars").GetInt32());
            Assert.Equal(49, path.GetProperty("iterations").GetInt32());
            Assert.Equal(27, path.GetProperty("bars").GetArrayLength());
        }
        await view.InvokeAsync(() => Task.WhenAll(view.Instance.Chose(8), view.Instance.Chose(9), player.StopAndSaveAsync()))
            .WaitAsync(TimeSpan.FromSeconds(5), Xunit.TestContext.Current.CancellationToken);
        Assert.False(player.Running);
        Assert.Equal(16, host.Browser.BarChoices[^1].GetProperty("to").GetInt32());
        // At rest an outside tap still extends the range, and dragging can shrink it explicitly.
        await view.InvokeAsync(() => view.Instance.Chose(18));
        view.WaitForAssertion(() => Assert.Equal(18, host.Browser.BarChoices[^1].GetProperty("to").GetInt32()));
        await view.InvokeAsync(() => view.Instance.Looped(2, 8));
        view.WaitForAssertion(() => Assert.Equal(2, host.Browser.BarChoices[^1].GetProperty("from").GetInt32()));
    }
}
