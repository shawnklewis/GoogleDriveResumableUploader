using System.Buffers;
using System.Net;

namespace Gdup.Drive;

public sealed class LocalFileChangedException(string message) : Exception(message);

/// <summary>Streams one byte range of a file straight to the socket; nothing is buffered beyond one read.</summary>
public sealed class FileRangeContent(string path, long offset, long length, Action<long> onBytesSent) : HttpContent
{
    public const int BufferSize = 4 * 1024 * 1024;

    protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context) =>
        SerializeToStreamAsync(stream, context, CancellationToken.None);

    protected override async Task SerializeToStreamAsync(Stream stream, TransportContext? context, CancellationToken ct)
    {
        await using var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite,
            bufferSize: 1, FileOptions.Asynchronous | FileOptions.SequentialScan);
        file.Position = offset;
        var buffer = ArrayPool<byte>.Shared.Rent(BufferSize);
        try
        {
            var remaining = length;
            while (remaining > 0)
            {
                var read = await file.ReadAsync(buffer.AsMemory(0, (int)Math.Min(BufferSize, remaining)), ct);
                if (read == 0)
                    throw new LocalFileChangedException($"{path} ended {remaining} bytes early; the file changed during upload.");
                await stream.WriteAsync(buffer.AsMemory(0, read), ct);
                remaining -= read;
                onBytesSent(read);
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
