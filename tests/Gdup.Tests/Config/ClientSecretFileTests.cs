using Gdup.Cli;
using Gdup.Config;

namespace Gdup.Tests.Config;

public class ClientSecretFileTests
{
    internal const string DesktopJson = """
        {"installed":{"client_id":"123-abc.apps.googleusercontent.com","project_id":"gdup-123456",
        "auth_uri":"https://accounts.google.com/o/oauth2/auth","token_uri":"https://oauth2.googleapis.com/token",
        "client_secret":"GOCSPX-secret","redirect_uris":["http://localhost"]}}
        """;

    private static readonly DateTimeOffset Now = new(2026, 10, 17, 12, 0, 0, TimeSpan.Zero);
    private static readonly TimeSpan MaxAge = TimeSpan.FromMinutes(30);

    [Fact]
    public void Parse_DesktopClient_ReturnsIdAndSecret()
    {
        Assert.Equal(new ClientSecretFile("123-abc.apps.googleusercontent.com", "GOCSPX-secret"), ClientSecretFile.Parse(DesktopJson));
    }

    [Fact]
    public void Parse_WebClient_SaysToCreateDesktopClient()
    {
        var ex = Assert.Throws<UsageException>(() =>
            ClientSecretFile.Parse("""{"web":{"client_id":"x","client_secret":"y"}}"""));
        Assert.Equal("This is a Web application client. Create a Desktop app client instead.", ex.Message);
    }

    [Theory]
    [InlineData("""{"installed":{"client_secret":"y"}}""", "client_id")]
    [InlineData("""{"installed":{"client_id":"","client_secret":"y"}}""", "client_id")]
    [InlineData("""{"installed":{"client_id":"x"}}""", "client_secret")]
    [InlineData("""{"installed":{"client_id":"x","client_secret":""}}""", "client_secret")]
    [InlineData("""{"installed":{"client_id":"x","client_secret":42}}""", "client_secret")]
    public void Parse_MissingOrEmptyField_NamesTheField(string json, string field)
    {
        var ex = Assert.Throws<UsageException>(() => ClientSecretFile.Parse(json));
        Assert.Contains($"\"{field}\"", ex.Message);
    }

    [Theory]
    [InlineData("{not json")]
    [InlineData("")]
    public void Parse_InvalidJson_Throws(string json)
    {
        var ex = Assert.Throws<UsageException>(() => ClientSecretFile.Parse(json));
        Assert.Contains("not valid JSON", ex.Message);
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("[]")]
    [InlineData("""{"installed":"x"}""")]
    public void Parse_WrongShape_Throws(string json) =>
        Assert.Throws<UsageException>(() => ClientSecretFile.Parse(json));

    [Fact]
    public void Load_StripsQuotesFromPastedPath_AndReportsMissingFile()
    {
        using var dir = new TempDir();
        File.WriteAllText(dir.File("client_secret_1.json"), DesktopJson);

        Assert.Equal("GOCSPX-secret", ClientSecretFile.Load($"  \"{dir.File("client_secret_1.json")}\" ").ClientSecret);
        Assert.Throws<UsageException>(() => ClientSecretFile.Load(dir.File("missing.json")));
    }

    [Fact]
    public void FindRecent_ReturnsNewestMatchingFile()
    {
        using var dir = new TempDir();
        Touch(dir.File("client_secret_old.json"), Now.AddMinutes(-20));
        Touch(dir.File("client_secret_new.json"), Now.AddMinutes(-2));
        Touch(dir.File("other.json"), Now.AddMinutes(-1));

        Assert.Equal(dir.File("client_secret_new.json"), ClientSecretFile.FindRecent(dir.Path, Now, MaxAge));
    }

    [Fact]
    public void FindRecent_IgnoresFilesOlderThanMaxAge()
    {
        using var dir = new TempDir();
        Touch(dir.File("client_secret_stale.json"), Now.AddMinutes(-31));

        Assert.Null(ClientSecretFile.FindRecent(dir.Path, Now, MaxAge));
    }

    [Fact]
    public void FindRecent_NoneFound_ReturnsNull()
    {
        using var dir = new TempDir();

        Assert.Null(ClientSecretFile.FindRecent(dir.Path, Now, MaxAge));
        Assert.Null(ClientSecretFile.FindRecent(dir.File("no-such-dir"), Now, MaxAge));
        Assert.Null(ClientSecretFile.FindRecent(null, Now, MaxAge));
    }

    [Fact]
    public void DownloadsFolder_ResolvesARootedPath()
    {
        var path = ClientSecretFile.DownloadsFolder();

        Assert.NotNull(path);
        Assert.True(Path.IsPathRooted(path));
    }

    internal static void Touch(string path, DateTimeOffset time)
    {
        File.WriteAllText(path, DesktopJson);
        File.SetCreationTimeUtc(path, time.UtcDateTime);
        File.SetLastWriteTimeUtc(path, time.UtcDateTime);
    }
}
