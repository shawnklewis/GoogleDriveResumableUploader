using Gdup.Drive;

namespace Gdup.Tests.Drive;

public class ResumableUploaderTests : IDisposable
{
    private const long Chunk = 256 * 1024;
    private readonly TempDir _dir = new();
    private readonly FakeDriveServer _server = new();
    private readonly FakeTokens _tokens = new();
    private readonly SessionStore _store;
    private readonly RecordingProgress _progress = new();

    public ResumableUploaderTests() => _store = new SessionStore(_dir.File("sessions"));

    public void Dispose() => _dir.Dispose();

    private static Task NoDelay(TimeSpan _, CancellationToken __) => Task.CompletedTask;

    private ResumableUploader Uploader() =>
        new(new HttpClient(_server, disposeHandler: false), _tokens, _store, _progress, FakeDriveServer.UploadBase, NoDelay);

    private (string Path, byte[] Data) MakeFile(int size, string name = "big.mkv")
    {
        var data = new byte[size];
        new Random(size).NextBytes(data);
        var path = _dir.File(name);
        File.WriteAllBytes(path, data);
        return (path, data);
    }

    private static UploadRequest Request(string path, double stallSeconds = 5) =>
        new(path, "gdrive", "folder-1", Path.GetFileName(path), Chunk, TimeSpan.FromSeconds(stallSeconds));

    [Fact]
    public async Task CleanUpload_SendsAllChunks_AndVerifiesMd5()
    {
        var (path, data) = MakeFile(1_300_000);

        var result = await Uploader().UploadAsync(Request(path), CancellationToken.None);

        Assert.Equal(data, _server.Committed);
        Assert.Equal("file-1", result.FileId);
        Assert.Equal(1_300_000, result.Size);
        Assert.True(result.Md5Matches);
        Assert.Equal(1, _server.SessionsStarted);
        Assert.Equal(0, _server.StatusQueries);
        Assert.Equal("big.mkv", _server.UploadedName);
        Assert.Equal("video/x-matroska", _server.UploadedContentType);
        Assert.All(_server.AuthHeaders, h => Assert.Equal("Bearer tok-1", h));
        Assert.Empty(_store.List());
        Assert.Equal(1_300_000, _progress.Total);
        Assert.Equal(1_300_000, _progress.Last);
    }

    [Fact]
    public async Task TransientStatuses_AreRetried()
    {
        var (path, data) = MakeFile(700_000);
        _server.ChunkFaults.Enqueue(Fault.Status503);
        _server.ChunkFaults.Enqueue(Fault.Status429);

        var result = await Uploader().UploadAsync(Request(path), CancellationToken.None);

        Assert.Equal(data, _server.Committed);
        Assert.True(result.Md5Matches);
        Assert.Equal(2, _progress.Events.Count(e => e.StartsWith("Retry")));
    }

    [Fact]
    public async Task PartialCommitAndConnectionReset_ResumesAtServerOffset()
    {
        var (path, data) = MakeFile(1_000_000);
        _server.ChunkFaults.Enqueue(Fault.None);
        _server.ChunkFaults.Enqueue(Fault.CommitHalfThenReset);

        var result = await Uploader().UploadAsync(Request(path), CancellationToken.None);

        Assert.Equal(data, _server.Committed);
        Assert.True(result.Md5Matches);
        Assert.True(_server.StatusQueries >= 1);
        Assert.Equal(1, _server.SessionsStarted);
    }

    [Fact]
    public async Task StalledChunk_IsCancelledByWatchdog_AndResumed()
    {
        var (path, data) = MakeFile(600_000);
        _server.ChunkFaults.Enqueue(Fault.Stall);

        var result = await Uploader().UploadAsync(Request(path, stallSeconds: 0.3), CancellationToken.None);

        Assert.Equal(data, _server.Committed);
        Assert.True(result.Md5Matches);
        Assert.Contains(_progress.Events, e => e.Contains("No upload progress"));
    }

    [Fact]
    public async Task SlowResponseAfterBody_IsNotTreatedAsStall()
    {
        var (path, data) = MakeFile(600_000);
        _server.ChunkFaults.Enqueue(Fault.SlowResponse);

        var result = await Uploader().UploadAsync(Request(path, stallSeconds: 0.3), CancellationToken.None);

        Assert.Equal(data, _server.Committed);
        Assert.True(result.Md5Matches);
        Assert.DoesNotContain(_progress.Events, e => e.StartsWith("Retry"));
    }

    [Fact]
    public async Task ExpiredSession_StartsANewSessionFromZero()
    {
        var (path, data) = MakeFile(900_000);
        _server.ChunkFaults.Enqueue(Fault.None);
        _server.ChunkFaults.Enqueue(Fault.Expire);

        var result = await Uploader().UploadAsync(Request(path), CancellationToken.None);

        Assert.Equal(data, _server.Committed);
        Assert.True(result.Md5Matches);
        Assert.Equal(2, _server.SessionsStarted);
        Assert.Contains(_progress.Events, e => e.Contains("new one"));
    }

    [Fact]
    public async Task Unauthorized_RefreshesTokenOnce_AndContinues()
    {
        var (path, data) = MakeFile(500_000);
        _server.ChunkFaults.Enqueue(Fault.Status401);

        await Uploader().UploadAsync(Request(path), CancellationToken.None);

        Assert.Equal(data, _server.Committed);
        Assert.Equal(1, _tokens.Refreshes);
        Assert.Equal("Bearer tok-2", _server.AuthHeaders[^1]);
    }

    [Fact]
    public async Task NonTransientError_IsThrown_AndSessionStateIsKept()
    {
        var (path, _) = MakeFile(500_000);
        _server.ChunkFaults.Enqueue(Fault.QuotaExceeded);

        var ex = await Assert.ThrowsAsync<DriveApiException>(() => Uploader().UploadAsync(Request(path), CancellationToken.None));

        Assert.Equal("storageQuotaExceeded", ex.Reason);
        Assert.Single(_store.List());
    }

    [Fact]
    public async Task CancelledRun_CanBeResumedByANewProcess()
    {
        var (path, data) = MakeFile(1_100_000);
        using var cts = new CancellationTokenSource();
        _server.ChunkFaults.Enqueue(Fault.None);
        _server.ChunkFaults.Enqueue(Fault.CancelClient);
        _server.OnCancelClient = cts.Cancel;

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Uploader().UploadAsync(Request(path), cts.Token));
        Assert.Single(_store.List());

        var result = await Uploader().UploadAsync(Request(path), CancellationToken.None);

        Assert.Equal(data, _server.Committed);
        Assert.True(result.Md5Matches);
        Assert.Equal(1, _server.SessionsStarted);
        Assert.True(_server.StatusQueries >= 1);
        Assert.Empty(_store.List());
    }

    [Fact]
    public async Task EmptyFile_Completes()
    {
        var (path, _) = MakeFile(0, "empty.bin");

        var result = await Uploader().UploadAsync(Request(path), CancellationToken.None);

        Assert.Equal(0, result.Size);
        Assert.True(result.Md5Matches);
        Assert.Empty(_server.Committed);
    }

    private sealed class RecordingProgress : IUploadProgress
    {
        public long Total { get; private set; }
        public long Last { get; private set; }
        public List<string> Events { get; } = [];

        public void Start(long totalBytes) => Total = totalBytes;
        public void Position(long bytes) => Last = bytes;
        public void Event(string message) => Events.Add(message);
    }
}
