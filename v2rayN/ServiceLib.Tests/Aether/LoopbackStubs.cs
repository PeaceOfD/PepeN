namespace ServiceLib.Tests.Aether;

// Servers of the tests' own, on the loopback, for the probes of latency and speed tests through an Aether tunnel: nothing
// reaches the network.

/// <summary>Accepts connections and never answers, like a tunnel whose far end is gone.</summary>
internal sealed class StallingSocksStub : IDisposable
{
    private readonly TcpListener _server = new(IPAddress.Loopback, 0);
    private readonly List<TcpClient> _clients = [];
    private int _connections;

    public StallingSocksStub()
    {
        _server.Start();
        _ = Task.Run(async () =>
        {
            while (true)
            {
                try
                {
                    var client = await _server.AcceptTcpClientAsync();
                    lock (_clients)
                    {
                        _clients.Add(client);
                    }
                    Interlocked.Increment(ref _connections);
                    Connected.TrySetResult(true);
                }
                catch (Exception ex) when (ex is SocketException or ObjectDisposedException)
                {
                    return;
                }
            }
        });
    }

    public int Port => ((IPEndPoint)_server.LocalEndpoint).Port;
    public TaskCompletionSource<bool> Connected { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

    /// <summary>How many connections it took.</summary>
    public int Connections => Volatile.Read(ref _connections);

    public void Dispose()
    {
        _server.Stop();
        lock (_clients)
        {
            _clients.ForEach(client => client.Dispose());
        }
    }
}

/// <summary>An HTTP server that answers every request with <paramref name="status" /> and nothing else.</summary>
internal sealed class HttpStub : IDisposable
{
    private readonly TcpListener _server = new(IPAddress.Loopback, 0);

    public HttpStub(string status)
    {
        _server.Start();
        _ = Task.Run(async () =>
        {
            while (true)
            {
                TcpClient client;
                try
                {
                    client = await _server.AcceptTcpClientAsync();
                }
                catch (Exception ex) when (ex is SocketException or ObjectDisposedException)
                {
                    return;
                }
                _ = Task.Run(async () =>
                {
                    using (client)
                    {
                        try
                        {
                            var stream = client.GetStream();
                            using var reader = new StreamReader(stream, Encoding.ASCII, false, 1024, leaveOpen: true);
                            while (!string.IsNullOrEmpty(await reader.ReadLineAsync()))
                            {
                            }
                            await stream.WriteAsync(Encoding.ASCII.GetBytes($"HTTP/1.1 {status}\r\nContent-Length: 0\r\nConnection: close\r\n\r\n"));
                        }
                        catch (Exception ex) when (ex is IOException or SocketException or ObjectDisposedException)
                        {
                        }
                    }
                });
            }
        });
    }

    public int Port => ((IPEndPoint)_server.LocalEndpoint).Port;

    public void Dispose()
    {
        _server.Stop();
    }
}

/// <summary>A SOCKS5 server that connects where it is asked, as a tunnel does.</summary>
internal sealed class SocksStub : IDisposable
{
    private readonly TcpListener _server = new(IPAddress.Loopback, 0);

    public SocksStub()
    {
        _server.Start();
        _ = Task.Run(async () =>
        {
            while (true)
            {
                TcpClient client;
                try
                {
                    client = await _server.AcceptTcpClientAsync();
                }
                catch (Exception ex) when (ex is SocketException or ObjectDisposedException)
                {
                    return;
                }
                _ = Task.Run(() => RelayAsync(client));
            }
        });
    }

    public int Port => ((IPEndPoint)_server.LocalEndpoint).Port;

    private static async Task RelayAsync(TcpClient client)
    {
        using (client)
        {
            try
            {
                var stream = client.GetStream();
                var head = new byte[2];
                await stream.ReadExactlyAsync(head);
                await stream.ReadExactlyAsync(new byte[head[1]]);
                await stream.WriteAsync(new byte[] { 5, 0 });
                var request = new byte[4];
                await stream.ReadExactlyAsync(request);
                string host;
                if (request[3] == 1)
                {
                    var address = new byte[4];
                    await stream.ReadExactlyAsync(address);
                    host = new IPAddress(address).ToString();
                }
                else if (request[3] == 3)
                {
                    var length = new byte[1];
                    await stream.ReadExactlyAsync(length);
                    var name = new byte[length[0]];
                    await stream.ReadExactlyAsync(name);
                    host = Encoding.ASCII.GetString(name);
                }
                else
                {
                    return;
                }
                var port = new byte[2];
                await stream.ReadExactlyAsync(port);
                using var target = new TcpClient();
                await target.ConnectAsync(host, (port[0] << 8) | port[1]);
                await stream.WriteAsync(new byte[] { 5, 0, 0, 1, 0, 0, 0, 0, 0, 0 });
                var upstream = target.GetStream();
                await Task.WhenAny(stream.CopyToAsync(upstream), upstream.CopyToAsync(stream));
            }
            catch (Exception ex) when (ex is IOException or SocketException or EndOfStreamException or ObjectDisposedException)
            {
            }
        }
    }

    public void Dispose()
    {
        _server.Stop();
    }
}

/// <summary>
/// An HTTP server whose file never finishes: it answers HEAD with the headers of a large file, and GET with them and the
/// body a little at a time, as a download over a slow tunnel comes, until the client gives up.
/// </summary>
internal sealed class SlowHttpStub : IDisposable
{
    private readonly TcpListener _server = new(IPAddress.Loopback, 0);
    private readonly CancellationTokenSource _stop = new();

    public SlowHttpStub()
    {
        _server.Start();
        _ = Task.Run(async () =>
        {
            while (true)
            {
                TcpClient client;
                try
                {
                    client = await _server.AcceptTcpClientAsync();
                }
                catch (Exception ex) when (ex is SocketException or ObjectDisposedException)
                {
                    return;
                }
                _ = Task.Run(async () =>
                {
                    using (client)
                    {
                        try
                        {
                            var stream = client.GetStream();
                            using var reader = new StreamReader(stream, Encoding.ASCII, false, 1024, leaveOpen: true);
                            var first = await reader.ReadLineAsync() ?? string.Empty;
                            while (!string.IsNullOrEmpty(await reader.ReadLineAsync()))
                            {
                            }
                            await stream.WriteAsync(Encoding.ASCII.GetBytes(
                                "HTTP/1.1 200 OK\r\nContent-Type: application/octet-stream\r\nContent-Length: 100000000\r\nConnection: close\r\n\r\n"));
                            if (first.StartsWith("HEAD", StringComparison.Ordinal))
                            {
                                return;
                            }
                            var chunk = new byte[4096];
                            while (!_stop.IsCancellationRequested)
                            {
                                await stream.WriteAsync(chunk, _stop.Token);
                                await Task.Delay(50, _stop.Token);
                            }
                        }
                        catch (Exception ex) when (ex is IOException or SocketException or ObjectDisposedException or OperationCanceledException)
                        {
                        }
                    }
                });
            }
        });
    }

    public int Port => ((IPEndPoint)_server.LocalEndpoint).Port;

    public void Dispose()
    {
        _stop.Cancel();
        _server.Stop();
    }
}
