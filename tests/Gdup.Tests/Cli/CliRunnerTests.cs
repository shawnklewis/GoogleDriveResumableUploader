using Gdup.Cli;
using Gdup.Config;
using Gdup.Drive;
using Gdup.Net;

namespace Gdup.Tests.Cli;

public class CliRunnerTests
{
    [Fact]
    public async Task Success_ReturnsBodyExitCode()
    {
        Assert.Equal(3, await CliRunner.RunAsync(() => Task.FromResult(3), CancellationToken.None, new StringWriter()));
    }

    [Fact]
    public async Task UsageAndNicErrors_MapToExitCode1_WithoutStackTrace()
    {
        var error = new StringWriter();
        Assert.Equal(ExitCodes.Usage, await CliRunner.RunAsync(() => throw new UsageException("bad args"), CancellationToken.None, error));
        Assert.Equal(ExitCodes.Usage, await CliRunner.RunAsync(() => throw new NicNotFoundException("no nic"), CancellationToken.None, error));
        Assert.Equal($"error: bad args{Environment.NewLine}error: no nic{Environment.NewLine}", error.ToString());
    }

    [Fact]
    public async Task ConfigCorruptException_MapsToExitCode1()
    {
        var error = new StringWriter();
        var code = await CliRunner.RunAsync(
            () => throw new ConfigCorruptException("cannot read secrets", new FormatException("bad base64")),
            CancellationToken.None, error);

        Assert.Equal(ExitCodes.Usage, code);
        Assert.Equal($"error: cannot read secrets{Environment.NewLine}", error.ToString());
    }

    [Fact]
    public async Task OtherErrors_MapToExitCode2_WithInnerMessages()
    {
        var error = new StringWriter();
        var code = await CliRunner.RunAsync(
            () => throw new HttpRequestException("send failed", new IOException("disk gone")),
            CancellationToken.None, error);

        Assert.Equal(ExitCodes.Failed, code);
        Assert.Contains("send failed -> disk gone", error.ToString());
        Assert.Equal(ExitCodes.Failed, await CliRunner.RunAsync(
            () => throw new DriveApiException(403, "storageQuotaExceeded", "quota"), CancellationToken.None, error));
    }

    [Fact]
    public async Task Cancellation_MapsToExitCode2_WithResumeHint()
    {
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();
        var error = new StringWriter();

        var code = await CliRunner.RunAsync(() => throw new OperationCanceledException(cts.Token), cts.Token, error);

        Assert.Equal(ExitCodes.Failed, code);
        Assert.Contains("gdup resume", error.ToString());
    }

    [Fact]
    public async Task Cancellation_TreatsAnyExceptionAsCancellation_WhenTokenIsCancelled()
    {
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();
        var error = new StringWriter();

        var code = await CliRunner.RunAsync(() => throw new IOException("socket aborted"), cts.Token, error);

        Assert.Equal(ExitCodes.Failed, code);
        Assert.Contains("Cancelled", error.ToString());
    }
}
