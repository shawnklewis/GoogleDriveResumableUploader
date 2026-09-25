using Gdup.Auth;
using Gdup.Config;

namespace Gdup.Tests.Config;

public class ConfigStoreTests
{
    private static readonly OAuthToken Token =
        new("ya29.access", "Bearer", "1//refresh", new DateTimeOffset(2026, 9, 25, 12, 0, 0, TimeSpan.Zero));

    [Fact]
    public void Dpapi_RoundTripsAndPassesPlainValuesThrough()
    {
        var stored = Dpapi.Protect("secret-value");

        Assert.StartsWith(Dpapi.Prefix, stored);
        Assert.DoesNotContain("secret-value", stored);
        Assert.Equal("secret-value", Dpapi.Unprotect(stored));
        Assert.Equal("plain", Dpapi.Unprotect("plain"));
    }

    [Fact]
    public void SaveThenLoad_RoundTripsRemote_AndEncryptsSecretsOnDisk()
    {
        using var dir = new TempDir();
        var store = new ConfigStore(dir.File("gdup.conf"));
        var remote = new Remote("gdrive", "id.apps.googleusercontent.com", "GOCSPX-secret", "drive", "", Token);

        store.Save(remote);

        var text = File.ReadAllText(store.FilePath);
        Assert.DoesNotContain("GOCSPX-secret", text);
        Assert.DoesNotContain("1//refresh", text);
        Assert.Contains("client_id = id.apps.googleusercontent.com", text);
        Assert.Contains("type = drive", text);
        Assert.Equal(remote, store.Load("gdrive"));
        Assert.Equal(new[] { "gdrive" }, store.RemoteNames());
    }

    [Fact]
    public void Load_AcceptsPlainSecrets_AndSaveEncryptsThem()
    {
        using var dir = new TempDir();
        var path = dir.File("gdup.conf");
        File.WriteAllText(path,
            "[r]\ntype = drive\nclient_id = cid\nclient_secret = plain-secret\nscope = drive\ntoken = " + Token.ToJson() + "\n");
        var store = new ConfigStore(path);

        var loaded = store.Load("r")!;
        Assert.Equal("plain-secret", loaded.ClientSecret);
        Assert.Equal(Token, loaded.Token);

        store.Save(loaded);
        Assert.DoesNotContain("plain-secret", File.ReadAllText(path));
        Assert.Equal(loaded, store.Load("r"));
    }

    [Fact]
    public void Save_KeepsOtherRemotes_AndDeleteRemovesOnlyOne()
    {
        using var dir = new TempDir();
        var store = new ConfigStore(dir.File("sub/gdup.conf"));
        store.Save(new Remote("a", "cid-a", "sec-a", "drive", "", null));
        store.Save(new Remote("b", "cid-b", "sec-b", "drive.file", "0ABC", Token));

        Assert.True(store.Delete("a"));
        Assert.False(store.Delete("a"));
        Assert.Equal(new[] { "b" }, store.RemoteNames());
        Assert.Equal("0ABC", store.Load("b")!.RootFolderId);
    }

    [Fact]
    public void Load_MissingFileOrRemote_ReturnsNull_AndDefaultsScope()
    {
        using var dir = new TempDir();
        var store = new ConfigStore(dir.File("gdup.conf"));
        Assert.Null(store.Load("nope"));
        Assert.Empty(store.RemoteNames());

        File.WriteAllText(store.FilePath, "[x]\ntype = drive\nclient_id = c\n");
        var x = store.Load("x")!;
        Assert.Equal("drive", x.Scope);
        Assert.Null(x.Token);
        Assert.Equal("", x.ClientSecret);
    }

    [Fact]
    public void Load_UndecryptableSecret_ThrowsConfigCorrupt()
    {
        using var dir = new TempDir();
        var path = dir.File("gdup.conf");
        File.WriteAllText(path,
            "[r]\ntype = drive\nclient_id = cid\nclient_secret = dpapi:AAAA\nscope = drive\n");
        var store = new ConfigStore(path);

        var ex = Assert.Throws<ConfigCorruptException>(() => store.Load("r"));
        Assert.Contains("gdup config", ex.Message);
    }

    [Fact]
    public void Load_CorruptToken_ThrowsConfigCorrupt()
    {
        using var dir = new TempDir();
        var path = dir.File("gdup.conf");
        File.WriteAllText(path,
            "[r]\ntype = drive\nclient_id = cid\nscope = drive\ntoken = {not json\n");
        var store = new ConfigStore(path);

        var ex = Assert.Throws<ConfigCorruptException>(() => store.Load("r"));
        Assert.Contains("gdup config", ex.Message);
    }
}
