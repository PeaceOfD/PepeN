using System.Threading.Channels;
using ServiceLib.Tests.CoreConfig;
using TUnit.Assertions.Enums;

namespace ServiceLib.Tests.Aether;

/// <summary>
/// From PattNG AetherDelayTesterTest (the route table, readiness, the TCP ping of an edge, the probe and its budget), and
/// PattN's own tests of a whole test on fakes (design 2.12.2, critique 9): no core runs, and nothing reaches the network;
/// the HTTP and SOCKS servers are the test's own, on the loopback.
/// </summary>
public class AetherDelayTesterTests : IDisposable
{
    private const int P = AetherPorts.DefaultListenPort;
    private static readonly TimeSpan Soon = TimeSpan.FromSeconds(5);

    private readonly string _root = Directory.CreateTempSubdirectory("pattn-aether-test-").FullName;

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

    private static ProfileItem Aether(AetherProtocol protocol, string id = "a", Action<ProfileItem>? change = null)
    {
        var profile = CoreConfigTestFactory.CreateAetherNode(id, $"WARP {id}", protocol.Type);
        change?.Invoke(profile);
        return profile;
    }

    private static ProfileItem WithExtra(ProfileItem profile, Func<ProtocolExtraItem, ProtocolExtraItem> extra)
    {
        profile.SetProtocolExtra(extra(profile.GetProtocolExtra()));
        return profile;
    }

    /// <summary>The live session, running <paramref name="running" /> on its own unless <paramref name="exit" /> says what its core dials out through.</summary>
    private static AetherLiveSession Session(ProfileItem running, bool listening = true, int port = P, string? exit = null)
    {
        return new AetherLiveSession(AetherCore.Of(running, port).Arguments, port, listening, exit ?? AetherExit.Of(running).Key);
    }

    // ---- the route (PattNG route)

    [Test]
    public async Task WithoutAnAetherSessionEachTestGetsItsOwnTunnel()
    {
        await AetherDelayTester.Route(AetherCore.Of(Aether(AetherProtocol.Masque), P), null).Should().BeEqualTo(AetherTestRoute.NewTunnel);
    }

    [Test]
    public async Task TheRunningProfileIsMeasuredThroughTheLiveSession()
    {
        var wireguard = Aether(AetherProtocol.WireGuard, change: profile =>
        {
            profile.Address = "162.159.192.1";
            profile.Port = 2408;
        });
        // Told by the session's arguments: PattN always knows them, so no selected profile stands in for them.
        await AetherDelayTester.Route(AetherCore.Of(wireguard, P), Session(wireguard)).Should().BeEqualTo(AetherTestRoute.ActiveSession);
        // The same tunnel on another port, as once the Aether listen port is changed under a running session, is not
        // where the configuration of the test dials, nor can a test core take the port the session holds.
        await AetherDelayTester.Route(AetherCore.Of(wireguard, P), Session(wireguard, port: 20808)).Should().BeEqualTo(AetherTestRoute.Skip);
    }

    [Test]
    public async Task TheRunningProfileIsLeftUntestedWhileItsSessionIsStillConnecting()
    {
        var wireguard = Aether(AetherProtocol.WireGuard, change: profile =>
        {
            profile.Address = "162.159.192.1";
            profile.Port = 2408;
        });
        var connecting = Session(wireguard, listening: false);
        // A request through a listener that is not up yet would fail, and that is not a failure of the profile.
        await AetherDelayTester.Route(AetherCore.Of(wireguard, P), connecting).Should().BeEqualTo(AetherTestRoute.NotReady);
        // The other routes do not depend on the listener.
        await AetherDelayTester.Route(AetherCore.Of(Aether(AetherProtocol.Gool), P), connecting).Should().BeEqualTo(AetherTestRoute.Skip);
        await AetherDelayTester.Route(AetherCore.Of(Aether(AetherProtocol.Masque), P), connecting).Should().BeEqualTo(AetherTestRoute.Skip);
    }

    [Test]
    public async Task TheSessionMeasuresOnlyACoreThatDialsOutThroughTheSameExitNode()
    {
        var wireguard = Aether(AetherProtocol.WireGuard, change: profile =>
        {
            profile.Address = "162.159.192.1";
            profile.Port = 2408;
        });
        var hop = new ProfileItem { ConfigType = EConfigType.VLESS, Remarks = "hop", Address = "1.2.3.4", Port = 443 };
        var alone = AetherCore.Of(wireguard, P);
        var chained = alone with { Exit = AetherExit.Through([hop]) };
        // The session runs the profile on its own: the profile is measured through it, a chain that dials out through a
        // hop is not, though it runs the same tunnel on the same port.
        var plainSession = Session(wireguard);
        await AetherDelayTester.Route(alone, plainSession).Should().BeEqualTo(AetherTestRoute.ActiveSession);
        await AetherDelayTester.Route(chained, plainSession).Should().BeEqualTo(AetherTestRoute.Skip);
        // And the other way round.
        var chainSession = Session(wireguard, exit: chained.Exit.Key);
        await AetherDelayTester.Route(chained, chainSession).Should().BeEqualTo(AetherTestRoute.ActiveSession);
        await AetherDelayTester.Route(alone, chainSession).Should().BeEqualTo(AetherTestRoute.Skip);
    }

    [Test]
    public async Task EveryOtherCoreIsLeftUntestedBesideTheLiveSession()
    {
        // The session's core holds the one port every core listens on, whichever key either of them uses.
        var running = Aether(AetherProtocol.Masque, change: profile =>
        {
            profile.Address = "162.159.198.1";
            profile.Port = 443;
        });
        var live = Session(running);
        foreach (var protocol in new[] { AetherProtocol.Masque, AetherProtocol.WireGuard, AetherProtocol.Gool, AetherProtocol.Mim })
        {
            await AetherDelayTester.Route(AetherCore.Of(Aether(protocol, "b"), P), live).Should().BeEqualTo(AetherTestRoute.Skip);
        }
    }

    [Test]
    public async Task TheSessionIsReadFromItsCore()
    {
        var cores = new FakeAetherCores(_root);
        var core = new AetherCore(["--protocol", "wg", "--bind", "127.0.0.1:20808", "--upstream", "socks5://127.0.0.1:20811"],
            new AetherExit(DialMode: "code-1"));
        await (await cores.Manager.StartSessionAsync(core)).Should().BeNull();
        int? asked = null;

        var session = await AetherDelayTester.LiveSessionOf(cores.Manager.Session, (port, _) =>
        {
            asked = port;
            return Task.FromResult(false);
        });

        await (session != null).Should().BeTrue();
        await session!.Arguments.Should().BeEquivalentTo(core.Arguments, CollectionOrdering.Matching);
        await session.Port.Should().BeEqualTo(20808);
        await asked.Should().BeEqualTo(20808);
        await session.Listening.Should().BeFalse();
        await session.ExitKey.Should().BeEqualTo(core.Exit.Key);
        await (await AetherDelayTester.LiveSessionOf(null, (_, _) => Task.FromResult(true))).Should().BeNull();
        await cores.Manager.StopSessionAsync();
    }

    // ---- readiness (PattNG awaitListening)

    [Test]
    public async Task ATestCoreThatDialsPsiphonIsReadyOnlyOnTheCoresWord()
    {
        using var socks = new SocksGreeter();
        var output = Channel.CreateUnbounded<string>();
        static Func<TimeSpan> Budget(int ms)
        {
            var clock = Stopwatch.StartNew();
            return () => TimeSpan.FromMilliseconds(ms) - clock.Elapsed;
        }
        var poll = TimeSpan.FromMilliseconds(50);
        // The listener answers, but nothing said yet: not ready within a short budget.
        await (await AetherDelayTester.AwaitTunnelAsync(socks.Port, output.Reader, Budget(600), true, AetherCoreManager.AnswersSocksAsync, poll)).Should().BeFalse();
        // Without the need, the answering listener is enough.
        await (await AetherDelayTester.AwaitTunnelAsync(socks.Port, output.Reader, Budget(2_000), false, AetherCoreManager.AnswersSocksAsync, poll)).Should().BeTrue();

        output.Writer.TryWrite("[2026-09-24T10:00:00.000Z INFO  aether] [*] starting psiphon through the tunnel at 127.0.0.1:10820");
        output.Writer.TryWrite($"[2026-09-24T10:00:00.000Z INFO  aether] [+] psiphon is ready; 127.0.0.1:{socks.Port} leaves through psiphon, carried by the tunnel");
        await (await AetherDelayTester.AwaitTunnelAsync(socks.Port, output.Reader, Budget(2_000), true, AetherCoreManager.AnswersSocksAsync, poll)).Should().BeTrue();

        // A core that ends before its word is a failed test, not a wait.
        var ended = Channel.CreateUnbounded<string>();
        ended.Writer.TryComplete();
        var clock = Stopwatch.StartNew();
        await (await AetherDelayTester.AwaitTunnelAsync(socks.Port, ended.Reader, Budget(2_000), true, AetherCoreManager.AnswersSocksAsync, poll)).Should().BeFalse();
        await (clock.Elapsed < TimeSpan.FromSeconds(1)).Should().BeTrue();
    }

    // ---- the TCP ping of an edge (PattNG reachability)

    [Test]
    public async Task ATcpPingProbesThePinnedEdgeInsteadOfOpeningATunnel()
    {
        var probed = new List<(string Host, int Port)>();
        Task<int> Connect(string host, int port, CancellationToken _)
        {
            probed.Add((host, port));
            return Task.FromResult(42);
        }

        var pinned = Aether(AetherProtocol.Masque, change: profile =>
        {
            profile.Address = "162.159.198.1";
            profile.Port = 2408;
        });
        await (await AetherDelayTester.ReachabilityAsync(pinned, Connect)).Should().BeEqualTo(42);
        await probed.Should().BeEquivalentTo([("162.159.198.1", 443)]);

        probed.Clear();
        var hops = WithExtra(Aether(AetherProtocol.Gool), extra => extra with { AetherWiwOuter = "162.159.192.1:2408", AetherWiwInner = "188.114.96.1:894" });
        await (await AetherDelayTester.ReachabilityAsync(hops, Connect)).Should().BeEqualTo(42);
        await probed.Should().BeEquivalentTo([("162.159.192.1", 443)]);

        probed.Clear();
        var masqueHops = WithExtra(Aether(AetherProtocol.Mim), extra => extra with { AetherWiwOuter = "162.159.197.3:443", AetherWiwInner = "188.114.96.1:443" });
        await (await AetherDelayTester.ReachabilityAsync(masqueHops, Connect)).Should().BeEqualTo(42);
        await probed.Should().BeEquivalentTo([("162.159.197.3", 443)]);

        await (await AetherDelayTester.ReachabilityAsync(pinned, (_, _, _) => Task.FromResult(-1))).Should().BeEqualTo(-1);
    }

    [Test]
    public async Task AProfileLeftToTheScannerHasNothingToProbe()
    {
        static Task<int> Probes(string host, int port, CancellationToken _)
        {
            throw new InvalidOperationException("must not probe");
        }

        await (await AetherDelayTester.ReachabilityAsync(Aether(AetherProtocol.Masque), Probes)).Should().BeEqualTo(AetherDelayTester.Untested);
        await (await AetherDelayTester.ReachabilityAsync(Aether(AetherProtocol.Gool), Probes)).Should().BeEqualTo(AetherDelayTester.Untested);
        await (await AetherDelayTester.ReachabilityAsync(Aether(AetherProtocol.Mim), Probes)).Should().BeEqualTo(AetherDelayTester.Untested);
        var halfPinned = Aether(AetherProtocol.WireGuard, change: profile => profile.Address = "162.159.198.1");
        await (await AetherDelayTester.ReachabilityAsync(halfPinned, Probes)).Should().BeEqualTo(AetherDelayTester.Untested);
        var hostName = Aether(AetherProtocol.WireGuard, change: profile =>
        {
            profile.Address = "engage.cloudflareclient.com";
            profile.Port = 2408;
        });
        await (await AetherDelayTester.ReachabilityAsync(hostName, Probes)).Should().BeEqualTo(AetherDelayTester.Untested);
        await AetherDelayTester.Untested.Should().BeEqualTo(0);
    }

    // ---- the probe (PattNG requestDelay, through ConnectionHandler.GetRealPingTime with a budget)

    private static Task<int> RequestDelay(int socksPort, string url, TimeSpan? budget = null, CancellationToken ct = default)
    {
        return ConnectionHandler.GetRealPingTime(new WebProxy($"socks5://127.0.0.1:{socksPort}"), url, budget ?? AetherCoreManager.TestBudget, ct);
    }

    [Test]
    public async Task ADelayIsMeasuredThroughTheSocksPort()
    {
        using var http = new HttpStub("204 No Content");
        using var socks = new SocksStub();
        var delay = await RequestDelay(socks.Port, $"http://127.0.0.1:{http.Port}/generate_204");
        await (delay > 0).Should().BeTrue().Because($"delay was {delay}");
    }

    [Test]
    public async Task APlainOkAlsoCountsAsAnAnswer()
    {
        using var http = new HttpStub("200 OK");
        using var socks = new SocksStub();
        await (await RequestDelay(socks.Port, $"http://127.0.0.1:{http.Port}/") > 0).Should().BeTrue();
    }

    [Test]
    public async Task AnErrorStatusIsNotADelay()
    {
        using var http = new HttpStub("500 Internal Server Error");
        using var socks = new SocksStub();
        await (await RequestDelay(socks.Port, $"http://127.0.0.1:{http.Port}/")).Should().BeEqualTo(-1);
    }

    [Test]
    public async Task NothingListeningOrABrokenUrlIsNotADelay()
    {
        var closedPort = ClosedPort();
        await (await RequestDelay(closedPort, "http://127.0.0.1:1/generate_204")).Should().BeEqualTo(-1);
        await (await RequestDelay(closedPort, "not a url")).Should().BeEqualTo(-1);
    }

    [Test]
    public async Task AProbeThatGetsNoAnswerGivesUpWithinItsBudget()
    {
        using var socks = new StallingSocksStub();
        var clock = Stopwatch.StartNew();
        var delay = await RequestDelay(socks.Port, "http://127.0.0.1:1/generate_204", TimeSpan.FromMilliseconds(1_500));
        await delay.Should().BeEqualTo(-1);
        // One attempt within the budget, no second one with less than a second left: the first used the whole budget,
        // which a second attempt given the budget anew, or a request with a fixed timeout of its own, would not keep to.
        await socks.Connections.Should().BeEqualTo(1);
        await (clock.Elapsed < TimeSpan.FromMilliseconds(2_500)).Should().BeTrue().Because($"gave up after {clock.Elapsed}");
        // PattNG's bound: no connect that outlasts the budget unbounded.
        await (clock.Elapsed < TimeSpan.FromSeconds(6)).Should().BeTrue().Because($"gave up after {clock.Elapsed}");
    }

    [Test]
    public async Task ACancelledTestEndsItsProbeAtOnce()
    {
        using var socks = new StallingSocksStub();
        using var cancel = new CancellationTokenSource();
        var probe = RequestDelay(socks.Port, "http://127.0.0.1:1/generate_204", AetherCoreManager.TestBudget, cancel.Token);
        await (await socks.Connected.Task.WaitAsync(Soon)).Should().BeTrue();
        var clock = Stopwatch.StartNew();
        cancel.Cancel();
        var cancelled = false;
        try
        {
            await probe.WaitAsync(Soon);
        }
        catch (OperationCanceledException)
        {
            cancelled = true;
        }
        await cancelled.Should().BeTrue();
        // Its request would otherwise wait for the rest of the 12-second budget, and keep the tunnel up.
        await (clock.Elapsed < TimeSpan.FromSeconds(2)).Should().BeTrue().Because($"ended after {clock.Elapsed}");
    }

    [Test]
    public async Task TheBudgetCoversEveryAttemptAndNoneStartsWithLessThanASecondLeft()
    {
        // Two attempts at most, each given what is left, and the best answer kept.
        var given = new List<TimeSpan>();
        var left = new Queue<TimeSpan>([TimeSpan.FromSeconds(12), TimeSpan.FromSeconds(9)]);
        var times = new Queue<int>([300, 120]);
        var best = await AetherDelayTester.BestOfAttemptsAsync(() => left.Dequeue(), (budget, _) =>
        {
            given.Add(budget);
            return Task.FromResult(times.Dequeue());
        });
        await best.Should().BeEqualTo(120);
        await given.Should().BeEquivalentTo([TimeSpan.FromSeconds(12), TimeSpan.FromSeconds(9)], CollectionOrdering.Matching);

        // A failed attempt does not count; the second one does.
        left = new Queue<TimeSpan>([TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(2)]);
        times = new Queue<int>([-1, 700]);
        await (await AetherDelayTester.BestOfAttemptsAsync(() => left.Dequeue(), (_, _) => Task.FromResult(times.Dequeue()))).Should().BeEqualTo(700);

        // Under a second left, no further attempt starts.
        left = new Queue<TimeSpan>([TimeSpan.FromSeconds(1.5), TimeSpan.FromMilliseconds(999)]);
        var attempts = 0;
        await (await AetherDelayTester.BestOfAttemptsAsync(() => left.Dequeue(), (_, _) =>
        {
            attempts++;
            return Task.FromResult(-1);
        })).Should().BeEqualTo(-1);
        await attempts.Should().BeEqualTo(1);
        await (await AetherDelayTester.BestOfAttemptsAsync(() => TimeSpan.FromMilliseconds(500), (_, _) => throw new InvalidOperationException("must not start")))
            .Should().BeEqualTo(-1);
    }

    // ---- a whole test on fakes (design 2.12.2)

    [Test]
    public async Task WithoutASessionTheTestRunsACoreOfItsOwnBehindItsOwnXray()
    {
        var warp = Aether(AetherProtocol.Masque);
        var rig = new AetherMeasureRig(_root) { Needed = AetherCore.Of(warp, P) };
        rig.Answer(P);

        var result = await rig.Tester().MeasureAsync(warp, rig.Probe());

        await result.Should().BeEqualTo(new AetherTestResult(42));
        // The first port the test picks is its mixed inbound's, the second the one its core dials out through. The Xray
        // comes up before the core, which is killed before the Xray stops; the core opens no exit of its own.
        await rig.Cores.Events.Should().BeEquivalentTo(
            ["generate 41501 41502", "xray start", "spawn 1", "probe 41501", "kill 1", "xray stop"], CollectionOrdering.Matching);
        await rig.Cores.Core(0).Arguments.Should().BeEquivalentTo(rig.Needed!.Through(41502).Arguments, CollectionOrdering.Matching);
        await rig.Cores.OpenedFor.Should().BeEmpty();
    }

    [Test]
    public async Task TheRunningConfigurationIsMeasuredThroughItsLiveSession()
    {
        var warp = Aether(AetherProtocol.Masque);
        var rig = new AetherMeasureRig(_root) { Needed = AetherCore.Of(warp, P) };
        await (await rig.Cores.Manager.StartSessionAsync(rig.Needed!.Through(P + AetherPorts.SecondaryOffset))).Should().BeNull();
        rig.Answer(P);

        var result = await rig.Tester().MeasureAsync(warp, rig.Probe());

        await result.Should().BeEqualTo(new AetherTestResult(42));
        // Generated again without a core of its own: its Aether outbound dials the live core. No second core.
        await rig.Cores.Events.Should().BeEquivalentTo(
            ["spawn 1", "generate 41501 41502", "generate 41501 -", "xray start", "probe 41501", "xray stop"], CollectionOrdering.Matching);
        await rig.Cores.Manager.StopSessionAsync();
    }

    [Test]
    public async Task TheRunningConfigurationIsLeftUntestedWhileItsSessionConnects()
    {
        var warp = Aether(AetherProtocol.Masque);
        var rig = new AetherMeasureRig(_root) { Needed = AetherCore.Of(warp, P) };
        await rig.Cores.Manager.StartSessionAsync(rig.Needed!.Through(P + AetherPorts.SecondaryOffset));

        var result = await rig.Tester().MeasureAsync(warp, rig.Probe());

        await result.Should().BeEqualTo(AetherTestResult.Untested(ResUI.MsgAetherConnecting));
        await rig.Cores.Events.Should().BeEquivalentTo(["spawn 1", "generate 41501 41502"], CollectionOrdering.Matching);
        await rig.Cores.Manager.StopSessionAsync();
    }

    [Test]
    public async Task AnotherCoreIsLeftUntestedBesideTheLiveSession()
    {
        var warp = Aether(AetherProtocol.Masque);
        var wg = Aether(AetherProtocol.WireGuard, "w");
        var rig = new AetherMeasureRig(_root) { Needed = AetherCore.Of(warp, P) };
        await rig.Cores.Manager.StartSessionAsync(AetherCore.Of(wg, P).Through(P + AetherPorts.SecondaryOffset));
        rig.Answer(P);

        var result = await rig.Tester().MeasureAsync(warp, rig.Probe());

        // Untested, with what the row shows for it, never "0" (critique 24).
        await result.Should().BeEqualTo(AetherTestResult.Untested(ResUI.MsgAetherTestSkipped));
        await result.IsUntested.Should().BeTrue();
        await rig.Probed.Should().BeEmpty();
        await rig.Cores.Events.Count(e => e.StartsWith("spawn")).Should().BeEqualTo(1);
        await rig.Cores.Manager.StopSessionAsync();
    }

    [Test]
    public async Task ATestDoesNotWaitBehindAScanOrAKeyRun()
    {
        var warp = Aether(AetherProtocol.Masque);
        var rig = new AetherMeasureRig(_root) { Needed = AetherCore.Of(warp, P) };
        rig.Answer(P);
        var scanning = new TaskCompletionSource();
        var release = new TaskCompletionSource();
        var scan = rig.Cores.Manager.RunStandaloneAsync<string>(new AetherStandaloneRun(["--scan"], AetherRole.Scan) { Exit = AetherExit.Plain },
            async (_, _) =>
            {
                scanning.TrySetResult();
                await release.Task;
                return "done";
            });
        await scanning.Task.WaitAsync(Soon);

        var clock = Stopwatch.StartNew();
        var result = await rig.Tester().MeasureAsync(warp, rig.Probe());

        // Left untested at once (critique 9), without an Xray or a core started for it.
        await result.Should().BeEqualTo(AetherTestResult.Untested(ResUI.SpeedtestingSkip));
        await (clock.Elapsed < TimeSpan.FromSeconds(2)).Should().BeTrue();
        await rig.Cores.Events.Contains("xray start").Should().BeFalse();
        await rig.Cores.Events.Count(e => e.StartsWith("spawn")).Should().BeEqualTo(1);
        release.SetResult();
        await (await scan.WaitAsync(Soon)).Outcome.Should().BeEqualTo(AetherRunOutcome.Done);
    }

    [Test]
    public async Task AProfileThatCannotRunFailsWithNothingStarted()
    {
        var warp = Aether(AetherProtocol.Masque);
        var rig = new AetherMeasureRig(_root)
        {
            Needed = AetherCore.Of(warp, P),
            Checks = new NodeValidatorResult(["Core 'sing_box' does not support protocol 'Aether'"], []),
        };

        var result = await rig.Tester().MeasureAsync(warp, rig.Probe());

        await result.Should().BeEqualTo(AetherTestResult.Failed);
        await rig.Cores.Events.Should().BeEmpty();
    }

    [Test]
    public async Task ATunnelThatDoesNotComeUpFailsWithinTheBudget()
    {
        var warp = Aether(AetherProtocol.Masque);
        var rig = new AetherMeasureRig(_root) { Needed = AetherCore.Of(warp, P) };
        var clock = Stopwatch.StartNew();

        var result = await rig.Tester(TimeSpan.FromMilliseconds(500)).MeasureAsync(warp, rig.Probe());

        await result.Should().BeEqualTo(AetherTestResult.Failed);
        await (clock.Elapsed < TimeSpan.FromSeconds(3)).Should().BeTrue();
        await rig.Probed.Should().BeEmpty();
        await rig.Cores.Events.Should().BeEquivalentTo(["generate 41501 41502", "xray start", "spawn 1", "kill 1", "xray stop"], CollectionOrdering.Matching);
    }

    [Test]
    public async Task ACoreThatEndsBeforeItsTunnelIsUpFailsAtOnce()
    {
        var warp = Aether(AetherProtocol.Masque);
        var rig = new AetherMeasureRig(_root) { Needed = AetherCore.Of(warp, P) };
        rig.Cores.Script = core =>
        {
            core.Exit(1);
            return Task.CompletedTask;
        };
        var clock = Stopwatch.StartNew();

        var result = await rig.Tester(TimeSpan.FromSeconds(10)).MeasureAsync(warp, rig.Probe());

        await result.Should().BeEqualTo(AetherTestResult.Failed);
        await (clock.Elapsed < TimeSpan.FromSeconds(3)).Should().BeTrue();
        await rig.Probed.Should().BeEmpty();
    }

    [Test]
    public async Task ASessionStartLeavesTheTestUntestedAndEndsItsCore()
    {
        var warp = Aether(AetherProtocol.Masque);
        var rig = new AetherMeasureRig(_root) { Needed = AetherCore.Of(warp, P) };
        var measuring = rig.Tester(TimeSpan.FromSeconds(10)).MeasureAsync(warp, rig.Probe());
        for (var waited = 0; waited < 200 && rig.Cores.Cores.Count == 0; waited++)
        {
            await Task.Delay(25);
        }

        await rig.Cores.Manager.CancelProbesAsync();
        var result = await measuring.WaitAsync(Soon);
        rig.Cores.Manager.EndSessionStart();

        await result.Should().BeEqualTo(AetherTestResult.Untested(ResUI.SpeedtestingSkip));
        await rig.Probed.Should().BeEmpty();
        await rig.Cores.Events.TakeLast(2).Should().BeEquivalentTo(["kill 1", "xray stop"], CollectionOrdering.Matching);
    }

    [Test]
    public async Task AConfigurationThatNeedsNoCoreIsMeasuredThroughItsXrayAlone()
    {
        var rig = new AetherMeasureRig(_root);

        var result = await rig.Tester().MeasureAsync(Aether(AetherProtocol.Masque), rig.Probe(7));

        await result.Should().BeEqualTo(new AetherTestResult(7));
        await rig.Cores.Events.Should().BeEquivalentTo(["generate 41501 41502", "xray start", "probe 41501", "xray stop"], CollectionOrdering.Matching);
    }

    [Test]
    public async Task ATestPicksNoPortOfTheAetherCore()
    {
        // The system hands out the listen port and the three after it first; the test takes none of them.
        var ports = new Queue<int>([P, P + 1, P + 2, P + 3, 41700, 41701]);
        var cores = new FakeAetherCores(_root);
        var generated = new List<string>();
        var tester = new AetherDelayTester(new AetherTestHost
        {
            Cores = cores.Manager,
            Build = profile => Task.FromResult(new CoreConfigContextBuilderResult(
                CoreConfigTestFactory.CreateContext(CoreConfigTestFactory.CreateConfig(), profile, ECoreType.Xray), NodeValidatorResult.Empty())),
            Generate = (context, port) =>
            {
                generated.Add($"{port} {context.Aether.SecondaryPort}");
                return new RetResult(false, "stop here");
            },
            StartXray = _ => Task.FromResult<IAsyncDisposable?>(null),
            PickPort = () => ports.Dequeue(),
        });

        await (await tester.MeasureAsync(Aether(AetherProtocol.Masque), (_, _, _) => Task.FromResult(1))).Should().BeEqualTo(AetherTestResult.Failed);
        await generated.Should().BeEquivalentTo(["41700 41701"], CollectionOrdering.Matching);
    }

    [Test]
    public async Task ASessionThatStartsWhileTheTestsXrayComesUpLeavesTheTestUntested()
    {
        // The test chose a tunnel of its own while no session ran, and a session starts while the test's Xray comes up:
        // the test's core must not start beside the session's, on its port, nor the session's core be measured for this
        // profile (PattNG cancels the tests of every start).
        var warp = Aether(AetherProtocol.Masque);
        var wireGuard = Aether(AetherProtocol.WireGuard, "w");
        var rig = new AetherMeasureRig(_root) { Needed = AetherCore.Of(warp, P) };
        rig.Answer(P);
        var xrayStarting = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var xrayMayStart = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        rig.XrayStarting = async () =>
        {
            xrayStarting.TrySetResult();
            await xrayMayStart.Task;
        };
        var measuring = rig.Tester(TimeSpan.FromSeconds(10)).MeasureAsync(warp, rig.Probe());
        await xrayStarting.Task.WaitAsync(Soon);

        await rig.Cores.Manager.CancelProbesAsync();
        await (await rig.Cores.Manager.StartSessionAsync(AetherCore.Of(wireGuard, P).Through(P + AetherPorts.SecondaryOffset))).Should().BeNull();
        rig.Cores.Manager.EndSessionStart();
        xrayMayStart.SetResult();
        var result = await measuring.WaitAsync(Soon);

        await result.Should().BeEqualTo(AetherTestResult.Untested(ResUI.SpeedtestingSkip));
        await rig.Probed.Should().BeEmpty();
        // The one core is the session's, and the test's Xray is stopped.
        await rig.Cores.Events.Count(e => e.StartsWith("spawn")).Should().BeEqualTo(1);
        await rig.Cores.Events.Last().Should().BeEqualTo("xray stop");
        await rig.Cores.Manager.Session!.Protocol.Should().BeEqualTo(AetherProtocol.WireGuard);
        await rig.Cores.Manager.StopSessionAsync();
    }

    [Test]
    public async Task TheTestsOfRunsThatOverlapTakeTurnsRatherThanBeLeftUntested()
    {
        // PattNG tunnels: a second run of tests, started while the first measures a profile on a tunnel of its own, waits
        // for it; only a scan or a key run leaves a test untested (critique 9).
        var first = Aether(AetherProtocol.Masque);
        var second = Aether(AetherProtocol.WireGuard, "b");
        var rig = new AetherMeasureRig(_root) { Needed = AetherCore.Of(first, P) };
        rig.Answer(P);
        var tester = rig.Tester(TimeSpan.FromSeconds(10));
        var probing = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var firstTest = tester.MeasureAsync(first, async (_, _, _) =>
        {
            rig.Cores.Log("probe first");
            probing.TrySetResult();
            await release.Task;
            return 42;
        });
        await probing.Task.WaitAsync(Soon);

        var secondTest = tester.MeasureAsync(second, (_, _, _) =>
        {
            rig.Cores.Log("probe second");
            return Task.FromResult(7);
        });
        await Task.Delay(300);
        await secondTest.IsCompleted.Should().BeFalse();
        release.SetResult();

        await (await firstTest.WaitAsync(Soon)).Should().BeEqualTo(new AetherTestResult(42));
        await (await secondTest.WaitAsync(Soon)).Should().BeEqualTo(new AetherTestResult(7));
        // One tunnel at a time: the first test's core and Xray are gone before the second test's come up.
        await rig.Cores.Events.Should().BeEquivalentTo(
            [
                "generate 41501 41502", "xray start", "spawn 1", "probe first", "generate 41503 41504", "kill 1", "xray stop",
                "xray start", "spawn 2", "probe second", "kill 2", "xray stop",
            ],
            CollectionOrdering.Matching);
    }

    [Test]
    public async Task ACancellationInsideTheProbeThatIsNotTheTestsIsNoAnswer()
    {
        // A probe that lets out a timeout of its own, as a download that reaches its time does: no session started, and
        // nothing cancelled the test, so it is a profile that did not answer, on every route.
        var warp = Aether(AetherProtocol.Masque);
        AetherTestProbe timesOut = (_, _, _) => throw new TaskCanceledException("the probe's own timeout");

        var ownTunnel = new AetherMeasureRig(_root) { Needed = AetherCore.Of(warp, P) };
        ownTunnel.Answer(P);
        await (await ownTunnel.Tester().MeasureAsync(warp, timesOut)).Should().BeEqualTo(AetherTestResult.Failed);
        await ownTunnel.Cores.Events.TakeLast(2).Should().BeEquivalentTo(["kill 1", "xray stop"], CollectionOrdering.Matching);

        var noCore = new AetherMeasureRig(_root);
        await (await noCore.Tester().MeasureAsync(warp, timesOut)).Should().BeEqualTo(AetherTestResult.Failed);

        var throughSession = new AetherMeasureRig(_root) { Needed = AetherCore.Of(warp, P) };
        await throughSession.Cores.Manager.StartSessionAsync(throughSession.Needed!.Through(P + AetherPorts.SecondaryOffset));
        throughSession.Answer(P);
        await (await throughSession.Tester().MeasureAsync(warp, timesOut)).Should().BeEqualTo(AetherTestResult.Failed);
        await throughSession.Cores.Manager.StopSessionAsync();

        // A cancellation of the test itself is thrown, as before.
        using var cancel = new CancellationTokenSource();
        cancel.Cancel();
        var thrown = false;
        try
        {
            await noCore.Tester().MeasureAsync(warp, timesOut, cancel.Token);
        }
        catch (OperationCanceledException)
        {
            thrown = true;
        }
        await thrown.Should().BeTrue();
    }

    [Test]
    public async Task ATestWhileTheCoresAreHeldIsLeftUntestedWithNothingStarted()
    {
        // An update of aether-core holds the cores: a passing state of the app, not a failure of the profile, which
        // "Remove invalid" would take for one.
        var warp = Aether(AetherProtocol.Masque);
        var rig = new AetherMeasureRig(_root) { Needed = AetherCore.Of(warp, P) };
        rig.Answer(P);
        var hold = await rig.Cores.Manager.HoldAsync();

        var result = await rig.Tester().MeasureAsync(warp, rig.Probe());
        await hold.DisposeAsync();

        await result.Should().BeEqualTo(AetherTestResult.Untested(ResUI.SpeedtestingSkip));
        // No Xray started for it.
        await rig.Cores.Events.Should().BeEquivalentTo(["generate 41501 41502"], CollectionOrdering.Matching);
    }

    [Test]
    public async Task AHoldEndsARunningTestAndItsXrayBeforeItIsThere()
    {
        // The files of Xray may be replaced under the hold as well: the test's own Xray is gone by the time it is there,
        // though it takes its time to end.
        var warp = Aether(AetherProtocol.Masque);
        var rig = new AetherMeasureRig(_root) { Needed = AetherCore.Of(warp, P), XrayStopping = () => Task.Delay(200) };
        var measuring = rig.Tester(TimeSpan.FromSeconds(10)).MeasureAsync(warp, rig.Probe());
        for (var waited = 0; waited < 200 && rig.Cores.Cores.Count == 0; waited++)
        {
            await Task.Delay(25);
        }

        var hold = await rig.Cores.Manager.HoldAsync().WaitAsync(Soon);
        var whenHeld = rig.Cores.Events;
        var result = await measuring.WaitAsync(Soon);
        await hold.DisposeAsync();

        await whenHeld.Should().BeEquivalentTo(["generate 41501 41502", "xray start", "spawn 1", "kill 1", "xray stop"], CollectionOrdering.Matching);
        await result.Should().BeEqualTo(AetherTestResult.Untested(ResUI.SpeedtestingSkip));
        await rig.Probed.Should().BeEmpty();
    }

    [Test]
    public async Task WithoutTheCoreATestIsLeftUntestedWithNothingStarted()
    {
        // D5: the core not downloaded yet, or taken away by an update of aether-core, is a state of the app; -1 would have
        // "Remove invalid" delete a profile that works.
        const string missing = "The Core file (file name: aether) was not found";
        var warp = Aether(AetherProtocol.Masque);
        var rig = new AetherMeasureRig(_root) { Needed = AetherCore.Of(warp, P), MissingCore = () => missing };
        rig.Answer(P);

        var result = await rig.Tester().MeasureAsync(warp, rig.Probe());

        await result.Should().BeEqualTo(AetherTestResult.Untested(missing));
        await result.Delay.Should().BeEqualTo(AetherDelayTester.Untested);
        // No test Xray, and no core, started for it.
        await rig.Cores.Events.Should().BeEquivalentTo(["generate 41501 41502"], CollectionOrdering.Matching);
    }

    [Test]
    public async Task ATestTheCoresAreHeldForWhileTheCoreIsTakenAwayIsToldAsAMissingCore()
    {
        // The core's program was there when the test looked; an update holds the cores and replaces it meanwhile.
        const string missing = "The Core file (file name: aether) was not found";
        var warp = Aether(AetherProtocol.Masque);
        var looked = 0;
        var rig = new AetherMeasureRig(_root)
        {
            Needed = AetherCore.Of(warp, P),
            MissingCore = () => Interlocked.Increment(ref looked) == 1 ? null : missing,
        };
        var hold = await rig.Cores.Manager.HoldAsync();

        var result = await rig.Tester().MeasureAsync(warp, rig.Probe());
        await hold.DisposeAsync();

        await result.Should().BeEqualTo(AetherTestResult.Untested(missing));
        await rig.Cores.Events.Should().BeEquivalentTo(["generate 41501 41502"], CollectionOrdering.Matching);
    }

    [Test]
    public async Task AMissingCoreLeavesTheTestThroughTheLiveSessionAlone()
    {
        // A test through the live session runs no core of its own: it is measured as before.
        var warp = Aether(AetherProtocol.Masque);
        var rig = new AetherMeasureRig(_root) { Needed = AetherCore.Of(warp, P), MissingCore = () => "never shown" };
        await (await rig.Cores.Manager.StartSessionAsync(rig.Needed!.Through(P + AetherPorts.SecondaryOffset))).Should().BeNull();
        rig.Answer(P);

        var result = await rig.Tester().MeasureAsync(warp, rig.Probe());

        await result.Should().BeEqualTo(new AetherTestResult(42));
        await rig.Cores.Manager.StopSessionAsync();
    }

    // ---- the uplink of the test's Xray under a TUN (D2)

    /// <summary>A binding while a TUN runs that writes down each look for the uplink, and each line it says, in <paramref name="seen" />.</summary>
    private static AetherUplinkBinding Uplink(List<string> seen, string? uplink = "Wi-Fi 2")
    {
        return new AetherUplinkBinding
        {
            TunRunning = () => true,
            FindUplink = () =>
            {
                lock (seen)
                {
                    seen.Add("find uplink");
                }
                return uplink;
            },
            Log = line =>
            {
                lock (seen)
                {
                    seen.Add(line);
                }
            },
        };
    }

    [Test]
    public async Task ATestLeftUntestedLooksForNoUplink()
    {
        // D2-4: no test Xray starts beside a session of another core, while the session connects, or without the core:
        // the uplink is not looked for, and no line says that a test Xray runs unbound.
        var warp = Aether(AetherProtocol.Masque);
        var seen = new List<string>();

        var beside = new AetherMeasureRig(_root) { Needed = AetherCore.Of(warp, P), Uplink = Uplink(seen, uplink: null) };
        await beside.Cores.Manager.StartSessionAsync(AetherCore.Of(Aether(AetherProtocol.WireGuard, "w"), P).Through(P + AetherPorts.SecondaryOffset));
        beside.Answer(P);
        await (await beside.Tester().MeasureAsync(warp, beside.Probe())).Should().BeEqualTo(AetherTestResult.Untested(ResUI.MsgAetherTestSkipped));
        await beside.Cores.Manager.StopSessionAsync();

        var connecting = new AetherMeasureRig(_root) { Needed = AetherCore.Of(warp, P), Uplink = Uplink(seen, uplink: null) };
        await connecting.Cores.Manager.StartSessionAsync(connecting.Needed!.Through(P + AetherPorts.SecondaryOffset));
        await (await connecting.Tester().MeasureAsync(warp, connecting.Probe())).Should().BeEqualTo(AetherTestResult.Untested(ResUI.MsgAetherConnecting));
        await connecting.Cores.Manager.StopSessionAsync();

        const string missing = "The Core file (file name: aether) was not found";
        var noCore = new AetherMeasureRig(_root) { Needed = AetherCore.Of(warp, P), MissingCore = () => missing, Uplink = Uplink(seen, uplink: null) };
        noCore.Answer(P);
        await (await noCore.Tester().MeasureAsync(warp, noCore.Probe())).Should().BeEqualTo(AetherTestResult.Untested(missing));

        await seen.Should().BeEmpty();
    }

    [Test]
    public async Task EveryTestXrayStartsOnItsConfigurationBoundToTheUplink()
    {
        // Each way a test Xray starts: through a core of its own, generated again once bound; through the live session,
        // whose generation without a core is bound; and with no core needed at all. The uplink is looked for once a test.
        var warp = Aether(AetherProtocol.Masque);
        var seen = new List<string>();
        const string bound = """{"uplink": "Wi-Fi 2"}""";

        var own = new AetherMeasureRig(_root) { Needed = AetherCore.Of(warp, P), Uplink = Uplink(seen) };
        own.Answer(P);
        await (await own.Tester().MeasureAsync(warp, own.Probe())).Should().BeEqualTo(new AetherTestResult(42));
        await own.Cores.Events.Should().BeEquivalentTo(
            ["generate 41501 41502", "generate 41501 41502", "xray start", "spawn 1", "probe 41501", "kill 1", "xray stop"], CollectionOrdering.Matching);
        await own.XrayStartedOn.Should().BeEquivalentTo([bound]);

        var live = new AetherMeasureRig(_root) { Needed = AetherCore.Of(warp, P), Uplink = Uplink(seen) };
        await (await live.Cores.Manager.StartSessionAsync(live.Needed!.Through(P + AetherPorts.SecondaryOffset))).Should().BeNull();
        live.Answer(P);
        await (await live.Tester().MeasureAsync(warp, live.Probe())).Should().BeEqualTo(new AetherTestResult(42));
        await live.XrayStartedOn.Should().BeEquivalentTo([bound]);
        await live.Cores.Manager.StopSessionAsync();

        var plain = new AetherMeasureRig(_root) { Uplink = Uplink(seen) };
        await (await plain.Tester().MeasureAsync(warp, plain.Probe())).Should().BeEqualTo(new AetherTestResult(42));
        await plain.XrayStartedOn.Should().BeEquivalentTo([bound]);

        await seen.Should().BeEquivalentTo(["find uplink", "find uplink", "find uplink"]);
    }

    // ---- stubs

    private static int ClosedPort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }

    /// <summary>Answers the SOCKS5 greeting and nothing more, as a served listener does.</summary>
    private sealed class SocksGreeter : IDisposable
    {
        private readonly TcpListener _server = new(IPAddress.Loopback, 0);

        public SocksGreeter()
        {
            _server.Start();
            _ = Task.Run(async () =>
            {
                while (true)
                {
                    TcpClient client;
                    try
                    {
                        client = await _server.AcceptTcpClientAsync();
                    }
                    catch (Exception ex) when (ex is SocketException or ObjectDisposedException)
                    {
                        return;
                    }
                    _ = Task.Run(async () =>
                    {
                        using (client)
                        {
                            try
                            {
                                var stream = client.GetStream();
                                var head = new byte[2];
                                await stream.ReadExactlyAsync(head);
                                await stream.ReadExactlyAsync(new byte[head[1]]);
                                await stream.WriteAsync(new byte[] { 5, 0 });
                            }
                            catch (Exception ex) when (ex is IOException or SocketException or EndOfStreamException)
                            {
                            }
                        }
                    });
                }
            });
        }

        public int Port => ((IPEndPoint)_server.LocalEndpoint).Port;

        public void Dispose()
        {
            _server.Stop();
        }
    }
}
