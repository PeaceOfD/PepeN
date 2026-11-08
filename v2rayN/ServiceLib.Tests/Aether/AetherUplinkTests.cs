using ServiceLib.Tests.CoreConfig;
using TUnit.Assertions.Enums;
using Win = ServiceLib.Aether.AetherWindowsUplinkReader;

namespace ServiceLib.Tests.Aether;

/// <summary>
/// PattN (D2): the uplink the short-lived Xray processes of Aether bind their outbounds to while a TUN runs, as each
/// system lists its default routes: Windows' forwarding table, Linux's /proc/net/route and ipv6_route, macOS' scutil and
/// route. No test changes or needs a route of this machine.
/// </summary>
public class AetherUplinkTests
{
    private static bool NotMacTun(string name)
    {
        return AetherUplink.IsTun(name, isMacOS: false);
    }

    private static bool MacTun(string name)
    {
        return AetherUplink.IsTun(name, isMacOS: true);
    }

    private static AetherWindowsRouteRow V4(string name, uint routeMetric, uint interfaceMetric, bool up = true)
    {
        return new AetherWindowsRouteRow(name, up, IPv6: false, routeMetric, interfaceMetric);
    }

    private static AetherWindowsRouteRow V6(string name, uint routeMetric, uint interfaceMetric, bool up = true)
    {
        return new AetherWindowsRouteRow(name, up, IPv6: true, routeMetric, interfaceMetric);
    }

    private static string? Windows(params AetherWindowsRouteRow[] rows)
    {
        return AetherUplink.Select(AetherUplink.WindowsTiers(rows), NotMacTun);
    }

    // ---- Windows

    [Test]
    public async Task OfTwoWindowsDefaultRoutesTheLowestRouteAndInterfaceMetricWins()
    {
        // The effective metric is the route's plus its interface's: Ethernet (10 + 25) beats Wi-Fi (0 + 50), whose route
        // metric alone is lower.
        await Windows(V4("Wi-Fi", 0, 50), V4("Ethernet", 10, 25)).Should().BeEqualTo("Ethernet");
        await Windows(V4("Ethernet", 10, 25), V4("Wi-Fi", 0, 50)).Should().BeEqualTo("Ethernet");
        await Windows(V4("Wi-Fi", 0, 20), V4("Ethernet", 10, 25)).Should().BeEqualTo("Wi-Fi");
        // A tie goes to the route listed first.
        await Windows(V4("Ethernet 2", 0, 25), V4("Ethernet", 0, 25)).Should().BeEqualTo("Ethernet 2");
        // Metrics as large as Windows can store add up without overflowing.
        await Windows(V4("Wi-Fi", uint.MaxValue, uint.MaxValue), V4("Ethernet", uint.MaxValue, uint.MaxValue - 1)).Should().BeEqualTo("Ethernet");
    }

    [Test]
    public async Task TheTunsDefaultRouteIsLeftOutHoweverLowItsMetric()
    {
        // Xray's TUN and sing-box's put their default route in with the lowest metric; the uplink is what lies beneath.
        await Windows(V4("xray_tun", 0, 5), V4("Ethernet", 0, 25), V4("singbox_tun", 0, 0)).Should().BeEqualTo("Ethernet");
        await Windows(V4("SingBox_Tun", 0, 0), V4("Wi-Fi", 0, 50)).Should().BeEqualTo("Wi-Fi");
        await Windows(V4("wintunsingbox_tun", 0, 0), V4("Wi-Fi", 0, 50)).Should().BeEqualTo("Wi-Fi");
        // Only the TUN: no uplink.
        await Windows(V4("xray_tun", 0, 5)).Should().BeNull();
    }

    [Test]
    public async Task AWindowsInterfaceThatIsNotUpIsLeftOut()
    {
        await Windows(V4("Ethernet", 0, 5, up: false), V4("Wi-Fi", 0, 50)).Should().BeEqualTo("Wi-Fi");
        await Windows(V4("Ethernet", 0, 5, up: false)).Should().BeNull();
    }

    [Test]
    public async Task WithoutAUsableIPv4DefaultRouteTheIPv6OneIsTheUplink()
    {
        // IPv4 first, whatever the metrics of the IPv6 routes.
        await Windows(V6("Wi-Fi", 0, 5), V4("Ethernet", 256, 25)).Should().BeEqualTo("Ethernet");
        // No IPv4 default route but the TUN's and one that is down: the IPv6 one with the lowest effective metric.
        await Windows(V4("xray_tun", 0, 5), V4("Ethernet", 0, 25, up: false), V6("xray_tun", 0, 5), V6("Cellular", 256, 90), V6("Wi-Fi", 256, 45))
            .Should().BeEqualTo("Wi-Fi");
        // No default route at all.
        await Windows().Should().BeNull();
    }

    [Test]
    public async Task TheTiersAfterTheFirstWithAUsableRouteAreNotRead()
    {
        // The macOS reader runs a tool for every tier; one that is not needed is never run.
        static IEnumerable<IReadOnlyList<AetherUplinkRoute>> Tiers()
        {
            yield return [new AetherUplinkRoute("utun4", 0)];
            yield return [new AetherUplinkRoute("en0", 0)];
            throw new InvalidOperationException("the third tier must not be read");
        }

        await AetherUplink.Select(Tiers(), MacTun).Should().BeEqualTo("en0");
    }

    // ---- Linux

    private const string ProcNetRoute =
        "Iface\tDestination\tGateway \tFlags\tRefCnt\tUse\tMetric\tMask\t\tMTU\tWindow\tIRTT                                                       \n"
        + "xray_tun\t00000000\t00000000\t0001\t0\t0\t1\t00000000\t0\t0\t0                                                                               \n"
        + "wlan0\t00000000\t0101A8C0\t0003\t0\t0\t600\t00000000\t0\t0\t0                                                                             \n"
        + "eth0\t00000000\t0102A8C0\t0003\t0\t0\t100\t00000000\t0\t0\t0                                                                               \n"
        + "eth0\t0002A8C0\t00000000\t0001\t0\t0\t100\t00FFFFFF\t0\t0\t0                                                                               \n";

    private const string ProcNetIpv6Route =
        "fe800000000000000000000000000000 40 00000000000000000000000000000000 00 00000000000000000000000000000000 00000100 00000001 00000000 00000001     eth0\n"
        + "00000000000000000000000000000000 00 00000000000000000000000000000000 00 fe800000000000000000000000000001 00000400 00000001 00000000 00450003     eth0\n"
        + "00000000000000000000000000000000 00 00000000000000000000000000000000 00 00000000000000000000000000000000 ffffffff 00000001 00000000 00200200       lo\n";

    private static string? Linux(string ipv4, string ipv6)
    {
        return AetherUplink.Select([AetherUplink.ParseLinuxIPv4Routes(ipv4), AetherUplink.ParseLinuxIPv6Routes(ipv6)], NotMacTun);
    }

    [Test]
    public async Task ProcNetRouteGivesTheDefaultRoutesWithTheirDecimalMetrics()
    {
        var routes = AetherUplink.ParseLinuxIPv4Routes(ProcNetRoute);

        // The header and the route to the local network are no default routes.
        await routes.Should().BeEquivalentTo(
            [new AetherUplinkRoute("xray_tun", 1), new AetherUplinkRoute("wlan0", 600), new AetherUplinkRoute("eth0", 100)],
            CollectionOrdering.Matching);
    }

    [Test]
    public async Task OnLinuxTheTunsDefaultRouteIsLeftOutAndTheLowestMetricWins()
    {
        // xray_tun, metric 1, is Xray's TUN; eth0 (100) beats wlan0 (600) though it is listed after it.
        await Linux(ProcNetRoute, ProcNetIpv6Route).Should().BeEqualTo("eth0");
        await Linux(ProcNetRoute.Replace("\t100\t00000000", "\t700\t00000000"), string.Empty).Should().BeEqualTo("wlan0");
    }

    [Test]
    public async Task ALinuxRouteThatIsNotUpOrRejectsIsNoUplink()
    {
        const string header = "Iface\tDestination\tGateway \tFlags\tRefCnt\tUse\tMetric\tMask\t\tMTU\tWindow\tIRTT\n";
        // eth0 without RTF_UP, an unreachable default route (RTF_UP | RTF_REJECT) on no device, and one on wlan0.
        var routes = header
                     + "eth0\t00000000\t0102A8C0\t0002\t0\t0\t100\t00000000\t0\t0\t0\n"
                     + "*\t00000000\t00000000\t0201\t0\t0\t0\t00000000\t0\t0\t0\n"
                     + "wlan1\t00000000\t00000000\t0201\t0\t0\t0\t00000000\t0\t0\t0\n"
                     + "wlan0\t00000000\t0101A8C0\t0003\t0\t0\t600\t00000000\t0\t0\t0\n";

        await Linux(routes, string.Empty).Should().BeEqualTo("wlan0");
    }

    [Test]
    public async Task WithoutAnIPv4DefaultRouteLinuxFallsBackToTheIPv6One()
    {
        const string noDefault = "Iface\tDestination\tGateway \tFlags\tRefCnt\tUse\tMetric\tMask\t\tMTU\tWindow\tIRTT\n"
                                 + "eth0\t0002A8C0\t00000000\t0001\t0\t0\t100\t00FFFFFF\t0\t0\t0\n";

        await AetherUplink.ParseLinuxIPv6Routes(ProcNetIpv6Route).Should().BeEquivalentTo(
            [new AetherUplinkRoute("eth0", 0x400)], CollectionOrdering.Matching);
        await Linux(noDefault, ProcNetIpv6Route).Should().BeEqualTo("eth0");
        // The kernel's unreachable default route on lo is no uplink: no default route at all.
        await Linux(noDefault, ProcNetIpv6Route.Split('\n')[2]).Should().BeNull();
        await Linux(string.Empty, string.Empty).Should().BeNull();
    }

    // ---- macOS

    private const string ScutilIPv4 =
        "<dictionary> {\n"
        + "  ARPResolvedHardwareAddress : 3c:22:fb:00:00:01\n"
        + "  ARPResolvedIPAddress : 192.168.1.1\n"
        + "  AdditionalRoutes : <array> {\n"
        + "    0 : <dictionary> {\n"
        + "      DestinationAddress : 192.168.1.20\n"
        + "      SubnetMask : 255.255.255.255\n"
        + "    }\n"
        + "  }\n"
        + "  PrimaryInterface : en0\n"
        + "  PrimaryService : 2F3B6C9D-0000-4A1B-9C2D-ABCDEF012345\n"
        + "  Router : 192.168.1.1\n"
        + "}\n";

    private const string RouteGetDefault =
        "   route to: default\n"
        + "destination: default\n"
        + "       mask: default\n"
        + "    gateway: 192.168.1.1\n"
        + "  interface: en1\n"
        + "      flags: <UP,GATEWAY,DONE,STATIC,PRCLONING,GLOBAL>\n"
        + " recvpipe  sendpipe  ssthresh  rtt,msec    rttvar  hopcount      mtu     expire\n"
        + "       0         0         0         0         0         0      1500         0\n";

    [Test]
    public async Task ScutilAndRouteNameTheInterfaceTheyWrite()
    {
        await AetherUplink.ParseScutilPrimaryInterface(ScutilIPv4).Should().BeEqualTo("en0");
        await AetherUplink.ParseScutilPrimaryInterface(ScutilIPv4.Replace("\n", "\r\n")).Should().BeEqualTo("en0");
        await AetherUplink.ParseScutilPrimaryInterface("  No such key\n").Should().BeNull();
        await AetherUplink.ParseScutilPrimaryInterface(null).Should().BeNull();
        await AetherUplink.ParseRouteGetInterface(RouteGetDefault).Should().BeEqualTo("en1");
        await AetherUplink.ParseRouteGetInterface("route: writing to routing socket: not in table\n").Should().BeNull();
        await AetherUplink.ParseRouteGetInterface(null).Should().BeNull();
    }

    [Test]
    public async Task OnMacOSAUtunIsTheTunAndTheNextSourceIsAsked()
    {
        static List<AetherUplinkRoute> Of(string? name)
        {
            return name == null ? [] : [new AetherUplinkRoute(name, 0)];
        }

        string? Mac(string? ipv4, string? ipv6, string? route)
        {
            return AetherUplink.Select(
                [Of(AetherUplink.ParseScutilPrimaryInterface(ipv4)), Of(AetherUplink.ParseScutilPrimaryInterface(ipv6)), Of(AetherUplink.ParseRouteGetInterface(route))],
                MacTun);
        }

        // The primary IPv4 interface, when it is no TUN.
        await Mac(ScutilIPv4, null, RouteGetDefault).Should().BeEqualTo("en0");
        // The TUN made itself primary: the primary IPv6 interface, then the default route's.
        var tunPrimary = ScutilIPv4.Replace("PrimaryInterface : en0", "PrimaryInterface : utun4");
        await Mac(tunPrimary, ScutilIPv4.Replace("en0", "en2"), RouteGetDefault).Should().BeEqualTo("en2");
        await Mac(tunPrimary, "  No such key\n", RouteGetDefault).Should().BeEqualTo("en1");
        await Mac(tunPrimary, tunPrimary, RouteGetDefault.Replace("en1", "utun5")).Should().BeNull();
        await Mac("  No such key\n", "  No such key\n", "route: writing to routing socket: not in table\n").Should().BeNull();
    }

    [Test]
    public async Task OnlyPattNsTunNamesAreTheTunAndEveryUtunOnMacOS()
    {
        foreach (var name in new[] { "xray_tun", "singbox_tun", "wintunsingbox_tun", "XRAY_TUN" })
        {
            await AetherUplink.IsTun(name, isMacOS: false).Should().BeTrue().Because(name);
            await AetherUplink.IsTun(name, isMacOS: true).Should().BeTrue().Because(name);
        }
        foreach (var name in new[] { "Ethernet", "Wi-Fi", "eth0", "wlan0", "en0", "tun0", "wg0" })
        {
            await AetherUplink.IsTun(name, isMacOS: false).Should().BeFalse().Because(name);
            await AetherUplink.IsTun(name, isMacOS: true).Should().BeFalse().Because(name);
        }
        // Xray numbers its macOS TUN at random and sing-box takes the next free one.
        await AetherUplink.IsTun("utun4", isMacOS: true).Should().BeTrue();
        await AetherUplink.IsTun("utun4", isMacOS: false).Should().BeFalse();
    }

    [Test]
    public async Task OnMacOSAnotherVpnsUtunIsNoTunOfPattNWhenTheTunsNameIsKnown()
    {
        // D2-5: Xray's own TUN names its utun in its configuration; another utun, as another VPN's, may be the way out.
        await AetherUplink.IsTun("utun42", isMacOS: true, tunInterface: "utun42").Should().BeTrue();
        await AetherUplink.IsTun("utun3", isMacOS: true, tunInterface: "utun42").Should().BeFalse();
        await AetherUplink.IsTun("xray_tun", isMacOS: true, tunInterface: "utun42").Should().BeTrue();
        // sing-box's TUN names none: every utun may be it.
        await AetherUplink.IsTun("utun3", isMacOS: true, tunInterface: null).Should().BeTrue();

        // The other VPN's utun3 is the primary interface, Xray's TUN utun42 beside it.
        var vpnPrimary = ScutilIPv4.Replace("PrimaryInterface : en0", "PrimaryInterface : utun3");
        IEnumerable<IReadOnlyList<AetherUplinkRoute>> Tiers()
        {
            yield return [new AetherUplinkRoute(AetherUplink.ParseScutilPrimaryInterface(vpnPrimary)!, 0)];
            yield return [new AetherUplinkRoute("en0", 0)];
        }

        await AetherUplink.Select(Tiers(), name => AetherUplink.IsTun(name, isMacOS: true, tunInterface: "utun42")).Should().BeEqualTo("utun3");
        await AetherUplink.Select(Tiers(), name => AetherUplink.IsTun(name, isMacOS: true)).Should().BeEqualTo("en0");
    }

    [Test]
    public async Task TheTunAConfigurationOpensIsReadFromIt()
    {
        // As CoreManager reads it from the configuration it starts: Xray's tun inbound, or sing-box's.
        await AetherUplink.TunInterfaceOf("""{"inbounds": [{"protocol": "mixed", "port": 10808}, {"protocol": "tun", "settings": {"name": "utun42"}}]}""")
            .Should().BeEqualTo("utun42");
        await AetherUplink.TunInterfaceOf("""{"inbounds": [{"type": "tun", "interface_name": "singbox_tun"}]}""").Should().BeEqualTo("singbox_tun");
        // sing-box on macOS names none; no TUN, or no configuration to read, names none either.
        await AetherUplink.TunInterfaceOf("""{"inbounds": [{"type": "tun"}]}""").Should().BeNull();
        await AetherUplink.TunInterfaceOf("""{"inbounds": [{"protocol": "mixed", "port": 10808}]}""").Should().BeNull();
        await AetherUplink.TunInterfaceOf("not json").Should().BeNull();
        await AetherUplink.TunInterfaceOf(null).Should().BeNull();
    }

    [Test]
    public async Task XraysOwnTunIsNamedInTheConfigurationOfTheSession()
    {
        // The name TunInterfaceOf reads is the one the generation gives Xray's TUN: a random utun on macOS.
        var config = CoreConfigTestFactory.CreateConfigWithTun(ECoreType.Xray, enableIPv6Address: false);
        CoreConfigTestFactory.BindAppManagerConfig(config);
        var node = CoreConfigTestFactory.CreateVmessNode(ECoreType.Xray);
        foreach (var macOS in new[] { true, false })
        {
            var context = CoreConfigTestFactory.CreateContext(config, node, ECoreType.Xray) with { IsMacOS = macOS, IsWindows = !macOS };
            var generated = new CoreConfigV2rayService(context).GenerateClientConfigContent();
            await generated.Success.Should().BeTrue().Because(generated.Msg ?? string.Empty);

            var name = AetherUplink.TunInterfaceOf(generated.Data!.ToString());

            if (macOS)
            {
                await (name?.StartsWith("utun", StringComparison.Ordinal) == true).Should().BeTrue().Because(name ?? "none");
            }
            else
            {
                await name.Should().BeEqualTo("xray_tun");
            }
        }
    }

    // ---- the Windows structures (netioapi.h, ws2ipdef.h; the same on x64 and arm64)

    [Test]
    public async Task TheWindowsRouteStructuresHaveTheLayoutOfTheWindowsSdk()
    {
        // The sizes and offsets of the Windows SDK's netioapi.h, as wireguard-windows' winipcfg pins them too.
        await Marshal.SizeOf<Win.SockaddrInet>().Should().BeEqualTo(28);
        await Marshal.SizeOf<Win.IpAddressPrefix>().Should().BeEqualTo(32);
        await Marshal.OffsetOf<Win.IpAddressPrefix>(nameof(Win.IpAddressPrefix.PrefixLength)).ToInt32().Should().BeEqualTo(28);

        await Marshal.SizeOf<Win.MibIpForwardRow2>().Should().BeEqualTo(104);
        async Task Row(string field, int offset)
        {
            await Marshal.OffsetOf<Win.MibIpForwardRow2>(field).ToInt32().Should().BeEqualTo(offset).Because(field);
        }

        await Row(nameof(Win.MibIpForwardRow2.InterfaceLuid), 0);
        await Row(nameof(Win.MibIpForwardRow2.InterfaceIndex), 8);
        await Row(nameof(Win.MibIpForwardRow2.DestinationPrefix), 12);
        await Row(nameof(Win.MibIpForwardRow2.NextHop), 44);
        await Row(nameof(Win.MibIpForwardRow2.SitePrefixLength), 72);
        await Row(nameof(Win.MibIpForwardRow2.ValidLifetime), 76);
        await Row(nameof(Win.MibIpForwardRow2.PreferredLifetime), 80);
        await Row(nameof(Win.MibIpForwardRow2.Metric), 84);
        await Row(nameof(Win.MibIpForwardRow2.Protocol), 88);
        await Row(nameof(Win.MibIpForwardRow2.Loopback), 92);
        await Row(nameof(Win.MibIpForwardRow2.Immortal), 95);
        await Row(nameof(Win.MibIpForwardRow2.Age), 96);
        await Row(nameof(Win.MibIpForwardRow2.Origin), 100);
        await Marshal.OffsetOf<Win.MibIpForwardTable2>(nameof(Win.MibIpForwardTable2.Table)).ToInt32().Should().BeEqualTo(8);

        await Marshal.SizeOf<Win.MibIpInterfaceRow>().Should().BeEqualTo(168);
        async Task Interface(string field, int offset)
        {
            await Marshal.OffsetOf<Win.MibIpInterfaceRow>(field).ToInt32().Should().BeEqualTo(offset).Because(field);
        }

        await Interface(nameof(Win.MibIpInterfaceRow.Family), 0);
        await Interface(nameof(Win.MibIpInterfaceRow.InterfaceLuid), 8);
        await Interface(nameof(Win.MibIpInterfaceRow.InterfaceIndex), 16);
        await Interface(nameof(Win.MibIpInterfaceRow.InterfaceIdentifier), 24);
        await Interface(nameof(Win.MibIpInterfaceRow.AdvertisingEnabled), 40);
        await Interface(nameof(Win.MibIpInterfaceRow.AdvertiseDefaultRoute), 48);
        await Interface(nameof(Win.MibIpInterfaceRow.RouterDiscoveryBehavior), 52);
        await Interface(nameof(Win.MibIpInterfaceRow.LinkLocalAddressBehavior), 72);
        await Interface(nameof(Win.MibIpInterfaceRow.ZoneIndices), 80);
        await Interface(nameof(Win.MibIpInterfaceRow.SitePrefixLength), 144);
        await Interface(nameof(Win.MibIpInterfaceRow.Metric), 148);
        await Interface(nameof(Win.MibIpInterfaceRow.NlMtu), 152);
        await Interface(nameof(Win.MibIpInterfaceRow.Connected), 156);
        await Interface(nameof(Win.MibIpInterfaceRow.ReachableTime), 160);
        await Interface(nameof(Win.MibIpInterfaceRow.TransmitOffload), 164);
        await Interface(nameof(Win.MibIpInterfaceRow.DisableDefaultRoutes), 166);
    }

    [Test]
    public async Task TheReaderOfThisSystemFindsAnInterfaceThatIsThereOrNone()
    {
        // Whatever routes this machine has, none on a CI machine among them: no failure, and a name only of an interface
        // that exists.
        var uplink = AetherUplink.Find();

        if (uplink != null)
        {
            await NetworkInterface.GetAllNetworkInterfaces().Select(networkInterface => networkInterface.Name).Should().Contain(uplink);
        }
    }

    [Test]
    public async Task TheReaderOfThisSystemListsTheDefaultRouteOfAnInterfaceWithAGateway()
    {
        // D2-6: checked against what the system says itself. An interface that is up, no TUN, and has an IPv4 gateway has
        // an IPv4 default route, which the reader of Windows (iphlpapi) or Linux (/proc/net/route) lists, read directly
        // so that a failure shows, and the uplink is found. A machine without such an interface has nothing to check.
        IAetherUplinkReader reader;
        if (OperatingSystem.IsWindows())
        {
            reader = new AetherWindowsUplinkReader();
        }
        else if (OperatingSystem.IsLinux())
        {
            reader = new AetherLinuxUplinkReader();
        }
        else
        {
            return;
        }
        static bool HasIPv4Gateway(NetworkInterface networkInterface)
        {
            try
            {
                return networkInterface.GetIPProperties().GatewayAddresses
                    .Any(gateway => gateway.Address.AddressFamily == AddressFamily.InterNetwork && !gateway.Address.Equals(IPAddress.Any));
            }
            catch (NetworkInformationException)
            {
                return false;
            }
        }

        var withGateway = NetworkInterface.GetAllNetworkInterfaces()
            .Where(networkInterface => networkInterface.OperationalStatus == OperationalStatus.Up && !AetherUplink.IsTun(networkInterface.Name, isMacOS: false))
            .Where(HasIPv4Gateway)
            .Select(networkInterface => networkInterface.Name)
            .ToList();
        if (withGateway.Count == 0)
        {
            return;
        }

        var ipv4 = reader.DefaultRoutes().First();

        await ipv4.Any(route => route.Up && withGateway.Contains(route.Interface)).Should().BeTrue()
            .Because($"gateways on {string.Join(", ", withGateway)}; IPv4 default routes {string.Join(", ", ipv4.Select(route => $"{route.Interface} ({route.Metric}, {(route.Up ? "up" : "down")})"))}");
        await AetherUplink.Find().Should().NotBeNull();
    }

    // ---- when the binding applies

    private static CoreConfigContext Context(string? bindInterface = null)
    {
        var config = CoreConfigTestFactory.CreateConfigWithTun(ECoreType.Xray, enableIPv6Address: false);
        config.CoreBasicItem.BindInterface = bindInterface;
        return CoreConfigTestFactory.CreateContext(config, AetherExitHost.ContextProfile(), ECoreType.Xray);
    }

    [Test]
    public async Task WhileATunRunsWithNoBindInterfaceSetTheUplinkIsFoundAndBound()
    {
        var finds = 0;
        var log = new List<string>();
        var binding = new AetherUplinkBinding
        {
            TunRunning = () => true,
            FindUplink = () =>
            {
                finds++;
                return "Wi-Fi";
            },
            Log = log.Add,
        };
        var context = Context(bindInterface: " ");

        await binding.Apply(context, AetherUplinkBinding.ExitRole).Should().BeEqualTo("Wi-Fi");

        await context.Aether.Uplink.Should().BeEqualTo("Wi-Fi");
        await finds.Should().BeEqualTo(1);
        await log.Should().BeEmpty();
        // Found anew for every run: the network may have changed.
        await binding.Apply(Context(), AetherUplinkBinding.TestRole).Should().BeEqualTo("Wi-Fi");
        await finds.Should().BeEqualTo(2);
        // A generation forgets what it built, never the uplink, which comes from outside it.
        context.Aether.Reset();
        await context.Aether.Uplink.Should().BeEqualTo("Wi-Fi");
    }

    [Test]
    public async Task TheBindInterfaceOfTheSettingsWinsAndWithoutATunNothingIsBound()
    {
        var finds = 0;
        var log = new List<string>();
        AetherUplinkBinding Binding(bool tun)
        {
            return new AetherUplinkBinding
            {
                TunRunning = () => tun,
                FindUplink = () =>
                {
                    finds++;
                    return "Wi-Fi";
                },
                Log = log.Add,
            };
        }

        var bound = Context(bindInterface: "Ethernet 2");
        await Binding(tun: true).Apply(bound, AetherUplinkBinding.ExitRole).Should().BeNull();
        await bound.Aether.Uplink.Should().BeNull();

        var noTun = Context();
        await Binding(tun: false).Apply(noTun, AetherUplinkBinding.TestRole).Should().BeNull();
        await noTun.Aether.Uplink.Should().BeNull();

        // Neither looked for an uplink, nor said anything.
        await finds.Should().BeEqualTo(0);
        await log.Should().BeEmpty();
    }

    [Test]
    public async Task WithNoUplinkTheXrayRunsUnboundAndALineSaysSo()
    {
        var log = new List<string>();
        var binding = new AetherUplinkBinding { TunRunning = () => true, FindUplink = () => null, Log = log.Add };
        var exit = Context();
        var test = Context();

        await binding.Apply(exit, AetherUplinkBinding.ExitRole).Should().BeNull();
        await binding.Apply(test, AetherUplinkBinding.TestRole).Should().BeNull();

        await exit.Aether.Uplink.Should().BeNull();
        await test.Aether.Uplink.Should().BeNull();
        await log.Should().BeEquivalentTo(
            [
                "AetherCore: no network interface outside the TUN was found; the exit Xray runs unbound",
                "AetherCore: no network interface outside the TUN was found; the test Xray runs unbound",
            ],
            CollectionOrdering.Matching);
    }

    // ---- E5: Linux before 5.7, where only a process with CAP_NET_RAW may bind to an interface

    [Test]
    public async Task TheReleaseOfALinuxKernelIsReadUpToItsMinorVersion()
    {
        await AetherUplink.ParseKernelRelease("5.4.0-150-generic").Should().BeEqualTo(new Version(5, 4));
        await AetherUplink.ParseKernelRelease("6.8.0").Should().BeEqualTo(new Version(6, 8));
        await AetherUplink.ParseKernelRelease("4.19.113+").Should().BeEqualTo(new Version(4, 19));
        await AetherUplink.ParseKernelRelease("5.10.0-rc1").Should().BeEqualTo(new Version(5, 10));
        await AetherUplink.ParseKernelRelease("5.7").Should().BeEqualTo(new Version(5, 7));
        await AetherUplink.ParseKernelRelease("6.6.87.2-microsoft-standard-WSL2").Should().BeEqualTo(new Version(6, 6));
        await AetherUplink.ParseKernelRelease("3.10.0-1160.el7.x86_64\n").Should().BeEqualTo(new Version(3, 10));
        await AetherUplink.ParseKernelRelease(" 5.15.0-1-amd64 ").Should().BeEqualTo(new Version(5, 15));
        foreach (var text in new[] { null, "", " ", "linux", "5", "5.", ".7", "v5.4", "99999999999.1", "5.x" })
        {
            await AetherUplink.ParseKernelRelease(text).Should().BeNull().Because($"\"{text}\"");
        }
    }

    [Test]
    public async Task AKernelBefore57BindsNothingAndOneThatCannotBeReadDoes()
    {
        foreach (var release in new[] { "5.4.0-150-generic", "4.19.113+", "5.6.19", "3.10.0-1160.el7.x86_64", "2.6.32" })
        {
            await AetherUplink.KernelBindsToInterface(release).Should().BeFalse().Because(release);
        }
        foreach (var release in new[] { "5.7.0", "5.7", "5.10.0-rc1", "6.8.0", "6.6.87.2-microsoft-standard-WSL2", "10.0" })
        {
            await AetherUplink.KernelBindsToInterface(release).Should().BeTrue().Because(release);
        }
        // A release that cannot be read binds, as every kernel since 2020 can.
        await AetherUplink.KernelBindsToInterface(null).Should().BeTrue();
        await AetherUplink.KernelBindsToInterface("unknown").Should().BeTrue();
        // Only Linux has such a kernel; this system's answer, whatever it is, is the same at every call, and PattN's own
        // binding asks it.
        if (!OperatingSystem.IsLinux())
        {
            await AetherUplink.SystemBindsToInterface().Should().BeTrue();
        }
        await AetherUplink.SystemBindsToInterface().Should().BeEqualTo(AetherUplink.SystemBindsToInterface());
        await AetherUplinkBinding.Default.CanBind().Should().BeEqualTo(AetherUplink.SystemBindsToInterface());
        // E5-1: and asks it through the system's own answer, which the line above cannot tell from "always" off an old
        // Linux kernel, where every runner of these tests is.
        await AetherUplinkBinding.Default.CanBind.Method.Should().BeEqualTo(typeof(AetherUplink).GetMethod(nameof(AetherUplink.SystemBindsToInterface)));
    }

    [Test]
    public async Task WhereTheKernelCannotBindTheXrayRunsUnboundAndALineSaysSoOnceForEachRole()
    {
        var finds = 0;
        var log = new List<string>();
        var binding = new AetherUplinkBinding
        {
            TunRunning = () => true,
            FindUplink = () =>
            {
                finds++;
                return "eth0";
            },
            CanBind = () => false,
            Log = log.Add,
        };
        var contexts = new[] { Context(), Context(), Context() };

        await binding.Apply(contexts[0], AetherUplinkBinding.ExitRole).Should().BeNull();
        await binding.Apply(contexts[1], AetherUplinkBinding.ExitRole).Should().BeNull();
        await binding.Apply(contexts[2], AetherUplinkBinding.TestRole).Should().BeNull();

        foreach (var context in contexts)
        {
            await context.Aether.Uplink.Should().BeNull();
        }
        // No uplink looked for, and one line for each role.
        await finds.Should().BeEqualTo(0);
        await log.Should().BeEquivalentTo(
            [
                "AetherCore: this kernel needs CAP_NET_RAW to bind to an interface; the exit Xray runs unbound",
                "AetherCore: this kernel needs CAP_NET_RAW to bind to an interface; the test Xray runs unbound",
            ],
            CollectionOrdering.Matching);
    }

    [Test]
    public async Task TheKernelIsAskedOnlyWhereTheUplinkWouldBeBound()
    {
        // The bind interface of the settings, or no TUN: nothing to bind, so nothing about the kernel either.
        var asked = 0;
        var log = new List<string>();
        AetherUplinkBinding Binding(bool tun)
        {
            return new AetherUplinkBinding
            {
                TunRunning = () => tun,
                FindUplink = () => "eth0",
                CanBind = () =>
                {
                    asked++;
                    return false;
                },
                Log = log.Add,
            };
        }

        await Binding(tun: true).Apply(Context(bindInterface: "eth1"), AetherUplinkBinding.ExitRole).Should().BeNull();
        await Binding(tun: false).Apply(Context(), AetherUplinkBinding.TestRole).Should().BeNull();
        await asked.Should().BeEqualTo(0);
        await log.Should().BeEmpty();

        // A kernel that can bind changes nothing.
        var bound = Context();
        await new AetherUplinkBinding { TunRunning = () => true, FindUplink = () => "eth0", CanBind = () => true, Log = log.Add }
            .Apply(bound, AetherUplinkBinding.TestRole).Should().BeEqualTo("eth0");
        await bound.Aether.Uplink.Should().BeEqualTo("eth0");
        await log.Should().BeEmpty();
    }
}
