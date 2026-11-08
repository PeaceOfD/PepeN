namespace ServiceLib.Services;

/// <summary>
/// Coordinates the independent route and interface-metric journals as one Hev network session.
/// The metric is lowered before tunnel default routes are installed. Recovery removes owned
/// routes first, then restores the adapter's original metrics.
/// </summary>
public sealed class HevNetworkTransaction(
    HevRouteTransaction routes,
    HevInterfaceMetricTransaction metrics)
{
    private readonly HevRouteTransaction _routes = routes ?? throw new ArgumentNullException(nameof(routes));
    private readonly HevInterfaceMetricTransaction _metrics = metrics ?? throw new ArgumentNullException(nameof(metrics));

    public async Task ApplyAsync(
        IReadOnlyList<HevRouteAction> routePlan,
        int tunnelInterfaceIndex,
        IEnumerable<AddressFamily> addressFamilies,
        string routeJournalPath,
        string metricJournalPath,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(routePlan);
        ArgumentNullException.ThrowIfNull(addressFamilies);
        ArgumentException.ThrowIfNullOrWhiteSpace(routeJournalPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(metricJournalPath);
        if (string.Equals(Path.GetFullPath(routeJournalPath), Path.GetFullPath(metricJournalPath), StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Route and metric journals must use different paths.");

        await _metrics.ApplyAsync(tunnelInterfaceIndex, addressFamilies, metricJournalPath,
            cancellationToken: cancellationToken);
        try
        {
            await _routes.ApplyAsync(routePlan, routeJournalPath, cancellationToken);
        }
        catch
        {
            // The route transaction has already attempted its own rollback. Restore the original
            // interface metrics only after that attempt; if restoration fails, its journal remains.
            try { await _metrics.RecoverAsync(metricJournalPath, CancellationToken.None); }
            catch (Exception recoveryError) { Logging.SaveLog("HevNetworkTransaction.MetricRollback", recoveryError); }
            throw;
        }
    }

    /// <summary>Use before any new Hev network mutation on application startup.</summary>
    public async Task RecoverAsync(
        string routeJournalPath,
        string metricJournalPath,
        CancellationToken cancellationToken = default)
    {
        await _routes.RecoverAsync(routeJournalPath, cancellationToken);
        await _metrics.RecoverAsync(metricJournalPath, cancellationToken);
    }

    /// <summary>Restore routes before metrics when ending a live Hev session.</summary>
    public Task StopAsync(
        string routeJournalPath,
        string metricJournalPath,
        CancellationToken cancellationToken = default) =>
        RecoverAsync(routeJournalPath, metricJournalPath, cancellationToken);
}
