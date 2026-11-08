using Playground.AI.Assist;

namespace AdaptiveWorkout.UI.Assist;

/// <summary>
/// Where the phone takes a Claude key in: a file a computer copies into the app's folder over USB (<c>adb push claude-key.txt
/// /sdcard/Android/data/com.saphyiere.adaptiveworkout/files/</c>), moved into the key store and deleted. The app looks for it when it
/// opens, and when Settings asks.
/// </summary>
public sealed class ClaudeKeyFile(string path)
{
    public string Path => path;

    /// <summary>
    /// What became of the last file found since the app opened, for Settings: taken in, or why not. A later look that finds no file
    /// leaves it, so a page drawn twice (the preview's) still says what happened.
    /// </summary>
    public KeyImport? Found { get; private set; }

    public async Task<KeyImport> ImportAsync(IAssistKeyStore keys)
    {
        KeyImport result;
        try
        {
            result = await AssistKey.ImportAsync(path, keys);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            result = new KeyImport(KeyImportResult.NotAKey, $"{AssistKey.FileName} couldn't be read: {e.Message}");
        }

        if (result.Result != KeyImportResult.NoFile) Found = result;
        return result;
    }
}

/// <summary>A Claude key kept in memory, for a host without secure storage (the preview): gone when it stops.</summary>
public sealed class MemoryKeyStore : IAssistKeyStore
{
    private string? key;

    public Task<string?> GetAsync() => Task.FromResult(key);

    public Task SetAsync(string value) => Task.FromResult(key = value);

    public Task RemoveAsync() => Task.FromResult(key = null);
}
