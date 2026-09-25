using System.Net;
using Gdup.Drive;

namespace Gdup.Tests.Drive;

public class DriveClientTests
{
    private static Task<HttpResponseMessage> Ok(string json) => Task.FromResult(StubHandler.Json(HttpStatusCode.OK, json));

    [Fact]
    public async Task ResolveFolder_FindsExistingSegments_AndCreatesMissingOnes()
    {
        var handler = new StubHandler((req, _) =>
        {
            var url = Uri.UnescapeDataString(req.RequestUri!.AbsoluteUri);
            if (req.Method == HttpMethod.Get && url.Contains("name = 'Backups' and 'root' in parents"))
                return Ok("""{"files":[{"id":"backups-id"}]}""");
            if (req.Method == HttpMethod.Get)
                return Ok("""{"files":[]}""");
            return Ok("""{"id":"new-id"}""");
        });
        var drive = new DriveClient(new HttpClient(handler), new FakeTokens(), "https://fake.local");

        var id = await drive.ResolveFolderAsync("", "Backups/2026", CancellationToken.None);

        Assert.Equal("new-id", id);
        Assert.Equal(3, handler.Requests.Count);
        Assert.Equal(HttpMethod.Post, handler.Requests[2].Method);
        Assert.Contains("\"name\":\"2026\"", handler.Bodies[2]);
        Assert.Contains("\"parents\":[\"backups-id\"]", handler.Bodies[2]);
        Assert.Contains("application/vnd.google-apps.folder", handler.Bodies[2]);
        Assert.All(handler.Requests, r => Assert.Equal("Bearer tok-1", r.Headers.Authorization!.ToString()));
    }

    [Fact]
    public async Task ResolveFolder_EmptyPath_ReturnsRootWithoutCallingDrive()
    {
        var handler = new StubHandler((_, _) => throw new InvalidOperationException("no request expected"));
        var drive = new DriveClient(new HttpClient(handler), new FakeTokens(), "https://fake.local");

        Assert.Equal("0ABC", await drive.ResolveFolderAsync("0ABC", "", CancellationToken.None));
        Assert.Equal("root", await drive.ResolveFolderAsync("", "/", CancellationToken.None));
    }

    [Fact]
    public async Task Unauthorized_RefreshesTokenOnce_AndRetries()
    {
        var calls = 0;
        var handler = new StubHandler((_, _) => ++calls == 1
            ? Task.FromResult(new HttpResponseMessage(HttpStatusCode.Unauthorized))
            : Ok("""{"files":[{"id":"x-id"}]}"""));
        var tokens = new FakeTokens();
        var drive = new DriveClient(new HttpClient(handler), tokens, "https://fake.local");

        Assert.Equal("x-id", await drive.ResolveFolderAsync("", "X", CancellationToken.None));
        Assert.Equal(1, tokens.Refreshes);
        Assert.Equal("Bearer tok-2", handler.Requests[1].Headers.Authorization!.ToString());
    }

    [Fact]
    public async Task ApiError_ThrowsDriveApiExceptionWithReason()
    {
        var handler = new StubHandler((_, _) => Task.FromResult(StubHandler.Json(HttpStatusCode.Forbidden,
            """{"error":{"code":403,"message":"The user's Drive storage quota has been exceeded.","errors":[{"reason":"storageQuotaExceeded"}]}}""")));
        var drive = new DriveClient(new HttpClient(handler), new FakeTokens(), "https://fake.local");

        var ex = await Assert.ThrowsAsync<DriveApiException>(() => drive.ResolveFolderAsync("", "X", CancellationToken.None));

        Assert.Equal(403, ex.StatusCode);
        Assert.Equal("storageQuotaExceeded", ex.Reason);
        Assert.False(ex.IsTransient);
        Assert.Contains("storage quota", ex.Message);
    }

    [Theory]
    [InlineData(503, "", true)]
    [InlineData(500, "backendError", true)]
    [InlineData(429, "", true)]
    [InlineData(403, "userRateLimitExceeded", true)]
    [InlineData(403, "rateLimitExceeded", true)]
    [InlineData(403, "storageQuotaExceeded", false)]
    [InlineData(404, "notFound", false)]
    public void IsTransient_ClassifiesStatusAndReason(int status, string reason, bool expected)
    {
        Assert.Equal(expected, new DriveApiException(status, reason, "m").IsTransient);
    }

    [Fact]
    public void Escape_QuotesAndBackslashes()
    {
        Assert.Equal(@"it\'s a \\ test", DriveClient.Escape(@"it's a \ test"));
    }

    private static Task NoDelay(TimeSpan _, CancellationToken __) => Task.CompletedTask;

    [Fact]
    public async Task TransientStatus_IsRetried_AndEventuallySucceeds()
    {
        var calls = 0;
        var handler = new StubHandler((_, _) => ++calls == 1
            ? Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable))
            : Ok("""{"files":[{"id":"x-id"}]}"""));
        var drive = new DriveClient(new HttpClient(handler), new FakeTokens(), "https://fake.local", delay: NoDelay);

        var id = await drive.ResolveFolderAsync("", "X", CancellationToken.None);

        Assert.Equal("x-id", id);
        Assert.Equal(2, handler.Requests.Count);
    }

    [Fact]
    public async Task FiveConsecutive503s_ThrowsDriveApiException()
    {
        var handler = new StubHandler((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)));
        var drive = new DriveClient(new HttpClient(handler), new FakeTokens(), "https://fake.local", delay: NoDelay);

        var ex = await Assert.ThrowsAsync<DriveApiException>(() => drive.ResolveFolderAsync("", "X", CancellationToken.None));

        Assert.Equal(503, ex.StatusCode);
        Assert.Equal(5, handler.Requests.Count);
    }

    [Fact]
    public async Task RequestNeverAnswers_EventuallyThrowsTimeoutException()
    {
        var handler = new StubHandler(async (_, ct) => { await Task.Delay(Timeout.Infinite, ct); return null!; });
        var drive = new DriveClient(new HttpClient(handler), new FakeTokens(), "https://fake.local",
            requestTimeout: TimeSpan.FromMilliseconds(100), delay: NoDelay);

        await Assert.ThrowsAsync<TimeoutException>(() => drive.ResolveFolderAsync("", "X", CancellationToken.None));
    }

    [Fact]
    public async Task StorageQuotaExceeded_IsNotRetried()
    {
        var handler = new StubHandler((_, _) => Task.FromResult(StubHandler.Json(HttpStatusCode.Forbidden,
            """{"error":{"code":403,"message":"Quota exceeded","errors":[{"reason":"storageQuotaExceeded"}]}}""")));
        var drive = new DriveClient(new HttpClient(handler), new FakeTokens(), "https://fake.local", delay: NoDelay);

        var ex = await Assert.ThrowsAsync<DriveApiException>(() => drive.ResolveFolderAsync("", "X", CancellationToken.None));

        Assert.Equal("storageQuotaExceeded", ex.Reason);
        Assert.Single(handler.Requests);
    }
}
