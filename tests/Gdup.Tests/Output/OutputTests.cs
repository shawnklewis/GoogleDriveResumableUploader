using Gdup.Output;

namespace Gdup.Tests.Output;

public class OutputTests
{
    [Theory]
    [InlineData(0, "0 B")]
    [InlineData(999, "999 B")]
    [InlineData(1500, "1.5 KB")]
    [InlineData(87_400_000, "87.4 MB")]
    [InlineData(211_600_000_000, "211.6 GB")]
    [InlineData(2_500_000_000_000, "2.5 TB")]
    public void Format_Bytes(double bytes, string expected) => Assert.Equal(expected, Format.Bytes(bytes));

    [Fact]
    public void Format_RateAndDuration()
    {
        Assert.Equal("87.4 MB/s", Format.Rate(87_400_000));
        Assert.Equal("00:56:12", Format.Duration(new TimeSpan(0, 56, 12)));
        Assert.Equal("26:00:05", Format.Duration(new TimeSpan(1, 2, 0, 5)));
    }

    [Fact]
    public void RateWindow_AveragesOverTheWindow_AndResets()
    {
        var w = new RateWindow(TimeSpan.FromSeconds(5));
        Assert.Equal(0, w.BytesPerSecond);

        w.Add(0, 0);
        w.Add(1, 100);
        w.Add(2, 200);
        Assert.Equal(100, w.BytesPerSecond);

        w.Add(10, 1200); // drops samples older than 5 s but keeps two
        Assert.Equal(125, w.BytesPerSecond);

        w.Reset();
        Assert.Equal(0, w.BytesPerSecond);
    }

    [Fact]
    public void ProgressReporter_NonInteractive_WritesLinesAndEvents()
    {
        var output = new StringWriter();
        var reporter = new ProgressReporter("big.mkv", "Ethernet 2 (192.168.2.10)", output, interactive: false);

        reporter.Start(1000);
        reporter.Position(500);
        reporter.Event("Retry 1 in 2s: boom");

        var text = output.ToString();
        Assert.Contains("big.mkv  50.0%  500 B/1.0 KB", text);
        Assert.Contains("via Ethernet 2 (192.168.2.10)", text);
        Assert.Contains("Retry 1 in 2s: boom" + Environment.NewLine, text);
        Assert.DoesNotContain("\r", text.Replace(Environment.NewLine, ""));
    }

    [Fact]
    public void ProgressReporter_Interactive_RedrawsInPlace_AndCompleteEndsTheLine()
    {
        var output = new StringWriter();
        var reporter = new ProgressReporter("f.bin", "", output, interactive: true);

        reporter.Start(100);
        reporter.Position(10);
        reporter.Complete();

        var text = output.ToString();
        Assert.StartsWith("\rf.bin  10.0%", text);
        Assert.EndsWith(Environment.NewLine, text);
        Assert.DoesNotContain("via", text);
    }
}
