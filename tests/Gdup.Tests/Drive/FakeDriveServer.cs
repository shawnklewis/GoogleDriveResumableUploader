using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Gdup.Tests.Drive;

public enum Fault
{
    None,
    Status503,
    Status429,
    Status401,
    QuotaExceeded,
    CommitHalfThenReset,
    Stall,
    Expire,
    CancelClient,
}

/// <summary>In-process imitation of Drive's resumable upload protocol with scripted per-chunk faults.</summary>
public sealed class FakeDriveServer : HttpMessageHandler
{
    public const string UploadBase = "https://fake.local";

    private readonly MemoryStream _committed = new();
    private string? _activeSession;
    private long _total;

    public Queue<Fault> ChunkFaults { get; } = new();
    public int SessionsStarted { get; private set; }
    public int StatusQueries { get; private set; }
    public List<string> AuthHeaders { get; } = [];
    public string? UploadedName { get; private set; }
    public string? UploadedContentType { get; private set; }
    public Action? OnCancelClient { get; set; }
    public byte[] Committed => _committed.ToArray();

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        AuthHeaders.Add(request.Headers.Authorization?.ToString() ?? "");
        var url = request.RequestUri!.AbsoluteUri;
        if (request.Method == HttpMethod.Post && url.StartsWith($"{UploadBase}/upload/drive/v3/files?uploadType=resumable"))
            return await StartSessionAsync(request, ct);
        if (request.Method == HttpMethod.Put)
            return await PutAsync(request, url, ct);
        return new HttpResponseMessage(HttpStatusCode.NotFound);
    }

    private async Task<HttpResponseMessage> StartSessionAsync(HttpRequestMessage request, CancellationToken ct)
    {
        using var metadata = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(ct));
        UploadedName = metadata.RootElement.GetProperty("name").GetString();
        UploadedContentType = request.Headers.GetValues("X-Upload-Content-Type").Single();
        _total = long.Parse(request.Headers.GetValues("X-Upload-Content-Length").Single());
        _committed.SetLength(0);
        _activeSession = $"{UploadBase}/upload/drive/v3/files?uploadType=resumable&upload_id={++SessionsStarted}";
        var response = new HttpResponseMessage(HttpStatusCode.OK);
        response.Headers.Location = new Uri(_activeSession);
        return response;
    }

    private async Task<HttpResponseMessage> PutAsync(HttpRequestMessage request, string url, CancellationToken ct)
    {
        if (url != _activeSession) return new HttpResponseMessage(HttpStatusCode.Gone);

        var range = request.Content?.Headers.ContentRange;
        if (range is null || !range.HasRange)
        {
            StatusQueries++;
            return Progress();
        }

        var fault = ChunkFaults.Count > 0 ? ChunkFaults.Dequeue() : Fault.None;
        switch (fault)
        {
            case Fault.Status503:
                await request.Content!.ReadAsByteArrayAsync(ct);
                return new HttpResponseMessage(HttpStatusCode.ServiceUnavailable);
            case Fault.Status429:
                await request.Content!.ReadAsByteArrayAsync(ct);
                return new HttpResponseMessage(HttpStatusCode.TooManyRequests);
            case Fault.Status401:
                return new HttpResponseMessage(HttpStatusCode.Unauthorized);
            case Fault.QuotaExceeded:
                return new HttpResponseMessage(HttpStatusCode.Forbidden)
                {
                    Content = new StringContent(
                        """{"error":{"code":403,"message":"Quota exceeded","errors":[{"reason":"storageQuotaExceeded"}]}}""",
                        Encoding.UTF8, "application/json"),
                };
            case Fault.Stall:
                await Task.Delay(Timeout.Infinite, ct);
                break;
            case Fault.CancelClient:
                OnCancelClient?.Invoke();
                await Task.Delay(Timeout.Infinite, ct);
                break;
            case Fault.Expire:
                _activeSession = null;
                _committed.SetLength(0);
                return new HttpResponseMessage(HttpStatusCode.Gone);
        }

        var body = await request.Content!.ReadAsByteArrayAsync(ct);
        if (range.From != _committed.Length || range.Length != _total || body.Length != range.To - range.From + 1)
            return new HttpResponseMessage(HttpStatusCode.BadRequest)
            {
                Content = new StringContent($"bad range {range} (committed {_committed.Length}, body {body.Length})"),
            };

        if (fault == Fault.CommitHalfThenReset)
        {
            _committed.Write(body, 0, body.Length / 2);
            throw new HttpRequestException("Connection reset by fake server");
        }

        _committed.Write(body);
        return Progress();
    }

    private HttpResponseMessage Progress()
    {
        if (_committed.Length == _total)
        {
            var json = JsonSerializer.Serialize(new
            {
                id = "file-1",
                name = UploadedName,
                size = _total.ToString(),
                md5Checksum = Convert.ToHexStringLower(MD5.HashData(Committed)),
            });
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(json, Encoding.UTF8, "application/json") };
        }
        var response = new HttpResponseMessage((HttpStatusCode)308);
        if (_committed.Length > 0)
            response.Headers.TryAddWithoutValidation("Range", $"bytes=0-{_committed.Length - 1}");
        return response;
    }
}
