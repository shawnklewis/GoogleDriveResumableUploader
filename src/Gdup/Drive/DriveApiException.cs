using System.Text.Json;

namespace Gdup.Drive;

public sealed class DriveApiException(int statusCode, string reason, string message) : Exception(message)
{
    public int StatusCode { get; } = statusCode;
    public string Reason { get; } = reason;

    public bool IsTransient =>
        StatusCode >= 500
        || StatusCode == 429
        || (StatusCode == 403 && Reason is "rateLimitExceeded" or "userRateLimitExceeded");

    public static async Task<DriveApiException> FromResponseAsync(HttpResponseMessage response, CancellationToken ct)
    {
        var body = await response.Content.ReadAsStringAsync(ct);
        var reason = "";
        var message = body;
        try
        {
            using var doc = JsonDocument.Parse(body);
            var error = doc.RootElement.GetProperty("error");
            if (error.TryGetProperty("message", out var m)) message = m.GetString() ?? body;
            if (error.TryGetProperty("errors", out var errors) && errors.GetArrayLength() > 0
                && errors[0].TryGetProperty("reason", out var r))
                reason = r.GetString() ?? "";
        }
        catch (Exception ex) when (ex is JsonException or KeyNotFoundException or InvalidOperationException)
        {
            // Not a Google JSON error body; keep the raw text.
        }
        var status = (int)response.StatusCode;
        return new DriveApiException(status, reason, $"Drive API error {status} {reason}: {message}".Trim());
    }
}
