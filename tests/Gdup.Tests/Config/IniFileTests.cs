using Gdup.Config;

namespace Gdup.Tests.Config;

public class IniFileTests
{
    [Fact]
    public void Parse_ReadsSectionsAndKeys_IgnoringCommentsAndBlankLines()
    {
        var ini = IniFile.Parse("# comment\r\n[gdrive]\r\ntype = drive\r\nclient_id=abc\r\n\r\n; other\r\n[second]\nkey = a=b\n");

        Assert.Equal(new[] { "gdrive", "second" }, ini.Sections.Select(s => s.Name));
        Assert.Equal("drive", ini.Get("gdrive")!["type"]);
        Assert.Equal("abc", ini.Get("GDRIVE")!["CLIENT_ID"]);
        Assert.Equal("a=b", ini.Get("second")!["key"]);
    }

    [Fact]
    public void ToString_RoundTripsUnknownKeysInOrder()
    {
        const string text = "[r]\ntype = drive\ncustom_thing = 42\n\n[s]\nx = 1\n\n";

        var ini = IniFile.Parse(text);
        ini.Get("r")!["type"] = "drive";

        Assert.Equal(text, ini.ToString());
    }

    [Fact]
    public void Indexer_SetNullRemovesKey_AndRemoveDeletesSection()
    {
        var ini = IniFile.Parse("[a]\nk = v\nj = w\n[b]\nz = 1\n");

        ini.Get("a")!["k"] = null;
        ini.GetOrAdd("c")["n"] = "new";

        Assert.True(ini.Remove("B"));
        Assert.False(ini.Remove("missing"));
        Assert.Equal("[a]\nj = w\n\n[c]\nn = new\n\n", ini.ToString());
    }
}
