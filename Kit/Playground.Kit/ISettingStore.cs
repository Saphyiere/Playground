namespace Playground.Kit;

/// <summary>Where an app keeps its settings, as text by key, so the parts it shares keep theirs there too, like the theme in use.</summary>
public interface ISettingStore
{
    /// <summary>Every saved setting, by key.</summary>
    Task<Dictionary<string, string>> GetSettingsAsync();

    /// <summary>Saves a setting; an empty one is removed.</summary>
    Task SetSettingAsync(string key, string? value);
}
