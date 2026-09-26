using System.Buffers;
using System.Net;

namespace Gdup.Drive;

public sealed class LocalFileChangedException(string message) : Exception(message);

/// <summary>The local file could not be read (e.g. a bad disk block). Not an IOException on purpose,
/// so it is never mistaken for a network error and retried forever.</summary>
public sealed class LocalFileReadException(string filePath, long offset, IOException inner)
    : Exception($"Cannot read {filePath} at offset {offset:N0}: {inner.Message} The disk may be failing; "
        + "check the Windows System event log for 'bad block' errors.", inner)
{
    public string FilePath { get; } = filePath;
    public long Offset { get; } = offset;
}

/// <summary>Streams one byte range of a file straight to the socket; nothing is buffered beyond one read.</summary>
public sealed class FileRangeContent(string path, long offset, long length, Action<long> onBytesSent,
    Func<string, Stream>? openRead = null) : HttpContent
{
    public const int BufferSize = 4 * 1024 * 1024;

    /// <summary>Reads use BufferSize, but each read is written to the network stream in slices no
    /// larger than this, so the stall watchdog sees activity more often than once per 4 MiB.</summary>
    public const int WriteSliceSize = 1024 * 1024;

    protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context) =>
        SerializeToStreamAsync(stream, context, CancellationToken.None);

    protected override async Task SerializeToStreamAsync(Stream stream, TransportContext? context, CancellationToken ct)
    {
        await using var file = openRead?.Invoke(path)
            ?? new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite,
                bufferSize: 1, FileOptions.Asynchronous | FileOptions.SequentialScan);
        file.Position = offset;
        var buffer = ArrayPool<byte>.Shared.Rent(BufferSize);
        try
        {
            var remaining = length;
            while (remaining > 0)
            {
                int read;
                try
                {
                    read = await file.ReadAsync(buffer.AsMemory(0, (int)Math.Min(BufferSize, remaining)), ct);
                }
                catch (IOException ex) when (ex is not FileNotFoundException and not DirectoryNotFoundException)
                {
                    throw new LocalFileReadException(path, offset + length - remaining, ex);
                }
                if (read == 0)
                    throw new LocalFileChangedException($"{path} ended {remaining} bytes early; the file changed during upload.");
                var written = 0;
                while (written < read)
                {
                    var slice = Math.Min(WriteSliceSize, read - written);
                    await stream.WriteAsync(buffer.AsMemory(written, slice), ct);
                    written += slice;
                    onBytesSent(slice);
                }
                remaining -= read;
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    protected override bool TryComputeLength(out long computedLength)
    {
        computedLength = length;
        return true;
    }
}
