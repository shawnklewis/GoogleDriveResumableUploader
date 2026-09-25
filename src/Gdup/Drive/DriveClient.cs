using System.Text;
using System.Text.Json;
using Gdup.Auth;

namespace Gdup.Drive;

/// <summary>Drive v3 metadata calls: resolves "a/b/c" folder paths, creating missing folders.</summary>
public sealed class DriveClient(
    HttpClient http,
    IAccessTokenSource tokens,
    string apiBase = DriveClient.DefaultApiBase,
    TimeSpan? requestTimeout = null,
    Func<TimeSpan, CancellationToken, Task>? delay = null)
{
    public const string DefaultApiBase = "https://www.googleapis.com";
    private const string FolderMime = "application/vnd.google-apps.folder";
    private const int MaxAttempts = 5;
    private readonly TimeSpan _requestTimeout = requestTimeout ?? TimeSpan.FromSeconds(60);
    private readonly Func<TimeSpan, CancellationToken, Task> _delay = delay ?? Task.Delay;

    public async Task<string> ResolveFolderAsync(string rootFolderId, string path, CancellationToken ct)
    {
        var parent = string.IsNullOrEmpty(rootFolderId) ? "root" : rootFolderId;
        var segments = path.Split(['/', '\\'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        foreach (var segment in segments)
            parent = await FindFolderAsync(parent, segment, ct) ?? await CreateFolderAsync(parent, segment, ct);
        return parent;
    }

    internal static string Escape(string value) => value.Replace("\\", "\\\\").Replace("'", "\\'");

    private async Task<string?> FindFolderAsync(string parentId, string name, CancellationToken ct)
    {
        var q = $"name = '{Escape(name)}' and '{Escape(parentId)}' in parents and mimeType = '{FolderMime}' and trashed = false";
        var url = $"{apiBase}/drive/v3/files?q={Uri.EscapeDataString(q)}&fields=files(id)&pageSize=10"
                  + "&supportsAllDrives=true&includeItemsFromAllDrives=true&corpora=allDrives";
        using var doc = await SendJsonAsync(() => new HttpRequestMessage(HttpMethod.Get, url), ct);
        var files = doc.RootElement.GetProperty("files");
        return files.GetArrayLength() == 0 ? null : files[0].GetProperty("id").GetString();
    }

    private async Task<string> CreateFolderAsync(string parentId, string name, CancellationToken ct)
    {
        var url = $"{apiBase}/drive/v3/files?fields=id&supportsAllDrives=true";
        var body = JsonSerializer.Serialize(new { name, mimeType = FolderMime, parents = new[] { parentId } });
        using var doc = await SendJsonAsync(() => new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json"),
        }, ct);
        return doc.RootElement.GetProperty("id").GetString()!;
    }

    /// <summary>Sends one metadata request with a per-attempt timeout, retrying transient failures
    /// up to MaxAttempts times with the shared backoff; never swallows the caller's own cancellation.</summary>
    private async Task<JsonDocument> SendJsonAsync(Func<HttpRequestMessage> createRequest, CancellationToken ct)
    {
        Exception? lastError = null;
        for (var attempt = 1; attempt <= MaxAttempts; attempt++)
        {
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeoutCts.CancelAfter(_requestTimeout);
            try
            {
                using var response = await DriveHttp.SendAsync(http, tokens, createRequest, HttpCompletionOption.ResponseContentRead, timeoutCts.Token);
                if (!response.IsSuccessStatusCode) throw await DriveApiException.FromResponseAsync(response, ct);
                return JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                lastError = new TimeoutException($"Drive did not answer within {_requestTimeout.TotalSeconds:0.#} s.");
            }
            catch (Exception ex) when (!ct.IsCancellationRequested && IsTransient(ex))
            {
                lastError = ex;
            }

            if (attempt < MaxAttempts)
                await _delay(Backoff.For(attempt), ct);
        }
        throw lastError!;
    }

    private static bool IsTransient(Exception ex) => ex switch
    {
        DriveApiException api => api.IsTransient,
        HttpRequestException => true,
        TimeoutException => true,
        _ => false,
    };
}
