namespace Prima.UI.Components;

/// <summary>One of Settings' tabs: its name, and its id in the address (<c>settings?tab=keyboard</c>).</summary>
public sealed record SettingsTab(string Id, string Name);

/// <summary>
/// Settings' tabs and its search. Every <see cref="Setting"/> on the page is kept here with the words it's found by: its section's
/// name and what the section says first, its own name, its choices and what it says. A setting shows when its tab is open, or,
/// while something's looked for, when it has every word looked for, from whichever tab; a section shows when any of its settings
/// do. Case doesn't matter, nor ’ for ', and # finds ♯.
/// </summary>
public sealed class SettingsFinder
{
    /// <summary>The tabs, in order. The first opens when the address names none, or one there isn't.</summary>
    public static readonly IReadOnlyList<SettingsTab> Tabs =
    [
        new("practice", "Practice"),
        new("keyboard", "Keyboard & sound"),
        new("look", "Look & music"),
        new("claude", "Claude"),
        new("app", "App & data"),
    ];

    private readonly List<Setting> settings = [];
    private string query = "";
    private string[] words = [];

    /// <summary>The tab open: its id.</summary>
    public string Tab { get; set; } = Tabs[0].Id;

    /// <summary>What's typed in the search, as typed.</summary>
    public string Query
    {
        get => query;
        set => (query, words) = (value ?? "", WordsOf(value));
    }

    /// <summary>Something's looked for: the tabs give way to what's found.</summary>
    public bool Searching => words.Length > 0;

    /// <summary>Anything at all has every word looked for.</summary>
    public bool FoundAny => settings.Any(Found);

    /// <summary>The tab with this id, or the first.</summary>
    public static SettingsTab TabFor(string? id) => Tabs.FirstOrDefault(t => t.Id == id) ?? Tabs[0];

    public bool Shows(Setting setting) => Searching ? Found(setting) : setting.Section.Tab == Tab;

    public bool Shows(SettingsSection section) => Searching ? settings.Any(s => s.Section == section && Found(s)) : section.Tab == Tab;

    internal void Add(Setting setting) => settings.Add(setting);

    internal void Remove(Setting setting) => settings.Remove(setting);

    private bool Found(Setting setting)
    {
        var text = Fold(setting.Text);
        return words.All(text.Contains);
    }

    // Each word typed: a lone & or › isn't one.
    private static string[] WordsOf(string? query) =>
        [.. Fold(query ?? "").Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Where(w => w.Any(c => char.IsLetterOrDigit(c) || c == '♯'))];

    private static string Fold(string text) => text.ToLowerInvariant().Replace('’', '\'').Replace('‘', '\'').Replace('#', '♯');
}
