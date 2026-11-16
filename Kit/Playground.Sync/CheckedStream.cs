using System.Security.Cryptography;

namespace Playground.Sync;

/// <summary>
/// A stream read through, checked against the size and SHA-256 it should have: once all of it has been read, a difference throws
/// <c>mismatch()</c>, as does any byte past its size, so a store never keeps content under a fingerprint that isn't its own and a
/// restore never takes damaged content. It tells <c>read</c> each count of bytes read, for progress, and disposes <c>owner</c> with
/// itself (an archive its entry came from).
/// </summary>
internal sealed class CheckedStream(Stream inner, long size, string sha256, Func<Exception> mismatch, Action<int>? read = null, IDisposable? owner = null) : Stream
{
    private readonly IncrementalHash hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
    private long count;
    private bool whole;

    public override bool CanRead => true;

    public override bool CanSeek => false;

    public override bool CanWrite => false;

    public override long Length => size;

    public override long Position
    {
        get => count;
        set => throw new NotSupportedException();
    }

    public override int Read(byte[] buffer, int offset, int count) => Read(buffer.AsSpan(offset, count));

    public override int Read(Span<byte> buffer)
    {
        var n = inner.Read(buffer);
        Seen(buffer[..n]);
        return n;
    }

    public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
        var n = await inner.ReadAsync(buffer, cancellationToken);
        Seen(buffer.Span[..n]);
        return n;
    }

    public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
        ReadAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();

    private void Seen(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length > 0)
        {
            if (whole) throw mismatch();
            hash.AppendData(bytes);
            count += bytes.Length;
            read?.Invoke(bytes.Length);
            if (count > size) throw mismatch();
            if (count < size) return;
        }
        else if (whole)
        {
            return;
        }

        // All of it read (or the end came early): its fingerprint has to be the one it should have.
        whole = true;
        if (count != size || !Convert.ToHexStringLower(hash.GetHashAndReset()).Equals(sha256, StringComparison.OrdinalIgnoreCase)) throw mismatch();
    }

    public override void Flush()
    {
    }

    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

    public override void SetLength(long value) => throw new NotSupportedException();

    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            inner.Dispose();
            owner?.Dispose();
            hash.Dispose();
        }
        base.Dispose(disposing);
    }
}
