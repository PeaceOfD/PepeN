using ServiceLib.Tests.CoreConfig;

namespace ServiceLib.Tests.Fmt;

public class InnerFmtTests
{
    [Test]
    public async Task ToUriAndResolve_ShouldRoundTripPolicyGroupReferences()
    {
        var childA = CoreConfigTestFactory.CreateSocksNode(ECoreType.Xray, "child-a", "child-a");
        var childB = CoreConfigTestFactory.CreateVmessNode(ECoreType.Xray, "child-b", "child-b");
        var group = CoreConfigTestFactory.CreatePolicyGroupNode(ECoreType.Xray, "group-1", "group-1",
            [childA.IndexId, childB.IndexId]);
        group.SetProtocolExtra(group.GetProtocolExtra() with { SubChildItems = "original-sub" });

        var uri = InnerFmt.ToUri([group, childA, childB]);

        await uri.Should().NotBeNull();
        await uri.Should().NotBeEmpty();

        var resolved = InnerFmt.Resolve(uri!, "sub-123");

        await resolved.Should().NotBeNull();
        await resolved.Should().HaveCount(3);

        var resolvedGroup = resolved!.Single(x => x.Remarks == group.Remarks);
        var resolvedChildA = resolved.Single(x => x.Remarks == childA.Remarks);
        var resolvedChildB = resolved.Single(x => x.Remarks == childB.Remarks);

        await resolvedGroup.ConfigType.Should().BeEqualTo(EConfigType.PolicyGroup);
        await resolvedGroup.GetProtocolExtra().SubChildItems.Should().BeEqualTo("sub-123");
        await resolvedGroup.GetProtocolExtra().ChildItems.Should().BeEqualTo($"{resolvedChildA.IndexId},{resolvedChildB.IndexId}");
    }

    [Test]
    public async Task ToUriAndResolve_ShouldLeaveAetherProfilesToTheirOwnLinks()
    {
        // PattN: an Aether profile travels as an aether:// link only, which carries neither its exit-node, a name of a
        // profile of this device, nor a command of its own, which would run as written.
        var socks = CoreConfigTestFactory.CreateSocksNode(ECoreType.Xray, "socks-1", "socks-1");
        var aether = new ProfileItem
        {
            IndexId = "aether-1",
            ConfigType = EConfigType.Aether,
            CoreType = ECoreType.Xray,
            Remarks = "aether-1",
        };
        aether.SetProtocolExtra(new ProtocolExtraItem
        {
            AetherProtocol = "masque",
            AetherExitNode = "socks-1",
            AetherCommand = "aether --masque --bind 127.0.0.1:10819",
        });

        var uri = InnerFmt.ToUri([aether, socks]);

        await uri.Should().NotBeNull();
        await uri!.Trim().Split('\n').Length.Should().BeEqualTo(1);
        await uri.StartsWith($"{Global.InnerUriProtocol}socks/", StringComparison.Ordinal).Should().BeTrue();

        // Nor is one read from an inner link written elsewhere.
        var crafted = Utils.Base64Encode(JsonUtils.Serialize(aether, false)).Replace('+', '-').Replace('/', '_').Replace("=", "");
        var resolved = InnerFmt.Resolve($"{Global.InnerUriProtocol}aether/{crafted}\n{uri}", "sub-123");

        await resolved.Should().NotBeNull();
        await string.Join(",", resolved!.Select(item => item.ConfigType)).Should().BeEqualTo(nameof(EConfigType.SOCKS));
    }
}
