using System.Net;
using System.Net.Sockets;
using ServiceLib.Services;

namespace ServiceLib.Tests.Services;

public class HevUpstreamEndpointResolverTests
{
    private static async Task<bool> ThrowsAsync<TException>(Func<Task> action) where TException : Exception
    {
        try
        {
            await action();
            return false;
        }
        catch (TException)
        {
            return true;
        }
    }

    [Test]
    public async Task ResolvesXrayAndSingboxOutboundEndpointsOnly()
    {
        const string config = """
            {
              "outbounds": [
                { "protocol": "vless", "settings": { "vnext": [{ "address": "proxy.example", "port": 443 }] } },
                { "protocol": "shadowsocks", "settings": { "servers": [{ "address": "198.51.100.7", "port": 8388 }] } },
                { "type": "trojan", "server": "edge.example", "server_port": 443, "tls": { "server_name": "sni.example" } },
                { "protocol": "freedom", "settings": {} }
              ],
              "dns": { "servers": [{ "address": "dns.example" }] }
            }
            """;

        var calls = new List<string>();
        var result = await HevUpstreamEndpointResolver.ResolveAsync(
            config,
            resolveHost: (host, _) =>
            {
                calls.Add(host);
                return Task.FromResult(host switch
                {
                    "proxy.example" => new[] { IPAddress.Parse("203.0.113.10") },
                    "edge.example" => new[] { IPAddress.Parse("2001:db8::10") },
                    _ => Array.Empty<IPAddress>()
                });
            });

        await result.Should().BeEquivalentTo(new[]
        {
            IPAddress.Parse("203.0.113.10"),
            IPAddress.Parse("198.51.100.7"),
            IPAddress.Parse("2001:db8::10")
        });
        await calls.Should().BeEquivalentTo(new[] { "proxy.example", "edge.example" });
    }

    [Test]
    public async Task FailsClosedWhenAnyEndpointCannotBeResolved()
    {
        const string config = """
            {
              "outbounds": [
                { "type": "vless", "server": "available.example" },
                { "type": "trojan", "server": "missing.example" }
              ]
            }
            """;

        var act = async () => await HevUpstreamEndpointResolver.ResolveAsync(
            config,
            resolveHost: (host, _) => host == "available.example"
                ? Task.FromResult(new[] { IPAddress.Parse("203.0.113.10") })
                : throw new SocketException((int)SocketError.HostNotFound));

        await ThrowsAsync<SocketException>(act).Should().BeTrue();
    }

    [Test]
    public async Task FailsClosedWhenOneOfSeveralEndpointsHasNoUsableAddress()
    {
        const string config = """
            {
              "outbounds": [
                { "type": "vless", "server": "available.example" },
                { "type": "trojan", "server": "unusable.example" }
              ]
            }
            """;

        var act = async () => await HevUpstreamEndpointResolver.ResolveAsync(
            config,
            resolveHost: (host, _) => Task.FromResult(host == "available.example"
                ? new[] { IPAddress.Parse("203.0.113.10") }
                : Array.Empty<IPAddress>()));

        await ThrowsAsync<InvalidDataException>(act).Should().BeTrue();
    }

    [Test]
    public async Task RejectsConfigsWithoutRecognizedProxyEndpoints()
    {
        const string config = """{ "outbounds": [{ "protocol": "freedom", "settings": {} }] }""";

        var act = async () => await HevUpstreamEndpointResolver.ResolveAsync(config);

        await act.Should().ThrowAsync<InvalidDataException>();
    }

    [Test]
    public async Task DoesNotReturnLoopbackOrMulticastAddresses()
    {
        const string config = """
            {
              "outbounds": [
                { "type": "vless", "server": "localhost" },
                { "type": "trojan", "server": "239.1.1.1" }
              ]
            }
            """;

        var act = async () => await HevUpstreamEndpointResolver.ResolveAsync(
            config,
            resolveHost: (host, _) => Task.FromResult(new[] { IPAddress.Loopback, IPAddress.Parse("239.1.1.1") }));

        await act.Should().ThrowAsync<InvalidDataException>();
    }
}
