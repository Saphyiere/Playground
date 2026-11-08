using Playground.AI.Assist;

namespace AdaptiveWorkout.Phone;

/// <summary>
/// The Claude key in the phone's secure storage: encrypted with a key held in the Android Keystore, never in the app's data file, its
/// settings or the web view, and left out of Android's backups (Platforms/Android/Resources/xml).
/// </summary>
public sealed class SecureKeyStore : IAssistKeyStore
{
    private const string Name = "claude-api-key";

    public async Task<string?> GetAsync()
    {
        try
        {
            return await SecureStorage.Default.GetAsync(Name);
        }
        catch (Exception)
        {
            // Restored from a backup this phone can't decrypt: the key has to be pushed again.
            SecureStorage.Default.Remove(Name);
            return null;
        }
    }

    public Task SetAsync(string key) => SecureStorage.Default.SetAsync(Name, key);

    public Task RemoveAsync() => Task.FromResult(SecureStorage.Default.Remove(Name));
}
