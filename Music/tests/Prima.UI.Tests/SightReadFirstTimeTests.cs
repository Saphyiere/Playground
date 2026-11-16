using Bunit;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.JSInterop;
using Playground.AI.Assist;
using Prima.Core.Keyboard;
using Prima.Core.Timing;
using Prima.UI.Host;
using Prima.UI.Pages;
using Prima.UI.Storage;

namespace Prima.UI.Tests;

/// <summary>
/// From the owner's report of 15 November: on a brand-new Prima, Sight-read's first passage showed as usual, and then Start did
/// nothing (no count-in, no click), with no way out but leaving the screen. Fails on main at a7ab94d.
/// </summary>
public class SightReadFirstTimeTests
{
    // SightRead.razor:407-417 (Run) sets the page to "running" before it asks the play bar to start, and the play bar can decline
    // without a word: with no piano plugged in, PlayAlong.razor:429-433 only says so for four seconds. The look's end starts the
    // run by itself (SightRead.razor:620), so a first passage looked at for longer than the look, before the piano is in, leaves
    // the page running nothing. From then on Start goes to Run(), which returns at once because the page isn't looking any more:
    // the tap does nothing, and the key goes when the page next draws.
    [Fact]
    public async Task AfterTheLookRunsOutWithNoPianoStartStillStartsOnceItsPluggedIn()
    {
        await using var host = new ReadingHost();
        host.Context.Services.GetRequiredService<PrimaSettings>().LookSeconds = 1;
        var view = host.Context.Render<SightRead>();
        await view.InvokeAsync(() => view.WaitForElement(".control-row-play .mu-primary", TimeSpan.FromSeconds(10)).ClickAsync(new MouseEventArgs()));
        view.WaitForAssertion(() => Assert.NotNull(Key(view, "Start")), TimeSpan.FromSeconds(10));

        // The look runs out, with nothing to play on.
        await Task.Delay(2000, Xunit.TestContext.Current.CancellationToken);
        host.Keyboard.Plug();
        view.WaitForAssertion(() => Assert.NotNull(Key(view, "Start")), TimeSpan.FromSeconds(3));

        await view.InvokeAsync(() => Key(view, "Start")!.ClickAsync(new MouseEventArgs()));

        // The run is on: its count-in, with Stop to end it.
        view.WaitForAssertion(() => Assert.NotNull(Key(view, "Stop")), TimeSpan.FromSeconds(3));
    }

    private static AngleSharp.Dom.IElement? Key(IRenderedComponent<SightRead> view, string words) => view.FindAll("button").FirstOrDefault(b => b.TextContent.Trim() == words);

    private sealed class ReadingHost : IAsyncDisposable
    {
        private readonly string database = Path.Combine(Path.GetTempPath(), $"prima-sight-read-{Guid.NewGuid():N}.db");

        public BunitContext Context { get; } = new();

        public Piano Keyboard { get; } = new();

        public PrimaStore Store { get; }

        public ReadingHost()
        {
            Context.Services.AddSingleton<IJSRuntime>(new BrowserCalls());
            Context.Services.AddSingleton<IKeyboard>(Keyboard);
            Context.Services.AddSingleton<IClaude>(new FakeClaude());
            Context.Services.AddPrima(database);
            Store = Context.Services.GetRequiredService<PrimaStore>();
        }

        public async ValueTask DisposeAsync()
        {
            await Context.DisposeAsync();
            await Store.GetSettingsAsync();
            using var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = database, ForeignKeys = true }.ToString());
            SqliteConnection.ClearPool(connection);
            File.Delete(database);
        }
    }

    // A piano that isn't plugged in until it is.
    private sealed class Piano : IKeyboard
    {
        public string? Name { get; private set; }

        public bool CanSend => false;

        public SensingJitter Sensing { get; } = new();

        public event Action<MidiEvent>? Received { add { } remove { } }

        public event Action? Changed;

        public void Send(byte[] message, long atNs)
        {
        }

        public void Silence()
        {
        }

        public void Plug()
        {
            Name = "P-225B";
            Changed?.Invoke();
        }
    }
}
