using System.Security.Cryptography;

namespace Playground.Sync;

/// <summary>
/// A mark of the secrets a copy last searched every file for, kept in a file of the app's own (<see cref="SyncOptions.SecretsMark"/>),
/// never in a store: an HMAC-SHA256 of them under a key made for it, written beside it. It tells whether the secrets are the same as
/// last time without anything in it giving them back, as a plain hash of one could be checked against a guess.
/// </summary>
internal static class SecretsMark
{
    /// <summary>Whether the mark at <paramref name="path"/> is of these secrets; false when there's none, or it can't be read.</summary>
    public static bool Matches(string? path, IReadOnlyList<byte[]> secrets)
    {
        if (path is null) return false;
        try
        {
            var kept = File.ReadAllText(path).Split(' ');
            return kept.Length == 2 && CryptographicOperations.FixedTimeEquals(Convert.FromHexString(kept[1]), Of(Convert.FromHexString(kept[0]), secrets));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or FormatException)
        {
            return false;
        }
    }

    /// <summary>Keeps the mark of these secrets, under a key made afresh. When it can't be, the next copy reads every file again: only slower.</summary>
    public static void Keep(string? path, IReadOnlyList<byte[]> secrets)
    {
        if (path is null) return;
        var key = RandomNumberGenerator.GetBytes(32);
        try
        {
            File.WriteAllText(path, $"{Convert.ToHexStringLower(key)} {Convert.ToHexStringLower(Of(key, secrets))}");
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
        }
    }

    // Each secret once, in an order of their own, its length before it, so no two lists of them give the same mark.
    private static byte[] Of(byte[] key, IReadOnlyList<byte[]> secrets)
    {
        using var hmac = IncrementalHash.CreateHMAC(HashAlgorithmName.SHA256, key);
        foreach (var secret in secrets.DistinctBy(Convert.ToHexString).OrderBy(Convert.ToHexString, StringComparer.Ordinal))
        {
            hmac.AppendData(BitConverter.GetBytes(secret.Length));
            hmac.AppendData(secret);
        }
        return hmac.GetHashAndReset();
    }
}
