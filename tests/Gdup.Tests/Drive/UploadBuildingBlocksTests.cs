using System.Net;
using Gdup.Drive;

namespace Gdup.Tests.Drive;

public class UploadBuildingBlocksTests
{
    [Theory]
    [InlineData(null, 0L)]
    [InlineData("", 0L)]
    [InlineData("bytes=0-0", 1L)]
    [InlineData("bytes=0-41", 42L)]
    [InlineData("bytes=0-268435455", 268435456L)]
    public void ResumeOffset_Parse(string? header, long expected)
    {
        Assert.Equal(expected, ResumeOffset.Parse(header));
    }

    [Fact]
    public void ResumeOffset_Parse_RejectsGarbage()
    {
        Assert.Throws<FormatException>(() => ResumeOffset.Parse("items=0-5"));
        Assert.Throws<FormatException>(() => ResumeOffset.Parse("bytes=0-x"));
    }

    [Theory]
    [InlineData(0.3, 300)]
    [InlineData(120, 600)]
    public void ResponseTimeoutFor_IsAtLeastFiveMinutes_OrFiveTimesStall(double stallSeconds, double expectedSeconds)
    {
        var actual = ResumableUploader.ResponseTimeoutFor(TimeSpan.FromSeconds(stallSeconds));
        Assert.Equal(TimeSpan.FromSeconds(expectedSeconds), actual);
    }

    [Fact]
    public void ResumeOffset_FromResponse_ReadsRangeHeaderOf308()
    {
        var response = new HttpResponseMessage((HttpStatusCode)308);
        Assert.Equal(0, ResumeOffset.FromResponse(response));
        response.Headers.TryAddWithoutValidation("Range", "bytes=0-99");
        Assert.Equal(100, ResumeOffset.FromResponse(response));
    }

    [Fact]
    public async Task FileRangeContent_StreamsExactlyTheRange_AndReportsProgress()
    {
        using var dir = new TempDir();
        var data = new byte[10_000_000];
        new Random(42).NextBytes(data);
        var path = dir.File("data.bin");
        File.WriteAllBytes(path, data);
        long reported = 0;

        using var content = new FileRangeContent(path, 3, 9_000_000, n => reported += n);
        var bytes = await content.ReadAsByteArrayAsync();

        Assert.Equal(9_000_000, content.Headers.ContentLength);
        Assert.Equal(data.AsSpan(3, 9_000_000).ToArray(), bytes);
        Assert.Equal(9_000_000, reported);
    }

    [Fact]
    public async Task FileRangeContent_FileShorterThanRange_Throws()
    {
        using var dir = new TempDir();
        var path = dir.File("short.bin");
        File.WriteAllBytes(path, new byte[100]);

        using var content = new FileRangeContent(path, 50, 100, _ => { });

        await Assert.ThrowsAnyAsync<LocalFileChangedException>(() => content.ReadAsByteArrayAsync());
    }

    [Fact]
    public void UploadResult_Md5Matches_NullWhenEitherChecksumMissing()
    {
        Assert.Null(new UploadResult("id", "n", 1, "abc", null).Md5Matches);
        Assert.Null(new UploadResult("id", "n", 1, null, "abc").Md5Matches);
    }

    [Fact]
    public void UploadResult_Md5Matches_TrueWhenEqualIgnoringCase()
    {
        Assert.True(new UploadResult("id", "n", 1, "ABC123", "abc123").Md5Matches);
    }

    [Theory]
    [InlineData("movie.MKV", "video/x-matroska")]
    [InlineData("a.mp4", "video/mp4")]
    [InlineData("backup.7z", "application/x-7z-compressed")]
    [InlineData("noext", "application/octet-stream")]
    [InlineData("weird.xyz", "application/octet-stream")]
    public void ContentTypes_MapsExtensions(string name, string expected)
    {
        Assert.Equal(expected, ContentTypes.For(name));
    }
}
