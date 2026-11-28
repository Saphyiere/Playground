using Bunit;
using Bunit.Rendering;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Prima.Core.Music;
using Prima.UI.Components;
using Prima.UI.Glossary;
using Prima.UI.Pages;
using Prima.UI.Storage;

namespace Prima.UI.Tests;

/// <summary>
/// What's this, on every screen: the ? key in every head turns it on, the panel says a piece's key and offers its notes' names,
/// a tap on the music says what's there (a note by the key it plays, a key signature by the key the music is in, a chord by its
/// notes, or which of several), a tap on a control says what it does, and leaving the screen turns it off.
/// </summary>
public class WhatsThisTests
{
    // B minor with two sharps: up its triad to an A6 four ledger lines over the staff, down to the leading note, home to B.
    private static async Task<PieceRecord> AddBMinor(ComponentHost host)
    {
        var piece = (await host.AddPiece()) with { Id = "b-minor", Title = "Study" };
        static Notes Note(string id, int start, int length, params Spelled[] pitches) => new(id, start, length, [.. pitches.Select((p, i) => new Tone(pitches.Length == 1 ? id : $"{id}{i}", p))]);
        var score = new Score(new Prima.Core.Music.Key(2), new Meter(4, 4),
        [
            new Bar(1, [new Voice(1, [Note("b4", 0, 480, new Spelled(Step.B, 0, 4)), Note("d5", 480, 480, new Spelled(Step.D, 0, 5)), Note("f5", 960, 480, new Spelled(Step.F, 1, 5)), Note("a6", 1440, 480, new Spelled(Step.A, 0, 6))]), new Voice(2, [Note("b2", 0, 1920, new Spelled(Step.B, 0, 2))])]),
            new Bar(2, [new Voice(1, [Note("ch", 0, 1920, new Spelled(Step.A, 1, 4), new Spelled(Step.C, 1, 5), new Spelled(Step.F, 1, 5))]), new Voice(2, [Note("f2", 0, 1920, new Spelled(Step.F, 1, 2))])]),
            new Bar(3, [new Voice(1, [Note("end", 0, 1920, new Spelled(Step.B, 0, 4))]), new Voice(2, [Note("b1", 0, 1920, new Spelled(Step.B, 0, 1))])]),
        ]);
        await host.Store.SavePieceAsync(piece, MeiWriter.Write(score), []);
        await host.Store.StartSessionAsync(piece.Id, "custom", DateTimeOffset.Now);
        return piece;
    }

    private static IRenderedComponent<ContainerFragment> Open(ComponentHost host, string id) => host.Context.Render(b =>
    {
        b.OpenComponent<WhatsThisLayer>(0);
        b.CloseComponent();
        b.OpenComponent<Piece>(1);
        b.AddAttribute(2, nameof(Piece.Id), id);
        b.CloseComponent();
    });

    private static Task Tap(IRenderedComponent<ContainerFragment> view, string selector) => view.InvokeAsync(() => view.Find(selector).ClickAsync(new MouseEventArgs()));

    private static Task TapMusic(IRenderedComponent<ContainerFragment> view, params SheetSymbol[] near) => view.InvokeAsync(() => view.FindComponent<Sheet>().Instance.SymbolsTapped(near));

    [Fact]
    public async Task OnAPieceItSaysTheKeyAndNamesTheNotesAsChosen()
    {
        await using var host = new ComponentHost();
        var piece = await AddBMinor(host);
        var view = Open(host, piece.Id);
        view.WaitForElement(".sheet");

        await Tap(view, ".page-head .whats-this-key");

        view.WaitForAssertion(() => Assert.Equal("B minor", view.Find(".whats-this-key-name").TextContent.Trim()));
        Assert.Equal("2 sharps, F♯ C♯. It ends on B.", view.Find(".whats-this-key-block .whats-this-words").TextContent.Trim());
        Assert.Equal([true], host.Browser.Explaining);
        // The music marks what there is to tap: told as it's drawn, or after.
        view.WaitForAssertion(() => Assert.True(host.Browser.SheetsExplaining.LastOrDefault() || host.Browser.Renders.Last().Options.GetProperty("explain").GetBoolean()));
        Assert.Null(view.FindComponent<Sheet>().Instance.NoteLabels);

        // The hard ones: the A over the staff and the B under the bass staff, each by its octave (two sharps aren't enough to make
        // the signature's hard, and the A sharp is printed beside its note).
        await Tap(view, "[data-names=hard]");
        view.WaitForAssertion(() => Assert.Equal(["A6", "B1"], view.FindComponent<Sheet>().Instance.NoteLabels!.Select(l => l.Text)));
        Assert.Equal("hard", (await host.Store.GetSettingsAsync())["Notation.NoteNames"]);
        // All of them, by letter, the sharps the signature's.
        await Tap(view, "[data-names=all]");
        view.WaitForAssertion(() => Assert.Equal(["B", "D", "F♯", "A6", "B", "A♯", "C♯", "F♯", "F♯", "B", "B1"],
            view.FindComponent<Sheet>().Instance.NoteLabels!.Select(l => l.Text)));
        // Done: off, the names staying as chosen.
        await Tap(view, "[data-action=whats-this-done]");
        view.WaitForAssertion(() => Assert.Empty(view.FindAll(".whats-this")));
        Assert.Equal([true, false], host.Browser.Explaining);
        Assert.NotNull(view.FindComponent<Sheet>().Instance.NoteLabels);
    }

    [Fact]
    public async Task ATapOnTheMusicSaysWhatsThere()
    {
        await using var host = new ComponentHost();
        var piece = await AddBMinor(host);
        var view = Open(host, piece.Id);
        view.WaitForElement(".sheet");
        await Tap(view, ".page-head .whats-this-key");
        view.WaitForElement(".whats-this-key-name");

        // A note: the key it plays, where it's printed, and why it's sharp.
        await TapMusic(view, new SheetSymbol("f5", "note", null) { Away = 3, Left = 100, Top = 50, Right = 110, Bottom = 58 });
        view.WaitForAssertion(() => Assert.Equal("F♯5", view.Find(".explain-card-name").TextContent.Trim()));
        Assert.Equal(["On the top line of the treble staff.", "It's sharp by the key signature: 2 sharps, F♯ C♯."], view.FindAll(".explain-card p").Select(p => p.TextContent.Trim()));

        // The key signature: its sharps, and the key the music is in.
        await TapMusic(view, new SheetSymbol("ks", "keySig", "m1"));
        view.WaitForAssertion(() => Assert.Equal("Key signature: 2 sharps", view.Find(".explain-card-name").TextContent.Trim()));
        Assert.Contains("The music here is in B minor.", view.FindAll(".explain-card p").Select(p => p.TextContent.Trim()));

        // A chord's notes as near as each other: the chord, from the top.
        await TapMusic(view, new SheetSymbol("ch2", "note", "ch") { Away = 1 }, new SheetSymbol("ch1", "note", "ch") { Away = 2 });
        view.WaitForAssertion(() => Assert.Equal("Chord: F♯5 · C♯5 · A♯4", view.Find(".explain-card-name").TextContent.Trim()));
        Assert.Equal(["F♯5 and C♯5: sharp by the key signature: 2 sharps, F♯ C♯.", "A♯4: sharp by its own sign."], view.FindAll(".explain-card p").Select(p => p.TextContent.Trim()));

        // Two different things as near: which one, and the one chosen.
        await TapMusic(view, new SheetSymbol("b4", "note", null) { Away = 2 }, new SheetSymbol("sl", "slur", null) { Away = 4 });
        view.WaitForAssertion(() => Assert.Equal("Which one?", view.Find(".explain-card-name").TextContent.Trim()));
        Assert.Equal(["B4", "Slur"], view.FindAll(".explain-card-choices .mu-btn").Select(b => b.TextContent.Trim()));
        await Tap(view, ".explain-card-choices .mu-btn:last-child");
        view.WaitForAssertion(() => Assert.Equal("Slur", view.Find(".explain-card-name").TextContent.Trim()));
        // More: its glossary entry, in place.
        await Tap(view, ".explain-card-more");
        view.WaitForAssertion(() => Assert.Equal("Slur", view.Find(".explain-entry .explain-card-name").TextContent.Trim()));
    }

    [Fact]
    public async Task AnySheetSaysWhatsOnItsOwnMusic()
    {
        // An exercise's or a drill's passage: a sheet with nobody to tell, in A minor (no sharps or flats, ending on A).
        await using var host = new ComponentHost();
        var score = new Score(new Prima.Core.Music.Key(0), new Meter(4, 4),
        [
            new Bar(1, [new Voice(1, [new Notes("e", 0, 960, [new Tone("e5", new Spelled(Step.E, 0, 5))]), new Notes("g", 960, 960, [new Tone("g5", new Spelled(Step.G, 1, 5))])]), new Voice(2, [new Notes("a", 0, 1920, [new Tone("a2", new Spelled(Step.A, 0, 2))])])]),
            new Bar(2, [new Voice(1, [new Notes("end", 0, 1920, [new Tone("a4", new Spelled(Step.A, 0, 4))])]), new Voice(2, [new Notes("low", 0, 1920, [new Tone("a1", new Spelled(Step.A, 0, 2))])])]),
        ]);
        var mei = MeiWriter.Write(score);
        var view = host.Context.Render(b =>
        {
            b.OpenComponent<WhatsThisLayer>(0);
            b.CloseComponent();
            b.OpenComponent<Sheet>(1);
            b.AddAttribute(2, nameof(Sheet.Mei), mei);
            b.CloseComponent();
        });
        var whatsThis = host.Context.Services.GetRequiredService<WhatsThis>();
        await view.InvokeAsync(() => whatsThis.SetAsync(true));

        await view.InvokeAsync(() => view.FindComponent<Sheet>().Instance.SymbolsTapped([new SheetSymbol("g5", "note", null)]));
        view.WaitForAssertion(() => Assert.Equal("G♯5", view.Find(".explain-card-name").TextContent.Trim()));
        Assert.Equal(["Just above the treble staff.", "It's sharp by its own sign."], view.FindAll(".explain-card p").Select(p => p.TextContent.Trim()));
        await view.InvokeAsync(() => view.FindComponent<Sheet>().Instance.SymbolsTapped([new SheetSymbol("k", "keySig", "m1")]));
        view.WaitForAssertion(() => Assert.Contains("The music here is in A minor.", view.FindAll(".explain-card p").Select(p => p.TextContent.Trim())));
    }

    [Fact]
    public async Task ATapOnAControlSaysWhatItDoesAndLeavingTheScreenTurnsItOff()
    {
        await using var host = new ComponentHost();
        var piece = await AddBMinor(host);
        var view = Open(host, piece.Id);
        view.WaitForElement(".sheet");
        await Tap(view, ".page-head .whats-this-key");
        view.WaitForElement(".whats-this-key-name");
        var whatsThis = host.Context.Services.GetRequiredService<WhatsThis>();

        // The loop key, as explain.js tells of a tap on it; then something with no words yet.
        await view.InvokeAsync(() => whatsThis.Tapped("loop", 900, 740, 980, 770));
        view.WaitForAssertion(() => Assert.Equal("Loop", view.Find(".explain-card-name").TextContent.Trim()));
        Assert.Equal("false", view.Find("[data-explain=loop]").GetAttribute("aria-checked"));
        await view.InvokeAsync(() => whatsThis.Tapped(null, 10, 10, 20, 20));
        view.WaitForAssertion(() => Assert.Equal("Nothing to say about that yet", view.Find(".explain-card-name").TextContent.Trim()));

        // Every marked control has words.
        foreach (var marked in view.FindAll("[data-explain]").Select(e => e.GetAttribute("data-explain")!).Distinct()) Assert.NotNull(Explanations.Of(marked));

        await view.InvokeAsync(() => host.Context.Services.GetRequiredService<NavigationManager>().NavigateTo("library"));
        view.WaitForAssertion(() => Assert.False(whatsThis.On));
        Assert.Equal([true, false], host.Browser.Explaining);
    }
}
