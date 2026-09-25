using System.Text.Json;
using System.Text.Json.Serialization;

namespace Gdup.Auth;

/// <summary>OAuth token in the same JSON shape rclone stores.</summary>
public sealed record OAuthToken(
    [property: JsonPropertyName("access_token")] string AccessToken,
    [property: JsonPropertyName("token_type")] string TokenType,
    [property: JsonPropertyName("refresh_token")] string RefreshToken,
    [property: JsonPropertyName("expiry")] DateTimeOffset Expiry)
{
    public string ToJson() => JsonSerializer.Serialize(this);

    public static OAuthToken FromJson(string json) =>
        JsonSerializer.Deserialize<OAuthToken>(json) ?? throw new FormatException("Token JSON is empty.");
}
