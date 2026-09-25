using Gdup.Config;

namespace Gdup.Tests.Config;

public class RcloneImporterTests
{
    private const string Conf = """
        [gdrive]
        type = drive
        client_id = 123.apps.googleusercontent.com
        client_secret = GOCSPX-abc
        scope = drive
        root_folder_id = 0ABCdef
        token = {"access_token":"ya29.x","token_type":"Bearer","refresh_token":"1//r","expiry":"2026-09-25T10:11:12.123456789-05:00"}

        [s3stuff]
        type = s3
        provider = AWS

        [builtin]
        type = drive
        token = {"access_token":"ya29.y","token_type":"Bearer","refresh_token":"1//q","expiry":"2026-09-25T10:11:12Z"}
        """;

    [Fact]
    public void ReadDriveRemotes_ReturnsOnlyDriveRemotesWithTheirSettings()
    {
        var remotes = RcloneImporter.ReadDriveRemotes(Conf);

        Assert.Equal(new[] { "gdrive", "builtin" }, remotes.Select(r => r.Name));
        var g = remotes[0];
        Assert.Equal("123.apps.googleusercontent.com", g.ClientId);
        Assert.Equal("GOCSPX-abc", g.ClientSecret);
        Assert.Equal("drive", g.Scope);
        Assert.Equal("0ABCdef", g.RootFolderId);
        Assert.Equal("1//r", g.Token!.RefreshToken);
        var expected = new DateTimeOffset(2026, 9, 25, 15, 11, 12, TimeSpan.Zero);
        Assert.True(Math.Abs((g.Token.Expiry - expected).TotalSeconds) < 1);
    }

    [Fact]
    public void ReadDriveRemotes_BuiltInClient_HasEmptyClientIdAndDefaultScope()
    {
        var builtin = RcloneImporter.ReadDriveRemotes(Conf).Single(r => r.Name == "builtin");

        Assert.Equal("", builtin.ClientId);
        Assert.Equal("drive", builtin.Scope);
        Assert.Equal("ya29.y", builtin.Token!.AccessToken);
    }

    [Fact]
    public void ReadDriveRemotes_EncryptedConfig_Throws()
    {
        const string encrypted = "# Encrypted rclone configuration File\n\nRCLONE_ENCRYPT_V0:\nQUJDREVGRw==\n";

        Assert.Throws<RcloneConfigEncryptedException>(() => RcloneImporter.ReadDriveRemotes(encrypted));
    }
}
