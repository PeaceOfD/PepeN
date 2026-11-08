using ServiceLib.Services;

namespace ServiceLib.Tests.Services;

public class HevNetworkTransactionTests
{
    [Test]
    public async Task AppliesMetricsBeforeRoutesAndRestoresBothOnStop()
    {
        var order = new List<string>();
        var routeBackend = new FakeRouteBackend(order);
        var metricBackend = new FakeMetricBackend(order);
        var routeJournal = NewPath("routes");
        var metricJournal = NewPath("metrics");
        try
        {
            var transaction = new HevNetworkTransaction(
                new HevRouteTransaction(routeBackend),
                new HevInterfaceMetricTransaction(metricBackend));
            await transaction.ApplyAsync(
                [new("0.0.0.0/0", 77, IPAddress.Any, 1, false)],
                77, [AddressFamily.InterNetwork], routeJournal, metricJournal);

            await order[0].Should().BeEqualTo("metric:1");
            await order[1].Should().BeEqualTo("route:add");
            await File.Exists(routeJournal).Should().BeTrue();
            await File.Exists(metricJournal).Should().BeTrue();

            await transaction.StopAsync(routeJournal, metricJournal);
            await routeBackend.Present.Should().BeFalse();
            await metricBackend.Value.Should().BeEqualTo(35);
            await File.Exists(routeJournal).Should().BeFalse();
            await File.Exists(metricJournal).Should().BeFalse();
        }
        finally { Cleanup(routeJournal); Cleanup(metricJournal); }
    }

    [Test]
    public async Task StartupRecoveryRestoresBothJournalsIdempotently()
    {
        var order = new List<string>();
        var routeBackend = new FakeRouteBackend(order) { Present = true };
        var metricBackend = new FakeMetricBackend(order) { Value = 1 };
        var routeJournal = NewPath("routes");
        var metricJournal = NewPath("metrics");
        try
        {
            await File.WriteAllTextAsync(routeJournal, """
            {"Routes":[{"DestinationPrefix":"0.0.0.0/0","InterfaceIndex":77,"NextHop":"0.0.0.0","RouteMetric":1,"IsUpstreamBypass":false}],"InFlightIndex":-1,"AppliedCount":1}
            """);
            await File.WriteAllTextAsync(metricJournal, """
            {"Entries":[{"InterfaceIndex":77,"Family":2,"OriginalMetric":35,"TunnelMetric":1}]}
            """);
            var transaction = new HevNetworkTransaction(
                new HevRouteTransaction(routeBackend),
                new HevInterfaceMetricTransaction(metricBackend));
            await transaction.RecoverAsync(routeJournal, metricJournal);
            await routeBackend.Present.Should().BeFalse();
            await metricBackend.Value.Should().BeEqualTo(35);
            await order[0].Should().BeEqualTo("route:remove");
            await order[1].Should().BeEqualTo("metric:35");
        }
        finally { Cleanup(routeJournal); Cleanup(metricJournal); }
    }

    private static string NewPath(string kind) =>
        Path.Combine(Path.GetTempPath(), $"pepen-hev-{kind}-{Guid.NewGuid():N}.json");
    private static void Cleanup(string path)
    {
        if (File.Exists(path)) File.Delete(path);
        if (File.Exists(path + ".tmp")) File.Delete(path + ".tmp");
    }

    private sealed class FakeRouteBackend(List<string> order) : IHevRouteBackend
    {
        public bool Present { get; set; }
        public Task<bool> ExistsAsync(HevRouteAction route, CancellationToken token) => Task.FromResult(Present);
        public Task AddAsync(HevRouteAction route, CancellationToken token)
        {
            order.Add("route:add");
            Present = true;
            return Task.CompletedTask;
        }
        public Task RemoveAsync(HevRouteAction route, CancellationToken token)
        {
            order.Add("route:remove");
            Present = false;
            return Task.CompletedTask;
        }
    }

    private sealed class FakeMetricBackend(List<string> order) : IHevInterfaceMetricBackend
    {
        public int Value { get; set; } = 35;
        public Task<int> GetAsync(int index, AddressFamily family, CancellationToken token) => Task.FromResult(Value);
        public Task SetAsync(int index, AddressFamily family, int metric, CancellationToken token)
        {
            Value = metric;
            order.Add($"metric:{metric}");
            return Task.CompletedTask;
        }
    }
}
