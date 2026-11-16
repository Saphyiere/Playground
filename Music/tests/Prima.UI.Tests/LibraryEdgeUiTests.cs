using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Prima.Core.Keyboard;
using Prima.Core.Music;
using Prima.Core.Timing;
using Prima.UI.Host;
using Prima.UI.Library;
using Prima.UI.Storage;

namespace Prima.UI.Tests;

/// <summary>
/// From the review of main at a7ab94d: the Library's lists and its openings at their edges. Each fails on that code; the place in
/// it is given with each.
/// </summary>
public class LibraryEdgeUiTests
{
    // A piece in notation, as LibraryBrowseTests adds one: four bars of crotchets, with its composer and title.
    private static async Task<PieceRecord> Add(ComponentHost host, string id, string composer, string title)
    {
        var piece = new PieceRecord(id, "collection", title, composer, null, "mei", null, null, 7, 7, null, 4, null, null, null, null, null, null, null, null, null,
            DateTimeOffset.Now, null);
        var bars = Enumerable.Range(1, 4).Select(b => new Bar(b, [new Voice(1, [.. Enumerable.Range(0, 4).Select(q =>
            new Notes($"n{b}-{q}", q * Ticks.Quarter, Ticks.Quarter, [new Tone($"t{b}-{q}", new Spelled(Step.C, 0, 4))]))])])).ToArray();
        await host.Store.SavePieceAsync(piece, MeiWriter.Write(new Score(new Prima.Core.Music.Key(0), new Meter(4, 4), bars)), []);
        return piece;
    }

    // Openings.cs:36-62: ToggleAsync stops what's playing, then reads the new piece's notes, then takes its place, without
    // stopping an opening that started while it read. Two taps close together so leave both openings playing on the piano, and
    // Stop (■, or leaving the Library) reaches only the later one: the other goes on sending notes for up to half a minute.
    // concept.md: "One opening at a time … stops … when the Library is left".
    [Fact]
    public async Task TwoOpeningsAskedForTogetherDoNotBothPlayOnAfterStop()
    {
        await using var host = new ComponentHost();
        var first = await Add(host, "first", "Johann Sebastian Bach", "Prelude");
        var second = await Add(host, "second", "Frédéric Chopin", "Étude");
        var piano = new Piano();
        using var openings = new Openings(host.Store, new NoBundle(), piano, new Speaker(), new StopwatchClock());

        await Task.WhenAll(openings.ToggleAsync(first), openings.ToggleAsync(second));
        openings.Stop();
        var sent = piano.Count;
        await Task.Delay(1500, Xunit.TestContext.Current.CancellationToken);

        Assert.Null(openings.Playing);
        Assert.Equal(sent, piano.Count);
    }

    // Library.razor:44-53 draws "Find here" on every list, but :86-101 (All composers) never reads what's typed in it.
    // concept.md: a list opened from a way in "can be … searched".
    [Fact]
    public async Task FindHereOnAllComposersLeavesTheComposersThatMatch()
    {
        await using var host = new ComponentHost();
        await Add(host, "wtc1", "Johann Sebastian Bach", "Prelude No. 1 in C major, BWV 846");
        await Add(host, "op10-1", "Frédéric Chopin", "Étude in C major, Op. 10 No. 1");
        await Add(host, "maple", "Scott Joplin", "Maple Leaf Rag");
        host.Context.Services.GetRequiredService<NavigationManager>().NavigateTo("library?by=composers");
        var view = host.Context.Render<Pages.Library>();
        view.WaitForAssertion(() => Assert.Equal(3, view.FindAll(".shelf-chip").Count));

        await view.InvokeAsync(() => view.Find("input[type=search]").InputAsync(new ChangeEventArgs { Value = "Chopin" }));

        view.WaitForAssertion(() => Assert.Equal(["Frédéric Chopin 1"], view.FindAll(".shelf-chip").Select(a => a.TextContent.Trim())), TimeSpan.FromSeconds(3));
    }

    // Library.razor:136-199: the home's Periods, Kinds, Levels and Lists (Everything among them) are all inside the block that
    // shows when some piece has a composer. A Library of imported MIDI files or PDFs, which have none, opens on no ways in.
    [Fact]
    public async Task ALibraryWhosePiecesHaveNoComposerStillOpensOnEverything()
    {
        await using var host = new ComponentHost();
        await Add(host, "mine", "", "A piece of my own");

        var view = host.Context.Render<Pages.Library>();

        view.WaitForAssertion(() => Assert.Contains("Everything 1", view.FindAll(".shelf-chip").Select(a => a.TextContent.Trim())), TimeSpan.FromSeconds(3));
    }

    // Library.razor:217 and :437-441 count the group of pieces with no composer as a composer: twelve composers and one piece
    // without one are "by 13 composers", and go over to a key for each letter, which concept.md gives to lists "past twelve
    // composers".
    [Fact]
    public async Task ThePiecesWithoutAComposerAreNotCountedAsOne()
    {
        await using var host = new ComponentHost();
        string[] composers = ["Johann Sebastian Bach", "Frédéric Chopin", "Scott Joplin", "Carl Czerny", "Muzio Clementi", "Johann Burgmüller", "Antonín Dvořák", "Edvard Grieg",
            "George Frideric Handel", "Joseph Haydn", "Franz Liszt", "Felix Mendelssohn"];
        foreach (var (name, i) in composers.Select((name, i) => (name, i))) await Add(host, $"piece-{i}", name, $"Piece {i}");
        await Add(host, "mine", "", "A piece of my own");
        host.Context.Services.GetRequiredService<NavigationManager>().NavigateTo("library?by=all");

        var view = host.Context.Render<Pages.Library>();

        view.WaitForAssertion(() => Assert.Equal(13, view.FindAll(".piece-item").Count));
        Assert.Contains("13 pieces by 12 composers.", view.FindAll(".section-note").Select(n => n.TextContent.Trim()));
        Assert.Equal(13, view.FindAll(".shelf-jump .shelf-chip").Count);
    }

    // A piano that takes notes, counting what it was sent.
    private sealed class Piano : IKeyboard
    {
        private int count;

        public int Count => Volatile.Read(ref count);

        public string? Name => "P-225B";

        public bool CanSend => true;

        public SensingJitter Sensing { get; } = new();

        public event Action<MidiEvent>? Received { add { } remove { } }

        public event Action? Changed { add { } remove { } }

        public void Send(byte[] message, long atNs) => Interlocked.Increment(ref count);

        public void Silence()
        {
        }
    }

    private sealed class Speaker : ISpeaker
    {
        public bool CanPlay => true;

        public void Play(byte[] midi)
        {
        }

        public void Stop()
        {
        }
    }
}
