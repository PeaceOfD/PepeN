using TUnit.Assertions.Enums;

namespace ServiceLib.Tests.Aether;

// From PattNG AetherKeysTest. Ported elsewhere: eachFingerprintOffersTheTls12SuitesOfItsCapturedClientHello,
// aKindStoredBeforeWireGuardOverMasqueKeepsItsKeys, the Grease flags of onlyChromeSendsGrease and
// theFingerprintListNamesEveryFingerprintInItsOrder but its labels (AetherOptionsTests),
// anExitNodeTakesABlankFinalMaskOrAJsonObject (AetherExitTests).
public class AetherKeysTests
{
    /// <summary>The number of each TLS 1.2 suite by the name BoringSSL gives it, for the ones the fingerprints name.</summary>
    private static readonly Dictionary<string, int> SuiteIds = new()
    {
        ["ECDHE-ECDSA-AES128-GCM-SHA256"] = 0xc02b,
        ["ECDHE-RSA-AES128-GCM-SHA256"] = 0xc02f,
        ["ECDHE-ECDSA-AES256-GCM-SHA384"] = 0xc02c,
        ["ECDHE-RSA-AES256-GCM-SHA384"] = 0xc030,
        ["ECDHE-ECDSA-CHACHA20-POLY1305"] = 0xcca9,
        ["ECDHE-RSA-CHACHA20-POLY1305"] = 0xcca8,
        ["ECDHE-RSA-AES128-SHA"] = 0xc013,
        ["ECDHE-RSA-AES256-SHA"] = 0xc014,
        ["ECDHE-ECDSA-AES128-SHA"] = 0xc009,
        ["ECDHE-ECDSA-AES256-SHA"] = 0xc00a,
        ["ECDHE-ECDSA-AES128-SHA256"] = 0xc023,
        ["ECDHE-RSA-AES128-SHA256"] = 0xc027,
        ["AES128-GCM-SHA256"] = 0x009c,
        ["AES256-GCM-SHA384"] = 0x009d,
        ["AES128-SHA"] = 0x002f,
        ["AES256-SHA"] = 0x0035,
    };

    /// <summary>The number of each TLS 1.2 suite by the name Xray's cipherSuites takes, Go's, for the ones its presets name.</summary>
    private static readonly Dictionary<string, int> XraySuiteIds = new()
    {
        ["TLS_ECDHE_ECDSA_WITH_AES_128_GCM_SHA256"] = 0xc02b,
        ["TLS_ECDHE_RSA_WITH_AES_128_GCM_SHA256"] = 0xc02f,
        ["TLS_ECDHE_ECDSA_WITH_AES_256_GCM_SHA384"] = 0xc02c,
        ["TLS_ECDHE_RSA_WITH_AES_256_GCM_SHA384"] = 0xc030,
        ["TLS_ECDHE_ECDSA_WITH_CHACHA20_POLY1305_SHA256"] = 0xcca9,
        ["TLS_ECDHE_RSA_WITH_CHACHA20_POLY1305_SHA256"] = 0xcca8,
        ["TLS_ECDHE_RSA_WITH_AES_128_CBC_SHA"] = 0xc013,
        ["TLS_ECDHE_RSA_WITH_AES_256_CBC_SHA"] = 0xc014,
        ["TLS_ECDHE_ECDSA_WITH_AES_256_CBC_SHA"] = 0xc00a,
        ["TLS_ECDHE_ECDSA_WITH_AES_128_CBC_SHA256"] = 0xc023,
        ["TLS_ECDHE_RSA_WITH_AES_128_CBC_SHA256"] = 0xc027,
        ["TLS_RSA_WITH_AES_128_GCM_SHA256"] = 0x009c,
        ["TLS_RSA_WITH_AES_256_GCM_SHA384"] = 0x009d,
        ["TLS_RSA_WITH_AES_128_CBC_SHA"] = 0x002f,
        ["TLS_RSA_WITH_AES_256_CBC_SHA"] = 0x0035,
    };

    // As PattNG's test helper: the value after the last flag.
    private static string? ValueAfter(IReadOnlyList<string> arguments, string flag)
    {
        var at = arguments.ToList().LastIndexOf(flag);
        return at >= 0 && at + 1 < arguments.Count ? arguments[at + 1] : null;
    }

    // PattNG's assertEquals on lists: the same items in the same order.
    private static async Task ShouldBe<T>(IReadOnlyList<T> actual, params T[] expected)
    {
        await actual.Should().BeEquivalentTo(expected, CollectionOrdering.Matching);
    }

    /// <summary>The TLS 1.2 suites of a ClientHello's cipher_suites in hex, GREASE and the TLS 1.3 suites left out.</summary>
    private static List<int> Tls12Of(string captured)
    {
        return captured.Chunk(4)
            .Select(chunk => Convert.ToInt32(new string(chunk), 16))
            .Where(suite => (suite & 0x0f0f) != 0x0a0a && suite is not (>= 0x1301 and <= 0x1305))
            .ToList();
    }

    private static List<int> SuitesOf(AetherFingerprint fingerprint)
    {
        return fingerprint.Ciphers.Split(':').Select(name => SuiteIds[name]).ToList();
    }

    [Test]
    public async Task EachCipherSuitesPresetOffersTheTls12SuitesOfTheAetherFingerprintOfTheSameClient()
    {
        // The lists the cipherSuites field of the other protocols offers (PattNG: the app's resources; PattN: Global).
        var presets = Global.CipherSuitesPresets.ToDictionary(preset => preset.Name, preset => preset.Value);
        List<int> PresetSuites(string name) => presets[name].Split(':').Select(suite => XraySuiteIds[suite]).ToList();
        await ShouldBe(PresetSuites("real-firefox-cipherSuites"), [.. SuitesOf(AetherFingerprint.Firefox)]);
        await ShouldBe(PresetSuites("real-firefox-cipherSuites"), [.. Tls12Of("130113031302c02bc02fcca9cca8c02cc030c013c014009c009d002f0035")]);
        await ShouldBe(PresetSuites("semi-python-cipherSuites"), [.. SuitesOf(AetherFingerprint.SemiPython)]);
    }

    // The flags of the fingerprints are checked against their captures in AetherOptionsTests.
    [Test]
    public async Task TheKeysWindowGivesTheCoreEveryOptionOfTheFingerprint()
    {
        foreach (var fingerprint in AetherFingerprint.All)
        {
            var arguments = AetherKeys.Arguments(new AetherKeysSettings { Fingerprint = fingerprint });
            await ValueAfter(arguments, "--tls-ciphers").Should().BeEqualTo(fingerprint.Ciphers);
            await (ValueAfter(arguments, "--tls13-ciphers") == fingerprint.Tls13Ciphers).Should().BeTrue();
            await ValueAfter(arguments, "--tls-groups").Should().BeEqualTo(fingerprint.Groups);
            await (ValueAfter(arguments, "--tls-key-shares") == fingerprint.KeyShares).Should().BeTrue();
            await ValueAfter(arguments, "--tls-sigalgs").Should().BeEqualTo(fingerprint.Sigalgs);
            await (ValueAfter(arguments, "--tls-cert-compression") == fingerprint.CertCompression).Should().BeTrue();
            await (ValueAfter(arguments, "--tls-alps") == fingerprint.Alps).Should().BeTrue();
            await (ValueAfter(arguments, "--tls-trust-anchors") == fingerprint.TrustAnchors).Should().BeTrue();
            var padding = fingerprint.ServerPadding?.ToString(System.Globalization.CultureInfo.InvariantCulture);
            await (ValueAfter(arguments, "--tls-server-padding") == padding).Should().BeTrue();
            await arguments.Contains("--disable-grease").Should().BeEqualTo(!fingerprint.Grease);
            await arguments.Contains("--enable-grease-sigalgs").Should().BeEqualTo(fingerprint.GreaseSigalgs);
            await arguments.Contains("--enable-ech-grease").Should().BeEqualTo(fingerprint.EchGrease);
            await arguments.Contains("--disable-ocsp-stapling").Should().BeEqualTo(!fingerprint.OcspStapling);
            await arguments.Contains("--disable-sct").Should().BeEqualTo(!fingerprint.Sct);
            await arguments.Contains("--disable-session-ticket").Should().BeEqualTo(!fingerprint.SessionTicket);
            await arguments.Contains("--disable-extension-permutation").Should().BeEqualTo(!fingerprint.PermuteExtensions);
        }
    }

    [Test]
    public async Task TheDefaultsGetEveryKeyTheWayTheCoreCallsTheApiWithChromesRule()
    {
        // No address, server name or host: the core's own, api.devices.cloudflare.com, then api.cloudflareclient.com when
        // that one gets no answer.
        await ShouldBe(AetherKeys.Arguments(new AetherKeysSettings()), ["--register", "all", .. AetherFingerprint.Chrome.Arguments]);
        await AetherKeys.BuiltCommand(new AetherKeysSettings())
            .Should().BeEqualTo(
                "aether --register all --tls-ciphers ALL:!aPSK:!ECDSA+SHA1:!3DES --tls-groups X25519MLKEM768:X25519:P-256:P-384 "
                + $"--tls-sigalgs {AetherFingerprint.Chrome.Sigalgs} --tls-cert-compression brotli --tls-alps new "
                + $"--tls-trust-anchors {AetherFingerprint.Chrome.TrustAnchors} --tls-server-padding 6000 "
                + "--enable-grease-sigalgs --enable-ech-grease");
        await AetherKeys.DefaultApiHost.Should().BeEqualTo("api.devices.cloudflare.com");
        await AetherKeys.FallbackApiHost.Should().BeEqualTo("api.cloudflareclient.com");
        // A plain exit-node.
        await new AetherKeysSettings().Exit.Should().BeEqualTo(new AetherExit());
    }

    [Test]
    public async Task EachKindRegistersItsKeysAndReadsBack()
    {
        foreach (var kind in AetherKeyKind.All)
        {
            var arguments = AetherKeys.Arguments(new AetherKeysSettings { Kind = kind });
            await ShouldBe(arguments.Take(2).ToList(), "--register", kind.Register);
            await AetherKeys.KindOf(arguments).Should().BeEqualTo(kind);
        }
    }

    [Test]
    public async Task TheKindIsReadAsTheCoreReadsIt()
    {
        await AetherKeys.KindOf(["--register", "WireGuard"]).Should().BeEqualTo(AetherKeyKind.WireGuard);
        await AetherKeys.KindOf(["--register", "warp"]).Should().BeEqualTo(AetherKeyKind.WireGuard);
        await AetherKeys.KindOf(["--register", "warp-in-warp"]).Should().BeEqualTo(AetherKeyKind.Gool);
        await AetherKeys.KindOf(["--register", " wiw "]).Should().BeEqualTo(AetherKeyKind.Gool);
        await AetherKeys.KindOf(["--register", "gool-classic"]).Should().BeEqualTo(AetherKeyKind.Gool);
        // gool is what the core's --gool runs, WireGuard over MASQUE.
        await AetherKeys.KindOf(["--register", "gool"]).Should().BeEqualTo(AetherKeyKind.WgOverMasque);
        await AetherKeys.KindOf(["--register", "WG-over-MASQUE"]).Should().BeEqualTo(AetherKeyKind.WgOverMasque);
        await AetherKeys.KindOf(["--register", "masque-in-masque"]).Should().BeEqualTo(AetherKeyKind.Mim);
        // The last one counts.
        await AetherKeys.KindOf(["--register", "all", "--register", "masque"]).Should().BeEqualTo(AetherKeyKind.Masque);
        await AetherKeys.KindOf(["--register", "everything"]).Should().BeNull();
        await AetherKeys.KindOf(["--register"]).Should().BeNull();
        await AetherKeys.KindOf(["--protocol", "wg"]).Should().BeNull();
    }

    [Test]
    public async Task EchAsksForTheKeyWhereTheSettingsSayAndKeepsTheDefaultsForBlanks()
    {
        var ech = AetherKeys.Arguments(new AetherKeysSettings { Ech = true, EchDns = "https://1.1.1.1/dns-query", EchDomain = " example.com " });
        await ValueAfter(ech, "--ech").Should().BeEqualTo("auto");
        await ValueAfter(ech, "--ech-dns").Should().BeEqualTo("https://1.1.1.1/dns-query");
        await ValueAfter(ech, "--ech-domain").Should().BeEqualTo("example.com");

        var defaults = AetherKeys.Arguments(new AetherKeysSettings { Ech = true, EchDns = " ", EchDomain = "" });
        await ValueAfter(defaults, "--ech-dns").Should().BeEqualTo("udp://1.1.1.1");
        await ValueAfter(defaults, "--ech-domain").Should().BeEqualTo("cloudflare-ech.com");

        // Off, nothing about ECH goes along, whatever the fields hold.
        await AetherKeys.Arguments(new AetherKeysSettings { EchDns = "tcp://8.8.8.8" }).Any(it => it.StartsWith("--ech")).Should().BeFalse();
    }

    [Test]
    public async Task ABlankApiAddressLeavesTheCoresOwn()
    {
        await AetherKeys.Arguments(new AetherKeysSettings { EnrollAddress = "  " }).Contains("--api-address").Should().BeFalse();
        await ValueAfter(AetherKeys.Arguments(new AetherKeysSettings { EnrollAddress = " 188.114.97.6:443 " }), "--api-address")
            .Should().BeEqualTo("188.114.97.6:443");
    }

    [Test]
    public async Task TheApiFieldsOfferTheTwoNamesOfTheApiAndStartBlank()
    {
        // What the API address, server name and host offer to pick: the name the core goes by first, then the other one.
        await AetherKeys.ApiNameOptions.Should()
            .BeEquivalentTo(["api.devices.cloudflare.com", "api.cloudflareclient.com"], CollectionOrdering.Matching);
        // None of them is chosen until one is picked or written: blank is the core's own.
        var defaults = new AetherKeysSettings();
        await defaults.EnrollAddress.Should().BeEqualTo(string.Empty);
        await defaults.ApiSni.Should().BeEqualTo(string.Empty);
        await defaults.ApiHost.Should().BeEqualTo(string.Empty);
        // Each name picked goes to the core as it is.
        foreach (var name in AetherKeys.ApiNameOptions)
        {
            var picked = new AetherKeysSettings { EnrollAddress = name, ApiSni = name, ApiHost = name };
            await AetherKeys.Problem(picked).Should().BeNull().Because(name);
            var arguments = AetherKeys.Arguments(picked);
            await ValueAfter(arguments, "--api-address").Should().BeEqualTo(name);
            await ValueAfter(arguments, "--api-sni").Should().BeEqualTo(name);
            await ValueAfter(arguments, "--api-host").Should().BeEqualTo(name);
        }
    }

    [Test]
    public async Task TheApiServerNameAndHostGoTrimmedAndBlankLeavesTheCoresOwn()
    {
        var blank = AetherKeys.Arguments(new AetherKeysSettings { ApiSni = " ", ApiHost = "" });
        await blank.Contains("--api-sni").Should().BeFalse();
        await blank.Contains("--api-host").Should().BeFalse();
        // Each one apart, after the address, as the core takes them; the same name for both, as Cloudflare wants.
        await ShouldBe(
            AetherKeys.Arguments(new AetherKeysSettings
            {
                EnrollAddress = "162.159.137.105",
                ApiSni = " api.cloudflareclient.com ",
                ApiHost = "api.cloudflareclient.com ",
            }),
            [
                "--register", "all",
                "--api-address", "162.159.137.105",
                "--api-sni", "api.cloudflareclient.com",
                "--api-host", "api.cloudflareclient.com",
                .. AetherFingerprint.Chrome.Arguments,
            ]);
        // One alone leaves the others to the core.
        var hostOnly = AetherKeys.Arguments(new AetherKeysSettings { ApiHost = "api.cloudflareclient.com" });
        await ValueAfter(hostOnly, "--api-host").Should().BeEqualTo("api.cloudflareclient.com");
        await hostOnly.Contains("--api-sni").Should().BeFalse();
        await hostOnly.Contains("--api-address").Should().BeFalse();
    }

    [Test]
    public async Task TheRequestAddressIsWhatTheCoreTakes()
    {
        foreach (var address in new[]
        {
            "api.cloudflareclient.com", "api.cloudflareclient.com:443", "api.cloudflareclient.com.", "188.114.97.6",
            "188.114.97.6:2053", "2606:4700::1", "[2606:4700::1]", "[2606:4700::1]:8443", "[188.114.97.6]:443",
        })
        {
            await AetherKeys.IsEnrollAddress(address).Should().BeTrue().Because(address);
        }
        foreach (var address in new[]
        {
            "", "-x", "--upstream", "a b", "188.114.97.6:0", "188.114.97.6:65536", "188.114.97.6:", ":443",
            "188.114.97.6:4a3", "2606:4700::1:", "[2606:4700::1", "[2606:4700::1]:", "https://api.cloudflareclient.com",
            "api.-cloudflareclient.com",
            // An IPv6 address takes brackets before a port.
            "1:2:3:4:5:6:7:8:443",
        })
        {
            await AetherKeys.IsEnrollAddress(address).Should().BeFalse().Because(address);
        }
    }

    [Test]
    public async Task EachProblemIsFoundAndNothingElse()
    {
        var fine = new AetherKeysSettings();
        await AetherKeys.Problem(fine).Should().BeNull();
        await AetherKeys.Problem(fine with { EnrollAddress = "-x" }).Should().BeEqualTo(AetherKeysProblem.InvalidEnrollAddress);
        await AetherKeys.Problem(fine with { EnrollAddress = "" }).Should().BeNull();
        // The server name and the host are domain names, as the core takes them, though no label starts or ends with '-',
        // which the core would take, so that no value reads as an option; blank, the core's own.
        foreach (var name in new[] { "", "  ", "api.devices.cloudflare.com", " api.cloudflareclient.com. " })
        {
            await AetherKeys.Problem(fine with { ApiSni = name }).Should().BeNull().Because(name);
            await AetherKeys.Problem(fine with { ApiHost = name }).Should().BeNull().Because(name);
        }
        foreach (var name in new[]
                 {
                     "162.159.137.105", "2606:4700:7::a29f:8969", "https://api.devices.cloudflare.com", "api.devices.cloudflare.com:443",
                     "-x", "a b",
                 })
        {
            await AetherKeys.Problem(fine with { ApiSni = name }).Should().BeEqualTo(AetherKeysProblem.InvalidApiSni).Because(name);
            await AetherKeys.Problem(fine with { ApiHost = name }).Should().BeEqualTo(AetherKeysProblem.InvalidApiHost).Because(name);
        }
        // The ECH fields count while ECH is on.
        await AetherKeys.Problem(fine with { EchDns = "dns.google" }).Should().BeNull();
        await AetherKeys.Problem(fine with { Ech = true, EchDns = "dns.google" }).Should().BeEqualTo(AetherKeysProblem.InvalidEchDns);
        await AetherKeys.Problem(fine with { Ech = true, EchDomain = "-x" }).Should().BeEqualTo(AetherKeysProblem.InvalidEchDomain);
        await AetherKeys.Problem(fine with { Ech = true, EchDns = "", EchDomain = "" }).Should().BeNull();
        await AetherKeys.Problem(fine with { FinalMask = "{not json" }).Should().BeEqualTo(AetherKeysProblem.InvalidFinalMask);
        await AetherKeys.Problem(fine with { FinalMask = """{"tcp": []}""", DialMode = "anything" }).Should().BeNull();
    }

    [Test]
    public async Task ACommandWrittenByHandRunsAsWritten()
    {
        var settings = new AetherKeysSettings { EnrollAddress = "-x", Command = "aether --register wg --tor-reverse" };
        await AetherKeys.IsCustom(settings).Should().BeTrue();
        await ShouldBe(AetherKeys.RunArguments(settings), "--register", "wg", "--tor-reverse");
        // The settings it replaces do not count; what it registers does, and so does the exit-node it dials out through.
        await AetherKeys.Problem(settings).Should().BeNull();
        await AetherKeys.Problem(settings with { Command = "aether --protocol wg" }).Should().BeEqualTo(AetherKeysProblem.InvalidCommand);
        await AetherKeys.Problem(settings with { Command = "--register every" }).Should().BeEqualTo(AetherKeysProblem.InvalidCommand);
        await AetherKeys.Problem(settings with { FinalMask = "[" }).Should().BeEqualTo(AetherKeysProblem.InvalidFinalMask);

        // The command the settings give, or none, is no command of its own.
        var built = new AetherKeysSettings { Kind = AetherKeyKind.Mim };
        await AetherKeys.IsCustom(built with { Command = " " + AetherKeys.BuiltCommand(built) + " " }).Should().BeFalse();
        await AetherKeys.IsCustom(built with { Command = "" }).Should().BeFalse();
        await ShouldBe(AetherKeys.RunArguments(built with { Command = AetherKeys.BuiltCommand(built) }), [.. AetherKeys.Arguments(built)]);
    }

    [Test]
    public async Task TheFragmentSendsTheClientHelloOfTheApiInPiecesShapedAsSet()
    {
        var on = new AetherKeysSettings { Fragment = true };
        await ShouldBe(AetherKeys.Arguments(on), ["--register", "all", "--fragment", .. AetherFingerprint.Chrome.Arguments]);
        var shaped = AetherKeys.Arguments(on with { FragmentSize = " 16 - 8 ", FragmentDelay = "5" });
        await ValueAfter(shaped, "--fragment-size").Should().BeEqualTo("8-16");
        await ValueAfter(shaped, "--fragment-delay").Should().BeEqualTo("5");
        // Off, the sizes are kept, but nothing of them reaches the core, and no flag says off: the core takes none.
        var off = AetherKeys.Arguments(on with { Fragment = false, FragmentSize = "8", FragmentDelay = "5" });
        await off.Contains("--fragment").Should().BeFalse();
        await off.Contains("--no-fragment").Should().BeFalse();
        await off.Contains("--fragment-size").Should().BeFalse();
        await off.Contains("--fragment-delay").Should().BeFalse();
        // A size or a delay that is no number or range stops the run, only while fragmenting is on.
        await AetherKeys.Problem(on with { FragmentSize = "0" }).Should().BeEqualTo(AetherKeysProblem.InvalidFragment);
        await AetherKeys.Problem(on with { FragmentDelay = "x" }).Should().BeEqualTo(AetherKeysProblem.InvalidFragment);
        await AetherKeys.Problem(on with { FragmentSize = "8-16", FragmentDelay = "2-10" }).Should().BeNull();
        await AetherKeys.Problem(on with { Fragment = false, FragmentSize = "0" }).Should().BeNull();
    }

    // PattN: the window reads its settings by Kotlin's whitespace all the way, as the Aether editor reads a profile's:
    // the separators U+001C to U+001F are spaces, U+0085 is not.
    [Test]
    public async Task TheFragmentIsReadAsTheEditorReadsAProfiles()
    {
        var on = new AetherKeysSettings { Fragment = true, FragmentSize = "5\u001F", FragmentDelay = "\u001F1-2" };
        await AetherKeys.Problem(on).Should().BeNull();
        var arguments = AetherKeys.Arguments(on);
        await ValueAfter(arguments, "--fragment-size").Should().BeEqualTo("5");
        await ValueAfter(arguments, "--fragment-delay").Should().BeEqualTo("1-2");
        await AetherKeys.Problem(on with { FragmentSize = "5\u0085" }).Should().BeEqualTo(AetherKeysProblem.InvalidFragment);
    }

    [Test]
    public async Task AProfileAsTheExitNodeLeavesTheFinalMaskAndTheDialModeOutOfUse()
    {
        var noded = new AetherKeysSettings { ExitNode = "germany", FinalMask = """{"tcp": []}""", DialMode = "ForceIP" };
        await noded.Exit.Should().BeEqualTo(new AetherExit(Node: "germany"));
        await (noded with { ExitNode = " germany " }).Exit.Should().BeEqualTo(new AetherExit(Node: "germany"));
        await AetherKeys.Problem(noded).Should().BeNull();
        // A broken finalMask is refused all the same, as the Aether editor refuses it: none is kept for freedom.
        await AetherKeys.Problem(noded with { FinalMask = "{not json" }).Should().BeEqualTo(AetherKeysProblem.InvalidFinalMask);
        await AetherKeys.Problem(noded with { ExitNode = "", FinalMask = "{not json" }).Should().BeEqualTo(AetherKeysProblem.InvalidFinalMask);
        await (noded with { ExitNode = " ", FinalMask = "" }).Exit.Should().BeEqualTo(new AetherExit(DialMode: "ForceIP"));
        // The exit-node is Xray's, so the command of the run stays as it was.
        await ShouldBe(AetherKeys.Arguments(new AetherKeysSettings { ExitNode = "germany" }), [.. AetherKeys.Arguments(new AetherKeysSettings())]);
    }

    [Test]
    public async Task TheExitNodeTakesTheFinalMaskAndTheDialMode()
    {
        var settings = new AetherKeysSettings { FinalMask = """{"tcp": []}""", DialMode = "ForceIP" };
        await settings.Exit.Should().BeEqualTo(new AetherExit("""{"tcp": []}""", "ForceIP"));
        await (settings with { FinalMask = " ", DialMode = "" }).Exit.Should().BeEqualTo(new AetherExit());
    }
}
