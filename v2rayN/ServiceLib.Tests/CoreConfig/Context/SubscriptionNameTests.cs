using ServiceLib.Tests.Aether;
using TUnit.Assertions.Enums;
using static ServiceLib.Tests.CoreConfig.V2ray.AetherConfigTests;

namespace ServiceLib.Tests.CoreConfig.Context;

/// <summary>
/// PattN (N1; PattNG CoreConfigContextBuilder.kt resolveProxyChainProfilesFromGroup, RealPingWorkerService): the previous
/// and the next profile a subscription names are found by their names, the spaces around them aside. A name that no
/// profile has, or several have, refuses the chain of every profile of the subscription, whatever its type, rather than
/// leave that hop out with a warning or take the first profile of the name; the session start is refused for it with
/// PattNG's words, before it stops anything; and a latency test of such a profile fails (-1), as PattNG's does, never
/// measured without that hop. Every name is new to the test, as the database is shared; the tests do not run beside the
/// others that write to it. No core runs, and nothing reaches the network.
/// </summary>
[NotInParallel]
public class SubscriptionNameTests : IDisposable
{
    private static readonly TimeSpan Soon = TimeSpan.FromSeconds(10);

    private readonly string _root = Directory.CreateTempSubdirectory("pattn-subscription-names-").FullName;

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

    private static string NewName(string prefix)
    {
        return $"{prefix}-{Guid.NewGuid():N}";
    }

    private static async Task Store(params ProfileItem[] profiles)
    {
        SQLiteHelper.Instance.CreateTable<ProfileItem>();
        foreach (var profile in profiles)
        {
            await SQLiteHelper.Instance.ReplaceAsync(profile);
        }
    }

    /// <summary>A stored subscription with the previous and the next profile it names; its id.</summary>
    private static async Task<string> Subscription(string? prev, string? next)
    {
        SQLiteHelper.Instance.CreateTable<SubItem>();
        var id = NewName("sub");
        await SQLiteHelper.Instance.ReplaceAsync(new SubItem { Id = id, Remarks = "chained", Url = string.Empty, PrevProfile = prev, NextProfile = next });
        return id;
    }

    private static ProfileItem In(string subId, ProfileItem profile)
    {
        profile.Subid = subId;
        return profile;
    }

    /// <summary>What a session start builds for <paramref name="node" />, while the active routing has no rule.</summary>
    private static Task<CoreConfigContextBuilderResult> Session(ProfileItem node)
    {
        return RuleTargetChainTests.WithRules([], () => CoreConfigContextBuilder.Build(NewConfig(), node));
    }

    /// <summary>Two profiles of <paramref name="name" />, the spaces around it aside, which it names neither of.</summary>
    private static ProfileItem[] Twins(string name)
    {
        return [Vless(NewName("twin"), name, "198.51.100.91"), Vless(NewName("twin"), $"{name} ", "198.51.100.92")];
    }

    // ---- the session

    [Test]
    [Arguments(false, false)]
    [Arguments(false, true)]
    [Arguments(true, false)]
    [Arguments(true, true)]
    public async Task ASubscriptionNamingAProfileNoneOrSeveralHaveRefusesTheSessionOfEveryProfileType(bool several, bool asNext)
    {
        var named = NewName("named");
        ProfileItem[] twins = several ? Twins(named) : [];
        var subId = await Subscription(asNext ? null : named, asNext ? named : null);
        var vless = In(subId, Vless(NewName("vless"), NewName("vless")));
        var member = Vless(NewName("member"), NewName("member"), "198.51.100.93");
        var group = In(subId, CoreConfigTestFactory.CreatePolicyGroupNode(ECoreType.Xray, NewName("group"), NewName("group"), [member.IndexId]));
        var warp = In(subId, Warp(NewName("warp"), NewName("WARP")));
        await Store([vless, member, group, warp, .. twins]);
        var problem = string.Format(several ? ResUI.MsgRoutingRuleTargetDuplicate : ResUI.MsgRoutingRuleTargetNotFound, named);

        foreach (var node in new[] { vless, group, warp })
        {
            var (context, result) = await Session(node);

            await result.Success.Should().BeFalse().Because(node.ConfigType.ToString());
            await result.Errors.Should().BeEquivalentTo([problem], CollectionOrdering.Matching);
            // Neither run alone nor through the first profile of the name.
            await context.AllProxiesMap.Should().NotContainKey(node.IndexId);
            foreach (var twin in twins)
            {
                await context.AllProxiesMap.Should().NotContainKey(twin.IndexId);
            }
            await context.AllProxiesMap.Keys.Any(key => key.StartsWith("inner-", StringComparison.Ordinal)).Should().BeFalse();
            // As a test that builds no context of its own reads it (CoreManager.LoadCoreConfigSpeedtest).
            await (await CoreConfigContextBuilder.SubscriptionChainRefusalAsync(node)).Should().BeEqualTo(problem);
        }
    }

    [Test]
    public async Task EachNameThatFindsNoOneProfileIsTold()
    {
        var missing = NewName("missing");
        var shared = NewName("shared");
        var vless = In(await Subscription(missing, shared), Vless(NewName("vless"), NewName("vless")));
        await Store([vless, .. Twins(shared)]);

        var (_, result) = await Session(vless);

        await result.Errors.Should().BeEquivalentTo(
            [string.Format(ResUI.MsgRoutingRuleTargetNotFound, missing), string.Format(ResUI.MsgRoutingRuleTargetDuplicate, shared)],
            CollectionOrdering.Matching);
    }

    [Test]
    public async Task ANameFindsItsProfileWithoutTheSpacesAroundIt()
    {
        var prevName = NewName("prev");
        var nextName = NewName("next");
        var prev = Vless(NewName("prev"), $" {prevName}", "198.51.100.94");
        var next = Vless(NewName("next"), nextName, "198.51.100.95");
        var vless = In(await Subscription($"{prevName}  ", $" {nextName} "), Vless(NewName("vless"), NewName("vless")));
        await Store(prev, next, vless);

        var (context, result) = await Session(vless);

        await result.Success.Should().BeTrue().Because(string.Join(" | ", result.Errors));
        await context.Node.ConfigType.Should().BeEqualTo(EConfigType.ProxyChain);
        await context.Node.GetProtocolExtra().ChildItems.Should().BeEqualTo(string.Join(",", prev.IndexId, vless.IndexId, next.IndexId));
        await (await CoreConfigContextBuilder.SubscriptionChainRefusalAsync(vless)).Should().BeNull();
    }

    [Test]
    public async Task ABlankNameNamesNoProfile()
    {
        var vless = In(await Subscription("   ", string.Empty), Vless(NewName("vless"), NewName("vless")));
        await Store(vless);

        var (context, result) = await Session(vless);

        await result.Success.Should().BeTrue().Because(string.Join(" | ", result.Errors));
        await result.Warnings.Should().BeEmpty();
        await context.Node.IndexId.Should().BeEqualTo(vless.IndexId);
    }

    /// <summary>A custom configuration of <paramref name="name" />, which is no outbound a chain can pass through.</summary>
    internal static ProfileItem Custom(string name)
    {
        var custom = CoreConfigTestFactory.CreateCustomOutboundNode(ECoreType.Xray, NewName("custom"), name);
        custom.ConfigType = EConfigType.Custom;
        return custom;
    }

    [Test]
    public async Task ACustomConfigurationOfTheNameMakesNoSecondProfileOfIt()
    {
        // PattNG takesAsHop, and the editor's picker, which hides custom configurations: the profile of the name is the
        // one it finds, and the chain runs through it.
        var name = NewName("landing");
        var landing = Vless(NewName("landing"), name, "198.51.100.98");
        var vless = In(await Subscription(null, $" {name}"), Vless(NewName("vless"), NewName("vless")));
        await Store(Custom(name), landing, vless);

        var (context, result) = await Session(vless);

        await result.Success.Should().BeTrue().Because(string.Join(" | ", result.Errors));
        await context.Node.ConfigType.Should().BeEqualTo(EConfigType.ProxyChain);
        await context.Node.GetProtocolExtra().ChildItems.Should().BeEqualTo(string.Join(",", vless.IndexId, landing.IndexId));
        await (await CoreConfigContextBuilder.SubscriptionChainRefusalAsync(vless)).Should().BeNull();
        await (await CoreConfigContextBuilder.SubscriptionNamesProblemAsync(null, name)).Should().BeNull();
    }

    [Test]
    public async Task ANameOnlyACustomConfigurationHasNamesNoProfile()
    {
        // Never a hop: the chain would otherwise pass through the placeholder outbound a custom configuration gets.
        var name = NewName("only-custom");
        var custom = Custom(name);
        var vless = In(await Subscription(name, null), Vless(NewName("vless"), NewName("vless")));
        await Store(custom, vless);
        var problem = string.Format(ResUI.MsgRoutingRuleTargetNotFound, name);

        var (context, result) = await Session(vless);

        await result.Success.Should().BeFalse();
        await result.Errors.Should().BeEquivalentTo([problem], CollectionOrdering.Matching);
        await context.AllProxiesMap.Should().NotContainKey(custom.IndexId);
        await context.AllProxiesMap.Keys.Any(key => key.StartsWith("inner-", StringComparison.Ordinal)).Should().BeFalse();
        await (await CoreConfigContextBuilder.SubscriptionChainRefusalAsync(vless)).Should().BeEqualTo(problem);
        await (await CoreConfigContextBuilder.SubscriptionNamesProblemAsync(name, null)).Should().BeEqualTo(problem);
    }

    // ---- latency tests (PattNG: getV2rayConfig4Speedtest refuses such a profile, and its test gives -1)

    /// <summary>The Aether tester of the app, but on fake cores: its checks are the app's, and no Xray or core starts.</summary>
    private AetherDelayTester Tester(Config config, List<string> started)
    {
        var rig = new AetherMeasureRig(_root);
        return new AetherDelayTester(new AetherTestHost
        {
            Cores = rig.Cores.Manager,
            Build = profile => CoreConfigContextBuilder.Build(config, profile, refuseRuleTargets: false),
            Generate = (_, port) =>
            {
                lock (started)
                {
                    started.Add($"generate {port}");
                }
                return new RetResult(false, "not expected", null);
            },
            StartXray = _ =>
            {
                lock (started)
                {
                    started.Add("xray start");
                }
                return Task.FromResult<IAsyncDisposable?>(null);
            },
        });
    }

    [Test]
    [Arguments(ESpeedActionType.Realping)]
    [Arguments(ESpeedActionType.Mixedtest)]
    public async Task ALatencyTestOfSuchAProfileFailsAndIsNeverMeasuredWithoutTheHop(ESpeedActionType actionType)
    {
        var vless = In(await Subscription(NewName("missing"), null), Vless(NewName("vless"), NewName("vless")));
        await Store(vless);
        var config = NewConfig();
        config.UiItem.HideColumnIpInfo = true;
        var started = new List<string>();
        var shown = new List<SpeedTestResult>();
        var service = new SpeedtestService(config, result =>
        {
            lock (shown)
            {
                shown.Add(result);
            }
            return Task.CompletedTask;
        })
        {
            AetherTester = Tester(config, started),
            AetherCandidates = AetherTestCandidates.FromDatabase,
        };
        var method = typeof(SpeedtestService).GetMethod("RunAsync", BindingFlags.Instance | BindingFlags.NonPublic)!;

        await RuleTargetChainTests.WithRules([], async () =>
        {
            await ((Task)method.Invoke(service, [actionType, new List<ProfileItem> { vless }, CancellationToken.None])!).WaitAsync(Soon);
            return true;
        });

        List<SpeedTestResult> rows;
        lock (shown)
        {
            rows = [.. shown.Where(row => row.IndexId == vless.IndexId)];
        }
        await rows.Where(row => !string.IsNullOrEmpty(row.Delay)).Select(row => row.Delay).LastOrDefault().Should().BeEqualTo("-1");
        if (actionType == ESpeedActionType.Mixedtest)
        {
            await rows.Where(row => !string.IsNullOrEmpty(row.Speed)).Select(row => row.Speed).LastOrDefault().Should().BeEqualTo(ResUI.SpeedtestingSkip);
        }
        // Refused by the checks before anything was generated or started for it.
        await started.Should().BeEmpty();
    }

    [Test]
    public async Task TheBatchLeavesSuchAProfileUntestedThoughAnotherChainHoldsIt()
    {
        // The batch, which skips the checks, would otherwise find the profile in the context, where the chain of another
        // subscription that names it as its previous profile put it, and measure it without its own hop.
        var refused = In(await Subscription(NewName("missing"), null), Vless(NewName("refused"), NewName("refused"), "198.51.100.96"));
        var behind = In(await Subscription(refused.Remarks, null), Vless(NewName("behind"), NewName("behind"), "198.51.100.97"));
        await Store(refused, behind);
        SQLiteHelper.Instance.CreateTable<FullConfigTemplateItem>();
        SQLiteHelper.Instance.CreateTable<DNSItem>();
        var behindItem = new ServerTestItem { IndexId = behind.IndexId, ConfigType = behind.ConfigType, Port = behind.Port, Profile = behind, CoreType = ECoreType.Xray };
        var refusedItem = new ServerTestItem { IndexId = refused.IndexId, ConfigType = refused.ConfigType, Port = refused.Port, Profile = refused, CoreType = ECoreType.Xray, QueueNum = 1, AllowTest = true };
        var file = Path.Combine(_root, "speedtest.json");

        var result = await RuleTargetChainTests.WithRules([], () =>
            CoreConfigHandler.GenerateClientSpeedtestConfig(NewConfig(), file, [behindItem, refusedItem], ECoreType.Xray));

        await result.Success.Should().BeTrue().Because(result.Msg ?? string.Empty);
        await refusedItem.AllowTest.Should().BeFalse();
        await behindItem.AllowTest.Should().BeTrue();
        var generated = JsonUtils.Deserialize<V2rayConfig>(File.ReadAllText(file))!;
        await generated.inbounds.Select(inbound => inbound.port ?? 0).Should().BeEquivalentTo([behindItem.Port]);
    }
}
