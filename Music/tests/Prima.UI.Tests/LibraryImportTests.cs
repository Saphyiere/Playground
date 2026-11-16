using System.Text;
using Microsoft.JSInterop;
using Prima.UI.Host;
using Prima.UI.Library;

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
