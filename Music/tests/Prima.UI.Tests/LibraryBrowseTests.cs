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
    private static async Task<PieceRecord> Add(ComponentHost host, string id, string composer, string title, string? work, int level = 7, string origin = "collection", string? source = null)
    {
        var piece = new PieceRecord(id, origin, title, composer, work, "mei", null, null, level, level, null, 4, source, null, null, null, null, null, null, null, null,
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

    // A row of the Library's list as it reads: its name, then how many it holds.
    private static List<string> Rows(IRenderedComponent<Pages.Library> view, string selector = ".lib-list .col-item") =>
        [.. view.FindAll(selector).Select(a => $"{a.QuerySelector("span")!.TextContent} {a.QuerySelector(".n")!.TextContent}")];

    // The owner, 17 November: the Library centres on the catalogue, "but we always want to keep that left side list so that
    // it's easy to go back and forth".
    [Fact]
    public async Task TheLibrarysListHasEveryComposerSurnameFirstThenTheKindsAndLevelsWithHowManyPiecesEachHolds()
    {
        await using var host = new ComponentHost();
        await AddFew(host);

        var view = host.Context.Render<Pages.Library>();

        view.WaitForAssertion(() => Assert.Equal(["Near your level 2", "Bach, Johann Sebastian 2", "Chopin, Frédéric 1", "Joplin, Scott 1"], Rows(view)));
        Assert.Equal("4 pieces", view.Find(".page-kicker").TextContent);
        Assert.Equal("library?by=composer&of=Johann%20Sebastian%20Bach", view.Find(".lib-list .col-item[href*='by=composer']").GetAttribute("href"));
        // With nothing uploaded, the pieces near your level are beside it, and the list says so.
        Assert.Equal("Near your level", view.Find(".col-head").TextContent);
        Assert.Equal("Near your level", view.Find(".lib-list .col-item[aria-current]").QuerySelector("span")!.TextContent);

        await view.InvokeAsync(() => view.FindAll(".col-by a").Single(a => a.TextContent == "Kinds").ClickAsync(new Microsoft.AspNetCore.Components.Web.MouseEventArgs()));

        view.WaitForAssertion(() => Assert.Equal(["Near your level 2", "Études 1", "Preludes and fugues 1", "Dances 2"], Rows(view)));

        await view.InvokeAsync(() => view.FindAll(".col-by a").Single(a => a.TextContent == "Levels").ClickAsync(new Microsoft.AspNetCore.Components.Web.MouseEventArgs()));

        view.WaitForAssertion(() => Assert.Equal(["Near your level 2", "Level 7 2", "Level 9 1", "Level 12 1"], Rows(view)));
    }

    // The owner, 17 November: "Just build A", with B's tabs: "the tab for the Beethoven part ... is way better looking". A
    // composer's kinds are tabs; a kind whose pieces each stand alone is a list of them.
    [Fact]
    public async Task AComposersKindsAreTabsAndPiecesThatStandAloneAreListedUnderTheirOwnNames()
    {
        await using var host = new ComponentHost();
        await AddFew(host);
        var nav = host.Context.Services.GetRequiredService<NavigationManager>();
        nav.NavigateTo("library?by=composer&of=Johann%20Sebastian%20Bach");

        var view = host.Context.Render<Pages.Library>();

        view.WaitForAssertion(() => Assert.Equal(["Preludes and fugues1", "Dances1"], view.FindAll(".settings-tab").Select(t => t.TextContent)));
        Assert.Equal("Johann Sebastian Bach", view.Find(".col-head").TextContent);
        Assert.Equal("2 pieces · 2 works", view.Find(".lib-main-head .col-sub").TextContent);
        Assert.Equal("Bach, Johann Sebastian", view.Find(".lib-list .col-item[aria-current]").QuerySelector("span")!.TextContent);
        // The catalogue number is apart from the name, with the set its source has it in.
        Assert.Equal(["Prelude No. 1 in C major"], view.FindAll(".piece-title").Select(t => t.TextContent));
        Assert.Equal("BWV 846 · The Well-Tempered Clavier, Book I · 4 bars", view.Find(".mov-name small").TextContent);
        Assert.All(view.FindAll(".piece-hear"), b => Assert.Equal("▶", b.TextContent));

        await view.InvokeAsync(() => view.FindAll(".settings-tab").Single(t => t.TextContent == "Dances1").ClickAsync(new Microsoft.AspNetCore.Components.Web.MouseEventArgs()));

        view.WaitForAssertion(() => Assert.Equal(["Allemande"], view.FindAll(".piece-title").Select(t => t.TextContent)));
        Assert.EndsWith("library?by=composer&of=Johann%20Sebastian%20Bach&kind=Dances", nav.Uri);
        // The tab took the address's place: the way back doesn't step through each tab.
        Assert.True(((Bunit.TestDoubles.BunitNavigationManager)nav).History.First().Options.ReplaceHistoryEntry);
    }

    // The owner, 17 November: a sonata's movements were rows among a hundred, and the same sonata stood in three places. A
    // work is one row; chosen, its movements are beside it in order, and the other versions of it under them.
    [Fact]
    public async Task AWorkIsOneRowAndChosenItHasItsMovementsInOrderWithTheOtherVersionsOfItUnderThem()
    {
        await using var host = new ComponentHost();
        await Add(host, "dcml-beethoven-piano-sonatas-08-2", "Ludwig van Beethoven", "Sonata No. 8 'Pathetique', Op. 13: II. Adagio cantabile", "Piano sonatas", 11, source: "DCML");
        await Add(host, "dcml-beethoven-piano-sonatas-08-1", "Ludwig van Beethoven", "Sonata No. 8 'Pathetique', Op. 13: I. Grave: Allegro di molto e con brio", "Piano sonatas", 12, source: "DCML");
        await Add(host, "dcml-beethoven-piano-sonatas-14-1", "Ludwig van Beethoven", "Sonata No. 'quasi una fantasia' ('Moonlight'), Op. 27/2: I. Adagio sostenuto", "Piano sonatas", 11, source: "DCML");
        await Add(host, "pdmx-moonlight", "Ludwig van Beethoven", "Moonlight Sonata 1st movement", null, 11, source: "PDMX");
        await Add(host, "pdmx-elise", "Ludwig van Beethoven", "Für Elise", null, 6, source: "PDMX");
        var nav = host.Context.Services.GetRequiredService<NavigationManager>();
        nav.NavigateTo("library?by=composer&of=Ludwig%20van%20Beethoven");

        var view = host.Context.Render<Pages.Library>();

        // Two sonatas, each a row with its number, its catalogue number and how much there is of it; the first is chosen.
        view.WaitForAssertion(() => Assert.Equal(2, view.FindAll(".lib-pair .col-work").Count));
        Assert.Equal(["Sonatas4", "Character pieces1"], view.FindAll(".settings-tab").Select(t => t.TextContent));
        Assert.Equal(["8", "14"], view.FindAll(".lib-pair .col-work .work-no").Select(n => n.TextContent));
        Assert.Equal(["Op. 13 · 2 movements", "Op. 27 No. 2 · 1 other version"], view.FindAll(".lib-pair .col-work small").Select(s => s.TextContent));
        Assert.Equal(["11–12", "11"], view.FindAll(".lib-pair .col-work .n").Select(n => n.TextContent));
        Assert.Equal("Sonata No. 8 “Pathetique”", view.Find(".lib-pair .col-head").TextContent);
        Assert.Equal(["I", "II"], view.FindAll(".lib-pair .mov-no").Select(n => n.TextContent));
        Assert.Equal(["Grave: Allegro di molto e con brio", "Adagio cantabile"], view.FindAll(".lib-pair .piece-title").Select(t => t.TextContent));

        // The Moonlight, its number from its id: its movement, then the arrangement of it by its own title.
        await view.InvokeAsync(() => view.FindAll(".lib-pair .col-work")[1].ClickAsync(new Microsoft.AspNetCore.Components.Web.MouseEventArgs()));

        view.WaitForAssertion(() => Assert.Equal("Sonata No. 14 “quasi una fantasia” “Moonlight”", view.Find(".lib-pair .col-head").TextContent));
        Assert.Equal(["Adagio sostenuto", "Moonlight Sonata 1st movement"], view.FindAll(".lib-pair .piece-title").Select(t => t.TextContent));
        Assert.Equal("Another version of it", view.Find(".lib-pair .others-title").TextContent);
        Assert.Equal("library/dcml-beethoven-piano-sonatas-14-1", view.Find(".lib-pair .mov-name").GetAttribute("href"));
        Assert.Contains("work=w%3Asonata%20no.%2014", nav.Uri, StringComparison.Ordinal);
        // Upright the same work is open where it is, the other closed.
        Assert.Equal(["false", "true"], view.FindAll(".work-list .work-head").Select(h => h.GetAttribute("aria-expanded")));
        Assert.Equal(["Adagio sostenuto", "Moonlight Sonata 1st movement"], view.FindAll(".work-list .piece-title").Select(t => t.TextContent));
    }

    // A kind is an index of every composer's works of it, each going to its composer opened at that work; and what's typed
    // above finds a piece by its work's name as the catalogue has it ("sonata 14" for a movement titled without its number).
    [Fact]
    public async Task AKindIsAnIndexOfItsWorksAndTheSearchFindsAPieceByItsWorksName()
    {
        await using var host = new ComponentHost();
        await AddFew(host);
        await Add(host, "dcml-beethoven-piano-sonatas-14-1", "Ludwig van Beethoven", "Sonata No. 'quasi una fantasia' ('Moonlight'), Op. 27/2: I. Adagio sostenuto", "Piano sonatas", 11);
        await Add(host, "dcml-beethoven-piano-sonatas-14-2", "Ludwig van Beethoven", "Sonata No. 'quasi una fantasia' ('Moonlight'), Op. 27/2: II. Allegretto", "Piano sonatas", 12);
        var nav = host.Context.Services.GetRequiredService<NavigationManager>();
        nav.NavigateTo("library?by=kind&of=Sonatas");

        var kind = host.Context.Render<Pages.Library>();

        kind.WaitForAssertion(() => Assert.Single(kind.FindAll(".lib-main .col-work")));
        Assert.Equal("1 work by 1 composer · 2 pieces", kind.Find(".lib-main-head .col-sub").TextContent);
        Assert.StartsWith("library?by=composer&of=Ludwig%20van%20Beethoven&kind=Sonatas&work=w%3Asonata%20no.%2014", kind.Find(".lib-main .col-work").GetAttribute("href"), StringComparison.Ordinal);
        Assert.Equal("Sonatas", kind.Find(".lib-list .col-item[aria-current]").QuerySelector("span")!.TextContent);

        nav.NavigateTo("library?q=sonata%2014%20allegretto");
        var found = host.Context.Render<Pages.Library>();

        found.WaitForAssertion(() => Assert.Equal(["Sonata No. 'quasi una fantasia' ('Moonlight'), Op. 27/2: II. Allegretto"], found.FindAll(".piece-title").Select(t => t.TextContent)));
        Assert.Equal("Found for “sonata 14 allegretto”", found.Find(".col-head").TextContent);
        Assert.Equal("Sonata No. 14 “quasi una fantasia” “Moonlight”, Op. 27 No. 2 · 4 bars", found.Find(".mov-name small").TextContent);
    }

    // The owner, 17 November: "when you star it, that adds to your repertoire", and the repertoire has collections under names
    // of their own. A star's first tap keeps the piece; a tap on one kept opens where it's kept.
    [Fact]
    public async Task AStarKeepsAPieceInTheRepertoireAndASecondTapPutsItInACollectionOrTakesItOut()
    {
        await using var host = new ComponentHost();
        await AddFew(host);
        var book = host.Context.Services.GetRequiredService<RepertoireBook>();
        host.Context.Services.GetRequiredService<NavigationManager>().NavigateTo("library?by=composer&of=Scott%20Joplin");
        var view = host.Context.Render<Pages.Library>();
        view.WaitForAssertion(() => Assert.Equal("false", view.Find(".lb-star").GetAttribute("aria-pressed")));

        await view.InvokeAsync(() => view.Find(".lb-star").ClickAsync(new Microsoft.AspNetCore.Components.Web.MouseEventArgs()));

        view.WaitForAssertion(() => Assert.Equal("true", view.Find(".lb-star").GetAttribute("aria-pressed")));
        Assert.Equal("“Maple Leaf Rag” is in your repertoire.", view.Find(".control-note").TextContent);
        Assert.True(book.Current.Has("maple"));
        Assert.Empty(view.FindAll(".lb-menu"));

        // The second tap: its collections, and a new one made with the piece in it.
        await view.InvokeAsync(() => view.Find(".lb-star").ClickAsync(new Microsoft.AspNetCore.Components.Web.MouseEventArgs()));
        Assert.Equal(["Repertoire", "New collection…"], view.FindAll(".lb-menu > button").Select(b => b.TextContent));
        await view.InvokeAsync(() => view.FindAll(".lb-menu > button").Last().ClickAsync(new Microsoft.AspNetCore.Components.Web.MouseEventArgs()));
        await view.InvokeAsync(() => view.Find(".lb-menu-new input").InputAsync(new ChangeEventArgs { Value = "Learning pieces 5" }));
        await view.InvokeAsync(() => view.Find(".lb-menu-new button").ClickAsync(new Microsoft.AspNetCore.Components.Web.MouseEventArgs()));

        view.WaitForAssertion(() => Assert.Equal(["Repertoire", "Learning pieces 5", "New collection…"], view.FindAll(".lb-menu > button").Select(b => b.TextContent)));
        Assert.Equal(["true", "true"], view.FindAll(".lb-menu > button[role=menuitemcheckbox]").Select(b => b.GetAttribute("aria-checked")));
        Assert.Equal(["maple"], book.Current.Collections.Single().Pieces);
        // It's kept in Prima's data: another screen reads the same.
        var read = new RepertoireBook(host.Store);
        await read.LoadAsync();
        Assert.Equal("Learning pieces 5", read.Current.Collections.Single().Name);

        // Out of the repertoire is out of its collections too.
        await view.InvokeAsync(() => view.FindAll(".lb-menu > button").First().ClickAsync(new Microsoft.AspNetCore.Components.Web.MouseEventArgs()));

        view.WaitForAssertion(() => Assert.Equal("false", view.Find(".lb-star").GetAttribute("aria-pressed")));
        Assert.Empty(view.FindAll(".lb-menu"));
        Assert.False(book.Current.Has("maple"));
        Assert.Empty(book.Current.Collections.Single().Pieces);
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

    // The owner's rule 27, decided 15 November (the review of main at a7ab94d, part 5): the Library's search folds accents, so
    // "Dvorak" finds Dvořák, on the home and on All composers.
    [Fact]
    public async Task TheLibrarysSearchFoldsAccentsSoDvorakFindsDvorak()
    {
        await using var host = new ComponentHost();
        await AddFew(host);
        await Add(host, "humoresque", "Antonín Dvořák", "Humoresque, Op. 101 No. 7", "Humoresques");
        var view = host.Context.Render<Pages.Library>();
        view.WaitForAssertion(() => Assert.Equal(4, view.FindAll(".lib-list .col-item[href*='by=composer']").Count));

        await view.InvokeAsync(() => view.Find("input[type=search]").InputAsync(new ChangeEventArgs { Value = "Dvorak" }));

        view.WaitForAssertion(() => Assert.Equal(["Humoresque, Op. 101 No. 7"], view.FindAll(".piece-title").Select(t => t.TextContent)), TimeSpan.FromSeconds(3));
        // The composer too, as a way to all of theirs.
        Assert.Equal(["Dvořák, Antonín 1"], Rows(view, ".lib-main .col-item"));

        // And the list's own Find a composer.
        host.Context.Services.GetRequiredService<NavigationManager>().NavigateTo("library?by=composers");
        var composers = host.Context.Render<Pages.Library>();
        composers.WaitForAssertion(() => Assert.Equal(4, composers.FindAll(".lib-list .col-item[href*='by=composer']").Count));
        await composers.InvokeAsync(() => composers.Find(".col-find").InputAsync(new ChangeEventArgs { Value = "dvorak" }));

        composers.WaitForAssertion(() => Assert.Equal(["Dvořák, Antonín 1"], Rows(composers, ".lib-list .col-item[href*='by=composer']")), TimeSpan.FromSeconds(3));
    }

    // The owner, 17 November: "The biggest part about yours is the import file, which is just gone ... I have to go back for
    // whatever reason to import my own files." And "Yours" is "Uploaded pieces", "because they're not really yours, right?"
    [Fact]
    public async Task TheUploadedPiecesHaveImportFilesOnTheirListAndAreCalledUploadedPieces()
    {
        await using var host = new ComponentHost();
        await AddFew(host);
        await Add(host, "import-numb", "", "Numb  Piano Solo Arrangement", null, origin: "import");
        var nav = host.Context.Services.GetRequiredService<NavigationManager>();
        nav.NavigateTo("library");
        var home = host.Context.Render<Pages.Library>();
        // They head the Library's list, and with some uploaded they're what the Library opens on.
        home.WaitForAssertion(() => Assert.Equal("Uploaded pieces 1", Rows(home).First()));
        Assert.Equal("5 pieces · 1 uploaded", home.Find(".page-kicker").TextContent);
        Assert.Equal("Uploaded pieces", home.Find(".col-head").TextContent);
        Assert.DoesNotContain("Yours", home.Markup, StringComparison.OrdinalIgnoreCase);

        nav.NavigateTo("library?by=mine");
        var uploaded = host.Context.Render<Pages.Library>();

        uploaded.WaitForAssertion(() => Assert.Single(uploaded.FindAll(".piece-item")));
        Assert.Equal("Uploaded pieces", uploaded.Find(".col-head").TextContent);
        Assert.Equal("1 piece · the latest first", uploaded.Find(".lib-main-head .col-sub").TextContent);
        Assert.Equal(["Import files"], uploaded.FindAll(".band-tools .mu-btn").Select(b => b.TextContent));
        Assert.Equal(["Latest", "A–Z", "By artist"], uploaded.FindAll(".band-tools .mu-toggle").Select(b => b.TextContent));
        Assert.Equal("Notes", uploaded.Find(".piece-tag").TextContent);
        Assert.DoesNotContain("Yours", uploaded.Markup, StringComparison.OrdinalIgnoreCase);

        // It imports from here as from the Library's head (the test's tablet has no files to pick).
        await uploaded.InvokeAsync(() => uploaded.Find(".band-tools .mu-btn").ClickAsync(new Microsoft.AspNetCore.Components.Web.MouseEventArgs()));

        uploaded.WaitForAssertion(() => Assert.Equal("No file chosen.", uploaded.Find(".control-note").TextContent));
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
