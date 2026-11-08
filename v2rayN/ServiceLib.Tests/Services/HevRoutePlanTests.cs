namespace ServiceLib.Tests.Services;

public class HevRoutePlanTests
{
    [Test]
    public async Task AddsPhysicalUpstreamBypassesBeforeTunnelDefaultRoutes()
    {
        var plan = HevRoutePlan.Create(
            tunnelInterfaceIndex: 77,
            physicalDefaults:
            [
                new(12, IPAddress.Parse("192.0.2.1"), 10, 20),
                new(13, IPAddress.Parse("198.51.100.1"), 5, 5),
            ],
            upstreamAddresses: [IPAddress.Parse("203.0.113.20")],
            includeIpv6: false);

        await plan.Count.Should().BeEqualTo(2);
        await plan[0].IsUpstreamBypass.Should().BeTrue();
        await plan[0].DestinationPrefix.Should().BeEqualTo("203.0.113.20/32");
        await plan[0].InterfaceIndex.Should().BeEqualTo(13);
        await plan[0].NextHop.Should().BeEqualTo(IPAddress.Parse("198.51.100.1"));
        await plan[1].IsUpstreamBypass.Should().BeFalse();
        await plan[1].DestinationPrefix.Should().BeEqualTo("0.0.0.0/0");
        await plan[1].InterfaceIndex.Should().BeEqualTo(77);
    }

    [Test]
    public async Task PlansIpv4AndIpv6BypassesAndTunnelDefaults()
    {
        var plan = HevRoutePlan.Create(
            tunnelInterfaceIndex: 77,
            physicalDefaults:
            [
                new(12, IPAddress.Parse("192.0.2.1"), 10, 10),
                new(12, IPAddress.Parse("fe80::1"), 10, 10),
            ],
            upstreamAddresses:
            [
                IPAddress.Parse("203.0.113.20"),
                IPAddress.Parse("2001:db8::20"),
            ],
            includeIpv6: true);

        await plan.Count.Should().BeEqualTo(4);
        await plan[0].DestinationPrefix.Should().BeEqualTo("203.0.113.20/32");
        await plan[1].DestinationPrefix.Should().BeEqualTo("2001:db8::20/128");
        await plan[2].DestinationPrefix.Should().BeEqualTo("0.0.0.0/0");
        await plan[3].DestinationPrefix.Should().BeEqualTo("::/0");
        await plan[3].NextHop.Should().BeEqualTo(IPAddress.IPv6Any);
    }

    [Test]
    public async Task AllowsIpv6TunnelDefaultWhenUpstreamEndpointIsIpv4Only()
    {
        var plan = HevRoutePlan.Create(
            tunnelInterfaceIndex: 77,
            physicalDefaults:
            [
                new(12, IPAddress.Parse("192.0.2.1"), 10, 10),
                new(12, IPAddress.Parse("fe80::1"), 10, 10),
            ],
            upstreamAddresses: [IPAddress.Parse("203.0.113.20")],
            includeIpv6: true);

        await plan.Count.Should().BeEqualTo(3);
        await plan[0].DestinationPrefix.Should().BeEqualTo("203.0.113.20/32");
        await plan[1].DestinationPrefix.Should().BeEqualTo("0.0.0.0/0");
        await plan[2].DestinationPrefix.Should().BeEqualTo("::/0");
        await plan[2].InterfaceIndex.Should().BeEqualTo(77);
    }

    [Test]
    public async Task RefusesToBuildPlanWithoutAResolvedUpstreamAddress()
    {
        var threw = Throws<ArgumentException>(() => HevRoutePlan.Create(
            tunnelInterfaceIndex: 77,
            physicalDefaults: [new(12, IPAddress.Parse("192.0.2.1"), 10, 10)],
            upstreamAddresses: [IPAddress.Loopback],
            includeIpv6: false));

        await threw.Should().BeTrue();
    }

    private static bool Throws<TException>(Action action) where TException : Exception
    {
        try
        {
            action();
            return false;
        }
        catch (TException)
        {
            return true;
        }
    }
}
