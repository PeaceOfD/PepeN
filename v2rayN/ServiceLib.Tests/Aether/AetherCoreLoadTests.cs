using TUnit.Assertions.Enums;

namespace ServiceLib.Tests.Aether;

/// <summary>
/// PattN (design 2.6.4, critiques 15 and 31; PattNG CoreServiceManager.launchCore): what CoreManager.LoadCore does for the
/// Aether core, once the configuration is generated, once the old cores stopped (D3: the ports are free, which the tests
/// hold with listeners of their own) and once Xray started, the turns its loads and stops take with the stop that follows
/// an Aether core exiting on its own, and the waits for a port (D6). Every other step is a fake that writes down that it
/// ran.
/// </summary>
public class AetherCoreLoadTests
{
    private static readonly AetherCore Warp = new AetherCore(["--masque", "--bind", "127.0.0.1:10819"]).Through(10822);

    /// <summary>Steps that write down what they do; <paramref name="core" /> is the Aether core the configuration needs.</summary>
    private sealed class Steps(AetherCore? core)
    {
        public List<string> Events { get; } = [];
        public string? Missing { get; init; }
        public bool XrayUp { get; set; } = true;
        public bool PortListens { get; init; } = true;

        /// <summary>Xray as it is once the wait for its inbound is over: it may have ended meanwhile.</summary>
        public bool? XrayUpAfterTheWait { get; init; }

        public string? SessionFailure { get; init; }
        public long Generation { get; private set; } = 41;

        /// <summary>The port of Xray's secondary-socks inbound for the core.</summary>
        public int? Secondary { get; init; } = 10822;

        /// <summary>How long a taken port is given to be released.</summary>
        public TimeSpan PortRelease { get; init; } = TimeSpan.FromMilliseconds(300);

        /// <summary>Whether a port can be listened on: PattN's own probe unless a test says otherwise.</summary>
        public Func<int, bool, bool> CanListen { get; init; } = AetherCoreLoad.CanListen;

        /// <summary>What killing the cores an earlier PattN left behind does: nothing unless a test says otherwise.</summary>
        public Action? Reap { get; init; }

        public AetherCoreLoadSteps Build()
        {
            return new AetherCoreLoadSteps
            {
                Core = () => core,
                MissingCore = () => Missing,
                NewGeneration = () =>
                {
                    Events.Add("new generation");
                    return ++Generation;
                },
                CancelProbes = () => Log("cancel probes"),
                ReapLeftovers = () => Reap?.Invoke(),
                SecondaryPort = () => Secondary,
                CanListen = CanListen,
                PortRelease = PortRelease,
                XrayUp = () => XrayUp,
                WaitForPort = port =>
                {
                    Events.Add($"wait for {port}");
                    if (XrayUpAfterTheWait is { } after)
                    {
                        XrayUp = after;
                    }
                    return Task.FromResult(PortListens);
                },
                StartSession = (started, generation) =>
                {
                    Events.Add($"start aether {generation}");
                    return Task.FromResult(SessionFailure);
                },
                StopCores = () => Log("stop cores"),
                Report = (notify, message) => Log($"report {notify} {message}"),
            };
        }

        private Task Log(string text)
        {
            Events.Add(text);
            return Task.CompletedTask;
        }
    }

    // ---- before the old cores stop

    [Test]
    public async Task ALoadThatNeedsAnAetherCoreCancelsALatencyTestBeforeTheOldCoresStop()
    {
        var steps = new Steps(Warp);

        // A latency test's core may hold the listen port; the cores the load starts are a new generation.
        await (await AetherCoreLoad.BeforeStopAsync(steps.Build())).Should().BeEqualTo(42);
        await steps.Events.Should().BeEquivalentTo(["new generation", "cancel probes"], CollectionOrdering.Matching);
    }

    [Test]
    public async Task AnAetherCoreThatCannotRunKeepsTheCoresThatRun()
    {
        var steps = new Steps(Warp) { Missing = "The Core file (file name: aether) was not found" };

        await (await AetherCoreLoad.BeforeStopAsync(steps.Build())).Should().BeNull();

        // No new generation: the end of the old session still stops the old Xray.
        await steps.Events.Should().BeEquivalentTo(["report True The Core file (file name: aether) was not found"], CollectionOrdering.Matching);
    }

    [Test]
    public async Task AConfigurationWithoutAnAetherProfileStartsAsBefore()
    {
        var steps = new Steps(null) { Missing = "never asked" };

        await (await AetherCoreLoad.BeforeStopAsync(steps.Build())).Should().BeEqualTo(42);
        await (await AetherCoreLoad.AfterXrayAsync(steps.Build(), 42)).Should().BeEqualTo(AetherCoreLoadOutcome.NoCore);

        // No probe of a latency test is cancelled, and nothing waits for an inbound of the core.
        await steps.Events.Should().BeEquivalentTo(["new generation"], CollectionOrdering.Matching);
    }

    // ---- once the old cores stopped (D3): the ports are held by listeners of the test's own

    /// <summary>
    /// A loopback port P with P..P+3 free now, by TCP and UDP. It is drawn from 20000..31999, outside the ranges systems
    /// give ports out of by default (Windows and macOS 49152 on, Linux 32768 to 60999), so that a port another test is
    /// given meanwhile, which is the next one up on Windows, does not land among them.
    /// </summary>
    private static int FreePorts()
    {
        for (var attempt = 0; attempt < 64; attempt++)
        {
            var port = Random.Shared.Next(20000, 32000);
            if (Enumerable.Range(port, AetherPorts.SecondaryOffset + 1).All(each => AetherCoreLoad.CanListen(each, udp: true)))
            {
                return port;
            }
        }
        throw new InvalidOperationException("no four loopback ports in a row are free");
    }

    /// <summary>
    /// The core of a session on <paramref name="p" />, dialling out through P+3; with <paramref name="inside" /> ("tor" or
    /// "psiphon") inside the tunnel, which takes P, and the core's own listener on P+1.
    /// </summary>
    private static AetherCore SessionCore(int p, string? inside = null)
    {
        string[] arguments = inside == null
            ? ["--masque", "--bind", $"127.0.0.1:{p}"]
            : ["--masque", $"--{inside}", $"--{inside}-bind", $"127.0.0.1:{p}", "--bind", $"127.0.0.1:{p + 1}"];
        return new AetherCore(arguments).Through(p + AetherPorts.SecondaryOffset);
    }

    /// <summary>The Aether steps of a load, in the order CoreManager.LoadCoreInTurn takes them among its own.</summary>
    private static async Task Load(Steps steps)
    {
        var built = steps.Build();
        if (await AetherCoreLoad.BeforeStopAsync(built) is not { } generation)
        {
            return;
        }
        steps.Events.Add("stop the old cores");
        if (!await AetherCoreLoad.BeforeXrayAsync(built))
        {
            return;
        }
        steps.Events.Add("start xray");
        await AetherCoreLoad.AfterXrayAsync(built, generation);
    }

    [Test]
    public async Task TheCoresStartWhenTheirPortsAreFree()
    {
        var p = FreePorts();
        var steps = new Steps(SessionCore(p, "tor")) { Secondary = p + 3 };

        await Load(steps);

        await steps.Events.Should().BeEquivalentTo(
            ["new generation", "cancel probes", "stop the old cores", "start xray", $"wait for {p + 3}", "start aether 42"],
            CollectionOrdering.Matching);
    }

    [Test]
    public async Task AListenPortOfTheCoreThatIsTakenLeavesTheCoresStopped()
    {
        // P, the core's own listener, or Tor's or Psiphon's inside the tunnel; P+1, the core's own beside those.
        foreach (var (inside, offset) in new (string?, int)[] { (null, 0), ("tor", 0), ("tor", 1), ("psiphon", 1) })
        {
            var p = FreePorts();
            using var holder = new TcpHolder(p + offset);
            var steps = new Steps(SessionCore(p, inside)) { Secondary = p + 3 };

            await Load(steps);

            // Neither Xray nor the core starts once the old cores stopped; the user is told which port is taken.
            await steps.Events.Should().BeEquivalentTo(
                ["new generation", "cancel probes", "stop the old cores", $"report True {string.Format(ResUI.MsgAetherListenPortInUse, p + offset)}"],
                CollectionOrdering.Matching).Because($"P+{offset} taken, inside: {inside ?? "nothing"}");
        }
    }

    [Test]
    public async Task ACoreAnEarlierPattNLeftOnTheListenPortIsKilledBeforeThePortsAreChecked()
    {
        // Nothing else would free the port: the load gives a taken port no time to be released here.
        var p = FreePorts();
        using var leftover = new TcpHolder(p);
        var steps = new Steps(SessionCore(p)) { Secondary = p + 3, PortRelease = TimeSpan.Zero, Reap = leftover.Dispose };

        await Load(steps);

        await steps.Events.Should().BeEquivalentTo(
            ["new generation", "cancel probes", "stop the old cores", "start xray", $"wait for {p + 3}", "start aether 42"],
            CollectionOrdering.Matching);
    }

    [Test]
    public async Task AnHttpProxyOnNoPortIsRefusedAsAListenerOnNoPortIs()
    {
        // D3-1 holds for the HTTP proxies of a command as well; the core would leave such a proxy out without a word.
        var core = AetherCore.OfCommand("aether --masque --bind 127.0.0.1:10819 --http-proxy 127.0.0.1:70000", AetherPorts.DefaultListenPort)!;

        await AetherCoreLoad.PortsProblem(core, null, (_, _) => true).Should().BeEqualTo(string.Format(ResUI.MsgAetherListenPortInvalid, 70000));
    }

    [Test]
    public async Task TheListenerTheAppDialsOnPortZeroIsRefused()
    {
        // 0 has the core listen on a port the app does not know; another listener may take it.
        var bind = AetherCore.OfCommand("aether --masque --bind 127.0.0.1:0", AetherPorts.DefaultListenPort)!;
        await AetherCoreLoad.PortsProblem(bind, null, (_, _) => true).Should().BeEqualTo(string.Format(ResUI.MsgAetherListenPortInvalid, 0));
        var tor = AetherCore.OfCommand("aether --masque --tor --tor-bind 127.0.0.1:0 --bind 127.0.0.1:10820", AetherPorts.DefaultListenPort)!;
        await AetherCoreLoad.PortsProblem(tor, null, (_, _) => true).Should().BeEqualTo(string.Format(ResUI.MsgAetherListenPortInvalid, 0));
        var around = AetherCore.OfCommand("aether --masque --psiphon-reverse --psiphon-bind 127.0.0.1:0 --bind 127.0.0.1:10819", AetherPorts.DefaultListenPort)!;
        await AetherCoreLoad.PortsProblem(around, null, (_, _) => true).Should().BeNull();
    }

    [Test]
    public async Task TwoListenersOnOnePortAreRefusedWithoutAWait()
    {
        // A command stored before the editor refused it: the core would bind the port once and fail on the second listener.
        var core = AetherCore.OfCommand("aether --masque --bind 127.0.0.1:10819 --http-proxy 127.0.0.1:10819", AetherPorts.DefaultListenPort)!;
        var refusal = string.Format(ResUI.MsgAetherListenPortShared, 10819);
        await AetherCoreLoad.PortsProblem(core, null, (_, _) => true).Should().BeEqualTo(refusal);
        var steps = new Steps(core) { Secondary = null, PortRelease = TimeSpan.FromSeconds(5), CanListen = (_, _) => true };

        var waited = Stopwatch.StartNew();
        await Load(steps);

        await (waited.Elapsed < TimeSpan.FromSeconds(2)).Should().BeTrue();
        await steps.Events.Should().BeEquivalentTo(
            ["new generation", "cancel probes", "stop the old cores", $"report True {refusal}"],
            CollectionOrdering.Matching);
        // The port of a listener the command leaves out is the core's default, which the message names.
        var onDefault = AetherCore.OfCommand("aether --wg --tor --tor-bind 127.0.0.1:10819 --tor-http 127.0.0.1:1819", AetherPorts.DefaultListenPort)!;
        await AetherCoreLoad.PortsProblem(onDefault, null, (_, _) => true).Should().BeEqualTo(string.Format(ResUI.MsgAetherListenPortShared, 1819));
    }

    [Test]
    public async Task AListenerTheAppDialsWhereItDoesNotReachItIsRefusedWithoutAWait()
    {
        // The core would listen on ::1, where the app does not dial it: the session would stay connecting.
        var core = AetherCore.OfCommand("aether --masque --bind [::1]:20808", AetherPorts.DefaultListenPort)!;
        var refusal = string.Format(ResUI.MsgAetherListenAddressUnreachable, "--bind");
        await AetherCoreLoad.PortsProblem(core, null, (_, _) => true).Should().BeEqualTo(refusal);
        // Before the port two listeners share.
        var shared = AetherCore.OfCommand("aether --masque --bind [::1]:20808 --http-proxy 127.0.0.1:20808", AetherPorts.DefaultListenPort)!;
        await AetherCoreLoad.PortsProblem(shared, null, (_, _) => true).Should().BeEqualTo(refusal);
        var steps = new Steps(core) { Secondary = null, PortRelease = TimeSpan.FromSeconds(5), CanListen = (_, _) => false };

        var waited = Stopwatch.StartNew();
        await Load(steps);

        await (waited.Elapsed < TimeSpan.FromSeconds(2)).Should().BeTrue();
        await steps.Events.Should().BeEquivalentTo(
            ["new generation", "cancel probes", "stop the old cores", $"report True {refusal}"],
            CollectionOrdering.Matching);
        // 0.0.0.0 answers on 127.0.0.1 as well.
        var everywhere = AetherCore.OfCommand("aether --masque --bind 0.0.0.0:20808", AetherPorts.DefaultListenPort)!;
        await AetherCoreLoad.PortsProblem(everywhere, null, (_, _) => true).Should().BeNull();
    }

    [Test]
    public async Task AnAddressTheCoreDoesNotTakeIsRefusedWithoutAWait()
    {
        // A command stored before the editor refused it: the core would quietly listen on 1819, where the app dials 20808.
        var core = AetherCore.OfCommand("aether --masque --bind localhost:20808", AetherPorts.DefaultListenPort)!;
        // Named by its option, which the log may carry.
        var refusal = string.Format(ResUI.MsgAetherListenAddressInvalid, "--bind");
        await AetherCoreLoad.PortsProblem(core, null, (_, _) => true).Should().BeEqualTo(refusal);
        // Before the port two listeners share.
        var shared = AetherCore.OfCommand("aether --masque --bind localhost:20808 --http-proxy 127.0.0.1:20808", AetherPorts.DefaultListenPort)!;
        await AetherCoreLoad.PortsProblem(shared, null, (_, _) => true).Should().BeEqualTo(refusal);
        // A port the old cores held would be looked at again for a while; this one is not.
        var steps = new Steps(core) { Secondary = null, PortRelease = TimeSpan.FromSeconds(5), CanListen = (_, _) => false };

        var waited = Stopwatch.StartNew();
        await Load(steps);

        await (waited.Elapsed < TimeSpan.FromSeconds(2)).Should().BeTrue();
        await steps.Events.Should().BeEquivalentTo(
            ["new generation", "cancel probes", "stop the old cores", $"report True {refusal}"],
            CollectionOrdering.Matching);
        // A number that is no port comes first.
        var noPort = AetherCore.OfCommand("aether --masque --bind localhost:70000", AetherPorts.DefaultListenPort)!;
        await AetherCoreLoad.PortsProblem(noPort, null, (_, _) => true).Should().BeEqualTo(string.Format(ResUI.MsgAetherListenPortInvalid, 70000));
    }

    [Test]
    public async Task AnHttpProxyOfTheCoreThatIsTakenLeavesTheCoresStopped()
    {
        // A command written by hand may open an HTTP proxy beside the core's own listener; its port is the core's as well.
        var p = FreePorts();
        using var holder = new TcpHolder(p + 1);
        var core = new AetherCore(["--masque", "--bind", $"127.0.0.1:{p}", "--http-proxy", $"127.0.0.1:{p + 1}"]).Through(p + 3);
        var steps = new Steps(core) { Secondary = p + 3 };

        await Load(steps);

        await steps.Events.Should().BeEquivalentTo(
            ["new generation", "cancel probes", "stop the old cores", $"report True {string.Format(ResUI.MsgAetherListenPortInUse, p + 1)}"],
            CollectionOrdering.Matching);
    }

    [Test]
    public async Task XraysInboundForTheCoreTakenByTcpOrUdpLeavesTheCoresStopped()
    {
        // The secondary-socks inbound serves UDP as well, on the same port.
        foreach (var udp in new[] { false, true })
        {
            var p = FreePorts();
            using IDisposable holder = udp
                ? new UdpClient(new IPEndPoint(IPAddress.Loopback, p + 3))
                : new TcpHolder(p + 3);
            var steps = new Steps(SessionCore(p)) { Secondary = p + 3 };

            await Load(steps);

            await steps.Events.Should().BeEquivalentTo(
                ["new generation", "cancel probes", "stop the old cores", $"report True {string.Format(ResUI.MsgAetherSecondaryPortInUse, p + 3)}"],
                CollectionOrdering.Matching).Because($"UDP: {udp}");
        }
    }

    [Test]
    public async Task APortTheOldCoresStillHoldIsGivenAMomentToBeReleased()
    {
        // The old Xray is killed without a wait for its end, and may hold its inbound a moment longer.
        var p = FreePorts();
        var holder = new TcpHolder(p + 3);
        var steps = new Steps(SessionCore(p)) { Secondary = p + 3, PortRelease = TimeSpan.FromSeconds(5) };
        _ = Task.Delay(300).ContinueWith(_ => holder.Dispose(), TaskScheduler.Default);

        await (await AetherCoreLoad.BeforeXrayAsync(steps.Build())).Should().BeTrue();

        await steps.Events.Should().BeEmpty();
    }

    [Test]
    public async Task AConfigurationWithoutAnAetherCoreChecksNoPort()
    {
        var asked = 0;
        var steps = new Steps(null)
        {
            CanListen = (_, _) =>
            {
                asked++;
                return false;
            },
        };

        await (await AetherCoreLoad.BeforeXrayAsync(steps.Build())).Should().BeTrue();

        await asked.Should().BeEqualTo(0);
        await steps.Events.Should().BeEmpty();
    }

    [Test]
    public async Task ACoreWithAnUpstreamOfItsOwnHasNoInboundOfXraysToCheck()
    {
        // A port another program listens on, as the proxy a command names may: the configuration gave no secondary-socks
        // inbound, and only the core's own listener counts.
        var p = FreePorts();
        using var proxy = new TcpHolder(p + 3);
        var own = new AetherCore(["--masque", "--bind", $"127.0.0.1:{p}", "--upstream", $"socks5://127.0.0.1:{p + 3}"]);

        await AetherCoreLoad.PortsProblem(own, null, AetherCoreLoad.CanListen).Should().BeNull();
        await AetherCoreLoad.PortsProblem(own, p + 3, AetherCoreLoad.CanListen).Should().BeEqualTo(string.Format(ResUI.MsgAetherSecondaryPortInUse, p + 3));
    }

    [Test]
    public async Task ACommandThatNamesNoPortIsRefusedWithoutAnException()
    {
        // D3-1: a command written by hand may name a listener port outside 1..65535, which the editor stores and the
        // generation passes on; the check of the ports refuses it, after no wait, rather than throw once the old cores
        // stopped.
        foreach (var port in new[] { 70000, -1, 65536 })
        {
            var core = AetherCore.OfCommand($"aether --masque --bind 127.0.0.1:{port}", AetherPorts.DefaultListenPort)!;
            await core.Ports.Should().Contain(port);
            await AetherCoreLoad.CanListen(port, udp: false).Should().BeFalse();
            await AetherCoreLoad.CanListen(port, udp: true).Should().BeFalse();
            var refusal = string.Format(ResUI.MsgAetherListenPortInvalid, port);
            await AetherCoreLoad.PortsProblem(core, null, AetherCoreLoad.CanListen).Should().BeEqualTo(refusal);
            var steps = new Steps(core) { Secondary = null, PortRelease = TimeSpan.FromSeconds(5) };

            var waited = Stopwatch.StartNew();
            await Load(steps);

            await (waited.Elapsed < TimeSpan.FromSeconds(2)).Should().BeTrue().Because($"port {port}");
            await steps.Events.Should().BeEquivalentTo(
                ["new generation", "cancel probes", "stop the old cores", $"report True {refusal}"],
                CollectionOrdering.Matching).Because($"port {port}");
        }
    }

    /// <summary>Holds a loopback port by TCP, as another program's listener does, until disposed.</summary>
    private sealed class TcpHolder : IDisposable
    {
        private readonly TcpListener _listener;

        public TcpHolder(int port)
        {
            _listener = new TcpListener(IPAddress.Loopback, port);
            _listener.Start();
        }

        public void Dispose()
        {
            _listener.Stop();
        }
    }

    // ---- once Xray started

    [Test]
    public async Task TheAetherCoreStartsOnceXraysInboundListens()
    {
        var steps = new Steps(Warp);

        await (await AetherCoreLoad.AfterXrayAsync(steps.Build(), 42)).Should().BeEqualTo(AetherCoreLoadOutcome.Started);

        // Xray's secondary-socks inbound listens before the core is started on it, as a session of the load's generation.
        await steps.Events.Should().BeEquivalentTo(["wait for 10822", "start aether 42"], CollectionOrdering.Matching);
    }

    [Test]
    public async Task TheAetherCoreIsNotStartedWhenXrayIsNotUp()
    {
        // Critique 31: Xray reported why it did not start; the core would only dial a port nobody listens on.
        var steps = new Steps(Warp) { XrayUp = false };

        await (await AetherCoreLoad.AfterXrayAsync(steps.Build(), 42)).Should().BeEqualTo(AetherCoreLoadOutcome.XrayDown);

        await steps.Events.Should().BeEmpty();
    }

    [Test]
    public async Task TheAetherCoreIsNotStartedWhenXrayEndsWhileItWaits()
    {
        // Xray was up a moment after its start and ended before its inbound listened (a port it cannot bind, a wrong
        // password for sudo); a listener of another program on that port does not count either.
        foreach (var listens in new[] { false, true })
        {
            var steps = new Steps(Warp) { XrayUpAfterTheWait = false, PortListens = listens };

            await (await AetherCoreLoad.AfterXrayAsync(steps.Build(), 42)).Should().BeEqualTo(AetherCoreLoadOutcome.XrayDown).Because($"listens: {listens}");

            await steps.Events.Should().BeEquivalentTo(["wait for 10822"], CollectionOrdering.Matching).Because($"listens: {listens}");
        }
    }

    [Test]
    public async Task XrayAndTheAetherCoreRunTogetherOrNotAtAll()
    {
        var steps = new Steps(Warp) { SessionFailure = "The Aether core could not start" };

        await (await AetherCoreLoad.AfterXrayAsync(steps.Build(), 42)).Should().BeEqualTo(AetherCoreLoadOutcome.AetherFailed);

        await steps.Events.Should().BeEquivalentTo(
            ["wait for 10822", "start aether 42", "report True The Aether core could not start", "stop cores"], CollectionOrdering.Matching);
    }

    [Test]
    public async Task TheAetherCoreStartsEvenWhenXraysInboundIsSlowToListen()
    {
        var steps = new Steps(Warp) { PortListens = false };

        await (await AetherCoreLoad.AfterXrayAsync(steps.Build(), 42)).Should().BeEqualTo(AetherCoreLoadOutcome.Started);

        await steps.Events.Should().BeEquivalentTo(["wait for 10822", "start aether 42"], CollectionOrdering.Matching);
    }

    [Test]
    public async Task ACoreThatNamesAnUpstreamOfItsOwnStartsWithoutAWaitForXray()
    {
        // A hand-written --upstream to a proxy elsewhere is no inbound of Xray's.
        var steps = new Steps(new AetherCore(["--masque", "--bind", "127.0.0.1:10819", "--upstream", "socks5://192.0.2.7:1080"]));

        await AetherCoreLoad.AfterXrayAsync(steps.Build(), 42);

        await steps.Events.Should().BeEquivalentTo(["start aether 42"], CollectionOrdering.Matching);
    }

    [Test]
    public async Task TheUpstreamOfACoreIsTheLoopbackPortItNames()
    {
        await AetherCoreLoad.UpstreamPort(Warp).Should().BeEqualTo(10822);
        await AetherCoreLoad.UpstreamPort(new AetherCore(["--upstream", "socks5://localhost:2080"])).Should().BeEqualTo(2080);
        await AetherCoreLoad.UpstreamPort(new AetherCore(["--upstream", "socks5h://user:pass@[::1]:2081"])).Should().BeEqualTo(2081);
        await AetherCoreLoad.UpstreamPort(new AetherCore(["--upstream", "socks5://192.0.2.7:1080"])).Should().BeNull();
        await AetherCoreLoad.UpstreamPort(new AetherCore(["--masque"])).Should().BeNull();
        await AetherCoreLoad.UpstreamPort(new AetherCore(["--upstream", "not a url"])).Should().BeNull();
    }

    [Test]
    public async Task AWaitForAPortEndsOnceItListensOrWhenItsTimeIsUp()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        try
        {
            await (await AetherCoreLoad.WaitForPortAsync(port, TimeSpan.FromSeconds(5))).Should().BeTrue();
        }
        finally
        {
            listener.Stop();
        }
        var waited = Stopwatch.StartNew();
        await (await AetherCoreLoad.WaitForPortAsync(port, TimeSpan.FromMilliseconds(400))).Should().BeFalse();
        await (waited.Elapsed < TimeSpan.FromSeconds(3)).Should().BeTrue();
    }

    [Test]
    public async Task AConnectionToItselfIsNoListener()
    {
        // D6: on Linux, a connection to a closed loopback port in the ephemeral range may be given that port to dial from,
        // and then connects to itself; no wait counts it (WaitForPortAsync, AnswersSocksAsync).
        var self = new IPEndPoint(IPAddress.Loopback, 50123);
        await AetherCoreLoad.IsSelfConnect(self, new IPEndPoint(IPAddress.Loopback, 50123)).Should().BeTrue();
        await AetherCoreLoad.IsSelfConnect(new IPEndPoint(IPAddress.IPv6Loopback, 50123), new IPEndPoint(IPAddress.IPv6Loopback, 50123)).Should().BeTrue();
        // As a dual-mode socket gives the IPv4 address.
        await AetherCoreLoad.IsSelfConnect(new IPEndPoint(IPAddress.Parse("::ffff:127.0.0.1"), 50123), self).Should().BeTrue();

        await AetherCoreLoad.IsSelfConnect(self, new IPEndPoint(IPAddress.Loopback, 50124)).Should().BeFalse();
        await AetherCoreLoad.IsSelfConnect(self, new IPEndPoint(IPAddress.Parse("127.0.0.2"), 50123)).Should().BeFalse();
        await AetherCoreLoad.IsSelfConnect(null, self).Should().BeFalse();
        await AetherCoreLoad.IsSelfConnect(self, null).Should().BeFalse();

        // A connection a listener takes comes from a port of its own.
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        try
        {
            using var client = new TcpClient();
            await client.ConnectAsync(IPAddress.Loopback, ((IPEndPoint)listener.LocalEndpoint).Port);
            await AetherCoreLoad.IsSelfConnect(client.Client.LocalEndPoint, client.Client.RemoteEndPoint).Should().BeFalse();
        }
        finally
        {
            listener.Stop();
        }

        // Where the system makes one (Linux and Windows: a socket dialling the port it is bound to), it is told as one.
        if (MakesSelfConnections)
        {
            using var socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
            socket.Bind(new IPEndPoint(IPAddress.Loopback, 0));
            await socket.ConnectAsync(socket.LocalEndPoint!);
            await AetherCoreLoad.IsSelfConnect(socket.LocalEndPoint, socket.RemoteEndPoint).Should().BeTrue();
        }
    }

    /// <summary>Whether this system connects a socket that dials the loopback port it is bound to to itself.</summary>
    private static bool MakesSelfConnections => OperatingSystem.IsLinux() || OperatingSystem.IsWindows();

    [Test]
    public async Task AWaitForAPortTakesNoConnectionToItselfForAListener()
    {
        // D6: each attempt dials from the port it waits for, nothing listening there, and so connects to itself; the
        // wait closes that connection and runs out. Where the system makes no such connection, there is nothing to tell.
        if (!MakesSelfConnections)
        {
            return;
        }
        int port;
        using (var probe = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp))
        {
            probe.Bind(new IPEndPoint(IPAddress.Loopback, 0));
            port = ((IPEndPoint)probe.LocalEndPoint!).Port;
        }
        var attempts = 0;
        TcpClient FromThePort()
        {
            Interlocked.Increment(ref attempts);
            var client = new TcpClient(AddressFamily.InterNetwork);
            client.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
            client.Client.Bind(new IPEndPoint(IPAddress.Loopback, port));
            return client;
        }

        await (await AetherCoreLoad.WaitForPortAsync(port, TimeSpan.FromMilliseconds(600), FromThePort)).Should().BeFalse();

        await (attempts > 0).Should().BeTrue();
    }

    // ---- the turns of loads, stops and the end of a session (critique 15)

    [Test]
    public async Task ASessionThatEndsOnItsOwnStopsTheCoresOfItsGeneration()
    {
        var turns = new AetherCoreTurns();
        long generation = 0;
        await turns.RunAsync(() =>
        {
            generation = turns.NewGeneration();
            return Task.CompletedTask;
        });
        var stops = 0;

        await (await turns.EndIfCurrentAsync(generation, () =>
        {
            stops++;
            return Task.CompletedTask;
        })).Should().BeTrue();

        await stops.Should().BeEqualTo(1);
        // Reported twice, it stops once: the stop started a generation of its own.
        await (await turns.EndIfCurrentAsync(generation, () =>
        {
            stops++;
            return Task.CompletedTask;
        })).Should().BeFalse();
        await stops.Should().BeEqualTo(1);
    }

    [Test]
    public async Task ACoreThatDiesWhileAReloadStartsTheNextCoresNeverStopsThem()
    {
        var turns = new AetherCoreTurns();
        long old = 0;
        await turns.RunAsync(() =>
        {
            old = turns.NewGeneration();
            return Task.CompletedTask;
        });
        var reloadStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var xrayUp = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var events = new List<string>();

        // A reload takes its turn: it stops the old cores and is between starting Xray and starting the next Aether core.
        var reload = turns.RunAsync(async () =>
        {
            turns.NewGeneration();
            lock (events)
            {
                events.Add("reload stops the old cores");
            }
            reloadStarted.SetResult();
            await xrayUp.Task;
            lock (events)
            {
                events.Add("reload starts the next aether core");
            }
        });
        await reloadStarted.Task;
        // The old core's end, reported meanwhile, waits for the reload's turn and finds a newer generation then.
        var end = turns.EndIfCurrentAsync(old, () =>
        {
            lock (events)
            {
                events.Add("the end stops the cores");
            }
            return Task.CompletedTask;
        });
        await Task.Delay(100);
        await end.IsCompleted.Should().BeFalse();
        xrayUp.SetResult();
        await reload;

        await (await end).Should().BeFalse();
        await events.Should().BeEquivalentTo(["reload stops the old cores", "reload starts the next aether core"], CollectionOrdering.Matching);
    }

    [Test]
    public async Task AReloadThatFailsBeforeItReplacesTheCoresLeavesTheirEndToStopThem()
    {
        var turns = new AetherCoreTurns();
        long old = 0;
        await turns.RunAsync(() =>
        {
            old = turns.NewGeneration();
            return Task.CompletedTask;
        });
        // A configuration that cannot be generated starts no generation: the old Xray still runs, without its core.
        await turns.RunAsync(() => Task.CompletedTask);
        var stopped = false;

        await (await turns.EndIfCurrentAsync(old, () =>
        {
            stopped = true;
            return Task.CompletedTask;
        })).Should().BeTrue();
        await stopped.Should().BeTrue();
    }

    [Test]
    public async Task AStopOnRequestOwnsTheCoresAsWell()
    {
        var turns = new AetherCoreTurns();
        long session = 0;
        await turns.RunAsync(() =>
        {
            session = turns.NewGeneration();
            return Task.CompletedTask;
        });
        // CoreStop: a new generation, then the stop.
        await turns.RunAsync(() =>
        {
            turns.NewGeneration();
            return Task.CompletedTask;
        });

        await (await turns.EndIfCurrentAsync(session, () => Task.CompletedTask)).Should().BeFalse();
    }
}
