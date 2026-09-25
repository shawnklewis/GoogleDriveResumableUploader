using System.Net;
using Gdup.Auth;
using Gdup.Config;

namespace Gdup.Tests.Auth;

public class TokenProviderTests
{
    private static Remote RemoteExpiringIn(TimeSpan t) =>
        new("gdrive", "cid", "sec", "drive", "", new OAuthToken("old-access", "Bearer", "1//r", DateTimeOffset.UtcNow + t));

    private static StubHandler TokenEndpoint(HttpStatusCode code, string json) =>
        new((_, _) => Task.FromResult(StubHandler.Json(code, json)));

    private const string Fresh = """{"access_token":"new-access","expires_in":3600,"token_type":"Bearer"}""";

    [Fact]
    public async Task ValidToken_IsReturnedWithoutRefreshing()
    {
        var handler = TokenEndpoint(HttpStatusCode.OK, Fresh);
        var persisted = new List<Remote>();
        var provider = new TokenProvider(RemoteExpiringIn(TimeSpan.FromHours(1)), new HttpClient(handler), persisted.Add);

        Assert.Equal("old-access", await provider.GetAccessTokenAsync(CancellationToken.None));
        Assert.Empty(handler.Requests);
        Assert.Empty(persisted);
    }

    [Fact]
    public async Task TokenNearExpiry_IsRefreshedAndPersisted()
    {
        var handler = TokenEndpoint(HttpStatusCode.OK, Fresh);
        var persisted = new List<Remote>();
        var provider = new TokenProvider(RemoteExpiringIn(TimeSpan.FromMinutes(2)), new HttpClient(handler), persisted.Add);

        Assert.Equal("new-access", await provider.GetAccessTokenAsync(CancellationToken.None));
        Assert.Equal("new-access", await provider.GetAccessTokenAsync(CancellationToken.None));

        Assert.Single(handler.Requests);
        var saved = Assert.Single(persisted);
        Assert.Equal("new-access", saved.Token!.AccessToken);
        Assert.Equal("1//r", saved.Token.RefreshToken);
    }

    [Fact]
    public async Task ForceRefresh_RefreshesEvenWhenTokenIsValid()
    {
        var handler = TokenEndpoint(HttpStatusCode.OK, Fresh);
        var provider = new TokenProvider(RemoteExpiringIn(TimeSpan.FromHours(1)), new HttpClient(handler), _ => { });

        Assert.Equal("new-access", await provider.ForceRefreshAsync(CancellationToken.None));
        Assert.Single(handler.Requests);
    }

    [Fact]
    public async Task RevokedRefreshToken_ThrowsAuthExpiredWithReconnectHint()
    {
        var handler = TokenEndpoint(HttpStatusCode.BadRequest, """{"error":"invalid_grant","error_description":"revoked"}""");
        var provider = new TokenProvider(RemoteExpiringIn(TimeSpan.Zero), new HttpClient(handler), _ => { });

        var ex = await Assert.ThrowsAsync<AuthExpiredException>(() => provider.GetAccessTokenAsync(CancellationToken.None));
        Assert.Contains("gdup config reconnect gdrive", ex.Message);
    }

    [Fact]
    public void RemoteWithoutToken_IsRejected()
    {
        var remote = new Remote("gdrive", "cid", "sec", "drive", "", null);
        Assert.Throws<ArgumentException>(() => new TokenProvider(remote, new HttpClient(), _ => { }));
    }
}
