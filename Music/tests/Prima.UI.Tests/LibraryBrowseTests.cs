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

public class LibraryBrowseTests
{
    // A piece in notation: four bars of crotchets, with its composer, title and work.
    private static async Task<PieceRecord> Add(ComponentHost host, string id, string composer, string title, string? work, int level = 7)
    {
        var piece = new PieceRecord(id, "collection", title, composer, work, "mei", null, null, level, level, null, 4, null, null, null, null, null, null, null, null, null,
            DateTimeOffset.Now, null);
        var bars = Enumerable.Range(1, 4).Select(b => new Bar(b, [new Voice(1, [.. Enumerable.Range(0, 4).Select(q =>
            new Notes($"n{b}-{q}", q * Ticks.Quarter, Ticks.Quarter, [new Tone($"t{b}-{q}", new Spelled(Step.C, 0, 4))]))])])).ToArray();
        await host.Store.SavePieceAsync(piece, MeiWriter.Write(new Score(new Prima.Core.Music.Key(0), new Meter(4, 4), bars)), []);
        return piece;
    }

    private static async Task AddFew(ComponentHost host)
    {
        await Add(host, "wtc1", "Johann Sebastian Bach", "Prelude No. 1 in C major, BWV 846", "The Well-Tempered Clavier, Book I");
        await Add(host, "allemande", "Johann Sebastian Bach", "Allemande", "English Suites");
        await Add(host, "op10-1", "Frédéric Chopin", "Étude in C major, Op. 10 No. 1", "Études", 12);
        await Add(host, "maple", "Scott Joplin", "Maple Leaf Rag", "Rags, marches and waltzes", 9);
    }

    [Fact]
    public async Task TheLibraryOpensOnWaysInWithHowManyPiecesEachHolds()
    {
        await using var host = new ComponentHost();
        await AddFew(host);

        var view = host.Context.Render<Pages.Library>();

        view.WaitForAssertion(() => Assert.Contains("Composers", view.FindAll(".section-title").Select(h => h.TextContent)));
        var chips = view.FindAll(".shelf-chip").Select(a => a.TextContent.Trim()).ToList();
        Assert.Contains("Johann Sebastian Bach 2", chips);
        Assert.Contains("Baroque 2", chips);
        Assert.Contains("Ragtime 1", chips);
        Assert.Contains("Preludes and fugues 1", chips);
        Assert.Contains("Dances 2", chips);
        Assert.Contains("Études 1", chips);
        Assert.Contains("Everything 4", chips);
        Assert.Equal("library?by=composer&of=Johann%20Sebastian%20Bach", view.FindAll(".shelf-chip").First(a => a.TextContent.StartsWith("Johann", StringComparison.Ordinal)).GetAttribute("href"));
    }

    [Fact]
    public async Task AComposersPiecesAreListedByKind()
    {
        await using var host = new ComponentHost();
        await AddFew(host);
        host.Context.Services.GetRequiredService<NavigationManager>().NavigateTo("library?by=composer&of=Johann%20Sebastian%20Bach");

        var view = host.Context.Render<Pages.Library>();

        view.WaitForAssertion(() => Assert.Equal(2, view.FindAll(".piece-item").Count));
        Assert.Equal(["Preludes and fugues", "Dances"], view.FindAll("section .section-title").Select(h => h.TextContent));
        Assert.All(view.FindAll(".piece-hear"), b => Assert.Equal("▶", b.TextContent));
    }

    [Fact]
    public async Task AListOfManyComposersHasAKeyForEachLetterTheirSurnamesStartWith()
    {
        await using var host = new ComponentHost();
        await AddFew(host);
        host.Context.Services.GetRequiredService<NavigationManager>().NavigateTo("library?by=all");

        // Three composers: a key each.
        var few = host.Context.Render<Pages.Library>();

        few.WaitForAssertion(() => Assert.Equal(4, few.FindAll(".piece-item").Count));
        Assert.Equal(["Johann Sebastian Bach 2", "Frédéric Chopin 1", "Scott Joplin 1"], few.FindAll(".shelf-jump .shelf-chip").Select(a => a.TextContent.Trim()));

        // Thirteen: a key for each letter, with how many pieces are under it, going to the first composer there.
        string[] more = ["Carl Czerny", "Muzio Clementi", "Johann Burgmüller", "Antonín Dvořák", "Edvard Grieg", "George Frideric Handel", "Joseph Haydn",
            "Franz Liszt", "Felix Mendelssohn", "Wolfgang Amadeus Mozart"];
        foreach (var (name, i) in more.Select((name, i) => (name, i)))
        {
            await Add(host, $"more-{i}", name, $"Piece {i}", null);
        }

        var many = host.Context.Render<Pages.Library>();

        many.WaitForAssertion(() => Assert.Equal(14, many.FindAll(".piece-item").Count));
        var keys = many.FindAll(".shelf-jump .shelf-chip");
        Assert.Equal(["B 3", "C 3", "D 1", "G 1", "H 2", "J 1", "L 1", "M 2"], keys.Select(a => a.TextContent.Trim()));
        Assert.EndsWith("#group-2", keys[1].GetAttribute("href"));
        Assert.Equal("Frédéric Chopin", many.Find("#group-2").TextContent);
    }

    [Fact]
    public async Task AnOpeningPlaysOnThePianoWhenOneTakesNotes()
    {
        await using var host = new ComponentHost();
        var piece = await Add(host, "wtc1", "Johann Sebastian Bach", "Prelude No. 1 in C major, BWV 846", null);
        var piano = new Piano();
        var speaker = new Speaker();
        using var openings = new Openings(host.Store, new NoBundle(), piano, speaker, new StopwatchClock());

        Assert.Null(await openings.ToggleAsync(piece));

        Assert.Equal("wtc1", openings.Playing);
        Assert.Equal("on the piano", openings.Where);
        await WaitFor(() => piano.Sent.Any(m => m[0] == 0x9F && m[1] == 60));
        Assert.Empty(speaker.Played);

        Assert.Null(await openings.ToggleAsync(piece));

        Assert.Null(openings.Playing);
        Assert.Equal(1, piano.Silenced);
    }

    [Fact]
    public async Task WithoutThePianoAnOpeningPlaysOnTheTablet()
    {
        await using var host = new ComponentHost();
        var piece = await Add(host, "wtc1", "Johann Sebastian Bach", "Prelude No. 1 in C major, BWV 846", null);
        var speaker = new Speaker();
        using var openings = new Openings(host.Store, new NoBundle(), new NoKeyboard(), speaker, new StopwatchClock());

        Assert.Null(await openings.ToggleAsync(piece));

        Assert.Equal("on the tablet", openings.Where);
        var midi = Assert.Single(speaker.Played);
        Assert.Equal("MThd"u8.ToArray(), midi[..4]);
        Assert.Equal(16, Prima.Core.Library.MidiFile.Read(midi).Notes.Count);

        openings.Stop();

        Assert.Null(openings.Playing);
        Assert.Equal(1, speaker.Stopped);
    }

    [Fact]
    public async Task APdfWithoutMidiSaysThereIsNothingToPlay()
    {
        await using var host = new ComponentHost();
        var pdf = new PieceRecord("scan", "import", "A scan", "", null, "pdf", "scan.pdf", null, null, null, null, null, null, null, null, null, null, null, null, null, null,
            DateTimeOffset.Now, null);
        await host.Store.SavePieceAsync(pdf, null, []);
        var speaker = new Speaker();
        using var openings = new Openings(host.Store, new NoBundle(), new NoKeyboard(), speaker, new StopwatchClock());

        var said = await openings.ToggleAsync(pdf);

        Assert.Equal("A scan came without MIDI, so there's nothing to play until its notes are read.", said);
        Assert.Null(openings.Playing);
        Assert.Empty(speaker.Played);
    }

    private static async Task WaitFor(Func<bool> condition)
    {
        for (var waited = 0; !condition(); waited += 20)
        {
            Assert.True(waited < 3000, "Waited three seconds.");
            await Task.Delay(20, Xunit.TestContext.Current.CancellationToken);
        }
    }

    // A piano that takes notes, keeping what it was sent.
    private sealed class Piano : IKeyboard
    {
        public List<byte[]> Sent { get; } = [];

        public int Silenced { get; private set; }

        public string? Name => "P-225B";

        public bool CanSend => true;

        public SensingJitter Sensing { get; } = new();

        public event Action<MidiEvent>? Received { add { } remove { } }

        public event Action? Changed { add { } remove { } }

        public void Send(byte[] message, long atNs)
        {
            lock (Sent) Sent.Add(message);
        }

        public void Silence() => Silenced++;
    }

    // The tablet's speaker, keeping what it was given to play.
    private sealed class Speaker : ISpeaker
    {
        public List<byte[]> Played { get; } = [];

        public int Stopped { get; private set; }

        public bool CanPlay => true;

        public void Play(byte[] midi) => Played.Add(midi);

        public void Stop() => Stopped++;
    }
}
