using TUnit.Assertions.Enums;
using ServiceLib.Tests.CoreConfig;

namespace ServiceLib.Tests.Aether;

/// <summary>
/// PattN (design 2.6.5): the short-lived exit Xray a scan or a key run dials out through, with a fake Xray. Its
/// configuration is checked in AetherExitConfigTests.
/// </summary>
public class AetherExitServiceTests : IDisposable
{
    private const int P = AetherPorts.DefaultListenPort;

    private readonly string _root = Directory.CreateTempSubdirectory("pattn-aether-exit-").FullName;

    public void Dispose()
    {
        Directory.Delete(_root, recursive: true);
    }

    private sealed class FakeXray(List<string> events) : IAetherExitProcess
    {
        public bool HasExited { get; private set; }

        public Task StopAsync()
        {
            events.Add("stop xray");
            HasExited = true;
            return Task.CompletedTask;
        }
    }

    private AetherExitHost Host(List<string> events, bool generated = true, bool starts = true, bool listens = true, Func<int>? pick = null,
        Func<CancellationToken, Task<bool>>? wait = null)
    {
        return new AetherExitHost
        {
            ListenPort = () => P,
            PickPort = pick ?? (() => 41950),
            Generate = (exit, port) =>
            {
                events.Add($"generate {port}");
                return Task.FromResult(generated
                    ? new RetResult(true, "ok", $$"""{"inbounds":[{"port":{{port}}}]}""")
                    : new RetResult(false, "no profile is named \"germany\""));
            },
            ConfigPath = name => Path.Combine(_root, name),
            StartXray = name =>
            {
                events.Add($"start xray {(File.Exists(Path.Combine(_root, name)) ? "on its file" : "without a file")}");
                return Task.FromResult<IAetherExitProcess?>(starts ? new FakeXray(events) : null);
            },
            WaitForPort = (port, _, ct) =>
            {
                events.Add($"wait for {port}");
                return wait?.Invoke(ct) ?? Task.FromResult(listens);
            },
        };
    }

    private List<string> ConfigFiles()
    {
        return Directory.EnumerateFiles(_root, "config*AetherExit*.json").ToList();
    }

    // The exit Xray's file holds the outbound of the exit-node's profile; one a PattN that ended without closing its exit
    // left behind goes with the hourly sweep of binConfigs, as a latency test's does.
    [Test]
    public async Task AnExitsFileLeftBehindGoesWithTheHourlySweepOfBinConfigs()
    {
        var events = new List<string>();
        var exit = await new AetherExitService(Host(events)).OpenAsync(AetherExit.Plain, "aether-scan");
        var file = ConfigFiles().Single();
        var latencyTest = Path.Combine(_root, string.Format(Global.CoreSpeedtestConfigFileName, Utils.GetGuid(false)));
        File.WriteAllText(latencyTest, "{}");
        foreach (var left in new[] { file, latencyTest })
        {
            File.SetCreationTime(left, DateTime.Now.AddHours(-3));
        }

        // TaskManager's sweep, every hour.
        FileUtils.DeleteExpiredFiles(_root, DateTime.Now.AddHours(-1), "Test");

        await File.Exists(latencyTest).Should().BeFalse();
        await File.Exists(file).Should().BeFalse();
        // Closing the exit after the sweep is no failure.
        await exit!.DisposeAsync();
    }

    // Design 2.6.5: the context of an exit is built to the end, as a session's is. A profile with an id would be checked,
    // fail the checks of a VMess, and stop the builder before it leaves out the bind interface and the send-through
    // address of the settings that the app cannot use (see AetherExitConfigTests).
    [Test]
    public async Task TheProfileAnExitsContextIsBuiltForIsCheckedForNothing()
    {
        var profile = AetherExitHost.ContextProfile();
        var context = CoreConfigTestFactory.CreateContext(CoreConfigTestFactory.CreateConfig(ECoreType.Xray), profile, ECoreType.Xray);

        var (resolved, result) = await CoreConfigContextBuilder.ResolveNodeAsync(context, profile);

        await result.Success.Should().BeTrue().Because(string.Join(" | ", result.Errors));
        await ReferenceEquals(resolved, profile).Should().BeTrue();
        await profile.CoreType.Should().BeEqualTo(ECoreType.Xray);
    }

    [Test]
    public async Task AnExitOpensOnceItsXrayListensAndClosesWithItsFile()
    {
        var events = new List<string>();
        var service = new AetherExitService(Host(events));

        var exit = await service.OpenAsync(new AetherExit(DialMode: "code-1"), "aether-scan");

        await exit.Should().NotBeNull();
        await exit!.Port.Should().BeEqualTo(41950);
        await ConfigFiles().Count.Should().BeEqualTo(1);
        await File.ReadAllText(ConfigFiles()[0]).Should().Contain("41950");
        await exit.DisposeAsync();
        await exit.DisposeAsync();

        await events.Should().BeEquivalentTo(["generate 41950", "start xray on its file", "wait for 41950", "stop xray"], CollectionOrdering.Matching);
        await ConfigFiles().Count.Should().BeEqualTo(0);
    }

    [Test]
    public async Task AnExitNodeThatGivesNoOutboundStartsNoXray()
    {
        // The core would reach the internet without the exit-node it names.
        var events = new List<string>();

        var exit = await new AetherExitService(Host(events, generated: false)).OpenAsync(new AetherExit(Node: "germany"), "aether-key");

        await exit.Should().BeNull();
        await events.Should().BeEquivalentTo(["generate 41950"], CollectionOrdering.Matching);
        await ConfigFiles().Count.Should().BeEqualTo(0);
    }

    [Test]
    public async Task AnXrayThatDoesNotStartOrListenLeavesNothingBehind()
    {
        var notStarted = new List<string>();
        await (await new AetherExitService(Host(notStarted, starts: false)).OpenAsync(AetherExit.Plain, "aether-scan")).Should().BeNull();
        await notStarted.Should().BeEquivalentTo(["generate 41950", "start xray on its file"], CollectionOrdering.Matching);
        await ConfigFiles().Count.Should().BeEqualTo(0);

        var notListening = new List<string>();
        await (await new AetherExitService(Host(notListening, listens: false)).OpenAsync(AetherExit.Plain, "aether-scan")).Should().BeNull();
        await notListening.Should().BeEquivalentTo(["generate 41950", "start xray on its file", "wait for 41950", "stop xray"], CollectionOrdering.Matching);
        await ConfigFiles().Count.Should().BeEqualTo(0);
    }

    [Test]
    public async Task AnExitCancelledWhileItsXrayComesUpIsStopped()
    {
        var events = new List<string>();
        using var cancellation = new CancellationTokenSource();
        var service = new AetherExitService(Host(events, wait: async ct =>
        {
            await Task.Delay(Timeout.Infinite, ct);
            return true;
        }));

        var opening = service.OpenAsync(AetherExit.Plain, "aether-scan", cancellation.Token);
        await Task.Delay(100);
        cancellation.Cancel();
        var cancelled = false;
        try
        {
            await opening;
        }
        catch (OperationCanceledException)
        {
            cancelled = true;
        }

        await cancelled.Should().BeTrue();
        await events.Last().Should().BeEqualTo("stop xray");
        await ConfigFiles().Count.Should().BeEqualTo(0);
    }

    [Test]
    public async Task TheExitTakesNoneOfThePortsOfTheAetherCores()
    {
        // The Aether listen port and the three after it are for the cores, a test's or the session's, which may start meanwhile.
        var given = new Queue<int>([P, P + 1, P + 2, P + 3, 41950]);
        await AetherExitService.FreePort(given.Dequeue, P).Should().BeEqualTo(41950);
        await AetherExitService.FreePort(() => P + 3, P).Should().BeNull();
        await AetherExitService.FreePort(() => P + 4, P).Should().BeEqualTo(P + 4);
        await AetherExitService.FreePort(() => P - 1, P).Should().BeEqualTo(P - 1);

        var events = new List<string>();
        var ports = new Queue<int>([P + 3, 41951]);
        var exit = await new AetherExitService(Host(events, pick: ports.Dequeue)).OpenAsync(AetherExit.Plain, "aether-scan");
        await exit!.Port.Should().BeEqualTo(41951);
        await exit.DisposeAsync();
    }
}
