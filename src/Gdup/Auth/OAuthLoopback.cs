using System.Collections.Specialized;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Web;

namespace Gdup.Auth;

/// <summary>
/// Installed-app sign-in: listens on 127.0.0.1:&lt;free port&gt;, sends the user to Google,
/// receives the redirect with the authorization code, and exchanges it (PKCE).
/// Uses a raw TcpListener so no http.sys URL reservation or admin rights are needed.
/// </summary>
public static class OAuthLoopback
{
    public static async Task<OAuthToken> AuthorizeAsync(HttpClient http, string clientId, string clientSecret,
        string scope, Action<string> openBrowser, CancellationToken ct, TimeSpan? connectionReadTimeout = null)
    {
        var readTimeout = connectionReadTimeout ?? TimeSpan.FromSeconds(5);
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var redirectUri = $"http://127.0.0.1:{((IPEndPoint)listener.LocalEndpoint).Port}/";
        var (verifier, challenge) = GoogleOAuth.CreatePkce();
        var state = GoogleOAuth.Base64Url(RandomNumberGenerator.GetBytes(16));

        openBrowser(GoogleOAuth.BuildAuthUrl(clientId, redirectUri, scope, challenge, state));

        while (true)
        {
            using var client = await listener.AcceptTcpClientAsync(ct);
            var stream = client.GetStream();
            using var readCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            readCts.CancelAfter(readTimeout);
            NameValueCollection? query;
            try
            {
                query = await ReadQueryAsync(stream, readCts.Token);
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                // Connection accepted but never sent a complete request line; drop it and keep listening.
                continue;
            }
            if (query is null || (query["code"] is null && query["error"] is null))
            {
                await RespondAsync(stream, "404 Not Found", "Not found.", ct);
                continue;
            }
            if (query["state"] != state)
            {
                await RespondAsync(stream, "400 Bad Request", "Sign-in state did not match. Close this window and try again.", ct);
                throw new OAuthException("state_mismatch", "OAuth state mismatch; the sign-in response was not for this request.");
            }
            if (query["error"] is { } error)
            {
                await RespondAsync(stream, "200 OK", $"Sign-in failed: {error}. You can close this window.", ct);
                throw new OAuthException(error, $"Google sign-in failed: {error}");
            }
            await RespondAsync(stream, "200 OK", "gdup is signed in. You can close this window.", ct);
            return await GoogleOAuth.ExchangeCodeAsync(http, clientId, clientSecret, query["code"]!, verifier, redirectUri, ct);
        }
    }

    private static async Task<NameValueCollection?> ReadQueryAsync(NetworkStream stream, CancellationToken ct)
    {
        var buffer = new byte[8192];
        var total = 0;
        while (total < buffer.Length)
        {
            var n = await stream.ReadAsync(buffer.AsMemory(total), ct);
            if (n == 0) break;
            total += n;
            if (Encoding.ASCII.GetString(buffer, 0, total).Contains("\r\n\r\n")) break;
        }
        // Request line: GET /?state=...&code=... HTTP/1.1
        var parts = Encoding.ASCII.GetString(buffer, 0, total).Split("\r\n")[0].Split(' ');
        if (parts.Length < 2 || parts[0] != "GET") return null;
        var q = parts[1].IndexOf('?');
        return q < 0 ? null : HttpUtility.ParseQueryString(parts[1][(q + 1)..]);
    }

    private static async Task RespondAsync(NetworkStream stream, string status, string message, CancellationToken ct)
    {
        var body = Encoding.UTF8.GetBytes($"<html><body><p>{WebUtility.HtmlEncode(message)}</p></body></html>");
        var header = $"HTTP/1.1 {status}\r\nContent-Type: text/html; charset=utf-8\r\nContent-Length: {body.Length}\r\nConnection: close\r\n\r\n";
        await stream.WriteAsync(Encoding.ASCII.GetBytes(header), ct);
        await stream.WriteAsync(body, ct);
    }
}
