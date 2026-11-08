using ServiceLib.Tests.Aether;
using ServiceLib.Tests.CoreConfig;
using static ServiceLib.Tests.CoreConfig.V2ray.AetherConfigTests;

namespace ServiceLib.Tests.Services;

/// <summary>
/// PattN (design 2.12, critiques 21 and 24): the speed tests beside Aether: their port pickers never take a port of the
/// Aether core, a group or a chain that dials an Aether core is not tested in the batch, a scan Aether profile is tested
/// although it has no port, and the row of a profile the Aether tester tests shows what came of it. No core runs; the
/// servers a probe reaches are the test's own, on the loopback.
/// </summary>
public class SpeedtestServiceAetherTests : IDisposable
{
    private const int L = 10808;
    private const int P = AetherPorts.DefaultListenPort;
    private static readonly TimeSpan Soon = TimeSpan.FromSeconds(10);

    private readonly string _root = Directory.CreateTempSubdirectory("pattn-speedtest-aether-").FullName;

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

    /// <summary>The first port a speed test picks: the local port plus the speed test's offset.</summary>
    private const int SpeedtestPort = L + (int)EInboundProtocol.speedtest;

    private static ServerTestItem Item(ProfileItem profile, int queue = 0)
    {
        return new ServerTestItem
        {
            IndexId = profile.IndexId,
            ConfigType = profile.ConfigType,
            Port = profile.Port,
            Profile = profile,
            QueueNum = queue,
            CoreType = profile.CoreType ?? ECoreType.Xray,
        };
    }

    [Test]
    public async Task APortOfTheAetherCoreGivesWayToAnother()
    {
        await AetherPorts.Unreserved(5000, AetherPorts.DefaultListenPort).Should().BeEqualTo(5000);
        await AetherPorts.Unreserved(AetherPorts.DefaultListenPort + 4, AetherPorts.DefaultListenPort).Should().BeEqualTo(AetherPorts.DefaultListenPort + 4);
        for (var offset = 0; offset <= AetherPorts.SecondaryOffset; offset++)
        {
            var port = AetherPorts.Unreserved(AetherPorts.DefaultListenPort + offset, AetherPorts.DefaultListenPort);
            await AetherPorts.IsReserved(port, AetherPorts.DefaultListenPort).Should().BeFalse();
            await (port > 0).Should().BeTrue();
        }
    }

    [Test]
    [NotInParallel("AppManager config")]
    public async Task TheBatchOfXrayTakesNoPortOfTheAetherCore()
    {
        // The Aether listen port where the speed test starts picking: the four ports of the core are passed over.
        var config = NewConfig(config => config.CoreBasicItem.AetherListenPort = SpeedtestPort);
        var v1 = Vless("v1", "v1");
        var v2 = Vless("v2", "v2");
        var context = AetherContext(config, new ProfileItem { IndexId = "dummy", CoreType = ECoreType.Xray }, v1, v2);
        var items = new List<ServerTestItem> { Item(v1), Item(v2, 1) };

        var result = new CoreConfigV2rayService(context).GenerateClientSpeedtestConfig(items);

        await result.Success.Should().BeTrue().Because(result.Msg ?? string.Empty);
        foreach (var item in items)
        {
            await item.AllowTest.Should().BeTrue();
            await AetherPorts.IsReserved(item.Port, SpeedtestPort).Should().BeFalse().Because($"port {item.Port}");
            await (item.Port > SpeedtestPort + AetherPorts.SecondaryOffset).Should().BeTrue().Because($"port {item.Port}");
        }
    }

    [Test]
    [NotInParallel("AppManager config")]
    public async Task TheBatchOfSingBoxTakesNoPortOfTheAetherCore()
    {
        var config = NewConfig(config => config.CoreBasicItem.AetherListenPort = SpeedtestPort);
        var v1 = Vless("v1", "v1");
        v1.CoreType = ECoreType.sing_box;
        var context = CoreConfigTestFactory.CreateContext(config, new ProfileItem { IndexId = "dummy", CoreType = ECoreType.sing_box }, ECoreType.sing_box);
        context.AllProxiesMap[v1.IndexId] = v1;
        var items = new List<ServerTestItem> { Item(v1) };

        var result = new CoreConfigSingboxService(context).GenerateClientSpeedtestConfig(items);

        await result.Success.Should().BeTrue().Because(result.Msg ?? string.Empty);
        await items[0].AllowTest.Should().BeTrue();
        await AetherPorts.IsReserved(items[0].Port, SpeedtestPort).Should().BeFalse().Because($"port {items[0].Port}");
    }

    [Test]
    [NotInParallel("AppManager config")]
    public async Task TheTestOfOneProfileTakesNoPortOfTheAetherCore()
    {
        var config = NewConfig(config => config.CoreBasicItem.AetherListenPort = SpeedtestPort + 2);
        var vless = Vless("one", "one");
        var context = AetherContext(config, vless);
        var item = Item(vless, queue: 2);
        var file = Path.Combine(Directory.CreateTempSubdirectory("pattn-speedtest-").FullName, "configTest.json");

        var result = await CoreConfigHandler.GenerateClientSpeedtestConfig(config, context, item, file);

        await result.Success.Should().BeTrue().Because(result.Msg ?? string.Empty);
        await AetherPorts.IsReserved(item.Port, SpeedtestPort + 2).Should().BeFalse().Because($"port {item.Port}");
        await JsonNode.Parse(File.ReadAllText(file))!["inbounds"]![0]!["port"]!.GetValue<int>().Should().BeEqualTo(item.Port);
        Directory.Delete(Path.GetDirectoryName(file)!, recursive: true);
    }

    [Test]
    [NotInParallel("AppManager config")]
    public async Task AGroupThatDialsAnAetherCoreIsNotTestedInTheBatch()
    {
        var config = NewConfig();
        var warp = Warp();
        var vless = Vless("v", "v");
        var group = CoreConfigTestFactory.CreatePolicyGroupNode(ECoreType.Xray, "group", "group", [warp.IndexId]);
        var context = AetherContext(config, new ProfileItem { IndexId = "dummy", CoreType = ECoreType.Xray }, warp, vless, group);
        var groupItem = Item(group);
        var vlessItem = Item(vless, 1);

        var result = new CoreConfigV2rayService(context).GenerateClientSpeedtestConfig([groupItem, vlessItem]);

        await result.Success.Should().BeTrue().Because(result.Msg ?? string.Empty);
        // Its test is the Aether tester's; left in the batch, it would dial a core that is not there.
        await groupItem.AllowTest.Should().BeFalse();
        await vlessItem.AllowTest.Should().BeTrue();
        var generated = JsonUtils.Deserialize<V2rayConfig>(result.Data!.ToString())!;
        await generated.inbounds.Select(inbound => inbound.port ?? 0).Should().BeEquivalentTo([vlessItem.Port]);
        await generated.outbounds.Any(outbound => outbound.protocol == "socks").Should().BeFalse();
    }

    [Test]
    [NotInParallel("AppManager config")]
    public async Task AScanAetherProfileIsTestedThoughItHasNoPort()
    {
        // Critique 21: GetClearItem dropped every profile without a port; an Aether profile left to the scanner has none.
        CoreConfigTestFactory.BindAppManagerConfig(CoreConfigTestFactory.CreateConfig());
        SQLiteHelper.Instance.CreateTable<ProfileItem>();
        var suffix = Guid.NewGuid().ToString("N");
        // Not stored: the profiles a run is given stand for themselves where the database has none of theirs, and the test
        // database is shared with the user's own runs.
        var scan = CoreConfigTestFactory.CreateAetherNode($"scan-{suffix}", $"scan-{suffix}");
        var portless = new ProfileItem { IndexId = $"vless-{suffix}", ConfigType = EConfigType.VLESS, Remarks = $"vless-{suffix}", Address = "1.2.3.4", Port = 0 };
        var shown = new List<SpeedTestResult>();
        var service = new SpeedtestService(CoreConfigTestFactory.CreateConfig(), result =>
        {
            lock (shown)
            {
                shown.Add(result);
            }
            return Task.CompletedTask;
        });
        var method = typeof(SpeedtestService).GetMethod("GetClearItem", BindingFlags.Instance | BindingFlags.NonPublic)!;

        var items = await (Task<List<ServerTestItem>>)method.Invoke(service, [ESpeedActionType.Realping, new List<ProfileItem> { scan, portless }])!;

        await items.Select(item => item.IndexId).Should().BeEquivalentTo([scan.IndexId]);
        await items[0].CoreType.Should().BeEqualTo(ECoreType.Xray);
        await items[0].Profile.ConfigType.Should().BeEqualTo(EConfigType.Aether);
    }

    // ---- the rows of the profiles the Aether tester tests (critique 24)

    /// <summary>A speed test service on <paramref name="config" />, whose rows land in <paramref name="shown" />, testing what needs an Aether core with <paramref name="tester" />.</summary>
    private static SpeedtestService Service(Config config, List<SpeedTestResult> shown, AetherDelayTester tester)
    {
        return new SpeedtestService(config, result =>
        {
            lock (shown)
            {
                shown.Add(result);
            }
            return Task.CompletedTask;
        })
        {
            AetherTester = tester,
        };
    }

    /// <summary>The tests of <paramref name="items" />, which need an Aether core, as a run makes them after its batch.</summary>
    private static Task RunAetherTests(SpeedtestService service, ESpeedActionType actionType, params ServerTestItem[] items)
    {
        var method = typeof(SpeedtestService).GetMethod("RunAetherTestsAsync", BindingFlags.Instance | BindingFlags.NonPublic)!;
        return (Task)method.Invoke(service, [actionType, items.ToList(), new ConcurrentDictionary<string, byte>(), CancellationToken.None])!;
    }

    /// <summary>What the row of <paramref name="id" /> shows last in the column <paramref name="column" /> picks.</summary>
    private static string? Last(List<SpeedTestResult> shown, string id, Func<SpeedTestResult, string?> column)
    {
        lock (shown)
        {
            return shown.Where(result => result.IndexId == id && !string.IsNullOrEmpty(column(result))).Select(column).LastOrDefault();
        }
    }

    private static Config TestConfig(string pingUrl = "http://127.0.0.1:1/generate_204")
    {
        var config = CoreConfigTestFactory.CreateConfig();
        config.SpeedTestItem.SpeedPingTestUrl = pingUrl;
        // No IP lookup: its service is on the internet.
        config.UiItem.HideColumnIpInfo = true;
        return config;
    }

    [Test]
    public async Task AnAetherRowShowsWhatItsTestGaveAndNeverZero()
    {
        var warp = CoreConfigTestFactory.CreateAetherNode("row-warp", "WARP row");
        var wireGuard = CoreConfigTestFactory.CreateAetherNode("row-wg", "WARP row wg", AetherProtocol.WireGuard.Type);

        // Beside a session of another core: untested, and why.
        var beside = new AetherMeasureRig(_root) { Needed = AetherCore.Of(warp, P) };
        await beside.Cores.Manager.StartSessionAsync(AetherCore.Of(wireGuard, P).Through(P + AetherPorts.SecondaryOffset));
        beside.Answer(P);
        var shown = new List<SpeedTestResult>();
        await RunAetherTests(Service(TestConfig(), shown, beside.Tester()), ESpeedActionType.Mixedtest, Item(warp)).WaitAsync(Soon);
        await Last(shown, warp.IndexId, row => row.Delay).Should().BeEqualTo(ResUI.MsgAetherTestSkipped);
        await Last(shown, warp.IndexId, row => row.Speed).Should().BeEqualTo(ResUI.SpeedtestingSkip);
        await beside.Cores.Manager.StopSessionAsync();

        // A profile whose session would be refused: -1.
        var refused = new AetherMeasureRig(_root)
        {
            Needed = AetherCore.Of(warp, P),
            Checks = new NodeValidatorResult(["Core 'sing_box' does not support protocol 'Aether'"], []),
        };
        shown.Clear();
        await RunAetherTests(Service(TestConfig(), shown, refused.Tester()), ESpeedActionType.Realping, Item(warp)).WaitAsync(Soon);
        await Last(shown, warp.IndexId, row => row.Delay).Should().BeEqualTo("-1");

        // Behind a scan or a key run: untested.
        var busy = new AetherMeasureRig(_root) { Needed = AetherCore.Of(warp, P) };
        var scanning = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var scan = busy.Cores.Manager.RunStandaloneAsync<string>(new AetherStandaloneRun(["--scan"], AetherRole.Scan) { Exit = AetherExit.Plain },
            async (_, _) =>
            {
                scanning.TrySetResult();
                await release.Task;
                return "done";
            });
        await scanning.Task.WaitAsync(Soon);
        shown.Clear();
        await RunAetherTests(Service(TestConfig(), shown, busy.Tester()), ESpeedActionType.Realping, Item(warp)).WaitAsync(Soon);
        await Last(shown, warp.IndexId, row => row.Delay).Should().BeEqualTo(ResUI.SpeedtestingSkip);
        release.SetResult();
        await scan.WaitAsync(Soon);
    }

    [Test]
    public async Task WithoutTheCoreAnAetherRowIsLeftUntestedNotInvalid()
    {
        // D5: the row says why and stores 0, which "Remove invalid" leaves alone; a result from before does not stay.
        const string missing = "The Core file (file name: aether) was not found";
        var warp = CoreConfigTestFactory.CreateAetherNode($"missing-{Guid.NewGuid():N}", "WARP missing");
        var rig = new AetherMeasureRig(_root) { Needed = AetherCore.Of(warp, P), MissingCore = () => missing };
        rig.Answer(P);
        ProfileExManager.Instance.SetTestDelay(warp.IndexId, 321);
        var shown = new List<SpeedTestResult>();

        await RunAetherTests(Service(TestConfig(), shown, rig.Tester()), ESpeedActionType.Mixedtest, Item(warp)).WaitAsync(Soon);

        await Last(shown, warp.IndexId, row => row.Delay).Should().BeEqualTo(missing);
        await Last(shown, warp.IndexId, row => row.Speed).Should().BeEqualTo(ResUI.SpeedtestingSkip);
        var stored = (await ProfileExManager.Instance.GetProfileExs()).First(item => item.IndexId == warp.IndexId);
        await stored.Delay.Should().BeEqualTo(0);
        // No test Xray, and no core, started for it.
        await rig.Cores.Events.Contains("xray start").Should().BeFalse();
        await rig.Cores.Cores.Count.Should().BeEqualTo(0);
    }

    [Test]
    public async Task ATestThatASessionStartEndsInItsProbeShowsWhyItIsUntested()
    {
        // Critique 24: the session start cancels the test while its request is on its way; the row says the test was
        // skipped rather than keep "Testing..." until the list is read again, and so does the speed it never measured.
        var warp = CoreConfigTestFactory.CreateAetherNode("cut-warp", "WARP cut");
        using var socks = new StallingSocksStub();
        var rig = new AetherMeasureRig(_root) { Needed = AetherCore.Of(warp, P) };
        rig.Ports.Enqueue(socks.Port);
        rig.Answer(P);
        var shown = new List<SpeedTestResult>();
        var testing = RunAetherTests(Service(TestConfig(), shown, rig.Tester(TimeSpan.FromSeconds(10))), ESpeedActionType.Mixedtest, Item(warp));
        await socks.Connected.Task.WaitAsync(Soon);

        await rig.Cores.Manager.CancelProbesAsync();
        await testing.WaitAsync(Soon);
        rig.Cores.Manager.EndSessionStart();

        await Last(shown, warp.IndexId, row => row.Delay).Should().BeEqualTo(ResUI.SpeedtestingSkip);
        await Last(shown, warp.IndexId, row => row.Speed).Should().BeEqualTo(ResUI.SpeedtestingSkip);
    }

    [Test]
    [NotInParallel("CertPemManager config")]
    public async Task ASpeedTestThatReachesItsTimeKeepsTheDelayItMeasured()
    {
        // As RunMixedTestAsync: the download's own timeout ends the speed test, not the row's delay, which "Skip test" must
        // not replace; nor is it a session that started.
        var warp = CoreConfigTestFactory.CreateAetherNode("slow-warp", "WARP slow");
        using var answers = new HttpStub("204 No Content");
        using var slow = new SlowHttpStub();
        using var socks = new SocksStub();
        var config = TestConfig($"http://127.0.0.1:{answers.Port}/generate_204");
        config.SpeedTestItem.SpeedTestUrl = $"http://127.0.0.1:{slow.Port}/100MB.bin";
        config.SpeedTestItem.SpeedTestTimeout = 1;
        // The download reads the root store the settings name.
        await CertPemManager.Instance.Init(config);
        // The configuration needs no core of the test's own: its Xray, here the relay, carries the requests.
        var rig = new AetherMeasureRig(_root);
        rig.Ports.Enqueue(socks.Port);
        var shown = new List<SpeedTestResult>();

        await RunAetherTests(Service(config, shown, rig.Tester(TimeSpan.FromSeconds(10))), ESpeedActionType.Speedtest, Item(warp)).WaitAsync(Soon);

        var delay = Last(shown, warp.IndexId, row => row.Delay);
        await (int.TryParse(delay, out var measured) && measured > 0).Should().BeTrue().Because($"the row shows \"{delay}\"");
        // The speed test ran and ended on its own: what it showed last stays, as upstream's does; it was not skipped.
        var speed = Last(shown, warp.IndexId, row => row.Speed);
        await (speed != null && speed != ResUI.SpeedtestingSkip).Should().BeTrue().Because($"the speed shows \"{speed}\"");
    }

    [Test]
    [NotInParallel("AppManager config")]
    public async Task AFailedLookupOfTheStoredProfilesIsTheRunsFailureNotAnUnseenOne()
    {
        // The partition reads the subscriptions and the groups of the database: an error there is logged and shown as the
        // run's failure, as every other error of a run is, rather than end the run unobserved.
        CoreConfigTestFactory.BindAppManagerConfig(CoreConfigTestFactory.CreateConfig());
        SQLiteHelper.Instance.CreateTable<ProfileItem>();
        var subscribed = Vless($"locked-{Guid.NewGuid():N}", "locked");
        subscribed.Subid = "locked-sub";
        var shown = new List<SpeedTestResult>();
        var service = new SpeedtestService(CoreConfigTestFactory.CreateConfig(), result =>
        {
            lock (shown)
            {
                shown.Add(result);
            }
            return Task.CompletedTask;
        })
        {
            AetherCandidates = () => new AetherTestCandidates(
                _ => throw new InvalidOperationException("database is locked"),
                _ => Task.FromResult<ByName<ProfileItem>>(ByName<ProfileItem>.None.Instance),
                _ => Task.FromResult<IEnumerable<ProfileItem>>([])),
        };
        var method = typeof(SpeedtestService).GetMethod("RunAsync", BindingFlags.Instance | BindingFlags.NonPublic)!;

        await ((Task)method.Invoke(service, [ESpeedActionType.Realping, new List<ProfileItem> { subscribed }, CancellationToken.None])!).WaitAsync(Soon);

        List<SpeedTestResult> rows;
        lock (shown)
        {
            rows = [.. shown];
        }
        await rows.Any(row => row.IndexId == string.Empty && row.Delay == "database is locked").Should().BeTrue();
    }
}
