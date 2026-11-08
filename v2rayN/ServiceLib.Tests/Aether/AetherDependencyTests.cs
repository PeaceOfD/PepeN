namespace ServiceLib.Tests.Aether;

// From PattNG AetherDependencyTest: the core a configuration needs, from the Aether outbounds it builds, and the inbound
// ports of the configuration Xray is handed. PattNG reads the outbounds from its resolved outbounds; here they are the
// AetherUse records the Xray configuration records, made below as PattNG's ResolvedOutbound gives them. The tests of
// custom configurations (aetherCommand) go with later stages.
public class AetherDependencyTests
{
    private const int ListenPort = AetherPorts.DefaultListenPort;

    // PattNG CoreResolvedType: how an outbound resolved.
    private enum Resolved
    {
        Normal,
        PolicyGroup,
        ProxyChain,
    }

    private static readonly ProfileItem Masque = AetherProfile("warp", AetherProtocol.Masque);
    private static readonly ProfileItem MasqueCopy = AetherProfile("warp again", AetherProtocol.Masque);
    private static readonly ProfileItem Wireguard = AetherProfile("wg", AetherProtocol.WireGuard);
    private static readonly ProfileItem Vless = Server(EConfigType.VLESS, "vless", "1.2.3.4");
    private static readonly ProfileItem Trojan = Server(EConfigType.Trojan, "trojan", "5.6.7.8");

    private static ProfileItem AetherProfile(string remarks, AetherProtocol protocol, Func<ProtocolExtraItem, ProtocolExtraItem>? extra = null, Action<ProfileItem>? set = null)
    {
        var profile = new ProfileItem { ConfigType = EConfigType.Aether, Remarks = remarks };
        var protocolExtra = new ProtocolExtraItem { AetherProtocol = protocol.Type };
        profile.SetProtocolExtra(extra == null ? protocolExtra : extra(protocolExtra));
        set?.Invoke(profile);
        return profile;
    }

    private static ProfileItem Server(EConfigType type, string remarks, string address)
    {
        return new ProfileItem { ConfigType = type, Remarks = remarks, Address = address, Port = 443 };
    }

    // PattNG's profile.copy(...): a new profile with the changes, the one copied left as it was.
    private static ProfileItem Copy(ProfileItem profile, Func<ProtocolExtraItem, ProtocolExtraItem>? extra = null, Action<ProfileItem>? set = null)
    {
        var copy = JsonUtils.DeepCopy(profile)!;
        if (extra != null)
        {
            copy.SetProtocolExtra(extra(copy.GetProtocolExtra()));
        }
        set?.Invoke(copy);
        return copy;
    }

    /// <summary>
    /// The uses an outbound tagged <paramref name="tag" /> records, as the configuration builds them from PattNG's
    /// ResolvedOutbound: a chain's <paramref name="profiles" /> are in reverse dial order, the exit first, the entry hop
    /// last, and an Aether hop dials out through the hops after it.
    /// <para>
    /// This helper stands in for the chain builder, which sets <see cref="AetherUse.Exit" /> in PattN, where PattNG's
    /// AetherDependency.of works it out itself. So the expected exits below come from the same rule as the actual ones,
    /// and these tests pin Of's comparisons alone. The rule itself (a non-entry Aether hop dials out through the hops on
    /// its entry side, whatever exit-node, finalMask or dialMode its profile names) is tested against the real chain
    /// builder in the Xray configuration stage (AetherConfigTests).
    /// </para>
    /// </summary>
    private static List<AetherUse> Outbound(string tag, Resolved type, params ProfileItem[] profiles)
    {
        var chained = type == Resolved.ProxyChain;
        var uses = new List<AetherUse>();
        for (var index = 0; index < profiles.Length; index++)
        {
            var profile = profiles[index];
            if (profile.ConfigType != EConfigType.Aether)
            {
                continue;
            }
            var exit = chained && index < profiles.Length - 1 ? AetherExit.Through(profiles.Skip(index + 1)) : null;
            uses.Add(new AetherUse(profile, AetherCore.Of(profile, ListenPort), exit, tag, chained ? tag : null));
        }
        return uses;
    }

    private static AetherDependency Of(params List<AetherUse>[] outbounds)
    {
        return AetherDependency.Of(outbounds.SelectMany(uses => uses));
    }

    /// <summary>The dependency on the core of <paramref name="profile" />.</summary>
    private static AetherDependency Single(ProfileItem profile)
    {
        return new AetherDependency.Single(AetherCore.Of(profile, ListenPort));
    }

    /// <summary>The dependency on the core of <paramref name="profile" /> dialling out through <paramref name="hops" />, the hops on its entry side in a chain.</summary>
    private static AetherDependency Through(ProfileItem profile, params ProfileItem[] hops)
    {
        return new AetherDependency.Single(AetherCore.Of(profile, ListenPort) with { Exit = AetherExit.Through(hops) });
    }

    private static AetherCore CoreOf(AetherDependency dependency)
    {
        return ((AetherDependency.Single)dependency).Core;
    }

    [Test]
    public async Task AConfigurationWithoutAetherNeedsNoCore()
    {
        await Of(Outbound("proxy", Resolved.Normal, Vless)).Should().BeEqualTo(AetherDependency.None.Instance);
        await AetherDependency.Of([]).Should().BeEqualTo(AetherDependency.None.Instance);
    }

    [Test]
    public async Task TheSelectedAetherProfileIsTheDependency()
    {
        await Of(Outbound("proxy", Resolved.Normal, Masque)).Should().BeEqualTo(Single(Masque));
    }

    [Test]
    public async Task AnAetherEntryHopDialsOutThroughItsOwnExitNode()
    {
        // Chain profiles are listed exit first, entry last: this chain dials the Aether core first.
        await Of(Outbound("proxy", Resolved.ProxyChain, Vless, Masque)).Should().BeEqualTo(Single(Masque));
    }

    [Test]
    public async Task AnyOtherAetherHopDialsOutThroughTheHopsOnItsEntrySide()
    {
        var exitHop = Of(Outbound("proxy", Resolved.ProxyChain, Masque, Vless));
        await exitHop.Should().BeEqualTo(Through(Masque, Vless));
        await Of(Outbound("proxy", Resolved.ProxyChain, Vless, Masque, Trojan)).Should().BeEqualTo(Through(Masque, Trojan));
        await Of(Outbound("proxy", Resolved.ProxyChain, Masque, Vless, Trojan)).Should().BeEqualTo(Through(Masque, Vless, Trojan));
        // The exit-node settings of the profile do not count there: the hop is the exit-node.
        var masked = AetherProfile("warp masked", AetherProtocol.Masque, set: profile =>
        {
            profile.Finalmask = """{"tcp": []}""";
            profile.DialMode = "code-1";
        });
        await Of(Outbound("proxy", Resolved.ProxyChain, masked, Vless)).Should().BeEqualTo(exitHop);
    }

    [Test]
    public async Task AChainsHopTakesThePlaceOfTheExitNodeAProfileNames()
    {
        var noded = Copy(Masque, extra => extra with { AetherExitNode = "germany" }, profile => profile.Remarks = "warp through");
        // On its own, or as a chain's entry hop, the core dials out through the profile it names.
        await CoreOf(Of(Outbound("proxy", Resolved.Normal, noded))).Exit.Should().BeEqualTo(new AetherExit(Node: "germany"));
        await CoreOf(Of(Outbound("proxy", Resolved.ProxyChain, Vless, noded))).Exit.Should().BeEqualTo(new AetherExit(Node: "germany"));
        // Anywhere else in a chain, through the hops on its entry side, as for the finalMask and the dialMode.
        await Of(Outbound("proxy", Resolved.ProxyChain, noded, Vless)).Should().BeEqualTo(Through(Masque, Vless));
        // One tunnel cannot dial out through freedom and a profile at once.
        await Of(Outbound("proxy", Resolved.Normal, Masque), Outbound("warp", Resolved.Normal, noded))
            .Should().BeEqualTo(AetherDependency.TwoExits.Instance);
    }

    [Test]
    public async Task TheHopsAnAetherHopDialsOutThroughAreTheCoresOwn()
    {
        // Two chains through the same hops share the core; one core cannot dial out two ways.
        await Of(Outbound("proxy", Resolved.ProxyChain, Masque, Vless), Outbound("warp", Resolved.ProxyChain, MasqueCopy, Vless))
            .Should().BeEqualTo(Through(Masque, Vless));
        await Of(Outbound("proxy", Resolved.ProxyChain, Masque, Vless), Outbound("warp", Resolved.ProxyChain, Masque, Trojan))
            .Should().BeEqualTo(AetherDependency.TwoExits.Instance);
        await Of(Outbound("proxy", Resolved.ProxyChain, Masque, Vless), Outbound("warp", Resolved.Normal, Masque))
            .Should().BeEqualTo(AetherDependency.TwoExits.Instance);
        await Of(Outbound("proxy", Resolved.ProxyChain, Masque, Vless), Outbound("warp", Resolved.ProxyChain, Trojan, Masque))
            .Should().BeEqualTo(AetherDependency.TwoExits.Instance);
        // Another tunnel is another core, wherever it dials out.
        await Of(Outbound("proxy", Resolved.ProxyChain, Masque, Vless), Outbound("warp", Resolved.Normal, Wireguard))
            .Should().BeEqualTo(AetherDependency.Conflicting.Instance);
    }

    [Test]
    public async Task AChainCanHaveOneAetherHop()
    {
        await Of(Outbound("proxy", Resolved.ProxyChain, Masque, Vless, Wireguard))
            .Should().BeEqualTo(new AetherDependency.TwoAetherHops("proxy"));
        await Of(Outbound("proxy", Resolved.Normal, Vless), Outbound("warp", Resolved.ProxyChain, Masque, MasqueCopy))
            .Should().BeEqualTo(new AetherDependency.TwoAetherHops("warp"));
    }

    [Test]
    public async Task ARoutingTargetOrAGroupMemberMayBeAetherAnywhere()
    {
        await Of(Outbound("proxy", Resolved.Normal, Vless), Outbound("warp", Resolved.Normal, Wireguard)).Should().BeEqualTo(Single(Wireguard));
        await Of(Outbound("proxy", Resolved.PolicyGroup, Vless, Masque, Trojan)).Should().BeEqualTo(Single(Masque));
    }

    [Test]
    public async Task TheSameSettingsUnderTwoNamesAreOneDependency()
    {
        await Of(Outbound("proxy", Resolved.ProxyChain, Vless, Masque), Outbound("warp", Resolved.Normal, MasqueCopy))
            .Should().BeEqualTo(Single(Masque));
    }

    [Test]
    public async Task AListenPortAProfileStoredOfItsOwnSplitsNoCore()
    {
        // Profiles stored while each profile had a listen port of its own may carry one still; every core listens on
        // the one port now. PattN has no such field: the stored JSON of such a profile carries the key, which nothing reads.
        var elsewhere = new ProfileItem
        {
            ConfigType = EConfigType.Aether,
            Remarks = "warp on 20808",
            ProtoExtra = """{"AetherProtocol":"masque","AetherListenPort":"20808"}""",
        };
        await elsewhere.GetProtocolExtra().AetherProtocol.Should().BeEqualTo("masque");
        await Of(Outbound("proxy", Resolved.Normal, Masque), Outbound("warp", Resolved.Normal, elsewhere)).Should().BeEqualTo(Single(Masque));
    }

    [Test]
    public async Task TwoDifferentAetherProfilesCannotShareOneCore()
    {
        await Of(Outbound("proxy", Resolved.Normal, Masque), Outbound("warp", Resolved.Normal, Wireguard))
            .Should().BeEqualTo(AetherDependency.Conflicting.Instance);
        await Of(Outbound("proxy", Resolved.PolicyGroup, Masque, Wireguard)).Should().BeEqualTo(AetherDependency.Conflicting.Instance);
    }

    [Test]
    public async Task TwoProfilesOnOneTunnelWithDifferentExitNodesCannotShareOneCore()
    {
        // A process dials out through one exit-node.
        var masked = AetherProfile("warp masked", AetherProtocol.Masque, set: profile => profile.Finalmask = """{"tcp": [{"type": "fragment"}]}""");
        await Of(Outbound("proxy", Resolved.Normal, Masque), Outbound("warp", Resolved.Normal, masked))
            .Should().BeEqualTo(AetherDependency.TwoExits.Instance);
        var alsoMasked = Copy(masked, set: profile => profile.Remarks = "warp masked again");
        var dependency = Of(Outbound("proxy", Resolved.PolicyGroup, masked, alsoMasked));
        await dependency.Should().BeEqualTo(Single(masked));
        await CoreOf(dependency).Exit.Should().BeEqualTo(new AetherExit("""{"tcp": [{"type": "fragment"}]}"""));
    }

    private static string WithInbounds(params string[] inbounds)
    {
        return $$"""{"inbounds": [{{string.Join(",", inbounds)}}], "outbounds": []}""";
    }

    [Test]
    public async Task AnInboundOnTheAetherPortIsFoundBeforeTheCoreIsStarted()
    {
        // What the app builds: the local proxy with a port, and a tun inbound without one.
        var built = WithInbounds(
            """{"tag": "socks", "protocol": "socks", "listen": "127.0.0.1", "port": 10808}""",
            """{"tag": "tun", "protocol": "tun", "settings": {"mtu": 1500}}""");
        await AetherDependency.InboundListensOn(built, 10808).Should().BeTrue();
        await AetherDependency.InboundListensOn(built, 10819).Should().BeFalse();

        // The local proxy moved onto the default Aether port, or picked there at random.
        await AetherDependency.InboundListensOn(WithInbounds("""{"protocol": "socks", "port": 10819}"""), 10819).Should().BeTrue();
    }

    [Test]
    public async Task TheInboundPortsOfACustomConfigurationAreReadInEveryFormXrayReads()
    {
        await AetherDependency.InboundListensOn(WithInbounds("""{"protocol": "socks", "port": "20808"}"""), 20808).Should().BeTrue();
        await AetherDependency.InboundListensOn(WithInbounds("""{"protocol": "dokodemo-door", "port": "20000-21000"}"""), 20808).Should().BeTrue();
        await AetherDependency.InboundListensOn(WithInbounds("""{"protocol": "http", "port": "53, 443 ,20800-20810"}"""), 20808).Should().BeTrue();
        await AetherDependency.InboundListensOn(WithInbounds("""{"protocol": "http", "port": "53,443,20800-20807"}"""), 20808).Should().BeFalse();
        await AetherDependency.InboundListensOn(WithInbounds("""{"protocol": "socks", "port": "20000-21000"}"""), 21001).Should().BeFalse();
        // A port taken from the environment is not known here, and it is not a reason to refuse the start.
        await AetherDependency.InboundListensOn(WithInbounds("""{"protocol": "socks", "port": "env:PORT"}"""), 20808).Should().BeFalse();
    }

    [Test]
    public async Task AConfigurationWithoutReadableInboundsCollidesWithNothing()
    {
        await AetherDependency.InboundListensOn("""{"outbounds": []}""", 10819).Should().BeFalse();
        await AetherDependency.InboundListensOn("""{"inbounds": {"port": 10819}}""", 10819).Should().BeFalse();
        await AetherDependency.InboundListensOn(WithInbounds("10819", """{"port": null}""", """{"port": [10819]}""", """{"port": true}"""), 10819)
            .Should().BeFalse();
        await AetherDependency.InboundListensOn("", 10819).Should().BeFalse();
        await AetherDependency.InboundListensOn("not json {", 10819).Should().BeFalse();
        await AetherDependency.InboundListensOn("[]", 10819).Should().BeFalse();
    }

    // PattN: the inbounds of a full-configuration template are written by hand and are what Xray binds; a range there
    // can take the Aether listen port or the secondary-socks port after it.
    [Test]
    public async Task ARangeOfATemplatesInboundTakesTheAetherPorts()
    {
        var template = WithInbounds("""{"tag": "range", "protocol": "dokodemo-door", "port": "10800-10830"}""");
        await AetherDependency.InboundListensOn(template, AetherPorts.DefaultListenPort).Should().BeTrue();
        await AetherDependency.InboundListensOn(template, AetherPorts.SecondarySocksPortOf(AetherPorts.DefaultListenPort)).Should().BeTrue();
        await AetherDependency.InboundListensOn(template, 10831).Should().BeFalse();
        // Digits as Kotlin reads them, a reversed range included.
        await AetherDependency.InboundListensOn(WithInbounds("""{"port": " 10830 - 10800 "}"""), 10819).Should().BeTrue();
    }
}
