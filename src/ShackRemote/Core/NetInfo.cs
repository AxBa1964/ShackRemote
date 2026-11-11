using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;

namespace ShackRemote.Core;

public enum NetKind { LAN, WLAN, Tailscale, Sonstige }

public sealed record LocalAddress(IPAddress Address, IPAddress? Broadcast, NetKind Kind, string InterfaceName)
{
    public override string ToString() =>
        string.Equals(InterfaceName, Kind.ToString(), StringComparison.OrdinalIgnoreCase)
            ? $"{Address}   ({Kind})"
            : $"{Address}   ({Kind}, {InterfaceName})";
}

/// <summary>Ermittelt die IPv4-Adressen dieses Rechners und ordnet sie WLAN / LAN / Tailscale zu.</summary>
public static class NetInfo
{
    public static List<LocalAddress> GetAll()
    {
        var result = new List<LocalAddress>();
        foreach (var ni in NetworkInterface.GetAllNetworkInterfaces())
        {
            if (ni.OperationalStatus != OperationalStatus.Up) continue;
            if (ni.NetworkInterfaceType == NetworkInterfaceType.Loopback) continue;

            foreach (var ua in ni.GetIPProperties().UnicastAddresses)
            {
                if (ua.Address.AddressFamily != AddressFamily.InterNetwork) continue;
                var b = ua.Address.GetAddressBytes();
                if (b[0] == 169 && b[1] == 254) continue; // APIPA / ohne DHCP

                var kind = IsTailscale(b) ? NetKind.Tailscale
                         : ni.NetworkInterfaceType == NetworkInterfaceType.Wireless80211 ? NetKind.WLAN
                         : ni.NetworkInterfaceType is NetworkInterfaceType.Ethernet
                               or NetworkInterfaceType.GigabitEthernet
                               or NetworkInterfaceType.FastEthernetT
                               or NetworkInterfaceType.FastEthernetFx
                               or NetworkInterfaceType.Ethernet3Megabit ? NetKind.LAN
                         : NetKind.Sonstige;

                result.Add(new LocalAddress(ua.Address, BroadcastOf(ua.Address, ua.IPv4Mask), kind, ni.Name));
            }
        }
        return result;
    }

    /// <summary>Tailscale vergibt IPv4-Adressen aus 100.64.0.0/10.</summary>
    public static bool IsTailscale(byte[] b) => b[0] == 100 && (b[1] & 0xC0) == 64;

    private static IPAddress? BroadcastOf(IPAddress address, IPAddress? mask)
    {
        if (mask is null || mask.Equals(IPAddress.Any)) return null;
        var a = address.GetAddressBytes();
        var m = mask.GetAddressBytes();
        var r = new byte[4];
        for (int i = 0; i < 4; i++) r[i] = (byte)(a[i] | ~m[i]);
        return new IPAddress(r);
    }
}
