using System.Net;
using System.Net.Sockets;

namespace ServiceLib.Services;

/// <summary>
/// A physical default route observed before Hev is activated.
/// InterfaceMetric is included because Windows compares the sum of route and interface metrics.
/// </summary>
public sealed record HevPhysicalDefaultRoute(
    int InterfaceIndex,
    IPAddress NextHop,
    int RouteMetric,
    int InterfaceMetric);

/// <summary>
/// A route to add during Hev activation. Upstream bypasses must be applied before tunnel defaults.
/// </summary>
public sealed record HevRouteAction(
    string DestinationPrefix,
    int InterfaceIndex,
    IPAddress NextHop,
    int RouteMetric,
    bool IsUpstreamBypass);

/// <summary>
/// Pure, side-effect-free route planner. It deliberately does not execute commands or mutate
/// Windows networking; a platform route executor must apply the returned actions transactionally.
/// </summary>
public static class HevRoutePlan
{
    public static IReadOnlyList<HevRouteAction> Create(
        int tunnelInterfaceIndex,
        IEnumerable<HevPhysicalDefaultRoute> physicalDefaults,
        IEnumerable<IPAddress> upstreamAddresses,
        bool includeIpv6)
    {
        if (tunnelInterfaceIndex <= 0)
            throw new ArgumentOutOfRangeException(nameof(tunnelInterfaceIndex));

        ArgumentNullException.ThrowIfNull(physicalDefaults);
        ArgumentNullException.ThrowIfNull(upstreamAddresses);

        var upstreams = upstreamAddresses
            .Where(IsUsableUnicastAddress)
            .Distinct()
            .ToArray();
        if (upstreams.Length == 0)
            throw new ArgumentException("At least one resolved upstream server IP is required.", nameof(upstreamAddresses));

        var defaults = physicalDefaults
            .Where(route => route.InterfaceIndex > 0
                && route.InterfaceIndex != tunnelInterfaceIndex
                && route.RouteMetric >= 0
                && route.InterfaceMetric >= 0
                && IsUsableGateway(route.NextHop))
            .ToArray();

        var families = includeIpv6
            ? new[] { AddressFamily.InterNetwork, AddressFamily.InterNetworkV6 }
            : new[] { AddressFamily.InterNetwork };

        var selected = new Dictionary<AddressFamily, HevPhysicalDefaultRoute>();
        foreach (var family in families)
        {
            var route = defaults
                .Where(item => item.NextHop.AddressFamily == family)
                .OrderBy(item => (long)item.RouteMetric + item.InterfaceMetric)
                .ThenBy(item => item.InterfaceIndex)
                .FirstOrDefault();

            if (route is null)
                throw new InvalidOperationException($"No usable physical {FamilyName(family)} default route was found.");

            if (!upstreams.Any(address => address.AddressFamily == family))
                throw new InvalidOperationException(
                    $"No resolved {FamilyName(family)} upstream IP is available to bypass the Hev tunnel.");
            selected.Add(family, route);
        }

        var actions = new List<HevRouteAction>();

        // Critical ordering: preserve a direct path to every proxy endpoint before adding defaults.
        foreach (var upstream in upstreams.Where(address => selected.ContainsKey(address.AddressFamily)))
        {
            var physical = selected[upstream.AddressFamily];
            var prefixLength = upstream.AddressFamily == AddressFamily.InterNetwork ? 32 : 128;
            actions.Add(new HevRouteAction(
                $"{upstream}/{prefixLength}",
                physical.InterfaceIndex,
                physical.NextHop,
                1,
                IsUpstreamBypass: true));
        }

        foreach (var family in families)
        {
            var nextHop = family == AddressFamily.InterNetwork ? IPAddress.Any : IPAddress.IPv6Any;
            var prefix = family == AddressFamily.InterNetwork ? "0.0.0.0/0" : "::/0";
            actions.Add(new HevRouteAction(
                prefix,
                tunnelInterfaceIndex,
                nextHop,
                1,
                IsUpstreamBypass: false));
        }

        return actions;
    }

    private static bool IsUsableGateway(IPAddress address)
    {
        if (address.Equals(IPAddress.Any) || address.Equals(IPAddress.IPv6Any)
            || IPAddress.IsLoopback(address))
            return false;

        return address.AddressFamily is AddressFamily.InterNetwork or AddressFamily.InterNetworkV6;
    }

    private static bool IsUsableUnicastAddress(IPAddress address)
    {
        if (IPAddress.IsLoopback(address)
            || address.Equals(IPAddress.Any)
            || address.Equals(IPAddress.IPv6Any))
            return false;

        if (address.AddressFamily == AddressFamily.InterNetworkV6)
            return !address.IsIPv6Multicast && !address.IsIPv6LinkLocal;

        if (address.AddressFamily != AddressFamily.InterNetwork)
            return false;

        var bytes = address.GetAddressBytes();
        return bytes[0] is not (0 or 127 or >= 224);
    }

    private static string FamilyName(AddressFamily family) =>
        family == AddressFamily.InterNetwork ? "IPv4" : "IPv6";
}
