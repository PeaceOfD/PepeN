using ServiceLib.Tests.Aether;
using TUnit.Assertions.Enums;
using static ServiceLib.Tests.CoreConfig.V2ray.AetherConfigTests;

namespace ServiceLib.Tests.CoreConfig.V2ray;

/// <summary>
/// PattN (D2): while a TUN runs and the settings name no bind interface, the exit Xray of a scan or a key run and the Xray
/// of a latency test bind every outbound that dials out of the machine to the uplink (sockopt.interface), their ECH
/// outbounds among them; the outbounds that reach the core on the loopback address, or carry nothing of theirs, stay as
/// they are. A bind interface of the settings wins, and without a TUN, or without an uplink, nothing changes.
/// </summary>
public class AetherUplinkConfigTests : IDisposable
{
    private const int P = AetherPorts.DefaultListenPort;
    private const string Uplink = "Wi-Fi 2";
    private const string EchOutbound = """{"tag": "ech-out", "protocol": "freedom"}""";

    private readonly string _root = Directory.CreateTempSubdirectory("pattn-aether-uplink-").FullName;

    public void Dispose()
    {
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (IOException)
        {
            // The system cleans its temp folder.
        }
    }

    private static Config TunConfig(string? bindInterface = null)
    {
        return NewConfig(config =>
        {
            config.TunModeItem.EnableTun = true;
            config.CoreBasicItem.BindInterface = bindInterface;
        });
    }

    private static AetherUplinkBinding Binding(bool tun = true, string? uplink = Uplink, List<string>? log = null)
    {
        return new AetherUplinkBinding { TunRunning = () => tun, FindUplink = () => uplink, Log = (log ?? []).Add };
    }

    /// <summary>A VLESS profile over TLS whose ECH config query goes through <paramref name="echOutbound" />.</summary>
    private static ProfileItem WithEch(ProfileItem profile, string echOutbound = EchOutbound)
    {
        profile.StreamSecurity = Global.StreamSecurity;
        profile.Sni = "germany.example";
        profile.EchConfigList = "cloudflare-ech.com+udp://1.1.1.1";
        profile.EchOutbound = echOutbound;
        return profile;
    }

    /// <summary>The context of an exit whose node is <paramref name="node" />, as AetherExitHost builds it.</summary>
    private static CoreConfigContext ExitContext(Config config, ProfileItem? node = null)
    {
        var context = CoreConfigTestFactory.CreateContext(config, AetherExitHost.ContextProfile(), ECoreType.Xray);
        if (node != null)
        {
            context.Aether.ExitNodes[node.Remarks] = ExitNodeLookup.Of(new ByName<ProfileItem>.One(node));
        }
        return context;
    }

    private static async Task<JsonObject> ExitConfig(CoreConfigContext context, AetherExit exit)
    {
        var result = new CoreConfigV2rayService(context).GenerateAetherExitConfig(exit, 41980);
        await result.Success.Should().BeTrue().Because(result.Msg ?? string.Empty);
        return JsonNode.Parse(result.Data!.ToString()!)!.AsObject();
    }

    /// <summary>The configuration of a latency test of <paramref name="context" />'s profile with a core of its own.</summary>
    private static async Task<JsonObject> TestConfig(CoreConfigContext context)
    {
        context.Aether.SecondaryPort = 41982;
        var result = new CoreConfigV2rayService(context).GenerateClientSpeedtestConfig(41981);
        await result.Success.Should().BeTrue().Because(result.Msg ?? string.Empty);
        return JsonNode.Parse(result.Data!.ToString()!)!.AsObject();
    }

    private static JsonObject Outbound(JsonObject config, string tag)
    {
        return config["outbounds"]!.AsArray().OfType<JsonObject>().Single(outbound => outbound["tag"]!.GetValue<string>() == tag);
    }

    private static string? InterfaceOf(JsonObject outbound)
    {
        return outbound["streamSettings"]?["sockopt"]?["interface"]?.GetValue<string>();
    }

    // ---- the exit of a scan or a key run

    [Test]
    public async Task UnderATunTheExitXrayDialsOutByTheUplink()
    {
        var germany = WithEch(Vless("germany-id", "germany", "198.51.100.9"));
        var named = ExitContext(TunConfig(), germany);
        var plain = ExitContext(TunConfig());

        await Binding().Apply(named, AetherUplinkBinding.ExitRole).Should().BeEqualTo(Uplink);
        await Binding().Apply(plain, AetherUplinkBinding.ExitRole).Should().BeEqualTo(Uplink);
        var node = await ExitConfig(named, new AetherExit(Node: "germany"));
        var freedom = await ExitConfig(plain, AetherExit.Plain);

        // The named exit-node and its ECH outbound, which the ECH config query of its TLS leaves by; and the direct outbound
        // the exit's DNS leaves by (D2-3).
        await Tags(node).Should().BeEquivalentTo([Global.AetherExitNodeTag, Global.DirectTag, "ech-out"], CollectionOrdering.Any);
        await InterfaceOf(Outbound(node, Global.AetherExitNodeTag)).Should().BeEqualTo(Uplink);
        await InterfaceOf(Outbound(node, "ech-out")).Should().BeEqualTo(Uplink);
        await InterfaceOf(Outbound(node, Global.DirectTag)).Should().BeEqualTo(Uplink);
        // The freedom exit-node, the outbound of a plain exit besides that direct one; it keeps what the TUN gives it besides.
        var exitNode = Outbound(freedom, Global.AetherExitNodeTag);
        await Tags(freedom).Should().BeEquivalentTo([Global.AetherExitNodeTag, Global.DirectTag], CollectionOrdering.Any);
        await InterfaceOf(exitNode).Should().BeEqualTo(Uplink);
        await exitNode["streamSettings"]!["sockopt"]!["domainStrategy"]!.GetValue<string>().Should().BeEqualTo("ForceIP");
    }

    private static List<string> Tags(JsonObject config)
    {
        return config["outbounds"]!.AsArray().Select(outbound => outbound!["tag"]!.GetValue<string>()).ToList();
    }

    private static JsonObject? EchSockoptOf(JsonObject outbound)
    {
        return outbound["streamSettings"]?["tlsSettings"]?["echSockopt"] as JsonObject;
    }

    private static string? DomainStrategyOf(JsonObject outbound)
    {
        return outbound["streamSettings"]?["sockopt"]?["domainStrategy"]?.GetValue<string>();
    }

    /// <summary>A VLESS profile over TLS whose ECH config query goes to <paramref name="server" />, with no ECH outbound.</summary>
    private static ProfileItem WithEchQuery(ProfileItem profile, string server = "udp://1.1.1.1")
    {
        profile.StreamSecurity = Global.StreamSecurity;
        profile.Sni = "germany.example";
        profile.EchConfigList = $"cloudflare-ech.com+{server}";
        return profile;
    }

    // ---- the ECH config query of a profile without an ECH outbound (D2-1)

    [Test]
    public async Task UnderATunTheEchQueryOfAProfileWithoutAnEchOutboundLeavesByTheUplink()
    {
        // Xray sends the query from a socket of its own (tlsSettings.echSockopt), through no dialerProxy of the outbound:
        // the exit-node's query, and the query of a chain hop that dials through another hop, are bound.
        var germany = WithEchQuery(Vless("germany-id", "germany", "198.51.100.9"));
        var exit = ExitContext(TunConfig(), germany);
        Binding().Apply(exit, AetherUplinkBinding.ExitRole);
        var exitNode = Outbound(await ExitConfig(exit, new AetherExit(Node: "germany")), Global.AetherExitNodeTag);
        await (EchSockoptOf(exitNode)?["interface"]?.GetValue<string>()).Should().BeEqualTo(Uplink);
        // A server named by its address needs no lookup.
        await (EchSockoptOf(exitNode)?["domainStrategy"]).Should().BeNull();

        // A, B, WARP: the WARP core dials out through B, the exit-node, which dials through A; B's query does not.
        var a = Vless("hop-a", "A", "203.0.113.71");
        var b = WithEchQuery(Vless("hop-b", "B", "203.0.113.72"), "https://dns.example/dns-query");
        var warp = Warp();
        var chain = CoreConfigTestFactory.CreateProxyChainNode(ECoreType.Xray, "chain-a-b-warp", "chain", [a.IndexId, b.IndexId, warp.IndexId]);
        var test = AetherContext(TunConfig(), chain, a, b, warp);
        Binding().Apply(test, AetherUplinkBinding.TestRole);
        var config = await TestConfig(test);
        var hopB = Outbound(config, Global.AetherExitNodeTag);
        await (hopB["streamSettings"]?["sockopt"]?["dialerProxy"]?.GetValue<string>()).Should().NotBeNull();
        await InterfaceOf(hopB).Should().BeNull();
        await (EchSockoptOf(hopB)?["interface"]?.GetValue<string>()).Should().BeEqualTo(Uplink);
        // A server named by its domain is looked up by Xray's DNS, whose queries leave by the uplink too (D2-3).
        await (EchSockoptOf(hopB)?["domainStrategy"]?.GetValue<string>()).Should().BeEqualTo("ForceIP");
    }

    [Test]
    public async Task AnEchQueryToTheLoopbackAddressOrWithoutAnUplinkIsLeftAsItWas()
    {
        var local = WithEchQuery(Vless("germany-id", "germany", "198.51.100.9"), "udp://127.0.0.1:53");
        var toLoopback = ExitContext(TunConfig(), local);
        Binding().Apply(toLoopback, AetherUplinkBinding.ExitRole);
        await EchSockoptOf(Outbound(await ExitConfig(toLoopback, new AetherExit(Node: "germany")), Global.AetherExitNodeTag)).Should().BeNull();

        var germany = WithEchQuery(Vless("germany-id", "germany", "198.51.100.9"));
        var bindInterface = ExitContext(TunConfig(bindInterface: "Ethernet 2"), germany);
        Binding().Apply(bindInterface, AetherUplinkBinding.ExitRole);
        await EchSockoptOf(Outbound(await ExitConfig(bindInterface, new AetherExit(Node: "germany")), Global.AetherExitNodeTag)).Should().BeNull();

        var noTun = ExitContext(TunConfig(), germany);
        Binding(tun: false).Apply(noTun, AetherUplinkBinding.ExitRole);
        await EchSockoptOf(Outbound(await ExitConfig(noTun, new AetherExit(Node: "germany")), Global.AetherExitNodeTag)).Should().BeNull();
    }

    // ---- what reaches the loopback address (D2-2)

    [Test]
    public async Task AnExitNodeOnTheLoopbackAddressOrNamingItsInterfaceIsLeftAsItIs()
    {
        // A custom outbound is bound by its own JSON, not by the stand-in the generation builds for it; a WireGuard peer is
        // host:port.
        var asWritten = new[]
        {
            """{"protocol": "socks", "settings": {"servers": [{"address": "127.0.0.1", "port": 9050}]}}""",
            """{"protocol": "socks", "settings": {"servers": [{"address": "203.0.113.90", "port": 1080}]}, "streamSettings": {"sockopt": {"interface": "eth9"}}}""",
        };
        foreach (var json in asWritten)
        {
            var custom = CoreConfigTestFactory.CreateCustomOutboundNode(ECoreType.Xray, "tor-id", "tor");
            var context = ExitContext(TunConfig(), custom);
            context.CustomOutboundContent[custom.IndexId] = json;
            Binding().Apply(context, AetherUplinkBinding.ExitRole);

            var exitNode = Outbound(await ExitConfig(context, new AetherExit(Node: "tor")), Global.AetherExitNodeTag);

            var written = JsonNode.Parse(json)!.AsObject();
            await InterfaceOf(exitNode).Should().BeEqualTo(InterfaceOf(written)).Because(json);
            await DomainStrategyOf(exitNode).Should().BeNull().Because(json);
        }

        var relay = CoreConfigTestFactory.CreateWireguardNode(ECoreType.Xray, "relay-id", "relay");
        relay.Address = "127.0.0.1";
        var wireGuard = ExitContext(TunConfig(), relay);
        Binding().Apply(wireGuard, AetherUplinkBinding.ExitRole);
        var peer = Outbound(await ExitConfig(wireGuard, new AetherExit(Node: "relay")), Global.AetherExitNodeTag);
        await peer["settings"]!["peers"]![0]!["endpoint"]!.GetValue<string>().Should().BeEqualTo("127.0.0.1:51820");
        await InterfaceOf(peer).Should().BeNull();
    }

    [Test]
    public async Task ACustomOutboundThatDialsOutIsBoundByItsOwnJson()
    {
        const string json = """{"protocol": "vless", "settings": {"address": "de.example.com", "port": 443, "id": "a"}, "streamSettings": {"sockopt": {"tcpKeepAliveIdle": 100}}}""";
        var custom = CoreConfigTestFactory.CreateCustomOutboundNode(ECoreType.Xray, "de-id", "de");
        var context = ExitContext(TunConfig(), custom);
        context.CustomOutboundContent[custom.IndexId] = json;
        Binding().Apply(context, AetherUplinkBinding.ExitRole);

        var exitNode = Outbound(await ExitConfig(context, new AetherExit(Node: "de")), Global.AetherExitNodeTag);

        await InterfaceOf(exitNode).Should().BeEqualTo(Uplink);
        await exitNode["streamSettings"]!["sockopt"]!["tcpKeepAliveIdle"]!.GetValue<int>().Should().BeEqualTo(100);
        // Its server's name is looked up by Xray's DNS (D2-3).
        await DomainStrategyOf(exitNode).Should().BeEqualTo("ForceIP");

        // With the settings' bind interface, as before: the interface over whatever the file wrote.
        var bound = ExitContext(TunConfig(bindInterface: "Ethernet 2"), custom);
        bound.CustomOutboundContent[custom.IndexId] = json;
        var asBefore = Outbound(await ExitConfig(bound, new AetherExit(Node: "de")), Global.AetherExitNodeTag);
        await InterfaceOf(asBefore).Should().BeEqualTo("Ethernet 2");
        await DomainStrategyOf(asBefore).Should().BeNull();
    }

    // ---- the names the bound Xray looks up (D2-3)

    private static JsonObject DnsOf(JsonObject config)
    {
        return config["dns"]!.AsObject();
    }

    private static List<string> DnsAddressesOf(JsonObject config)
    {
        return DnsOf(config)["servers"]!.AsArray().Select(server => server is JsonValue value ? value.GetValue<string>() : server!["address"]!.GetValue<string>()).ToList();
    }

    private static async Task AssertDnsLeavesByTheUplink(JsonObject config)
    {
        await DnsOf(config)["tag"]!.GetValue<string>().Should().BeEqualTo(CoreConfigV2rayService.UplinkDnsTag);
        var rule = config["routing"]!["rules"]!.AsArray().OfType<JsonObject>()
            .Single(rule => rule["inboundTag"]?.AsArray().Any(tag => tag!.GetValue<string>() == CoreConfigV2rayService.UplinkDnsTag) == true);
        await rule["outboundTag"]!.GetValue<string>().Should().BeEqualTo(Global.DirectTag);
        var direct = Outbound(config, Global.DirectTag);
        await direct["protocol"]!.GetValue<string>().Should().BeEqualTo("freedom");
        await InterfaceOf(direct).Should().BeEqualTo(Uplink);
        await DomainStrategyOf(direct).Should().BeEqualTo("ForceIP");
    }

    [Test]
    public async Task TheBoundExitLooksNamesUpByItsOwnDnsWhichLeavesByTheUplink()
    {
        // FakeIP: the TUN's DNS answers the system's resolver with addresses only the TUN takes back; a bound connection
        // cannot reach them. The named exit-node's server, a domain, is looked up by Xray's DNS, and the system's resolver
        // ("dhcp") gives way to the bootstrap DNS.
        var config = TunConfig();
        config.SimpleDNSItem.DirectDNS = "dhcp,tcp+local://198.51.100.53";
        var de = Vless("de-id", "de", "de.example.com");
        var context = ExitContext(config, de);
        Binding().Apply(context, AetherUplinkBinding.ExitRole);

        var exit = await ExitConfig(context, new AetherExit(Node: "de"));

        await DomainStrategyOf(Outbound(exit, Global.AetherExitNodeTag)).Should().BeEqualTo("ForceIP");
        await AssertDnsLeavesByTheUplink(exit);
        await DnsAddressesOf(exit).Should().BeEquivalentTo([config.SimpleDNSItem.BootstrapDNS!, "tcp://198.51.100.53"], CollectionOrdering.Any);
    }

    [Test]
    public async Task TheBoundTestXrayLooksNamesUpByItsOwnDnsWhichLeavesByTheUplink()
    {
        // A test has no DNS of its own unless it is bound: the hop its WARP core dials out through, named by its domain,
        // is looked up by the DNS it gets then.
        var x = Vless("hop-x", "X", "x.example.com");
        var warp = Warp();
        var chain = CoreConfigTestFactory.CreateProxyChainNode(ECoreType.Xray, "chain-x-warp", "chain", [x.IndexId, warp.IndexId]);
        var context = AetherContext(TunConfig(), chain, x, warp);
        Binding().Apply(context, AetherUplinkBinding.TestRole);

        var config = await TestConfig(context);

        await DomainStrategyOf(Outbound(config, Global.AetherExitNodeTag)).Should().BeEqualTo("ForceIP");
        await AssertDnsLeavesByTheUplink(config);
        await DnsAddressesOf(config).Should().NotContain("localhost");
        // The core is on the loopback address.
        await DomainStrategyOf(Outbound(config, Global.ProxyTag)).Should().BeNull();

        // Unbound, as before: no DNS.
        var unbound = AetherContext(TunConfig(), chain, x, warp);
        await (await TestConfig(unbound))["dns"].Should().BeNull();
    }

    [Test]
    public async Task TheBindInterfaceOfTheSettingsWinsAsItDidBefore()
    {
        var germany = WithEch(Vless("germany-id", "germany", "198.51.100.9"));
        var context = ExitContext(TunConfig(bindInterface: "Ethernet 2"), germany);

        await Binding().Apply(context, AetherUplinkBinding.ExitRole).Should().BeNull();
        var config = await ExitConfig(context, new AetherExit(Node: "germany"));

        await InterfaceOf(Outbound(config, Global.AetherExitNodeTag)).Should().BeEqualTo("Ethernet 2");
        // As before: the ECH outbound is left as its profile wrote it.
        await JsonNode.DeepEquals(Outbound(config, "ech-out"), JsonNode.Parse(EchOutbound)).Should().BeTrue();
    }

    [Test]
    public async Task WithoutATunOrWithoutAnUplinkTheExitIsWhatItWasAndALineSaysWhy()
    {
        var germany = WithEch(Vless("germany-id", "germany", "198.51.100.9"));
        var before = (await ExitConfig(ExitContext(TunConfig(), germany), new AetherExit(Node: "germany"))).ToJsonString();
        var noTun = ExitContext(TunConfig(), germany);
        var noUplink = ExitContext(TunConfig(), germany);
        var log = new List<string>();

        await Binding(tun: false, log: log).Apply(noTun, AetherUplinkBinding.ExitRole).Should().BeNull();
        await (await ExitConfig(noTun, new AetherExit(Node: "germany"))).ToJsonString().Should().BeEqualTo(before);
        await log.Should().BeEmpty();

        await Binding(uplink: null, log: log).Apply(noUplink, AetherUplinkBinding.ExitRole).Should().BeNull();
        await (await ExitConfig(noUplink, new AetherExit(Node: "germany"))).ToJsonString().Should().BeEqualTo(before);
        await log.Should().BeEquivalentTo(["AetherCore: no network interface outside the TUN was found; the exit Xray runs unbound"]);
        await before.Contains("\"interface\"").Should().BeFalse();
    }

    [Test]
    public async Task AnEchOutboundThatDialsNothingItselfOrNamesItsInterfaceIsLeftAsItIs()
    {
        // Through another outbound, to the loopback address, a blackhole, or bound by its profile already: as written. A
        // proxy one of its own is bound as the exit-node is.
        var asWritten = new[]
        {
            """{"tag": "ech-out", "protocol": "freedom", "streamSettings": {"sockopt": {"dialerProxy": "exit-node"}}}""",
            """{"tag": "ech-out", "protocol": "socks", "settings": {"servers": [{"address": "127.0.0.1", "port": 1080}]}}""",
            """{"tag": "ech-out", "protocol": "socks", "settings": {"address": "localhost", "port": 1080}}""",
            """{"tag": "ech-out", "protocol": "freedom", "settings": {"redirect": "127.0.0.1:53"}}""",
            """{"tag": "ech-out", "protocol": "blackhole"}""",
            """{"tag": "ech-out", "protocol": "freedom", "streamSettings": {"sockopt": {"interface": "eth9"}}}""",
        };
        foreach (var echOutbound in asWritten)
        {
            var context = ExitContext(TunConfig(), WithEch(Vless("germany-id", "germany", "198.51.100.9"), echOutbound));
            Binding().Apply(context, AetherUplinkBinding.ExitRole);

            var config = await ExitConfig(context, new AetherExit(Node: "germany"));

            await JsonNode.DeepEquals(Outbound(config, "ech-out"), JsonNode.Parse(echOutbound)).Should().BeTrue().Because(echOutbound);
            await InterfaceOf(Outbound(config, Global.AetherExitNodeTag)).Should().BeEqualTo(Uplink);
        }

        const string proxied = """{"tag": "ech-out", "protocol": "socks", "settings": {"servers": [{"address": "203.0.113.53", "port": 1080}]}, "streamSettings": {"sockopt": {"tcpKeepAliveIdle": 100}}}""";
        var proxiedContext = ExitContext(TunConfig(), WithEch(Vless("germany-id", "germany", "198.51.100.9"), proxied));
        Binding().Apply(proxiedContext, AetherUplinkBinding.ExitRole);
        var bound = Outbound(await ExitConfig(proxiedContext, new AetherExit(Node: "germany")), "ech-out");
        await InterfaceOf(bound).Should().BeEqualTo(Uplink);
        // What the profile wrote stays beside it.
        await bound["streamSettings"]!["sockopt"]!["tcpKeepAliveIdle"]!.GetValue<int>().Should().BeEqualTo(100);
    }

    // ---- the Xray of a latency test

    [Test]
    public async Task UnderATunTheTestXrayDialsOutByTheUplink()
    {
        // X, WARP: the WARP core dials out through X, which the test's Xray dials as its exit-node.
        var x = WithEch(Vless("hop-x", "X", "203.0.113.70"));
        var warp = Warp();
        var chain = CoreConfigTestFactory.CreateProxyChainNode(ECoreType.Xray, "chain-x-warp", "chain", [x.IndexId, warp.IndexId]);
        var context = AetherContext(TunConfig(), chain, x, warp);

        await Binding().Apply(context, AetherUplinkBinding.TestRole).Should().BeEqualTo(Uplink);
        var config = await TestConfig(context);

        await InterfaceOf(Outbound(config, Global.AetherExitNodeTag)).Should().BeEqualTo(Uplink);
        await InterfaceOf(Outbound(config, "ech-out")).Should().BeEqualTo(Uplink);
        // The core is on the loopback address, and the test sends nothing to the blackhole or to the TUN's DNS outbound,
        // which no rule of a test leads to. The direct outbound carries the test's DNS, by the uplink (D2-3).
        await InterfaceOf(Outbound(config, Global.ProxyTag)).Should().BeNull();
        await InterfaceOf(Outbound(config, Global.DirectTag)).Should().BeEqualTo(Uplink);
        await InterfaceOf(Outbound(config, Global.BlockTag)).Should().BeNull();
        await InterfaceOf(Outbound(config, Global.DnsOutboundTag)).Should().BeNull();
        await config["outbounds"]!.AsArray().Select(outbound => outbound!["tag"]!.GetValue<string>()).Should().BeEquivalentTo(
            [Global.ProxyTag, Global.DirectTag, Global.BlockTag, Global.DnsOutboundTag, Global.AetherExitNodeTag, "ech-out"],
            CollectionOrdering.Any);

        // A WARP profile on its own: its freedom exit-node.
        var alone = AetherContext(TunConfig(), Warp());
        Binding().Apply(alone, AetherUplinkBinding.TestRole);
        await InterfaceOf(Outbound(await TestConfig(alone), Global.AetherExitNodeTag)).Should().BeEqualTo(Uplink);
    }

    [Test]
    public async Task WithoutATunOrAnUplinkTheTestXrayIsWhatItWas()
    {
        var before = (await TestConfig(AetherContext(TunConfig(), Warp()))).ToJsonString();
        var log = new List<string>();
        var noTun = AetherContext(TunConfig(), Warp());
        var noUplink = AetherContext(TunConfig(), Warp());

        Binding(tun: false, log: log).Apply(noTun, AetherUplinkBinding.TestRole);
        Binding(uplink: null, log: log).Apply(noUplink, AetherUplinkBinding.TestRole);

        await (await TestConfig(noTun)).ToJsonString().Should().BeEqualTo(before);
        await (await TestConfig(noUplink)).ToJsonString().Should().BeEqualTo(before);
        await log.Should().BeEquivalentTo(["AetherCore: no network interface outside the TUN was found; the test Xray runs unbound"]);
        await before.Contains("\"interface\"").Should().BeFalse();
    }

    [Test]
    public async Task ALatencyTestStartsItsXrayOnItsConfigurationBoundToTheUplink()
    {
        // MeasureAsync, with PattN's own generation: the configuration the test's Xray starts on is bound, generated again
        // once the test knows that it starts that Xray (D2-4).
        var cores = new FakeAetherCores(_root);
        var started = new List<string>();
        AetherDelayTester Tester(AetherUplinkBinding? uplink)
        {
            return new AetherDelayTester(new AetherTestHost
            {
                Cores = cores.Manager,
                Build = profile => Task.FromResult(new CoreConfigContextBuilderResult(
                    AetherContext(TunConfig(), profile), NodeValidatorResult.Empty())),
                Generate = (context, port) => new CoreConfigV2rayService(context).GenerateClientSpeedtestConfig(port),
                StartXray = content =>
                {
                    // Written down, and not run.
                    started.Add(content);
                    return Task.FromResult<IAsyncDisposable?>(null);
                },
                Uplink = uplink,
            });
        }

        await (await Tester(Binding()).MeasureAsync(Warp(), (_, _, _) => Task.FromResult(1))).Should().BeEqualTo(AetherTestResult.Failed);
        await (await Tester(null).MeasureAsync(Warp(), (_, _, _) => Task.FromResult(1))).Should().BeEqualTo(AetherTestResult.Failed);

        await started.Count.Should().BeEqualTo(2);
        var bound = JsonNode.Parse(started[0])!.AsObject();
        await InterfaceOf(Outbound(bound, Global.AetherExitNodeTag)).Should().BeEqualTo(Uplink);
        // A host without a binding, as the fakes have, binds nothing.
        await started[1].Contains("\"interface\"").Should().BeFalse();
    }
}

/// <summary>
/// PattN (D2): the exit of a scan or a key run as PattN generates it (AetherExitHost.GenerateAsync), with the context
/// builder and the test database: bound to the uplink under a TUN. Not beside the other tests that write to the database.
/// </summary>
[NotInParallel]
public class AetherUplinkExitHostTests
{
    [Test]
    public async Task TheExitOfACoreOfItsOwnIsBoundToTheUplinkUnderATun()
    {
        SQLiteHelper.Instance.CreateTable<FullConfigTemplateItem>();
        SQLiteHelper.Instance.CreateTable<DNSItem>();
        SQLiteHelper.Instance.CreateTable<RoutingItem>();
        SQLiteHelper.Instance.CreateTable<ProfileItem>();
        NewConfig(config => config.TunModeItem.EnableTun = true);
        var log = new List<string>();
        var binding = new AetherUplinkBinding { TunRunning = () => true, FindUplink = () => "Wi-Fi 2", Log = log.Add };
        var unbound = new AetherUplinkBinding { TunRunning = () => false, FindUplink = () => "Wi-Fi 2", Log = log.Add };

        var result = await AetherExitHost.GenerateAsync(AetherExit.Plain, 41983, binding);
        var plain = await AetherExitHost.GenerateAsync(AetherExit.Plain, 41983, unbound);

        await result.Success.Should().BeTrue().Because(result.Msg ?? string.Empty);
        var exitNode = JsonNode.Parse(result.Data!.ToString()!)!["outbounds"]![0]!;
        await exitNode["tag"]!.GetValue<string>().Should().BeEqualTo(Global.AetherExitNodeTag);
        await exitNode["streamSettings"]!["sockopt"]!["interface"]!.GetValue<string>().Should().BeEqualTo("Wi-Fi 2");
        await plain.Success.Should().BeTrue().Because(plain.Msg ?? string.Empty);
        await plain.Data!.ToString()!.Contains("\"interface\"").Should().BeFalse();
        await log.Should().BeEmpty();
    }
}
