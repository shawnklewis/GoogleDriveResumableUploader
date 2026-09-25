using System.Net;
using System.Web;
using Gdup.Auth;

namespace Gdup.Tests.Auth;

public class OAuthLoopbackTests
{
    [Fact]
    public async Task AuthorizeAsync_ReceivesCodeOnLoopback_AndExchangesItWithVerifier()
    {
        var handler = new StubHandler((_, _) => Task.FromResult(StubHandler.Json(HttpStatusCode.OK,
            """{"access_token":"at","refresh_token":"rt","expires_in":3600,"token_type":"Bearer"}""")));
        using var browser = new HttpClient();
        Task<string>? browserVisit = null;

        var token = await OAuthLoopback.AuthorizeAsync(new HttpClient(handler), "cid", "sec", "drive", url =>
        {
            var q = HttpUtility.ParseQueryString(new Uri(url).Query);
            browserVisit = browser.GetStringAsync($"{q["redirect_uri"]}?state={q["state"]}&code=the-code");
        }, CancellationToken.None);

        Assert.Equal("at", token.AccessToken);
        Assert.Equal("rt", token.RefreshToken);
        Assert.Contains("signed in", await browserVisit!);
        Assert.Contains("code=the-code", handler.Bodies[0]);
        Assert.Contains("code_verifier=", handler.Bodies[0]);
        Assert.Contains("grant_type=authorization_code", handler.Bodies[0]);
    }

    [Fact]
    public async Task AuthorizeAsync_WrongState_Throws()
    {
        using var browser = new HttpClient();
        var handler = new StubHandler((_, _) => throw new InvalidOperationException("no token exchange expected"));

        var ex = await Assert.ThrowsAsync<OAuthException>(() =>
            OAuthLoopback.AuthorizeAsync(new HttpClient(handler), "cid", "sec", "drive", url =>
            {
                var q = HttpUtility.ParseQueryString(new Uri(url).Query);
                _ = browser.GetStringAsync($"{q["redirect_uri"]}?state=forged&code=x");
            }, CancellationToken.None));

        Assert.Equal("state_mismatch", ex.Error);
        Assert.Empty(handler.Requests);
    }
}
