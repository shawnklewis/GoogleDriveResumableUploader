using System.Text;
using System.Text.Json;
using Gdup.Auth;

namespace Gdup.Drive;

/// <summary>Drive v3 metadata calls: resolves "a/b/c" folder paths, creating missing folders.</summary>
public sealed class DriveClient(HttpClient http, IAccessTokenSource tokens, string apiBase = DriveClient.DefaultApiBase)
{
    public const string DefaultApiBase = "https://www.googleapis.com";
    private const string FolderMime = "application/vnd.google-apps.folder";

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

    private async Task<JsonDocument> SendJsonAsync(Func<HttpRequestMessage> createRequest, CancellationToken ct)
    {
        using var response = await DriveHttp.SendAsync(http, tokens, createRequest, HttpCompletionOption.ResponseContentRead, ct);
        if (!response.IsSuccessStatusCode) throw await DriveApiException.FromResponseAsync(response, ct);
        return JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
    }
}
