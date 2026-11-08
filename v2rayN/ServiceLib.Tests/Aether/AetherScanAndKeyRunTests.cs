using System.Threading.Channels;
using ServiceLib.Tests.CoreConfig;
using TUnit.Assertions.Enums;

namespace ServiceLib.Tests.Aether;

/// <summary>
/// PattN (design 2.10, 2.13; PattNG AetherScanner.scan, AetherIdentityManager.register and AetherCoreManager.runUntil, which
/// PattNG has no unit test of): the scan and the key run, as cores of their own on fake cores. No core runs; the exit is a
/// fake one.
/// </summary>
public class AetherScanAndKeyRunTests : IDisposable
{
    private const int ExitPort = 41234;
    private static readonly TimeSpan Soon = TimeSpan.FromSeconds(5);

    private readonly string _root = Directory.CreateTempSubdirectory("pattn-aether-runs-").FullName;

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

    private static string LogLine(string message)
    {
        return $"[2026-09-11T10:00:00.000Z INFO  aether] {message}";
    }

    private static Func<FakeAetherCore, Task> Writes(params string[] lines)
    {
        return core =>
        {
            foreach (var line in lines)
            {
                core.Write(line);
            }
            return Task.CompletedTask;
        };
    }

    // ---- the first line that matches (PattNG runUntil)

    [Test]
    public async Task TheFirstMatchingLineEndsTheWait()
    {
        var lines = Channel.CreateUnbounded<string>();
        lines.Writer.TryWrite("one");
        lines.Writer.TryWrite("two: found");
        lines.Writer.TryWrite("three: found");

        var found = await AetherCoreManager.FirstMatchAsync(lines.Reader, line => line.EndsWith("found") ? line : null, Soon);

        await found.Should().BeEqualTo("two: found");
    }

    [Test]
    public async Task ACoreThatEndsOrTakesTooLongGivesNothing()
    {
        var ended = Channel.CreateUnbounded<string>();
        ended.Writer.TryWrite("nothing here");
        ended.Writer.TryComplete();
        await (await AetherCoreManager.FirstMatchAsync(ended.Reader, line => line == "found" ? line : null, Soon)).Should().BeNull();

        var silent = Channel.CreateUnbounded<string>();
        var clock = Stopwatch.StartNew();
        await (await AetherCoreManager.FirstMatchAsync(silent.Reader, line => line, TimeSpan.FromMilliseconds(200))).Should().BeNull();
        await (clock.Elapsed < Soon).Should().BeTrue();

        // A cancellation is no timeout: it is thrown.
        using var cancel = new CancellationTokenSource();
        var waiting = AetherCoreManager.FirstMatchAsync(silent.Reader, line => line, Soon, cancel.Token);
        cancel.Cancel();
        var cancelled = false;
        try
        {
            await waiting;
        }
        catch (OperationCanceledException)
        {
            cancelled = true;
        }
        await cancelled.Should().BeTrue();
    }

    // ---- the scan (design 2.13)

    private static ProfileItem Masque(Func<ProtocolExtraItem, ProtocolExtraItem>? extra = null)
    {
        return CoreConfigTestFactory.CreateAetherNode("scan", "WARP", AetherProtocol.Masque.Type, extra);
    }

    [Test]
    public async Task AScanRunsOnItsScanArgumentsThroughTheProfilesExitUntilTheEndpointIsNamed()
    {
        var cores = new FakeAetherCores(_root) { ExitPort = ExitPort };
        cores.Script = Writes(LogLine("[*] scanning"), LogLine("[+] selected MASQUE gateway 162.159.198.1:443"), LogLine("later"));
        var profile = Masque();
        var seen = new List<string>();

        var result = await AetherScanner.ScanAsync(cores.Manager, profile, line =>
        {
            lock (seen)
            {
                seen.Add(line);
            }
        }, Soon);

        await result.Outcome.Should().BeEqualTo(AetherRunOutcome.Done);
        await result.Value.Should().BeEqualTo(new AetherScanResult(new AetherEndpoint("162.159.198.1", 443)));
        // The scan arguments of the profile's settings, dialling out through the exit opened for the profile's exit-node.
        var expected = AetherArguments.StandaloneArguments(AetherArguments.Build(profile, 0, scan: true), ExitPort);
        await cores.Core(0).Arguments.Should().BeEquivalentTo(expected, CollectionOrdering.Matching);
        await cores.OpenedFor.Should().BeEquivalentTo([(AetherExit.Of(profile), AetherRole.Scan)], CollectionOrdering.Matching);
        await cores.Events.Should().BeEquivalentTo(["open Scan", "spawn 1", "kill 1", "close"], CollectionOrdering.Matching);
        List<string> lines;
        lock (seen)
        {
            lines = [.. seen];
        }
        await lines.Should().Contain(LogLine("[*] scanning"));
    }

    [Test]
    public async Task WithAnExitRuleTheScanEndsOnTheEndpointWhoseExitTheCoreAccepted()
    {
        var cores = new FakeAetherCores(_root);
        cores.Script = Writes(
            LogLine("[+] selected MASQUE gateway 162.159.198.1:443"),
            LogLine("[!] exit location RU rejected, looking again"),
            LogLine("[+] selected MASQUE gateway 162.159.198.2:443"),
            LogLine("[+] exit location DE accepted"));

        var result = await AetherScanner.ScanAsync(cores.Manager, Masque(extra => extra with { AetherExitLoc = "DE" }), null, Soon);

        await result.Value.Should().BeEqualTo(new AetherScanResult(new AetherEndpoint("162.159.198.2", 443)));
    }

    [Test]
    public async Task AScanThatEndsWithoutAnEndpointOrRunsOutOfTimeFindsNothing()
    {
        var cores = new FakeAetherCores(_root);
        cores.Script = core =>
        {
            core.Write(LogLine("[!] no gateway answered"));
            core.Exit(1);
            return Task.CompletedTask;
        };
        var ended = await AetherScanner.ScanAsync(cores.Manager, Masque(), null, Soon);
        await ended.Outcome.Should().BeEqualTo(AetherRunOutcome.Done);
        await ended.Value.Should().BeNull();

        cores.Script = null;
        var clock = Stopwatch.StartNew();
        var silent = await AetherScanner.ScanAsync(cores.Manager, Masque(), null, TimeSpan.FromMilliseconds(300));
        await silent.Value.Should().BeNull();
        await (clock.Elapsed < Soon).Should().BeTrue();
        // Its core is killed and its exit closed all the same.
        await cores.Events.TakeLast(2).Should().BeEquivalentTo(["kill 2", "close"], CollectionOrdering.Matching);
    }

    [Test]
    public async Task AScanWhoseExitNodeGivesNoOutboundRunsNoCore()
    {
        var cores = new FakeAetherCores(_root) { ExitPort = null };
        var profile = Masque(extra => extra with { AetherExitNode = " germany " });

        var result = await AetherScanner.ScanAsync(cores.Manager, profile, null, Soon);

        await result.Outcome.Should().BeEqualTo(AetherRunOutcome.NoExit);
        await cores.Cores.Should().BeEmpty();
        // The exit-node is looked up by its name, never a guess.
        await cores.OpenedFor.Single().Exit.Node.Should().BeEqualTo("germany");
    }

    [Test]
    public async Task AScanWhoseCoreCannotStartSaysSoRatherThanFindNothing()
    {
        // Design 2.4.4, as for the session's core: a core Windows cannot start for want of a DLL, or one that ends at once
        // without a line, did not start; "no endpoint found" would hide that.
        var cores = new FakeAetherCores(_root);
        cores.Script = core =>
        {
            core.Exit(AetherProcess.DllNotFoundExitCode);
            return Task.CompletedTask;
        };
        await (await AetherScanner.ScanAsync(cores.Manager, Masque(), null, Soon)).Outcome.Should().BeEqualTo(AetherRunOutcome.NotStarted);

        cores.Script = core =>
        {
            core.Exit(1);
            return Task.CompletedTask;
        };
        await (await AetherScanner.ScanAsync(cores.Manager, Masque(), null, Soon)).Outcome.Should().BeEqualTo(AetherRunOutcome.NotStarted);

        // A core that wrote a line ran: it found nothing.
        cores.Script = core =>
        {
            core.Write(LogLine("[!] no gateway answered"));
            core.Exit(1);
            return Task.CompletedTask;
        };
        var ran = await AetherScanner.ScanAsync(cores.Manager, Masque(), null, Soon);
        await ran.Outcome.Should().BeEqualTo(AetherRunOutcome.Done);
        await ran.Value.Should().BeNull();

        // And so did one that lived past the first second, whatever it wrote.
        cores.Script = async core =>
        {
            await Task.Delay(AetherCoreManager.StartWindow + TimeSpan.FromMilliseconds(200));
            core.Exit(1);
        };
        var lived = await AetherScanner.ScanAsync(cores.Manager, Masque(), null, Soon);
        await lived.Outcome.Should().BeEqualTo(AetherRunOutcome.Done);
        await lived.Value.Should().BeNull();
    }

    [Test]
    public async Task AStopOfEveryCoreEndsAScanWithoutACancellationItsCallerNeverAskedFor()
    {
        // An update of aether-core holds the cores, and PattN stops them as it exits: the scan says it was stopped, and
        // throws nothing, so that a window never takes it for its own cancel.
        var cores = new FakeAetherCores(_root);
        var scanning = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        cores.Script = core =>
        {
            core.Write(LogLine("[*] scanning"));
            scanning.TrySetResult();
            return Task.CompletedTask;
        };
        var scan = AetherScanner.ScanAsync(cores.Manager, Masque(), null, TimeSpan.FromMinutes(1));
        await scanning.Task.WaitAsync(Soon);

        var hold = await cores.Manager.HoldAsync().WaitAsync(Soon);

        await (await scan.WaitAsync(Soon)).Should().BeEqualTo(new AetherRunResult<AetherScanResult>(AetherRunOutcome.Stopped));
        // Its core is gone and its exit closed by the time the hold is there.
        await cores.Events.Should().BeEquivalentTo(["open Scan", "spawn 1", "kill 1", "close"], CollectionOrdering.Matching);
        // One asked for during the hold does not start, and says so the same way.
        await (await AetherScanner.ScanAsync(cores.Manager, Masque(), null, Soon)).Outcome.Should().BeEqualTo(AetherRunOutcome.Stopped);
        await hold.DisposeAsync();
        await cores.Cores.Count.Should().BeEqualTo(1);

        // A cancellation of the caller's is thrown, as before.
        cores.Script = null;
        using var cancel = new CancellationTokenSource();
        var cancelled = AetherScanner.ScanAsync(cores.Manager, Masque(), null, TimeSpan.FromMinutes(1), cancel.Token);
        for (var waited = 0; waited < 200 && cores.Cores.Count < 2; waited++)
        {
            await Task.Delay(25);
        }
        cancel.Cancel();
        var thrown = false;
        try
        {
            await cancelled.WaitAsync(Soon);
        }
        catch (OperationCanceledException)
        {
            thrown = true;
        }
        await thrown.Should().BeTrue();
    }

    // ---- the key run (design 2.10, PattNG register)

    private static string KeyFile(string deviceId)
    {
        return $"device_id = \"{deviceId}\"\nipv4 = \"172.16.0.2\"\nipv6 = \"2606:4700:110:8a36::1\"\n";
    }

    private static Func<FakeAetherCore, Task> Registers(IEnumerable<string> files, string deviceId = "new", bool word = true)
    {
        return core =>
        {
            foreach (var file in files)
            {
                File.WriteAllText(Path.Combine(core.KeysDir, file), KeyFile(deviceId));
            }
            core.Write(LogLine(word ? "[+] identities ready: masque" : "[!] registration failed"));
            if (!word)
            {
                core.Exit(1);
            }
            return Task.CompletedTask;
        };
    }

    [Test]
    public async Task AKeyRunRegistersIntoTheFolderItIsGivenAtTheInfoLevelThroughItsExit()
    {
        var cores = new FakeAetherCores(_root);
        var keysDir = Directory.CreateDirectory(Path.Combine(_root, "renewal")).FullName;
        cores.Script = Registers([AetherPaths.MasqueFile]);
        var exit = new AetherExit(Node: "germany");
        var seen = new List<string>();

        var registered = await AetherIdentityManager.Register(cores.Manager, ["--register", "masque", "--log-level", "error"], exit, line =>
        {
            lock (seen)
            {
                seen.Add(line);
            }
        })(keysDir, CancellationToken.None);

        await registered.Should().BeTrue();
        await File.Exists(Path.Combine(keysDir, AetherPaths.MasqueFile)).Should().BeTrue();
        // A quieter level gives way to the info level, whose lines the run waits for.
        await cores.Core(0).Arguments.Should().BeEquivalentTo(
            ["--register", "masque", "--log-level", "info", "--upstream", $"socks5://127.0.0.1:{ExitPort}"], CollectionOrdering.Matching);
        await cores.OpenedFor.Should().BeEquivalentTo([(exit, AetherRole.Key)], CollectionOrdering.Matching);
        await cores.Core(0).KeysDir.Should().BeEqualTo(keysDir);
        await cores.Core(0).Killed.Should().BeTrue();
        List<string> lines;
        lock (seen)
        {
            lines = [.. seen];
        }
        await lines.Should().Contain(LogLine("[+] identities ready: masque"));
    }

    [Test]
    public async Task ARenewalThroughTheCorePutsTheNewKeysInPlaceOnlyOnceTheyAreAllReady()
    {
        var cores = new FakeAetherCores(_root);
        var work = Directory.CreateDirectory(Path.Combine(_root, "guiConfigs", "aether")).FullName;
        var renewal = Path.Combine(_root, "guiConfigs", "aether-renewal");
        foreach (var file in AetherPaths.KeyFiles)
        {
            File.WriteAllText(Path.Combine(work, file), KeyFile("old"));
        }
        var files = AetherIdentityManager.FilesOf(AetherKeyKind.Mim);
        var register = AetherIdentityManager.Register(cores.Manager, ["--register", "mim"], AetherExit.Plain);

        // A run that ends without its word: every key in use stays.
        cores.Script = Registers(files, word: false);
        await (await AetherIdentityManager.RenewAsync(work, renewal, files, ct => register(renewal, ct))).Should().BeFalse();
        await AetherPaths.KeyFiles.All(file => AetherIdentityManager.Parse(File.ReadAllText(Path.Combine(work, file)))?.DeviceId == "old").Should().BeTrue();
        await Directory.Exists(renewal).Should().BeFalse();

        // A run that registers them: the keys of its kind are replaced, the others left alone (the memory rule: an old key
        // goes only once its new one is ready).
        cores.Script = Registers(files);
        await (await AetherIdentityManager.RenewAsync(work, renewal, files, ct => register(renewal, ct))).Should().BeTrue();
        foreach (var file in AetherPaths.KeyFiles)
        {
            var device = AetherIdentityManager.Parse(File.ReadAllText(Path.Combine(work, file)))?.DeviceId;
            await device.Should().BeEqualTo(files.Contains(file) ? "new" : "old");
        }
        await Directory.Exists(renewal).Should().BeFalse();
    }

    [Test]
    public async Task AKeyRunWhoseCoreCannotStartRegistersNothingAndSaysWhy()
    {
        var cores = new FakeAetherCores(_root);
        cores.Script = core =>
        {
            core.Exit(AetherProcess.DllNotFoundExitCode);
            return Task.CompletedTask;
        };
        var run = new AetherStandaloneRun(["--register", "masque"], AetherRole.Key) { Exit = AetherExit.Plain };

        var result = await cores.Manager.RunUntilAsync(run, Soon, line => AetherIdentityManager.IsRegistered(line) ? line : null);

        await result.Outcome.Should().BeEqualTo(AetherRunOutcome.NotStarted);
        await result.Value.Should().BeNull();
        var keysDir = Directory.CreateDirectory(Path.Combine(_root, "renewal")).FullName;
        await (await AetherIdentityManager.Register(cores.Manager, ["--register", "masque"], AetherExit.Plain)(keysDir, CancellationToken.None)).Should().BeFalse();
    }

    [Test]
    public async Task ARenewalThatAStopOfEveryCoreEndsKeepsTheKeysInUse()
    {
        // PattN exits, or aether-core is updated, while a key run waits for its word: no new key goes into place, every
        // key in use stays, and nothing is thrown at the keys window, whose token nobody cancelled.
        var cores = new FakeAetherCores(_root);
        var work = Directory.CreateDirectory(Path.Combine(_root, "guiConfigs", "aether")).FullName;
        var renewal = Path.Combine(_root, "guiConfigs", "aether-renewal");
        foreach (var file in AetherPaths.KeyFiles)
        {
            File.WriteAllText(Path.Combine(work, file), KeyFile("old"));
        }
        var files = AetherIdentityManager.FilesOf(AetherKeyKind.Masque);
        var registering = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        cores.Script = core =>
        {
            // Half the keys written, and no word yet.
            File.WriteAllText(Path.Combine(core.KeysDir, files[0]), KeyFile("new"));
            core.Write(LogLine("[*] registering"));
            registering.TrySetResult();
            return Task.CompletedTask;
        };
        var register = AetherIdentityManager.Register(cores.Manager, ["--register", "masque"], AetherExit.Plain);
        var renewed = AetherIdentityManager.RenewAsync(work, renewal, files, ct => register(renewal, ct));
        await registering.Task.WaitAsync(Soon);

        await cores.Manager.StopAllAsync().WaitAsync(Soon);

        await (await renewed.WaitAsync(Soon)).Should().BeFalse();
        await AetherPaths.KeyFiles.All(file => AetherIdentityManager.Parse(File.ReadAllText(Path.Combine(work, file)))?.DeviceId == "old").Should().BeTrue();
        await Directory.Exists(renewal).Should().BeFalse();
    }

    [Test]
    public async Task AKeyRunWhoseExitDoesNotOpenRegistersNothing()
    {
        var cores = new FakeAetherCores(_root) { ExitPort = null };
        var keysDir = Directory.CreateDirectory(Path.Combine(_root, "renewal")).FullName;

        var registered = await AetherIdentityManager.Register(cores.Manager, ["--register", "wg"], new AetherExit(Node: "gone"))(keysDir, CancellationToken.None);

        await registered.Should().BeFalse();
        await cores.Cores.Should().BeEmpty();
    }
}
