using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Web;
using Gdup.Auth;

namespace Gdup.Tests.Auth;

public class GoogleOAuthTests
{
    [Fact]
    public void CreatePkce_ChallengeIsBase64UrlSha256OfVerifier()
    {
        var (verifier, challenge) = GoogleOAuth.CreatePkce();

        Assert.Equal(43, verifier.Length);
        Assert.Equal(GoogleOAuth.Base64Url(SHA256.HashData(Encoding.ASCII.GetBytes(verifier))), challenge);
        Assert.DoesNotContain('=', challenge);
        Assert.DoesNotContain('+', challenge);
    }

    [Fact]
    public void BuildAuthUrl_RequestsOfflineAccessWithPkce()
    {
        var url = GoogleOAuth.BuildAuthUrl("cid", "http://127.0.0.1:5000/", "drive", "chal", "st");
        var q = HttpUtility.ParseQueryString(new Uri(url).Query);

        Assert.StartsWith(GoogleOAuth.AuthEndpoint + "?", url);
        Assert.Equal("cid", q["client_id"]);
        Assert.Equal("http://127.0.0.1:5000/", q["redirect_uri"]);
        Assert.Equal("code", q["response_type"]);
        Assert.Equal("https://www.googleapis.com/auth/drive", q["scope"]);
        Assert.Equal("chal", q["code_challenge"]);
        Assert.Equal("S256", q["code_challenge_method"]);
        Assert.Equal("st", q["state"]);
        Assert.Equal("offline", q["access_type"]);
        Assert.Equal("consent", q["prompt"]);
    }

    [Fact]
    public void ScopeUrl_RejectsUnknownScopes()
    {
        Assert.Equal("https://www.googleapis.com/auth/drive.file", GoogleOAuth.ScopeUrl("drive.file"));
        Assert.Throws<ArgumentException>(() => GoogleOAuth.ScopeUrl("photos"));
    }

    [Fact]
    public async Task RefreshAsync_KeepsExistingRefreshTokenWhenGoogleOmitsIt()
    {
        var handler = new StubHandler((_, _) => Task.FromResult(StubHandler.Json(HttpStatusCode.OK,
            """{"access_token":"new","expires_in":3599,"token_type":"Bearer"}""")));

        var token = await GoogleOAuth.RefreshAsync(new HttpClient(handler), "cid", "sec", "1//old", CancellationToken.None);

        Assert.Equal("new", token.AccessToken);
        Assert.Equal("1//old", token.RefreshToken);
        Assert.InRange(token.Expiry - DateTimeOffset.UtcNow, TimeSpan.FromMinutes(59), TimeSpan.FromMinutes(60));
        Assert.Equal(GoogleOAuth.TokenEndpoint, handler.Requests[0].RequestUri!.ToString());
        Assert.Contains("grant_type=refresh_token", handler.Bodies[0]);
        Assert.Contains("refresh_token=1%2F%2Fold", handler.Bodies[0]);
    }

    [Fact]
    public async Task RefreshAsync_ErrorResponse_ThrowsWithGoogleErrorCode()
    {
        var handler = new StubHandler((_, _) => Task.FromResult(StubHandler.Json(HttpStatusCode.BadRequest,
            """{"error":"invalid_grant","error_description":"Token has been expired or revoked."}""")));

        var ex = await Assert.ThrowsAsync<OAuthException>(() =>
            GoogleOAuth.RefreshAsync(new HttpClient(handler), "cid", "sec", "1//old", CancellationToken.None));

        Assert.Equal("invalid_grant", ex.Error);
        Assert.Contains("expired or revoked", ex.Message);
    }
}
