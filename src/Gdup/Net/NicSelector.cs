using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;

namespace Gdup.Net;

public sealed record NicInfo(string Name, string Description, IReadOnlyList<IPAddress> Addresses, long SpeedBitsPerSecond);

public sealed class NicNotFoundException(string message) : Exception(message);

public static class NicSelector
{
    public static IReadOnlyList<NicInfo> ListUp() =>
        NetworkInterface.GetAllNetworkInterfaces()
            .Where(n => n.OperationalStatus == OperationalStatus.Up
                && n.NetworkInterfaceType is not (NetworkInterfaceType.Loopback or NetworkInterfaceType.Tunnel))
            .Select(n => new NicInfo(
                n.Name,
                n.Description,
                n.GetIPProperties().UnicastAddresses.Select(u => u.Address).ToList(),
                n.Speed))
            .ToList();

    public static (NicInfo Nic, IPAddress Address) Select(string via, IReadOnlyList<NicInfo> nics)
    {
        if (IPAddress.TryParse(via, out var ip))
        {
            var owner = nics.FirstOrDefault(n => n.Addresses.Contains(ip))
                ?? throw new NicNotFoundException($"No active network interface has the address {via}.");
            return (owner, ip);
        }

        var nic = nics.FirstOrDefault(n => string.Equals(n.Name, via, StringComparison.OrdinalIgnoreCase))
            ?? nics.FirstOrDefault(n => string.Equals(n.Description, via, StringComparison.OrdinalIgnoreCase))
            ?? throw new NicNotFoundException($"No active network interface is named '{via}'.");

        var address = nic.Addresses.FirstOrDefault(a => a.AddressFamily == AddressFamily.InterNetwork && !IsIPv4LinkLocal(a))
            ?? nic.Addresses.FirstOrDefault(a => a.AddressFamily == AddressFamily.InterNetworkV6 && !a.IsIPv6LinkLocal)
            ?? throw new NicNotFoundException($"Interface '{nic.Name}' has no routable IPv4 or IPv6 address.");
        return (nic, address);
    }

    private static bool IsIPv4LinkLocal(IPAddress address)
    {
        var b = address.GetAddressBytes();
        return b[0] == 169 && b[1] == 254;
    }
}
