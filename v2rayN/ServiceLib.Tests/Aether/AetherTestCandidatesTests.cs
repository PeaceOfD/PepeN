using ServiceLib.Tests.CoreConfig;
using TUnit.Assertions.Enums;

namespace ServiceLib.Tests.Aether;

/// <summary>
/// PattN (design 2.12.1, critique 25): which profiles of a test run need an Aether core and leave the batch, told from the
/// stored profiles, the subscriptions resolved once each.
/// </summary>
public class AetherTestCandidatesTests
{
    private static ProfileItem Warp(string id = "warp", string? subId = null)
    {
        var profile = CoreConfigTestFactory.CreateAetherNode(id, $"WARP {id}");
        profile.Subid = subId ?? string.Empty;
        return profile;
    }

    private static ProfileItem Vless(string id, string? subId = null)
    {
        return new ProfileItem
        {
            IndexId = id,
            ConfigType = EConfigType.VLESS,
            CoreType = ECoreType.Xray,
            Remarks = id,
            Address = "203.0.113.7",
            Port = 443,
            Subid = subId ?? string.Empty,
        };
    }

    /// <summary>The candidates of a store of <paramref name="subscriptions" />, <paramref name="profiles" /> by name and group members, counting the lookups.</summary>
    private sealed class Store
    {
        public Dictionary<string, SubItem> Subscriptions { get; } = [];
        public Dictionary<string, ProfileItem> Named { get; } = [];

        /// <summary>The names several profiles have (N1).</summary>
        public HashSet<string> Shared { get; } = [];

        public Dictionary<string, List<ProfileItem>> Members { get; } = [];
        public List<string> Lookups { get; } = [];

        /// <summary>The profiles the checks of a session start refuse, by id; null to leave out the checks.</summary>
        public HashSet<string>? Refused { get; set; }

        public AetherTestCandidates Candidates()
        {
            return new AetherTestCandidates(
                subId =>
                {
                    Lookups.Add($"sub {subId}");
                    return Task.FromResult(Subscriptions.GetValueOrDefault(subId));
                },
                name =>
                {
                    Lookups.Add($"name {name}");
                    if (Shared.Contains(name))
                    {
                        return Task.FromResult<ByName<ProfileItem>>(ByName<ProfileItem>.Several.Instance);
                    }
                    return Task.FromResult<ByName<ProfileItem>>(Named.TryGetValue(name, out var profile)
                        ? new ByName<ProfileItem>.One(profile)
                        : ByName<ProfileItem>.None.Instance);
                },
                group =>
                {
                    Lookups.Add($"members {group.IndexId}");
                    return Task.FromResult<IEnumerable<ProfileItem>>(Members.GetValueOrDefault(group.IndexId) ?? []);
                },
                Refused is { } refused
                    ? profile =>
                    {
                        Lookups.Add($"checks {profile.IndexId}");
                        return Task.FromResult(refused.Contains(profile.IndexId));
                    }
                    : null);
        }
    }

    [Test]
    public async Task AnAetherProfileNeedsItsCore()
    {
        var store = new Store();
        await (await store.Candidates().NeedsAetherAsync(Warp())).Should().BeTrue();
        await (await store.Candidates().NeedsAetherAsync(Vless("v"))).Should().BeFalse();
        // Neither asks the store: no subscription, no members.
        await store.Lookups.Should().BeEmpty();
    }

    [Test]
    public async Task AGroupOrAChainWithAnAetherMemberNeedsItsCore()
    {
        var store = new Store();
        var group = CoreConfigTestFactory.CreatePolicyGroupNode(ECoreType.Xray, "group", "group", ["v", "warp"]);
        store.Members["group"] = [Vless("v"), Warp()];
        var chain = CoreConfigTestFactory.CreateProxyChainNode(ECoreType.Xray, "chain", "chain", ["v", "w"]);
        store.Members["chain"] = [Vless("v"), Vless("w")];

        await (await store.Candidates().NeedsAetherAsync(group)).Should().BeTrue();
        await (await store.Candidates().NeedsAetherAsync(chain)).Should().BeFalse();
    }

    [Test]
    public async Task AProfileItsSubscriptionChainsWithAnAetherProfileNeedsItsCore()
    {
        var store = new Store();
        store.Subscriptions["sub"] = new SubItem { Id = "sub", Remarks = "sub", PrevProfile = "WARP prev" };
        store.Named["WARP prev"] = Warp("prev");
        store.Subscriptions["plain"] = new SubItem { Id = "plain", Remarks = "plain", NextProfile = "exit" };
        store.Named["exit"] = Vless("exit");
        // The next profile a group holding Aether.
        store.Subscriptions["grouped"] = new SubItem { Id = "grouped", Remarks = "grouped", NextProfile = "pool" };
        store.Named["pool"] = CoreConfigTestFactory.CreatePolicyGroupNode(ECoreType.Xray, "pool", "pool", ["prev"]);
        store.Members["pool"] = [Warp("prev")];
        var candidates = store.Candidates();

        await (await candidates.NeedsAetherAsync(Vless("a", "sub"))).Should().BeTrue();
        await (await candidates.NeedsAetherAsync(Vless("b", "plain"))).Should().BeFalse();
        await (await candidates.NeedsAetherAsync(Vless("c", "grouped"))).Should().BeTrue();
        await (await candidates.NeedsAetherAsync(Vless("d", "missing"))).Should().BeFalse();
    }

    [Test]
    public async Task ASubscriptionIsResolvedOncePerRun()
    {
        var store = new Store();
        store.Subscriptions["sub"] = new SubItem { Id = "sub", Remarks = "sub", PrevProfile = "WARP prev", NextProfile = "exit" };
        store.Named["WARP prev"] = Vless("not aether");
        store.Named["exit"] = Vless("exit");
        var candidates = store.Candidates();

        foreach (var id in new[] { "a", "b", "c", "d" })
        {
            await (await candidates.NeedsAetherAsync(Vless(id, "sub"))).Should().BeFalse();
        }

        // Every profile of the subscription is chained the same way: one lookup of it and of each name, not one per profile.
        await store.Lookups.Should().BeEquivalentTo(["sub sub", "name WARP prev", "name exit"], CollectionOrdering.Matching);
    }

    private static ServerTestItem Item(ProfileItem profile, ECoreType coreType = ECoreType.Xray)
    {
        return new ServerTestItem
        {
            IndexId = profile.IndexId,
            ConfigType = profile.ConfigType,
            Port = profile.Port,
            Profile = profile,
            CoreType = coreType,
        };
    }

    [Test]
    public async Task ThePartitionKeepsTheBatchForTheRest()
    {
        var store = new Store();
        var group = CoreConfigTestFactory.CreatePolicyGroupNode(ECoreType.Xray, "group", "group", ["warp"]);
        store.Members["group"] = [Warp()];
        var singBoxGroup = CoreConfigTestFactory.CreatePolicyGroupNode(ECoreType.sing_box, "sb-group", "sb-group", ["warp"]);
        store.Members["sb-group"] = [Warp()];
        var v1 = Item(Vless("v1"));
        var warp = Item(Warp());
        var v2 = Item(Vless("v2"));
        var grouped = Item(group);
        var singBox = Item(singBoxGroup, ECoreType.sing_box);
        var v3 = Item(Vless("v3"));
        var batch = new List<ServerTestItem> { v1, warp, v2, grouped, singBox, v3 };

        var taken = await store.Candidates().TakeAsync(ESpeedActionType.Realping, batch);

        // The Aether profile and the Xray group holding one leave, in their order; the rest stays for the batch, in its
        // order, sing-box's group with it (sing-box runs no Aether profile: its checks leave it out).
        await taken.Should().BeEquivalentTo([warp, grouped], CollectionOrdering.Matching);
        await batch.Should().BeEquivalentTo([v1, v2, singBox, v3], CollectionOrdering.Matching);
    }

    [Test]
    public async Task AProfileAnotherCoreRunsLeavesTheBatchOnlyWhenItsSessionWouldBeRefused()
    {
        // Critique 4: sing-box runs no Aether profile, so a chain of it with an Aether hop is refused whole; the test of one
        // profile of sing-box skips those checks and would measure the chain the shorter way, without the hop. It goes to
        // the Aether tester, which refuses it (-1) as its session is refused. A group of sing-box with an Aether member
        // runs without it, as its session does, and keeps its batch test.
        var store = new Store { Refused = ["sb-chain", "sb-sub"] };
        var chain = CoreConfigTestFactory.CreateProxyChainNode(ECoreType.sing_box, "sb-chain", "sb-chain", ["v", "warp"]);
        store.Members["sb-chain"] = [Vless("v"), Warp()];
        var group = CoreConfigTestFactory.CreatePolicyGroupNode(ECoreType.sing_box, "sb-group", "sb-group", ["v", "warp"]);
        store.Members["sb-group"] = [Vless("v"), Warp()];
        store.Subscriptions["sub"] = new SubItem { Id = "sub", Remarks = "sub", PrevProfile = "WARP" };
        store.Named["WARP"] = Warp();
        var subscribed = Vless("sb-sub", "sub");
        var plain = Vless("sb-plain");
        var chained = Item(chain, ECoreType.sing_box);
        var grouped = Item(group, ECoreType.sing_box);
        var wrapped = Item(subscribed, ECoreType.sing_box);
        var alone = Item(plain, ECoreType.sing_box);
        var batch = new List<ServerTestItem> { chained, grouped, wrapped, alone };

        foreach (var actionType in new[] { ESpeedActionType.Realping, ESpeedActionType.Speedtest, ESpeedActionType.Mixedtest })
        {
            var items = new List<ServerTestItem>(batch);
            var taken = await store.Candidates().TakeAsync(actionType, items);

            await taken.Should().BeEquivalentTo([chained, wrapped], CollectionOrdering.Matching).Because(actionType.ToString());
            await items.Should().BeEquivalentTo([grouped, alone], CollectionOrdering.Matching).Because(actionType.ToString());
        }
        // Only a profile that needs an Aether core is built for its checks.
        await store.Lookups.Contains("checks sb-plain").Should().BeFalse();
        // Without the checks, a profile of another core stays, and nothing is looked up for it.
        var withoutChecks = new Store();
        withoutChecks.Members["sb-chain"] = [Vless("v"), Warp()];
        var left = new List<ServerTestItem> { chained };
        await (await withoutChecks.Candidates().TakeAsync(ESpeedActionType.Realping, left)).Should().BeEmpty();
        await withoutChecks.Lookups.Should().BeEmpty();
    }

    [Test]
    public async Task ATcpPingTakesOnlyTheAetherProfiles()
    {
        // A TCP ping opens no tunnel: an Aether profile's edge is probed, and a group keeps PattN's TCP ping.
        var store = new Store();
        var group = CoreConfigTestFactory.CreatePolicyGroupNode(ECoreType.Xray, "group", "group", ["warp"]);
        store.Members["group"] = [Warp()];
        var warp = Item(Warp());
        var grouped = Item(group);
        var chained = Item(Vless("v", "sub"));
        store.Subscriptions["sub"] = new SubItem { Id = "sub", Remarks = "sub", PrevProfile = "WARP" };
        store.Named["WARP"] = Warp();
        var batch = new List<ServerTestItem> { grouped, warp, chained };

        var taken = await store.Candidates().TakeAsync(ESpeedActionType.Tcping, batch);

        await taken.Should().BeEquivalentTo([warp], CollectionOrdering.Matching);
        await batch.Should().BeEquivalentTo([grouped, chained], CollectionOrdering.Matching);
        await store.Lookups.Should().BeEmpty();
    }

    // N1: a name of the subscription that no profile has, or several have, refuses the chain whatever it holds; the batch,
    // which skips that check, would measure the profile without that hop. The profile goes to the Aether tester, whose
    // checks refuse it (-1) as its session is refused, as PattNG fails its test; whether the name holds Aether is never
    // guessed from the first profile of it.
    [Test]
    public async Task AProfileWhoseSubscriptionNamesAProfileNoneOrSeveralHaveLeavesTheBatch()
    {
        var store = new Store { Refused = ["sb-gone"] };
        store.Subscriptions["gone"] = new SubItem { Id = "gone", Remarks = "gone", PrevProfile = "nobody" };
        store.Subscriptions["twice"] = new SubItem { Id = "twice", Remarks = "twice", NextProfile = "twins" };
        store.Shared.Add("twins");
        store.Subscriptions["fine"] = new SubItem { Id = "fine", Remarks = "fine", NextProfile = "exit" };
        store.Named["exit"] = Vless("exit");
        // A next profile that is found, behind a previous one that is not.
        store.Subscriptions["half"] = new SubItem { Id = "half", Remarks = "half", PrevProfile = "nobody", NextProfile = "exit" };
        var gone = Item(Vless("v-gone", "gone"));
        var twice = Item(Vless("v-twice", "twice"));
        var fine = Item(Vless("v-fine", "fine"));
        var half = Item(Vless("v-half", "half"));
        var singBoxGone = Item(Vless("sb-gone", "gone"), ECoreType.sing_box);
        var batch = new List<ServerTestItem> { gone, twice, fine, half, singBoxGone };

        var candidates = store.Candidates();
        var taken = await candidates.TakeAsync(ESpeedActionType.Realping, batch);

        await taken.Should().BeEquivalentTo([gone, twice, half, singBoxGone], CollectionOrdering.Matching);
        await batch.Should().BeEquivalentTo([fine], CollectionOrdering.Matching);
        // None of them needs an Aether core: a name none or several have holds none.
        foreach (var item in new[] { gone, twice, half })
        {
            await (await candidates.NeedsAetherAsync(item.Profile)).Should().BeFalse();
        }
        // The profile of another core is taken as its checks refuse it.
        await store.Lookups.Contains("checks sb-gone").Should().BeTrue();

        // Without the checks, a profile of another core stays, as the batch leaves it untested (CoreConfigHandler).
        var withoutChecks = new Store();
        withoutChecks.Subscriptions["gone"] = new SubItem { Id = "gone", Remarks = "gone", PrevProfile = "nobody" };
        var left = new List<ServerTestItem> { singBoxGone };
        await (await withoutChecks.Candidates().TakeAsync(ESpeedActionType.Realping, left)).Should().BeEmpty();
        // A TCP ping, which reaches the server alone, keeps PattN's way.
        var pinged = new List<ServerTestItem> { gone, twice };
        await (await store.Candidates().TakeAsync(ESpeedActionType.Tcping, pinged)).Should().BeEmpty();
    }

    [Test]
    public async Task TheNamesOfASubscriptionAreFoundWithoutTheSpacesAroundThemAndABlankOneNamesNone()
    {
        var store = new Store();
        store.Subscriptions["spaced"] = new SubItem { Id = "spaced", Remarks = "spaced", PrevProfile = "  WARP  " };
        store.Named["WARP"] = Warp();
        store.Subscriptions["blank"] = new SubItem { Id = "blank", Remarks = "blank", PrevProfile = "   ", NextProfile = "" };
        var spaced = Item(Vless("v-spaced", "spaced"));
        var blank = Item(Vless("v-blank", "blank"));
        var batch = new List<ServerTestItem> { spaced, blank };

        var candidates = store.Candidates();
        var taken = await candidates.TakeAsync(ESpeedActionType.Realping, batch);

        await taken.Should().BeEquivalentTo([spaced], CollectionOrdering.Matching);
        await batch.Should().BeEquivalentTo([blank], CollectionOrdering.Matching);
        await (await candidates.NeedsAetherAsync(spaced.Profile)).Should().BeTrue();
        await store.Lookups.Should().BeEquivalentTo(["sub spaced", "name WARP", "sub blank"], CollectionOrdering.Matching);
    }
}
