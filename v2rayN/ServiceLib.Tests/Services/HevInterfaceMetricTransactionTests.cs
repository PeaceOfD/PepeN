using System.Net.Sockets;
using ServiceLib.Services;

namespace ServiceLib.Tests.Services;

public class HevInterfaceMetricTransactionTests
{
    [Test]
    public async Task AppliesTunnelMetricAndRestoresOriginalIpv4AndIpv6Metrics()
    {
        var backend = new FakeMetricBackend();
        var path = NewJournal();
        try
        {
            var transaction = new HevInterfaceMetricTransaction(backend);
            await transaction.ApplyAsync(77, [AddressFamily.InterNetwork, AddressFamily.InterNetworkV6], path);
            await backend.Values[(77, AddressFamily.InterNetwork)].Should().BeEqualTo(1);
            await backend.Values[(77, AddressFamily.InterNetworkV6)].Should().BeEqualTo(1);
            await File.Exists(path).Should().BeTrue();

            await transaction.RecoverAsync(path);
            await backend.Values[(77, AddressFamily.InterNetwork)].Should().BeEqualTo(35);
            await backend.Values[(77, AddressFamily.InterNetworkV6)].Should().BeEqualTo(45);
            await File.Exists(path).Should().BeFalse();
        }
        finally { Cleanup(path); }
    }

    [Test]
    public async Task RecoveryRestoresMetricsAfterSimulatedCrash()
    {
        var backend = new FakeMetricBackend();
        var path = NewJournal();
        try
        {
            await File.WriteAllTextAsync(path, """
            {"Entries":[
              {"InterfaceIndex":77,"Family":2,"OriginalMetric":35,"TunnelMetric":1},
              {"InterfaceIndex":77,"Family":23,"OriginalMetric":45,"TunnelMetric":1}
            ]}
            """);
            var transaction = new HevInterfaceMetricTransaction(backend);
            await transaction.RecoverAsync(path);
            await backend.Values[(77, AddressFamily.InterNetwork)].Should().BeEqualTo(35);
            await backend.Values[(77, AddressFamily.InterNetworkV6)].Should().BeEqualTo(45);
            await File.Exists(path).Should().BeFalse();
        }
        finally { Cleanup(path); }
    }

    [Test]
    public async Task FailedMetricChangeAttemptsToRestoreEveryOriginalValue()
    {
        var backend = new FakeMetricBackend { FailOn = AddressFamily.InterNetworkV6 };
        var path = NewJournal();
        try
        {
            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                new HevInterfaceMetricTransaction(backend).ApplyAsync(
                    77, [AddressFamily.InterNetwork, AddressFamily.InterNetworkV6], path));
            await backend.Values[(77, AddressFamily.InterNetwork)].Should().BeEqualTo(35);
            await backend.Values[(77, AddressFamily.InterNetworkV6)].Should().BeEqualTo(45);
            await File.Exists(path).Should().BeFalse();
        }
        finally { Cleanup(path); }
    }

    private static string NewJournal() => Path.Combine(Path.GetTempPath(), $"pepen-hev-metric-{Guid.NewGuid():N}.json");
    private static void Cleanup(string path)
    {
        if (File.Exists(path)) File.Delete(path);
        if (File.Exists(path + ".tmp")) File.Delete(path + ".tmp");
    }

    private sealed class FakeMetricBackend : IHevInterfaceMetricBackend
    {
        public Dictionary<(int, AddressFamily), int> Values { get; } = new()
        {
            [(77, AddressFamily.InterNetwork)] = 35,
            [(77, AddressFamily.InterNetworkV6)] = 45
        };
        public AddressFamily? FailOn { get; init; }

        public Task<int> GetAsync(int index, AddressFamily family, CancellationToken token) =>
            Task.FromResult(Values[(index, family)]);

        public Task SetAsync(int index, AddressFamily family, int metric, CancellationToken token)
        {
            if (family == FailOn && metric == 1)
                throw new InvalidOperationException("Simulated metric update failure.");
            Values[(index, family)] = metric;
            return Task.CompletedTask;
        }
    }
}
