using System.Net;
using System.Net.Http.Headers;
using Gdup.Auth;

namespace Gdup.Drive;

internal static class DriveHttp
{
    /// <summary>Sends with a bearer token; on 401 refreshes once and resends a fresh request from the factory.</summary>
    public static async Task<HttpResponseMessage> SendAsync(HttpClient http, IAccessTokenSource tokens,
        Func<HttpRequestMessage> createRequest, HttpCompletionOption completion, CancellationToken ct)
    {
        var response = await SendOnceAsync(http, createRequest(), await tokens.GetAccessTokenAsync(ct), completion, ct);
        if (response.StatusCode != HttpStatusCode.Unauthorized) return response;

        response.Dispose();
        var refreshed = await tokens.ForceRefreshAsync(ct);
        return await SendOnceAsync(http, createRequest(), refreshed, completion, ct);
    }

    private static Task<HttpResponseMessage> SendOnceAsync(HttpClient http, HttpRequestMessage request,
        string accessToken, HttpCompletionOption completion, CancellationToken ct)
    {
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        return http.SendAsync(request, completion, ct);
    }
}
