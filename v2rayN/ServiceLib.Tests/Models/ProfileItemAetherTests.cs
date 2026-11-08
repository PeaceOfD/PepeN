namespace ServiceLib.Tests.Models;

// PattN: how an Aether profile reads as a profile: usable without an endpoint, and named by its tunnel.
public class ProfileItemAetherTests
{
    private static ProfileItem Aether(string remarks, Func<ProtocolExtraItem, ProtocolExtraItem>? copy = null, string address = "", int port = 0)
    {
        var item = new ProfileItem
        {
            ConfigType = EConfigType.Aether,
            CoreType = ECoreType.Xray,
            Remarks = remarks,
            Address = address,
            Port = port,
        };
        var extra = new ProtocolExtraItem();
        item.SetProtocolExtra(copy == null ? extra : copy(extra));
        return item;
    }

    [Test]
    public async Task AnAetherProfileStartsWithoutAnEndpoint()
    {
        // PattNG LauncherManagerTest.anAetherProfileStartsWithoutAnEndpoint
        await Aether("scan").IsValid().Should().BeTrue();
        await Aether("pinned", address: "162.159.198.1", port: 443).IsValid().Should().BeTrue();
        await Aether("hops", extra => extra with
        {
            AetherProtocol = "gool",
            AetherWiwOuter = "162.159.192.1:2408",
            AetherWiwInner = "162.159.192.5:894",
        }).IsValid().Should().BeTrue();
    }

    [Test]
    public async Task ARegularProfileStillNeedsAnEndpoint()
    {
        var socks = new ProfileItem { ConfigType = EConfigType.SOCKS, Remarks = "socks", Address = string.Empty, Port = 0 };
        await socks.IsValid().Should().BeFalse();

        socks.Address = "127.0.0.1";
        socks.Port = 1080;
        await socks.IsValid().Should().BeTrue();
    }

    [Test]
    public async Task AnAetherProfileIsNamedByItsTunnelFromTheOutsideIn()
    {
        // PattNG MainServerRowModelsTest.anAetherRowNamesTheTunnelFromTheOutsideIn, with the protocols under their own
        // names, as the editor's labels come with the editor.
        await Aether("WARP").GetSummary().Should().BeEqualTo("[Aether] WARP(WIREGUARD)");
        await Aether("P", extra => extra with { AetherProtocol = "wg", AetherPsiphon = "chain" }).GetSummary()
            .Should().BeEqualTo("[Aether] P(WIREGUARD → PSIPHON)");
        await Aether("T", extra => extra with { AetherProtocol = "masque", AetherTor = "reverse" }).GetSummary()
            .Should().BeEqualTo("[Aether] T(TOR → MASQUE)");
        await Aether("M", extra => extra with { AetherProtocol = "mim", AetherTor = "chain" }).GetSummary()
            .Should().BeEqualTo("[Aether] M(MIM → TOR)");
        await Aether("O", extra => extra with { AetherProtocol = "wg", AetherPsiphon = "only" }).GetSummary()
            .Should().BeEqualTo("[Aether] O(PSIPHON)");
        await Aether("C", extra => extra with { AetherCommand = "aether --gool --tor" }).GetSummary()
            .Should().BeEqualTo("[Aether] C(WG_OVER_MASQUE → TOR)");
    }

    [Test]
    public async Task AnAetherProfileNamesTheEndpointItIsPinnedToAsOtherProfilesDo()
    {
        await Aether("WARP", extra => extra with { AetherProtocol = "masque" }, "162.159.198.1", 443).GetSummary()
            .Should().BeEqualTo("[Aether] WARP(MASQUE, 162***1:443)");
        await Aether("WARP6", extra => extra with { AetherProtocol = "wg" }, "2606:4700:d0::a29f:c001", 2408).GetSummary()
            .Should().BeEqualTo("[Aether] WARP6(WIREGUARD, 2606***c001:2408)");
    }
}
