using System.Security.Principal;

namespace ServiceLib.Services;

/// <summary>
/// Windows PowerShell-backed route executor. It mutates only the exact routes supplied by the
/// transaction journal; it never deletes or replaces pre-existing default routes.
/// </summary>
public sealed class WindowsHevRouteBackend : IHevRouteBackend
{
    public async Task<IReadOnlyList<HevPhysicalDefaultRoute>> GetPhysicalDefaultRoutesAsync(
        CancellationToken cancellationToken = default)
    {
        EnsureWindows();
        const string script = @"
$routes = @(Get-NetRoute -PolicyStore ActiveStore -ErrorAction Stop |
    Where-Object { $_.DestinationPrefix -in @('0.0.0.0/0', '::/0') })
$result = @($routes | ForEach-Object {
    $adapter = Get-NetAdapter -InterfaceIndex $_.ifIndex -ErrorAction SilentlyContinue
    if ($null -eq $adapter -or -not $adapter.HardwareInterface -or $adapter.Status -ne 'Up') { return }
    $family = if ($_.DestinationPrefix -eq '::/0') { 'IPv6' } else { 'IPv4' }
    $interface = Get-NetIPInterface -InterfaceIndex $_.ifIndex -AddressFamily $family -ErrorAction Stop
    [pscustomobject]@{
        InterfaceIndex = [int]$_.ifIndex
        NextHop = [string]$_.NextHop
        RouteMetric = [int]$_.RouteMetric
        InterfaceMetric = [int]$interface.InterfaceMetric
    }
})
ConvertTo-Json -InputObject $result -Compress -Depth 3
";
        var output = await RunPowerShellAsync(script, cancellationToken);
        using var document = JsonDocument.Parse(output);
        JsonElement[] elements;
        if (document.RootElement.ValueKind == JsonValueKind.Array)
            elements = document.RootElement.EnumerateArray().ToArray();
        else
            elements = new[] { document.RootElement };

        var routes = new List<HevPhysicalDefaultRoute>();
        foreach (var item in elements)
        {
            if (!IPAddress.TryParse(item.GetProperty("NextHop").GetString(), out var nextHop))
                continue;
            routes.Add(new HevPhysicalDefaultRoute(
                item.GetProperty("InterfaceIndex").GetInt32(),
                nextHop,
                item.GetProperty("RouteMetric").GetInt32(),
                item.GetProperty("InterfaceMetric").GetInt32()));
        }

        return routes;
    }

    public async Task<bool> ExistsAsync(HevRouteAction route, CancellationToken cancellationToken)
    {
        ValidateRoute(route);
        var script = $@"
$route = Get-NetRoute -DestinationPrefix '{Escape(route.DestinationPrefix)}' -InterfaceIndex {route.InterfaceIndex} -PolicyStore ActiveStore -ErrorAction SilentlyContinue |
    Where-Object {{ $_.NextHop -eq '{Escape(route.NextHop.ToString())}' -and $_.RouteMetric -eq {route.RouteMetric} }} |
    Select-Object -First 1
if ($null -ne $route) {{ 'true' }} else {{ 'false' }}
";
        var output = await RunPowerShellAsync(script, cancellationToken);
        return output.Trim().Equals("true", StringComparison.OrdinalIgnoreCase);
    }

    public async Task AddAsync(HevRouteAction route, CancellationToken cancellationToken)
    {
        EnsureAdministrator();
        ValidateRoute(route);
        var script = $@"
New-NetRoute -DestinationPrefix '{Escape(route.DestinationPrefix)}' -InterfaceIndex {route.InterfaceIndex} -NextHop '{Escape(route.NextHop.ToString())}' -RouteMetric {route.RouteMetric} -PolicyStore ActiveStore -ErrorAction Stop | Out-Null
";
        await RunPowerShellAsync(script, cancellationToken);
    }

    public async Task RemoveAsync(HevRouteAction route, CancellationToken cancellationToken)
    {
        EnsureAdministrator();
        ValidateRoute(route);
        var script = $@"
Get-NetRoute -DestinationPrefix '{Escape(route.DestinationPrefix)}' -InterfaceIndex {route.InterfaceIndex} -PolicyStore ActiveStore -ErrorAction SilentlyContinue |
    Where-Object {{ $_.NextHop -eq '{Escape(route.NextHop.ToString())}' -and $_.RouteMetric -eq {route.RouteMetric} }} |
    Remove-NetRoute -Confirm:$false -ErrorAction Stop
";
        await RunPowerShellAsync(script, cancellationToken);
    }

    private static void ValidateRoute(HevRouteAction route)
    {
        ArgumentNullException.ThrowIfNull(route);
        if (route.InterfaceIndex <= 0 || route.RouteMetric < 0)
            throw new ArgumentOutOfRangeException(nameof(route));
        var parts = route.DestinationPrefix.Split('/');
        if (parts.Length != 2 || !IPAddress.TryParse(parts[0], out var destination)
            || !int.TryParse(parts[1], out var prefixLength)
            || prefixLength < 0
            || prefixLength > (destination.AddressFamily == AddressFamily.InterNetwork ? 32 : 128)
            || destination.AddressFamily != route.NextHop.AddressFamily)
            throw new ArgumentException("The route destination and next hop must be valid and use the same address family.", nameof(route));
    }

    private static string Escape(string value) => value.Replace("'", "''", StringComparison.Ordinal);

    private static void EnsureWindows()
    {
        if (!OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException("The Hev route backend is available only on Windows.");
    }

    private static void EnsureAdministrator()
    {
        if (!OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException("The Hev route backend is available only on Windows.");
        using var identity = WindowsIdentity.GetCurrent();
        var principal = new WindowsPrincipal(identity);
        if (!principal.IsInRole(WindowsBuiltInRole.Administrator))
            throw new UnauthorizedAccessException("Changing Windows routes requires running PepeN as Administrator.");
    }

    private static async Task<string> RunPowerShellAsync(string script, CancellationToken cancellationToken)
    {
        EnsureWindows();
        var encoded = Convert.ToBase64String(Encoding.Unicode.GetBytes("$ErrorActionPreference = 'Stop'; " + script));
        var startInfo = new ProcessStartInfo
        {
            FileName = "powershell.exe",
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        startInfo.ArgumentList.Add("-NoLogo");
        startInfo.ArgumentList.Add("-NoProfile");
        startInfo.ArgumentList.Add("-NonInteractive");
        startInfo.ArgumentList.Add("-EncodedCommand");
        startInfo.ArgumentList.Add(encoded);

        using var process = new Process { StartInfo = startInfo };
        if (!process.Start())
            throw new InvalidOperationException("Unable to start Windows PowerShell.");

        var stdout = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var stderr = process.StandardError.ReadToEndAsync(cancellationToken);
        try
        {
            await process.WaitForExitAsync(cancellationToken);
        }
        catch
        {
            try { process.Kill(true); } catch { }
            throw;
        }

        var output = await stdout;
        var error = await stderr;
        if (process.ExitCode != 0)
            throw new InvalidOperationException($"Windows route command failed ({process.ExitCode}): {error.Trim()}");
        return output.Trim();
    }
}
