using System.Net;
using System.Net.Sockets;

namespace Gdup.Net;

/// <summary>
/// Builds the single HttpClient used for a run. Every connection goes through ConnectCallback so we can
/// bind the socket to the --via interface's local IP (Windows then routes out of that NIC) and tune it
/// for one long, fast upload stream.
/// </summary>
public static class HttpClientFactory
{
    public const int SendBufferBytes = 8 * 1024 * 1024;

    public static HttpClient Create(IPAddress? bindAddress) => new(CreateHandler(bindAddress))
    {
        Timeout = Timeout.InfiniteTimeSpan,
        DefaultRequestVersion = HttpVersion.Version11,
        DefaultVersionPolicy = HttpVersionPolicy.RequestVersionExact,
    };

    public static SocketsHttpHandler CreateHandler(IPAddress? bindAddress) => new()
    {
        AllowAutoRedirect = false, // Drive answers 308 "Resume Incomplete"; it is not a redirect.
        AutomaticDecompression = DecompressionMethods.None,
        PooledConnectionLifetime = Timeout.InfiniteTimeSpan,
        PooledConnectionIdleTimeout = TimeSpan.FromMinutes(5),
        ConnectCallback = (context, ct) => ConnectAsync(context.DnsEndPoint, bindAddress, ct),
    };

    private static async ValueTask<Stream> ConnectAsync(DnsEndPoint endPoint, IPAddress? bindAddress, CancellationToken ct)
    {
        var addresses = await Dns.GetHostAddressesAsync(endPoint.Host, ct);
        if (bindAddress is not null)
            addresses = addresses.Where(a => a.AddressFamily == bindAddress.AddressFamily).ToArray();
        if (addresses.Length == 0)
            throw new HttpRequestException($"{endPoint.Host} has no {bindAddress?.AddressFamily} address usable from {bindAddress}.");

        Exception? last = null;
        foreach (var address in addresses)
        {
            var socket = new Socket(address.AddressFamily, SocketType.Stream, ProtocolType.Tcp)
            {
                NoDelay = true,
                SendBufferSize = SendBufferBytes,
            };
            try
            {
                if (bindAddress is not null) socket.Bind(new IPEndPoint(bindAddress, 0));
                await socket.ConnectAsync(new IPEndPoint(address, endPoint.Port), ct);
                return new NetworkStream(socket, ownsSocket: true);
            }
            catch (SocketException ex)
            {
                socket.Dispose();
                last = ex;
            }
            catch
            {
                socket.Dispose();
                throw;
            }
        }
        throw new HttpRequestException(
            $"Could not connect to {endPoint.Host}:{endPoint.Port}{(bindAddress is null ? "" : $" from {bindAddress}")}.", last);
    }
}
