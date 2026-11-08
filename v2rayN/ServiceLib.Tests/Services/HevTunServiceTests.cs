namespace ServiceLib.Tests.Services;

public class HevTunServiceTests
{
    [Test]
    public async Task BuildsIPv4ConfigurationForTheLocalSocksListener()
    {
        var actual = HevTunService.BuildConfig(10808, 8500, "198.18.0.1", null);
        var expected = string.Join(Environment.NewLine,
        [
            "tunnel:",
            "  name: PepeNTun",
            "  mtu: 8500",
            "  multi-queue: false",
            "  ipv4: '198.18.0.1'",
            "  icmp: 'off'",
            "socks5:",
            "  port: 10808",
            "  address: 127.0.0.1",
            "  udp: 'udp'",
            "  log-level: info",
            "",
        ]);
        await actual.Should().BeEqualTo(expected);
    }

    [Test]
    public async Task IncludesIPv6OnlyWhenAValidAddressIsProvided()
    {
        var actual = HevTunService.BuildConfig(10809, 1500, "198.18.0.1", "fc00::1");
        await actual.Should().BeEqualTo(string.Join(Environment.NewLine,
        [
            "tunnel:",
            "  name: PepeNTun",
            "  mtu: 1500",
            "  multi-queue: false",
            "  ipv4: '198.18.0.1'",
            "  icmp: 'off'",
            "  ipv6: 'fc00::1'",
            "socks5:",
            "  port: 10809",
            "  address: 127.0.0.1",
            "  udp: 'udp'",
            "  log-level: info",
            "",
        ]));
    }

    [Test]
    public async Task RejectsInvalidSocksPortMtuAndTunnelAddresses()
    {
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(
            () => Task.Run(() => HevTunService.BuildConfig(0, 1500, "198.18.0.1", null)));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(
            () => Task.Run(() => HevTunService.BuildConfig(10808, 500, "198.18.0.1", null)));
        await Assert.ThrowsAsync<ArgumentException>(
            () => Task.Run(() => HevTunService.BuildConfig(10808, 1500, "not-an-ip", null)));
        await Assert.ThrowsAsync<ArgumentException>(
            () => Task.Run(() => HevTunService.BuildConfig(10808, 1500, "198.18.0.1", "not-ipv6")));
    }
}
