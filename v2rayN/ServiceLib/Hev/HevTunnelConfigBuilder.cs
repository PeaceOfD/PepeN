using System.Net;
using System.Net.Sockets;
using YamlDotNet.Serialization;

namespace ServiceLib.Hev;

/// <summary>
/// The settings needed to connect HevSocks5Tunnel to a local SOCKS5 listener.
/// </summary>
public sealed record HevTunnelOptions(
    string InterfaceName,
    string? InterfaceGuid,
    int Mtu,
    string IPv4Address,
    string? IPv6Address,
    string Socks5Address,
    int Socks5Port,
    bool ReplyToIcmp = false,
    string UdpMode = "udp");

/// <summary>
/// Builds the YAML configuration consumed by the upstream hev-socks5-tunnel executable.
/// This class deliberately does not change routes or start processes; those operations must be
/// coordinated by the Windows tunnel lifecycle service.
/// </summary>
public static class HevTunnelConfigBuilder
{
    public static string Build(HevTunnelOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        Validate(options);

        var document = new Dictionary<string, object>
        {
            ["tunnel"] = new Dictionary<string, object?>
            {
                ["name"] = options.InterfaceName,
                ["guid"] = string.IsNullOrWhiteSpace(options.InterfaceGuid) ? null : options.InterfaceGuid,
                ["mtu"] = options.Mtu,
                ["multi-queue"] = false,
                ["ipv4"] = options.IPv4Address,
                ["ipv6"] = string.IsNullOrWhiteSpace(options.IPv6Address) ? null : options.IPv6Address,
                ["icmp"] = options.ReplyToIcmp ? "reply" : "off"
            },
            ["socks5"] = new Dictionary<string, object>
            {
                ["address"] = options.Socks5Address,
                ["port"] = options.Socks5Port,
                ["udp"] = options.UdpMode
            }
        };

        return new SerializerBuilder().Build().Serialize(document);
    }

    private static void Validate(HevTunnelOptions options)
    {
        if (string.IsNullOrWhiteSpace(options.InterfaceName)
            || options.InterfaceName.Length > 64
            || options.InterfaceName.Any(char.IsControl))
        {
            throw new ArgumentException("A valid Hev tunnel interface name is required.", nameof(options));
        }

        if (!string.IsNullOrWhiteSpace(options.InterfaceGuid)
            && !Guid.TryParse(options.InterfaceGuid, out _))
        {
            throw new ArgumentException("The Hev interface GUID must be a valid GUID.", nameof(options));
        }

        if (options.Mtu is < 576 or > 9000)
        {
            throw new ArgumentOutOfRangeException(nameof(options), "The Hev MTU must be between 576 and 9000.");
        }

        if (!IPAddress.TryParse(options.IPv4Address, out var ipv4)
            || ipv4.AddressFamily != AddressFamily.InterNetwork)
        {
            throw new ArgumentException("A valid IPv4 tunnel address is required.", nameof(options));
        }

        if (!string.IsNullOrWhiteSpace(options.IPv6Address)
            && (!IPAddress.TryParse(options.IPv6Address, out var ipv6)
                || ipv6.AddressFamily != AddressFamily.InterNetworkV6))
        {
            throw new ArgumentException("The optional tunnel address must be IPv6.", nameof(options));
        }

        if (!IPAddress.TryParse(options.Socks5Address, out _))
        {
            throw new ArgumentException("Hev must connect to a resolved SOCKS5 IP address.", nameof(options));
        }

        if (options.Socks5Port is < 1 or > 65535)
        {
            throw new ArgumentOutOfRangeException(nameof(options), "The SOCKS5 port must be between 1 and 65535.");
        }

        if (options.UdpMode is not ("udp" or "tcp"))
        {
            throw new ArgumentException("The SOCKS5 UDP mode must be 'udp' or 'tcp'.", nameof(options));
        }
    }
}
