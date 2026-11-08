namespace ServiceLib.Tests.Aether;

public class AetherEndpointTests
{
    [Test]
    public async Task AnIpv4AddressAndPortFormAnEndpoint()
    {
        await (AetherEndpoint.Of("162.159.192.1", "2408")?.ToString()).Should().BeEqualTo("162.159.192.1:2408");
        await (AetherEndpoint.Of(" 162.159.192.1 ", " 2408 ")?.ToString()).Should().BeEqualTo("162.159.192.1:2408");
    }

    [Test]
    public async Task AnIpv6AddressIsBracketedWhenWrittenOut()
    {
        var endpoint = AetherEndpoint.Of("2606:4700:d0::a29f:c001", "443");
        await (endpoint?.Host).Should().BeEqualTo("2606:4700:d0::a29f:c001");
        await (endpoint?.ToString()).Should().BeEqualTo("[2606:4700:d0::a29f:c001]:443");
        await AetherEndpoint.Of("[2606:4700:d0::a29f:c001]", "443").Should().BeEqualTo(endpoint);
    }

    [Test]
    public async Task AnIpv6AddressIsWrittenInItsCanonicalForm()
    {
        await (AetherEndpoint.Of("2606:4700:0:0:0:0:0:1", "443")?.Host).Should().BeEqualTo("2606:4700::1");
        await (AetherEndpoint.Of("2606:4700:0000::0001", "443")?.Host).Should().BeEqualTo("2606:4700::1");
        await (AetherEndpoint.Of("2606:4700::ABCD", "443")?.Host).Should().BeEqualTo("2606:4700::abcd");
        await (AetherEndpoint.Of("1:0:0:2:0:0:0:3", "443")?.Host).Should().BeEqualTo("1:0:0:2::3");
        await (AetherEndpoint.Of("1:0:0:2:0:0:3:4", "443")?.Host).Should().BeEqualTo("1::2:0:0:3:4");
        await (AetherEndpoint.Of("1:0:2:3:4:5:6:7", "443")?.Host).Should().BeEqualTo("1:0:2:3:4:5:6:7");
        await (AetherEndpoint.Of("::", "443")?.Host).Should().BeEqualTo("::");
        await (AetherEndpoint.Of("::ffff:162.159.192.1", "443")?.Host).Should().BeEqualTo("::ffff:a29f:c001");
    }

    [Test]
    public async Task DigitsFromOtherScriptsAreReadAsAsciiDigits()
    {
        await (AetherEndpoint.Of("۱۶۲.۱۵۹.۱۹۲.۱", "۲۴۰۸")?.ToString()).Should().BeEqualTo("162.159.192.1:2408");
    }

    [Test]
    public async Task OnlyAnIpLiteralIsAnAddress()
    {
        await AetherEndpoint.Of("engage.cloudflareclient.com", "2408").Should().BeNull();
        await AetherEndpoint.Of("162.159.192", "2408").Should().BeNull();
        await AetherEndpoint.Of("162.159.192.256", "2408").Should().BeNull();
        await AetherEndpoint.Of("162.159.192.01", "2408").Should().BeNull();
        await AetherEndpoint.Of("1::2::3", "2408").Should().BeNull();
        await AetherEndpoint.Of("12345::1", "2408").Should().BeNull();
        await AetherEndpoint.Of("1:2:3:4:5:6:7:8:9", "2408").Should().BeNull();
        await AetherEndpoint.Of("1:2:3:4:5:6:7", "2408").Should().BeNull();
        await AetherEndpoint.Of("fe80::1%wlan0", "2408").Should().BeNull();
        await AetherEndpoint.Of("162.159.192.1::1", "2408").Should().BeNull();
        await AetherEndpoint.Of("", "2408").Should().BeNull();
        await AetherEndpoint.Of(null, "2408").Should().BeNull();
    }

    [Test]
    public async Task ThePortMustBeAUsablePortNumber()
    {
        await AetherEndpoint.Of("162.159.192.1", "0").Should().BeNull();
        await AetherEndpoint.Of("162.159.192.1", "65536").Should().BeNull();
        await AetherEndpoint.Of("162.159.192.1", "+443").Should().BeNull();
        await AetherEndpoint.Of("162.159.192.1", "http").Should().BeNull();
        await AetherEndpoint.Of("162.159.192.1", "").Should().BeNull();
        await AetherEndpoint.Of("162.159.192.1", null).Should().BeNull();
        await (AetherEndpoint.Of("162.159.192.1", "65535")?.Port).Should().BeEqualTo(65535);
    }

    [Test]
    public async Task AnEndpointIsReadFromTheWayTheCoreWritesIt()
    {
        await AetherEndpoint.Parse("162.159.192.1:2408").Should().BeEqualTo(new AetherEndpoint("162.159.192.1", 2408));
        await AetherEndpoint.Parse("[2606:4700::1]:2408").Should().BeEqualTo(new AetherEndpoint("2606:4700::1", 2408));
        await AetherEndpoint.Parse(" 162.159.192.1:2408 ").Should().BeEqualTo(new AetherEndpoint("162.159.192.1", 2408));
    }

    [Test]
    public async Task AnEndpointWithoutItsPortOrBracketsIsRefused()
    {
        await AetherEndpoint.Parse("162.159.192.1").Should().BeNull();
        await AetherEndpoint.Parse("162.159.192.1:").Should().BeNull();
        await AetherEndpoint.Parse(":2408").Should().BeNull();
        await AetherEndpoint.Parse("2606:4700::1:2408").Should().BeNull();
        await AetherEndpoint.Parse("[2606:4700::1]").Should().BeNull();
        await AetherEndpoint.Parse("").Should().BeNull();
        await AetherEndpoint.Parse(null).Should().BeNull();
    }

    // PattN: trimmed by Kotlin's whitespace, as PattNG trims: the separators U+001C to U+001F are spaces, U+0085 is not.
    [Test]
    public async Task TheSpacesAroundAnEndpointAreKotlins()
    {
        await (AetherEndpoint.Of("\u001F162.159.192.1\u001C", "\u001E2408\u001D")?.ToString()).Should().BeEqualTo("162.159.192.1:2408");
        await (AetherEndpoint.Parse("\u001F[2606:4700::1]:2408\u001F")?.ToString()).Should().BeEqualTo("[2606:4700::1]:2408");
        await AetherEndpoint.Of("162.159.192.1\u0085", "2408").Should().BeNull();
        await AetherEndpoint.Of("162.159.192.1", "2408\u0085").Should().BeNull();
        await AetherEndpoint.Parse("162.159.192.1:2408\u0085").Should().BeNull();
    }
}
