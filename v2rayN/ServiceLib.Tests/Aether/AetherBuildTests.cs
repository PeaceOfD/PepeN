namespace ServiceLib.Tests.Aether;

// PattN (design 2.7.2, critique 8 and 22): what the Aether outbounds of one configuration record while it is built.
public class AetherBuildTests
{
    private static AetherUse Use()
    {
        var profile = new ProfileItem { ConfigType = EConfigType.Aether, Remarks = "warp" };
        profile.SetProtocolExtra(new ProtocolExtraItem { AetherProtocol = AetherProtocol.Masque.Type });
        return new AetherUse(profile, AetherCore.Of(profile, AetherPorts.DefaultListenPort));
    }

    [Test]
    public async Task TheOutboundsAreKnownByThemselvesNotByTheirTags()
    {
        var build = new AetherBuild();
        var outbound = new Outbounds4Ray { tag = Global.ProxyTag, protocol = "socks" };
        var sameTag = new Outbounds4Ray { tag = Global.ProxyTag, protocol = "socks" };
        var use = Use();

        build.Add(outbound, use);
        // Callers tag a group member, a chain hop or a routing target after it is built.
        outbound.tag = "proxy-1-warp";

        await build.IsAetherOutbound(outbound).Should().BeTrue();
        await build.IsAetherOutbound(sameTag).Should().BeFalse();
        await build.IsAetherOutbound(null).Should().BeFalse();
        await build.UseOf(outbound).Should().BeSameReferenceAs(use);
        await build.Uses.Count.Should().BeEqualTo(1);

        // A chain that copies its hops copies an Aether outbound as one.
        var copy = JsonUtils.DeepCopy(outbound)!;
        build.AddCopy(outbound, copy);
        build.AddCopy(sameTag, new Outbounds4Ray());
        await build.IsAetherOutbound(copy).Should().BeTrue();
        await build.Outbounds.Count().Should().BeEqualTo(2);
        await build.Uses.Count.Should().BeEqualTo(1);
    }

    [Test]
    public async Task TheFirstRefusalCounts()
    {
        var build = new AetherBuild();
        build.Refuse("first");
        build.Refuse("second");
        await build.Failure.Should().BeEqualTo("first");
    }

    [Test]
    public async Task AGenerationForgetsWhatTheLastBuiltButNotWhatCameFromOutside()
    {
        var build = new AetherBuild();
        var outbound = new Outbounds4Ray { tag = Global.ProxyTag };
        build.Add(outbound, Use());
        build.Refuse("refused");
        build.ExitNodeTagTaken = true;
        build.Core = Use().Core;
        build.SecondaryInboundPort = 10822;
        build.ExitNodes["germany"] = ExitNodeLookup.NotFound.Instance;
        build.SecondaryPort = 40000;
        var firstChain = build.NewChainTag(Global.ProxyTag);

        build.Reset();

        await build.Uses.Should().BeEmpty();
        await build.IsAetherOutbound(outbound).Should().BeFalse();
        await build.Failure.Should().BeNull();
        await build.ExitNodeTagTaken.Should().BeFalse();
        await build.Core.Should().BeNull();
        await build.SecondaryInboundPort.Should().BeNull();
        await build.NewChainTag(Global.ProxyTag).Should().BeEqualTo(firstChain);
        // The exit-node lookups are the builder's, made once from the database; the test picks its secondary port.
        await build.ExitNodes.ContainsKey("germany").Should().BeTrue();
        await build.SecondaryPort.Should().BeEqualTo(40000);
    }

    [Test]
    public async Task EveryChainBuiltGetsATagOfItsOwn()
    {
        var build = new AetherBuild();
        var first = build.NewChainTag(Global.ProxyTag);
        var second = build.NewChainTag(Global.ProxyTag);
        await first.Should().NotBeEqualTo(second);
    }
}
