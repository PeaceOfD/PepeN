namespace ServiceLib.Services;

/// <summary>
/// Owns the Windows hev-socks5-tunnel process. Route management is deliberately separate:
/// callers must install the upstream bypass and default routes, then restore them on shutdown.
/// </summary>
public sealed class HevTunService : IAsyncDisposable
{
    public const string ExecutableName = "hev-socks5-tunnel.exe";
    public const string WintunLibraryName = "wintun.dll";
    public const string InterfaceName = "PepeNTun";

    private readonly Process _process;
    private readonly string _configPath;
    private bool _disposed;

    private HevTunService(Process process, string configPath)
    {
        _process = process;
        _configPath = configPath;
    }

    /// <summary>Raised when the Hev process exits unexpectedly or after a requested stop.</summary>
    public event EventHandler? Exited;

    public bool HasExited
    {
        get
        {
            try { return _process.HasExited; }
            catch (InvalidOperationException) { return true; }
        }
    }

    /// <summary>
    /// Builds a validated Hev configuration without touching the filesystem or network.
    /// Kept separate so the exact runtime configuration can be covered by unit tests.
    /// </summary>
    public static string BuildConfig(int socksPort, int mtu, string ipv4Address, string? ipv6Address)
    {
        if (socksPort is < 1 or > 65535)
            throw new ArgumentOutOfRangeException(nameof(socksPort));
        if (mtu is < 576 or > 9000)
            throw new ArgumentOutOfRangeException(nameof(mtu));
        if (!IPAddress.TryParse(ipv4Address, out var ipv4) || ipv4.AddressFamily != AddressFamily.InterNetwork)
            throw new ArgumentException("A valid IPv4 tunnel address is required.", nameof(ipv4Address));
        if (!string.IsNullOrWhiteSpace(ipv6Address)
            && (!IPAddress.TryParse(ipv6Address, out var ipv6) || ipv6.AddressFamily != AddressFamily.InterNetworkV6))
            throw new ArgumentException("The optional tunnel address must be a valid IPv6 address.", nameof(ipv6Address));

        var yaml = new StringBuilder()
            .AppendLine("tunnel:")
            .AppendLine($"  name: {InterfaceName}")
            .AppendLine($"  mtu: {mtu}")
            .AppendLine("  multi-queue: false")
            .AppendLine($"  ipv4: '{ipv4}'")
            .AppendLine("  icmp: 'off'");
        if (!string.IsNullOrWhiteSpace(ipv6Address))
            yaml.AppendLine($"  ipv6: '{IPAddress.Parse(ipv6Address)}'");
        yaml.AppendLine("socks5:")
            .AppendLine($"  port: {socksPort}")
            .AppendLine("  address: 127.0.0.1")
            .AppendLine("  udp: 'udp'")
            .AppendLine("  log-level: info");
        return yaml.ToString();
    }

    /// <summary>
    /// Starts Hev against the local SOCKS5 listener. The executable and matching-architecture
    /// wintun.dll must be installed side-by-side in the application's bin directory. Startup
    /// is not considered successful until Windows reports the requested tunnel interface Up.
    /// </summary>
    public static async Task<HevTunService> StartAsync(
        int socksPort,
        int mtu,
        string ipv4Address,
        string? ipv6Address,
        Func<bool, string, Task>? report = null,
        CancellationToken cancellationToken = default)
    {
        if (!OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException("Hev Windows integration is only available on Windows.");
        var yaml = BuildConfig(socksPort, mtu, ipv4Address, ipv6Address);

        var executablePath = Utils.GetBinPath(ExecutableName);
        var wintunPath = Utils.GetBinPath(WintunLibraryName);
        if (!File.Exists(executablePath))
            throw new FileNotFoundException($"Hev executable was not found: {executablePath}", executablePath);
        if (!File.Exists(wintunPath))
            throw new FileNotFoundException($"The matching Wintun library was not found: {wintunPath}", wintunPath);

        var configPath = Utils.GetBinConfigPath("hev-tun.yml");
        await File.WriteAllTextAsync(configPath, yaml, new UTF8Encoding(false), cancellationToken);

        var startInfo = new ProcessStartInfo
        {
            FileName = executablePath,
            Arguments = $"\\"{configPath}\\"",
            WorkingDirectory = Path.GetDirectoryName(executablePath)!,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };

        var process = new Process { StartInfo = startInfo, EnableRaisingEvents = true };
        process.OutputDataReceived += (_, e) =>
        {
            if (!string.IsNullOrWhiteSpace(e.Data))
                _ = ReportSafelyAsync(report, $"[Hev] {e.Data}{Environment.NewLine}");
        };
        process.ErrorDataReceived += (_, e) =>
        {
            if (!string.IsNullOrWhiteSpace(e.Data))
                _ = ReportSafelyAsync(report, $"[Hev] {e.Data}{Environment.NewLine}");
        };

        try
        {
            if (!process.Start())
                throw new InvalidOperationException("Windows did not start the Hev process.");

            var service = new HevTunService(process, configPath);
            process.Exited += (_, _) => service.OnProcessExited();
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();
            await WaitForTunnelInterfaceAsync(process, cancellationToken);

            if (report is not null)
                await report(false, $"Hev started; SOCKS5 127.0.0.1:{socksPort}, interface {InterfaceName}.");
            return service;
        }
        catch
        {
            try { if (!process.HasExited) process.Kill(true); } catch { }
            process.Dispose();
            try { File.Delete(configPath); } catch { }
            throw;
        }
    }

    private static async Task WaitForTunnelInterfaceAsync(Process process, CancellationToken cancellationToken)
    {
        // Hev creates the Wintun adapter asynchronously. Do not let the caller install routes
        // until Windows can see the adapter in the Up state.
        var deadline = System.Diagnostics.Stopwatch.StartNew();
        var timeout = TimeSpan.FromSeconds(15);
        while (deadline.Elapsed < timeout)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (process.HasExited)
                throw new InvalidOperationException($"Hev exited during startup (exit code {process.ExitCode}).");

            var ready = System.Net.NetworkInformation.NetworkInterface.GetAllNetworkInterfaces()
                .Any(adapter =>
                    string.Equals(adapter.Name, InterfaceName, StringComparison.OrdinalIgnoreCase)
                    && adapter.OperationalStatus == System.Net.NetworkInformation.OperationalStatus.Up);
            if (ready)
                return;

            await Task.Delay(150, cancellationToken);
        }

        throw new TimeoutException(
            $"Hev did not bring the '{InterfaceName}' network interface Up within {timeout.TotalSeconds:0} seconds.");
    }

    private void OnProcessExited()
    {
        try { Exited?.Invoke(this, EventArgs.Empty); }
        catch (Exception ex) { Logging.SaveLog("HevTunService", ex); }
    }

    private static async Task ReportSafelyAsync(Func<bool, string, Task>? report, string message)
    {
        if (report is null) return;
        try { await report(false, message); }
        catch (Exception ex) { Logging.SaveLog("HevTunService", ex); }
    }

    public async Task StopAsync()
    {
        if (_disposed) return;
        try
        {
            if (!_process.HasExited)
            {
                _process.Kill(true);
                await _process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(5));
            }
        }
        catch (InvalidOperationException) { }
        finally
        {
            try { File.Delete(_configPath); } catch { }
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        await StopAsync();
        _process.Dispose();
        _disposed = true;
        GC.SuppressFinalize(this);
    }
}
