using Gdup.Auth;

namespace Gdup.Tests;

public sealed class FakeTokens : IAccessTokenSource
{
    public string Current { get; private set; } = "tok-1";
    public int Refreshes { get; private set; }

    public Task<string> GetAccessTokenAsync(CancellationToken ct) => Task.FromResult(Current);

    public Task<string> ForceRefreshAsync(CancellationToken ct)
    {
        Refreshes++;
        Current = $"tok-{Refreshes + 1}";
        return Task.FromResult(Current);
    }
}
