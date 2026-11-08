namespace ServiceLib.Tests.Aether;

/// <summary>From PattNG AetherScannerTest.</summary>
public class AetherScannerTests
{
    private static string LogLine(string message)
    {
        return $"[2026-09-11T10:00:00.000Z INFO  aether] {message}";
    }

    [Test]
    public async Task ReadsTheMasqueGatewayOutOfTheLog()
    {
        var found = AetherScanner.Parse(AetherProtocol.Masque, LogLine("[+] selected MASQUE gateway 162.159.197.3:443 (rtt 84ms)"));
        await (found?.Endpoint).Should().BeEqualTo(new AetherEndpoint("162.159.197.3", 443));
        await (found?.InnerHop).Should().BeNull();
    }

    [Test]
    public async Task ReadsAnIpv6MasqueGatewayWrittenWithoutBrackets()
    {
        var found = AetherScanner.Parse(AetherProtocol.Masque, LogLine("[+] selected MASQUE gateway 2606:4700:102::3:443 (rtt 90ms)"));
        await (found?.Endpoint).Should().BeEqualTo(new AetherEndpoint("2606:4700:102::3", 443));
    }

    [Test]
    public async Task ReadsTheWireguardEndpointOutOfTheLog()
    {
        var found = AetherScanner.Parse(
            AetherProtocol.WireGuard,
            LogLine("[+] selected WireGuard endpoint 162.159.192.1:894 using aethernoize profile 'balanced'"));
        await (found?.Endpoint).Should().BeEqualTo(new AetherEndpoint("162.159.192.1", 894));

        var ipv6 = AetherScanner.Parse(
            AetherProtocol.WireGuard,
            LogLine("[+] selected WireGuard endpoint [2606:4700:d0::a29f:c001]:2408 using aethernoize profile 'light'"));
        await (ipv6?.Endpoint).Should().BeEqualTo(new AetherEndpoint("2606:4700:d0::a29f:c001", 2408));
    }

    [Test]
    public async Task ReadsBothGoolHopsOutOfOneLine()
    {
        var found = AetherScanner.Parse(
            AetherProtocol.Gool,
            LogLine("[+] using cloudflare edge 162.159.192.1:2408 (outer) and 188.114.96.1:894 (inner)"));
        await (found?.Endpoint).Should().BeEqualTo(new AetherEndpoint("162.159.192.1", 2408));
        await (found?.InnerHop).Should().BeEqualTo(new AetherEndpoint("188.114.96.1", 894));
    }

    [Test]
    public async Task GoolWaitsForBothHopsInsteadOfTheFirstEndpoint()
    {
        await AetherScanner.Parse(AetherProtocol.Gool, LogLine("[+] selected WireGuard endpoint 162.159.192.1:2408 (rtt 61ms)")).Should().BeNull();
        await AetherScanner.Parse(AetherProtocol.Gool, LogLine("[+] using cloudflare edge 162.159.192.1:2408")).Should().BeNull();
    }

    [Test]
    public async Task ReadsBothMimHopsOutOfTheReadyLine()
    {
        var found = AetherScanner.Parse(
            AetherProtocol.Mim,
            LogLine("[+] masque-in-masque ready: 162.159.192.1:443 (outer) and 188.114.96.1:443 (inner)"));
        await (found?.Endpoint).Should().BeEqualTo(new AetherEndpoint("162.159.192.1", 443));
        await (found?.InnerHop).Should().BeEqualTo(new AetherEndpoint("188.114.96.1", 443));

        var ipv6 = AetherScanner.Parse(
            AetherProtocol.Mim,
            LogLine("[+] masque-in-masque ready: [2606:4700:102::3]:443 (outer) and [2606:4700:103::4]:443 (inner)"));
        await (ipv6?.Endpoint).Should().BeEqualTo(new AetherEndpoint("2606:4700:102::3", 443));
        await (ipv6?.InnerHop).Should().BeEqualTo(new AetherEndpoint("2606:4700:103::4", 443));
    }

    [Test]
    public async Task MimWaitsForThePairToCarryTrafficInsteadOfTheFirstHop()
    {
        await AetherScanner.Parse(AetherProtocol.Mim, LogLine("[+] selected MASQUE gateway 162.159.192.1:443 (rtt 84ms)")).Should().BeNull();
        await AetherScanner.Parse(AetherProtocol.Mim, LogLine("[*] establishing outer MASQUE tunnel to 162.159.192.1:443...")).Should().BeNull();
        await AetherScanner.Parse(AetherProtocol.Mim, LogLine("[+] inner MASQUE tunnel established through 188.114.96.1:443")).Should().BeNull();
        await AetherScanner.Parse(
                AetherProtocol.Mim,
                LogLine("[+] using cloudflare edge 162.159.192.1:2408 (outer) and 188.114.96.1:894 (inner)"))
            .Should().BeNull();
        await AetherScanner.Parse(
                AetherProtocol.Gool,
                LogLine("[+] masque-in-masque ready: 162.159.192.1:443 (outer) and 188.114.96.1:443 (inner)"))
            .Should().BeNull();
    }

    [Test]
    public async Task WireGuardOverMasqueEndsOnItsReadyLineWithTheGatewayAlone()
    {
        // The gateway is named first, then the tunnel comes up and the WireGuard inside it; only that ends the scan.
        await AetherScanner.Parse(AetherProtocol.WgOverMasque, LogLine("[+] selected MASQUE gateway 162.159.197.3:443 (rtt 84ms)")).Should().BeNull();
        await AetherScanner.Parse(AetherProtocol.WgOverMasque, LogLine("[+] using cloudflare edge 162.159.197.3:443")).Should().BeNull();
        var found = AetherScanner.Parse(
            AetherProtocol.WgOverMasque,
            LogLine("[+] gool ready: masque 162.159.197.3:443 carries wireguard 162.159.192.1:2408"));
        await (found?.Endpoint).Should().BeEqualTo(new AetherEndpoint("162.159.197.3", 443));
        // The WireGuard endpoint is the profile's own, which the scan kept, and no finding of its.
        await (found?.InnerHop).Should().BeNull();
        var ipv6 = AetherScanner.Parse(
            AetherProtocol.WgOverMasque,
            LogLine("[+] gool ready: masque [2606:4700:102::3]:443 carries wireguard [2606:4700:d0::a29f:c001]:2408"));
        await (ipv6?.Endpoint).Should().BeEqualTo(new AetherEndpoint("2606:4700:102::3", 443));
        // The core says it is ready after it has checked the exit, so the line stands on its own.
        var ruled = AetherScanner.Matcher(AetherProtocol.WgOverMasque, exitRuled: true);
        await ruled(LogLine("[+] selected MASQUE gateway 162.159.197.3:443 (rtt 84ms)")).Should().BeNull();
        await ruled(LogLine("[+] exit location DE accepted (not IR)")).Should().BeNull();
        await (ruled(LogLine("[+] gool ready: masque 162.159.197.3:443 carries wireguard 162.159.192.1:2408"))?.Endpoint)
            .Should().BeEqualTo(new AetherEndpoint("162.159.197.3", 443));
        // Nor does another protocol take it.
        await AetherScanner.Parse(AetherProtocol.Gool, LogLine("[+] gool ready: masque 162.159.197.3:443 carries wireguard 162.159.192.1:2408")).Should().BeNull();
        await AetherScanner.Parse(AetherProtocol.Masque, LogLine("[+] gool ready: masque 162.159.197.3:443 carries wireguard 162.159.192.1:2408")).Should().BeNull();
    }

    [Test]
    public async Task WithAnExitRuleTheScanEndsOnTheEndpointWhoseExitTheCoreAccepted()
    {
        var match = AetherScanner.Matcher(AetherProtocol.Masque, exitRuled: true);
        await match(LogLine("[+] selected MASQUE gateway 162.159.197.3:443 (rtt 84ms)")).Should().BeNull();
        await match(LogLine("[-] exit location IR rejected (not IR,RU)")).Should().BeNull();
        // The refused gateway is forgotten; a stray acceptance names nothing.
        await match(LogLine("[+] exit location DE accepted (not IR,RU)")).Should().BeNull();
        await match(LogLine("[+] selected MASQUE gateway 188.114.96.1:443 (rtt 90ms)")).Should().BeNull();
        await (match(LogLine("[+] exit location DE accepted (not IR,RU)"))?.Endpoint).Should().BeEqualTo(new AetherEndpoint("188.114.96.1", 443));

        var hops = AetherScanner.Matcher(AetherProtocol.Gool, exitRuled: true);
        await hops(LogLine("[+] using cloudflare edge 162.159.192.1:2408 (outer) and 188.114.96.1:894 (inner)")).Should().BeNull();
        await (hops(LogLine("[+] exit location SE accepted (DE,SE)"))?.InnerHop).Should().BeEqualTo(new AetherEndpoint("188.114.96.1", 894));
    }

    [Test]
    public async Task WithoutAnExitRuleTheEndpointLineEndsTheScanAndMasqueInMasqueNeedsNoWait()
    {
        var plain = AetherScanner.Matcher(AetherProtocol.Masque, exitRuled: false);
        await (plain(LogLine("[+] selected MASQUE gateway 162.159.197.3:443 (rtt 84ms)"))?.Endpoint).Should().BeEqualTo(new AetherEndpoint("162.159.197.3", 443));
        // The core names the masque-in-masque hops only after it has checked the exit, so the line stands on its own.
        var mim = AetherScanner.Matcher(AetherProtocol.Mim, exitRuled: true);
        await mim(LogLine("[+] exit location DE accepted (not IR)")).Should().BeNull();
        await (mim(LogLine("[+] masque-in-masque ready: 162.159.192.1:443 (outer) and 188.114.96.1:443 (inner)"))?.Endpoint)
            .Should().BeEqualTo(new AetherEndpoint("162.159.192.1", 443));
    }

    [Test]
    public async Task EachProtocolOnlyReadsItsOwnResult()
    {
        var masque = LogLine("[+] selected MASQUE gateway 162.159.197.3:443 (rtt 84ms)");
        var wireguard = LogLine("[+] selected WireGuard endpoint 162.159.192.1:894 (rtt 61ms)");
        await AetherScanner.Parse(AetherProtocol.WireGuard, masque).Should().BeNull();
        await AetherScanner.Parse(AetherProtocol.Masque, wireguard).Should().BeNull();
    }

    [Test]
    public async Task ALineThatNamesNoUsableEndpointYieldsNothing()
    {
        await AetherScanner.Parse(AetherProtocol.Masque, LogLine("[+] selected protocol: MASQUE")).Should().BeNull();
        await AetherScanner.Parse(AetherProtocol.Masque, LogLine("[*] hunting for a working MASQUE gateway")).Should().BeNull();
        await AetherScanner.Parse(AetherProtocol.Masque, LogLine("[+] selected MASQUE gateway 162.159.197.3 (rtt 84ms)")).Should().BeNull();
        await AetherScanner.Parse(AetherProtocol.Masque, "").Should().BeNull();
        await AetherScanner.Parse(
                AetherProtocol.Gool,
                LogLine("[+] using cloudflare edge 162.159.192.1:2408 (outer) and 188.114.96.1 (inner)"))
            .Should().BeNull();
    }
}
