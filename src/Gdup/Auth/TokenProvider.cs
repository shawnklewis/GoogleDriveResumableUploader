using Gdup.Config;

namespace Gdup.Auth;

public interface IAccessTokenSource
{
    Task<string> GetAccessTokenAsync(CancellationToken ct);
    Task<string> ForceRefreshAsync(CancellationToken ct);
}

public sealed class AuthExpiredException(string remoteName, Exception inner)
    : Exception($"Google rejected the saved sign-in for '{remoteName}'. Run: gdup config reconnect {remoteName}", inner)
{
    public string RemoteName { get; } = remoteName;
}

/// <summary>Hands out access tokens, refreshing near expiry and persisting refreshed tokens.</summary>
public sealed class TokenProvider : IAccessTokenSource
{
    private static readonly TimeSpan RefreshMargin = TimeSpan.FromMinutes(5);
    private readonly SemaphoreSlim _lock = new(1, 1);
    private readonly HttpClient _http;
    private readonly Action<Remote> _persist;
    private Remote _remote;

    public TokenProvider(Remote remote, HttpClient http, Action<Remote> persist)
    {
        if (remote.Token is null)
            throw new ArgumentException($"Remote '{remote.Name}' is not signed in. Run: gdup config reconnect {remote.Name}", nameof(remote));
        _remote = remote;
        _http = http;
        _persist = persist;
    }

    public async Task<string> GetAccessTokenAsync(CancellationToken ct)
    {
        await _lock.WaitAsync(ct);
        try
        {
            var token = _remote.Token!;
            return token.Expiry - DateTimeOffset.UtcNow > RefreshMargin
                ? token.AccessToken
                : await RefreshLockedAsync(ct);
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task<string> ForceRefreshAsync(CancellationToken ct)
    {
        await _lock.WaitAsync(ct);
        try
        {
            return await RefreshLockedAsync(ct);
        }
        finally
        {
            _lock.Release();
        }
    }

    private async Task<string> RefreshLockedAsync(CancellationToken ct)
    {
        OAuthToken fresh;
        try
        {
            fresh = await GoogleOAuth.RefreshAsync(_http, _remote.ClientId, _remote.ClientSecret, _remote.Token!.RefreshToken, ct);
        }
        catch (OAuthException ex) when (ex.Error is "invalid_grant" or "invalid_client" or "unauthorized_client")
        {
            throw new AuthExpiredException(_remote.Name, ex);
        }
        _remote = _remote with { Token = fresh };
        _persist(_remote);
        return fresh.AccessToken;
    }
}
