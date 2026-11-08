using TUnit.Assertions.Enums;

namespace ServiceLib.Tests.Fmt;

// From PattNG fmt/AetherEchOptionsTest.kt: the resolvers and the domains the ECH fields of the Aether editor and of the
// WARP keys window offer, Global's lists here in place of PattNG's string arrays: each is a value the core takes, and the
// default comes first.
public class AetherEchOptionsTests
{
    [Test]
    public async Task EveryResolverOnOfferIsOneTheCoreTakesWithTheDefaultFirst()
    {
        var resolvers = Global.AetherEchDnsOptions;
        await resolvers.Should().BeEquivalentTo(
            [
                "udp://1.1.1.1",
                "udp://8.8.8.8",
                "https://1.1.1.1/dns-query@sni=www.microsoft.com",
                "https://doq.dns4all.eu/dns-query@address=194.0.5.3",
                "tcp://1.1.1.1",
                "tcp://8.8.8.8",
            ],
            CollectionOrdering.Matching);
        await resolvers.First().Should().BeEqualTo(AetherArguments.DefaultEchDns);
        foreach (var resolver in resolvers)
        {
            await $"{resolver}: {AetherFmt.IsEchDns(resolver)}".Should().BeEqualTo($"{resolver}: True");
        }
    }

    [Test]
    public async Task EveryDomainOnOfferIsOneTheCoreTakesWithTheDefaultFirst()
    {
        var domains = Global.AetherEchDomainOptions;
        await domains.Should().BeEquivalentTo(
            [
                "cloudflare-ech.com",
                "crypto.cloudflare.com",
                "ip.gs",
                "api.cloudflareclient.com",
                "consumer-masque.cloudflareclient.com",
                "consumer-masque-proxy.cloudflareclient.com",
            ],
            CollectionOrdering.Matching);
        await domains.First().Should().BeEqualTo(AetherArguments.DefaultEchDomain);
        foreach (var domain in domains)
        {
            await $"{domain}: {AetherFmt.IsEchDomain(domain)}".Should().BeEqualTo($"{domain}: True");
        }
    }
}
