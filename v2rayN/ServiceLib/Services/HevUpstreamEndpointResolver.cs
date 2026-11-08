using System.Net;
using System.Net.Sockets;
using System.Text.Json;

namespace ServiceLib.Services;

/// <summary>
/// Finds the remote proxy endpoints in a generated Xray or sing-box configuration and resolves
/// their hostnames before the TUN default routes are installed. It intentionally reads only
/// known endpoint fields inside the outbound list; SNI, HTTP Host, DNS names and rule domains
/// are not treated as route destinations.
/// </summary>
public static class HevUpstreamEndpointResolver
{
    public static async Task<IReadOnlyList<IPAddress>> ResolveAsync(
        string generatedConfig,
        CancellationToken cancellationToken = default,
        Func<string, CancellationToken, Task<IPAddress[]>>? resolveHost = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(generatedConfig);
        using var document = JsonDocument.Parse(generatedConfig);
        if (!document.RootElement.TryGetProperty("outbounds", out var outbounds)
            || outbounds.ValueKind != JsonValueKind.Array)
        {
            throw new InvalidDataException("The generated core configuration has no outbound array.");
        }

        var hosts = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var outbound in outbounds.EnumerateArray())
        {
            if (outbound.ValueKind != JsonValueKind.Object)
                continue;

            // sing-box proxy outbounds (vmess, vless, trojan, shadowsocks, hysteria, tuic, socks, etc.)
            AddStringProperty(outbound, "server", hosts);

            // Xray proxy outbounds. The same structures also cover custom Xray-compatible outbounds.
            if (outbound.TryGetProperty("settings", out var settings)
                && settings.ValueKind == JsonValueKind.Object)
            {
                AddStringProperty(settings, "address", hosts);
                AddAddressArray(settings, "vnext", hosts);
                AddAddressArray(settings, "servers", hosts);
            }
        }

        if (hosts.Count == 0)
            throw new InvalidDataException("No remote proxy endpoint was found in the generated core configuration.");

        resolveHost ??= static (host, token) => Dns.GetHostAddressesAsync(host, token);
        var addresses = new HashSet<IPAddress>();
        foreach (var host in hosts)
        {
            cancellationToken.ThrowIfCancellationRequested();
            IPAddress[] resolved;
            if (IPAddress.TryParse(TrimBrackets(host), out var literal))
            {
                resolved = [literal];
            }
            else
            {
                // Fail closed: a missing DNS answer for even one configured outbound could leave
                // that endpoint without a physical bypass route once the tunnel default is active.
                resolved = await resolveHost(host, cancellationToken);
            }

            foreach (var address in resolved)
            {
                if (IsUsableEndpoint(address))
                    addresses.Add(address);
            }
        }

        if (addresses.Count == 0)
            throw new InvalidDataException("The configured proxy endpoints did not resolve to any usable unicast IP address.");

        return addresses.OrderBy(address => address.AddressFamily).ThenBy(address => address.ToString(), StringComparer.Ordinal).ToArray();
    }

    private static void AddStringProperty(JsonElement element, string name, ISet<string> hosts)
    {
        if (element.TryGetProperty(name, out var value)
            && value.ValueKind == JsonValueKind.String
            && !string.IsNullOrWhiteSpace(value.GetString()))
        {
            hosts.Add(value.GetString()!.Trim());
        }
    }

    private static void AddAddressArray(JsonElement settings, string name, ISet<string> hosts)
    {
        if (!settings.TryGetProperty(name, out var values) || values.ValueKind != JsonValueKind.Array)
            return;

        foreach (var item in values.EnumerateArray())
        {
            if (item.ValueKind == JsonValueKind.Object)
                AddStringProperty(item, "address", hosts);
        }
    }

    private static string TrimBrackets(string host) =>
        host.Length > 2 && host[0] == '[' && host[^1] == ']' ? host[1..^1] : host;

    private static bool IsUsableEndpoint(IPAddress address)
    {
        if (IPAddress.IsLoopback(address)
            || address.Equals(IPAddress.Any)
            || address.Equals(IPAddress.IPv6Any))
            return false;

        if (address.AddressFamily == AddressFamily.InterNetworkV6)
            return !address.IsIPv6Multicast && !address.IsIPv6LinkLocal;

        if (address.AddressFamily != AddressFamily.InterNetwork)
            return false;

        var first = address.GetAddressBytes()[0];
        return first is not (0 or >= 224);
    }
}
