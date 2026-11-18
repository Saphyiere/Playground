using System.IO.Compression;
using System.Text;
using System.Text.Json;
using Bunit;
using Microsoft.JSInterop;
using Prima.Core.Library;
using Prima.UI.Host;
using Prima.UI.Library;
using Prima.UI.Storage;

namespace Prima.UI.Tests;

public class LibraryImportTests
{
    // Two bars of piano with a drum part under them, as Verovio writes one from MusicXML: the drum's notes have no pitch, only a place
    // on their staff (loc).
    private const string WithADrumPart = """
        <mei xmlns="http://www.music-encoding.org/ns/mei"><music><body><mdiv><score>
        <scoreDef meter.count="2" meter.unit="4" key.sig="0"><staffGrp>
        <staffGrp><staffDef n="1" lines="5" clef.shape="G" clef.line="2"/><staffDef n="2" lines="5" clef.shape="F" clef.line="4"/></staffGrp>
        <staffDef n="3" lines="5" clef.shape="perc"/>
        </staffGrp></scoreDef>
        <section>
        <measure n="1">
        <staff n="1"><layer n="1"><note xml:id="a" pname="e" oct="5" dur="4"/><note xml:id="b" pname="d" oct="5" dur="4"/></layer></staff>
        <staff n="2"><layer n="1"><note xml:id="c" pname="c" oct="3" dur="2"/></layer></staff>
        <staff n="3"><layer n="1"><note xml:id="d" loc="5" dur="4"/><chord xml:id="e" dur="4"><note xml:id="e1" loc="1"/><note xml:id="e2" loc="9" head.shape="x"/></chord></layer></staff>
        </measure>
        <measure n="2">
        <staff n="1"><layer n="1"><note xml:id="f" pname="c" oct="5" dur="2"/></layer></staff>
        <staff n="2"><layer n="1"><note xml:id="g" pname="c" oct="3" dur="2"/></layer></staff>
        <staff n="3"><layer n="1"><note xml:id="h" loc="5" dur="2"/></layer></staff>
        </measure>
        </section></score></mdiv></body></music></mei>
        """;

    // A time signature of four over nothing, which the reader can't make sense of (it divides by it).
    private const string OverNothing = """
        <mei xmlns="http://www.music-encoding.org/ns/mei"><music><body><mdiv><score>
        <scoreDef meter.count="4" meter.unit="0" key.sig="0"><staffGrp><staffDef n="1" lines="5" clef.shape="G" clef.line="2"/></staffGrp></scoreDef>
        <section><measure n="1"><staff n="1"><layer n="1"><note xml:id="a" pname="c" oct="5" dur="1"/></layer></staff></measure></section>
        </score></mdiv></body></music></mei>
        """;

    [Fact]
    public async Task AFileThatCantBeReadIsNamedAndTheOthersComeIn()
    {
        await using var host = new ComponentHost();
        var prep = new LibraryPrep(host.Store, new NoBundle(), new PassingVerovio());

        var (id, note) = await prep.ImportAsync([("Over nothing.mei", Encoding.UTF8.GetBytes(OverNothing)), ("With drums.mei", Encoding.UTF8.GetBytes(WithADrumPart))]);

        Assert.Null(id);
        Assert.Equal("1 piece imported. Over nothing.mei couldn't be read as music.", note);
        var piece = Assert.Single(await host.Store.GetPiecesAsync(), p => p.Imported);
        Assert.Equal(("With drums", 2), (piece.Title, piece.Bars));
    }

    [Fact]
    public async Task ACollectionBuiltAgainTakesOutTheCollectionsPiecesItNoLongerHasAndBringsTheRestUpToDate()
    {
        await using var host = new ComponentHost();
        var prep = new LibraryPrep(host.Store, new NoBundle(), new PassingVerovio());
        var own = await host.AddPiece();
        await host.Store.SavePieceAsync(new PieceRecord("bundled-minuet", "bundle", "Minuet", "Christian Petzold", null, "pdf", "minuet.pdf", null, 4, 4, null, null, "Mutopia", null,
            null, null, null, "A", null, null, null, DateTimeOffset.Now, null), null, []);
        await prep.ImportAsync([Zip("prima-collection-mutopia.zip", [Pdf("rebuilt-kept", "Melodie"), Pdf("rebuilt-wrong", "Mompou Variations on a Theme by")], null)]);
        await prep.EnsureAsync();
        // A run kept on the piece the collection built again no longer has.
        await host.Store.AddAttemptAsync(new AttemptRecord(0, DateTimeOffset.Now, DateOnly.FromDateTime(DateTime.Now), "piece", "in time", "rebuilt-wrong", 7, 72, null, null, "[]", null,
            JsonSerializer.Serialize(new { inTime = 0.9, right = 9, wrong = 1, missed = 0, from = 1, hands = "both" }), 0.9, false, null));

        // Built again: the piece left out, the other retitled and share-alike now, each zip naming the whole collection.
        var (_, note) = await prep.ImportAsync([Zip("prima-collection-mutopia.zip", [Pdf("rebuilt-kept", "Melodie, No. 1") with { Licence = "CC BY-SA 4.0", Tier = Tier.SA }],
            ["rebuilt-kept", "dcml-in-another-zip"])]);

        Assert.Equal("1 piece the collection no longer has was taken out. Titles and credits of 1 piece brought up to date. Everything in prima-collection-mutopia.zip is in the Library already.", note);
        var pieces = await host.Store.GetPiecesAsync();
        Assert.Equal(["bundled-minuet", "rebuilt-kept", own.Id], pieces.Select(p => p.Id).Order(StringComparer.Ordinal));
        var kept = Assert.Single(pieces, p => p.Id == "rebuilt-kept");
        Assert.Equal(("Melodie, No. 1", "CC BY-SA 4.0", "SA"), (kept.Title, kept.Licence, kept.Tier));
        // The piece that went takes its PDF with it, and leaves its run: its history stays, and the Library opens without it.
        Assert.False(File.Exists(Path.Combine(host.Store.FilesPath, "rebuilt-wrong.pdf")));
        Assert.True(File.Exists(Path.Combine(host.Store.FilesPath, "rebuilt-kept.pdf")));
        Assert.Single(await host.Store.GetPieceRunsAsync("rebuilt-wrong"));
        var view = host.Context.Render<Pages.Library>();
        view.WaitForAssertion(() => Assert.Equal("3 pieces · 1 uploaded", view.Find(".page-kicker").TextContent));
        File.Delete(Path.Combine(host.Store.FilesPath, "rebuilt-kept.pdf"));
    }

    [Fact]
    public async Task ACollectionThatDoesntNameTheWholeOfItselfTakesNothingOut()
    {
        await using var host = new ComponentHost();
        var prep = new LibraryPrep(host.Store, new NoBundle(), new PassingVerovio());
        await prep.ImportAsync([Zip("old.zip", [Pdf("older-one", "One"), Pdf("older-two", "Two")], null)]);
        await prep.EnsureAsync();

        // A zip from before collections named themselves whole: what it has comes up to date, nothing goes.
        var (_, note) = await prep.ImportAsync([Zip("old.zip", [Pdf("older-one", "One, retitled")], null)]);

        Assert.Equal("Titles and credits of 1 piece brought up to date. Everything in old.zip is in the Library already.", note);
        Assert.Equal(["One, retitled", "Two"], (await host.Store.GetPiecesAsync()).Select(p => p.Title).Order(StringComparer.Ordinal));
        foreach (var id in new[] { "older-one", "older-two" }) File.Delete(Path.Combine(host.Store.FilesPath, id + ".pdf"));
    }

    // The owner, 17 November: "If I remove something, it doesn't remember that." A collection's piece removed by hand came back
    // with its zip imported again. It stays out now, as one of Prima's own does, until the Library is asked to let it back in.
    [Fact]
    public async Task ACollectionsPieceRemovedByHandStaysOutWhenItsZipIsImportedAgainUntilItsLetBackIn()
    {
        await using var host = new ComponentHost();
        var prep = new LibraryPrep(host.Store, new NoBundle(), new PassingVerovio());
        var zip = Zip("prima-collection-pdmx.zip", [Pdf("kept-one", "Melodie"), Pdf("removed-one", "Diabelli Variations (WIP)")], null);
        await prep.ImportAsync([zip]);
        await prep.EnsureAsync();

        await host.Store.RemovePieceAsync("removed-one");
        var (_, note) = await prep.ImportAsync([zip]);

        Assert.Equal("1 piece you removed stays out. Everything else in prima-collection-pdmx.zip is in the Library already.", note);
        Assert.Equal(["kept-one"], (await host.Store.GetPiecesAsync()).Select(p => p.Id));
        var home = host.Context.Render<Pages.Library>();
        home.WaitForAssertion(() => Assert.Equal("1 of your collection's pieces is out of the Library: you removed it, and importing its collection again leaves it out.",
            home.FindAll(".library-put-back .section-note").Single().TextContent));

        await home.InvokeAsync(() => home.FindAll(".library-put-back button").Single(b => b.TextContent == "Let it back in").ClickAsync(new Microsoft.AspNetCore.Components.Web.MouseEventArgs()));

        home.WaitForAssertion(() => Assert.Empty(home.FindAll(".library-put-back")));
        Assert.Equal("It comes back the next time its collection is imported.", home.Find(".control-note").TextContent);
        await prep.ImportAsync([zip]);
        await prep.EnsureAsync();
        Assert.Equal(["kept-one", "removed-one"], (await host.Store.GetPiecesAsync()).Select(p => p.Id).Order(StringComparer.Ordinal));
        foreach (var id in new[] { "kept-one", "removed-one" }) File.Delete(Path.Combine(host.Store.FilesPath, id + ".pdf"));
    }

    // About this piece said "Imported 17 November from import-92d6….pdf", the name Prima keeps a PDF under: it says the file's own
    // name now, and for a PDF imported before that name was kept, only when.
    [Fact]
    public async Task AboutNamesTheFileAsItWasImported()
    {
        await using var host = new ComponentHost();
        var prep = new LibraryPrep(host.Store, new NoBundle(), new PassingVerovio());
        var (id, _) = await prep.ImportAsync([("Numb  Linkin Park Piano Solo Arrangement.pdf", "%PDF-1.4 a score"u8.ToArray())]);
        var day = DateTimeOffset.Now.ToString("d MMMM", System.Globalization.CultureInfo.InvariantCulture);

        var page = host.Context.Render<Pages.Piece>(p => p.Add(c => c.Id, id!));

        page.WaitForAssertion(() => Assert.Contains($"Imported {day} from Numb  Linkin Park Piano Solo Arrangement.pdf.", page.FindAll(".piece-about li").Select(l => l.TextContent)));
        Assert.StartsWith("import-", (await host.Store.GetPieceAsync(id!))!.File, StringComparison.Ordinal);

        await host.Store.SavePieceAsync((await host.Store.GetPieceAsync(id!))! with { Source = null }, null, []);
        var before = host.Context.Render<Pages.Piece>(p => p.Add(c => c.Id, id!));
        before.WaitForAssertion(() => Assert.Contains($"Imported {day}.", before.FindAll(".piece-about li").Select(l => l.TextContent)));
    }

    // A MIDI file of two bars of crotchets in 4/4, C to C.
    private static byte[] TwoBars()
    {
        List<byte> track = [0, 0xFF, 0x58, 4, 4, 2, 24, 8];
        foreach (var key in new[] { 60, 62, 64, 65, 67, 69, 71, 72 }) track.AddRange([0, 0x90, (byte)key, 80, 0x83, 0x60, 0x80, (byte)key, 0]);
        track.AddRange([0, 0xFF, 0x2F, 0]);
        return [.. "MThd"u8, 0, 0, 0, 6, 0, 0, 0, 1, 1, 0xE0, .. "MTrk"u8, 0, 0, 0, (byte)track.Count, .. track];
    }

    // The owner's bought music comes as a PDF with its MIDI. Picked together they were two pieces of one name: the pages
    // without notes to play along with, and the notes without the pages. They're one piece now, and its title is the music's,
    // without what the shop adds to the file's name.
    [Fact]
    public async Task APdfPickedWithItsMidiIsOnePieceWithItsPagesAndItsNotes()
    {
        await using var host = new ComponentHost();
        var prep = new LibraryPrep(host.Store, new NoBundle(), new PassingVerovio());

        var (id, note) = await prep.ImportAsync([("Hallelujah - C Major - MN0258239.mid", TwoBars()), ("Hallelujah - C Major - MN0258239.pdf", "%PDF-1.4 a score"u8.ToArray())]);

        var piece = Assert.Single(await host.Store.GetPiecesAsync());
        Assert.Equal((piece.Id, null), (id, note));
        Assert.Equal(("Hallelujah", "pdf", true, true, 2), (piece.Title, piece.Format, piece.HasNotes, piece.PlaysAlong, piece.Bars));
        Assert.Equal((piece.Id + ".pdf", piece.Id + ".mid", "Hallelujah - C Major - MN0258239.pdf"), (piece.File, piece.Midi, piece.Source));
        Assert.NotNull(piece.Level);
        Assert.Contains("<measure", await host.Store.GetMeiAsync(piece.Id), StringComparison.Ordinal);
        Assert.True(File.Exists(Path.Combine(host.Store.FilesPath, piece.Id + ".mid")));
    }

    [Fact]
    public async Task AMidiByItselfIsAPieceAndAPdfWhoseMidiIsNoMidiKeepsItsPagesOnly()
    {
        await using var host = new ComponentHost();
        var prep = new LibraryPrep(host.Store, new NoBundle(), new PassingVerovio());

        var (_, note) = await prep.ImportAsync([("A_Bird_Story - Mud Dash.mid", TwoBars()), ("Broken.pdf", "%PDF-1.4 a score"u8.ToArray()), ("Broken.mid", [1, 2, 3])]);

        Assert.Equal("2 pieces imported. Broken's MIDI couldn't be read as music: it came in with its pages only.", note);
        var pieces = (await host.Store.GetPiecesAsync()).OrderBy(p => p.Title).ToList();
        Assert.Equal([("A Bird Story - Mud Dash", "mei", true), ("Broken", "pdf", false)], pieces.Select(p => (p.Title, p.Format, p.PlaysAlong)));
        Assert.Null(pieces[1].Midi);
        Assert.False(File.Exists(Path.Combine(host.Store.FilesPath, pieces[1].Id + ".mid")));
    }

    [Theory]
    [InlineData("Hallelujah - C Major - MN0258239.pdf", "Hallelujah")]
    [InlineData("Clair de Lune - Db Major - MN0105623.pdf", "Clair de Lune")]
    [InlineData("Numb  Linkin Park Piano Solo Arrangement.pdf", "Numb Linkin Park Piano Solo Arrangement")]
    [InlineData("Interstellar 2 - Cornfield Chase.mid", "Interstellar 2 - Cornfield Chase")]
    [InlineData("river_flows_in_you.musicxml", "river flows in you")]
    public void APiecesTitleStartsFromItsFilesNameWithoutWhatAShopAdds(string file, string title) => Assert.Equal(title, LibraryPrep.TitleFrom(file));

    // A piece already in the Library is given what it lacks: a PDF its notes from the MIDI it came with, a MIDI its pages.
    [Fact]
    public async Task AnUploadedPieceIsGivenItsNotesOrItsPagesAfterwards()
    {
        await using var host = new ComponentHost();
        var prep = new LibraryPrep(host.Store, new NoBundle(), new PassingVerovio());
        var (pdfId, _) = await prep.ImportAsync([("Numb.pdf", "%PDF-1.4 a score"u8.ToArray())]);
        var (midiId, _) = await prep.ImportAsync([("Sweden.mid", TwoBars())]);
        await host.Store.SetPieceAsync(midiId!, tempo: 96);

        Assert.Equal("words.txt: its notes come from a MIDI, MusicXML, MEI or ABC file.", await prep.GiveNotesAsync((await host.Store.GetPieceAsync(pdfId!))!, "words.txt", [1]));
        Assert.Equal("Numb.mid couldn't be read as music.", await prep.GiveNotesAsync((await host.Store.GetPieceAsync(pdfId!))!, "Numb.mid", [1, 2, 3]));
        Assert.False((await host.Store.GetPieceAsync(pdfId!))!.PlaysAlong);

        Assert.Null(await prep.GiveNotesAsync((await host.Store.GetPieceAsync(pdfId!))!, "Numb.mid", TwoBars()));

        var pdf = (await host.Store.GetPieceAsync(pdfId!))!;
        Assert.Equal(("Numb", "pdf", pdfId + ".pdf", pdfId + ".mid", true, 2), (pdf.Title, pdf.Format, pdf.File, pdf.Midi, pdf.PlaysAlong, pdf.Bars));

        Assert.Equal("Sweden.txt isn't a PDF.", await prep.GivePagesAsync((await host.Store.GetPieceAsync(midiId!))!, "Sweden.txt", [1, 2, 3, 4, 5]));
        Assert.Null(await prep.GivePagesAsync((await host.Store.GetPieceAsync(midiId!))!, "Sweden.pdf", "%PDF-1.4 its print"u8.ToArray()));

        // Its notes, its excerpts' level and the tempo set for it stay; the print is beside them.
        var midi = (await host.Store.GetPieceAsync(midiId!))!;
        Assert.Equal(("Sweden", "pdf", midiId + ".pdf", true, 2, 96.0), (midi.Title, midi.Format, midi.File, midi.PlaysAlong, midi.Bars, midi.Tempo));
        Assert.Equal("%PDF-1.4 its print"u8.ToArray(), await File.ReadAllBytesAsync(Path.Combine(host.Store.FilesPath, midiId + ".pdf"), Xunit.TestContext.Current.CancellationToken));
    }

    // A PDF of the local collection as the Library tool lists it.
    private static BundledPiece Pdf(string id, string title) => new(id, title, "Robert Schumann", "Op. 68", "pdf", $"pieces/{id}.pdf", null, "", "Mutopia", "",
        "Public domain", "", "Typeset for the Mutopia Project; Public domain.", Tier.A, null, 7);

    // One of a collection's zips as the Library tool writes it: its manifest, naming the whole collection when it's given, and its
    // pieces' PDFs.
    private static (string Name, byte[] Data) Zip(string name, IReadOnlyList<BundledPiece> pieces, IReadOnlyList<string>? whole)
    {
        using var bytes = new MemoryStream();
        using (var zip = new ZipArchive(bytes, ZipArchiveMode.Create, leaveOpen: true))
        {
            using (var writer = new StreamWriter(zip.CreateEntry("manifest.json").Open())) writer.Write(new LibraryManifest(1, "2026-11-15", pieces, whole).Write());
            foreach (var piece in pieces)
            {
                using var stream = zip.CreateEntry(piece.File).Open();
                stream.Write("%PDF-1.4 "u8);
            }
        }
        return (name, bytes.ToArray());
    }

    // Verovio, for MEI: each file's MEI back as it came.
    private sealed class PassingVerovio : IJSRuntime, IJSObjectReference
    {
        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args) => InvokeAsync<TValue>(identifier, default, args);

        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, CancellationToken cancellationToken, object?[]? args) => identifier switch
        {
            "import" => ValueTask.FromResult((TValue)(object)this),
            "toMei" => ValueTask.FromResult((TValue)(object)Encoding.UTF8.GetString((byte[])args![0]!)),
            _ => throw new InvalidOperationException($"Not Verovio's: {identifier}"),
        };

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
