using TUnit.Assertions.Enums;
using ServiceLib.Tests.CoreConfig;

namespace ServiceLib.Tests.Aether;

// PattN (design 2.7.6, critique 11): the names an Aether core asks Xray for before its tunnel is up, which the TUN's DNS
// has to answer directly.
public class AetherBootstrapNamesTests
{
    /// <summary>The names of the WARP API every core may call: the first one, then the other.</summary>
    private static readonly string[] Api = [AetherBootstrapNames.WarpApi, AetherBootstrapNames.WarpApiFallback];

    private static List<string> Of(string command)
    {
        return AetherBootstrapNames.Of(AetherCore.ArgumentsOf(command));
    }

    [Test]
    public async Task EveryCoreMayCallTheWarpApi()
    {
        // Both names the core goes by, the second when the first gets no answer.
        await Api.Should().BeEquivalentTo(["api.devices.cloudflare.com", "api.cloudflareclient.com"], CollectionOrdering.Matching);
        List<string> api = [.. Api];
        await Of("aether --bind 127.0.0.1:10819 --protocol masque").Should()
            .BeEquivalentTo(api, CollectionOrdering.Matching);
        // An address the command sends the calls to, when it is a name; an IP address needs no lookup.
        await Of("aether --protocol wg --api-address api.example.net").Should()
            .BeEquivalentTo([.. api, "api.example.net"], CollectionOrdering.Matching);
        await Of("aether --protocol wg --api-address 162.159.192.1").Should()
            .BeEquivalentTo(api, CollectionOrdering.Matching);
        // With a port after it (the core's --api-address <ip|name[:port]>), an IPv6 address then in brackets.
        await Of("aether --protocol wg --api-address api.example.net:8443").Should()
            .BeEquivalentTo([.. api, "api.example.net"], CollectionOrdering.Matching);
        await Of("aether --protocol wg --api-address 188.114.97.6:443").Should()
            .BeEquivalentTo(api, CollectionOrdering.Matching);
        await Of("aether --protocol wg --api-address [2606:4700::1]:8443").Should()
            .BeEquivalentTo(api, CollectionOrdering.Matching);
        await Of("aether --protocol wg --api-address 2606:4700::1").Should()
            .BeEquivalentTo(api, CollectionOrdering.Matching);
        // Without an address, the calls go to the server name, which follows the host: each name given goes in as the
        // core reads it, trimmed and without a trailing dot, the one it does not look up as well, which does no harm.
        await Of("aether --protocol wg --api-host api.example.net.").Should()
            .BeEquivalentTo([.. api, "api.example.net"], CollectionOrdering.Matching);
        await Of("aether --protocol wg --api-sni front.example.net --api-host api.example.net").Should()
            .BeEquivalentTo([.. api, "front.example.net", "api.example.net"], CollectionOrdering.Matching);
        // Any name the core takes, as it takes --masque-sni, even one with an underscore; none it refuses.
        await Of("aether --protocol wg --api-sni _front.example.net").Should()
            .BeEquivalentTo([.. api, "_front.example.net"], CollectionOrdering.Matching);
        await Of("aether --protocol wg --api-sni 162.159.137.105").Should()
            .BeEquivalentTo(api, CollectionOrdering.Matching);
    }

    [Test]
    public async Task TheProxyACommandNamesWithUpstreamIsLookedUpByTheCoreItself()
    {
        static string? UpstreamHost(string command) => AetherBootstrapNames.UpstreamHostOf(AetherCore.ArgumentsOf(command));

        await UpstreamHost("aether --masque --upstream socks5://proxy.example.net:1080").Should().BeEqualTo("proxy.example.net");
        await UpstreamHost("aether --masque --upstream socks5h://user:pass@proxy.example.net:1080").Should().BeEqualTo("proxy.example.net");
        // An address needs no lookup, nor does the loopback name.
        await UpstreamHost("aether --masque --upstream socks5://192.0.2.7:1080").Should().BeNull();
        await UpstreamHost("aether --masque --upstream socks5://[2001:db8::7]:1080").Should().BeNull();
        await UpstreamHost("aether --masque --upstream socks5://localhost:1080").Should().BeNull();
        await UpstreamHost("aether --masque").Should().BeNull();
        await UpstreamHost("aether --masque --upstream nonsense").Should().BeNull();
    }

    [Test]
    public async Task AnEchKeyAskedOfAPlainResolverHasItsDomainProtected()
    {
        // Behind a sing-box TUN in front of Xray, the core's HTTPS-record query over UDP or TCP is answered by sing-box's
        // DNS rules; a protected name goes to the direct DNS ahead of the rule that blocks such queries.
        foreach (var dns in new[] { "", "--ech-dns udp://1.1.1.1", "--ech-dns tcp://8.8.8.8:53" })
        {
            await Of($"aether --masque --ech auto {dns} --ech-domain ech.example.com").Should()
                .BeEquivalentTo([.. Api, "ech.example.com"], CollectionOrdering.Matching).Because(dns);
        }
        // Without a domain named, the core's default.
        await Of("aether --masque --ech auto").Should()
            .BeEquivalentTo([.. Api, AetherArguments.DefaultEchDomain], CollectionOrdering.Matching);
        // A DNS-over-HTTPS resolver is reached at its host, and asked over HTTPS; a key given in the command is asked of nobody.
        await Of("aether --masque --ech auto --ech-dns https://dns.example/dns-query --ech-domain ech.example.com").Should()
            .BeEquivalentTo([.. Api, "dns.example"], CollectionOrdering.Matching);
        await Of("aether --masque --ech AEX+DQ== --ech-domain ech.example.com").Should()
            .BeEquivalentTo(Api, CollectionOrdering.Matching);
        // As the profile's settings build it.
        var profile = CoreConfigTestFactory.CreateAetherNode("warp-ech", "WARP ECH", AetherProtocol.Masque.Type, extra => extra with { AetherEch = true });
        await AetherBootstrapNames.Of(AetherCore.Of(profile, AetherPorts.DefaultListenPort).Arguments).Should().Contain(AetherArguments.DefaultEchDomain);
    }

    [Test]
    public async Task TheBridgesOfTheProfileThatDialNamesHaveThemProtected()
    {
        const string webtunnel = "webtunnel [2001:db8::1]:443 0123456789ABCDEF0123456789ABCDEF01234567 url=https://wt.example.org/abc ver=0.0.1";
        const string snowflake = "snowflake 192.0.2.3:80 2B280B23E1107BB62ABFC40DDCC8824814F80A72 fingerprint=2B280B23E1107BB62ABFC40DDCC8824814F80A72 "
                                 + "url=https://broker.example.net/ fronts=front1.example.com,front2.example.com "
                                 + "ice=stun:stun.example.org:3478,stun:192.0.2.9:3478 utls-imitate=hellorandomizedalpn";
        const string meek = "meek_lite 192.0.2.18:80 BE776A53492E1E044A26F17306E1BC46A55A1625 url=https://meek.example.net/ front=ajax.example.com";
        const string obfs4 = "obfs4 192.0.2.4:443 0123456789ABCDEF0123456789ABCDEF01234567 cert=abc iat-mode=0";

        var names = AetherBootstrapNames.Of(["--tor-only", "--tor-bridge", webtunnel, "--tor-bridge", snowflake, "--tor-bridge", meek, "--tor-bridge", obfs4]);

        foreach (var host in new[] { "wt.example.org", "broker.example.net", "front1.example.com", "front2.example.com", "stun.example.org", "meek.example.net", "ajax.example.com" })
        {
            await names.Should().Contain(host).Because(host);
        }
        await names.Should().Contain("bridges.torproject.org");
        await names.Any(name => name.Contains("192.0.2.", StringComparison.Ordinal) || name.Contains("2001:db8", StringComparison.Ordinal)).Should().BeFalse();
        // Tor inside the tunnel dials its bridges through it.
        await AetherBootstrapNames.Of(["--masque", "--tor", "--tor-bridge", webtunnel]).Should().NotContain("wt.example.org");
        await AetherBootstrapNames.TorBeforeTheTunnel(["--tor-reverse"]).Should().BeTrue();
        await AetherBootstrapNames.TorBeforeTheTunnel(["--tor"]).Should().BeFalse();
    }

    [Test]
    public async Task TheEchKeyIsAskedOfTheHostTheDohResolverIsReachedAt()
    {
        // The @address= name when there is one, the URL's host otherwise; the resolvers of other schemes are addresses.
        await Of("aether --ech auto --ech-dns https://dns.example/dns-query --ech-domain cloudflare-ech.com").Should()
            .BeEquivalentTo([.. Api, "dns.example"], CollectionOrdering.Matching);
        await Of("aether --ech auto --ech-dns https://dns.example:8443/dns-query").Should()
            .BeEquivalentTo([.. Api, "dns.example"], CollectionOrdering.Matching);
        await Of("aether --ech auto --ech-dns https://doq.dns4all.eu/dns-query@address=front.example@sni=www.example.com").Should()
            .BeEquivalentTo([.. Api, "front.example"], CollectionOrdering.Matching);
        // A plain resolver is an address as well; its query asks for the ECH domain (AnEchKeyAskedOfAPlainResolverHasItsDomainProtected).
        foreach (var dns in new[]
                 {
                     "https://doq.dns4all.eu/dns-query@address=194.0.5.3", "https://1.1.1.1/dns-query@sni=www.microsoft.com",
                     "https://[2606:4700::1111]/dns-query",
                 })
        {
            await Of($"aether --ech auto --ech-dns {dns}").Should()
                .BeEquivalentTo(Api, CollectionOrdering.Matching).Because(dns);
        }
    }

    [Test]
    public async Task TorAroundTheTunnelOrAloneReachesItsBridgesBeforeAnyTunnel()
    {
        List<string> withTor = [.. Api, .. AetherBootstrapNames.TorBootstrap];
        await Of("aether --protocol masque --tor-reverse --tor-bind 127.0.0.1:10820").Should()
            .BeEquivalentTo(withTor, CollectionOrdering.Matching);
        await Of("aether --tor-only").Should().BeEquivalentTo(withTor, CollectionOrdering.Matching);
        // Tor inside the tunnel starts once the tunnel is up, and its names go through it.
        await Of("aether --protocol masque --tor --tor-bind 127.0.0.1:10819").Should()
            .BeEquivalentTo(Api, CollectionOrdering.Matching);
        await AetherBootstrapNames.TorBootstrap.Should().Contain("bridges.torproject.org");
        await AetherBootstrapNames.TorBootstrap.Should().Contain("stun.m-online.net");
    }

    [Test]
    public async Task PsiphonAroundTheTunnelOrAloneFetchesItsServerListBeforeAnyTunnel()
    {
        foreach (var flag in new[] { "--psiphon-reverse", "--psiphon-only" })
        {
            await Of($"aether --protocol masque {flag}").Should()
                .BeEquivalentTo([.. Api, AetherBootstrapNames.PsiphonRemoteList], CollectionOrdering.Matching)
                .Because(flag);
        }
        // Psiphon inside the tunnel starts once the tunnel is up and fetches its list through it (the fork's psiphon.rs
        // run_chain): no name of it is asked of Xray, and every app's s3.amazonaws.com stays with the remote DNS.
        await Of("aether --protocol masque --psiphon").Should()
            .BeEquivalentTo(Api, CollectionOrdering.Matching);
        // Psiphon before the tunnel dials names its client builds in, which nothing protects.
        await AetherBootstrapNames.PsiphonBeforeTheTunnel(AetherCore.ArgumentsOf("aether --psiphon-reverse")).Should().BeTrue();
        await AetherBootstrapNames.PsiphonBeforeTheTunnel(AetherCore.ArgumentsOf("aether --psiphon-only")).Should().BeTrue();
        await AetherBootstrapNames.PsiphonBeforeTheTunnel(AetherCore.ArgumentsOf("aether --psiphon")).Should().BeFalse();
        await AetherBootstrapNames.PsiphonBeforeTheTunnel(AetherCore.ArgumentsOf("aether --protocol wg")).Should().BeFalse();
    }
}
