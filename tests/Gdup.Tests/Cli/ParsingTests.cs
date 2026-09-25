using Gdup.Cli;

namespace Gdup.Tests.Cli;

public class ParsingTests
{
    [Theory]
    [InlineData("256M", 268_435_456L)]
    [InlineData("256m", 268_435_456L)]
    [InlineData("256MB", 268_435_456L)]
    [InlineData("512K", 524_288L)]
    [InlineData("1G", 1_073_741_824L)]
    [InlineData("4G", 4_294_967_296L)]
    [InlineData("262144", 262_144L)]
    public void ParseChunkSize_Valid(string text, long expected) => Assert.Equal(expected, SizeParser.ParseChunkSize(text));

    [Theory]
    [InlineData("0")]
    [InlineData("100K")]
    [InlineData("5G")]
    [InlineData("99999999999G")]
    [InlineData("abc")]
    [InlineData("12X")]
    [InlineData("-256M")]
    [InlineData("")]
    public void ParseChunkSize_Invalid_ThrowsUsage(string text) =>
        Assert.Throws<UsageException>(() => SizeParser.ParseChunkSize(text));

    [Theory]
    [InlineData("gdrive:Backups/2026", "gdrive", "Backups/2026")]
    [InlineData("gdrive:", "gdrive", "")]
    [InlineData("gdrive:/a/b/", "gdrive", "a/b")]
    public void RemotePath_Parse(string text, string remote, string path)
    {
        Assert.Equal(new RemotePath(remote, path), RemotePath.Parse(text));
    }

    [Theory]
    [InlineData("nocolon")]
    [InlineData(":x")]
    public void RemotePath_Invalid_ThrowsUsage(string text) =>
        Assert.Throws<UsageException>(() => RemotePath.Parse(text));
}
