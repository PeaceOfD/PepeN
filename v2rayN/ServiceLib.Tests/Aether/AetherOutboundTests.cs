namespace ServiceLib.Tests.Aether;

// From PattNG CoreOutboundBuilderTest (its targetStrategy tests): the targetStrategy an Aether outbound carries.
public class AetherOutboundTests
{
    private static ProfileItem Aether(AetherTor? tor = null, AetherPsiphon? psiphon = null, string? command = null, string? targetStrategy = null)
    {
        var profile = new ProfileItem { ConfigType = EConfigType.Aether, TargetStrategy = targetStrategy! };
        profile.SetProtocolExtra(new ProtocolExtraItem
        {
            AetherProtocol = AetherProtocol.Masque.Type,
            AetherTor = (tor ?? AetherTor.Off).Type,
            AetherPsiphon = (psiphon ?? AetherPsiphon.Off).Type,
            AetherCommand = command,
        });
        return profile;
    }

    [Test]
    public async Task test_applyTargetStrategy_anAetherProfileThroughWarpDefaultsToForceIPv4v6()
    {
        // Its core looks names up inside the tunnel with no cache, so Xray's DNS looks them up for it first.
        await AetherOutbound.TargetStrategyOf(Aether()).Should().BeEqualTo(AetherOutbound.ForceIPv4v6);
        await AetherOutbound.TargetStrategyOf(Aether(targetStrategy: " ")).Should().BeEqualTo(AetherOutbound.ForceIPv4v6);
        // AsIs chosen for one stays AsIs, so its outbound carries none (critique 5: PattN's GetTargetStrategy reads a
        // stored AsIs as none, which would bring the default back); any other choice is kept.
        await AetherOutbound.TargetStrategyOf(Aether(targetStrategy: Global.AsIs)).Should().BeNull();
        await AetherOutbound.TargetStrategyOf(Aether(targetStrategy: "asis")).Should().BeNull();
        await AetherOutbound.TargetStrategyOf(Aether(targetStrategy: "UseIPv6")).Should().BeEqualTo("UseIPv6");
        await AetherOutbound.TargetStrategyOf(Aether(targetStrategy: " UseIPv4 ")).Should().BeEqualTo("UseIPv4");
    }

    [Test]
    public async Task test_defaultTargetStrategy_anAetherProfileWhoseTrafficLeavesThroughTorOrPsiphonPassesNamesOn()
    {
        static string Default(ProfileItem profile) => AetherOutbound.DefaultTargetStrategy(profile);
        const string force = AetherOutbound.ForceIPv4v6;
        const string asIs = Global.AsIs;

        // Inside the tunnel, or alone, Tor and Psiphon carry the traffic last, and look names up at their exit.
        await Default(Aether(tor: AetherTor.Chain)).Should().BeEqualTo(asIs);
        await Default(Aether(tor: AetherTor.Only)).Should().BeEqualTo(asIs);
        await Default(Aether(psiphon: AetherPsiphon.Chain)).Should().BeEqualTo(asIs);
        await Default(Aether(psiphon: AetherPsiphon.Only)).Should().BeEqualTo(asIs);
        await Default(Aether(tor: AetherTor.Reverse, psiphon: AetherPsiphon.Chain)).Should().BeEqualTo(asIs);
        // Around the tunnel, they leave the traffic to WARP.
        await Default(Aether()).Should().BeEqualTo(force);
        await Default(Aether(tor: AetherTor.Reverse)).Should().BeEqualTo(force);
        await Default(Aether(psiphon: AetherPsiphon.Reverse)).Should().BeEqualTo(force);
        await Default(Aether(tor: AetherTor.Reverse, psiphon: AetherPsiphon.Reverse)).Should().BeEqualTo(force);

        // A command of its own runs in place of the settings, so it says where the traffic leaves.
        await Default(Aether(command: "aether --masque --tor")).Should().BeEqualTo(asIs);
        await Default(Aether(command: "aether --psiphon-only --psiphon-bind 127.0.0.1:10819")).Should().BeEqualTo(asIs);
        await Default(Aether(tor: AetherTor.Chain, command: "aether --masque --tor-reverse")).Should().BeEqualTo(force);
        // One the app cannot read does not run, and the settings do.
        await Default(Aether(tor: AetherTor.Chain, command: "aether")).Should().BeEqualTo(asIs);
        await Default(Aether(command: "aether --tor --tor-bind 127.0.0.1:")).Should().BeEqualTo(force);
    }

    [Test]
    public async Task AHopBehindAnotherKeepsATargetStrategyOnlyWhenItsProfileStoresOne()
    {
        await AetherOutbound.KeepsTargetStrategyBehindAHop(Aether()).Should().BeFalse();
        await AetherOutbound.KeepsTargetStrategyBehindAHop(Aether(targetStrategy: "  ")).Should().BeFalse();
        await AetherOutbound.KeepsTargetStrategyBehindAHop(Aether(targetStrategy: "UseIPv4v6")).Should().BeTrue();
        await AetherOutbound.KeepsTargetStrategyBehindAHop(Aether(targetStrategy: Global.AsIs)).Should().BeTrue();
    }

    [Test]
    public async Task UnderTheTunTheFreedomExitNodeLooksNamesUpWithXraysDnsAloneByTheDirectOutboundsFamily()
    {
        // A Use strategy falls back to the OS resolver when Xray's DNS finds nothing; a Force one never does.
        foreach (var (direct, forced) in new[]
                 {
                     ("UseIP", "ForceIP"), ("UseIPv4", "ForceIPv4"), ("UseIPv6", "ForceIPv6"), ("UseIPv4v6", "ForceIPv4v6"),
                     ("UseIPv6v4", "ForceIPv6v4"), ("ForceIPv6", "ForceIPv6"), (" useipv4 ", "ForceIPv4"),
                 })
        {
            await AetherOutbound.ForcedStrategyOf(direct).Should().BeEqualTo(forced).Because(direct);
        }
        foreach (var direct in new[] { null, "", Global.AsIs, "UseSomething" })
        {
            await AetherOutbound.ForcedStrategyOf(direct).Should().BeEqualTo("ForceIP").Because(direct ?? "null");
        }
    }
}
