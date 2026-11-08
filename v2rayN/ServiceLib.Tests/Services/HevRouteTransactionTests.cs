namespace ServiceLib.Tests.Services;

public class HevRouteTransactionTests
{
    [Test]
    public async Task AppliesRoutesInPlanOrderAndRemovesJournalAfterSuccess()
    {
        var backend = new FakeRouteBackend();
        var transaction = new HevRouteTransaction(backend);
        var journal = NewJournalPath();
        try
        {
            var routes = CreateRoutes();
            await transaction.ApplyAsync(routes, journal);

            await backend.Added.Should().BeEqualTo(routes.Select(x => x.DestinationPrefix).ToList());
            await File.Exists(journal).Should().BeTrue();

            await transaction.RecoverAsync(journal);
            await backend.Present.Count.Should().BeEqualTo(0);
            await File.Exists(journal).Should().BeFalse();
        }
        finally
        {
            DeleteJournal(journal);
        }
    }

    [Test]
    public async Task RollsBackEarlierRoutesWhenAnAdditionFails()
    {
        var backend = new FakeRouteBackend { FailOnDestination = "0.0.0.0/0" };
        var transaction = new HevRouteTransaction(backend);
        var journal = NewJournalPath();
        try
        {
            var threw = false;
            try
            {
                await transaction.ApplyAsync(CreateRoutes(), journal);
            }
            catch (InvalidOperationException)
            {
                threw = true;
            }

            await threw.Should().BeTrue();
            await backend.Present.Count.Should().BeEqualTo(0);
            await File.Exists(journal).Should().BeFalse();
        }
        finally
        {
            DeleteJournal(journal);
        }
    }

    [Test]
    public async Task RecoversAnInFlightRouteAfterProcessCrash()
    {
        var backend = new FakeRouteBackend();
        var route = CreateRoutes()[0];
        await backend.AddAsync(route, CancellationToken.None);
        var journal = NewJournalPath();
        try
        {
            var serialized = $$"""
                {
                  "Routes": [{
                    "DestinationPrefix": "{{route.DestinationPrefix}}",
                    "InterfaceIndex": {{route.InterfaceIndex}},
                    "NextHop": "{{route.NextHop}}",
                    "RouteMetric": {{route.RouteMetric}},
                    "IsUpstreamBypass": true
                  }],
                  "InFlightIndex": 0,
                  "AppliedCount": 0
                }
                """;
            await File.WriteAllTextAsync(journal, serialized);

            await new HevRouteTransaction(backend).RecoverAsync(journal);

            await backend.Present.Count.Should().BeEqualTo(0);
            await File.Exists(journal).Should().BeFalse();
        }
        finally
        {
            DeleteJournal(journal);
        }
    }

    [Test]
    public async Task RefusesToApplyWhenARecoveryJournalAlreadyExists()
    {
        var backend = new FakeRouteBackend();
        var journal = NewJournalPath();
        try
        {
            await File.WriteAllTextAsync(journal, "{}");
            var threw = false;
            try
            {
                await new HevRouteTransaction(backend).ApplyAsync(CreateRoutes(), journal);
            }
            catch (InvalidOperationException)
            {
                threw = true;
            }

            await threw.Should().BeTrue();
            await backend.Present.Count.Should().BeEqualTo(0);
        }
        finally
        {
            DeleteJournal(journal);
        }
    }

    private static List<HevRouteAction> CreateRoutes() =>
    [
        new("203.0.113.20/32", 12, IPAddress.Parse("192.0.2.1"), 1, true),
        new("0.0.0.0/0", 77, IPAddress.Any, 1, false)
    ];

    private static string NewJournalPath() =>
        Path.Combine(Path.GetTempPath(), $"pepen-hev-route-{Guid.NewGuid():N}.json");

    private static void DeleteJournal(string path)
    {
        if (File.Exists(path)) File.Delete(path);
        if (File.Exists(path + ".tmp")) File.Delete(path + ".tmp");
    }

    private sealed class FakeRouteBackend : IHevRouteBackend
    {
        public HashSet<string> Present { get; } = [];
        public List<string> Added { get; } = [];
        public string? FailOnDestination { get; init; }

        public Task<bool> ExistsAsync(HevRouteAction route, CancellationToken cancellationToken) =>
            Task.FromResult(Present.Contains(route.DestinationPrefix));

        public Task AddAsync(HevRouteAction route, CancellationToken cancellationToken)
        {
            if (route.DestinationPrefix == FailOnDestination)
                throw new InvalidOperationException("Simulated route add failure.");
            Present.Add(route.DestinationPrefix);
            Added.Add(route.DestinationPrefix);
            return Task.CompletedTask;
        }

        public Task RemoveAsync(HevRouteAction route, CancellationToken cancellationToken)
        {
            Present.Remove(route.DestinationPrefix);
            return Task.CompletedTask;
        }
    }
}
