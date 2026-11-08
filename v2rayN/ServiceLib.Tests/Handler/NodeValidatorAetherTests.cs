namespace ServiceLib.Tests.Handler;

// PattN: the checks an Aether profile gets before a configuration is built of it. It needs no endpoint, has no TLS of
// its own, and runs on Xray alone.
public class NodeValidatorAetherTests
{
    private static ProfileItem Aether(string protocol, string address = "", int port = 0, Func<ProtocolExtraItem, ProtocolExtraItem>? copy = null)
    {
        var item = new ProfileItem
        {
            IndexId = $"aether-{protocol}",
            ConfigType = EConfigType.Aether,
            CoreType = ECoreType.Xray,
            Remarks = $"WARP {protocol}",
            Address = address,
            Port = port,
        };
        var extra = new ProtocolExtraItem { AetherProtocol = protocol };
        item.SetProtocolExtra(copy == null ? extra : copy(extra));
        return item;
    }

    // Xray takes an Aether profile, since it builds its outbound (Global.XraySupportConfigType), and never refuses one
    // for a missing endpoint.
    private static string XrayErrors()
    {
        return string.Empty;
    }

    [Test]
    public async Task XrayRunsAnAetherProfile()
    {
        await Global.XraySupportConfigType.Contains(EConfigType.Aether).Should().BeTrue();
        await Global.SingboxSupportConfigType.Contains(EConfigType.Aether).Should().BeFalse();
        await Errors(Aether("masque"), ECoreType.Xray).Should().BeEqualTo(string.Empty);
    }

    private static string Errors(ProfileItem item, ECoreType coreType)
    {
        return string.Join("\n", NodeValidator.Validate(item, coreType).Errors);
    }

    [Test]
    public async Task AnAetherProfileNeedsNoEndpoint()
    {
        ProfileItem[] profiles =
        [
            Aether("masque"),
            Aether("wg"),
            Aether("masque", "162.159.198.1", 443),
            Aether("gool", copy: extra => extra with { AetherWiwOuter = "162.159.192.1:2408", AetherWiwInner = "162.159.192.5:894" }),
            Aether("mim"),
            Aether("wg-over-masque"),
            // A subscription's override is applied as it comes; the core scans when it is no IP.
            Aether("wg", "engage.cloudflareclient.com", 2408),
        ];

        foreach (var profile in profiles)
        {
            var result = NodeValidator.Validate(profile, ECoreType.Xray);
            var label = $"{profile.Remarks} {profile.Address}:{profile.Port}";

            await string.Join("\n", result.Errors).Should().BeEqualTo(XrayErrors()).Because(label);
            await result.Warnings.Count.Should().BeEqualTo(0).Because(label);
        }
    }

    [Test]
    public async Task SingBoxRefusesAnAetherProfile()
    {
        foreach (var profile in new[] { Aether("masque"), Aether("wg", "162.159.192.1", 2408) })
        {
            await Errors(profile, ECoreType.sing_box)
                .Should().BeEqualTo(string.Format(ResUI.MsgCoreNotSupportProtocol, nameof(ECoreType.sing_box), EConfigType.Aether))
                .Because(profile.Remarks);
        }
    }

    [Test]
    public async Task TheFinalMaskOfTheExitNodeIsAJsonObjectOrNothing()
    {
        var invalid = string.Format(ResUI.MsgInvalidProperty, ResUI.TbFinalmask);
        foreach (var finalMask in new[] { "", "  ", """{"tcp": []}""", "{\n  // a comment\n  \"udp\": []\n}" })
        {
            var profile = Aether("masque");
            profile.Finalmask = finalMask;

            await Errors(profile, ECoreType.Xray).Should().BeEqualTo(XrayErrors()).Because(finalMask);
        }
        foreach (var finalMask in new[] { "fragment", "[]", """{"tcp": [}""" })
        {
            var profile = Aether("masque");
            profile.Finalmask = finalMask;

            await Errors(profile, ECoreType.Xray)
                .Should().BeEqualTo(string.Join("\n", new[] { XrayErrors(), invalid }.Where(error => error.Length > 0)))
                .Because(finalMask);
        }
    }

    [Test]
    public async Task TheTlsSettingsOfOtherProfilesDoNotApplyToAnAetherProfile()
    {
        // Columns an Aether profile never stores, as a hand-made profile might still carry them: none is checked.
        var reality = Aether("masque");
        reality.StreamSecurity = Global.StreamSecurityReality;
        reality.PublicKey = string.Empty;

        var tls = Aether("wg");
        tls.StreamSecurity = Global.StreamSecurity;
        tls.Cert = "not a certificate";
        tls.AllowInsecure = Global.StringTrue;
        tls.EchOutbound = "freedom";
        tls.EchConfigList = string.Empty;

        foreach (var profile in new[] { reality, tls })
        {
            var result = NodeValidator.Validate(profile, ECoreType.Xray);

            await string.Join("\n", result.Errors).Should().BeEqualTo(XrayErrors()).Because(profile.StreamSecurity);
            await result.Warnings.Count.Should().BeEqualTo(0).Because(profile.StreamSecurity);
        }
    }
}
