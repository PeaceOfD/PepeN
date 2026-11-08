namespace ServiceLib.Tests.Hev;

public class HevTunnelConfigBuilderTests
{
    [Test]
    public async Task Build_ShouldWriteTheTunnelAndLocalSocks5Settings()
    {
        var yaml = HevTunnelConfigBuilder.Build(new HevTunnelOptions(
            InterfaceName: "PepeN-TUN",
            InterfaceGuid: "12345678-9abc-def0-1234-56789abcdef0",
            Mtu: 8500,
            IPv4Address: "198.18.0.1",
            IPv6Address: "fc00::1",
            Socks5Address: "127.0.0.1",
            Socks5Port: 10808));

        await yaml.Should().Contain("name: PepeN-TUN");
        await yaml.Should().Contain("guid: 12345678-9abc-def0-1234-56789abcdef0");
        await yaml.Should().Contain("mtu: 8500");
        await yaml.Should().Contain("ipv4: 198.18.0.1");
        await yaml.Should().Contain("ipv6: fc00::1");
        await yaml.Should().Contain("address: 127.0.0.1");
        await yaml.Should().Contain("port: 10808");
        await yaml.Should().Contain("udp: udp");
        await yaml.Should().Contain("icmp: off");
    }

    [Test]
    public async Task Build_ShouldRejectAnUnresolvedSocks5Hostname()
    {
        var rejected = false;
        try
        {
            HevTunnelConfigBuilder.Build(new HevTunnelOptions(
                InterfaceName: "PepeN-TUN",
                InterfaceGuid: null,
                Mtu: 1500,
                IPv4Address: "198.18.0.1",
                IPv6Address: null,
                Socks5Address: "localhost",
                Socks5Port: 10808));
        }
        catch (ArgumentException)
        {
            rejected = true;
        }

        await rejected.Should().BeTrue();
    }

    [Test]
    public async Task Build_ShouldAllowIpv4OnlyAndIcmpReplies()
    {
        var yaml = HevTunnelConfigBuilder.Build(new HevTunnelOptions(
            InterfaceName: "PepeN-TUN",
            InterfaceGuid: null,
            Mtu: 1500,
            IPv4Address: "198.18.0.1",
            IPv6Address: null,
            Socks5Address: "127.0.0.1",
            Socks5Port: 10808,
            ReplyToIcmp: true));

        await yaml.Should().Contain("icmp: reply");
        await yaml.Should().Contain("ipv4: 198.18.0.1");
        await yaml.Should().NotContain("ipv6:");
    }
}
