namespace Playground.Sync;

/// <summary>A copy, restore or export that stopped in a way the person should hear about, in a sentence for the screen.</summary>
public class SyncException(string message, Exception? inner = null) : Exception(message, inner);

/// <summary>
/// A restore that didn't go ahead because what was there couldn't be kept first: nothing was restored. The app can offer to restore
/// without keeping it (<see cref="Syncer.RestoreAsync(SyncCopy, bool, CancellationToken)"/>).
/// </summary>
public sealed class SyncKeepException(string message, Exception inner) : SyncException(message, inner);

/// <summary>A copy or export that would have held a secret: it stops before anything is written, and says which file held it.</summary>
public sealed class SyncSecretException(string file) : SyncException($"{file} holds a secret, which copies never take")
{
    /// <summary>The file, where it sits in a copy ("errors.log").</summary>
    public string File => file;
}

/// <summary>A file that changed between being fingerprinted and being written: it's fingerprinted again and written as it is now.</summary>
internal sealed class SyncChangedException(string file) : IOException($"{file} changed while it was being copied");
