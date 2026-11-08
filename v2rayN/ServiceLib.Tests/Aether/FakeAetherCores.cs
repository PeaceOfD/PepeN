using System.ComponentModel;
using ServiceLib.Tests.CoreConfig;

namespace ServiceLib.Tests.Aether;

/// <summary>
/// An <see cref="AetherCoreManager" /> on fake cores for the tests of latency tests, scans and key runs: no core runs,
/// and nothing reaches the network. Each spawned core runs <see cref="Script" />, which writes what a real core would,
/// and an exit the manager opens is a fake one on <see cref="ExitPort" />.
/// </summary>
internal sealed class FakeAetherCores
{
    private readonly List<string> _events = [];
    private int _ids;

    public FakeAetherCores(string root)
    {
        Folders = new AetherFolders(Path.Combine(root, "guiConfigs"), Path.Combine(root, "bin", "aether"));
        Manager = new AetherCoreManager(new AetherCoreHost
        {
            Folders = Folders,
            Spawn = (info, onLine) =>
            {
                if (SpawnFails)
                {
                    throw new Win32Exception(2, "The system cannot find the file specified");
                }
                var core = new FakeAetherCore(this, info, onLine, Interlocked.Increment(ref _ids));
                lock (Cores)
                {
                    Cores.Add(core);
                }
                Log($"spawn {core.Id}");
                if (Script is { } script)
                {
                    // As a real core writes, after it started.
                    _ = Task.Run(async () =>
                    {
                        await Task.Delay(20);
                        await script(core);
                    });
                }
                return core;
            },
            OpenExit = (exit, role, _) =>
            {
                Log($"open {role}");
                lock (OpenedFor)
                {
                    OpenedFor.Add((exit, role));
                }
                return Task.FromResult<IAetherExit?>(ExitPort is { } port ? new FakeExit(this, port) : null);
            },
            ExitWait = TimeSpan.FromMilliseconds(300),
            ProbeExitWait = TimeSpan.FromMilliseconds(300),
            TestWait = TimeSpan.FromSeconds(3),
        });
    }

    public AetherFolders Folders { get; }
    public AetherCoreManager Manager { get; }
    public List<FakeAetherCore> Cores { get; } = [];
    public List<(AetherExit Exit, AetherRole Role)> OpenedFor { get; } = [];

    /// <summary>The port of the exit the manager opens; null for an exit that does not open.</summary>
    public int? ExitPort { get; set; } = 41234;

    public bool SpawnFails { get; set; }

    /// <summary>What each core does once spawned.</summary>
    public Func<FakeAetherCore, Task>? Script { get; set; }

    public List<string> Events
    {
        get
        {
            lock (_events)
            {
                return [.. _events];
            }
        }
    }

    public FakeAetherCore Core(int index)
    {
        lock (Cores)
        {
            return Cores[index];
        }
    }

    public void Log(string text)
    {
        lock (_events)
        {
            _events.Add(text);
        }
    }

    private sealed class FakeExit(FakeAetherCores cores, int port) : IAetherExit
    {
        public int Port { get; } = port;

        public ValueTask DisposeAsync()
        {
            cores.Log("close");
            return ValueTask.CompletedTask;
        }
    }
}

/// <summary>A core that writes what the test says, exits when told, and ends when killed.</summary>
internal sealed class FakeAetherCore(FakeAetherCores cores, ProcessStartInfo info, Action<string> onLine, int id) : IAetherCoreProcess
{
    private readonly TaskCompletionSource<int> _exited = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public ProcessStartInfo Info { get; } = info;
    public List<string> Arguments => Info.ArgumentList.ToList();
    public int Id { get; } = id;
    public DateTime StartedAt { get; } = DateTime.UtcNow;
    public bool HasExited => _exited.Task.IsCompleted;
    public Task<int> Exited => _exited.Task;
    public bool Killed { get; private set; }

    /// <summary>A stop of every core and the run's own end may kill it at once: the kill is told once, before either goes on.</summary>
    private readonly object _killing = new();

    /// <summary>The folder the core writes the keys it registers into.</summary>
    public string KeysDir => Path.GetDirectoryName(Info.Environment["AETHER_MASQUE_CONFIG"]!)!;

    public void Write(string line)
    {
        onLine(line);
    }

    public void Exit(int code)
    {
        _exited.TrySetResult(code);
    }

    public Task<bool> KillAsync(TimeSpan wait)
    {
        lock (_killing)
        {
            if (!Killed)
            {
                Killed = true;
                cores.Log($"kill {Id}");
            }
        }
        Exit(-9);
        return Task.FromResult(true);
    }

    public void Dispose()
    {
    }
}

/// <summary>
/// What <see cref="AetherDelayTester" /> works with, on fakes: the profile's checks, the core its configuration needs
/// (before it dials out through the test's Xray), the ports that answer, and a log of what happened, in order. Shared by
/// the tests of the tester and of the speed test that runs it.
/// </summary>
internal sealed class AetherMeasureRig(string root)
{
    private readonly HashSet<int> _answering = [];
    private int _nextPort = 41500;

    public FakeAetherCores Cores { get; } = new(root);
    public NodeValidatorResult Checks { get; set; } = NodeValidatorResult.Empty();
    public AetherCore? Needed { get; set; }
    public List<int> Probed { get; } = [];

    /// <summary>Ports the system hands out first, before 41501 and on: the test's mixed inbound's, then its secondary one's.</summary>
    public Queue<int> Ports { get; } = new();

    /// <summary>What the start of a test's Xray waits for once it is logged, as a real Xray takes its time.</summary>
    public Func<Task>? XrayStarting { get; set; }

    /// <summary>What the stop of a test's Xray waits for before it is logged, as a real Xray takes its time to end.</summary>
    public Func<Task>? XrayStopping { get; set; }

    /// <summary>Why the Aether core cannot run, as AetherCoreLoad.MissingCoreMessage tells it; null while it can.</summary>
    public Func<string?> MissingCore { get; set; } = () => null;

    /// <summary>Whether, and to what, the test's Xray is bound under a TUN (D2); none unless a test says so.</summary>
    public AetherUplinkBinding? Uplink { get; set; }

    /// <summary>
    /// The configurations the test's Xray was started on, in order: each generation gives "{}", or the uplink it was
    /// bound to as {"uplink": name}.
    /// </summary>
    public List<string> XrayStartedOn { get; } = [];

    public void Answer(int port)
    {
        lock (_answering)
        {
            _answering.Add(port);
        }
    }

    public AetherDelayTester Tester(TimeSpan? budget = null)
    {
        return new AetherDelayTester(new AetherTestHost
        {
            Cores = Cores.Manager,
            Build = profile => Task.FromResult(new CoreConfigContextBuilderResult(
                CoreConfigTestFactory.CreateContext(CoreConfigTestFactory.CreateConfig(), profile, ECoreType.Xray), Checks)),
            Generate = (context, port) =>
            {
                var secondary = context.Aether.SecondaryPort;
                Cores.Log($"generate {port} {(secondary is { } routed ? routed.ToString() : "-")}");
                context.Aether.Reset();
                if (Needed != null && secondary is { } through)
                {
                    context.Aether.Core = Needed.Through(through);
                }
                return new RetResult(true, null, context.Aether.Uplink is { } uplink ? $$"""{"uplink": "{{uplink}}"}""" : "{}");
            },
            StartXray = async content =>
            {
                lock (XrayStartedOn)
                {
                    XrayStartedOn.Add(content);
                }
                Cores.Log("xray start");
                if (XrayStarting is { } starting)
                {
                    await starting();
                }
                return new Stopping(Cores, XrayStopping);
            },
            PickPort = () =>
            {
                lock (Ports)
                {
                    if (Ports.Count > 0)
                    {
                        return Ports.Dequeue();
                    }
                }
                return Interlocked.Increment(ref _nextPort);
            },
            WaitForPort = (_, _, _) => Task.FromResult(true),
            AnswersSocks = (port, _) =>
            {
                lock (_answering)
                {
                    return Task.FromResult(_answering.Contains(port));
                }
            },
            MissingCore = () => MissingCore(),
            Uplink = Uplink,
            Budget = budget ?? TimeSpan.FromSeconds(3),
            Poll = TimeSpan.FromMilliseconds(20),
        });
    }

    public AetherTestProbe Probe(int delay = 42)
    {
        return (port, _, _) =>
        {
            lock (Probed)
            {
                Probed.Add(port);
            }
            Cores.Log($"probe {port}");
            return Task.FromResult(delay);
        };
    }

    private sealed class Stopping(FakeAetherCores cores, Func<Task>? stopping) : IAsyncDisposable
    {
        public async ValueTask DisposeAsync()
        {
            if (stopping != null)
            {
                await stopping();
            }
            cores.Log("xray stop");
        }
    }
}
