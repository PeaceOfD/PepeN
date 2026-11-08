namespace ServiceLib.Tests.Fmt;

// PattN: aether:// links through FmtHandler, which every import (clipboard, QR code, file, subscription) and every
// export (share link, QR code, export of share links) goes through. AetherFmtTests checks the format itself.
public class AetherFmtHandlerTests
{
    // A link as PattNG writes it: a MASQUE profile pinned to an endpoint, with Psiphon after WARP.
    private const string PinnedLink = "aether://162.159.198.1:443?protocol=masque&scan=verified&ip=both&transport=h2"
                                      + "&psiphon=chain&psiphon_mode=cdn&region=DE"
                                      + "&fm=%7B%22tcp%22%3A%5B%5D%7D&dialMode=code-1#WARP%20%E2%9C%93";

    [Test]
    public async Task TheShareLinkOfAnAetherProfileIsItsAetherLink()
    {
        var profile = AetherFmt.Resolve(PinnedLink, out _)!;

        var uri = FmtHandler.GetShareUri(profile);

        await uri.Should().BeEqualTo(AetherFmt.ToUri(profile));
        await uri!.StartsWith("aether://162.159.198.1:443?", StringComparison.Ordinal).Should().BeTrue().Because(uri);
    }

    [Test]
    public async Task AnAetherLinkIsReadAsAnAetherProfileThatXrayRuns()
    {
        // As an import hands it over: one line of a list, with the spaces and the line break around it.
        var parsed = FmtHandler.ResolveConfig($"  {PinnedLink}\r\n", out var msg);

        await parsed.Should().NotBeNull().Because(msg);
        var extra = parsed!.GetProtocolExtra();
        await parsed.ConfigType.Should().BeEqualTo(EConfigType.Aether);
        await parsed.CoreType.Should().BeEqualTo(ECoreType.Xray);
        await parsed.Remarks.Should().BeEqualTo("WARP ✓");
        await parsed.Address.Should().BeEqualTo("162.159.198.1");
        await parsed.Port.Should().BeEqualTo(443);
        await extra.AetherProtocol.Should().BeEqualTo("masque");
        await extra.AetherScanMode.Should().BeEqualTo("verified");
        await extra.AetherIpVersion.Should().BeEqualTo("both");
        await extra.AetherTransport.Should().BeEqualTo("h2");
        await extra.AetherPsiphon.Should().BeEqualTo("chain");
        await extra.AetherPsiphonMode.Should().BeEqualTo("cdn");
        await extra.AetherPsiphonRegion.Should().BeEqualTo("DE");
        await parsed.Finalmask.Should().BeEqualTo("""{"tcp":[]}""");
        await parsed.DialMode.Should().BeEqualTo("code-1");
    }

    [Test]
    public async Task AnAetherLinkWithoutAnEndpointIsReadAsAProfileThatScans()
    {
        var scan = FmtHandler.ResolveConfig("aether://?protocol=wg&scan=balanced#Scan", out _);
        var hops = FmtHandler.ResolveConfig(
            "aether://?protocol=gool&outer=162.159.192.1%3A2408&inner=162.159.192.5%3A894#Hops", out _);

        await scan.Should().NotBeNull();
        await scan!.Address.Should().BeEqualTo(string.Empty);
        await scan.Port.Should().BeEqualTo(0);
        await scan.IsValid().Should().BeTrue();

        await hops.Should().NotBeNull();
        await hops!.Address.Should().BeEqualTo(string.Empty);
        await hops.Port.Should().BeEqualTo(0);
        await hops.GetProtocolExtra().AetherWiwOuter.Should().BeEqualTo("162.159.192.1:2408");
        await hops.GetProtocolExtra().AetherWiwInner.Should().BeEqualTo("162.159.192.5:894");
        await hops.IsValid().Should().BeTrue();
    }

    [Test]
    public async Task AnAetherProfileComesBackAsItWasSharedWithoutWhatStaysOnTheDevice()
    {
        var source = AetherFmt.Resolve(PinnedLink, out _)!;
        // A name of a profile of this device, a command of the profile's own and a local choice of outbound: none of
        // them travels in a link.
        source.SetProtocolExtra(source.GetProtocolExtra() with
        {
            AetherExitNode = "germany",
            AetherCommand = "aether --masque --bind 127.0.0.1:10819",
        });
        source.TargetStrategy = "UseIPv4";

        var uri = FmtHandler.GetShareUri(source);
        var resolved = FmtHandler.ResolveConfig(uri!, out var msg);

        await resolved.Should().NotBeNull().Because(msg);
        await (resolved!.GetProtocolExtra() == (source.GetProtocolExtra() with { AetherExitNode = null, AetherCommand = null }))
            .Should().BeTrue().Because(uri);
        await resolved.Remarks.Should().BeEqualTo(source.Remarks);
        await resolved.Address.Should().BeEqualTo(source.Address);
        await resolved.Port.Should().BeEqualTo(source.Port);
        await resolved.Finalmask.Should().BeEqualTo(source.Finalmask);
        await resolved.DialMode.Should().BeEqualTo(source.DialMode);
        await resolved.TargetStrategy.Should().BeEqualTo(string.Empty);
        await uri!.Contains("germany", StringComparison.Ordinal).Should().BeFalse().Because(uri);
        await uri.Contains("bind", StringComparison.Ordinal).Should().BeFalse().Because(uri);
    }
}
