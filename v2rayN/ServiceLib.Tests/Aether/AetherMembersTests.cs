using TUnit.Assertions.Enums;

namespace ServiceLib.Tests.Aether;

// From PattNG ProxyChainMembersTest, ServerProxyChainViewModelTest.aSecondAetherMemberIsTold,
// SubEditViewModelTest.twoAetherNeighborsAreTold and CoreConfigContextBuilderTest (withOneAetherProfile): the Aether
// profiles a proxy chain or a policy group can hold.
public class AetherMembersTests
{
    private const int ListenPort = AetherPorts.DefaultListenPort;

    private static ProfileItem Aether(string name, AetherProtocol protocol, string? storedJson = null)
    {
        var profile = new ProfileItem { IndexId = $"id-{name}", ConfigType = EConfigType.Aether, Remarks = name };
        if (storedJson != null)
        {
            profile.ProtoExtra = storedJson;
        }
        else
        {
            profile.SetProtocolExtra(new ProtocolExtraItem { AetherProtocol = protocol.Type });
        }
        return profile;
    }

    private static readonly ProfileItem Vless = new()
    {
        IndexId = "id-vless",
        ConfigType = EConfigType.VLESS,
        Remarks = "vless",
        Address = "1.2.3.4",
        Port = 443,
    };

    [Test]
    public async Task anAetherMemberCanStandAnywhereButOnlyOnce()
    {
        await AetherMembers.HasSecondAetherMember([EConfigType.Aether, EConfigType.VLESS]).Should().BeFalse();
        await AetherMembers.HasSecondAetherMember([EConfigType.VLESS, EConfigType.Aether]).Should().BeFalse();
        await AetherMembers.HasSecondAetherMember([EConfigType.VLESS, EConfigType.Aether, EConfigType.Trojan]).Should().BeFalse();
        await AetherMembers.HasSecondAetherMember([EConfigType.VLESS, EConfigType.Trojan]).Should().BeFalse();
        await AetherMembers.HasSecondAetherMember([null, EConfigType.VLESS]).Should().BeFalse();
        await AetherMembers.HasSecondAetherMember([EConfigType.Aether, EConfigType.Aether]).Should().BeTrue();
        await AetherMembers.HasSecondAetherMember([EConfigType.Aether, EConfigType.VLESS, EConfigType.Aether]).Should().BeTrue();
    }

    // PattNG ServerProxyChainViewModelTest.aSecondAetherMemberIsTold: the chain editor refuses to save it.
    [Test]
    public async Task aSecondAetherMemberIsTold()
    {
        var warp = Aether("warp", AetherProtocol.Masque);
        var warp2 = Aether("warp 2", AetherProtocol.WireGuard);

        await AetherMembers.SaveProblem(EConfigType.ProxyChain, ECoreType.Xray, [warp, Vless, warp2])
            .Should().BeEqualTo(ResUI.MsgAetherChainOneProfile);
        await AetherMembers.SaveProblem(EConfigType.ProxyChain, ECoreType.Xray, [warp, Vless]).Should().BeNull();
        // A policy group's members are told apart when the configuration is built, where a filter may prune them.
        await AetherMembers.SaveProblem(EConfigType.PolicyGroup, ECoreType.Xray, [warp, warp2]).Should().BeNull();
    }

    // PattNG SubEditViewModelTest.twoAetherNeighborsAreTold: the previous and the next profile of a subscription chain
    // each of its profiles, so they can hold one Aether profile between them.
    [Test]
    public async Task twoAetherNeighborsAreTold()
    {
        await AetherMembers.HasSecondAetherMember([EConfigType.Aether, EConfigType.Aether]).Should().BeTrue();
        await AetherMembers.HasSecondAetherMember([EConfigType.Aether, null]).Should().BeFalse();
        await AetherMembers.HasSecondAetherMember([null, null]).Should().BeFalse();
    }

    // PattN (critique 4): sing-box runs no Aether profile, and its group or chain would drop one without a word.
    [Test]
    public async Task ASingBoxGroupOrChainWithAnAetherMemberIsNotSaved()
    {
        var refusal = string.Format(ResUI.MsgCoreNotSupportProtocol, nameof(ECoreType.sing_box), EConfigType.Aether);
        var warp = Aether("warp", AetherProtocol.Masque);

        await AetherMembers.SaveProblem(EConfigType.ProxyChain, ECoreType.sing_box, [Vless, warp]).Should().BeEqualTo(refusal);
        await AetherMembers.SaveProblem(EConfigType.PolicyGroup, ECoreType.sing_box, [warp]).Should().BeEqualTo(refusal);
        await AetherMembers.SaveProblem(EConfigType.ProxyChain, ECoreType.sing_box, [Vless]).Should().BeNull();
    }

    [Test]
    public async Task aGroupKeepsItsFirstAetherProfileAndAnyWithTheSameSettings()
    {
        var first = Aether("warp", AetherProtocol.Masque);
        var same = Aether("warp again", AetherProtocol.Masque);
        var other = Aether("wg", AetherProtocol.WireGuard);

        var (kept, leftOut) = AetherMembers.WithOneAetherProfile([Vless, first, other, same], _ => true, ListenPort);

        await kept.Should().BeEquivalentTo([Vless, first, same], CollectionOrdering.Matching);
        await leftOut.Should().BeEquivalentTo([other], CollectionOrdering.Matching);
    }

    [Test]
    public async Task aGroupKeepsTheSameTunnelStoredWithAListenPortOfItsOwn()
    {
        // Profiles stored while each profile had a listen port of its own may carry one still; every core listens on
        // the one port now. PattN has no such field: the stored JSON keeps the key, which nothing reads.
        var first = Aether("warp", AetherProtocol.Masque);
        var elsewhere = Aether("warp on 20808", AetherProtocol.Masque, """{"AetherProtocol":"masque","AetherListenPort":"20808"}""");
        await elsewhere.GetProtocolExtra().AetherProtocol.Should().BeEqualTo("masque");

        var (kept, leftOut) = AetherMembers.WithOneAetherProfile([first, elsewhere], _ => true, ListenPort);

        await kept.Should().BeEquivalentTo([first, elsewhere], CollectionOrdering.Matching);
        await leftOut.Should().BeEmpty();
    }

    [Test]
    public async Task aGroupWithoutAetherIsUnchanged()
    {
        var (kept, leftOut) = AetherMembers.WithOneAetherProfile([Vless], _ => true, ListenPort);
        await kept.Should().BeEquivalentTo([Vless], CollectionOrdering.Matching);
        await leftOut.Should().BeEmpty();

        var (none, noneLeftOut) = AetherMembers.WithOneAetherProfile([], _ => true, ListenPort);
        await none.Should().BeEmpty();
        await noneLeftOut.Should().BeEmpty();
    }

    // PattN: a member picked by hand is never left out, and its core is the one the members a filter found keep to; a
    // conflict among members picked by hand is refused when the configuration is generated.
    [Test]
    public async Task AMemberPickedByHandStaysAndSetsTheCoreTheFilteredOnesKeepTo()
    {
        var picked = Aether("wg picked", AetherProtocol.WireGuard);
        var filtered = Aether("warp", AetherProtocol.Masque);
        var filteredSame = Aether("wg found", AetherProtocol.WireGuard);
        var pickedOther = Aether("masque picked", AetherProtocol.Masque);
        bool FilterBuilt(ProfileItem member) => member != picked && member != pickedOther;

        var (kept, leftOut) = AetherMembers.WithOneAetherProfile([filtered, Vless, filteredSame, picked, pickedOther], FilterBuilt, ListenPort);

        await kept.Should().BeEquivalentTo([Vless, filteredSame, picked, pickedOther], CollectionOrdering.Matching);
        await leftOut.Should().BeEquivalentTo([filtered], CollectionOrdering.Matching);
    }

    // PattNG proxyChainProblem counts the hops of every name listed: a profile picked twice in the chain editor is two
    // Aether members, where the members stored and read back would count it once (and the chain would run shorter).
    [Test]
    public async Task AProfilePickedTwiceInTheEditorCountsTwice()
    {
        var warp = Aether("warp", AetherProtocol.Masque);
        var extra = new ProtocolExtraItem { ChildItems = Utils.List2String([warp.IndexId, Vless.IndexId, warp.IndexId]) };

        var members = await AetherMembers.EditorMembersAsync([warp, Vless, warp], extra);

        await members.Count.Should().BeEqualTo(3);
        await AetherMembers.SaveProblem(EConfigType.ProxyChain, ECoreType.Xray, members).Should().BeEqualTo(ResUI.MsgAetherChainOneProfile);
    }

    // The WebAPI saves groups, chains and subscriptions as well; it refuses what the desktop's editors refuse.
    [Test]
    [NotInParallel]
    public async Task WhatTheEditorsRefuseIsRefusedWhereverItIsSaved()
    {
        SQLiteHelper.Instance.CreateTable<ProfileItem>();
        var suffix = Guid.NewGuid().ToString("N");
        var warp = Aether($"warp-{suffix}", AetherProtocol.Masque);
        var wg = Aether($"wg-{suffix}", AetherProtocol.WireGuard);
        var vless = new ProfileItem { IndexId = $"id-vless-{suffix}", ConfigType = EConfigType.VLESS, Remarks = $"vless-{suffix}", Address = "1.2.3.4", Port = 443 };
        foreach (var profile in new[] { warp, wg, vless })
        {
            await SQLiteHelper.Instance.ReplaceAsync(profile);
        }

        async Task<string?> ChainProblem(ECoreType coreType, params ProfileItem[] hops)
        {
            var chain = new ProfileItem { ConfigType = EConfigType.ProxyChain, CoreType = coreType, Remarks = "chain" };
            return await AetherMembers.SaveProblemAsync(chain, new ProtocolExtraItem { ChildItems = Utils.List2String(hops.Select(hop => hop.IndexId).ToList()) });
        }

        await (await ChainProblem(ECoreType.Xray, warp, vless, wg)).Should().BeEqualTo(ResUI.MsgAetherChainOneProfile);
        await (await ChainProblem(ECoreType.Xray, warp, vless, warp)).Should().BeEqualTo(ResUI.MsgAetherChainOneProfile);
        await (await ChainProblem(ECoreType.sing_box, vless, warp)).Should()
            .BeEqualTo(string.Format(ResUI.MsgCoreNotSupportProtocol, nameof(ECoreType.sing_box), EConfigType.Aether));
        await (await ChainProblem(ECoreType.Xray, vless, warp)).Should().BeNull();

        await (await AetherMembers.SubscriptionProblemAsync(warp.Remarks, wg.Remarks)).Should().BeEqualTo(ResUI.MsgAetherChainOneProfile);
        await (await AetherMembers.SubscriptionProblemAsync(warp.Remarks, vless.Remarks)).Should().BeNull();
        await (await AetherMembers.SubscriptionProblemAsync(null, warp.Remarks)).Should().BeNull();
    }

    // N1: the previous and the next profile are found by their names as the chain finds them, the spaces around them
    // aside; a name several profiles have names none of them, never the first, and is refused for itself.
    [Test]
    [NotInParallel]
    public async Task ASubscriptionNameIsFoundAsTheChainFindsIt()
    {
        SQLiteHelper.Instance.CreateTable<ProfileItem>();
        var suffix = Guid.NewGuid().ToString("N");
        var shared = $"shared-{suffix}";
        var first = Aether($"first-{suffix}", AetherProtocol.Masque);
        first.Remarks = shared;
        var second = Aether($"second-{suffix}", AetherProtocol.WireGuard);
        second.Remarks = $" {shared}";
        var wg = Aether($"wg-{suffix}", AetherProtocol.WireGuard);
        foreach (var profile in new[] { first, second, wg })
        {
            await SQLiteHelper.Instance.ReplaceAsync(profile);
        }

        // Two Aether profiles, when the shared name is taken for neither.
        await (await AetherMembers.SubscriptionProblemAsync(shared, wg.Remarks)).Should().BeNull();
        await (await CoreConfigContextBuilder.SubscriptionNamesProblemAsync(shared, wg.Remarks)).Should()
            .BeEqualTo(string.Format(ResUI.MsgRoutingRuleTargetDuplicate, shared));
        await (await AetherMembers.SubscriptionProblemAsync($"  {wg.Remarks} ", wg.Remarks)).Should().BeEqualTo(ResUI.MsgAetherChainOneProfile);
        await (await CoreConfigContextBuilder.SubscriptionNamesProblemAsync($"  {wg.Remarks} ", "  ")).Should().BeNull();
    }

    // N1: a custom configuration is no hop (PattNG takesAsHop), so beside an Aether profile of its name it makes no second
    // profile of the name: the Aether profile is the one the name finds, as the chain finds it.
    [Test]
    [NotInParallel]
    public async Task ACustomConfigurationOfTheNameLeavesTheAetherProfileTheOneItFinds()
    {
        SQLiteHelper.Instance.CreateTable<ProfileItem>();
        var suffix = Guid.NewGuid().ToString("N");
        var warp = Aether($"warp-{suffix}", AetherProtocol.Masque);
        var custom = new ProfileItem { IndexId = $"id-custom-{suffix}", ConfigType = EConfigType.Custom, Remarks = warp.Remarks, Address = "custom.json" };
        var wg = Aether($"wg-{suffix}", AetherProtocol.WireGuard);
        foreach (var profile in new[] { warp, custom, wg })
        {
            await SQLiteHelper.Instance.ReplaceAsync(profile);
        }

        await (await CoreConfigContextBuilder.SubscriptionNamesProblemAsync(warp.Remarks, wg.Remarks)).Should().BeNull();
        await (await AetherMembers.SubscriptionProblemAsync(warp.Remarks, wg.Remarks)).Should().BeEqualTo(ResUI.MsgAetherChainOneProfile);
    }
}
