using Bunit;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Prima.Core.Music;
using Prima.UI.Components;
using Prima.UI.Pages;
using Key = Prima.Core.Music.Key;

namespace Prima.UI.Tests;

/// <summary>
/// A piece played in another key, chosen in its More: the music on the stand, the notes a run expects and the run kept all go by
/// it, it's kept for the piece, and As written brings it back.
/// </summary>
public class PieceKeyTests
{
    // Waits for it on the page, then taps it, as a finger would: the wait is outside the renderer, so it can draw meanwhile.
    private static async Task Tap(IRenderedComponent<Piece> view, string selector)
    {
        view.WaitForElement(selector);
        await view.InvokeAsync(() => view.Find(selector).ClickAsync(new MouseEventArgs()));
    }

    private static int FirstNote(string mei) => MeiReader.Read(mei).Expected()[0].Midi;

    [Fact]
    public async Task AKeyChosenInMoreMovesTheMusicTheRunAndTheNextVisit()
    {
        var piano = new TestPiano(plugged: true);
        await using var host = new ComponentHost(keyboard: piano);
        await host.Store.SetSettingAsync(PlayAlong.ModeKeyOf("library"), "wait");
        var piece = await host.AddPiece(bars: 1);
        var view = host.Context.Render<Piece>(p => p.Add(c => c.Id, piece.Id));
        await Tap(view, ".piece-front-bar .mu-primary:not([disabled])");

        // As written: C major, its own key lit, As written chosen.
        await Tap(view, "[data-action=more]");
        var group = view.WaitForElement(".play-more .more-key");
        Assert.Equal("C major", group.QuerySelector(".key-piano-name")!.TextContent);
        Assert.Equal("true", view.Find(".more-key [data-action=as-written]").GetAttribute("aria-pressed"));
        Assert.Equal("true", view.Find(".more-key .key-piano button[aria-label='C major']").GetAttribute("aria-pressed"));

        // D major: the sheet's music and the run's notes up a tone, kept for the piece.
        await Tap(view, ".more-key .key-piano button[aria-label='D major']");
        view.WaitForAssertion(() => Assert.Equal(62, FirstNote(view.FindComponent<Sheet>().Instance.Mei)));
        Assert.Equal([62], view.FindComponent<PlayAlong>().Instance.Music!.Notes.Select(n => n.Midi));
        Assert.Equal("2", (await host.Store.GetSettingsAsync()).GetValueOrDefault($"Library.Key.{piece.Id}"));
        Assert.Equal("D major", view.Find(".more-key .key-piano-name").TextContent);
        Assert.Equal("false", view.Find(".more-key [data-action=as-written]").GetAttribute("aria-pressed"));
        Assert.StartsWith("Written in C major.", view.Find(".more-key .section-note").TextContent);

        // A run in Wait for me, its one note played: kept with its key.
        await Tap(view, ".play-more .change-done");
        await Tap(view, ".play-bar [data-piano=go]");
        view.WaitForAssertion(() => Assert.Contains(view.FindAll(".play-bar button"), b => b.TextContent == "Stop"));
        piano.Play(62, 1_000_000_000);
        await Eventually(async () => Assert.Single(await host.Store.GetPieceRunsAsync(piece.Id)));
        var run = Assert.Single(await host.Store.GetPieceRunsAsync(piece.Id)).Run;
        Assert.Equal((new Transposition(1, 2), "D major", 1), (run.Transposed, run.KeyName, run.Right));

        // The piece opens in D major the next time (in its session, left going).
        var again = host.Context.Render<Piece>(p => p.Add(c => c.Id, piece.Id));
        again.WaitForAssertion(() => Assert.Equal(62, FirstNote(again.FindComponent<Sheet>().Instance.Mei)));
    }

    [Fact]
    public async Task AsWrittenBringsTheMusicBack()
    {
        await using var host = new ComponentHost();
        var piece = await host.AddPiece(bars: 1);
        await host.Store.SetSettingAsync($"Library.Key.{piece.Id}", "-3");
        var view = host.Context.Render<Piece>(p => p.Add(c => c.Id, piece.Id));
        // Kept in E♭ major: its C is an E♭, up a minor third, the nearer way; its front says the key.
        view.WaitForAssertion(() => Assert.Equal(63, FirstNote(view.FindComponent<Sheet>().Instance.Mei)));
        Assert.Contains(view.FindAll(".piece-front-bar .mu-readout"), r => r.TextContent == "KeyE♭ major");
        await Tap(view, ".piece-front-bar .mu-primary:not([disabled])");
        await Tap(view, "[data-action=more]");

        await Tap(view, ".more-key [data-action=as-written]");

        view.WaitForAssertion(() => Assert.Equal(60, FirstNote(view.FindComponent<Sheet>().Instance.Mei)));
        Assert.Null((await host.Store.GetSettingsAsync()).GetValueOrDefault($"Library.Key.{piece.Id}"));
        Assert.Equal("C major", view.Find(".more-key .key-piano-name").TextContent);
    }

    [Fact]
    public async Task APdfWithoutItsNotesSaysToConvertItFirst()
    {
        await using var host = new ComponentHost();
        var piece = await host.AddPiece(pdf: true);
        var view = host.Context.Render<Piece>(p => p.Add(c => c.Id, piece.Id));

        await Tap(view, "[data-action=more]");

        var group = view.WaitForElement(".play-more .more-key");
        Assert.All(group.QuerySelectorAll(".key-piano button"), key => Assert.True(key.HasAttribute("disabled")));
        Assert.Contains("Convert to notation first", group.TextContent);
    }

    private static async Task Eventually(Func<Task> check)
    {
        for (var i = 0; ; i++)
        {
            try
            {
                await check();
                return;
            }
            catch (Exception) when (i < 100)
            {
                await Task.Delay(50, Xunit.TestContext.Current.CancellationToken);
            }
        }
    }
}
