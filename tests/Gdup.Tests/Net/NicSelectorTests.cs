using System.Net;
using Gdup.Net;

namespace Gdup.Tests.Net;

public class NicSelectorTests
{
    private static readonly NicInfo Ethernet = new("Ethernet", "Intel(R) Ethernet I225-V",
        [IPAddress.Parse("fe80::1"), IPAddress.Parse("192.168.1.20")], 1_000_000_000);

    private static readonly NicInfo Ethernet2 = new("Ethernet 2", "Realtek USB 5GbE",
        [IPAddress.Parse("169.254.3.4"), IPAddress.Parse("fe80::2"), IPAddress.Parse("2001:db8::20")], 5_000_000_000);

    private static readonly NicInfo[] All = [Ethernet, Ethernet2];

    [Fact]
    public void Select_ByLocalIp_ReturnsOwningNicAndThatAddress()
    {
        var (nic, address) = NicSelector.Select("192.168.1.20", All);
        Assert.Same(Ethernet, nic);
        Assert.Equal(IPAddress.Parse("192.168.1.20"), address);
    }

    [Fact]
    public void Select_ByNameCaseInsensitive_PrefersRoutableIPv4()
    {
        var (nic, address) = NicSelector.Select("ethernet", All);
        Assert.Same(Ethernet, nic);
        Assert.Equal(IPAddress.Parse("192.168.1.20"), address);
    }

    [Fact]
    public void Select_SkipsLinkLocalAddresses_AndFallsBackToGlobalIPv6()
    {
        var (nic, address) = NicSelector.Select("Ethernet 2", All);
        Assert.Same(Ethernet2, nic);
        Assert.Equal(IPAddress.Parse("2001:db8::20"), address);
    }

    [Fact]
    public void Select_ByDescription()
    {
        Assert.Same(Ethernet2, NicSelector.Select("realtek usb 5gbe", All).Nic);
    }

    [Fact]
    public void Select_Unknown_Throws()
    {
        Assert.Throws<NicNotFoundException>(() => NicSelector.Select("Wi-Fi", All));
        Assert.Throws<NicNotFoundException>(() => NicSelector.Select("10.0.0.99", All));
    }
}
