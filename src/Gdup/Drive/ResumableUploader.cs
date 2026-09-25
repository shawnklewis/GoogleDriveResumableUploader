using System.Diagnostics;
using System.Globalization;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Gdup.Auth;

namespace Gdup.Drive;

public sealed record UploadRequest(string FilePath, string RemoteName, string FolderId, string Name, long ChunkSize, TimeSpan StallTimeout);

public sealed record UploadResult(string FileId, string Name, long Size, string? DriveMd5, string? LocalMd5)
{
    /// <summary>null when Drive did not report a checksum, or the local MD5 could not be computed.</summary>
    public bool? Md5Matches => DriveMd5 is null || LocalMd5 is null
        ? null
        : string.Equals(DriveMd5, LocalMd5, StringComparison.OrdinalIgnoreCase);
}

public interface IUploadProgress
{
    void Start(long totalBytes);

    /// <summary>Bytes of the file sent so far; moves backwards when a retry resumes from Drive's committed offset.</summary>
    void Position(long bytes);

    void Event(string message);
}

/// <summary>
/// Drive v3 resumable upload over one stream: chunks are sent strictly in order, and after any
/// failure Drive is asked for its committed offset before continuing.
/// </summary>
public sealed class ResumableUploader(
    HttpClient http,
    IAccessTokenSource tokens,
    SessionStore sessions,
    IUploadProgress progress,
    string uploadBase = DriveClient.DefaultApiBase,
    Func<TimeSpan, CancellationToken, Task>? delay = null)
{
    private const double MaxBackoffSeconds = 64;
    private readonly Func<TimeSpan, CancellationToken, Task> _delay = delay ?? Task.Delay;

    public async Task<UploadResult> UploadAsync(UploadRequest request, CancellationToken ct)
    {
        var file = new FileInfo(request.FilePath);
        if (!file.Exists) throw new FileNotFoundException($"File not found: {file.FullName}", file.FullName);
        var size = file.Length;
        var mtime = file.LastWriteTimeUtc.Ticks;
        var key = SessionStore.KeyFor(file.FullName, size, mtime, request.RemoteName, request.FolderId, request.Name);
        sessions.DeleteOtherVersions(key, file.FullName, request.RemoteName, request.FolderId, request.Name);

        using var hashCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var md5Task = ComputeMd5Async(file.FullName, hashCts.Token);
        progress.Start(size);

        var sessionUri = sessions.Load(key)?.SessionUri;
        var attempt = 0;
        DriveFile? done = null;
        try
        {
            while (done is null)
            {
                try
                {
                    Step step;
                    if (sessionUri is null)
                    {
                        sessionUri = await StartSessionAsync(request, size, ct);
                        sessions.Save(key, new SessionState(sessionUri, file.FullName, size, mtime, request.RemoteName,
                            request.FolderId, request.Name, request.ChunkSize, DateTimeOffset.UtcNow));
                        step = Step.At(0);
                    }
                    else
                    {
                        step = await QueryAsync(sessionUri, size, request.StallTimeout, ct);
                    }

                    while (step is { Completed: null, Expired: false })
                    {
                        progress.Position(step.Offset);
                        var length = Math.Min(request.ChunkSize, size - step.Offset);
                        step = await SendChunkAsync(sessionUri, file.FullName, step.Offset, length, size, request.StallTimeout, ct);
                        attempt = 0;
                    }

                    if (step.Expired)
                    {
                        progress.Event("Drive discarded the upload session; starting a new one from the beginning.");
                        sessions.Delete(key);
                        sessionUri = null;
                        continue;
                    }

                    done = step.Completed;
                }
                catch (Exception ex) when (!ct.IsCancellationRequested && IsTransient(ex))
                {
                    attempt++;
                    var wait = Backoff(attempt);
                    progress.Event($"Retry {attempt} in {wait.TotalSeconds:0.#}s: {ex.Message}");
                    await _delay(wait, ct);
                }
            }

            progress.Position(size);
            sessions.Delete(key);
            string? localMd5;
            try
            {
                localMd5 = await md5Task;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                progress.Event($"Could not compute the local MD5 ({ex.Message}); checksum not verified.");
                localMd5 = null;
            }
            return new UploadResult(done.Id, done.Name, done.Size >= 0 ? done.Size : size, done.Md5, localMd5);
        }
        finally
        {
            await hashCts.CancelAsync();
        }
    }

    private async Task<string> StartSessionAsync(UploadRequest request, long size, CancellationToken ct)
    {
        var url = $"{uploadBase}/upload/drive/v3/files?uploadType=resumable&supportsAllDrives=true&fields=id,name,size,md5Checksum";
        var metadata = JsonSerializer.Serialize(new { name = request.Name, parents = new[] { request.FolderId } });
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(request.StallTimeout);

        using var response = await DriveHttp.SendAsync(http, tokens, () =>
        {
            var message = new HttpRequestMessage(HttpMethod.Post, url)
            {
                Content = new StringContent(metadata, Encoding.UTF8, "application/json"),
            };
            message.Headers.Add("X-Upload-Content-Type", ContentTypes.For(request.Name));
            message.Headers.Add("X-Upload-Content-Length", size.ToString(CultureInfo.InvariantCulture));
            return message;
        }, HttpCompletionOption.ResponseContentRead, timeout.Token);

        if (!response.IsSuccessStatusCode) throw await DriveApiException.FromResponseAsync(response, ct);
        return response.Headers.Location?.AbsoluteUri
            ?? throw new DriveApiException((int)response.StatusCode, "noLocation", "Drive did not return an upload session URI.");
    }

    private async Task<Step> QueryAsync(string sessionUri, long size, TimeSpan stallTimeout, CancellationToken ct)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(stallTimeout);
        using var response = await DriveHttp.SendAsync(http, tokens, () =>
        {
            var content = new ByteArrayContent([]);
            content.Headers.ContentRange = new ContentRangeHeaderValue(size); // bytes */size
            return new HttpRequestMessage(HttpMethod.Put, sessionUri) { Content = content };
        }, HttpCompletionOption.ResponseContentRead, timeout.Token);
        return await InterpretAsync(response, ct);
    }

    private async Task<Step> SendChunkAsync(string sessionUri, string path, long offset, long length, long size,
        TimeSpan stallTimeout, CancellationToken ct)
    {
        using var stallCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var clock = new ActivityClock();
        var responseTimeout = ResponseTimeoutFor(stallTimeout);
        var watchdog = WatchAsync(clock, stallTimeout, responseTimeout, stallCts);
        try
        {
            using var response = await DriveHttp.SendAsync(http, tokens, () =>
            {
                // Reset for each attempt: the 401-resend path invokes this factory again.
                clock.Reset();
                if (length == 0) // zero-byte file: an empty PUT completes the upload
                {
                    clock.MarkBodySent();
                    return new HttpRequestMessage(HttpMethod.Put, sessionUri) { Content = new ByteArrayContent([]) };
                }

                var position = offset;
                var content = new FileRangeContent(path, offset, length, sent =>
                {
                    clock.Touch();
                    position += sent;
                    progress.Position(position);
                    if (position >= offset + length)
                        clock.MarkBodySent();
                });
                content.Headers.ContentRange = new ContentRangeHeaderValue(offset, offset + length - 1, size);
                return new HttpRequestMessage(HttpMethod.Put, sessionUri) { Content = content };
            }, HttpCompletionOption.ResponseContentRead, stallCts.Token);
            return await InterpretAsync(response, ct);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            throw clock.BodySent
                ? new TimeoutException($"No response from Drive for {responseTimeout.TotalSeconds:0.#} s after sending a chunk; reconnecting.")
                : new TimeoutException($"No upload progress for {stallTimeout.TotalSeconds:0.#} s; reconnecting.");
        }
        finally
        {
            await stallCts.CancelAsync();
            await watchdog;
        }
    }

    /// <summary>How long to wait for Drive's reply once a chunk's body has been fully written.</summary>
    internal static TimeSpan ResponseTimeoutFor(TimeSpan stallTimeout) =>
        TimeSpan.FromSeconds(Math.Max(300, 5 * stallTimeout.TotalSeconds));

    private static async Task<Step> InterpretAsync(HttpResponseMessage response, CancellationToken ct)
    {
        switch ((int)response.StatusCode)
        {
            case 200 or 201:
                return Step.Done(await ParseFileAsync(response, ct));
            case 308:
                return Step.At(ResumeOffset.FromResponse(response));
            case 404 or 410:
                return Step.Gone;
            default:
                throw await DriveApiException.FromResponseAsync(response, ct);
        }
    }

    private static async Task<DriveFile> ParseFileAsync(HttpResponseMessage response, CancellationToken ct)
    {
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
        var r = doc.RootElement;
        return new DriveFile(
            r.GetProperty("id").GetString()!,
            r.TryGetProperty("name", out var n) ? n.GetString() ?? "" : "",
            r.TryGetProperty("size", out var s) && long.TryParse(s.GetString(), out var parsed) ? parsed : -1,
            r.TryGetProperty("md5Checksum", out var m) ? m.GetString() : null);
    }

    private static bool IsTransient(Exception ex)
    {
        for (var e = ex; e is not null; e = e.InnerException)
        {
            if (e is LocalFileChangedException or FileNotFoundException or DirectoryNotFoundException or UnauthorizedAccessException)
                return false;
        }
        return ex switch
        {
            DriveApiException api => api.IsTransient,
            HttpRequestException or IOException or TimeoutException or OperationCanceledException => true,
            _ => false,
        };
    }

    private static TimeSpan Backoff(int attempt)
    {
        var cap = Math.Min(MaxBackoffSeconds, Math.Pow(2, attempt));
        return TimeSpan.FromSeconds(cap / 2 + Random.Shared.NextDouble() * cap / 2);
    }

    private static Task<string> ComputeMd5Async(string path, CancellationToken ct) => Task.Run(async () =>
    {
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite,
            FileRangeContent.BufferSize, FileOptions.Asynchronous | FileOptions.SequentialScan);
        return Convert.ToHexStringLower(await MD5.HashDataAsync(stream, ct));
    }, ct);

    private static async Task WatchAsync(ActivityClock clock, TimeSpan stallTimeout, TimeSpan responseTimeout,
        CancellationTokenSource stallCts)
    {
        var tick = TimeSpan.FromMilliseconds(Math.Clamp(stallTimeout.TotalMilliseconds / 4, 10, 1000));
        try
        {
            while (!stallCts.IsCancellationRequested)
            {
                await Task.Delay(tick, stallCts.Token);
                var timedOut = clock.BodySent ? clock.SinceBodySent > responseTimeout : clock.Idle > stallTimeout;
                if (timedOut)
                {
                    await stallCts.CancelAsync();
                    return;
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Chunk finished or was cancelled.
        }
    }

    private sealed class ActivityClock
    {
        private long _last = Stopwatch.GetTimestamp();
        private long _bodySent;

        public TimeSpan Idle => Stopwatch.GetElapsedTime(Interlocked.Read(ref _last));

        public bool BodySent => Interlocked.Read(ref _bodySent) != 0;

        public TimeSpan SinceBodySent => Stopwatch.GetElapsedTime(Interlocked.Read(ref _bodySent));

        public void Touch() => Interlocked.Exchange(ref _last, Stopwatch.GetTimestamp());

        public void MarkBodySent() => Interlocked.Exchange(ref _bodySent, Stopwatch.GetTimestamp());

        public void Reset()
        {
            Touch();
            Interlocked.Exchange(ref _bodySent, 0);
        }
    }

    private sealed record DriveFile(string Id, string Name, long Size, string? Md5);

    private readonly record struct Step(long Offset, DriveFile? Completed, bool Expired)
    {
        public static readonly Step Gone = new(0, null, true);

        public static Step At(long offset) => new(offset, null, false);

        public static Step Done(DriveFile file) => new(0, file, false);
    }
}
