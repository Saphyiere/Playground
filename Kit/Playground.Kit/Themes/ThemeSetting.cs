using System.Diagnostics;

namespace Playground.Kit.Themes;

/// <summary>
/// The theme in use, kept with the app's other settings. The page follows it through MainLayout; a host overrides
/// <see cref="ApplyToShell"/> to dress its own parts to match, like the phone's status bar icons.
/// </summary>
public class ThemeSetting(ISettingStore store)
{
    private const string Key = "Theme";
    private Task? loading;

    public Theme Current { get; private set; } = Theme.Logbook;

    /// <summary>Raised when another theme is chosen.</summary>
    public event Action? Changed;

    /// <summary>Reads the saved theme; later calls wait on the same read.</summary>
    public Task LoadAsync() => loading ??= LoadSavedAsync();

    /// <summary>Reads the saved theme again, as when a restore has put other settings in place, and switches to it.</summary>
    public async Task ReloadAsync()
    {
        var before = Current;
        await (loading = LoadSavedAsync());
        if (Current != before) Changed?.Invoke();
    }

    public void Choose(Theme theme)
    {
        if (theme == Current) return;

        Current = theme;
        store.SetSettingAsync(Key, theme.Id).ContinueWith(t => Debug.WriteLine($"Playground.Kit: saving the theme failed: {t.Exception}"), TaskContinuationOptions.OnlyOnFaulted);
        ApplyToShell(theme);
        Changed?.Invoke();
    }

    /// <summary>Dresses the host's own parts (around the page, which follows data-theme) for the theme. Called on any thread.</summary>
    protected virtual void ApplyToShell(Theme theme)
    {
    }

    private async Task LoadSavedAsync()
    {
        Current = Theme.FromId((await store.GetSettingsAsync()).GetValueOrDefault(Key));
        ApplyToShell(Current);
    }
}
