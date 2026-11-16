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
        view.WaitForAssertion(() => Assert.Contains("Everything 3", view.FindAll(".shelf-chip").Select(a => a.TextContent.Trim())));
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
