using Gdup.Auth;

namespace Gdup.Config;

public sealed record Remote(
    string Name,
    string ClientId,
    string ClientSecret,
    string Scope,
    string RootFolderId,
    OAuthToken? Token);
