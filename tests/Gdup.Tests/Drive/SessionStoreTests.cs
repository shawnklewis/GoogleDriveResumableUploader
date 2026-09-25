using Gdup.Drive;

namespace Gdup.Tests.Drive;

public class SessionStoreTests
{
    private static SessionState State(string uri, long mtime, DateTimeOffset created) =>
        new(uri, @"C:\data\big.mkv", 1000, mtime, "gdrive", "folder-1", "big.mkv", 262144, created);

    [Fact]
    public void KeyFor_IsStable_AndChangesWhenTheFileChanges()
    {
        var a = SessionStore.KeyFor(@"C:\data\big.mkv", 1000, 5, "gdrive", "f", "big.mkv");

        Assert.Equal(a, SessionStore.KeyFor(@"c:\DATA\big.mkv", 1000, 5, "gdrive", "f", "big.mkv"));
        Assert.NotEqual(a, SessionStore.KeyFor(@"C:\data\big.mkv", 1000, 6, "gdrive", "f", "big.mkv"));
        Assert.NotEqual(a, SessionStore.KeyFor(@"C:\data\big.mkv", 1001, 5, "gdrive", "f", "big.mkv"));
        Assert.Equal(64, a.Length);
    }

    [Fact]
    public void SaveLoadListDelete_RoundTrip()
    {
        using var dir = new TempDir();
        var store = new SessionStore(dir.File("sessions"));
        var state = State("https://upload/1", 5, DateTimeOffset.UtcNow);

        Assert.Empty(store.List());
        store.Delete("missing");

        store.Save("k1", state);
        Assert.Equal(state, store.Load("k1"));
        var (key, listed) = Assert.Single(store.List());
        Assert.Equal("k1", key);
        Assert.Equal(state, listed);

        store.Delete("k1");
        Assert.Null(store.Load("k1"));
    }

    [Fact]
    public void DeleteOtherVersions_RemovesStaleStateForSameTarget_Only()
    {
        using var dir = new TempDir();
        var store = new SessionStore(dir.Path);
        var now = DateTimeOffset.UtcNow;
        store.Save("old", State("https://upload/old", 4, now.AddDays(-1)));
        store.Save("current", State("https://upload/cur", 5, now));
        store.Save("other", State("https://upload/x", 4, now) with { FilePath = @"C:\data\other.bin", Name = "other.bin" });

        store.DeleteOtherVersions("current", @"C:\data\big.mkv", "gdrive", "folder-1", "big.mkv");

        Assert.Equal(new[] { "current", "other" }, store.List().Select(e => e.Key).Order());
    }

    [Fact]
    public void Load_CorruptStateFile_ReturnsNull()
    {
        using var dir = new TempDir();
        var store = new SessionStore(dir.File("sessions"));
        var corruptPath = Path.Combine(dir.File("sessions"), "bad.json");
        Directory.CreateDirectory(Path.GetDirectoryName(corruptPath)!);
        File.WriteAllText(corruptPath, "{ not json");

        var result = store.Load("bad");

        Assert.Null(result);
    }
}
