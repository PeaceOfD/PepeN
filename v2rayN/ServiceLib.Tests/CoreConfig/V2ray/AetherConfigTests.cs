using TUnit.Assertions.Enums;

namespace ServiceLib.Tests.CoreConfig.V2ray;

/// <summary>
/// PattN (design 2.7, 2.9; PattNG CoreConfigManagerTest and CoreOutboundBuilderTest): the Xray configuration of a
/// profile that runs on an Aether core. Its outbound is a SOCKS one to the core; the core dials out through Xray's
/// secondary-socks inbound, three ports above the Aether listen port, which a rule ahead of every other sends out by the
/// exit-node; and one core has to serve every Aether outbound of the configuration.
/// </summary>
public class AetherConfigTests
{
    private const int P = AetherPorts.DefaultListenPort;
    private const int Secondary = P + AetherPorts.SecondaryOffset;
    private const string Mask = """{"tcp":[{"type":"fragment","settings":{"packets":"tlshello"}}]}""";

    internal static Config NewConfig(Action<Config>? change = null)
    {
        var config = CoreConfigTestFactory.CreateConfig(ECoreType.Xray);
        config.CoreBasicItem.AetherListenPort = P;
        change?.Invoke(config);
        CoreConfigTestFactory.BindAppManagerConfig(config);
        return config;
    }

    internal static CoreConfigContext AetherContext(Config config, ProfileItem node, params ProfileItem[] others)
    {
        var context = CoreConfigTestFactory.CreateContext(config, node, ECoreType.Xray);
        foreach (var other in others)
        {
            context.AllProxiesMap[other.IndexId] = other;
        }
        return context;
    }

    internal static ProfileItem Warp(string id = "warp", string remarks = "WARP", Func<ProtocolExtraItem, ProtocolExtraItem>? extra = null)
    {
        return CoreConfigTestFactory.CreateAetherNode(id, remarks, AetherProtocol.Masque.Type, extra);
    }

    internal static ProfileItem Wg(string id = "wg", string remarks = "WG")
    {
        return CoreConfigTestFactory.CreateAetherNode(id, remarks, AetherProtocol.WireGuard.Type);
    }

    internal static ProfileItem Vless(string id, string remarks, string address = "203.0.113.7")
    {
        var node = new ProfileItem
        {
            IndexId = id,
            ConfigType = EConfigType.VLESS,
            CoreType = ECoreType.Xray,
            Remarks = remarks,
            Address = address,
            Port = 443,
            Password = Guid.NewGuid().ToString(),
            Network = nameof(ETransport.raw),
            StreamSecurity = string.Empty,
            Subid = string.Empty,
        };
        node.SetProtocolExtra(new ProtocolExtraItem { VlessEncryption = Global.None, Flow = string.Empty });
        return node;
    }

    internal static RoutingItem RuleTo(string outboundTag)
    {
        return new RoutingItem
        {
            Id = $"r-{outboundTag}",
            Remarks = "aether",
            RuleSet = JsonUtils.Serialize(new List<RulesItem>
            {
                new()
                {
                    Enabled = true,
                    RuleType = ERuleType.Routing,
                    OutboundTag = outboundTag,
                    Domain = ["full:rule.example.com"],
                },
            }),
            DomainStrategy = Global.AsIs,
            DomainStrategy4Singbox = string.Empty,
        };
    }

    internal sealed record Generated(RetResult Result, CoreConfigContext Context)
    {
        public string Json => Result.Data!.ToString()!;

        public JsonObject Node => JsonNode.Parse(Json)!.AsObject();

        public V2rayConfig Config => JsonUtils.Deserialize<V2rayConfig>(Json)!;

        public Outbounds4Ray Outbound(string tag) => Config.outbounds.Single(outbound => outbound.tag == tag);

        public JsonObject OutboundNode(string tag) =>
            Node["outbounds"]!.AsArray().OfType<JsonObject>().Single(outbound => outbound["tag"]!.GetValue<string>() == tag);

        public List<string> Tags => Config.outbounds.Select(outbound => outbound.tag).ToList();

        public AetherCore Core => Context.Aether.Core!;
    }

    internal static async Task<Generated> Succeeds(CoreConfigContext context)
    {
        var result = new CoreConfigV2rayService(context).GenerateClientConfigContent();
        await result.Success.Should().BeTrue().Because(result.Msg ?? string.Empty);
        return new Generated(result, context);
    }

    internal static async Task Refused(CoreConfigContext context, string message)
    {
        var result = new CoreConfigV2rayService(context).GenerateClientConfigContent();
        await result.Success.Should().BeFalse();
        await result.Msg.Should().BeEqualTo(message);
        await context.Aether.Core.Should().BeNull();
    }

    private static async Task AssertSocksToTheCore(Outbounds4Ray outbound, int port = P)
    {
        await outbound.protocol.Should().BeEqualTo("socks");
        await outbound.settings.address!.ToString().Should().BeEqualTo(Global.Loopback);
        await outbound.settings.port.Should().BeEqualTo(port);
        await outbound.streamSettings.Should().BeNull();
        await outbound.mux.enabled.Should().BeFalse();
    }

    // ---- the outbound, the inbound, the rule and the exit-node

    [Test]
    public async Task TheMainAetherProfileIsASocksOutboundToItsCore()
    {
        var g = await Succeeds(AetherContext(NewConfig(), Warp()));

        var proxy = g.Outbound(Global.ProxyTag);
        await AssertSocksToTheCore(proxy);
        await proxy.targetStrategy.Should().BeEqualTo(AetherOutbound.ForceIPv4v6);
        // No TLS, finalMask or dialMode of its own: what it reaches is on the loopback address.
        await g.OutboundNode(Global.ProxyTag).ContainsKey("streamSettings").Should().BeFalse();
    }

    // PattNG whatTheAetherCoreSendsOutLeavesThroughXray
    [Test]
    public async Task whatTheAetherCoreSendsOutLeavesThroughXray()
    {
        var g = await Succeeds(AetherContext(NewConfig(), Warp()));

        var inbound = g.Config.inbounds.Single(inbound => inbound.tag == Global.AetherSecondarySocksTag);
        await inbound.port.Should().BeEqualTo(Secondary);
        await inbound.protocol.Should().BeEqualTo("mixed");
        await inbound.listen.Should().BeEqualTo(Global.Loopback);
        await inbound.settings.udp.Should().BeEqualTo(true);
        await inbound.settings.auth.Should().BeEqualTo("noauth");
        // No sniffing: the destination the core gives is used as it is.
        var inboundNode = g.Node["inbounds"]!.AsArray().OfType<JsonObject>().Single(node => node["tag"]!.GetValue<string>() == Global.AetherSecondarySocksTag);
        await inboundNode.ContainsKey("sniffing").Should().BeFalse();

        var exitNode = g.Outbound(Global.AetherExitNodeTag);
        await exitNode.protocol.Should().BeEqualTo("freedom");
        await exitNode.mux.Should().BeNull();
        await exitNode.streamSettings.Should().BeNull();
        // After every other outbound.
        await g.Tags.Last().Should().BeEqualTo(Global.AetherExitNodeTag);

        // What comes in on that inbound goes out by that outbound, before any other rule is asked.
        var first = g.Config.routing.rules.First();
        await first.inboundTag.Should().BeEquivalentTo([Global.AetherSecondarySocksTag], CollectionOrdering.Matching);
        await first.outboundTag.Should().BeEqualTo(Global.AetherExitNodeTag);
        await g.Config.routing.rules.Count(rule => rule.outboundTag == Global.AetherExitNodeTag).Should().BeEqualTo(1);

        await g.Core.Arguments.TakeLast(2).Should().BeEquivalentTo(["--upstream", $"socks5://127.0.0.1:{Secondary}"], CollectionOrdering.Matching);
        await g.Core.Should().BeEqualTo(AetherCore.Of(Warp(), P).Through(Secondary));
        // A session's load checks that port before Xray starts (D3).
        await g.Context.Aether.SecondaryInboundPort.Should().BeEqualTo(Secondary);
    }

    // PattNG theExitNodeCarriesTheFinalMaskAndDialModeOfTheAetherProfile, test_toOutboundAetherExit_carriesTheFinalMaskAndDialModeOfTheAetherProfile
    [Test]
    public async Task theExitNodeCarriesTheFinalMaskAndDialModeOfTheAetherProfile()
    {
        var warp = Warp();
        warp.Finalmask = Mask;
        warp.DialMode = "code-1";

        var g = await Succeeds(AetherContext(NewConfig(), warp));

        var exitNode = g.OutboundNode(Global.AetherExitNodeTag);
        await JsonNode.DeepEquals(exitNode["streamSettings"]!["finalmask"], JsonNode.Parse(Mask)).Should().BeTrue();
        await exitNode["streamSettings"]!["sockopt"]!["dialMode"]!.GetValue<string>().Should().BeEqualTo("code-1");
        // A freedom outbound has no transport to name.
        await exitNode["streamSettings"]!.AsObject().ContainsKey("network").Should().BeFalse();
        await exitNode.ContainsKey("mux").Should().BeFalse();
        // The outbound to the core stays as it was: what it reaches is on the loopback address.
        await g.Outbound(Global.ProxyTag).streamSettings.Should().BeNull();
        await g.Core.Exit.Should().BeEqualTo(new AetherExit(Mask, "code-1"));

        // dialMode alone.
        var dialOnly = Warp();
        dialOnly.DialMode = "code-1";
        var only = await Succeeds(AetherContext(NewConfig(), dialOnly));
        var onlyExit = only.OutboundNode(Global.AetherExitNodeTag);
        await onlyExit["streamSettings"]!["sockopt"]!["dialMode"]!.GetValue<string>().Should().BeEqualTo("code-1");
        await onlyExit["streamSettings"]!.AsObject().ContainsKey("finalmask").Should().BeFalse();
    }

    [Test]
    public async Task TheListenPortOfTheSettingsIsWhereTheCoreListensAndThreeAboveWhereItDialsOut()
    {
        var g = await Succeeds(AetherContext(NewConfig(config => config.CoreBasicItem.AetherListenPort = 20000), Warp()));

        await AssertSocksToTheCore(g.Outbound(Global.ProxyTag), 20000);
        await g.Config.inbounds.Single(inbound => inbound.tag == Global.AetherSecondarySocksTag).port.Should().BeEqualTo(20003);
        await g.Core.Should().BeEqualTo(AetherCore.Of(Warp(), 20000).Through(20003));
        await g.Core.Ports.Should().BeEquivalentTo([20000], CollectionOrdering.Matching);
    }

    // PattN (critique 14, PattNG toConfigResult): an exported configuration names the core it runs on.
    [Test]
    public async Task TheCommandOfTheCoreIsTheFirstKey()
    {
        var g = await Succeeds(AetherContext(NewConfig(), Warp()));

        var first = g.Node.First();
        await first.Key.Should().BeEqualTo("aetherCommand");
        await first.Value!.GetValue<string>().Should().BeEqualTo(g.Core.Command);
        await g.Core.Command.Should().EndWith($"--upstream socks5://127.0.0.1:{Secondary}");

        var plain = await Succeeds(AetherContext(NewConfig(), CoreConfigTestFactory.CreateVmessNode(ECoreType.Xray)));
        await plain.Node.ContainsKey("aetherCommand").Should().BeFalse();
        await plain.Context.Aether.Core.Should().BeNull();
        await plain.Config.inbounds.Any(inbound => inbound.tag == Global.AetherSecondarySocksTag).Should().BeFalse();
        await plain.Tags.Contains(Global.AetherExitNodeTag).Should().BeFalse();
    }

    // ---- targetStrategy

    [Test]
    public async Task TheDefaultTargetStrategyFollowsWhereTheTrafficLeaves()
    {
        static async Task<string?> StrategyOf(ProfileItem warp)
        {
            var g = await Succeeds(AetherContext(NewConfig(), warp));
            return g.Outbound(Global.ProxyTag).targetStrategy;
        }

        var warp = await StrategyOf(Warp());
        await warp.Should().BeEqualTo(AetherOutbound.ForceIPv4v6);
        var torAround = await StrategyOf(Warp(extra: extra => extra with { AetherTor = AetherTor.Reverse.Type }));
        await torAround.Should().BeEqualTo(AetherOutbound.ForceIPv4v6);
        var torInside = await StrategyOf(Warp(extra: extra => extra with { AetherTor = AetherTor.Chain.Type }));
        await torInside.Should().BeNull();
        var psiphonOnly = await StrategyOf(Warp(extra: extra => extra with { AetherPsiphon = AetherPsiphon.Only.Type }));
        await psiphonOnly.Should().BeNull();
        // critique 5: a stored AsIs stays AsIs, any other choice is kept.
        var asIs = Warp();
        asIs.TargetStrategy = Global.AsIs;
        await (await StrategyOf(asIs)).Should().BeNull();
        var chosen = Warp();
        chosen.TargetStrategy = "UseIPv6";
        await (await StrategyOf(chosen)).Should().BeEqualTo("UseIPv6");
    }

    // PattN (critique 6): the global proxy strategies pass the Aether outbounds and the exit-node by.
    [Test]
    public async Task TheProxyStrategiesOfTheDnsSettingsLeaveTheAetherOutboundsAlone()
    {
        var config = NewConfig(config =>
        {
            config.SimpleDNSItem.Strategy4Proxy = "UseIPv4";
            config.SimpleDNSItem.Strategy4ProxyDial = "UseIPv6";
        });
        var torChain = Warp(extra: extra => extra with { AetherTor = AetherTor.Chain.Type });
        var vless = Vless("vless-rule", "rule vless");
        var context = AetherContext(config, torChain) with { RoutingItem = RuleTo(vless.Remarks) };
        context.AllProxiesMap[$"remark:{vless.Remarks}"] = vless;

        var g = await Succeeds(context);

        var proxy = g.Outbound(Global.ProxyTag);
        // Tor looks names up at its exit: a forced lookup would drop .onion names.
        await proxy.targetStrategy.Should().BeNull();
        await proxy.streamSettings.Should().BeNull();
        var exitNode = g.Outbound(Global.AetherExitNodeTag);
        await exitNode.targetStrategy.Should().BeNull();
        await (exitNode.streamSettings?.sockopt?.domainStrategy).Should().BeNull();
        // Other proxies get them as before.
        var rule = g.Config.outbounds.Single(outbound => outbound.tag.StartsWith(vless.IndexId, StringComparison.Ordinal));
        await rule.targetStrategy.Should().BeEqualTo("UseIPv4");
        await rule.streamSettings.sockopt!.domainStrategy.Should().BeEqualTo("UseIPv6");

        var warp = await Succeeds(AetherContext(config, Warp()));
        await warp.Outbound(Global.ProxyTag).targetStrategy.Should().BeEqualTo(AetherOutbound.ForceIPv4v6);
    }

    // ---- the exit-node a profile names

    private static ProfileItem Noded(string node = "germany")
    {
        return Warp(extra: extra => extra with { AetherExitNode = node });
    }

    // PattNG aProfileChosenAsTheExitNodeIsTheExitNodeChangedInItsTagAlone, theSessionsCoreCarriesTheContentOfItsExitNodeInItsKey,
    // test_toOutboundAetherExit_takesTheOutboundOfItsNodeChangedInItsTagAndPassingEveryNameOn
    [Test]
    public async Task aProfileChosenAsTheExitNodeIsTheExitNodeChangedInItsTagAlone()
    {
        var germany = Vless("germany-id", "germany", "198.51.100.9");
        germany.TargetStrategy = AetherOutbound.ForceIPv4v6;
        var context = AetherContext(NewConfig(), Noded());
        context.Aether.ExitNodes["germany"] = ExitNodeLookup.Of(new ByName<ProfileItem>.One(germany));

        var g = await Succeeds(context);

        var exitNode = g.Outbound(Global.AetherExitNodeTag);
        await exitNode.protocol.Should().BeEqualTo("vless");
        await exitNode.settings.address!.ToString().Should().BeEqualTo("198.51.100.9");
        // What the core sends itself is not looked up by Xray, whose DNS would reach out through the core.
        await exitNode.targetStrategy.Should().BeNull();
        await g.Config.routing.rules.First().outboundTag.Should().BeEqualTo(Global.AetherExitNodeTag);
        await g.Tags.Count(tag => tag == Global.AetherExitNodeTag).Should().BeEqualTo(1);
        // The key of the exit-node carries the content of that profile, so that a test tells it from a changed one.
        await g.Core.Exit.Should().BeEqualTo(new AetherExit(Node: "germany", NodeContent: AetherExit.ContentOf(germany)));
        await g.Core.Arguments.Last().Should().BeEqualTo($"socks5://127.0.0.1:{Secondary}");
    }

    // PattN (critique 18): a named exit-node resolves its domains as the other proxies do; a freedom one keeps its own.
    [Test]
    public async Task ANamedExitNodeDialsItsDomainsAsTheOtherProxiesDo()
    {
        var config = NewConfig(config =>
        {
            config.SimpleDNSItem.Strategy4Proxy = "UseIPv4";
            config.SimpleDNSItem.Strategy4ProxyDial = "UseIPv6";
        });
        var context = AetherContext(config, Noded());
        context.Aether.ExitNodes["germany"] = ExitNodeLookup.Of(new ByName<ProfileItem>.One(Vless("germany-id", "germany")));

        var g = await Succeeds(context);
        var exitNode = g.Outbound(Global.AetherExitNodeTag);
        await exitNode.streamSettings.sockopt!.domainStrategy.Should().BeEqualTo("UseIPv6");
        await exitNode.targetStrategy.Should().BeNull();

        var freedom = await Succeeds(AetherContext(config, Warp()));
        await freedom.Outbound(Global.AetherExitNodeTag).streamSettings.Should().BeNull();
    }

    // The freedom exit-node carries the Aether core's own connection to its server, as a proxy's outbound carries the
    // proxy's: the settings that steer every proxy's server connection steer it too, as they do a named exit-node.
    [Test]
    public async Task TheFreedomExitNodeIsBoundAsAProxysServerConnectionIs()
    {
        var config = NewConfig(config =>
        {
            config.CoreBasicItem.BindInterface = "Ethernet 2";
            config.CoreBasicItem.SendThrough = "192.0.2.20";
        });
        var vless = Vless("vless-bound", "bound vless");
        var context = AetherContext(config, Warp()) with { RoutingItem = RuleTo(vless.Remarks) };
        context.AllProxiesMap[$"remark:{vless.Remarks}"] = vless;

        var g = await Succeeds(context);

        var exitNode = g.Outbound(Global.AetherExitNodeTag);
        await exitNode.protocol.Should().BeEqualTo("freedom");
        await exitNode.sendThrough.Should().BeEqualTo("192.0.2.20");
        await exitNode.streamSettings.sockopt!.Interface.Should().BeEqualTo("Ethernet 2");
        var rule = g.Config.outbounds.Single(outbound => outbound.tag.StartsWith(vless.IndexId, StringComparison.Ordinal));
        await rule.sendThrough.Should().BeEqualTo("192.0.2.20");
        await rule.streamSettings.sockopt!.Interface.Should().BeEqualTo("Ethernet 2");
        // The outbound to the core on the loopback address, and the direct one, are left alone.
        await g.Outbound(Global.ProxyTag).sendThrough.Should().BeNull();
        await g.Outbound(Global.ProxyTag).streamSettings.Should().BeNull();
        await g.Outbound(Global.DirectTag).sendThrough.Should().BeNull();
        await (g.Outbound(Global.DirectTag).streamSettings?.sockopt?.Interface).Should().BeNull();
    }

    [Test]
    public async Task UnderTheTunTheFreedomExitNodeResolvesAsTheDirectOutboundDoes()
    {
        // The direct outbound's family, in its Force form (a Use one would fall back to the OS resolver, whose query enters
        // the TUN), with the direct outbound's happyEyeballs: without them Xray dials one address at random, an IPv6
        // one half the time on a network whose IPv6 does not route.
        var config = CoreConfigTestFactory.CreateConfigWithTun(ECoreType.Xray, enableIPv6Address: false);
        config.CoreBasicItem.AetherListenPort = P;
        CoreConfigTestFactory.BindAppManagerConfig(config);

        var g = await Succeeds(AetherContext(config, Warp()));

        var exitNode = g.OutboundNode(Global.AetherExitNodeTag);
        var direct = g.OutboundNode(Global.DirectTag);
        await exitNode["streamSettings"]!["sockopt"]!["domainStrategy"]!.GetValue<string>().Should().BeEqualTo("ForceIP");
        await direct["streamSettings"]!["sockopt"]!["domainStrategy"]!.GetValue<string>().Should().BeEqualTo("ForceIP");
        await (exitNode["streamSettings"]!["sockopt"]!["happyEyeballs"] is JsonObject).Should().BeTrue();
        await JsonNode.DeepEquals(exitNode["streamSettings"]!["sockopt"]!["happyEyeballs"], direct["streamSettings"]!["sockopt"]!["happyEyeballs"]).Should().BeTrue();

        // A family of its own, with the happyEyeballs of the settings.
        config.SimpleDNSItem.Strategy4Freedom = "UseIPv4";
        config.SimpleDNSItem.EnableHappyEyeballs = true;
        config.HappyEyeballs4RayItem = new HappyEyeballs4RayItem { TryDelayMs = 150, PrioritizeIPv6 = false, Interleave = 1, MaxConcurrentTry = 4 };

        var chosen = await Succeeds(AetherContext(config, Warp()));

        var chosenExit = chosen.OutboundNode(Global.AetherExitNodeTag);
        var chosenDirect = chosen.OutboundNode(Global.DirectTag);
        await chosenDirect["streamSettings"]!["sockopt"]!["domainStrategy"]!.GetValue<string>().Should().BeEqualTo("UseIPv4");
        await chosenExit["streamSettings"]!["sockopt"]!["domainStrategy"]!.GetValue<string>().Should().BeEqualTo("ForceIPv4");
        await chosenExit["streamSettings"]!["sockopt"]!["happyEyeballs"]!["tryDelayMs"]!.GetValue<int>().Should().BeEqualTo(150);
        await JsonNode.DeepEquals(chosenExit["streamSettings"]!["sockopt"]!["happyEyeballs"], chosenDirect["streamSettings"]!["sockopt"]!["happyEyeballs"]).Should().BeTrue();
    }

    // PattN (design Q11): a custom outbound made for Xray can be the exit-node, its outbound replacing the placeholder by
    // the exit-node's tag, as for a chain's hop; its digest is that of its outbound, not of its file's name.
    [Test]
    public async Task ACustomOutboundCanBeTheExitNode()
    {
        // A targetStrategy its file sets goes as well: the exit-node passes every name the core sends on as it is (design
        // 2.7.4), since a name Xray looked up for it would be asked of the DNS that reaches out through the core itself.
        const string outbound = """{"tag": "{{tag}}", "protocol": "trojan", "targetStrategy": "UseIPv4", "settings": {"address": "198.51.100.20", "port": 443, "password": "x"}}""";
        var custom = CoreConfigTestFactory.CreateCustomOutboundNode(ECoreType.Xray, "custom-id", "custom");
        var context = AetherContext(NewConfig(), Noded("custom"));
        context.CustomOutboundContent[custom.IndexId] = outbound;
        context.Aether.ExitNodes["custom"] =
            ExitNodeLookup.Of(new ByName<ProfileItem>.One(custom), profile => context.CustomOutboundContent.GetValueOrDefault(profile.IndexId));

        var g = await Succeeds(context);

        var exitNode = g.OutboundNode(Global.AetherExitNodeTag);
        await exitNode["protocol"]!.GetValue<string>().Should().BeEqualTo("trojan");
        await exitNode["settings"]!["address"]!.GetValue<string>().Should().BeEqualTo("198.51.100.20");
        await exitNode.ContainsKey("targetStrategy").Should().BeFalse();
        await g.Core.Exit.NodeContent.Should().BeEqualTo(AetherExit.ContentOf(custom, _ => outbound));
        await g.Core.Exit.NodeContent.Should().NotBeEqualTo(AetherExit.ContentOf(custom));

        // Another outbound of the same file keeps what it sets.
        var vmess = CoreConfigTestFactory.CreateVmessNode(ECoreType.Xray);
        var other = CoreConfigTestFactory.CreateCustomOutboundNode(ECoreType.Xray, "custom-rule", "custom rule");
        var ruled = AetherContext(NewConfig(), vmess) with { RoutingItem = RuleTo(other.Remarks) };
        ruled.CustomOutboundContent[other.IndexId] = outbound;
        ruled.AllProxiesMap[$"remark:{other.Remarks}"] = other;
        var withRule = await Succeeds(ruled);
        await withRule.OutboundNode($"{other.IndexId}-{Global.ProxyTag}-{other.Remarks}")["targetStrategy"]!.GetValue<string>().Should().BeEqualTo("UseIPv4");
    }

    // PattNG aCoreWhoseExitNodeNameGivesNoOutboundDoesNotStartWithoutIt
    [Test]
    public async Task aCoreWhoseExitNodeNameGivesNoOutboundDoesNotStartWithoutIt()
    {
        var cases = new (ExitNodeLookup? Lookup, string Message)[]
        {
            (ExitNodeLookup.NotFound.Instance, ResUI.MsgAetherExitNodeNotFound),
            (ExitNodeLookup.SameName.Instance, ResUI.MsgAetherExitNodeDuplicate),
            (ExitNodeLookup.NoOutbound.Instance, ResUI.MsgAetherExitNodeUnusable),
            (ExitNodeLookup.EchUnusable.Instance, ResUI.MsgAetherExitNodeEchUnusable),
            // Never looked up: as a name no profile has.
            (null, ResUI.MsgAetherExitNodeNotFound),
        };
        foreach (var (lookup, message) in cases)
        {
            var context = AetherContext(NewConfig(), Noded("gone"));
            if (lookup != null)
            {
                context.Aether.ExitNodes["gone"] = lookup;
            }
            await Refused(context, string.Format(message, "gone"));
        }

        // A core with an upstream of its own dials out through no exit-node at all.
        var own = Warp(extra: extra => extra with
        {
            AetherExitNode = "gone",
            AetherCommand = "aether --bind 127.0.0.1:10819 --protocol masque --upstream socks5://127.0.0.1:1080",
        });
        var ownContext = AetherContext(NewConfig(), own);
        ownContext.Aether.ExitNodes["gone"] = ExitNodeLookup.NotFound.Instance;
        await Succeeds(ownContext);
    }

    // PattNG aCoreWithAnUpstreamOfItsOwnLeavesTheConfigurationAlone
    [Test]
    public async Task aCoreWithAnUpstreamOfItsOwnLeavesTheConfigurationAlone()
    {
        const string command = "aether --bind 127.0.0.1:10819 --protocol masque --upstream socks5://127.0.0.1:1080";
        var own = Warp(extra: extra => extra with { AetherCommand = command });

        var g = await Succeeds(AetherContext(NewConfig(), own));

        await g.Config.inbounds.Any(inbound => inbound.tag == Global.AetherSecondarySocksTag).Should().BeFalse();
        await g.Tags.Contains(Global.AetherExitNodeTag).Should().BeFalse();
        await g.Config.routing.rules.Any(rule => rule.outboundTag == Global.AetherExitNodeTag).Should().BeFalse();
        await g.Core.Should().BeEqualTo(AetherCore.Of(own, P));
        await g.Core.Arguments.Should().BeEquivalentTo(AetherCore.ArgumentsOf(command), CollectionOrdering.Matching);
        // No inbound of Xray's for the core, whose port a session's load would check (D3).
        await g.Context.Aether.SecondaryInboundPort.Should().BeNull();
        await g.Node.First().Key.Should().BeEqualTo("aetherCommand");
        await AssertSocksToTheCore(g.Outbound(Global.ProxyTag));
    }

    // ---- groups and routing rules

    [Test]
    public async Task APolicyGroupOfOneTunnelUnderTwoNamesSharesOneCore()
    {
        var warp = Warp();
        var again = Warp("warp-again", "WARP again");
        var vless = Vless("vless", "vless");
        var group = CoreConfigTestFactory.CreatePolicyGroupNode(ECoreType.Xray, "group", "group", [warp.IndexId, vless.IndexId, again.IndexId]);

        var g = await Succeeds(AetherContext(NewConfig(), group, warp, vless, again));

        await AssertSocksToTheCore(g.Outbound("proxy-1-WARP"));
        await AssertSocksToTheCore(g.Outbound("proxy-3-WARP again"));
        await g.Outbound("proxy-2-vless").protocol.Should().BeEqualTo("vless");
        await g.Core.Should().BeEqualTo(AetherCore.Of(warp, P).Through(Secondary));
        // The balancer picks its members by the group's prefix, which the exit-node never has.
        await g.Config.routing.balancers!.Single().selector.Should().BeEquivalentTo([Global.ProxyTag], CollectionOrdering.Matching);
    }

    // PattNG twoDifferentAetherProfilesCannotShareOneCore: a group picked by hand names its profiles, so a conflict is
    // reported (a group a filter fills prunes the extras instead, see AetherContextBuilderTests).
    [Test]
    public async Task twoDifferentAetherProfilesCannotShareOneCore()
    {
        var warp = Warp();
        var wg = Wg();
        var group = CoreConfigTestFactory.CreatePolicyGroupNode(ECoreType.Xray, "group", "group", [warp.IndexId, wg.IndexId]);
        await Refused(AetherContext(NewConfig(), group, warp, wg), ResUI.MsgAetherConfigSingleProfile);

        var context = AetherContext(NewConfig(), warp) with { RoutingItem = RuleTo(wg.Remarks) };
        context.AllProxiesMap[$"remark:{wg.Remarks}"] = wg;
        await Refused(context, ResUI.MsgAetherConfigSingleProfile);
    }

    // PattNG twoProfilesOnOneTunnelWithDifferentExitNodesCannotShareOneCore
    [Test]
    public async Task twoProfilesOnOneTunnelWithDifferentExitNodesCannotShareOneCore()
    {
        var warp = Warp();
        var masked = Warp("masked", "WARP masked");
        masked.Finalmask = Mask;
        var group = CoreConfigTestFactory.CreatePolicyGroupNode(ECoreType.Xray, "group", "group", [warp.IndexId, masked.IndexId]);
        await Refused(AetherContext(NewConfig(), group, warp, masked), ResUI.MsgAetherConfigTwoExits);

        var alsoMasked = Warp("masked-again", "WARP masked again");
        alsoMasked.Finalmask = Mask;
        var same = CoreConfigTestFactory.CreatePolicyGroupNode(ECoreType.Xray, "group", "group", [masked.IndexId, alsoMasked.IndexId]);
        var g = await Succeeds(AetherContext(NewConfig(), same, masked, alsoMasked));
        await g.Core.Exit.Should().BeEqualTo(new AetherExit(Mask));
    }

    // PattNG aRoutingTargetOrAGroupMemberMayBeAetherAnywhere
    [Test]
    public async Task ARoutingRuleMaySendToAnAetherProfile()
    {
        var warp = Warp();
        var context = AetherContext(NewConfig(), CoreConfigTestFactory.CreateVmessNode(ECoreType.Xray)) with { RoutingItem = RuleTo(warp.Remarks) };
        context.AllProxiesMap[$"remark:{warp.Remarks}"] = warp;

        var g = await Succeeds(context);

        var tag = $"{warp.IndexId}-{Global.ProxyTag}-{warp.Remarks}";
        await AssertSocksToTheCore(g.Outbound(tag));
        await g.Config.routing.rules.Any(rule => rule.outboundTag == tag && rule.domain?.Contains("full:rule.example.com") == true).Should().BeTrue();
        await g.Outbound(Global.ProxyTag).protocol.Should().BeEqualTo("vmess");
        await g.Core.Should().BeEqualTo(AetherCore.Of(warp, P).Through(Secondary));
        // The rule of the core comes before the user's rule.
        await g.Config.routing.rules.First().outboundTag.Should().BeEqualTo(Global.AetherExitNodeTag);
    }

    // ---- ports

    [Test]
    public async Task AnInboundOnAPortOfTheCoreRefusesTheConfiguration()
    {
        // The local proxy port on the Aether listen port: Xray would take it first. The app's PAC port, three above the
        // local proxy port, then sits on the port Xray opens for the core three above the listen port, a port the app holds,
        // which is looked at first and named.
        await Refused(AetherContext(NewConfig(config => config.Inbound[0].LocalPort = P), Warp()), string.Format(ResUI.MsgAetherListenPortTaken, Secondary));
        // On the port three above it, which the core dials out through.
        await Refused(AetherContext(NewConfig(config => config.Inbound[0].LocalPort = Secondary), Warp()),
            string.Format(ResUI.MsgAetherSecondaryPortTaken, Secondary));
        // Elsewhere, nothing in the way.
        await Succeeds(AetherContext(NewConfig(config => config.Inbound[0].LocalPort = Secondary + 1), Warp()));
    }

    // PattN (critique 10): a port the app holds besides its inbounds, which no inbound check sees.
    [Test]
    [NotInParallel("AppManager.StatePort")]
    public async Task AStatePortOfTheAppOnAPortOfTheCoreRefusesTheConfiguration()
    {
        const int listenPort = 47000;
        var field = typeof(AppManager).GetField("_statePort", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var config = NewConfig(config => config.CoreBasicItem.AetherListenPort = listenPort);
        _ = AppManager.Instance.StatePort;
        var saved = field.GetValue(AppManager.Instance);
        try
        {
            foreach (var port in new[] { listenPort, listenPort + AetherPorts.SecondaryOffset })
            {
                field.SetValue(AppManager.Instance, port);
                await Refused(AetherContext(config, Warp()), string.Format(ResUI.MsgAetherListenPortTaken, port));
            }
            // The two ports between are Tor's and Psiphon's, which this core does not take.
            field.SetValue(AppManager.Instance, listenPort + 1);
            await Succeeds(AetherContext(config, Warp()));
        }
        finally
        {
            field.SetValue(AppManager.Instance, saved);
        }
    }

    // The PAC server of the system proxy's PAC mode on Windows listens three above the local proxy port, outside Xray:
    // with the local proxy port at 10816 that is the default Aether listen port, and the core could not listen there.
    [Test]
    [NotInParallel("AppManager.StatePort")]
    public async Task ThePacPortOfTheAppOnAPortOfTheCoreRefusesTheConfiguration()
    {
        var statePort = typeof(AppManager).GetField("_statePort", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var statePort2 = typeof(AppManager).GetField("_statePort2", BindingFlags.Instance | BindingFlags.NonPublic)!;
        _ = NewConfig();
        _ = AppManager.Instance.StatePort;
        _ = AppManager.Instance.StatePort2;
        var saved = (statePort.GetValue(AppManager.Instance), statePort2.GetValue(AppManager.Instance));
        try
        {
            // The ports of the app's own APIs elsewhere.
            statePort.SetValue(AppManager.Instance, 47100);
            statePort2.SetValue(AppManager.Instance, 47200);

            // The configuration's own local proxy port counts; the app's configuration, which other tests bind, is left alone.
            static Config WithLocalPort(int port)
            {
                var config = CoreConfigTestFactory.CreateConfig(ECoreType.Xray);
                config.CoreBasicItem.AetherListenPort = P;
                config.Inbound[0].LocalPort = port;
                return config;
            }
            await (P - 3 + (int)EInboundProtocol.pac).Should().BeEqualTo(P);
            await Refused(AetherContext(WithLocalPort(P - 3), Warp()), string.Format(ResUI.MsgAetherListenPortTaken, P));

            // One above the listen port, where this core has no listener, it is no matter.
            await Succeeds(AetherContext(WithLocalPort(P - 2), Warp()));
        }
        finally
        {
            statePort.SetValue(AppManager.Instance, saved.Item1);
            statePort2.SetValue(AppManager.Instance, saved.Item2);
        }
    }

    // ---- fragments

    [Test]
    public async Task TheFinalFragmentFragmentsTheTrafficOfAnAetherProfileAsAnyProxys()
    {
        // The final fragment puts a fragmenting freedom in front of each proxy under its tag, which dials through the proxy:
        // it fragments the user's own stream inside the tunnel, never the loopback hop to the core.
        var warp = Warp();
        var vless = Vless("vless", "vless");
        var group = CoreConfigTestFactory.CreatePolicyGroupNode(ECoreType.Xray, "group", "group", [warp.IndexId, vless.IndexId]);
        var config = NewConfig(config =>
        {
            config.CoreBasicItem.EnableFragment = true;
            config.CoreBasicItem.EnableFinalFragment = true;
        });

        var g = await Succeeds(AetherContext(config, group, warp, vless));

        foreach (var member in new[] { "proxy-1-WARP", "proxy-2-vless" })
        {
            var front = g.Outbound(member);
            await front.protocol.Should().BeEqualTo("freedom").Because(member);
            await front.streamSettings.sockopt!.dialerProxy.Should().BeEqualTo($"fragment-{member}").Because(member);
            await (g.OutboundNode(member)["streamSettings"]!["finalmask"] is JsonObject).Should().BeTrue().Because(member);
        }
        // Behind it, the outbound to the core as it was: the fragment of the other settings applies to TLS outbounds.
        await AssertSocksToTheCore(g.Outbound("fragment-proxy-1-WARP"));
        await g.Outbound("fragment-proxy-2-vless").protocol.Should().BeEqualTo("vless");
        // The exit-node is no proxy the user's traffic takes: it is left as it is.
        await g.Outbound(Global.AetherExitNodeTag).protocol.Should().BeEqualTo("freedom");
        await g.Outbound(Global.AetherExitNodeTag).streamSettings.Should().BeNull();
        await g.Core.Should().BeEqualTo(AetherCore.Of(warp, P).Through(Secondary));

        var main = await Succeeds(AetherContext(config, Warp()));
        await main.Outbound(Global.ProxyTag).streamSettings.sockopt!.dialerProxy.Should().BeEqualTo("fragment-proxy");
        await AssertSocksToTheCore(main.Outbound("fragment-proxy"));
    }

    // ---- TUN

    [Test]
    public async Task UnderTheTunTheFreedomExitNodeResolvesWithXraysDns()
    {
        var config = CoreConfigTestFactory.CreateConfigWithTun(ECoreType.Xray, enableIPv6Address: false);
        config.CoreBasicItem.AetherListenPort = P;
        CoreConfigTestFactory.BindAppManagerConfig(config);

        var g = await Succeeds(AetherContext(config, Warp()));

        await g.Outbound(Global.AetherExitNodeTag).streamSettings.sockopt!.domainStrategy.Should().BeEqualTo("ForceIP");
        await AssertSocksToTheCore(g.Outbound(Global.ProxyTag));
    }

    [Test]
    [NotInParallel("bin/aether")]
    public async Task UnderTheTunWhatTheAetherProgramsDialThemselvesGoesDirect()
    {
        var config = CoreConfigTestFactory.CreateConfigWithTun(ECoreType.Xray, enableIPv6Address: false);
        config.CoreBasicItem.AetherListenPort = P;
        CoreConfigTestFactory.BindAppManagerConfig(config);
        // A helper there to be named; the folder is the test run's own bin/aether.
        var lyrebird = AetherBinaries.FileIn(AetherBinaries.Folder, AetherBinaries.LyrebirdName);
        var created = !File.Exists(lyrebird);
        if (created)
        {
            Directory.CreateDirectory(AetherBinaries.Folder);
            await File.WriteAllTextAsync(lyrebird, "lyrebird");
        }
        try
        {
            var context = AetherContext(config, Warp()) with { ProtectCoreTypeList = [ECoreType.Xray, ECoreType.sing_box, ECoreType.aether] };

            var g = await Succeeds(context);

            var direct = g.Config.routing.rules.First(rule => rule.process != null && rule.outboundTag == Global.DirectTag);
            await direct.process!.Should().Contain(Path.GetFullPath(lyrebird));
            foreach (var file in AetherBinaries.ExistingFiles(AetherBinaries.Folder))
            {
                await direct.process!.Should().Contain(file);
            }
            await direct.process!.Should().Contain("xray/");

            // So does the sing-box TUN in front of Xray, whose context takes the main context's core types.
            var singboxConfig = CoreConfigTestFactory.CreateConfig(ECoreType.sing_box);
            singboxConfig.TunModeItem.EnableTun = true;
            CoreConfigTestFactory.BindAppManagerConfig(singboxConfig);
            var singboxContext = CoreConfigTestFactory.CreateContext(singboxConfig, CoreConfigTestFactory.CreateVmessNode(ECoreType.sing_box), ECoreType.sing_box) with
            {
                IsTunEnabled = true,
                ProtectCoreTypeList = [ECoreType.Xray, ECoreType.sing_box, ECoreType.aether],
            };
            var singbox = new CoreConfigSingboxService(singboxContext).GenerateClientConfigContent();
            await singbox.Success.Should().BeTrue().Because(singbox.Msg ?? string.Empty);
            var singboxDirect = JsonUtils.Deserialize<SingboxConfig>(singbox.Data!.ToString())!.route.rules
                .First(rule => rule.process_path != null && rule.outbound == Global.DirectTag);
            await singboxDirect.process_path!.Should().Contain(Path.GetFullPath(lyrebird));
        }
        finally
        {
            if (created)
            {
                File.Delete(lyrebird);
            }
        }
    }

    // ---- full configuration template

    private static FullConfigTemplateItem Template(string config, bool addProxyOnly = true, string? proxyDetour = null)
    {
        return new FullConfigTemplateItem
        {
            Id = "template",
            Remarks = "template",
            Enabled = true,
            CoreType = ECoreType.Xray,
            Config = config,
            TunConfig = config,
            AddProxyOnly = addProxyOnly,
            ProxyDetour = proxyDetour,
        };
    }

    private const string TemplateConfig = """
        {
          "log": {"loglevel": "warning"},
          "inbounds": [{"tag": "socks", "port": 10808, "listen": "127.0.0.1", "protocol": "mixed"}],
          "outbounds": [{"tag": "detour", "protocol": "freedom"}],
          "routing": {"rules": [{"type": "field", "domain": ["geosite:private"], "outboundTag": "direct"}]}
        }
        """;

    [Test]
    public async Task AFullConfigurationTemplateKeepsTheInboundTheRuleAndTheExitNodeOfTheCore()
    {
        var config = NewConfig();
        var context = CoreConfigTestFactory.CreateContext(config, Warp(), ECoreType.Xray, fullConfigTemplate: Template(TemplateConfig, proxyDetour: "detour"));

        var g = await Succeeds(context);
        var node = g.Node;

        await node.First().Key.Should().BeEqualTo("aetherCommand");
        await node["aetherCommand"]!.GetValue<string>().Should().BeEqualTo(g.Core.Command);
        var inbounds = node["inbounds"]!.AsArray().OfType<JsonObject>().ToList();
        await inbounds.Select(inbound => inbound["tag"]!.GetValue<string>()).Should()
            .BeEquivalentTo(["socks", Global.AetherSecondarySocksTag], CollectionOrdering.Matching);
        await inbounds[1]["port"]!.GetValue<int>().Should().BeEqualTo(Secondary);
        var firstRule = node["routing"]!["rules"]![0]!;
        await firstRule["outboundTag"]!.GetValue<string>().Should().BeEqualTo(Global.AetherExitNodeTag);
        await firstRule["inboundTag"]![0]!.GetValue<string>().Should().BeEqualTo(Global.AetherSecondarySocksTag);
        // The freedom exit-node stays, whatever AddProxyOnly says of other freedom outbounds.
        var tags = node["outbounds"]!.AsArray().Select(outbound => outbound!["tag"]!.GetValue<string>()).ToList();
        await tags.Should().Contain(Global.AetherExitNodeTag);
        await tags.Should().NotContain(Global.DirectTag);
        // The outbound to the core dials it on the loopback address, never through the detour.
        await g.OutboundNode(Global.ProxyTag).ContainsKey("streamSettings").Should().BeFalse();
    }

    [Test]
    public async Task AnInboundOfTheTemplateOnAPortOfTheCoreRefusesTheConfiguration()
    {
        var onSecondary = TemplateConfig.Replace("10808", Secondary.ToString());
        await Refused(CoreConfigTestFactory.CreateContext(NewConfig(), Warp(), ECoreType.Xray, fullConfigTemplate: Template(onSecondary)),
            string.Format(ResUI.MsgAetherSecondaryPortTaken, Secondary));

        // A range, as Xray reads a port, over the listen port.
        var onRange = TemplateConfig.Replace("10808", "\"10800-10820\"");
        await Refused(CoreConfigTestFactory.CreateContext(NewConfig(), Warp(), ECoreType.Xray, fullConfigTemplate: Template(onRange)),
            string.Format(ResUI.MsgAetherListenPortTaken, P));
    }

    // PattN (proxy chains and templates review): a freedom exit-node carries the Aether core's own connection to its
    // server, which goes through the template's detour as every proxy's does.
    [Test]
    public async Task AFreedomExitNodeDialsThroughTheTemplatesDetourAsTheProxiesDo()
    {
        var vless = Vless("vless-detour", "detoured vless");
        var context = CoreConfigTestFactory.CreateContext(NewConfig(), Warp(), ECoreType.Xray, fullConfigTemplate: Template(TemplateConfig, proxyDetour: "detour"))
            with { RoutingItem = RuleTo(vless.Remarks) };
        context.AllProxiesMap[$"remark:{vless.Remarks}"] = vless;

        var g = await Succeeds(context);

        await g.OutboundNode(Global.AetherExitNodeTag)["streamSettings"]!["sockopt"]!["dialerProxy"]!.GetValue<string>().Should().BeEqualTo("detour");
        var rule = g.Node["outbounds"]!.AsArray().OfType<JsonObject>().Single(outbound => outbound["tag"]!.GetValue<string>().StartsWith(vless.IndexId, StringComparison.Ordinal));
        await rule["streamSettings"]!["sockopt"]!["dialerProxy"]!.GetValue<string>().Should().BeEqualTo("detour");
        // The outbound to the core dials it on the loopback address.
        await g.OutboundNode(Global.ProxyTag).ContainsKey("streamSettings").Should().BeFalse();

        // Under the TUN the exit-node has a sockopt of its own already, for its lookups; it dials through the detour all the same.
        var tun = CoreConfigTestFactory.CreateConfigWithTun(ECoreType.Xray, enableIPv6Address: false);
        tun.CoreBasicItem.AetherListenPort = P;
        CoreConfigTestFactory.BindAppManagerConfig(tun);
        var underTun = await Succeeds(CoreConfigTestFactory.CreateContext(tun, Warp(), ECoreType.Xray, fullConfigTemplate: Template(TemplateConfig, proxyDetour: "detour")));
        var tunExit = underTun.OutboundNode(Global.AetherExitNodeTag)["streamSettings"]!["sockopt"]!;
        await tunExit["dialerProxy"]!.GetValue<string>().Should().BeEqualTo("detour");
        await tunExit["domainStrategy"]!.GetValue<string>().Should().BeEqualTo("ForceIP");
    }

    // PattNG aCustomConfigurationWhoseBalancersWouldPickTheExitNodeIsLeftAsItIs: balancers and observatories pick the
    // outbounds whose tags start with a selector; one the exit-node's tag starts with could send what it balances out
    // by the exit-node, directly where that is a freedom one.
    [Test]
    public async Task ATemplateWhoseBalancersWouldPickTheExitNodeIsRefused()
    {
        static string WithRoutingAndMore(string routing, string more) => $$"""
            {
              "log": {"loglevel": "warning"},
              "inbounds": [{"tag": "socks", "port": 10808, "listen": "127.0.0.1", "protocol": "mixed"}],
              "outbounds": [{"tag": "exit-us", "protocol": "freedom"}],
              "routing": {"rules": [{"type": "field", "domain": ["geosite:private"], "balancerTag": "b"}]{{routing}}}{{more}}
            }
            """;
        var cases = new (string Template, string Selector)[]
        {
            (WithRoutingAndMore(""", "balancers": [{"tag": "b", "selector": ["proxy", "exit"]}]""", ""), "exit"),
            (WithRoutingAndMore("", """, "observatory": {"subjectSelector": ["e"]}"""), "e"),
            (WithRoutingAndMore("", """, "burstObservatory": {"subjectSelector": [""]}"""), ""),
        };
        foreach (var (template, selector) in cases)
        {
            await Refused(CoreConfigTestFactory.CreateContext(NewConfig(), Warp(), ECoreType.Xray, fullConfigTemplate: Template(template)),
                string.Format(ResUI.MsgAetherTemplateExitNodeSelected, selector));
        }

        // A selector the exit-node's tag does not start with is no matter.
        var elsewhere = WithRoutingAndMore(""", "balancers": [{"tag": "b", "selector": ["exit-node-", "proxy"]}]""",
            """, "observatory": {"subjectSelector": ["proxy"]}""");
        await Succeeds(CoreConfigTestFactory.CreateContext(NewConfig(), Warp(), ECoreType.Xray, fullConfigTemplate: Template(elsewhere)));
        // Nor does one of a configuration without an Aether core.
        await Succeeds(CoreConfigTestFactory.CreateContext(NewConfig(), CoreConfigTestFactory.CreateVmessNode(ECoreType.Xray), ECoreType.Xray,
            fullConfigTemplate: Template(cases[0].Template)));
    }

    // ---- latency tests

    // PattNG aLatencyTestsOutboundsCarryNoMuxAndPassEveryNameOn, critique 7
    [Test]
    public async Task ALatencyTestsOwnCoreDialsOutThroughTheTestsXray()
    {
        const int testPort = 41808;
        const int testSecondary = 41900;
        var context = AetherContext(NewConfig(), Warp());
        context.Aether.SecondaryPort = testSecondary;

        var result = new CoreConfigV2rayService(context).GenerateClientSpeedtestConfig(testPort);

        await result.Success.Should().BeTrue().Because(result.Msg ?? string.Empty);
        var g = new Generated(result, context);
        // A test has no DNS: every name passes on as it is.
        await g.Outbound(Global.ProxyTag).targetStrategy.Should().BeNull();
        await g.Config.inbounds.Select(inbound => inbound.port ?? 0).Should().BeEquivalentTo([testPort, testSecondary], CollectionOrdering.Matching);
        await g.Config.routing.rules.First().outboundTag.Should().BeEqualTo(Global.AetherExitNodeTag);
        await g.Outbound(Global.AetherExitNodeTag).protocol.Should().BeEqualTo("freedom");
        await g.Core.Should().BeEqualTo(AetherCore.Of(Warp(), P).Through(testSecondary));
        await g.Node.First().Key.Should().BeEqualTo("aetherCommand");

        // Through a chain's hop, the exit-node passes every name on as well.
        var hop = Vless("hop", "hop");
        hop.TargetStrategy = "UseIPv4";
        var chain = CoreConfigTestFactory.CreateProxyChainNode(ECoreType.Xray, "chain", "chain", [hop.IndexId, "warp"]);
        var chained = AetherContext(NewConfig(), chain, Warp(), hop);
        chained.Aether.SecondaryPort = testSecondary;
        var chainResult = new CoreConfigV2rayService(chained).GenerateClientSpeedtestConfig(testPort);
        await chainResult.Success.Should().BeTrue().Because(chainResult.Msg ?? string.Empty);
        await new Generated(chainResult, chained).Outbound(Global.AetherExitNodeTag).targetStrategy.Should().BeNull();
    }

    [Test]
    public async Task ALatencyTestThroughTheLiveCoreLeavesItsRoutingToTheSession()
    {
        var context = AetherContext(NewConfig(), Warp());

        var result = new CoreConfigV2rayService(context).GenerateClientSpeedtestConfig(41809);

        await result.Success.Should().BeTrue().Because(result.Msg ?? string.Empty);
        var g = new Generated(result, context);
        await AssertSocksToTheCore(g.Outbound(Global.ProxyTag));
        await g.Outbound(Global.ProxyTag).targetStrategy.Should().BeNull();
        await g.Config.inbounds.Select(inbound => inbound.port ?? 0).Should().BeEquivalentTo([41809], CollectionOrdering.Matching);
        await g.Tags.Contains(Global.AetherExitNodeTag).Should().BeFalse();
        await context.Aether.Core.Should().BeNull();

        // What one core cannot serve is refused all the same.
        var warp = Warp();
        var wg = Wg();
        var group = CoreConfigTestFactory.CreatePolicyGroupNode(ECoreType.Xray, "group", "group", [warp.IndexId, wg.IndexId]);
        var conflicting = new CoreConfigV2rayService(AetherContext(NewConfig(), group, warp, wg)).GenerateClientSpeedtestConfig(41810);
        await conflicting.Success.Should().BeFalse();
        await conflicting.Msg.Should().BeEqualTo(ResUI.MsgAetherConfigSingleProfile);
    }

    [Test]
    public async Task ABatchLatencyTestLeavesAnAetherProfileToItsOwnTest()
    {
        var warp = Warp(id: "warp-batch");
        warp.Address = "162.159.198.1";
        warp.Port = 443;
        var vless = Vless("vless-batch", "vless");
        var context = AetherContext(NewConfig(), new ProfileItem { IndexId = "dummy", CoreType = ECoreType.Xray }, warp, vless);
        var warpItem = new ServerTestItem { IndexId = warp.IndexId, ConfigType = warp.ConfigType, Port = warp.Port, Profile = warp };
        var vlessItem = new ServerTestItem { IndexId = vless.IndexId, ConfigType = vless.ConfigType, Port = vless.Port, Profile = vless };

        var result = new CoreConfigV2rayService(context).GenerateClientSpeedtestConfig([warpItem, vlessItem]);

        await result.Success.Should().BeTrue().Because(result.Msg ?? string.Empty);
        await warpItem.AllowTest.Should().BeFalse();
        await vlessItem.AllowTest.Should().BeTrue();
        await JsonUtils.Deserialize<V2rayConfig>(result.Data!.ToString())!.outbounds.Any(outbound => outbound.protocol == "socks").Should().BeFalse();
    }

    // ---- the exit of a core of its own

    // PattNG AetherCoreManagerTest.theExitOfACoreOfItsOwnOpensWithItsExitNode
    [Test]
    public async Task theExitOfACoreOfItsOwnOpensWithItsExitNode()
    {
        var config = NewConfig(config => config.CoreBasicItem.Loglevel = "none");
        var context = CoreConfigTestFactory.CreateContext(config, new ProfileItem { IndexId = "dummy", CoreType = ECoreType.Xray }, ECoreType.Xray);

        var result = new CoreConfigV2rayService(context).GenerateAetherExitConfig(new AetherExit(DialMode: "code-1"), 41950);

        await result.Success.Should().BeTrue().Because(result.Msg ?? string.Empty);
        var node = JsonNode.Parse(result.Data!.ToString()!)!.AsObject();
        var exitNode = node["outbounds"]!.AsArray().Single()!;
        await exitNode["tag"]!.GetValue<string>().Should().BeEqualTo(Global.AetherExitNodeTag);
        await exitNode["protocol"]!.GetValue<string>().Should().BeEqualTo("freedom");
        await exitNode["streamSettings"]!["sockopt"]!["dialMode"]!.GetValue<string>().Should().BeEqualTo("code-1");
        // It logs at the level of the app.
        await node["log"]!["loglevel"]!.GetValue<string>().Should().BeEqualTo("none");
        var inbound = node["inbounds"]!.AsArray().Single()!;
        await inbound["tag"]!.GetValue<string>().Should().BeEqualTo(Global.AetherSecondarySocksTag);
        await inbound["port"]!.GetValue<int>().Should().BeEqualTo(41950);
        var rule = node["routing"]!["rules"]!.AsArray().Single()!;
        await rule["outboundTag"]!.GetValue<string>().Should().BeEqualTo(Global.AetherExitNodeTag);
        await node.ContainsKey("aetherCommand").Should().BeFalse();
    }

    // PattNG AetherCoreManagerTest.theExitOfACoreOfItsOwnOpensWithTheOutboundOfItsNode
    [Test]
    public async Task theExitOfACoreOfItsOwnOpensWithTheOutboundOfItsNode()
    {
        var germany = Vless("germany-ech", "germany");
        germany.StreamSecurity = Global.StreamSecurity;
        germany.EchConfigList = "AEX+DQ";
        germany.EchOutbound = """{"tag": "ech-query", "protocol": "freedom"}""";
        var config = NewConfig();
        var context = CoreConfigTestFactory.CreateContext(config, new ProfileItem { IndexId = "dummy", CoreType = ECoreType.Xray }, ECoreType.Xray);
        context.Aether.ExitNodes["germany"] = ExitNodeLookup.Of(new ByName<ProfileItem>.One(germany));

        var result = new CoreConfigV2rayService(context).GenerateAetherExitConfig(new AetherExit(Node: "germany"), 41951);

        await result.Success.Should().BeTrue().Because(result.Msg ?? string.Empty);
        var outbounds = JsonNode.Parse(result.Data!.ToString()!)!["outbounds"]!.AsArray().OfType<JsonObject>().ToList();
        // The node's outbound under the tag of the exit-node, and its ECH outbound linked to it, as in a session.
        await outbounds.Select(outbound => outbound["tag"]!.GetValue<string>()).Should()
            .BeEquivalentTo([Global.AetherExitNodeTag, "ech-query"], CollectionOrdering.Matching);
        await outbounds[0]["protocol"]!.GetValue<string>().Should().BeEqualTo("vless");
        await outbounds[0]["streamSettings"]!["tlsSettings"]!["echSockopt"]!["dialerProxy"]!.GetValue<string>().Should().BeEqualTo("ech-query");

        // A node that gives no outbound opens no exit: the core would reach the internet without it.
        foreach (var problem in new ExitNodeLookup[] { ExitNodeLookup.NotFound.Instance, ExitNodeLookup.SameName.Instance, ExitNodeLookup.NoOutbound.Instance })
        {
            context.Aether.ExitNodes["gone"] = problem;
            var refused = new CoreConfigV2rayService(context).GenerateAetherExitConfig(new AetherExit(Node: "gone"), 41952);
            await refused.Success.Should().BeFalse();
            await refused.Msg.Should().BeEqualTo(string.Format(ResUI.ResourceManager.GetString(((ExitNodeLookup.Problem)problem).MessageKey, ResUI.Culture)!, "gone"));
        }
    }

    // ---- generations

    // PattN (critique 22): a context generated twice gives the same configuration.
    [Test]
    public async Task AContextGeneratedTwiceGivesTheSameConfiguration()
    {
        var hop = Vless("hop", "hop");
        var chain = CoreConfigTestFactory.CreateProxyChainNode(ECoreType.Xray, "chain", "chain", [hop.IndexId, "warp"]);
        var context = AetherContext(NewConfig(), chain, Warp(), hop);

        var first = await Succeeds(context);
        var second = await Succeeds(context);

        await second.Json.Should().BeEqualTo(first.Json);
        await second.Tags.Count(tag => tag == Global.AetherExitNodeTag).Should().BeEqualTo(1);
        await second.Context.Aether.Uses.Count.Should().BeEqualTo(1);
    }
}
