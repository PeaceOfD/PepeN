using TUnit.Assertions.Enums;
using static ServiceLib.Tests.CoreConfig.V2ray.AetherConfigTests;

namespace ServiceLib.Tests.CoreConfig.V2ray;

/// <summary>
/// PattN (design 2.8; PattNG AetherDependencyTest and CoreConfigManager handleProxyChainResolvedOutbound): an Aether
/// profile in a proxy chain, in either direction. A chain is stored entry hop first, as its editor shows it; Xray dials
/// it from the exit hop, tagged as the chain, to the entry hop. The Aether hop dials through no hop; the hop on its entry
/// side carries the core's own traffic as the exit-node.
/// </summary>
public class AetherChainConfigTests
{
    private const int P = AetherPorts.DefaultListenPort;

    private static ProfileItem Hop(string name)
    {
        // An address of its own for every name used here.
        return Vless($"hop-{name}", name, "203.0.113." + (name.Sum(c => c) % 250 + 1));
    }

    private static ProfileItem Chain(string remarks, params ProfileItem[] storedEntryFirst)
    {
        return CoreConfigTestFactory.CreateProxyChainNode(ECoreType.Xray, $"chain-{remarks}", remarks, storedEntryFirst.Select(hop => hop.IndexId));
    }

    private static string? DialerOf(Outbounds4Ray outbound)
    {
        return outbound.streamSettings?.sockopt?.dialerProxy;
    }

    private static async Task AssertSocksToTheCore(Outbounds4Ray outbound)
    {
        await outbound.protocol.Should().BeEqualTo("socks");
        await outbound.settings.address!.ToString().Should().BeEqualTo(Global.Loopback);
        await outbound.settings.port.Should().BeEqualTo(P);
        // Dials through no hop: what it reaches is on the loopback address.
        await DialerOf(outbound).Should().BeNull();
    }

    // Design 2.8, row 1: X, WARP. WARP is the exit hop; the WARP handshake goes out through X.
    [Test]
    public async Task AnAetherExitHopDialsOutThroughTheHopOnItsEntrySide()
    {
        var x = Hop("X");
        x.TargetStrategy = "UseIPv4";
        var warp = Warp();

        var g = await Succeeds(AetherContext(NewConfig(), Chain("chain", x, warp), x, warp));

        await AssertSocksToTheCore(g.Outbound(Global.ProxyTag));
        // The exit hop carries your traffic and keeps its default.
        await g.Outbound(Global.ProxyTag).targetStrategy.Should().BeEqualTo(AetherOutbound.ForceIPv4v6);
        var exitNode = g.Outbound(Global.AetherExitNodeTag);
        await exitNode.settings.address!.ToString().Should().BeEqualTo(x.Address);
        await DialerOf(exitNode).Should().BeNull();
        // The exit-node passes every name the core sends on as it is, whatever its profile says.
        await exitNode.targetStrategy.Should().BeNull();
        await g.Tags.Should().BeEquivalentTo([Global.ProxyTag, Global.DirectTag, Global.BlockTag, Global.AetherExitNodeTag], CollectionOrdering.Any);
        await g.Core.Exit.Should().BeEqualTo(AetherExit.Through([x]));
        await g.Config.routing.rules.First().outboundTag.Should().BeEqualTo(Global.AetherExitNodeTag);
    }

    // Design 2.8, row 2: WARP, X. WARP is the entry hop and dials out by its own exit-node; X carries your traffic.
    [Test]
    public async Task anAetherEntryHopDialsOutThroughItsOwnExitNode()
    {
        var x = Hop("X");
        var warp = Warp();

        var g = await Succeeds(AetherContext(NewConfig(), Chain("chain", warp, x), x, warp));

        var proxy = g.Outbound(Global.ProxyTag);
        await proxy.settings.address!.ToString().Should().BeEqualTo(x.Address);
        await DialerOf(proxy).Should().BeEqualTo("chain-proxy-1-WARP");
        var hop = g.Outbound("chain-proxy-1-WARP");
        await AssertSocksToTheCore(hop);
        // Behind another hop, it carries that hop's connection to its server: names pass on (PattNG Q14).
        await hop.targetStrategy.Should().BeNull();
        await g.Outbound(Global.AetherExitNodeTag).protocol.Should().BeEqualTo("freedom");
        await g.Core.Exit.Should().BeEqualTo(AetherExit.Plain);
    }

    // PattNG test_applyChainTargetStrategies_aHopCarryingTheHopBeforeItPassesNamesOnUnlessItsProfileSaysOtherwise: the
    // targetStrategy a profile stores is its own choice, kept behind another hop too.
    [Test]
    public async Task anAetherHopBehindAnotherKeepsTheTargetStrategyItsProfileStores()
    {
        var x = Hop("X");
        var warp = Warp();
        warp.TargetStrategy = "UseIPv4v6";

        var g = await Succeeds(AetherContext(NewConfig(), Chain("chain", warp, x), x, warp));

        await g.Outbound("chain-proxy-1-WARP").targetStrategy.Should().BeEqualTo("UseIPv4v6");
    }

    // PattN (critique 6 and 18): the hop the Aether core dials out through is a proxy whose server is dialled as the
    // other proxies' are, with the dial strategy of the DNS settings, and passes every name the core sends on as it is,
    // whatever the proxy strategy says.
    [Test]
    public async Task TheChainHopTheCoreDialsOutThroughDialsAsTheProxiesDoAndPassesNamesOn()
    {
        var config = NewConfig(config =>
        {
            config.SimpleDNSItem.Strategy4Proxy = "UseIPv4";
            config.SimpleDNSItem.Strategy4ProxyDial = "UseIPv6";
        });
        var x = Hop("X");
        var warp = Warp();

        var g = await Succeeds(AetherContext(config, Chain("chain", x, warp), x, warp));

        var exitNode = g.Outbound(Global.AetherExitNodeTag);
        await exitNode.settings.address!.ToString().Should().BeEqualTo(x.Address);
        await exitNode.targetStrategy.Should().BeNull();
        await exitNode.streamSettings.sockopt!.domainStrategy.Should().BeEqualTo("UseIPv6");

        // A latency test has no DNS: the exit-node passes every name on there too.
        var context = AetherContext(config, Chain("chain", x, warp), x, warp);
        context.Aether.SecondaryPort = 41901;
        var test = new CoreConfigV2rayService(context).GenerateClientSpeedtestConfig(41801);
        await test.Success.Should().BeTrue().Because(test.Msg ?? string.Empty);
        await new Generated(test, context).Outbound(Global.AetherExitNodeTag).targetStrategy.Should().BeNull();
    }

    // Design 2.7.4: a custom outbound on the entry side of the Aether hop is the exit-node, its file's outbound in place of
    // the placeholder, without a targetStrategy the file sets.
    [Test]
    public async Task ACustomOutboundOnTheEntrySideIsTheExitNodeAndPassesNamesOn()
    {
        const string outbound = """{"tag": "{{tag}}", "protocol": "trojan", "targetStrategy": "UseIPv4", "settings": {"address": "198.51.100.21", "port": 443, "password": "x"}}""";
        var custom = CoreConfigTestFactory.CreateCustomOutboundNode(ECoreType.Xray, "custom-hop", "custom hop");
        var warp = Warp();
        var context = AetherContext(NewConfig(), Chain("chain", custom, warp), custom, warp);
        context.CustomOutboundContent[custom.IndexId] = outbound;

        var g = await Succeeds(context);

        var exitNode = g.OutboundNode(Global.AetherExitNodeTag);
        await exitNode["protocol"]!.GetValue<string>().Should().BeEqualTo("trojan");
        await exitNode.ContainsKey("targetStrategy").Should().BeFalse();
    }

    // Design 2.8, row 3: A, WARP, B.
    [Test]
    public async Task AnAetherMiddleHopDialsOutThroughTheHopOnItsEntrySide()
    {
        var (a, b, warp) = (Hop("A"), Hop("B"), Warp());

        var g = await Succeeds(AetherContext(NewConfig(), Chain("chain", a, warp, b), a, b, warp));

        var proxy = g.Outbound(Global.ProxyTag);
        await proxy.settings.address!.ToString().Should().BeEqualTo(b.Address);
        await DialerOf(proxy).Should().BeEqualTo("chain-proxy-1-WARP");
        await AssertSocksToTheCore(g.Outbound("chain-proxy-1-WARP"));
        var exitNode = g.Outbound(Global.AetherExitNodeTag);
        await exitNode.settings.address!.ToString().Should().BeEqualTo(a.Address);
        await DialerOf(exitNode).Should().BeNull();
        await g.Tags.Any(tag => tag.StartsWith("chain-proxy-2", StringComparison.Ordinal)).Should().BeFalse();
        await g.Core.Exit.Should().BeEqualTo(AetherExit.Through([a]));
    }

    // Design 2.8, row 4: A1, A2, WARP, B. WARP is carried A1 -> A2.
    [Test]
    public async Task TheHopsOnTheEntrySideOfTheExitNodeDialOnAsBefore()
    {
        var (a1, a2, b, warp) = (Hop("A1"), Hop("A2"), Hop("B"), Warp());

        var g = await Succeeds(AetherContext(NewConfig(), Chain("chain", a1, a2, warp, b), a1, a2, b, warp));

        await DialerOf(g.Outbound(Global.ProxyTag)).Should().BeEqualTo("chain-proxy-1-WARP");
        await AssertSocksToTheCore(g.Outbound("chain-proxy-1-WARP"));
        var exitNode = g.Outbound(Global.AetherExitNodeTag);
        await exitNode.settings.address!.ToString().Should().BeEqualTo(a2.Address);
        await DialerOf(exitNode).Should().BeEqualTo("chain-proxy-3-A1");
        var entry = g.Outbound("chain-proxy-3-A1");
        await entry.settings.address!.ToString().Should().BeEqualTo(a1.Address);
        await DialerOf(entry).Should().BeNull();
        // Nearest first: the exit-node, then the entry hop.
        await g.Core.Exit.Should().BeEqualTo(AetherExit.Through([a2, a1]));
        await g.Core.Exit.Should().NotBeEqualTo(AetherExit.Through([a1, a2]));
    }

    // Design 2.8, row 5: a routing rule sends to the chain T = X, WARP.
    [Test]
    public async Task ARoutingRulesChainBuildsItsAetherHopAlike()
    {
        var (x, warp) = (Hop("X"), Warp());
        var t = Chain("T", x, warp);
        var context = AetherContext(NewConfig(), CoreConfigTestFactory.CreateVmessNode(ECoreType.Xray), x, warp, t) with { RoutingItem = RuleTo(t.Remarks) };
        context.AllProxiesMap[$"remark:{t.Remarks}"] = t;

        var g = await Succeeds(context);

        await AssertSocksToTheCore(g.Outbound($"{t.IndexId}-{Global.ProxyTag}-{t.Remarks}"));
        await g.Outbound(Global.AetherExitNodeTag).settings.address!.ToString().Should().BeEqualTo(x.Address);
        await g.Outbound(Global.ProxyTag).protocol.Should().BeEqualTo("vmess");
        await g.Core.Exit.Should().BeEqualTo(AetherExit.Through([x]));
    }

    // Design 2.8, row 6: a subscription puts its previous profile E and its next one N around its profile WARP, as the
    // chain E, WARP, N (CoreConfigContextBuilder.BuildSubscriptionChainNodeAsync).
    [Test]
    public async Task ASubscriptionsChainAroundAnAetherProfileBuildsAlike()
    {
        var (e, n, warp) = (Hop("E"), Hop("N"), Warp());
        var virtualChain = CoreConfigTestFactory.CreateProxyChainNode(ECoreType.Xray, "inner-chain", warp.Remarks, [e.IndexId, warp.IndexId, n.IndexId]);

        var g = await Succeeds(AetherContext(NewConfig(), virtualChain, e, n, warp));

        var proxy = g.Outbound(Global.ProxyTag);
        await proxy.settings.address!.ToString().Should().BeEqualTo(n.Address);
        await DialerOf(proxy).Should().BeEqualTo($"chain-proxy-1-{warp.Remarks}");
        await AssertSocksToTheCore(g.Outbound($"chain-proxy-1-{warp.Remarks}"));
        await g.Outbound(Global.AetherExitNodeTag).settings.address!.ToString().Should().BeEqualTo(e.Address);
        await g.Core.Exit.Should().BeEqualTo(AetherExit.Through([e]));
    }

    // PattNG anyOtherAetherHopDialsOutThroughTheHopsOnItsEntrySide: the exit-node settings of the profile do not count
    // there, the hop is the exit-node.
    [Test]
    public async Task anyOtherAetherHopDialsOutThroughTheHopsOnItsEntrySide()
    {
        var x = Hop("X");
        var plain = await Succeeds(AetherContext(NewConfig(), Chain("chain", x, Warp()), x, Warp()));

        var masked = Warp();
        masked.Finalmask = """{"tcp": []}""";
        masked.DialMode = "code-1";
        var maskedResult = await Succeeds(AetherContext(NewConfig(), Chain("chain", x, masked), x, masked));
        await maskedResult.Core.Should().BeEqualTo(plain.Core);

        // PattNG aChainsHopTakesThePlaceOfTheExitNodeAProfileNames: nor does a profile it names; it is not even looked up.
        var noded = Warp(extra: extra => extra with { AetherExitNode = "germany" });
        var nodedResult = await Succeeds(AetherContext(NewConfig(), Chain("chain", x, noded), x, noded));
        await nodedResult.Core.Exit.Should().BeEqualTo(AetherExit.Through([x]));
        await nodedResult.Outbound(Global.AetherExitNodeTag).settings.address!.ToString().Should().BeEqualTo(x.Address);
    }

    // PattNG aChainsHopTakesThePlaceOfTheExitNodeAProfileNames: as the chain's entry hop, the core dials out through the
    // profile it names.
    [Test]
    public async Task aChainsEntryHopDialsOutThroughTheProfileItNames()
    {
        var x = Hop("X");
        var germany = Vless("germany", "germany", "198.51.100.9");
        var noded = Warp(extra: extra => extra with { AetherExitNode = "germany" });
        var context = AetherContext(NewConfig(), Chain("chain", noded, x), x, noded);
        context.Aether.ExitNodes["germany"] = ExitNodeLookup.Of(new ByName<ProfileItem>.One(germany));

        var g = await Succeeds(context);

        await g.Outbound(Global.AetherExitNodeTag).settings.address!.ToString().Should().BeEqualTo(germany.Address);
        await g.Core.Exit.Should().BeEqualTo(new AetherExit(Node: "germany", NodeContent: AetherExit.ContentOf(germany)));
    }

    // PattNG theHopsAnAetherHopDialsOutThroughAreTheCoresOwn: two chains through the same hops share the core, and the
    // second keeps its own copy of the hop; one core cannot dial out two ways.
    [Test]
    public async Task theHopsAnAetherHopDialsOutThroughAreTheCoresOwn()
    {
        var (x, warp, again) = (Hop("X"), Warp(), Warp("warp-again", "WARP again"));
        var second = Chain("second", x, again);
        var context = AetherContext(NewConfig(), Chain("first", x, warp), x, warp, again, second) with { RoutingItem = RuleTo(second.Remarks) };
        context.AllProxiesMap[$"remark:{second.Remarks}"] = second;

        var g = await Succeeds(context);

        await g.Tags.Count(tag => tag == Global.AetherExitNodeTag).Should().BeEqualTo(1);
        var secondTag = $"{second.IndexId}-{Global.ProxyTag}-{second.Remarks}";
        var copy = g.Outbound($"chain-{secondTag}-1-X");
        await copy.settings.address!.ToString().Should().BeEqualTo(x.Address);
        await AssertSocksToTheCore(g.Outbound(secondTag));
        await g.Core.Exit.Should().BeEqualTo(AetherExit.Through([x]));

        // Through another hop: two exits for one tunnel.
        var y = Hop("Y");
        var other = Chain("other", y, again);
        var twoHops = AetherContext(NewConfig(), Chain("first", x, warp), x, y, warp, again, other) with { RoutingItem = RuleTo(other.Remarks) };
        twoHops.AllProxiesMap[$"remark:{other.Remarks}"] = other;
        await Refused(twoHops, ResUI.MsgAetherConfigTwoExits);

        // On its own and behind a hop: two exits as well.
        var alone = AetherContext(NewConfig(), warp, x, again, second) with { RoutingItem = RuleTo(second.Remarks) };
        alone.AllProxiesMap[$"remark:{second.Remarks}"] = second;
        await Refused(alone, ResUI.MsgAetherConfigTwoExits);

        // Another tunnel is another core, wherever it dials out.
        var wg = Wg();
        var otherTunnel = AetherContext(NewConfig(), Chain("first", x, warp), x, warp) with { RoutingItem = RuleTo(wg.Remarks) };
        otherTunnel.AllProxiesMap[$"remark:{wg.Remarks}"] = wg;
        await Refused(otherTunnel, ResUI.MsgAetherConfigSingleProfile);
    }

    // PattNG aChainCanHaveOneAetherHop
    [Test]
    public async Task aChainCanHaveOneAetherHop()
    {
        var (x, warp, wg) = (Hop("X"), Warp(), Wg());
        await Refused(AetherContext(NewConfig(), Chain("chain", warp, x, wg), x, warp, wg), ResUI.MsgAetherChainOneProfile);

        // The same tunnel twice is two hops all the same.
        var again = Warp("warp-again", "WARP again");
        await Refused(AetherContext(NewConfig(), Chain("chain", warp, again), warp, again), ResUI.MsgAetherChainOneProfile);
    }

    // PattN: one outbound has to be the exit-node, which a group or a chain on the entry side of the Aether hop is not.
    [Test]
    public async Task AGroupOnTheEntrySideOfTheAetherHopIsRefused()
    {
        var (x, y, warp) = (Hop("X"), Hop("Y"), Warp());
        var group = CoreConfigTestFactory.CreatePolicyGroupNode(ECoreType.Xray, "group", "G", [x.IndexId, y.IndexId]);
        var chain = Chain("the chain", group, warp);

        await Refused(AetherContext(NewConfig(), chain, group, x, y, warp), string.Format(ResUI.MsgAetherChainHopGroup, "the chain"));

        // On its exit side, the group's members dial through the Aether hop as through any hop.
        var exitSide = Chain("exit side", warp, group);
        var g = await Succeeds(AetherContext(NewConfig(), exitSide, group, x, y, warp));
        await DialerOf(g.Outbound("proxy-1-X")).Should().BeEqualTo("chain-proxy-1-WARP");
        await DialerOf(g.Outbound("proxy-2-Y")).Should().BeEqualTo("chain-proxy-1-WARP");
        await AssertSocksToTheCore(g.Outbound("chain-proxy-1-WARP"));
        await g.Core.Exit.Should().BeEqualTo(AetherExit.Plain);
    }

    // PattN (critique 3): a chain gives the members of a group or a chain that is its hop a dialer, so an Aether profile
    // reached through one would dial its core's loopback address through a remote hop.
    [Test]
    public async Task AnAetherProfileInsideAGroupOrAChainThatIsAHopIsRefused()
    {
        var (x, y, a, warp) = (Hop("X"), Hop("Y"), Hop("A"), Warp());
        var group = CoreConfigTestFactory.CreatePolicyGroupNode(ECoreType.Xray, "group", "G", [warp.IndexId, y.IndexId]);
        await Refused(AetherContext(NewConfig(), Chain("chain", x, group), group, x, y, warp),
            string.Format(ResUI.MsgAetherInGroupHop, "G", warp.Remarks));

        var inner = Chain("C1", a, warp);
        await Refused(AetherContext(NewConfig(), Chain("chain", inner, x), inner, x, a, warp),
            string.Format(ResUI.MsgAetherInGroupHop, "C1", warp.Remarks));

        // A group that is the main profile, not a hop, may hold one.
        var main = CoreConfigTestFactory.CreatePolicyGroupNode(ECoreType.Xray, "main-group", "main", [warp.IndexId, y.IndexId]);
        await Succeeds(AetherContext(NewConfig(), main, warp, y));
    }
}
