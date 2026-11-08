namespace ServiceLib.Tests.Fmt;

// From PattNG fmt/AetherFmtTest.kt. AetherFmt.ToUri writes the scheme itself, so PattNG's link(config) is ToUri here.
// An Aether profile's endpoint is Address/Port, "" and 0 meaning none (PattNG: server/serverPort, null meaning none),
// and its other settings are the Aether* fields of ProtocolExtraItem.
public class AetherFmtTests
{
    // PattNG AetherFmtTest.profile(): a profile with the options the tests do not change set; copy changes its Aether
    // settings and item the rest of it.
    private static ProfileItem Profile(Func<ProtocolExtraItem, ProtocolExtraItem>? copy = null, Action<ProfileItem>? item = null)
    {
        var profile = new ProfileItem
        {
            ConfigType = EConfigType.Aether,
            CoreType = ECoreType.Xray,
            Remarks = "My Node",
        };
        var extra = new ProtocolExtraItem
        {
            AetherProtocol = AetherProtocol.Masque.Type,
            AetherTransport = AetherTransport.Http3.Type,
            AetherScanMode = AetherScanMode.Balanced.Type,
            AetherObfuscation = AetherObfuscation.Balanced.Type,
            AetherIpVersion = AetherIpVersion.V4.Type,
        };
        profile.SetProtocolExtra(copy == null ? extra : copy(extra));
        item?.Invoke(profile);
        return profile;
    }

    // PattNG's profile.copy(...): a new profile, the one copied left as it was.
    private static ProfileItem Copy(ProfileItem profile, Func<ProtocolExtraItem, ProtocolExtraItem> change)
    {
        var copy = new ProfileItem
        {
            ConfigType = profile.ConfigType,
            CoreType = profile.CoreType,
            Remarks = profile.Remarks,
            Address = profile.Address,
            Port = profile.Port,
            Finalmask = profile.Finalmask,
            DialMode = profile.DialMode,
        };
        copy.SetProtocolExtra(change(profile.GetProtocolExtra()));
        return copy;
    }

    private static string Link(ProfileItem config)
    {
        return AetherFmt.ToUri(config)!;
    }

    private static ProfileItem? Parse(string link)
    {
        return AetherFmt.Resolve(link, out _);
    }

    // PattNG's normalize reads the listen port of the settings unless a screen passes one; that setting is 10819 in
    // these tests. AetherFmt.Normalize has no default, so that no caller forgets the setting.
    private static AetherProblem? Normalize(ProfileItem config, IEnumerable<int>? takenPorts = null, int listenPort = AetherPorts.DefaultListenPort)
    {
        return AetherFmt.Normalize(config, takenPorts, listenPort);
    }

    private static ProtocolExtraItem? Extra(ProfileItem? profile)
    {
        return profile?.GetProtocolExtra();
    }

    private static HashSet<int> Ports(params int[] ports)
    {
        return [.. ports];
    }

    // What a check gave beside what it checked, so that a failure inside a loop names it, as PattNG's messages do.
    private static string Labeled(string input, object? result)
    {
        return $"{input}: {result?.ToString() ?? "null"}";
    }

    // Kotlin's substringBefore and substringAfter, which the tests build crafted links with.
    private static string Before(string text, char delimiter)
    {
        var at = text.IndexOf(delimiter);
        return at < 0 ? text : text[..at];
    }

    private static string After(string text, char delimiter)
    {
        var at = text.IndexOf(delimiter);
        return at < 0 ? text : text[(at + 1)..];
    }

    // The query of link, each name with its value as the link writes it.
    private static Dictionary<string, string> Query(string link)
    {
        var query = new Dictionary<string, string>();
        foreach (var part in Before(After(link, '?'), '#').Split('&'))
        {
            query[Before(part, '=')] = After(part, '=');
        }
        return query;
    }

    [Test]
    public async Task APinnedMasqueNodeSurvivesTheRoundTrip()
    {
        var original = Profile(
            it => it with
            {
                AetherTransport = AetherTransport.Http2.Type,
                AetherScanMode = AetherScanMode.Verified.Type,
                AetherObfuscation = AetherObfuscation.Aggressive.Type,
                AetherIpVersion = AetherIpVersion.Dual.Type,
                AetherFragment = true,
            },
            it =>
            {
                it.Address = "162.159.198.1";
                it.Port = 443;
            });

        var parsed = Parse(Link(original));

        await parsed.Should().NotBeNull();
        await (parsed?.ConfigType).Should().BeEqualTo(EConfigType.Aether);
        await (parsed?.Remarks).Should().BeEqualTo("My Node");
        await (parsed?.Address).Should().BeEqualTo("162.159.198.1");
        await (parsed?.Port).Should().BeEqualTo(443);
        await (Extra(parsed)?.AetherProtocol).Should().BeEqualTo("masque");
        await (Extra(parsed)?.AetherTransport).Should().BeEqualTo("h2");
        await (Extra(parsed)?.AetherScanMode).Should().BeEqualTo("verified");
        // Over HTTP/2 obfuscation does nothing, and the link leaves it out.
        await (Extra(parsed)?.AetherObfuscation).Should().BeEqualTo("auto");
        await (Extra(parsed)?.AetherIpVersion).Should().BeEqualTo("both");
        await (Extra(parsed)?.AetherFragment).Should().BeEqualTo(true);
    }

    [Test]
    public async Task TheOldNameOfTheVerifiedScanModeStillReads()
    {
        await AetherScanMode.FromString("stealth").Should().BeEqualTo(AetherScanMode.Verified);
        await AetherScanMode.FromString("verified").Should().BeEqualTo(AetherScanMode.Verified);
        await AetherScanMode.FromString("quiet").Should().BeEqualTo(AetherScanMode.Balanced);
        // A link written before the rename.
        var parsed = Parse(Link(Profile()).Replace("scan=balanced", "scan=stealth"));
        await (Extra(parsed)?.AetherScanMode).Should().BeEqualTo("verified");
    }

    [Test]
    public async Task PsiphonSurvivesTheRoundTripAndStaysOutOfALinkWithoutIt()
    {
        var chained = Profile(it => it with
        {
            AetherPsiphon = "chain",
            AetherPsiphonMode = "cdn",
            AetherPsiphonCdnIps = "1.1.1.1,1.0.0.1",
            AetherPsiphonCdnSni = "a.example,b.example",
            AetherPsiphonRegion = "DE",
            AetherPsiphonBundledList = false,
            AetherPsiphonCdnSets = "cloudflare,fastly",
        });
        var parsed = Extra(Parse(Link(chained)));
        await (parsed?.AetherPsiphon).Should().BeEqualTo("chain");
        await (parsed?.AetherPsiphonMode).Should().BeEqualTo("cdn");
        await (parsed?.AetherPsiphonCdnIps).Should().BeEqualTo("1.1.1.1,1.0.0.1");
        await (parsed?.AetherPsiphonCdnSni).Should().BeEqualTo("a.example,b.example");
        await (parsed?.AetherPsiphonRegion).Should().BeEqualTo("DE");
        await (parsed?.AetherPsiphonBundledList).Should().BeEqualTo(false);
        await (parsed?.AetherPsiphonCdnSets).Should().BeEqualTo("cloudflare,fastly");

        var plain = AetherFmt.ToUri(Profile())!;
        await plain.Contains("psiphon").Should().BeFalse();
        var parsedPlain = Extra(Parse(Link(Profile())));
        await (parsedPlain?.AetherPsiphon).Should().BeNull();
        await (parsedPlain?.AetherPsiphonMode).Should().BeNull();

        // Starting from the bundled list is the default and needs no word in a link.
        await AetherFmt.ToUri(Profile(it => it with { AetherPsiphon = "chain" }))!.Contains("psiphon_bundled").Should().BeFalse();
        await (Extra(Parse(Link(Profile(it => it with { AetherPsiphon = "chain" }))))?.AetherPsiphonBundledList).Should().BeNull();
    }

    [Test]
    public async Task PsiphonSettingsAreNormalizedAndClearedWhenPsiphonIsOff()
    {
        var chained = Profile(it => it with
        {
            AetherPsiphon = "chain",
            AetherPsiphonMode = "made-up",
            AetherPsiphonCdnIps = " 1.1.1.1, 1.0.0.1  8.8.8.8 ",
            AetherPsiphonCdnSni = "",
            AetherPsiphonRegion = " de ",
            AetherPsiphonBundledList = true,
            AetherPsiphonCdnSets = " fastly, nowhere cloudflare fastly ",
        });
        await Normalize(chained).Should().BeNull();
        // The sets come out in the order the core tries them, once each, strangers left out.
        await chained.GetProtocolExtra().AetherPsiphonCdnSets.Should().BeEqualTo("cloudflare,fastly");
        await chained.GetProtocolExtra().AetherPsiphonMode.Should().BeEqualTo("auto");
        await chained.GetProtocolExtra().AetherPsiphonCdnIps.Should().BeEqualTo("1.1.1.1,1.0.0.1,8.8.8.8");
        await chained.GetProtocolExtra().AetherPsiphonCdnSni.Should().BeNull();
        await chained.GetProtocolExtra().AetherPsiphonRegion.Should().BeEqualTo("DE");
        // Yes is the default and is stored as nothing; no stays.
        await chained.GetProtocolExtra().AetherPsiphonBundledList.Should().BeNull();
        var fresh = Profile(it => it with { AetherPsiphon = "chain", AetherPsiphonBundledList = false });
        await Normalize(fresh).Should().BeNull();
        await fresh.GetProtocolExtra().AetherPsiphonBundledList.Should().BeEqualTo(false);

        var off = Profile(it => it with
        {
            AetherPsiphon = "off",
            AetherPsiphonMode = "cdn",
            AetherPsiphonRegion = "DE",
            AetherPsiphonBundledList = false,
            AetherPsiphonCdnSets = "fastly",
        });
        await Normalize(off).Should().BeNull();
        await off.GetProtocolExtra().AetherPsiphon.Should().BeNull();
        await off.GetProtocolExtra().AetherPsiphonMode.Should().BeNull();
        await off.GetProtocolExtra().AetherPsiphonRegion.Should().BeNull();
        await off.GetProtocolExtra().AetherPsiphonBundledList.Should().BeNull();
        await off.GetProtocolExtra().AetherPsiphonCdnSets.Should().BeNull();
    }

    [Test]
    public async Task PsiphonAroundTheTunnelNeedsMasqueAndPsiphonInsideItNeedsThePortAfterTheListenPort()
    {
        await Normalize(Profile(it => it with { AetherProtocol = AetherProtocol.WireGuard.Type, AetherPsiphon = "reverse" }))
            .Should().BeEqualTo(AetherProblem.PsiphonNeedsMasque);
        await Normalize(Profile(it => it with { AetherProtocol = AetherProtocol.Masque.Type, AetherPsiphon = "reverse" })).Should().BeNull();
        await Normalize(Profile(it => it with { AetherProtocol = AetherProtocol.Mim.Type, AetherPsiphon = "reverse" })).Should().BeNull();
        await Normalize(Profile(it => it with { AetherProtocol = AetherProtocol.Gool.Type, AetherPsiphon = "reverse" }))
            .Should().BeEqualTo(AetherProblem.PsiphonNeedsMasque);
        // WireGuard over MASQUE dials MASQUE alone, with its WireGuard inside that tunnel.
        await Normalize(Profile(it => it with { AetherProtocol = AetherProtocol.WgOverMasque.Type, AetherPsiphon = "reverse" })).Should().BeNull();
        await Normalize(Profile(it => it with { AetherProtocol = AetherProtocol.WgOverMasque.Type, AetherPsiphon = "chain" })).Should().BeNull();
        // WireGuard inside Psiphon is fine: Psiphon carries the tunnel only the other way round.
        await Normalize(Profile(it => it with { AetherProtocol = AetherProtocol.WireGuard.Type, AetherPsiphon = "chain" })).Should().BeNull();

        await Normalize(Profile(it => it with { AetherPsiphon = "chain" }), takenPorts: Ports(10820)).Should().BeEqualTo(AetherProblem.NextPortTaken);
        await Normalize(Profile(it => it with { AetherPsiphon = "chain" }), takenPorts: Ports(10819)).Should().BeEqualTo(AetherProblem.ListenPortTaken);
        await Normalize(Profile(it => it with { AetherPsiphon = "chain" }), takenPorts: Ports(10821)).Should().BeNull();
        // Without Psiphon inside, the port after the listen port is nobody's business.
        await Normalize(Profile(), takenPorts: Ports(10820)).Should().BeNull();
    }

    [Test]
    public async Task TorSurvivesTheRoundTripAndStaysOutOfALinkWithoutIt()
    {
        var bridged = Profile(it => it with
        {
            AetherTor = "only",
            AetherTorBridges = "own",
            AetherTorBridgeLines = "obfs4 192.0.2.55:38114 316E64 cert=abc iat-mode=0\nwebtunnel 198.51.100.25:443 7DD627 url=https://example.com/x",
        });
        var parsed = Extra(Parse(Link(bridged)));
        await (parsed?.AetherTor).Should().BeEqualTo("only");
        await (parsed?.AetherTorBridges).Should().BeEqualTo("own");
        await (parsed?.AetherTorBridgeLines).Should().BeEqualTo(bridged.GetProtocolExtra().AetherTorBridgeLines);

        var automatic = Extra(Parse(Link(Profile(it => it with { AetherTor = "chain" }))));
        await (automatic?.AetherTor).Should().BeEqualTo("chain");
        await (automatic?.AetherTorBridges).Should().BeEqualTo("auto");
        await (automatic?.AetherTorBridgeLines).Should().BeNull();

        var plain = AetherFmt.ToUri(Profile())!;
        await plain.Contains("tor=").Should().BeFalse();
        await plain.Contains("bridges").Should().BeFalse();
        await (Extra(Parse(Link(Profile())))?.AetherTor).Should().BeNull();
    }

    [Test]
    public async Task TheExitNodeSurvivesTheRoundTripUnderTheNamesOfAnOrdinaryLink()
    {
        const string mask = """{"tcp": [{"type": "fragment", "settings": {"packets": "tlshello", "length": "50-100"}}]}""";
        var masked = Profile(item: it =>
        {
            it.Finalmask = mask;
            it.DialMode = "code-1";
        });
        var uri = AetherFmt.ToUri(masked)!;
        await uri.Contains("fm=").Should().BeTrue();
        await uri.Contains("dialMode=code-1").Should().BeTrue();
        var parsed = Parse(Link(masked));
        // The link carries fm on one line, as the other formats write it (PattNG: as written), so it comes back compacted.
        await (parsed?.Finalmask).Should().BeEqualTo("""{"tcp":[{"type":"fragment","settings":{"packets":"tlshello","length":"50-100"}}]}""");
        await JsonNode.DeepEquals(JsonUtils.ParseJson(parsed?.Finalmask), JsonUtils.ParseJson(mask)).Should().BeTrue();
        await (parsed?.DialMode).Should().BeEqualTo("code-1");

        var plain = AetherFmt.ToUri(Profile())!;
        await plain.Contains("fm=").Should().BeFalse();
        await plain.Contains("dialMode").Should().BeFalse();
        var bare = Parse(Link(Profile()));
        await string.IsNullOrEmpty(bare?.Finalmask).Should().BeTrue();
        await string.IsNullOrEmpty(bare?.DialMode).Should().BeTrue();
    }

    [Test]
    public async Task TorSettingsAreNormalizedAndClearedWhenTorIsOff()
    {
        var own = Profile(it => it with
        {
            AetherTor = "chain",
            AetherTorBridges = "own",
            AetherTorBridgeLines = " Bridge obfs4 192.0.2.55:38114 316E64 cert=abc iat-mode=0 \n# a comment\n\nbridge webtunnel 198.51.100.25:443 7DD627 url=https://example.com/x\n",
        });
        await Normalize(own).Should().BeNull();
        await own.GetProtocolExtra().AetherTorBridgeLines.Should().BeEqualTo(
            "obfs4 192.0.2.55:38114 316E64 cert=abc iat-mode=0\nwebtunnel 198.51.100.25:443 7DD627 url=https://example.com/x");

        // Lines are kept only with the setting that uses them, and an unknown setting is the automatic one.
        var automatic = Profile(it => it with
        {
            AetherTor = "chain",
            AetherTorBridges = "made-up",
            AetherTorBridgeLines = "obfs4 192.0.2.55:38114 316E64 cert=abc",
        });
        await Normalize(automatic).Should().BeNull();
        await automatic.GetProtocolExtra().AetherTorBridges.Should().BeEqualTo("auto");
        await automatic.GetProtocolExtra().AetherTorBridgeLines.Should().BeNull();

        var off = Profile(it => it with
        {
            AetherTor = "off",
            AetherTorBridges = "first",
            AetherTorBridgeLines = "obfs4 192.0.2.55:38114 316E64 cert=abc",
        });
        await Normalize(off).Should().BeNull();
        await off.GetProtocolExtra().AetherTor.Should().BeNull();
        await off.GetProtocolExtra().AetherTorBridges.Should().BeNull();
        await off.GetProtocolExtra().AetherTorBridgeLines.Should().BeNull();

        await Normalize(Profile(it => it with { AetherTor = "only", AetherTorBridges = "own", AetherTorBridgeLines = "# nothing here\n" }))
            .Should().BeEqualTo(AetherProblem.TorBridgesMissing);
    }

    [Test]
    public async Task TorAroundTheTunnelNeedsMasqueAndTorAndPsiphonOnlyNest()
    {
        await Normalize(Profile(it => it with { AetherProtocol = AetherProtocol.WireGuard.Type, AetherTor = "reverse" }))
            .Should().BeEqualTo(AetherProblem.TorNeedsMasque);
        await Normalize(Profile(it => it with { AetherProtocol = AetherProtocol.Gool.Type, AetherTor = "reverse" }))
            .Should().BeEqualTo(AetherProblem.TorNeedsMasque);
        await Normalize(Profile(it => it with { AetherProtocol = AetherProtocol.WgOverMasque.Type, AetherTor = "reverse" })).Should().BeNull();
        await Normalize(Profile(it => it with { AetherProtocol = AetherProtocol.Mim.Type, AetherTor = "reverse" })).Should().BeNull();
        // Inside the tunnel or alone, Tor does not care what carries WARP.
        await Normalize(Profile(it => it with { AetherProtocol = AetherProtocol.WireGuard.Type, AetherTor = "chain" })).Should().BeNull();
        await Normalize(Profile(it => it with { AetherProtocol = AetherProtocol.WireGuard.Type, AetherTor = "only" })).Should().BeNull();

        await Normalize(Profile(it => it with { AetherTor = "chain", AetherPsiphon = "reverse" })).Should().BeNull();
        await Normalize(Profile(it => it with { AetherTor = "reverse", AetherPsiphon = "chain" })).Should().BeNull();
        var clashes = new[] { ("chain", "chain"), ("reverse", "reverse"), ("only", "chain"), ("only", "only"), ("chain", "only"), ("reverse", "only") };
        foreach (var (tor, psiphon) in clashes)
        {
            var problem = Normalize(Profile(it => it with { AetherTor = tor, AetherPsiphon = psiphon }));
            await Labeled($"tor={tor} psiphon={psiphon}", problem).Should().BeEqualTo(Labeled($"tor={tor} psiphon={psiphon}", AetherProblem.TorPsiphonConflict));
        }
    }

    [Test]
    public async Task ACommandCannotListenOnTheSecondarySocksPort()
    {
        // The editor holds the ports of a core to the local proxy ports and the Aether secondary SOCKS port together.
        var secondary = AetherPorts.SecondarySocksPortOf(AetherPorts.DefaultListenPort);
        var taken = Ports(10808, 10809, secondary);
        await Normalize(Profile(it => it with { AetherCommand = $"aether --wg --bind 127.0.0.1:{secondary}" }), taken)
            .Should().BeEqualTo(AetherProblem.ListenPortTaken);
        // On the Aether listen port, the most that Tor and Psiphon take leaves it free.
        await Normalize(Profile(it => it with { AetherPsiphon = "chain", AetherTor = "reverse" }), taken).Should().BeNull();
    }

    [Test]
    public async Task TorTakesThePortAfterTheListenPortInsideOrAroundTheTunnel()
    {
        await Normalize(Profile(it => it with { AetherTor = "chain" }), takenPorts: Ports(10820)).Should().BeEqualTo(AetherProblem.NextPortTaken);
        await Normalize(Profile(it => it with { AetherTor = "reverse" }), takenPorts: Ports(10820)).Should().BeEqualTo(AetherProblem.NextPortTaken);
        await Normalize(Profile(it => it with { AetherTor = "only" }), takenPorts: Ports(10820)).Should().BeNull();
        // Nested, Psiphon inside and Tor around, the core takes two ports after the listen port.
        await Normalize(Profile(it => it with { AetherPsiphon = "chain", AetherTor = "reverse" }), takenPorts: Ports(10821))
            .Should().BeEqualTo(AetherProblem.NextPortTaken);
        await Normalize(Profile(it => it with { AetherPsiphon = "chain", AetherTor = "reverse" }), takenPorts: Ports(10822)).Should().BeNull();
    }

    [Test]
    public async Task TheFingerprintRidesWithMasqueAndChromesStaysOutOfALink()
    {
        foreach (var fingerprint in new[] { "firefox", "semi-python", "go" })
        {
            foreach (var transport in AetherTransport.All)
            {
                var text = Link(Profile(it => it with { AetherTransport = transport.Type, AetherFingerprint = fingerprint }));
                await Labeled(text, text.Contains($"fingerprint={fingerprint}")).Should().BeEqualTo(Labeled(text, true));
                await (Extra(Parse(text))?.AetherFingerprint).Should().BeEqualTo(fingerprint);
            }
        }
        var chrome = Link(Profile(it => it with { AetherFingerprint = "chrome" }));
        await chrome.Contains("fingerprint=").Should().BeFalse();
        await (Extra(Parse(chrome))?.AetherFingerprint).Should().BeNull();
        // WireGuard has no TLS handshake of its own to shape.
        await Link(Profile(it => it with { AetherProtocol = AetherProtocol.WireGuard.Type, AetherFingerprint = "go" })).Contains("fingerprint=").Should().BeFalse();
    }

    [Test]
    public async Task ObfuscationStaysOutOfTheLinkOfMasqueOverHttp2()
    {
        await Link(Profile()).Contains("noize=balanced").Should().BeTrue();
        await Link(Profile(it => it with { AetherTransport = AetherTransport.Http2.Type })).Contains("noize=").Should().BeFalse();
        // Tor or Psiphon around the tunnel carry TCP alone, so the core takes HTTP/2 whatever the transport says.
        await Link(Profile(it => it with { AetherPsiphon = "reverse" })).Contains("noize=").Should().BeFalse();
        await Link(Profile(it => it with { AetherTor = "reverse" })).Contains("noize=").Should().BeFalse();
        // WireGuard keeps it whatever the transport says.
        await Link(Profile(it => it with { AetherProtocol = AetherProtocol.WireGuard.Type, AetherTransport = AetherTransport.Http2.Type }))
            .Contains("noize=balanced").Should().BeTrue();
    }

    [Test]
    public async Task AutomaticObfuscationStaysOutOfALinkAndEveryProfileOfTheCoreRoundTrips()
    {
        var automatic = Link(Profile(it => it with { AetherObfuscation = AetherObfuscation.Auto.Type }));
        await automatic.Contains("noize=").Should().BeFalse();
        await (Extra(Parse(automatic))?.AetherObfuscation).Should().BeEqualTo("auto");
        foreach (var named in new[] { "off", "light", "firewall", "balanced", "gfw", "aggressive" })
        {
            await (Extra(Parse(Link(Profile(it => it with { AetherObfuscation = named }))))?.AetherObfuscation).Should().BeEqualTo(named);
        }
    }

    [Test]
    public async Task TheEchResolverAndDomainSurviveTheRoundTripWhileEchIsOn()
    {
        var tuned = Profile(it => it with
        {
            AetherEch = true,
            AetherEchDns = "https://doq.dns4all.eu/dns-query",
            AetherEchDomain = "ip.gs",
        });
        var parsed = Extra(Parse(Link(tuned)));
        await (parsed?.AetherEchDns).Should().BeEqualTo("https://doq.dns4all.eu/dns-query");
        await (parsed?.AetherEchDomain).Should().BeEqualTo("ip.gs");

        // Without ECH a link says nothing of them, and they are not taken from one.
        var off = Link(Profile(it => it with { AetherEchDns = "tcp://1.1.1.1", AetherEchDomain = "ip.gs" }));
        await off.Contains("ech_dns=").Should().BeFalse();
        await off.Contains("ech_domain=").Should().BeFalse();
        var plain = Link(Profile());
        var strayLink = Before(plain, '#') + "&ech_dns=tcp%3A%2F%2F1.1.1.1&ech_domain=ip.gs#" + After(plain, '#');
        await Before(strayLink, '#').Contains("ech_dns=").Should().BeTrue();
        var stray = Extra(Parse(strayLink));
        await (stray?.AetherEchDns).Should().BeNull();
        await (stray?.AetherEchDomain).Should().BeNull();
    }

    [Test]
    public async Task TheEchResolverIsUdpOrTcpWithAnIpAddressOrAnHttpsUrl()
    {
        var good = new[]
        {
            "udp://1.0.0.1",
            "udp://1.1.1.1:5353",
            "tcp://8.8.8.8",
            "TCP://[2606:4700:4700::1111]:53",
            "tcp://[::1]",
            "udp://2606:4700::1111",
            "https://doq.dns4all.eu/dns-query",
            "https://1.1.1.1:8443/dns-query",
            // Where the connection goes and what the ClientHello names, each once and in either order.
            "https://1.1.1.1/dns-query@sni=www.microsoft.com",
            "https://doq.dns4all.eu/dns-query@address=194.0.5.3",
            "https://doq.dns4all.eu/dns-query@address=2.2.2.2@sni=google.com",
            "https://doq.dns4all.eu/dns-query@SNI=google.com@address=[2606:4700::1111]",
            "https://doq.dns4all.eu/dns-query@address=2606:4700::1111",
            "https://doq.dns4all.eu/dns-query@address=front.example.net",
        };
        foreach (var dns in good)
        {
            var config = Profile(it => it with { AetherEch = true, AetherEchDns = $" {dns} " });
            await Labeled(dns, Normalize(config)).Should().BeEqualTo(Labeled(dns, null));
            await config.GetProtocolExtra().AetherEchDns.Should().BeEqualTo(dns);
        }
        var bad = new[]
        {
            "1.1.1.1",
            "udp://dns.google",
            "tls://1.1.1.1",
            "udp://1.1.1.1:70000",
            "udp://",
            "https://",
            "https:///dns-query",
            // The core reads no bracketed IPv4 address, no space and no digit outside ASCII, as a Persian keyboard types.
            "udp://[1.1.1.1]",
            "udp://[1.1.1.1]:53",
            "udp:// 1.1.1.1",
            "udp://1.1.1.1 :53",
            "udp://1.1.1.1: 53",
            "udp://۱.۱.۱.۱",
            "udp://1.1.1.1:۵۳",
            "udp://１.１.１.１",
            "https://dns example/dns-query",
            "https://dns.example/dns-query?x='1'",
            // What the core refuses after a DoH URL: an empty or unusable setting, a port, an IP address as the
            // server name, a setting named twice or one it does not know.
            "https://@address=2.2.2.2",
            "https://doq.dns4all.eu/dns-query@address=",
            "https://doq.dns4all.eu/dns-query@address=2.2.2.2:443",
            "https://doq.dns4all.eu/dns-query@sni=",
            "https://doq.dns4all.eu/dns-query@sni=2.2.2.2",
            "https://doq.dns4all.eu/dns-query@sni=[::1]",
            "https://doq.dns4all.eu/dns-query@sni=google.com@sni=bing.com",
            "https://doq.dns4all.eu/dns-query@address=1.1.1.1@address=2.2.2.2",
            "https://doq.dns4all.eu/dns-query@port=443",
            "https://doq.dns4all.eu/dns-query@google.com",
        };
        foreach (var dns in bad)
        {
            var problem = Normalize(Profile(it => it with { AetherEch = true, AetherEchDns = dns }));
            await Labeled(dns, problem).Should().BeEqualTo(Labeled(dns, AetherProblem.InvalidEchDns));
        }
    }

    [Test]
    public async Task TheEchDomainIsADomainName()
    {
        foreach (var domain in new[] { "crypto.cloudflare.com", "ip.gs", "ip.gs.", "_ech.example.com" })
        {
            var config = Profile(it => it with { AetherEch = true, AetherEchDomain = domain });
            await Labeled(domain, Normalize(config)).Should().BeEqualTo(Labeled(domain, null));
            await config.GetProtocolExtra().AetherEchDomain.Should().BeEqualTo(domain);
        }
        foreach (var domain in new[] { "a..b", "with space.com", "https://ip.gs", "ip.gs/", $"{new string('a', 64)}.com", "--upstream", "-ip.gs", "ip-.gs" })
        {
            var problem = Normalize(Profile(it => it with { AetherEch = true, AetherEchDomain = domain }));
            await Labeled(domain, problem).Should().BeEqualTo(Labeled(domain, AetherProblem.InvalidEchDomain));
        }
    }

    [Test]
    public async Task EchFieldsOutOfUseNeitherBlockSavingNorLoseWhatWasWritten()
    {
        var masque = Profile(it => it with { AetherEch = true, AetherEchDns = "udp://dns.google", AetherEchDomain = "--upstream" });
        await Normalize(masque).Should().BeEqualTo(AetherProblem.InvalidEchDns);

        var shapes = new (string Name, Func<ProtocolExtraItem, ProtocolExtraItem> Shape)[]
        {
            ("ech off", it => it with { AetherEch = false }),
            ("wg", it => it with { AetherProtocol = AetherProtocol.WireGuard.Type }),
            ("psiphon only", it => it with { AetherPsiphon = "only" }),
            ("tor only", it => it with { AetherTor = "only" }),
        };
        foreach (var (name, shape) in shapes)
        {
            // Kept as written, as the WARP keys window keeps its own, to be put right when ECH is next in use.
            var waiting = Profile(it => shape(it with { AetherEch = true, AetherEchDns = " udp://dns.google ", AetherEchDomain = "--upstream" }));
            await Labeled(name, Normalize(waiting)).Should().BeEqualTo(Labeled(name, null));
            await waiting.GetProtocolExtra().AetherEchDns.Should().BeEqualTo("udp://dns.google");
            await waiting.GetProtocolExtra().AetherEchDomain.Should().BeEqualTo("--upstream");
            // Out of use, neither reaches the core nor a link.
            var arguments = AetherArguments.Build(waiting, 10819);
            var reaching = arguments.Any(it => it.StartsWith("--ech", StringComparison.Ordinal) || it == "--upstream");
            await Labeled($"{name}: {string.Join(' ', arguments)}", reaching).Should().BeEqualTo(Labeled($"{name}: {string.Join(' ', arguments)}", false));
            await Labeled(name, Link(waiting).Contains("ech")).Should().BeEqualTo(Labeled(name, false));

            var kept = Profile(it => shape(it with { AetherEch = true, AetherEchDns = "tcp://8.8.8.8", AetherEchDomain = "ip.gs" }));
            await Normalize(kept).Should().BeNull();
            await kept.GetProtocolExtra().AetherEchDns.Should().BeEqualTo("tcp://8.8.8.8");
            await kept.GetProtocolExtra().AetherEchDomain.Should().BeEqualTo("ip.gs");
        }

        // In use, a link carries ECH, but never a value the core would refuse.
        var uncheckedLink = Link(Profile(it => it with { AetherEch = true, AetherEchDns = "udp://dns.google", AetherEchDomain = "ip.gs" }));
        await uncheckedLink.Contains("ech=1").Should().BeTrue();
        await uncheckedLink.Contains("ech_dns=").Should().BeFalse();
        await uncheckedLink.Contains("ech_domain=ip.gs").Should().BeTrue();
    }

    [Test]
    public async Task ALinkGivesNoEchResolverOrDomainTheCoreWouldRefuse()
    {
        var plain = Link(Profile(it => it with { AetherEch = true }));
        var crafted = Before(plain, '#') + "&ech_dns=udp%3A%2F%2F%5B1.1.1.1%5D&ech_domain=--upstream#" + After(plain, '#');
        await Before(crafted, '#').Contains("ech_domain=").Should().BeTrue();
        var parsed = Extra(Parse(crafted));
        await (parsed?.AetherEch).Should().BeEqualTo(true);
        await (parsed?.AetherEchDns).Should().BeNull();
        await (parsed?.AetherEchDomain).Should().BeNull();
    }

    [Test]
    public async Task TheDefaultEchResolverAndDomainAreLeftToTheDefaults()
    {
        var config = Profile(it => it with
        {
            AetherEch = true,
            AetherEchDns = AetherArguments.DefaultEchDns,
            AetherEchDomain = $" {AetherArguments.DefaultEchDomain} ",
        });
        await Normalize(config).Should().BeNull();
        await config.GetProtocolExtra().AetherEchDns.Should().BeNull();
        await config.GetProtocolExtra().AetherEchDomain.Should().BeNull();
        // Others are kept with ECH off as well.
        var off = Profile(it => it with { AetherEchDns = "tcp://1.1.1.1", AetherEchDomain = "ip.gs" });
        await Normalize(off).Should().BeNull();
        await off.GetProtocolExtra().AetherEchDns.Should().BeEqualTo("tcp://1.1.1.1");
        await off.GetProtocolExtra().AetherEchDomain.Should().BeEqualTo("ip.gs");
    }

    [Test]
    public async Task TheMasqueServerNameIsOneTheCoreTakes()
    {
        // As the core takes it: a domain name, with a trailing dot or without.
        foreach (var name in new[] { "www.cloudflare.com", "consumer-masque.cloudflareclient.com", "www.cloudflare.com.", "localhost", "_sni.example.com" })
        {
            await Labeled(name, AetherFmt.IsMasqueSni(name)).Should().BeEqualTo(Labeled(name, true));
        }
        // No IPv4 address as the core's parser reads one; what it reads as none is a name like any other.
        foreach (var name in new[] { "1.1.1.1", "1.1.1.1.", "255.255.255.255", "0.0.0.0" })
        {
            await Labeled(name, AetherFmt.IsMasqueSni(name)).Should().BeEqualTo(Labeled(name, false));
        }
        foreach (var name in new[] { "1.1.1", "256.1.1.1", "01.1.1.1" })
        {
            await Labeled(name, AetherFmt.IsMasqueSni(name)).Should().BeEqualTo(Labeled(name, true));
        }
        var bad = new[]
        {
            "",
            ".",
            "www..cloudflare.com",
            "www.cloudflare.com..",
            "www cloudflare com",
            "www.cloudflare.com:443",
            "https://www.cloudflare.com",
            "2606:4700:4700::1111",
            "[2606:4700:4700::1111]",
            $"{new string('a', 64)}.com",
            "--upstream",
            "-www.cloudflare.com",
        };
        foreach (var name in bad)
        {
            await Labeled(name, AetherFmt.IsMasqueSni(name)).Should().BeEqualTo(Labeled(name, false));
        }
    }

    [Test]
    public async Task AMasqueServerNameTheCoreWouldRefuseIsRefusedOnlyWhileAMasqueTunnelTakesIt()
    {
        foreach (var protocol in new[] { AetherProtocol.Masque, AetherProtocol.Mim, AetherProtocol.WgOverMasque })
        {
            foreach (var name in new[] { "1.1.1.1", "www..cloudflare.com", "--upstream" })
            {
                var config = Profile(it => it with { AetherProtocol = protocol.Type, AetherMasqueSni = name });
                await Labeled($"{protocol.Type} {name}", Normalize(config))
                    .Should().BeEqualTo(Labeled($"{protocol.Type} {name}", AetherProblem.InvalidMasqueSni));
            }
        }

        var shapes = new (string Name, Func<ProtocolExtraItem, ProtocolExtraItem> Shape)[]
        {
            ("wg", it => it with { AetherProtocol = AetherProtocol.WireGuard.Type }),
            ("gool", it => it with { AetherProtocol = AetherProtocol.Gool.Type }),
            ("psiphon only", it => it with { AetherPsiphon = "only" }),
            ("tor only", it => it with { AetherTor = "only" }),
        };
        foreach (var (name, shape) in shapes)
        {
            // Kept as written, trimmed, to be put right when a MASQUE tunnel next takes it.
            var waiting = Profile(it => shape(it with { AetherMasqueSni = " 1.1.1.1 " }));
            await Labeled(name, Normalize(waiting)).Should().BeEqualTo(Labeled(name, null));
            await waiting.GetProtocolExtra().AetherMasqueSni.Should().BeEqualTo("1.1.1.1");
            // Out of use, it reaches neither the core nor a link.
            var arguments = AetherArguments.Build(waiting, 10819);
            await Labeled($"{name}: {string.Join(' ', arguments)}", arguments.Contains("--masque-sni"))
                .Should().BeEqualTo(Labeled($"{name}: {string.Join(' ', arguments)}", false));
            await Query(Link(waiting)).GetValueOrDefault("sni").Should().BeNull();
        }
    }

    [Test]
    public async Task TheDefaultMasqueServerNameIsLeftToTheDefault()
    {
        foreach (var value in new[] { $" {AetherArguments.DefaultMasqueSni} ", "www.cloudflare.com", " " })
        {
            var config = Profile(it => it with { AetherMasqueSni = value });
            await Labeled(value, Normalize(config)).Should().BeEqualTo(Labeled(value, null));
            await Labeled(value, config.GetProtocolExtra().AetherMasqueSni).Should().BeEqualTo(Labeled(value, null));
        }
        // Another is kept as written, trimmed; the core leaves out a trailing dot itself.
        var own = Profile(it => it with { AetherMasqueSni = " consumer-masque.cloudflareclient.com. " });
        await Normalize(own).Should().BeNull();
        await own.GetProtocolExtra().AetherMasqueSni.Should().BeEqualTo("consumer-masque.cloudflareclient.com.");
    }

    [Test]
    public async Task ALinkCarriesAMasqueServerNameOfItsOwnOverMasque()
    {
        foreach (var protocol in new[] { AetherProtocol.Masque, AetherProtocol.Mim, AetherProtocol.WgOverMasque })
        {
            var own = Link(Profile(it => it with { AetherProtocol = protocol.Type, AetherMasqueSni = "consumer-masque.cloudflareclient.com" }));
            await Labeled(own, Query(own).GetValueOrDefault("sni")).Should().BeEqualTo(Labeled(own, "consumer-masque.cloudflareclient.com"));
            await Labeled(own, Extra(Parse(own))?.AetherMasqueSni).Should().BeEqualTo(Labeled(own, "consumer-masque.cloudflareclient.com"));
        }
        // The default needs no word, and a link without one leaves the profile to the default.
        foreach (var value in new[] { null, AetherArguments.DefaultMasqueSni })
        {
            var plain = Link(Profile(it => it with { AetherMasqueSni = value }));
            await Labeled(plain, Query(plain).GetValueOrDefault("sni")).Should().BeEqualTo(Labeled(plain, null));
            await Labeled(plain, Extra(Parse(plain))?.AetherMasqueSni).Should().BeEqualTo(Labeled(plain, null));
        }
        // A name the core would refuse goes into no link and is taken from none, and WireGuard takes none at all.
        await Query(Link(Profile(it => it with { AetherMasqueSni = "1.1.1.1" }))).GetValueOrDefault("sni").Should().BeNull();
        var crafted = new[]
        {
            (Link(Profile()), "1.1.1.1"),
            (Link(Profile()), "--upstream"),
            (Link(Profile(it => it with { AetherProtocol = AetherProtocol.WireGuard.Type })), "consumer-masque.cloudflareclient.com"),
        };
        foreach (var (plain, name) in crafted)
        {
            var stray = Before(plain, '#') + $"&sni={name}#" + After(plain, '#');
            await Labeled(stray, Query(stray).GetValueOrDefault("sni")).Should().BeEqualTo(Labeled(stray, name));
            await Labeled(stray, Extra(Parse(stray))?.AetherMasqueSni).Should().BeEqualTo(Labeled(stray, null));
        }
    }

    [Test]
    public async Task EncryptedClientHelloTheResolversAndTheExitRuleSurviveTheRoundTrip()
    {
        var tuned = Profile(it => it with
        {
            AetherEch = true,
            AetherDns = "1.1.1.1,10.0.0.1:5353",
            AetherExitLoc = "!IR,RU",
        });
        var parsed = Extra(Parse(Link(tuned)));
        await (parsed?.AetherEch).Should().BeEqualTo(true);
        await (parsed?.AetherDns).Should().BeEqualTo("1.1.1.1,10.0.0.1:5353");
        await (parsed?.AetherExitLoc).Should().BeEqualTo("!IR,RU");

        var plain = AetherFmt.ToUri(Profile())!;
        await plain.Contains("ech=").Should().BeFalse();
        await plain.Contains("dns=").Should().BeFalse();
        await plain.Contains("exit_loc=").Should().BeFalse();
        // ECH rides with the MASQUE handshake alone.
        await Link(Profile(it => it with { AetherProtocol = AetherProtocol.WireGuard.Type, AetherEch = true })).Contains("ech=").Should().BeFalse();
        await Link(Profile(it => it with { AetherProtocol = AetherProtocol.Mim.Type, AetherEch = true })).Contains("ech=1").Should().BeTrue();
    }

    [Test]
    public async Task TheResolversAreCheckedAndWrittenBackWithCommas()
    {
        var mixed = Profile(it => it with { AetherDns = " 1.1.1.1, [2606:4700:4700::1111]:53  8.8.8.8;10.0.0.1:5353 " });
        await Normalize(mixed).Should().BeNull();
        await mixed.GetProtocolExtra().AetherDns.Should().BeEqualTo("1.1.1.1,[2606:4700:4700::1111]:53,8.8.8.8,10.0.0.1:5353");

        var bare6 = Profile(it => it with { AetherDns = "2606:4700:4700::1111" });
        await Normalize(bare6).Should().BeNull();
        await bare6.GetProtocolExtra().AetherDns.Should().BeEqualTo("2606:4700:4700::1111");

        var blank = Profile(it => it with { AetherDns = " , " });
        await Normalize(blank).Should().BeNull();
        await blank.GetProtocolExtra().AetherDns.Should().BeNull();

        await Normalize(Profile(it => it with { AetherDns = "dns.google" })).Should().BeEqualTo(AetherProblem.InvalidDns);
        await Normalize(Profile(it => it with { AetherDns = "1.1.1.1,10.0.0.1:70000" })).Should().BeEqualTo(AetherProblem.InvalidDns);
    }

    [Test]
    public async Task TheExitRuleIsCheckedAndWrittenInCapitals()
    {
        var refused = Profile(it => it with { AetherExitLoc = " ! ir, ru " });
        await Normalize(refused).Should().BeNull();
        await refused.GetProtocolExtra().AetherExitLoc.Should().BeEqualTo("!IR,RU");

        var allowed = Profile(it => it with { AetherExitLoc = "de,se" });
        await Normalize(allowed).Should().BeNull();
        await allowed.GetProtocolExtra().AetherExitLoc.Should().BeEqualTo("DE,SE");

        var blank = Profile(it => it with { AetherExitLoc = "  " });
        await Normalize(blank).Should().BeNull();
        await blank.GetProtocolExtra().AetherExitLoc.Should().BeNull();

        await Normalize(Profile(it => it with { AetherExitLoc = "Germany" })).Should().BeEqualTo(AetherProblem.InvalidExitLoc);
        await Normalize(Profile(it => it with { AetherExitLoc = "DE,,SE" })).Should().BeEqualTo(AetherProblem.InvalidExitLoc);
        await Normalize(Profile(it => it with { AetherExitLoc = "DE!" })).Should().BeEqualTo(AetherProblem.InvalidExitLoc);
    }

    [Test]
    public async Task TheBridgePoolRidesWithTorAndIsClearedWithIt()
    {
        await (Extra(Parse(Link(Profile(it => it with { AetherTor = "only", AetherTorRelays = "only" }))))?.AetherTorRelays).Should().BeEqualTo("only");
        await (Extra(Parse(Link(Profile(it => it with { AetherTor = "chain" }))))?.AetherTorRelays).Should().BeEqualTo("auto");
        await Link(Profile(it => it with { AetherTorRelays = "only" })).Contains("tor_relays").Should().BeFalse();

        var normalized = Profile(it => it with { AetherTor = "chain", AetherTorRelays = "made-up" });
        await Normalize(normalized).Should().BeNull();
        await normalized.GetProtocolExtra().AetherTorRelays.Should().BeEqualTo("auto");

        var off = Profile(it => it with { AetherTor = "off", AetherTorRelays = "only" });
        await Normalize(off).Should().BeNull();
        await off.GetProtocolExtra().AetherTorRelays.Should().BeNull();
    }

    [Test]
    public async Task ACommandWrittenInPlaceOfTheSettingsIsCheckedAndTrimmed()
    {
        var trimmed = Profile(it => it with { AetherCommand = "  aether --wg --bind 127.0.0.1:20808  " });
        await Normalize(trimmed).Should().BeNull();
        await trimmed.GetProtocolExtra().AetherCommand.Should().BeEqualTo("aether --wg --bind 127.0.0.1:20808");

        var blank = Profile(it => it with { AetherCommand = "   " });
        await Normalize(blank).Should().BeNull();
        await blank.GetProtocolExtra().AetherCommand.Should().BeNull();

        await Normalize(Profile(it => it with { AetherCommand = "aether" })).Should().BeEqualTo(AetherProblem.InvalidCommand);
        await Normalize(Profile(it => it with { AetherCommand = "aether --wg --bind 20808" })).Should().BeEqualTo(AetherProblem.InvalidCommand);
        // The ports a command names are held to the same rule as a listen port.
        await Normalize(Profile(it => it with { AetherCommand = "aether --wg --bind 127.0.0.1:10808" }), takenPorts: Ports(10808))
            .Should().BeEqualTo(AetherProblem.ListenPortTaken);
        await Normalize(
                Profile(it => it with { AetherCommand = "aether --psiphon --bind 127.0.0.1:10808 --psiphon-bind 127.0.0.1:10819" }),
                takenPorts: Ports(10808))
            .Should().BeEqualTo(AetherProblem.ListenPortTaken);
    }

    [Test]
    public async Task FragmentValuesSurviveTheRoundTrip()
    {
        var uri = Link(Profile(it => it with
        {
            AetherTransport = AetherTransport.Http2.Type,
            AetherFragment = true,
            AetherFragmentSize = "16-32",
            AetherFragmentDelay = "5",
        }));

        var parsed = Extra(Parse(uri));
        await (parsed?.AetherFragmentSize).Should().BeEqualTo("16-32");
        await (parsed?.AetherFragmentDelay).Should().BeEqualTo("5");

        var broken = Extra(Parse("aether://?protocol=masque&transport=h2&fragment=1&fragment_size=0&fragment_delay=x#X"));
        await (broken?.AetherFragmentSize).Should().BeNull();
        await (broken?.AetherFragmentDelay).Should().BeNull();
    }

    [Test]
    public async Task FragmentValuesAreCheckedOnlyWhenTheyAreUsed()
    {
        var used = Profile(it => it with
        {
            AetherTransport = AetherTransport.Http2.Type,
            AetherFragment = true,
            AetherFragmentSize = "32 - 16",
            AetherFragmentDelay = "",
        });
        await Normalize(used).Should().BeNull();
        await used.GetProtocolExtra().AetherFragmentSize.Should().BeEqualTo("16-32");
        await used.GetProtocolExtra().AetherFragmentDelay.Should().BeNull();

        await Normalize(Copy(used, it => it with { AetherFragmentDelay = "5000" })).Should().BeEqualTo(AetherProblem.InvalidFragment);

        var unused = Copy(used, it => it with { AetherTransport = AetherTransport.Http3.Type, AetherFragmentDelay = "5000" });
        await Normalize(unused).Should().BeNull();
        await unused.GetProtocolExtra().AetherFragmentDelay.Should().BeNull();

        // Masque-in-masque fragments on the HTTP/2 carrier as MASQUE does; WireGuard never does.
        await Normalize(Copy(used, it => it with { AetherProtocol = AetherProtocol.Mim.Type, AetherFragmentDelay = "5000" }))
            .Should().BeEqualTo(AetherProblem.InvalidFragment);
        await Normalize(Copy(used, it => it with { AetherProtocol = AetherProtocol.WireGuard.Type, AetherFragmentDelay = "5000" })).Should().BeNull();
    }

    [Test]
    public async Task BothGoolHopsSurviveTheRoundTrip()
    {
        var original = Profile(
            it => it with
            {
                AetherProtocol = AetherProtocol.Gool.Type,
                AetherWiwOuter = "162.159.192.1:2408",
                AetherWiwInner = "[2606:4700:d0::a29f:c001]:894",
            },
            it => it.Remarks = "Gool");

        var parsed = Parse(Link(original));

        await (Extra(parsed)?.AetherProtocol).Should().BeEqualTo("gool");
        await (Extra(parsed)?.AetherWiwOuter).Should().BeEqualTo("162.159.192.1:2408");
        await (Extra(parsed)?.AetherWiwInner).Should().BeEqualTo("[2606:4700:d0::a29f:c001]:894");
        await (parsed?.Address).Should().BeEqualTo(string.Empty);
        await (parsed?.Port).Should().BeEqualTo(0);
    }

    [Test]
    public async Task ANodeLeftToTheScannerCarriesNoEndpoint()
    {
        var uri = Link(Profile());

        await uri.StartsWith("aether://?", StringComparison.Ordinal).Should().BeTrue();

        var parsed = Parse(uri);
        await (parsed?.Address).Should().BeEqualTo(string.Empty);
        await (parsed?.Port).Should().BeEqualTo(0);
        await (Extra(parsed)?.AetherProtocol).Should().BeEqualTo("masque");
    }

    [Test]
    public async Task AnIpv6EndpointSurvivesTheRoundTrip()
    {
        var uri = Link(Profile(item: it =>
        {
            it.Address = "2606:4700:d0::a29f:c001";
            it.Port = 443;
        }));
        await uri.StartsWith("aether://[2606:4700:d0::a29f:c001]:443?", StringComparison.Ordinal).Should().BeTrue();

        var parsed = Parse(uri);
        await (parsed?.Address).Should().BeEqualTo("2606:4700:d0::a29f:c001");
        await (parsed?.Port).Should().BeEqualTo(443);
    }

    [Test]
    public async Task BothMimHopsAndTheTransportSurviveTheRoundTrip()
    {
        var original = Profile(
            it => it with
            {
                AetherProtocol = AetherProtocol.Mim.Type,
                AetherTransport = AetherTransport.Http2.Type,
                AetherFragment = true,
                AetherWiwOuter = "162.159.192.1:443",
                AetherWiwInner = "[2606:4700:d0::a29f:c001]:443",
            },
            it => it.Remarks = "Mim");

        var parsed = Parse(Link(original));

        await (Extra(parsed)?.AetherProtocol).Should().BeEqualTo("mim");
        await (Extra(parsed)?.AetherTransport).Should().BeEqualTo("h2");
        await (Extra(parsed)?.AetherFragment).Should().BeEqualTo(true);
        await (Extra(parsed)?.AetherWiwOuter).Should().BeEqualTo("162.159.192.1:443");
        await (Extra(parsed)?.AetherWiwInner).Should().BeEqualTo("[2606:4700:d0::a29f:c001]:443");
        await (parsed?.Address).Should().BeEqualTo(string.Empty);
        await (parsed?.Port).Should().BeEqualTo(0);
    }

    [Test]
    public async Task TheHopsOnlyRideAlongWithATwoHopProtocol()
    {
        var uri = Link(Profile(it => it with
        {
            AetherWiwOuter = "162.159.192.1:2408",
            AetherWiwInner = "188.114.96.1:894",
        }));

        await uri.Contains("outer=").Should().BeFalse();
        await uri.Contains("inner=").Should().BeFalse();
    }

    [Test]
    public async Task TheEndpointNeverRidesAlongWithGool()
    {
        var uri = Link(Profile(
            it => it with { AetherProtocol = AetherProtocol.Gool.Type },
            it =>
            {
                it.Address = "162.159.198.1";
                it.Port = 443;
            }));

        await uri.StartsWith("aether://?", StringComparison.Ordinal).Should().BeTrue();
        await (Parse("aether://162.159.198.1:443?protocol=gool#X")?.Address).Should().BeEqualTo(string.Empty);
    }

    [Test]
    public async Task TheTransportOnlyRidesAlongOverMasque()
    {
        var uri = Link(Profile(it => it with
        {
            AetherProtocol = AetherProtocol.WireGuard.Type,
            AetherTransport = AetherTransport.Http2.Type,
            AetherFragment = true,
        }));

        await uri.Contains("transport=").Should().BeFalse();
        await uri.Contains("fragment=").Should().BeFalse();
        await (Extra(Parse(uri))?.AetherProtocol).Should().BeEqualTo("wg");

        await Link(Profile(it => it with { AetherProtocol = AetherProtocol.Gool.Type, AetherTransport = AetherTransport.Http2.Type }))
            .Contains("transport=").Should().BeFalse();
        await Link(Profile(it => it with { AetherProtocol = AetherProtocol.Mim.Type, AetherTransport = AetherTransport.Http2.Type }))
            .Contains("transport=h2").Should().BeTrue();
    }

    [Test]
    public async Task ALinkWithoutSettingsFallsBackToTheDefaults()
    {
        var parsed = Parse("aether://#Shared");

        await (parsed?.Remarks).Should().BeEqualTo("Shared");
        await (Extra(parsed)?.AetherProtocol).Should().BeEqualTo("wg");
        await (Extra(parsed)?.AetherTransport).Should().BeEqualTo("h3");
        await (Extra(parsed)?.AetherScanMode).Should().BeEqualTo("balanced");
        await (Extra(parsed)?.AetherObfuscation).Should().BeEqualTo("auto");
        await (Extra(parsed)?.AetherIpVersion).Should().BeEqualTo("v4");
        await (Extra(parsed)?.AetherFragment).Should().BeEqualTo(false);
    }

    [Test]
    public async Task ANamelessLinkStillGetsALabel()
    {
        await (Parse("aether://?protocol=wg")?.Remarks).Should().BeEqualTo("Aether");
    }

    [Test]
    public async Task AnUnknownSettingFallsBackInsteadOfFailing()
    {
        var parsed = Extra(Parse("aether://?protocol=quantum&scan=instant&ip=v9#X"));

        await (parsed?.AetherProtocol).Should().BeEqualTo("wg");
        await (parsed?.AetherScanMode).Should().BeEqualTo("balanced");
        await (parsed?.AetherIpVersion).Should().BeEqualTo("v4");
    }

    [Test]
    public async Task AnEndpointTheCoreCannotUseIsDroppedFromALink()
    {
        var hostname = Parse("aether://engage.cloudflareclient.com:2408?protocol=wg#X");
        await (hostname?.Address).Should().BeEqualTo(string.Empty);
        await (hostname?.Port).Should().BeEqualTo(0);

        var portless = Parse("aether://162.159.198.1?protocol=masque#X");
        await (portless?.Address).Should().BeEqualTo(string.Empty);
        await (portless?.Port).Should().BeEqualTo(0);
    }

    [Test]
    public async Task GoolHopsFromALinkAreCheckedAndNormalized()
    {
        var parsed = Extra(Parse(
            "aether://?protocol=gool&outer=162.159.192.1%3A2408&inner=%5B2606%3A4700%3A0%3A0%3A0%3A0%3A0%3A1%5D%3A894#X"));
        await (parsed?.AetherWiwOuter).Should().BeEqualTo("162.159.192.1:2408");
        await (parsed?.AetherWiwInner).Should().BeEqualTo("[2606:4700::1]:894");

        var malformed = Extra(Parse("aether://?protocol=gool&outer=162.159.192.1&inner=example.com%3A894#X"));
        await (malformed?.AetherWiwOuter).Should().BeNull();
        await (malformed?.AetherWiwInner).Should().BeNull();

        var shared = Extra(Parse("aether://?protocol=gool&outer=162.159.192.1%3A2408&inner=162.159.192.1%3A894#X"));
        await (shared?.AetherWiwOuter).Should().BeEqualTo("162.159.192.1:2408");
        await (shared?.AetherWiwInner).Should().BeNull();
    }

    [Test]
    public async Task WireGuardOverMasqueGoesThroughALinkWithItsGatewayItsWireGuardEndpointAndItsMasqueSettings()
    {
        var config = Profile(it => it with
        {
            AetherProtocol = AetherProtocol.WgOverMasque.Type,
            AetherTransport = AetherTransport.Http2.Type,
            AetherFragment = true,
            AetherFragmentSize = "8-16",
            AetherEch = true,
            AetherFingerprint = "firefox",
            AetherWiwOuter = "162.159.192.1:443",
            // The same address on both hops is no problem for the core here.
            AetherWiwInner = "162.159.192.1:2408",
        });
        await Normalize(config).Should().BeNull();
        await config.GetProtocolExtra().AetherWiwOuter.Should().BeEqualTo("162.159.192.1:443");
        await config.GetProtocolExtra().AetherWiwInner.Should().BeEqualTo("162.159.192.1:2408");

        var text = Link(config);
        await text.Contains("protocol=wg-over-masque").Should().BeTrue();
        var parsed = Parse(text);
        await (Extra(parsed)?.AetherProtocol).Should().BeEqualTo(AetherProtocol.WgOverMasque.Type);
        await (Extra(parsed)?.AetherWiwOuter).Should().BeEqualTo("162.159.192.1:443");
        await (Extra(parsed)?.AetherWiwInner).Should().BeEqualTo("162.159.192.1:2408");
        await (Extra(parsed)?.AetherTransport).Should().BeEqualTo(AetherTransport.Http2.Type);
        await (Extra(parsed)?.AetherFragment).Should().BeEqualTo(true);
        await (Extra(parsed)?.AetherFragmentSize).Should().BeEqualTo("8-16");
        await (Extra(parsed)?.AetherEch).Should().BeEqualTo(true);
        await (Extra(parsed)?.AetherFingerprint).Should().BeEqualTo("firefox");
        await Link(parsed!).Should().BeEqualTo(text);
    }

    [Test]
    public async Task AnEmptyEndpointMeansScanning()
    {
        var config = Profile(
            it => it with { AetherWiwOuter = "162.159.192.1:2408" },
            it =>
            {
                it.Address = "  ";
                it.Port = 443;
            });

        await Normalize(config).Should().BeNull();
        await config.Address.Should().BeEqualTo(string.Empty);
        await config.Port.Should().BeEqualTo(0);
        await config.GetProtocolExtra().AetherWiwOuter.Should().BeNull();
    }

    [Test]
    public async Task APinnedEndpointIsNormalizedBeforeItIsSaved()
    {
        // PattNG's port " 0443 " is text; PattN's is a number, which the editor reads.
        var config = Profile(item: it =>
        {
            it.Address = " [2606:4700:0:0:0:0:0:1] ";
            it.Port = 443;
        });

        await Normalize(config).Should().BeNull();
        await config.Address.Should().BeEqualTo("2606:4700::1");
        await config.Port.Should().BeEqualTo(443);
    }

    [Test]
    public async Task APinnedEndpointTheCoreCannotUseIsRejected()
    {
        await Normalize(Profile(item: it =>
            {
                it.Address = "engage.cloudflareclient.com";
                it.Port = 2408;
            }))
            .Should().BeEqualTo(AetherProblem.InvalidPeer);
        // PattNG's empty port: 0 here.
        await Normalize(Profile(item: it =>
            {
                it.Address = "162.159.198.1";
                it.Port = 0;
            }))
            .Should().BeEqualTo(AetherProblem.InvalidPeer);
        await Normalize(Profile(item: it =>
            {
                it.Address = "162.159.198.1";
                it.Port = 70000;
            }))
            .Should().BeEqualTo(AetherProblem.InvalidPeer);
    }

    [Test]
    public async Task GoolHopsAreNormalizedAndTheEndpointIsCleared()
    {
        var config = Profile(
            it => it with
            {
                AetherProtocol = AetherProtocol.Gool.Type,
                AetherWiwOuter = " 162.159.192.1:2408 ",
                AetherWiwInner = "",
            },
            it =>
            {
                it.Address = "162.159.198.1";
                it.Port = 443;
            });

        await Normalize(config).Should().BeNull();
        await config.GetProtocolExtra().AetherWiwOuter.Should().BeEqualTo("162.159.192.1:2408");
        await config.GetProtocolExtra().AetherWiwInner.Should().BeNull();
        await config.Address.Should().BeEqualTo(string.Empty);
        await config.Port.Should().BeEqualTo(0);
    }

    [Test]
    public async Task AGoolHopTheCoreCannotUseIsRejected()
    {
        await Normalize(Profile(it => it with { AetherProtocol = AetherProtocol.Gool.Type, AetherWiwOuter = "162.159.192.1" }))
            .Should().BeEqualTo(AetherProblem.InvalidHop);
        await Normalize(Profile(it => it with { AetherProtocol = AetherProtocol.Gool.Type, AetherWiwInner = "2606:4700::1:894" }))
            .Should().BeEqualTo(AetherProblem.InvalidHop);
    }

    [Test]
    public async Task MimHopsAreCheckedLikeGoolHops()
    {
        var config = Profile(
            it => it with
            {
                AetherProtocol = AetherProtocol.Mim.Type,
                AetherWiwOuter = " 162.159.192.1:443 ",
                AetherWiwInner = "",
            },
            it =>
            {
                it.Address = "162.159.198.1";
                it.Port = 443;
            });

        await Normalize(config).Should().BeNull();
        await config.GetProtocolExtra().AetherWiwOuter.Should().BeEqualTo("162.159.192.1:443");
        await config.GetProtocolExtra().AetherWiwInner.Should().BeNull();
        await config.Address.Should().BeEqualTo(string.Empty);
        await config.Port.Should().BeEqualTo(0);

        await Normalize(Profile(it => it with { AetherProtocol = AetherProtocol.Mim.Type, AetherWiwOuter = "162.159.192.1" }))
            .Should().BeEqualTo(AetherProblem.InvalidHop);
        await Normalize(Profile(it => it with
            {
                AetherProtocol = AetherProtocol.Mim.Type,
                AetherWiwOuter = "162.159.192.1:443",
                AetherWiwInner = "162.159.192.1:2408",
            }))
            .Should().BeEqualTo(AetherProblem.SharedHop);
    }

    [Test]
    public async Task GoolHopsMustLeaveThroughDifferentAddresses()
    {
        var config = Profile(it => it with
        {
            AetherProtocol = AetherProtocol.Gool.Type,
            AetherWiwOuter = "162.159.192.1:2408",
            AetherWiwInner = "162.159.192.1:894",
        });

        await Normalize(config).Should().BeEqualTo(AetherProblem.SharedHop);
        await config.GetProtocolExtra().AetherWiwOuter.Should().BeEqualTo("162.159.192.1:2408");
        await config.GetProtocolExtra().AetherWiwInner.Should().BeEqualTo("162.159.192.1:894");
    }

    [Test]
    public async Task ALinkCarriesNoExitNodeProfile()
    {
        // The name is that of a profile of this device; a link elsewhere would name another there, or none.
        var noded = Profile(it => it with { AetherExitNode = "germany" });
        var text = Link(noded);
        await text.Contains("germany").Should().BeFalse();
        await text.Should().BeEqualTo(Link(Profile()));
        await (Extra(Parse(text))?.AetherExitNode).Should().BeNull();
    }

    [Test]
    public async Task AListenPortALinkStillNamesCountsNoMore()
    {
        // Links from before every core listened on the Aether listen port of the settings may name a port of their own.
        // PattN has no field for it (PattNG keeps aetherListenPort for profiles stored before then).
        var plain = Link(Profile());
        var old = plain.Replace("?", "?listen=20808&");
        var parsed = Parse(old);
        await parsed.Should().NotBeNull();
        await AetherCore.Of(parsed!, AetherPorts.DefaultListenPort).Port.Should().BeEqualTo(AetherPorts.DefaultListenPort);
        // Otherwise it reads as it did.
        await Link(parsed!).Should().BeEqualTo(plain);
    }

    [Test]
    public async Task TheAetherListenPortCannotBeAPortTheLocalProxyListensOn()
    {
        // PattNG sets the listen port of the settings; here it is passed, as a screen passes the one it holds.
        var localProxy = Ports(10808, 10809);
        await Normalize(Profile(), localProxy).Should().BeNull();
        await Normalize(Profile(), localProxy, listenPort: 10808).Should().BeEqualTo(AetherProblem.ListenPortTaken);
        await Normalize(Profile(), localProxy, listenPort: 10807).Should().BeNull();
        // Tor inside the tunnel takes the port after it.
        await Normalize(Profile(it => it with { AetherTor = "chain" }), localProxy, listenPort: 10807).Should().BeEqualTo(AetherProblem.NextPortTaken);
    }

    [Test]
    public async Task TheAetherListenPortIsTakenOnceTheLocalProxyWasMovedOntoIt()
    {
        var movedOntoIt = Ports(10819);
        await Normalize(Profile(), movedOntoIt).Should().BeEqualTo(AetherProblem.ListenPortTaken);
        // A command written by hand listens where it says.
        await Normalize(Profile(it => it with { AetherCommand = "aether --wg --bind 127.0.0.1:20808" }), movedOntoIt).Should().BeNull();
        await Normalize(Profile(it => it with { AetherCommand = "aether --wg --bind 127.0.0.1:10819" }), movedOntoIt)
            .Should().BeEqualTo(AetherProblem.ListenPortTaken);
        // So does an HTTP proxy it opens.
        await Normalize(Profile(it => it with { AetherCommand = "aether --wg --bind 127.0.0.1:20808 --http-proxy 127.0.0.1:10819" }), movedOntoIt)
            .Should().BeEqualTo(AetherProblem.ListenPortTaken);
    }

    [Test]
    public async Task ACommandWhoseListenersShareAPortIsRefused()
    {
        await Normalize(Profile(it => it with { AetherCommand = "aether --wg --bind 127.0.0.1:20808 --http-proxy 127.0.0.1:20808" }))
            .Should().BeEqualTo(AetherProblem.ListenPortShared);
        // A command naming no listener gets one on the Aether listen port, which its HTTP proxy then shares.
        await Normalize(Profile(it => it with { AetherCommand = "aether --wg --http-proxy 127.0.0.1:20808" }), listenPort: 20808)
            .Should().BeEqualTo(AetherProblem.ListenPortShared);
        await Normalize(Profile(it => it with { AetherCommand = "aether --wg --bind 127.0.0.1:20808 --http-proxy 127.0.0.1:20809" })).Should().BeNull();
    }

    [Test]
    public async Task TheMessageOfASharedPortNamesIt()
    {
        var shared = Profile(it => it with { AetherCommand = "aether --wg --bind 127.0.0.1:20808 --http-proxy 127.0.0.1:20808" });
        await Normalize(shared).Should().BeEqualTo(AetherProblem.ListenPortShared);
        await AetherFmt.SharedListenPort(shared, AetherPorts.DefaultListenPort).Should().BeEqualTo(20808);
        await ServiceLib.ViewModels.AddServerAetherViewModel.MessageOf(AetherProblem.ListenPortShared, shared, null, AetherPorts.DefaultListenPort)
            .Should().BeEqualTo(string.Format(ResUI.MsgAetherListenPortShared, 20808));
        // The core's own listener, left out, listens on its default, which the command writes only for Tor's HTTP proxy.
        var onDefault = Profile(it => it with { AetherCommand = "aether --wg --tor --tor-bind 127.0.0.1:10819 --tor-http 127.0.0.1:1819" });
        await Normalize(onDefault).Should().BeEqualTo(AetherProblem.ListenPortShared);
        await AetherFmt.SharedListenPort(onDefault, AetherPorts.DefaultListenPort).Should().BeEqualTo(1819);
        await AetherFmt.SharedListenPort(Profile(it => it with { AetherCommand = "aether --wg --bind 127.0.0.1:20808 --http-proxy 127.0.0.1:20809" }),
            AetherPorts.DefaultListenPort).Should().BeNull();
        await AetherFmt.SharedListenPort(Profile(), AetherPorts.DefaultListenPort).Should().BeNull();
    }

    [Test]
    public async Task ACommandListeningWhereTheCoreCannotIsRefusedAsAStartRefusesIt()
    {
        // A number that is no port comes first, then an address the core does not take, then ports two listeners share.
        var noPort = Profile(it => it with { AetherCommand = "aether --wg --bind localhost:70000 --http-proxy 127.0.0.1:70000" });
        await Normalize(noPort).Should().BeEqualTo(AetherProblem.ListenPortInvalid);
        await AetherFmt.InvalidListenPort(noPort, AetherPorts.DefaultListenPort).Should().BeEqualTo(70000);
        await ServiceLib.ViewModels.AddServerAetherViewModel.MessageOf(AetherProblem.ListenPortInvalid, noPort, null, AetherPorts.DefaultListenPort)
            .Should().BeEqualTo(string.Format(ResUI.MsgAetherListenPortInvalid, 70000));
        var noAddress = Profile(it => it with { AetherCommand = "aether --wg --bind localhost:20808 --http-proxy 127.0.0.1:20808" });
        await Normalize(noAddress, takenPorts: Ports(20808)).Should().BeEqualTo(AetherProblem.ListenAddressInvalid);
        await AetherFmt.InvalidListener(noAddress, AetherPorts.DefaultListenPort).Should().BeEqualTo("--bind");
        await AetherFmt.InvalidListenPort(noAddress, AetherPorts.DefaultListenPort).Should().BeNull();
        await ServiceLib.ViewModels.AddServerAetherViewModel.MessageOf(AetherProblem.ListenAddressInvalid, noAddress, null, AetherPorts.DefaultListenPort)
            .Should().BeEqualTo(string.Format(ResUI.MsgAetherListenAddressInvalid, "--bind"));
        // A listener the app does not dial, on port 0, takes any port, which the core can bind.
        var anyPort = Profile(it => it with { AetherCommand = "aether --wg --bind 127.0.0.1:20808 --http-proxy 127.0.0.1:0" });
        await Normalize(anyPort).Should().BeNull();
        await AetherFmt.InvalidListenPort(anyPort, AetherPorts.DefaultListenPort).Should().BeNull();
        await AetherFmt.InvalidListener(anyPort, AetherPorts.DefaultListenPort).Should().BeNull();
        // An address the core takes, where the app does not reach the listener it dials, comes before a shared port, and
        // before a taken one.
        var unreachable = Profile(it => it with { AetherCommand = "aether --wg --bind 192.168.1.5:20808 --http-proxy 127.0.0.1:20808" });
        await Normalize(unreachable, takenPorts: Ports(20808)).Should().BeEqualTo(AetherProblem.ListenAddressUnreachable);
        await AetherFmt.UnreachableListener(unreachable, AetherPorts.DefaultListenPort).Should().BeEqualTo("--bind");
        await ServiceLib.ViewModels.AddServerAetherViewModel.MessageOf(AetherProblem.ListenAddressUnreachable, unreachable, null, AetherPorts.DefaultListenPort)
            .Should().BeEqualTo(string.Format(ResUI.MsgAetherListenAddressUnreachable, "--bind"));
        await AetherFmt.UnreachableListener(anyPort, AetherPorts.DefaultListenPort).Should().BeNull();
        await Normalize(Profile(it => it with { AetherCommand = "aether --wg --bind 0.0.0.0:20808" })).Should().BeNull();
        // The app's own listener on port 0 is no port it can dial.
        await Normalize(Profile(it => it with { AetherCommand = "aether --wg --bind 127.0.0.1:0" })).Should().BeEqualTo(AetherProblem.ListenPortInvalid);
        // Nothing to name for a profile built from its settings, nor for a blank command.
        await AetherFmt.InvalidListenPort(Profile(), AetherPorts.DefaultListenPort).Should().BeNull();
        await AetherFmt.InvalidListener(Profile(it => it with { AetherCommand = "   " }), AetherPorts.DefaultListenPort).Should().BeNull();
    }

    [Test]
    public async Task WithNothingElseToNameAMessageNamesTheListenPort()
    {
        // A profile built from its settings listens on the listen port alone, and has no command to name another.
        var settings = Profile();
        await ServiceLib.ViewModels.AddServerAetherViewModel.MessageOf(AetherProblem.ListenPortTaken, settings, null, 20808)
            .Should().BeEqualTo(string.Format(ResUI.MsgAetherListenPortTaken, 20808));
        await ServiceLib.ViewModels.AddServerAetherViewModel.MessageOf(AetherProblem.ListenPortShared, settings, null, 20808)
            .Should().BeEqualTo(string.Format(ResUI.MsgAetherListenPortShared, 20808));
        await ServiceLib.ViewModels.AddServerAetherViewModel.MessageOf(AetherProblem.ListenPortInvalid, settings, null, 20808)
            .Should().BeEqualTo(string.Format(ResUI.MsgAetherListenPortInvalid, 20808));
        await ServiceLib.ViewModels.AddServerAetherViewModel.MessageOf(AetherProblem.ListenAddressInvalid, settings, null, 20808)
            .Should().BeEqualTo(string.Format(ResUI.MsgAetherListenAddressInvalid, string.Empty));
        await ServiceLib.ViewModels.AddServerAetherViewModel.MessageOf(AetherProblem.ListenAddressUnreachable, settings, null, 20808)
            .Should().BeEqualTo(string.Format(ResUI.MsgAetherListenAddressUnreachable, string.Empty));
    }

    [Test]
    public async Task TheMessageOfATakenPortNamesIt()
    {
        // A profile built from its settings: its listen port.
        await AetherFmt.TakenListenPort(Profile(), Ports(10808, 10819), AetherPorts.DefaultListenPort).Should().BeEqualTo(10819);
        await AetherFmt.TakenListenPort(Profile(), Ports(20808), 20808).Should().BeEqualTo(20808);
        // A command written by hand: the first of its ports that is taken, its HTTP proxy's too; a blank one is none.
        var command = Profile(it => it with { AetherCommand = "aether --wg --bind 127.0.0.1:20808 --http-proxy 127.0.0.1:10808" });
        await Normalize(command, Ports(10808)).Should().BeEqualTo(AetherProblem.ListenPortTaken);
        await AetherFmt.TakenListenPort(command, Ports(10808), AetherPorts.DefaultListenPort).Should().BeEqualTo(10808);
        await ServiceLib.ViewModels.AddServerAetherViewModel.MessageOf(AetherProblem.ListenPortTaken, command, Ports(10808), AetherPorts.DefaultListenPort)
            .Should().BeEqualTo(string.Format(ResUI.MsgAetherListenPortTaken, 10808));
        await AetherFmt.TakenListenPort(Profile(it => it with { AetherCommand = "   " }), Ports(10819), AetherPorts.DefaultListenPort)
            .Should().BeEqualTo(10819);
        // The core's own listener, left out, listens on its default, 1819, which the command never writes.
        var torInside = Profile(it => it with { AetherCommand = "aether --wg --tor --tor-bind 127.0.0.1:10819" });
        await Normalize(torInside, Ports(1819)).Should().BeEqualTo(AetherProblem.ListenPortTaken);
        await AetherFmt.TakenListenPort(torInside, Ports(1819), AetherPorts.DefaultListenPort).Should().BeEqualTo(1819);
        // Nothing taken: no port to name.
        await AetherFmt.TakenListenPort(Profile(), Ports(10808), AetherPorts.DefaultListenPort).Should().BeNull();
        await AetherFmt.TakenListenPort(command, Ports(10809), AetherPorts.DefaultListenPort).Should().BeNull();
        await AetherFmt.TakenListenPort(command, null, AetherPorts.DefaultListenPort).Should().BeNull();
    }

    [Test]
    public async Task WithoutKnownLocalPortsNoPortIsTaken()
    {
        // The local proxy port is picked at random on every start, or the caller has none to name.
        await Normalize(Profile(it => it with { AetherPsiphon = "chain", AetherTor = "reverse" })).Should().BeNull();
        await Normalize(Profile(it => it with { AetherPsiphon = "chain", AetherTor = "reverse" }), Ports()).Should().BeNull();
    }

    [Test]
    public async Task AScreenChecksAProfileOnTheListenPortItHolds()
    {
        // The setting says 10819; the screen holds 20808, which is what the checks go by.
        await Normalize(Profile(), takenPorts: Ports(20808), listenPort: 20808).Should().BeEqualTo(AetherProblem.ListenPortTaken);
        await Normalize(Profile(), takenPorts: Ports(10819), listenPort: 20808).Should().BeNull();
        await Normalize(Profile(it => it with { AetherPsiphon = "chain" }), takenPorts: Ports(20809), listenPort: 20808)
            .Should().BeEqualTo(AetherProblem.NextPortTaken);
        // A command naming no listener gets one on that port as well.
        await Normalize(Profile(it => it with { AetherCommand = "aether --wg" }), takenPorts: Ports(20808), listenPort: 20808)
            .Should().BeEqualTo(AetherProblem.ListenPortTaken);
        await Normalize(Profile(it => it with { AetherCommand = "aether --wg" }), takenPorts: Ports(10819), listenPort: 20808).Should().BeNull();
    }

    // PattN: links as PattNG writes them (Java's URLEncoder, a space as %20, '*' kept and '~' escaped) read field for
    // field, so that links stay interchangeable between the two apps.

    [Test]
    public async Task APattNGLinkWithEverySettingIsReadFieldForField()
    {
        const string link = "aether://162.159.198.1:443?protocol=masque&scan=verified&ip=both"
                            + "&dns=1.1.1.1%2C10.0.0.1%3A5353&exit_loc=%21IR%2CRU&transport=h2&sni=consumer-masque.cloudflareclient.com"
                            + "&fragment=1&fragment_size=16-32&fragment_delay=5&ech=1"
                            + "&ech_dns=https%3A%2F%2F1.1.1.1%2Fdns-query%40sni%3Dwww.microsoft.com&ech_domain=ip.gs&fingerprint=firefox"
                            + "&psiphon=chain&psiphon_mode=cdn&cdn_ips=1.1.1.1%2C1.0.0.1&cdn_sni=a.example%2Cb.example"
                            + "&cdn_sets=cloudflare%2Cfastly&region=DE&psiphon_bundled=0&tor=reverse&tor_bridges=own&tor_relays=only"
                            + "&bridges=obfs4%20192.0.2.55%3A38114%20316E64%20cert%3Dabc%20iat-mode%3D0%3Bwebtunnel%20198.51.100.25%3A443%207DD627%20url%3Dhttps%3A%2F%2Fexample.com%2Fx"
                            + "&fm=%7B%22tcp%22%3A%20%5B%7B%22type%22%3A%20%22fragment%22%2C%20%22settings%22%3A%20%7B%22packets%22%3A%20%22tlshello%22%2C%20%22length%22%3A%20%2250-100%22%7D%7D%5D%7D"
                            + "&dialMode=code-1#WARP%20%E2%9C%93%20%7Ehome*";

        var parsed = Parse(link);

        await parsed.Should().NotBeNull();
        await AssertEverySetting(parsed!);
        // And so does the link PattN writes of it.
        await AssertEverySetting(Parse(Link(parsed!))!);
    }

    private static async Task AssertEverySetting(ProfileItem parsed)
    {
        var extra = parsed.GetProtocolExtra();
        await parsed.ConfigType.Should().BeEqualTo(EConfigType.Aether);
        await parsed.CoreType.Should().BeEqualTo(ECoreType.Xray);
        await parsed.Remarks.Should().BeEqualTo("WARP ✓ ~home*");
        await parsed.Address.Should().BeEqualTo("162.159.198.1");
        await parsed.Port.Should().BeEqualTo(443);
        await extra.AetherProtocol.Should().BeEqualTo("masque");
        await extra.AetherScanMode.Should().BeEqualTo("verified");
        // PattNG leaves the obfuscation out over HTTP/2, where it does nothing.
        await extra.AetherObfuscation.Should().BeEqualTo("auto");
        await extra.AetherIpVersion.Should().BeEqualTo("both");
        await extra.AetherDns.Should().BeEqualTo("1.1.1.1,10.0.0.1:5353");
        await extra.AetherExitLoc.Should().BeEqualTo("!IR,RU");
        await extra.AetherTransport.Should().BeEqualTo("h2");
        await extra.AetherMasqueSni.Should().BeEqualTo("consumer-masque.cloudflareclient.com");
        await extra.AetherFragment.Should().BeEqualTo(true);
        await extra.AetherFragmentSize.Should().BeEqualTo("16-32");
        await extra.AetherFragmentDelay.Should().BeEqualTo("5");
        await extra.AetherEch.Should().BeEqualTo(true);
        await extra.AetherEchDns.Should().BeEqualTo("https://1.1.1.1/dns-query@sni=www.microsoft.com");
        await extra.AetherEchDomain.Should().BeEqualTo("ip.gs");
        await extra.AetherFingerprint.Should().BeEqualTo("firefox");
        await extra.AetherWiwOuter.Should().BeNull();
        await extra.AetherWiwInner.Should().BeNull();
        await extra.AetherPsiphon.Should().BeEqualTo("chain");
        await extra.AetherPsiphonMode.Should().BeEqualTo("cdn");
        await extra.AetherPsiphonCdnIps.Should().BeEqualTo("1.1.1.1,1.0.0.1");
        await extra.AetherPsiphonCdnSni.Should().BeEqualTo("a.example,b.example");
        await extra.AetherPsiphonCdnSets.Should().BeEqualTo("cloudflare,fastly");
        await extra.AetherPsiphonRegion.Should().BeEqualTo("DE");
        await extra.AetherPsiphonBundledList.Should().BeEqualTo(false);
        await extra.AetherTor.Should().BeEqualTo("reverse");
        await extra.AetherTorBridges.Should().BeEqualTo("own");
        await extra.AetherTorBridgeLines.Should().BeEqualTo(
            "obfs4 192.0.2.55:38114 316E64 cert=abc iat-mode=0\nwebtunnel 198.51.100.25:443 7DD627 url=https://example.com/x");
        await extra.AetherTorRelays.Should().BeEqualTo("only");
        await JsonNode.DeepEquals(
                JsonUtils.ParseJson(parsed.Finalmask),
                JsonUtils.ParseJson("""{"tcp": [{"type": "fragment", "settings": {"packets": "tlshello", "length": "50-100"}}]}"""))
            .Should().BeTrue();
        await parsed.DialMode.Should().BeEqualTo("code-1");
        // Never in a link.
        await extra.AetherExitNode.Should().BeNull();
        await extra.AetherCommand.Should().BeNull();
    }

    [Test]
    public async Task APattNGLinkOfANodeLeftToTheScannerIsReadFieldForField()
    {
        var parsed = Parse("aether://?protocol=masque&scan=balanced&noize=balanced&ip=v4&transport=h3#My%20Node");

        await (parsed?.Remarks).Should().BeEqualTo("My Node");
        await (parsed?.Address).Should().BeEqualTo(string.Empty);
        await (parsed?.Port).Should().BeEqualTo(0);
        // Everything a link does not name is left to the defaults, as PattNG's parse leaves it.
        await Extra(parsed).Should().BeEqualTo(new ProtocolExtraItem
        {
            AetherProtocol = "masque",
            AetherTransport = "h3",
            AetherScanMode = "balanced",
            AetherObfuscation = "balanced",
            AetherIpVersion = "v4",
            AetherFragment = false,
            AetherEch = false,
        });
        await (parsed?.Finalmask).Should().BeEqualTo(string.Empty);
        await (parsed?.DialMode).Should().BeEqualTo(string.Empty);
    }

    [Test]
    public async Task PattNGLinksWhoseEncodingPattNSharesComeBackAsPattNGWroteThem()
    {
        var links = new[]
        {
            "aether://[2606:4700:d0::a29f:c001]:443?protocol=masque&scan=balanced&noize=balanced&ip=v4&transport=h3#My%20Node",
            "aether://?protocol=gool&scan=balanced&noize=balanced&ip=v4&outer=162.159.192.1%3A2408&inner=%5B2606%3A4700%3Ad0%3A%3Aa29f%3Ac001%5D%3A894#Gool",
            "aether://?protocol=wg-over-masque&scan=balanced&ip=v4&transport=h2&fragment=1&fragment_size=8-16&ech=1&fingerprint=firefox"
            + "&outer=162.159.192.1%3A443&inner=162.159.192.1%3A2408#My%20Node",
        };
        foreach (var link in links)
        {
            await Link(Parse(link)!).Should().BeEqualTo(link);
        }

        var pinned = Parse(links[0]);
        await (pinned?.Address).Should().BeEqualTo("2606:4700:d0::a29f:c001");
        await (pinned?.Port).Should().BeEqualTo(443);
        var gool = Parse(links[1]);
        await (gool?.Remarks).Should().BeEqualTo("Gool");
        await (Extra(gool)?.AetherWiwOuter).Should().BeEqualTo("162.159.192.1:2408");
        await (Extra(gool)?.AetherWiwInner).Should().BeEqualTo("[2606:4700:d0::a29f:c001]:894");
        await (gool?.Address).Should().BeEqualTo(string.Empty);
    }

    [Test]
    public async Task AQueryElementWithoutAValueIsSkipped()
    {
        // PattN reads the query as its other formats do: an element without '=' is left out, where PattNG drops the link.
        var parsed = Extra(Parse("aether://?protocol=masque&fragment&scan=verified#X"));

        await (parsed?.AetherProtocol).Should().BeEqualTo("masque");
        await (parsed?.AetherScanMode).Should().BeEqualTo("verified");
        await (parsed?.AetherFragment).Should().BeEqualTo(false);
    }

    // PattN: java.net.URI, which PattNG reads a link with, takes an authority it cannot read as a host and a port for a
    // registry name: the link is kept without an endpoint, and its profile scans. .NET's Uri refuses such a link whole.
    [Test]
    public async Task ALinkWhoseAuthorityIsNoHostAndPortIsKeptWithoutAnEndpoint()
    {
        var links = new[]
        {
            // IPv6 without brackets, a port past 65535, no host, a port that is no number, a space in the host.
            "aether://2606:4700:d0::a29f:c001:443?protocol=masque&scan=verified#WARP",
            "aether://162.159.198.1:70000?protocol=masque&scan=verified#WARP",
            "aether://:2408?protocol=masque&scan=verified#WARP",
            "aether://162.159.198.1:abc?protocol=masque&scan=verified#WARP",
            "aether://a b:443?protocol=masque&scan=verified#WARP",
        };
        foreach (var link in links)
        {
            var parsed = Parse(link);
            var read = parsed == null ? null : $"[{parsed.Address}]:{parsed.Port} {parsed.Remarks} {Extra(parsed)?.AetherProtocol} {Extra(parsed)?.AetherScanMode}";
            await Labeled(link, read).Should().BeEqualTo(Labeled(link, "[]:0 WARP masque verified"));
        }
        // An authority java.net.URI cannot read as either is no link.
        await Parse("aether://1.2.3.4^:443?protocol=masque#X").Should().BeNull();
        await Parse("aether://[::1:443?protocol=masque#X").Should().BeNull();
    }

    // PattN: the endpoint is read from the link as it is written. .NET's Uri rewrites an IPv4 host with leading zeros
    // (010 read as octal 8, 08 as 255.255.255.255); java.net.URI keeps it as written, and an octet with a leading zero is
    // no address the core takes, so the profile scans.
    [Test]
    public async Task TheEndpointOfALinkIsReadAsWritten()
    {
        foreach (var host in new[] { "162.159.192.010", "162.159.192.08", "162.159.192.09", "1.1.1.010", "[::ffff:162.159.192.010]", "۱۶۲.۱۵۹.۱۹۲.۱" })
        {
            var parsed = Parse($"aether://{host}:2408?protocol=wg#X");
            await Labeled(host, parsed == null ? null : $"[{parsed.Address}]:{parsed.Port}").Should().BeEqualTo(Labeled(host, "[]:0"));
        }
        var plain = Parse("aether://162.159.192.1:2408?protocol=wg#X");
        await (plain?.Address).Should().BeEqualTo("162.159.192.1");
        await (plain?.Port).Should().BeEqualTo(2408);
        // A user name before the host is no part of it, and a port is the number it writes.
        var named = Parse("aether://name@162.159.192.1:000443?protocol=wg#X");
        await (named?.Address).Should().BeEqualTo("162.159.192.1");
        await (named?.Port).Should().BeEqualTo(443);
    }

    // PattN: capitals as Java's uppercase(Locale.ROOT) writes them, which maps the dotless i to I and ß to SS, where
    // .NET's ToUpperInvariant leaves both as they are.
    [Test]
    public async Task TheExitRuleAndTheRegionAreCapitalizedAsPattNGCapitalizesThem()
    {
        var refused = Profile(it => it with { AetherExitLoc = "!ır" });
        await Normalize(refused).Should().BeNull();
        await refused.GetProtocolExtra().AetherExitLoc.Should().BeEqualTo("!IR");
        var allowed = Profile(it => it with { AetherExitLoc = "de,ß" });
        await Normalize(allowed).Should().BeNull();
        await allowed.GetProtocolExtra().AetherExitLoc.Should().BeEqualTo("DE,SS");

        var region = Profile(it => it with { AetherPsiphon = "chain", AetherPsiphonRegion = " ır " });
        await Normalize(region).Should().BeNull();
        await region.GetProtocolExtra().AetherPsiphonRegion.Should().BeEqualTo("IR");
    }

    // PattN: Normalize checks in PattNG's order (design stage 1), and the steps before a problem have rewritten their
    // settings by then; the ones after it have not.
    [Test]
    public async Task TheProblemsAreCheckedInOrderAndEarlierStepsHaveRewrittenTheirSettings()
    {
        var config = Profile(
            it => it with
            {
                AetherMasqueSni = "1.1.1.1",
                AetherTransport = AetherTransport.Http2.Type,
                AetherFragment = true,
                AetherFragmentSize = "x",
                AetherDns = "x",
                AetherExitLoc = "Germany",
                AetherEch = true,
                AetherEchDns = "bad",
            },
            it =>
            {
                it.Address = "host.name";
                it.Port = 443;
            });
        void Change(Func<ProtocolExtraItem, ProtocolExtraItem> change)
        {
            config.SetProtocolExtra(change(config.GetProtocolExtra()));
        }

        await Normalize(config).Should().BeEqualTo(AetherProblem.InvalidMasqueSni);
        await config.GetProtocolExtra().AetherFragmentSize.Should().BeEqualTo("x");
        await config.Address.Should().BeEqualTo("host.name");

        Change(it => it with { AetherMasqueSni = " consumer-masque.cloudflareclient.com " });
        await Normalize(config).Should().BeEqualTo(AetherProblem.InvalidFragment);
        await config.GetProtocolExtra().AetherMasqueSni.Should().BeEqualTo("consumer-masque.cloudflareclient.com");

        Change(it => it with { AetherFragmentSize = " 32 - 16 " });
        await Normalize(config).Should().BeEqualTo(AetherProblem.InvalidPeer);
        await config.GetProtocolExtra().AetherFragmentSize.Should().BeEqualTo("16-32");

        config.Address = " 162.159.198.1 ";
        await Normalize(config).Should().BeEqualTo(AetherProblem.InvalidDns);
        await config.Address.Should().BeEqualTo("162.159.198.1");

        Change(it => it with { AetherDns = "1.1.1.1 8.8.8.8" });
        await Normalize(config).Should().BeEqualTo(AetherProblem.InvalidExitLoc);
        await config.GetProtocolExtra().AetherDns.Should().BeEqualTo("1.1.1.1,8.8.8.8");

        Change(it => it with { AetherExitLoc = "de" });
        await Normalize(config).Should().BeEqualTo(AetherProblem.InvalidEchDns);
        await config.GetProtocolExtra().AetherExitLoc.Should().BeEqualTo("DE");

        Change(it => it with { AetherEchDns = "udp://1.1.1.1", AetherEchDomain = "-bad-" });
        await Normalize(config).Should().BeEqualTo(AetherProblem.InvalidEchDomain);

        // Psiphon and Tor around the tunnel need MASQUE, checked after every MASQUE setting.
        Change(it => it with
        {
            AetherEchDomain = null,
            AetherProtocol = AetherProtocol.WireGuard.Type,
            AetherPsiphon = "reverse",
            AetherTor = "reverse",
            AetherTorBridges = "own",
        });
        await Normalize(config).Should().BeEqualTo(AetherProblem.PsiphonNeedsMasque);
        await config.GetProtocolExtra().AetherEchDns.Should().BeNull();

        Change(it => it with { AetherPsiphon = "chain" });
        await Normalize(config).Should().BeEqualTo(AetherProblem.TorNeedsMasque);
        await config.GetProtocolExtra().AetherPsiphonMode.Should().BeEqualTo("auto");

        Change(it => it with { AetherProtocol = AetherProtocol.Masque.Type });
        await Normalize(config).Should().BeEqualTo(AetherProblem.TorBridgesMissing);

        Change(it => it with { AetherTorBridgeLines = "obfs4 192.0.2.1:443 FP cert=x iat-mode=0" });
        await Normalize(config, takenPorts: Ports(10819)).Should().BeEqualTo(AetherProblem.ListenPortTaken);
        await config.GetProtocolExtra().AetherTorRelays.Should().BeEqualTo("auto");
        await Normalize(config, takenPorts: Ports(10821)).Should().BeEqualTo(AetherProblem.NextPortTaken);
        await Normalize(config, takenPorts: Ports(10822)).Should().BeNull();

        // A command written by hand comes last, once every setting has been rewritten.
        Change(it => it with { AetherCommand = " aether ", AetherExitLoc = " de " });
        await Normalize(config, takenPorts: Ports(10819)).Should().BeEqualTo(AetherProblem.InvalidCommand);
        await config.GetProtocolExtra().AetherExitLoc.Should().BeEqualTo("DE");
        await config.GetProtocolExtra().AetherCommand.Should().BeEqualTo("aether");

        // A fragment size is rewritten by the time the resolvers are refused, not by the time the server name is.
        var early = Profile(it => it with
        {
            AetherMasqueSni = "1.1.1.1",
            AetherDns = "x",
            AetherTransport = AetherTransport.Http2.Type,
            AetherFragment = true,
            AetherFragmentSize = " 32 - 16 ",
        });
        await Normalize(early).Should().BeEqualTo(AetherProblem.InvalidMasqueSni);
        await early.GetProtocolExtra().AetherFragmentSize.Should().BeEqualTo(" 32 - 16 ");
        var late = Copy(early, it => it with { AetherMasqueSni = null });
        await Normalize(late).Should().BeEqualTo(AetherProblem.InvalidDns);
        await late.GetProtocolExtra().AetherFragmentSize.Should().BeEqualTo("16-32");
    }

    // PattN: the settings are trimmed by Kotlin's whitespace, as PattNG trims them: the separators U+001C to U+001F count
    // as spaces there, U+0085 does not.
    [Test]
    public async Task SettingsAreTrimmedAsPattNGTrimsThem()
    {
        // A comment is no bridge line, whatever separator stands before it.
        await Normalize(Profile(it => it with { AetherTor = "only", AetherTorBridges = "own", AetherTorBridgeLines = "\u001F# nothing here\n" }))
            .Should().BeEqualTo(AetherProblem.TorBridgesMissing);
        var own = Profile(it => it with { AetherTor = "chain", AetherTorBridges = "own", AetherTorBridgeLines = "\u001Fobfs4 192.0.2.1:443 FP\u001F" });
        await Normalize(own).Should().BeNull();
        await own.GetProtocolExtra().AetherTorBridgeLines.Should().BeEqualTo("obfs4 192.0.2.1:443 FP");

        // A resolver is an address without the separators around it, which the core is never given.
        var dns = Profile(it => it with { AetherDns = "\u001F1.1.1.1" });
        await Normalize(dns).Should().BeNull();
        await AetherArguments.ValueAfter(AetherArguments.Build(dns, 10819), "--dns").Should().BeEqualTo("1.1.1.1");
        await Normalize(Profile(it => it with { AetherDns = "1.1.1.1\u0085" })).Should().BeEqualTo(AetherProblem.InvalidDns);
    }
}
