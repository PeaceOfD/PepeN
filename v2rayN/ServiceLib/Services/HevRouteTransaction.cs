namespace ServiceLib.Services;

/// <summary>
/// Minimal route backend contract so the transaction and crash-recovery behavior can be tested
/// without changing the host machine's routing table.
/// </summary>
public interface IHevRouteBackend
{
    Task<bool> ExistsAsync(HevRouteAction route, CancellationToken cancellationToken);
    Task AddAsync(HevRouteAction route, CancellationToken cancellationToken);
    Task RemoveAsync(HevRouteAction route, CancellationToken cancellationToken);
}

/// <summary>
/// Applies planned routes transactionally and persists intent before each OS mutation.
/// The journal is intentionally retained if rollback/recovery cannot fully complete.
/// </summary>
public sealed class HevRouteTransaction(IHevRouteBackend backend)
{
    private sealed class Journal
    {
        public List<HevRouteAction> Routes { get; set; } = [];
        public int InFlightIndex { get; set; } = -1;
        public int AppliedCount { get; set; }
    }

    private readonly IHevRouteBackend _backend = backend ?? throw new ArgumentNullException(nameof(backend));
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Converters = { new IPAddressConverter() }
    };

    private sealed class IPAddressConverter : JsonConverter<IPAddress>
    {
        public override IPAddress Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            var value = reader.GetString();
            return IPAddress.TryParse(value, out var address)
                ? address
                : throw new JsonException("Invalid IP address in the Hev route journal.");
        }

        public override void Write(Utf8JsonWriter writer, IPAddress value, JsonSerializerOptions options) =>
            writer.WriteStringValue(value.ToString());
    }

    public async Task ApplyAsync(
        IReadOnlyList<HevRouteAction> routes,
        string journalPath,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(routes);
        ArgumentException.ThrowIfNullOrWhiteSpace(journalPath);
        ValidateOrder(routes);

        if (File.Exists(journalPath))
            throw new InvalidOperationException("A Hev route recovery journal already exists. Recover it before applying new routes.");

        foreach (var route in routes)
        {
            if (await _backend.ExistsAsync(route, cancellationToken))
                throw new InvalidOperationException($"Refusing to take ownership of an existing route: {route.DestinationPrefix}.");
        }

        var journal = new Journal { Routes = routes.ToList() };
        await SaveJournalAsync(journalPath, journal, cancellationToken);

        try
        {
            for (var index = 0; index < journal.Routes.Count; index++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                journal.InFlightIndex = index;
                await SaveJournalAsync(journalPath, journal, cancellationToken);

                await _backend.AddAsync(journal.Routes[index], cancellationToken);

                journal.AppliedCount = index + 1;
                journal.InFlightIndex = -1;
                await SaveJournalAsync(journalPath, journal, cancellationToken);
            }

            // Keep the journal for the entire lifetime of the tunnel. It is the crash-recovery
            // record and is removed only after RecoverAsync has restored the original route state.
        }
        catch
        {
            try
            {
                await RollbackAsync(journal, journalPath, CancellationToken.None);
            }
            catch (Exception rollbackError)
            {
                Logging.SaveLog("HevRouteTransaction.Rollback", rollbackError);
            }
            throw;
        }
    }

    /// <summary>
    /// Removes only routes recorded by this transaction. The in-flight entry is included because
    /// the process may have crashed after the OS accepted a route but before the journal advanced.
    /// </summary>
    public async Task RecoverAsync(string journalPath, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(journalPath);
        if (!File.Exists(journalPath))
            return;

        var json = await File.ReadAllTextAsync(journalPath, cancellationToken);
        var journal = JsonSerializer.Deserialize<Journal>(json)
            ?? throw new InvalidDataException("The Hev route recovery journal is invalid.");
        await RollbackAsync(journal, journalPath, cancellationToken);
    }

    private async Task RollbackAsync(Journal journal, string journalPath, CancellationToken cancellationToken)
    {
        var indexes = new SortedSet<int>();
        for (var i = 0; i < Math.Min(journal.AppliedCount, journal.Routes.Count); i++)
            indexes.Add(i);
        if (journal.InFlightIndex >= 0 && journal.InFlightIndex < journal.Routes.Count)
            indexes.Add(journal.InFlightIndex);

        foreach (var index in indexes.Reverse())
        {
            cancellationToken.ThrowIfCancellationRequested();
            var route = journal.Routes[index];
            if (await _backend.ExistsAsync(route, cancellationToken))
                await _backend.RemoveAsync(route, cancellationToken);

            journal.AppliedCount = Math.Min(journal.AppliedCount, index);
            journal.InFlightIndex = -1;
            await SaveJournalAsync(journalPath, journal, cancellationToken);
        }

        File.Delete(journalPath);
    }

    private static void ValidateOrder(IReadOnlyList<HevRouteAction> routes)
    {
        var sawDefault = false;
        var destinations = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var route in routes)
        {
            if (route.InterfaceIndex <= 0 || route.RouteMetric < 0)
                throw new ArgumentException("Route interface index and metric must be valid.", nameof(routes));
            if (!destinations.Add(route.DestinationPrefix))
                throw new ArgumentException($"Duplicate route destination: {route.DestinationPrefix}", nameof(routes));

            if (!route.IsUpstreamBypass)
                sawDefault = true;
            else if (sawDefault)
                throw new ArgumentException("All upstream bypass routes must precede tunnel default routes.", nameof(routes));
        }
    }

    private static async Task SaveJournalAsync(string path, Journal journal, CancellationToken cancellationToken)
    {
        var directory = Path.GetDirectoryName(Path.GetFullPath(path))!;
        Directory.CreateDirectory(directory);
        var temporaryPath = path + ".tmp";
        var json = JsonSerializer.Serialize(journal, JsonOptions);
        await File.WriteAllTextAsync(temporaryPath, json, new UTF8Encoding(false), cancellationToken);
        File.Move(temporaryPath, path, overwrite: true);
    }
}
