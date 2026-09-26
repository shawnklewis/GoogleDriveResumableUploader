namespace Gdup.Tests.Drive;

/// <summary>
/// Wraps a real file stream and throws IOException (like a bad disk block) when a read starts inside
/// [BadStart, BadEnd), for the first <c>failures</c> such reads; -1 means fail forever.
/// </summary>
public sealed class FailingReadStream(Stream inner, long badStart, long badEnd, int failures) : Stream
{
    private int _failuresLeft = failures;

    public int FailedReads { get; private set; }

    public override bool CanRead => true;
    public override bool CanSeek => true;
    public override bool CanWrite => false;
    public override long Length => inner.Length;

    public override long Position
    {
        get => inner.Position;
        set => inner.Position = value;
    }

    public override int Read(byte[] buffer, int offset, int count)
    {
        ThrowIfBad(count);
        return inner.Read(buffer, offset, count);
    }

    public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken ct = default)
    {
        ThrowIfBad(buffer.Length);
        return await inner.ReadAsync(buffer, ct);
    }

    public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken ct) =>
        ReadAsync(buffer.AsMemory(offset, count), ct).AsTask();

    public override long Seek(long offset, SeekOrigin origin) => inner.Seek(offset, origin);
    public override void Flush() { }
    public override void SetLength(long value) => throw new NotSupportedException();
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

    protected override void Dispose(bool disposing)
    {
        if (disposing) inner.Dispose();
        base.Dispose(disposing);
    }

    private void ThrowIfBad(int count)
    {
        var start = inner.Position;
        if (_failuresLeft == 0 || start + count <= badStart || start >= badEnd) return;
        if (_failuresLeft > 0) _failuresLeft--;
        FailedReads++;
        throw new IOException("The request could not be performed because of an I/O device error.");
    }
}
