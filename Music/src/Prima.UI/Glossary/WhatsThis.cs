using Microsoft.JSInterop;

namespace Prima.UI.Glossary;

/// <summary>
/// A card What's this shows beside what it's about: a name, a line or two, and where the thing is on the screen (in px, for the
/// card to stand beside it). <see cref="EntryId"/> is its glossary entry, for More, which opens it in place; <see cref="Choices"/>
/// are what a tap could have meant, where several things are as near ("Which one?").
/// </summary>
public sealed record ExplainCard(string Name, IReadOnlyList<string> Lines, double Left, double Top, double Right, double Bottom)
{
    public string? EntryId { get; init; }

    public IReadOnlyList<(string Name, Func<Task> Pick)>? Choices { get; init; }
}

/// <summary>
/// What's this, the same on every screen: the ? key in the head (on the bar in full screen) turns it on and off. While it's on,
/// what can be explained is marked, and a tap says what it is instead of pressing it (explain.js); the music says what's printed
/// on it, and a screen puts its own lines in the panel (a piece's key and its notes' names). One for the app; leaving a screen
/// turns it off. The owner, 28 November 2026: "it needs to like be able to work anywhere".
/// </summary>
public sealed class WhatsThis(IJSRuntime js) : IAsyncDisposable
{
    private IJSObjectReference? module;
    private DotNetObjectReference<WhatsThis>? self;

    public bool On { get; private set; }

    /// <summary>The card showing, if one is.</summary>
    public ExplainCard? Card { get; private set; }

    /// <summary>A glossary entry opened in place from a card's More, if one is.</summary>
    public GlossaryEntry? Entry { get; private set; }

    /// <summary>When it goes on or off, or a card or an entry comes or goes.</summary>
    public event Action? Changed;

    public Task ToggleAsync() => SetAsync(!On);

    public async Task SetAsync(bool on)
    {
        if (On == on) return;

        (On, Card, Entry) = (on, null, null);
        Changed?.Invoke();
        try
        {
            module ??= await js.InvokeAsync<IJSObjectReference>("import", "./_content/Prima.UI/explain.js");
            await module.InvokeVoidAsync(on ? "on" : "off", on ? self ??= DotNetObjectReference.Create(this) : null);
        }
        catch (Exception e) when (e is JSDisconnectedException or JSException or InvalidOperationException or TaskCanceledException)
        {
        }
    }

    /// <summary>A card beside what it's about, or none.</summary>
    public void Show(ExplainCard? card)
    {
        (Card, Entry) = (card, null);
        Changed?.Invoke();
    }

    /// <summary>A glossary entry in place of the card, or back from it.</summary>
    public void Open(GlossaryEntry? entry)
    {
        (Entry, Card) = (entry, entry is null ? Card : null);
        Changed?.Invoke();
    }

    /// <summary>Places a card the page has drawn beside what it's about.</summary>
    public async Task PlaceAsync(Microsoft.AspNetCore.Components.ElementReference card, ExplainCard shown)
    {
        try
        {
            if (module is not null) await module.InvokeVoidAsync("place", card, shown.Left, shown.Top, shown.Right, shown.Bottom);
        }
        catch (Exception e) when (e is JSDisconnectedException or JSException)
        {
        }
    }

    /// <summary>From explain.js: a tap on something marked (its key in <see cref="Explanations"/>), or on something that isn't.</summary>
    [JSInvokable]
    public Task Tapped(string? key, double left, double top, double right, double bottom)
    {
        if (!On) return Task.CompletedTask;

        Show(key is not null && Explanations.Of(key) is { } said
            ? new ExplainCard(said.Name, said.Lines, left, top, right, bottom)
            : new ExplainCard("Nothing to say about that yet", ["Tap one of the things marked."], left, top, right, bottom));
        return Task.CompletedTask;
    }

    public async ValueTask DisposeAsync()
    {
        try
        {
            if (module is not null)
            {
                if (On) await module.InvokeVoidAsync("off", (object?)null);
                await module.DisposeAsync();
            }
        }
        catch (Exception e) when (e is JSDisconnectedException or JSException)
        {
        }
        self?.Dispose();
    }
}
