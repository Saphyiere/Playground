using System.Text.Json;

namespace Playground.AI.Assist;

/// <summary>
/// Where a device keeps its Claude API key. A host keeps it in the platform's secure storage (on Android, MAUI's
/// SecureStorage, encrypted with a key in the Android Keystore): never in the app's database, its settings or its backups,
/// and never handed to the web view.
/// </summary>
public interface IAssistKeyStore
{
    /// <summary>The key, or null when there's none.</summary>
    Task<string?> GetAsync();

    Task SetAsync(string key);

    Task RemoveAsync();
}

public enum KeyImportResult
{
    /// <summary>There was no key file.</summary>
    NoFile,

    /// <summary>The key was kept, and the file deleted.</summary>
    Saved,

    /// <summary>The file didn't hold a key; it was deleted all the same.</summary>
    NotAKey,
}

/// <summary>What became of a key file: the result, and the key masked (or what was wrong) for the screen.</summary>
public sealed record KeyImport(KeyImportResult Result, string Detail);

/// <summary>Getting the API key onto a device from a file, and showing it without giving it away.</summary>
public static class AssistKey
{
    /// <summary>
    /// The file a host looks for at start, in the app's own folder that a computer can copy into over USB, like
    /// <c>adb push claude-key.txt /sdcard/Android/data/com.saphyiere.prima/files/</c>.
    /// </summary>
    public const string FileName = "claude-key.txt";

    /// <summary>Whether <paramref name="key"/> looks like a Claude API key: <c>sk-ant-</c>, then no spaces.</summary>
    public static bool LooksRight(string key) => key.StartsWith("sk-ant-", StringComparison.Ordinal) && key.Length >= 30 && !key.Any(char.IsWhiteSpace);

    /// <summary>The key with all but its start and last four characters hidden, like <c>sk-ant-…a1B2</c>.</summary>
    public static string Mask(string key) => key.Length < 12 ? "…" : $"{key[..7]}…{key[^4..]}";

    /// <summary>
    /// Moves the key in the file at <paramref name="path"/> into <paramref name="store"/> and deletes the file, whatever it
    /// held. The file holds the key alone, or JSON with an <c>apiKey</c>.
    /// </summary>
    public static async Task<KeyImport> ImportAsync(string path, IAssistKeyStore store)
    {
        if (!File.Exists(path)) return new(KeyImportResult.NoFile, "");
        string text;
        try { text = (await File.ReadAllTextAsync(path)).Trim(); }
        finally { File.Delete(path); }

        var key = text;
        if (text.StartsWith('{'))
        {
            try { key = JsonDocument.Parse(text).RootElement.TryGetProperty("apiKey", out var value) ? value.GetString() ?? "" : ""; }
            catch (JsonException) { key = ""; }
        }

        if (!LooksRight(key)) return new(KeyImportResult.NotAKey, $"{FileName} didn't hold a Claude API key (one starts with sk-ant-).");
        await store.SetAsync(key);
        return new(KeyImportResult.Saved, Mask(key));
    }
}
