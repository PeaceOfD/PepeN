namespace ServiceLib.Services;

/// <summary>
/// Persists the original IPv4/IPv6 interface metrics before lowering the Hev adapter metric.
/// The journal remains for the tunnel lifetime and is removed only after restoration.
/// </summary>
public interface IHevInterfaceMetricBackend
{
    Task<int> GetAsync(int interfaceIndex, AddressFamily family, CancellationToken cancellationToken);
    Task SetAsync(int interfaceIndex, AddressFamily family, int metric, CancellationToken cancellationToken);
}

public sealed class HevInterfaceMetricTransaction(IHevInterfaceMetricBackend backend)
{
    private sealed class MetricEntry
    {
        public int InterfaceIndex { get; set; }
        public AddressFamily Family { get; set; }
        public int OriginalMetric { get; set; }
        public int TunnelMetric { get; set; }
    }

    private sealed class Journal
    {
        public List<MetricEntry> Entries { get; set; } = [];
    }

    private readonly IHevInterfaceMetricBackend _backend =
        backend ?? throw new ArgumentNullException(nameof(backend));

    public async Task ApplyAsync(
        int interfaceIndex,
        IEnumerable<AddressFamily> families,
        string journalPath,
        int tunnelMetric = 1,
        CancellationToken cancellationToken = default)
    {
        if (interfaceIndex <= 0) throw new ArgumentOutOfRangeException(nameof(interfaceIndex));
        if (tunnelMetric < 1) throw new ArgumentOutOfRangeException(nameof(tunnelMetric));
        ArgumentException.ThrowIfNullOrWhiteSpace(journalPath);
        if (File.Exists(journalPath))
            throw new InvalidOperationException("A Hev interface-metric recovery journal already exists.");

        var requested = families.Distinct().ToArray();
        if (requested.Length == 0 || requested.Any(x => x is not (AddressFamily.InterNetwork or AddressFamily.InterNetworkV6)))
            throw new ArgumentException("At least one supported IP address family is required.", nameof(families));

        var journal = new Journal();
        foreach (var family in requested)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var original = await _backend.GetAsync(interfaceIndex, family, cancellationToken);
            if (original < 1) throw new InvalidDataException($"Windows returned an invalid {family} interface metric.");
            journal.Entries.Add(new MetricEntry
            {
                InterfaceIndex = interfaceIndex,
                Family = family,
                OriginalMetric = original,
                TunnelMetric = tunnelMetric
            });
        }

        await SaveAsync(journalPath, journal, cancellationToken);
        try
        {
            foreach (var entry in journal.Entries)
            {
                cancellationToken.ThrowIfCancellationRequested();
                await _backend.SetAsync(entry.InterfaceIndex, entry.Family, entry.TunnelMetric, cancellationToken);
            }
        }
        catch
        {
            try { await RestoreAsync(journal, journalPath, CancellationToken.None); }
            catch (Exception ex) { Logging.SaveLog("HevInterfaceMetricTransaction.Rollback", ex); }
            throw;
        }
    }

    public async Task RecoverAsync(string journalPath, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(journalPath);
        if (!File.Exists(journalPath)) return;
        var journal = JsonSerializer.Deserialize<Journal>(await File.ReadAllTextAsync(journalPath, cancellationToken))
            ?? throw new InvalidDataException("The Hev interface-metric journal is invalid.");
        if (journal.Entries.Any(x => x.InterfaceIndex <= 0 || x.OriginalMetric < 1
            || x.Family is not (AddressFamily.InterNetwork or AddressFamily.InterNetworkV6)))
            throw new InvalidDataException("The Hev interface-metric journal contains invalid entries.");
        await RestoreAsync(journal, journalPath, cancellationToken);
    }

    private async Task RestoreAsync(Journal journal, string journalPath, CancellationToken cancellationToken)
    {
        foreach (var entry in journal.Entries.AsEnumerable().Reverse())
        {
            cancellationToken.ThrowIfCancellationRequested();
            await _backend.SetAsync(entry.InterfaceIndex, entry.Family, entry.OriginalMetric, cancellationToken);
        }
        File.Delete(journalPath);
    }

    private static async Task SaveAsync(string path, Journal journal, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        var temp = path + ".tmp";
        await File.WriteAllTextAsync(temp, JsonSerializer.Serialize(journal), new UTF8Encoding(false), cancellationToken);
        File.Move(temp, path, overwrite: true);
    }
}

/// <summary>Windows implementation for reading and setting one adapter's family-specific metric.</summary>
public sealed class WindowsHevInterfaceMetricBackend : IHevInterfaceMetricBackend
{
    public async Task<int> GetAsync(int interfaceIndex, AddressFamily family, CancellationToken cancellationToken)
    {
        EnsureWindows();
        Validate(interfaceIndex, family);
        var output = await RunAsync(
            $"(Get-NetIPInterface -InterfaceIndex {interfaceIndex} -AddressFamily '{FamilyName(family)}' -ErrorAction Stop).InterfaceMetric",
            cancellationToken);
        return int.TryParse(output.Trim(), out var metric)
            ? metric
            : throw new InvalidDataException("Unable to read the Hev adapter interface metric.");
    }

    public async Task SetAsync(int interfaceIndex, AddressFamily family, int metric, CancellationToken cancellationToken)
    {
        EnsureWindows();
        Validate(interfaceIndex, family);
        if (metric < 1) throw new ArgumentOutOfRangeException(nameof(metric));
        await RunAsync(
            $"Set-NetIPInterface -InterfaceIndex {interfaceIndex} -AddressFamily '{FamilyName(family)}' -InterfaceMetric {metric} -ErrorAction Stop",
            cancellationToken);
    }

    private static string FamilyName(AddressFamily family) => family switch
    {
        AddressFamily.InterNetwork => "IPv4",
        AddressFamily.InterNetworkV6 => "IPv6",
        _ => throw new ArgumentOutOfRangeException(nameof(family))
    };

    private static void Validate(int index, AddressFamily family)
    {
        if (index <= 0) throw new ArgumentOutOfRangeException(nameof(index));
        _ = FamilyName(family);
    }

    private static void EnsureWindows()
    {
        if (!OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException("Hev interface metrics are available only on Windows.");
    }

    private static async Task<string> RunAsync(string script, CancellationToken cancellationToken)
    {
        using var identity = WindowsIdentity.GetCurrent();
        if (!new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator))
            throw new UnauthorizedAccessException("Changing Windows interface metrics requires Administrator privileges.");

        var encoded = Convert.ToBase64String(Encoding.Unicode.GetBytes("$ErrorActionPreference = 'Stop'; " + script));
        var info = new ProcessStartInfo
        {
            FileName = "powershell.exe",
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        info.ArgumentList.Add("-NoLogo");
        info.ArgumentList.Add("-NoProfile");
        info.ArgumentList.Add("-NonInteractive");
        info.ArgumentList.Add("-EncodedCommand");
        info.ArgumentList.Add(encoded);

        using var process = new Process { StartInfo = info };
        if (!process.Start()) throw new InvalidOperationException("Unable to start Windows PowerShell.");
        var stdout = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var stderr = process.StandardError.ReadToEndAsync(cancellationToken);
        try { await process.WaitForExitAsync(cancellationToken); }
        catch { try { process.Kill(true); } catch { } throw; }
        var output = await stdout;
        var error = await stderr;
        if (process.ExitCode != 0)
            throw new InvalidOperationException($"Windows interface-metric command failed ({process.ExitCode}): {error.Trim()}");
        return output;
    }
}
