using System.Diagnostics;
using Playground.Kit;

namespace Prima.UI.Looks;

/// <summary>
/// The look in use, kept with the app's other settings. The page follows it through MainLayout; a host overrides
/// <see cref="ApplyToShell"/> to dress its own parts to match, like the tablet's status bar icons.
/// </summary>
public class LookSetting(ISettingStore store)
{
    private const string Key = "Look";
    private Task? loading;

    public Look Current { get; private set; } = Look.Studio;

    /// <summary>Raised when another look is chosen.</summary>
    public event Action? Changed;

    /// <summary>Reads the saved look; later calls wait on the same read.</summary>
    public Task LoadAsync() => loading ??= LoadSavedAsync();

    public void Choose(Look look)
    {
        if (look == Current) return;

        Current = look;
        store.SetSettingAsync(Key, look.Id).ContinueWith(t => Debug.WriteLine($"Prima: saving the look failed: {t.Exception}"), TaskContinuationOptions.OnlyOnFaulted);
        ApplyToShell(look);
        Changed?.Invoke();
    }

    /// <summary>Dresses the host's own parts (around the page, which follows data-theme) for the look. Called on any thread.</summary>
    protected virtual void ApplyToShell(Look look)
    {
    }

    private async Task LoadSavedAsync()
    {
        Current = Look.FromId((await store.GetSettingsAsync()).GetValueOrDefault(Key));
        ApplyToShell(Current);
    }
}
