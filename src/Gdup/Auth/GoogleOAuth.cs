using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Gdup.Auth;

public sealed class OAuthException(string error, string message) : Exception(message)
{
    public string Error { get; } = error;
}

/// <summary>Google OAuth 2.0 for installed apps: PKCE, auth URL, code exchange, refresh.</summary>
public static class GoogleOAuth
{
    public const string AuthEndpoint = "https://accounts.google.com/o/oauth2/v2/auth";
    public const string TokenEndpoint = "https://oauth2.googleapis.com/token";

    public static string ScopeUrl(string scope) => scope switch
    {
        "drive" => "https://www.googleapis.com/auth/drive",
        "drive.file" => "https://www.googleapis.com/auth/drive.file",
        _ => throw new ArgumentException($"Unsupported scope '{scope}'. Use drive or drive.file.", nameof(scope)),
    };

    public static (string Verifier, string Challenge) CreatePkce()
    {
        var verifier = Base64Url(RandomNumberGenerator.GetBytes(32));
        var challenge = Base64Url(SHA256.HashData(Encoding.ASCII.GetBytes(verifier)));
        return (verifier, challenge);
    }

    public static string BuildAuthUrl(string clientId, string redirectUri, string scope, string codeChallenge, string state)
    {
        var parameters = new Dictionary<string, string>
        {
            ["client_id"] = clientId,
            ["redirect_uri"] = redirectUri,
            ["response_type"] = "code",
            ["scope"] = ScopeUrl(scope),
            ["code_challenge"] = codeChallenge,
            ["code_challenge_method"] = "S256",
            ["state"] = state,
            ["access_type"] = "offline",
            ["prompt"] = "consent",
        };
        return AuthEndpoint + "?" + string.Join("&",
            parameters.Select(p => p.Key + "=" + Uri.EscapeDataString(p.Value)));
    }

    public static Task<OAuthToken> ExchangeCodeAsync(HttpClient http, string clientId, string clientSecret,
        string code, string verifier, string redirectUri, CancellationToken ct) =>
        PostTokenAsync(http, new Dictionary<string, string>
        {
            ["grant_type"] = "authorization_code",
            ["code"] = code,
            ["code_verifier"] = verifier,
            ["redirect_uri"] = redirectUri,
            ["client_id"] = clientId,
            ["client_secret"] = clientSecret,
        }, existingRefreshToken: null, ct);

    public static Task<OAuthToken> RefreshAsync(HttpClient http, string clientId, string clientSecret,
        string refreshToken, CancellationToken ct) =>
        PostTokenAsync(http, new Dictionary<string, string>
        {
            ["grant_type"] = "refresh_token",
            ["refresh_token"] = refreshToken,
            ["client_id"] = clientId,
            ["client_secret"] = clientSecret,
        }, refreshToken, ct);

    internal static string Base64Url(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static async Task<OAuthToken> PostTokenAsync(HttpClient http, Dictionary<string, string> form,
        string? existingRefreshToken, CancellationToken ct)
    {
        using var response = await http.PostAsync(TokenEndpoint, new FormUrlEncodedContent(form), ct);
        var body = await response.Content.ReadAsStringAsync(ct);
        var root = TryParseObject(body);

        if (!response.IsSuccessStatusCode)
        {
            var error = root is { } r1 && r1.TryGetProperty("error", out var e) ? e.GetString() ?? "unknown" : $"http_{(int)response.StatusCode}";
            var description = root is { } r2 && r2.TryGetProperty("error_description", out var d) ? d.GetString() : body;
            throw new OAuthException(error, $"Token request failed ({error}): {description}");
        }

        var json = root ?? throw new OAuthException("invalid_response", "Token endpoint returned a non-JSON body.");
        var refresh = json.TryGetProperty("refresh_token", out var rt) ? rt.GetString() : existingRefreshToken;
        return new OAuthToken(
            json.GetProperty("access_token").GetString()!,
            json.TryGetProperty("token_type", out var tt) ? tt.GetString() ?? "Bearer" : "Bearer",
            refresh ?? "",
            DateTimeOffset.UtcNow.AddSeconds(json.GetProperty("expires_in").GetInt32()));
    }

    private static JsonElement? TryParseObject(string body)
    {
        try
        {
            using var doc = JsonDocument.Parse(body);
            return doc.RootElement.ValueKind == JsonValueKind.Object ? doc.RootElement.Clone() : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
