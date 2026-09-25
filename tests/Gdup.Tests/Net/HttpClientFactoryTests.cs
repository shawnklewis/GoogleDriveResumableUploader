using System.Net;
using System.Net.Sockets;
using System.Text;
using Gdup.Net;

namespace Gdup.Tests.Net;

public class HttpClientFactoryTests
{
    [Fact]
    public async Task BoundClient_ConnectsFromTheBoundAddress()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        var server = Task.Run(async () =>
        {
            using var client = await listener.AcceptTcpClientAsync();
            var remote = (IPEndPoint)client.Client.RemoteEndPoint!;
            var stream = client.GetStream();
            _ = await stream.ReadAsync(new byte[4096]);
            var body = remote.Address.ToString();
            await stream.WriteAsync(Encoding.ASCII.GetBytes(
                $"HTTP/1.1 200 OK\r\nContent-Length: {body.Length}\r\nConnection: close\r\n\r\n{body}"));
        });

        using var http = HttpClientFactory.Create(IPAddress.Loopback);
        var seenByServer = await http.GetStringAsync($"http://127.0.0.1:{port}/");
        await server;

        Assert.Equal("127.0.0.1", seenByServer);
    }

    [Fact]
    public async Task BoundClient_WithNoAddressOfTheSameFamily_Fails()
    {
        using var http = HttpClientFactory.Create(IPAddress.IPv6Loopback);
        await Assert.ThrowsAnyAsync<HttpRequestException>(() => http.GetStringAsync("http://127.0.0.1:9/"));
    }

    [Fact]
    public void Handler_NeverFollowsRedirects_AndUsesCustomConnect()
    {
        using var handler = HttpClientFactory.CreateHandler(null);
        Assert.False(handler.AllowAutoRedirect);
        Assert.NotNull(handler.ConnectCallback);
    }
}
