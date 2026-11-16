using System.Text.Json;
using Bunit;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.JSInterop;
using Prima.UI.Components;
using Prima.UI.Pages;

namespace Prima.UI.Tests;

public class SheetLyricsTests
{
    // A song on a grand staff, its tune's words and a second voice's under the upper staff, both numbered verse 1, as Verovio
    // reads them from a MusicXML file whose two voices each have lyrics.
    private const string Song = """
        <mei xmlns="http://www.music-encoding.org/ns/mei" meiversion="5.0"><music><body><mdiv><score><scoreDef meter.count="4" meter.unit="4"><staffGrp symbol="brace"><staffDef n="1" lines="5" clef.shape="G" clef.line="2"/><staffDef n="2" lines="5" clef.shape="F" clef.line="4"/></staffGrp></scoreDef><section>
        <measure n="1"><staff n="1"><layer n="1"><note xml:id="a" pname="e" oct="5" dur="2"><verse n="1"><syl>high</syl></verse></note><note xml:id="b" pname="d" oct="5" dur="2"><verse n="1"><syl>words</syl></verse></note></layer><layer n="2"><note xml:id="c" pname="g" oct="4" dur="1"><verse n="1"><syl>low</syl></verse></note></layer></staff><staff n="2"><layer n="1"><note xml:id="d" pname="c" oct="3" dur="1"/></layer></staff></measure>
        </section></score></mdiv></body></music></mei>
        """;

    // A browser that keeps what the sheet gives Verovio to engrave: the music, and the options with it.
    private sealed class Engraving : IJSRuntime, IJSObjectReference
    {
        public List<(string Mei, JsonElement Options)> Renders { get; } = [];

        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args) => InvokeAsync<TValue>(identifier, default, args);

        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, CancellationToken cancellationToken, object?[]? args)
        {
            if (identifier == "import") return ValueTask.FromResult((TValue)(object)this);
            if (identifier == "render") lock (Renders) Renders.Add(((string)args![1]!, JsonSerializer.SerializeToElement(args[2])));
            return ValueTask.FromResult(default(TValue)!);
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    [Fact]
    public async Task ASongsWordsGoToVerovioEachVoiceOnALineOfItsOwn()
    {
        var browser = new Engraving();
        await using var host = new ComponentHost(js: browser);

        var view = host.Context.Render<Sheet>(p => p.Add(c => c.Mei, Song));

        view.WaitForAssertion(() => Assert.Single(browser.Renders));
        var (mei, options) = browser.Renders[0];
        Assert.Contains("""<verse n="1"><syl>high</syl>""", mei);
        Assert.Contains("""<verse n="2"><syl>low</syl>""", mei);
        Assert.True(options.GetProperty("words").GetBoolean());
    }

    [Fact]
    public async Task WithLyricsOffASongsSheetIsTheMusicAlone()
    {
        var browser = new Engraving();
        await using var host = new ComponentHost(js: browser);
        host.Context.Services.GetRequiredService<PrimaSettings>().Lyrics = false;

        var view = host.Context.Render<Sheet>(p => p.Add(c => c.Mei, Song));

        view.WaitForAssertion(() => Assert.Single(browser.Renders));
        var (mei, options) = browser.Renders[0];
        Assert.DoesNotContain("<verse", mei);
        Assert.DoesNotContain("<syl", mei);
        Assert.Contains("""xml:id="c""", mei);
        Assert.False(options.GetProperty("words").GetBoolean());
    }

    [Fact]
    public async Task PrimasOwnNamesUnderTheNotesArePrintedAsWritten()
    {
        var browser = new Engraving();
        await using var host = new ComponentHost(js: browser);
        host.Context.Services.GetRequiredService<PrimaSettings>().Lyrics = false;

        var view = host.Context.Render<Sheet>(p => p.Add(c => c.Mei, Song).Add(c => c.Names, true));

        view.WaitForAssertion(() => Assert.Single(browser.Renders));
        Assert.Same(Song, browser.Renders[0].Mei);
        Assert.False(browser.Renders[0].Options.GetProperty("words").GetBoolean());
    }

    [Fact]
    public async Task TheLyricsSettingIsKept()
    {
        await using var host = new ComponentHost();
        var fresh = new PrimaSettings();
        await host.Store.LoadAsync(fresh);
        Assert.True(fresh.Lyrics);

        fresh.Lyrics = false;
        await host.Store.SaveAsync(fresh);
        var loaded = new PrimaSettings();
        await host.Store.LoadAsync(loaded);

        Assert.False(loaded.Lyrics);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ASongsOptionsTurnItsWordsOnAndOff(bool song)
    {
        await using var host = new ComponentHost();
        var piece = await host.AddPiece();
        if (song) await host.Store.SavePieceAsync(piece, Song, []);
        var settings = host.Context.Services.GetRequiredService<PrimaSettings>();

        var view = host.Context.Render<Piece>(p => p.Add(c => c.Id, piece.Id));
        view.WaitForElement(".piece-front-bar .mu-primary").Click();
        view.WaitForElement(".piece-tool[aria-label='Options']").Click();
        view.WaitForElement(".piece-options");

        var row = view.FindAll(".piece-options .control-row").FirstOrDefault(r => r.QuerySelector(".control-label")?.TextContent == "Lyrics");
        Assert.Equal(song, row is not null);
        if (row is null) return;

        // The song's sheet is drawn with its words first, and again without them once they're turned off, beside it.
        view.WaitForAssertion(() => Assert.Contains(host.Browser.Renders, r => r.Mei.Contains("<verse")));
        row.QuerySelectorAll("button")[1].Click();
        view.WaitForAssertion(() => Assert.Contains("The music alone", view.Find(".piece-options").TextContent));
        view.WaitForAssertion(() => Assert.DoesNotContain("<verse", host.Browser.Renders[^1].Mei));
        Assert.False(host.Browser.Renders[^1].Options.GetProperty("words").GetBoolean());
        Assert.False(settings.Lyrics);
        var saved = new PrimaSettings();
        await host.Store.LoadAsync(saved);
        Assert.False(saved.Lyrics);
    }
}
