namespace ServiceLib.Tests.Aether;

public class AetherOptionsTests
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

    /// <summary>The number of each group by the name BoringSSL gives it, for every group it has (boring 5.3, with its post-quantum patch).</summary>
    private static readonly Dictionary<string, int> GroupIds = new()
    {
        ["P-256"] = 0x0017,
        ["P-384"] = 0x0018,
        ["P-521"] = 0x0019,
        ["X25519"] = 0x001d,
        ["X25519MLKEM768"] = 0x11ec,
        ["MLKEM1024"] = 0x0202,
        ["X25519Kyber768Draft00"] = 0x6399,
        ["P256Kyber768Draft00"] = 0xfe32,
    };

    /// <summary>The number of each TLS 1.3 suite by its standard name.</summary>
    private static readonly Dictionary<string, int> Tls13Ids = new()
    {
        ["TLS_AES_128_GCM_SHA256"] = 0x1301,
        ["TLS_AES_256_GCM_SHA384"] = 0x1302,
        ["TLS_CHACHA20_POLY1305_SHA256"] = 0x1303,
    };

    /// <summary>The TLS 1.3 suites of a ClientHello's cipher_suites in hex, in order.</summary>
    private static List<int> Tls13Of(string captured)
    {
        return captured.Chunk(4)
            .Select(chunk => Convert.ToInt32(new string(chunk), 16))
            .Where(suite => suite is >= 0x1301 and <= 0x1305)
            .ToList();
    }

    /// <summary>The groups of a ClientHello's supported_groups in hex that BoringSSL has, in order: GREASE and the rest left out.</summary>
    private static List<int> CapturedGroups(string captured)
    {
        return captured.Chunk(4)
            .Select(chunk => Convert.ToInt32(new string(chunk), 16))
            .Where(GroupIds.ContainsValue)
            .ToList();
    }

    private static List<int> GroupsOf(AetherFingerprint fingerprint)
    {
        return fingerprint.Groups.Split(':').Select(name => GroupIds[name]).ToList();
    }

    /// <summary>Whether <paramref name="value" /> is a GREASE value (RFC 8701).</summary>
    private static bool Grease(int value)
    {
        return (value & 0x0f0f) == 0x0a0a && value >> 8 == (value & 0xff);
    }

    /// <summary>The numbers of a ClientHello field in hex, two bytes each.</summary>
    private static List<int> Values(string captured)
    {
        return captured.Chunk(4).Select(chunk => Convert.ToInt32(new string(chunk), 16)).ToList();
    }

    /// <summary>The number of each signature algorithm by the name BoringSSL gives it, for every one it has (boring 5.3).</summary>
    private static readonly Dictionary<string, int> SigalgIds = new()
    {
        ["rsa_pkcs1_sha1"] = 0x0201,
        ["rsa_pkcs1_sha256"] = 0x0401,
        ["rsa_pkcs1_sha384"] = 0x0501,
        ["rsa_pkcs1_sha512"] = 0x0601,
        ["ecdsa_sha1"] = 0x0203,
        ["ecdsa_secp256r1_sha256"] = 0x0403,
        ["ecdsa_secp384r1_sha384"] = 0x0503,
        ["ecdsa_secp521r1_sha512"] = 0x0603,
        ["rsa_pss_rsae_sha256"] = 0x0804,
        ["rsa_pss_rsae_sha384"] = 0x0805,
        ["rsa_pss_rsae_sha512"] = 0x0806,
        ["ed25519"] = 0x0807,
        ["mldsa44"] = 0x0904,
        ["mldsa65"] = 0x0905,
        ["mldsa87"] = 0x0906,
    };

    /// <summary>The signature algorithms of a ClientHello's signature_algorithms in hex that BoringSSL has, in order: GREASE and the rest left out.</summary>
    private static List<int> CapturedSigalgs(string captured)
    {
        return Values(captured).Where(SigalgIds.ContainsValue).ToList();
    }

    private static List<int> SigalgsOf(AetherFingerprint fingerprint)
    {
        return fingerprint.Sigalgs.Split(':').Select(name => SigalgIds[name]).ToList();
    }

    /// <summary>The number of each certificate compression algorithm (RFC 8879) by its name.</summary>
    private static readonly Dictionary<string, int> CertCompressionIds = new()
    {
        ["zlib"] = 1,
        ["brotli"] = 2,
        ["zstd"] = 3,
    };

    /// <summary>The algorithms of a ClientHello's compress_certificate in hex, after the byte of their length.</summary>
    private static List<int> CapturedCompression(string captured)
    {
        return Values(captured[2..]);
    }

    private static List<int> CompressionOf(AetherFingerprint fingerprint)
    {
        return fingerprint.CertCompression!.Split(':').Select(name => CertCompressionIds[name]).ToList();
    }

    /// <summary>Trust anchor IDs in dotted decimal as trust_anchors carries them, in hex: each ID's arcs in base 128, after its length.</summary>
    private static string TrustAnchorsHex(string ids)
    {
        var hex = new System.Text.StringBuilder();
        foreach (var id in ids.Split(':'))
        {
            var bytes = new List<int>();
            foreach (var arc in id.Split('.'))
            {
                var value = long.Parse(arc, System.Globalization.CultureInfo.InvariantCulture);
                var base128 = new List<int> { (int)(value & 0x7f) };
                value >>= 7;
                while (value > 0)
                {
                    base128.Add(0x80 | (int)(value & 0x7f));
                    value >>= 7;
                }
                base128.Reverse();
                bytes.AddRange(base128);
            }
            hex.Append(bytes.Count.ToString("x2", System.Globalization.CultureInfo.InvariantCulture));
            foreach (var value in bytes)
            {
                hex.Append(value.ToString("x2", System.Globalization.CultureInfo.InvariantCulture));
            }
        }
        return hex.ToString();
    }

    // From PattNG AetherFmtTest; the assertion on a link written before the rename goes with AetherFmt.
    [Test]
    public async Task TheOldNameOfTheVerifiedScanModeStillReads()
    {
        await AetherScanMode.FromString("stealth").Should().BeEqualTo(AetherScanMode.Verified);
        await AetherScanMode.FromString("verified").Should().BeEqualTo(AetherScanMode.Verified);
        await AetherScanMode.FromString("quiet").Should().BeEqualTo(AetherScanMode.Balanced);
    }

    // From PattNG AetherKeysTest.
    [Test]
    public async Task AKindStoredBeforeWireGuardOverMasqueKeepsItsKeys()
    {
        // The page stored the type of the protocol; WARP-in-WARP's keys are still both WireGuard hops' ones.
        await AetherKeyKind.FromString("gool").Should().BeEqualTo(AetherKeyKind.Gool);
        await AetherKeyKind.Gool.Register.Should().BeEqualTo("gool-classic");
        await AetherKeyKind.FromString("wg-over-masque").Should().BeEqualTo(AetherKeyKind.WgOverMasque);
        await AetherKeyKind.FromString(null).Should().BeEqualTo(AetherKeyKind.AllKeys);
    }

    // From PattNG AetherKeysTest.
    [Test]
    public async Task EachFingerprintOffersTheTls12SuitesOfItsCapturedClientHello()
    {
        // The ClientHellos captured with Wireshark. Chrome's, eaea130113021303c02bc02fc02cc030cca9cca8c013c014009c009d002f0035,
        // comes from Chrome's own rule, which BoringSSL orders by the machine's AES instructions as it does the TLS 1.3
        // suites: these twelve with them, ChaCha20 first without (both seen on boring 5.2.0). So Chrome names its rule,
        // as Chromium sets it, and keeps GREASE.
        await AetherFingerprint.Chrome.Ciphers.Should().BeEqualTo("ALL:!aPSK:!ECDSA+SHA1:!3DES");
        // Chrome 120's, baba130113021303c02bc02fc02cc030cca9cca8c013c014009c009d002f0035, comes from the same rule: the same suites in the
        // same order.
        await AetherFingerprint.Chrome120.Ciphers.Should().BeEqualTo(AetherFingerprint.Chrome.Ciphers);
        await Tls12Of("baba130113021303c02bc02fc02cc030cca9cca8c013c014009c009d002f0035")
            .SequenceEqual(Tls12Of("eaea130113021303c02bc02fc02cc030cca9cca8c013c014009c009d002f0035"))
            .Should().BeTrue();
        await SuitesOf(AetherFingerprint.Firefox).SequenceEqual(Tls12Of("130113031302c02bc02fcca9cca8c02cc030c013c014009c009d002f0035")).Should().BeTrue();
        // Python's first ten, with c00a and c014 (AES-256-CBC with SHA-1) in place of c024 and c028 (with SHA-384),
        // which BoringSSL does not have; its four DHE suites after them are left out.
        var nearest = new Dictionary<int, int> { [0xc024] = 0xc00a, [0xc028] = 0xc014 };
        var python = Tls12Of("130213031301c02cc030c02bc02fcca9cca8c024c028c023c027009f009e006b0067")
            .Take(10)
            .Select(suite => nearest.GetValueOrDefault(suite, suite));
        await SuitesOf(AetherFingerprint.SemiPython).SequenceEqual(python).Should().BeTrue();
        // Go lists its TLS 1.3 suites last, which BoringSSL cannot do; its TLS 1.2 suites are all there.
        await SuitesOf(AetherFingerprint.Go).SequenceEqual(Tls12Of("c02bc02fc02cc030cca9cca8c009c013c00ac014130113021303")).Should().BeTrue();
    }

    // From PattNG AetherKeysTest.
    [Test]
    public async Task EachFingerprintOffersTheGroupsOfItsCapturedClientHelloThatBoringSslHas()
    {
        // The supported_groups of the ClientHellos captured in October 2026. Chrome's and Chrome 120's lead with a GREASE
        // value, and Chrome 120's has no post-quantum group; Python's
        // also lists X448, ffdhe2048 and ffdhe3072, and Go's SecP256r1MLKEM768 and SecP384r1MLKEM1024, none of which
        // BoringSSL has.
        await GroupsOf(AetherFingerprint.Chrome).SequenceEqual(CapturedGroups("5a5a11ec001d00170018")).Should().BeTrue();
        await GroupsOf(AetherFingerprint.Chrome120).SequenceEqual(CapturedGroups("5a5a001d00170018")).Should().BeTrue();
        await GroupsOf(AetherFingerprint.Firefox).SequenceEqual(CapturedGroups("11ec001d001700180019")).Should().BeTrue();
        await GroupsOf(AetherFingerprint.SemiPython).SequenceEqual(CapturedGroups("11ec001d0017001e0018001901000101")).Should().BeTrue();
        await GroupsOf(AetherFingerprint.Go).SequenceEqual(CapturedGroups("11ec11eb11ed001d001700180019")).Should().BeTrue();
    }

    // From PattNG AetherKeysTest.
    [Test]
    public async Task EachFingerprintOffersTheTls13OrderAndTheKeySharesOfItsCapturedClientHello()
    {
        // Firefox and Python list their TLS 1.3 suites in an order of their own, which their fingerprints name.
        static List<int> Named(string? list) => list!.Split(':').Select(name => Tls13Ids[name]).ToList();
        await Named(AetherFingerprint.Firefox.Tls13Ciphers)
            .SequenceEqual(Tls13Of("130113031302c02bc02fcca9cca8c02cc030c013c014009c009d002f0035"))
            .Should().BeTrue();
        await Named(AetherFingerprint.SemiPython.Tls13Ciphers)
            .SequenceEqual(Tls13Of("130213031301c02cc030c02bc02fcca9cca8c024c028c023c027009f009e006b0067"))
            .Should().BeTrue();
        // Chrome's and Go's are BoringSSL's own order, by the machine's AES instructions, as their captures on a PC with
        // them show; a fixed list would keep AES-GCM first where they go ChaCha20 first.
        await Tls13Of("eaea130113021303c02bc02fc02cc030cca9cca8c013c014009c009d002f0035").SequenceEqual([0x1301, 0x1302, 0x1303]).Should().BeTrue();
        await Tls13Of("c02bc02fc02cc030cca9cca8c009c013c00ac014130113021303").SequenceEqual([0x1301, 0x1302, 0x1303]).Should().BeTrue();
        await Tls13Of("baba130113021303c02bc02fc02cc030cca9cca8c013c014009c009d002f0035").SequenceEqual([0x1301, 0x1302, 0x1303]).Should().BeTrue();
        await AetherFingerprint.Chrome.Tls13Ciphers.Should().BeNull();
        await AetherFingerprint.Chrome120.Tls13Ciphers.Should().BeNull();
        await AetherFingerprint.Go.Tls13Ciphers.Should().BeNull();

        // Firefox's key_share carries X25519MLKEM768, X25519 and P-256; the others carry the two BoringSSL sends, the
        // first group and the first that is not post-quantum.
        await AetherFingerprint.Firefox.KeyShares!.Split(':').Select(name => GroupIds[name]).SequenceEqual([0x11ec, 0x001d, 0x0017]).Should().BeTrue();
        foreach (var fingerprint in new[] { AetherFingerprint.Chrome, AetherFingerprint.Chrome120, AetherFingerprint.SemiPython, AetherFingerprint.Go })
        {
            await fingerprint.KeyShares.Should().BeNull();
        }
    }

    // From PattNG AetherKeysTest.
    [Test]
    public async Task EachFingerprintOffersTheSignatureAlgorithmsOfItsCapturedClientHelloThatBoringSslHas()
    {
        // The signature_algorithms of the ClientHellos captured in October 2026. Chrome's leads with a GREASE value, which
        // GreaseSigalgs gives, then ML-DSA; Python's also lists Ed448, the brainpool curves, RSA-PSS with PSS keys, SHA-224
        // and DSA, none of which BoringSSL has.
        var captured = new Dictionary<AetherFingerprint, string>
        {
            [AetherFingerprint.Chrome] = "2a2a09040905090604030804040105030805050108060601",
            [AetherFingerprint.Chrome120] = "04030804040105030805050108060601",
            [AetherFingerprint.Firefox] = "04030503060308040805080604010501060102030201",
            [AetherFingerprint.SemiPython] =
                "09050906090404030503060308070808081a081b081c0809080a080b080408050806040105010601030303010302040205020602",
            [AetherFingerprint.Go] = "0904090509060804040308070805080604010501060105030603",
        };
        await captured.Keys.ToHashSet().SetEquals(AetherFingerprint.All).Should().BeTrue();
        foreach (var (fingerprint, hex) in captured)
        {
            await SigalgsOf(fingerprint).SequenceEqual(CapturedSigalgs(hex)).Should().BeTrue();
            await fingerprint.GreaseSigalgs.Should().BeEqualTo(Grease(Values(hex)[0]));
        }
        // Only Python's list loses algorithms to BoringSSL: the others keep every one of theirs, GREASE aside.
        foreach (var fingerprint in AetherFingerprint.All.Where(it => it != AetherFingerprint.SemiPython))
        {
            var all = Values(captured[fingerprint]).Where(value => !Grease(value));
            await SigalgsOf(fingerprint).SequenceEqual(all).Should().BeTrue();
        }
    }

    // From PattNG AetherKeysTest.
    [Test]
    public async Task EachFingerprintSendsTheExtensionsOfItsCapturedClientHelloThatBoringSslCanSend()
    {
        // The types of the extensions of the ClientHellos captured in October 2026, in their order.
        var captured = new Dictionary<AetherFingerprint, string>
        {
            [AetherFingerprint.Chrome] = "aaaa0000000aca34002d001700330005000b0012000dfe0d12e0002bff0144cd00230010001bcaca",
            [AetherFingerprint.Chrome120] = "eaea00230010002b0000000b000a000dfe0d001b00124469ff010017002d003300053a3a",
            [AetherFingerprint.Firefox] = "00000017ff01000a000b00100005002200120033002b000d001c001bfe0d",
            [AetherFingerprint.SemiPython] = "ff010000000b000a0023001000160017000d002b002d0033",
            [AetherFingerprint.Go] = "0000000bff01001700120005000a000d00320010002b0033",
        };
        await captured.Keys.ToHashSet().SetEquals(AetherFingerprint.All).Should().BeTrue();
        foreach (var (fingerprint, types) in captured)
        {
            var all = Values(types);
            var sent = all.Where(value => !Grease(value)).ToHashSet();
            await fingerprint.Grease.Should().BeEqualTo(all.Any(Grease));
            await fingerprint.OcspStapling.Should().BeEqualTo(sent.Contains(0x0005));
            await fingerprint.Sct.Should().BeEqualTo(sent.Contains(0x0012));
            await fingerprint.SessionTicket.Should().BeEqualTo(sent.Contains(0x0023));
            await (fingerprint.CertCompression is not null).Should().BeEqualTo(sent.Contains(0x001b));
            // An ECH where there is no key to offer is a GREASE one; Chrome's and Firefox's captures went with
            // Cloudflare's key, Chrome 120's with a GREASE one.
            await fingerprint.EchGrease.Should().BeEqualTo(sent.Contains(0xfe0d));
            await (fingerprint.TrustAnchors is not null).Should().BeEqualTo(sent.Contains(0xca34));
            await (fingerprint.ServerPadding is not null).Should().BeEqualTo(sent.Contains(0x12e0));
            var alps = sent.Contains(0x44cd) ? "new" : sent.Contains(0x4469) ? "old" : null;
            await fingerprint.Alps.Should().BeEqualTo(alps);
        }

        // What they carry: compress_certificate, trust_anchors after the two bytes of its length, server_padding.
        await CompressionOf(AetherFingerprint.Chrome).SequenceEqual(CapturedCompression("020002")).Should().BeTrue();
        await CompressionOf(AetherFingerprint.Chrome120).SequenceEqual(CapturedCompression("020002")).Should().BeTrue();
        await CompressionOf(AetherFingerprint.Firefox).SequenceEqual(CapturedCompression("06000100020003")).Should().BeTrue();
        const string trustAnchors =
            "00b80582df1302010582df1302060582df13020d0582df13020e0582df13020f0582df1302120582df1302130582df130214"
            + "08839a648c9b2d010708839a648c9b2d010808839a648c9b2d010908839a648c9b2d010a08839a648c9b2d010b08839a648c9b2d010c"
            + "08839a648c9b2d010d08839a648c9b2d011208839a648c9b2d011304d679090104d679090404d679090504d679090604d679090704d6"
            + "79090804d679090a04d679090b04d679090c04d679090d04d679090f";
        await TrustAnchorsHex(AetherFingerprint.Chrome.TrustAnchors!).Should().BeEqualTo(trustAnchors[4..]);
        await (Convert.ToInt32(trustAnchors[..4], 16) * 2).Should().BeEqualTo(trustAnchors.Length - 4);
        await AetherFingerprint.Chrome.ServerPadding.Should().BeEqualTo(Convert.ToInt32("1770", 16));

        // Chrome has sent its extensions in a new order on each handshake since its version 110, Chrome 120 too;
        // Firefox, Python and Go keep one order, which no capture shows alone.
        await AetherFingerprint.All.Where(it => it.PermuteExtensions)
            .SequenceEqual([AetherFingerprint.Chrome, AetherFingerprint.Chrome120])
            .Should().BeTrue();
    }

    // From PattNG AetherKeysTest; the check of AetherKeys.arguments for every fingerprint goes with AetherKeys.
    [Test]
    public async Task OnlyChromeSendsGrease()
    {
        await AetherFingerprint.Chrome.Grease.Should().BeTrue();
        await AetherFingerprint.Chrome120.Grease.Should().BeTrue();
        await AetherFingerprint.Firefox.Grease.Should().BeFalse();
        await AetherFingerprint.SemiPython.Grease.Should().BeFalse();
        await AetherFingerprint.Go.Grease.Should().BeFalse();
        // Chrome of today alone sends one among its signature algorithms as well; Chrome 120 did not.
        await AetherFingerprint.All.Where(it => it.GreaseSigalgs).SequenceEqual([AetherFingerprint.Chrome]).Should().BeTrue();
    }

    // From PattNG AetherKeysTest.
    [Test]
    public async Task EachFingerprintNamesItsSuitesItsGroupsAndItsSignatureAlgorithms()
    {
        // Each is named, so that the core's defaults cannot change it. What the core sends unless told otherwise (GREASE,
        // OCSP, signed certificate timestamps, the session ticket, a new order of the extensions on each handshake) a
        // fingerprint without it turns off; what the core leaves out unless told otherwise, a fingerprint with it turns
        // on. The TLS 1.3 order and the key shares are named where they are not BoringSSL's.
        const string groups = "X25519MLKEM768:X25519:P-256:P-384:P-521";
        const string chromeEight = "ecdsa_secp256r1_sha256:rsa_pss_rsae_sha256:rsa_pkcs1_sha256:ecdsa_secp384r1_sha384:"
            + "rsa_pss_rsae_sha384:rsa_pkcs1_sha384:rsa_pss_rsae_sha512:rsa_pkcs1_sha512";
        await AetherFingerprint.Chrome.Arguments
            .SequenceEqual([
                "--tls-ciphers", "ALL:!aPSK:!ECDSA+SHA1:!3DES",
                "--tls-groups", "X25519MLKEM768:X25519:P-256:P-384",
                "--tls-sigalgs", "mldsa44:mldsa65:mldsa87:" + chromeEight,
                "--tls-cert-compression", "brotli",
                "--tls-alps", "new",
                "--tls-trust-anchors", AetherFingerprint.Chrome.TrustAnchors!,
                "--tls-server-padding", "6000",
                "--enable-grease-sigalgs",
                "--enable-ech-grease",
            ])
            .Should().BeTrue();
        await AetherFingerprint.Chrome120.Arguments
            .SequenceEqual([
                "--tls-ciphers", "ALL:!aPSK:!ECDSA+SHA1:!3DES",
                "--tls-groups", "X25519:P-256:P-384",
                "--tls-sigalgs", chromeEight,
                "--tls-cert-compression", "brotli",
                "--tls-alps", "old",
                "--enable-ech-grease",
            ])
            .Should().BeTrue();
        await AetherFingerprint.Firefox.Arguments
            .SequenceEqual([
                "--tls-ciphers", AetherFingerprint.Firefox.Ciphers,
                "--tls13-ciphers", "TLS_AES_128_GCM_SHA256:TLS_CHACHA20_POLY1305_SHA256:TLS_AES_256_GCM_SHA384",
                "--tls-groups", groups,
                "--tls-key-shares", "X25519MLKEM768:X25519:P-256",
                "--tls-sigalgs", "ecdsa_secp256r1_sha256:ecdsa_secp384r1_sha384:ecdsa_secp521r1_sha512:rsa_pss_rsae_sha256:"
                    + "rsa_pss_rsae_sha384:rsa_pss_rsae_sha512:rsa_pkcs1_sha256:rsa_pkcs1_sha384:rsa_pkcs1_sha512:ecdsa_sha1:"
                    + "rsa_pkcs1_sha1",
                "--tls-cert-compression", "zlib:brotli:zstd",
                "--disable-grease",
                "--enable-ech-grease",
                "--disable-session-ticket",
                "--disable-extension-permutation",
            ])
            .Should().BeTrue();
        await AetherFingerprint.SemiPython.Arguments
            .SequenceEqual([
                "--tls-ciphers", AetherFingerprint.SemiPython.Ciphers,
                "--tls13-ciphers", "TLS_AES_256_GCM_SHA384:TLS_CHACHA20_POLY1305_SHA256:TLS_AES_128_GCM_SHA256",
                "--tls-groups", groups,
                "--tls-sigalgs", "mldsa65:mldsa87:mldsa44:ecdsa_secp256r1_sha256:ecdsa_secp384r1_sha384:"
                    + "ecdsa_secp521r1_sha512:ed25519:rsa_pss_rsae_sha256:rsa_pss_rsae_sha384:rsa_pss_rsae_sha512:"
                    + "rsa_pkcs1_sha256:rsa_pkcs1_sha384:rsa_pkcs1_sha512",
                "--disable-grease",
                "--disable-ocsp-stapling",
                "--disable-sct",
                "--disable-extension-permutation",
            ])
            .Should().BeTrue();
        await AetherFingerprint.Go.Arguments
            .SequenceEqual([
                "--tls-ciphers", AetherFingerprint.Go.Ciphers,
                "--tls-groups", groups,
                "--tls-sigalgs", "mldsa44:mldsa65:mldsa87:rsa_pss_rsae_sha256:ecdsa_secp256r1_sha256:ed25519:"
                    + "rsa_pss_rsae_sha384:rsa_pss_rsae_sha512:rsa_pkcs1_sha256:rsa_pkcs1_sha384:rsa_pkcs1_sha512:"
                    + "ecdsa_secp384r1_sha384:ecdsa_secp521r1_sha512",
                "--disable-grease",
                "--disable-session-ticket",
                "--disable-extension-permutation",
            ])
            .Should().BeTrue();
    }

    // From PattNG AetherKeysTest. Its check of the dropdown's labels (Chrome, Firefox, Semi-Python, Go) against this
    // order goes with the editor and the WARP keys window, which list them by their index in All.
    [Test]
    public async Task TheFingerprintListNamesEveryFingerprintInItsOrder()
    {
        await AetherFingerprint.All.Select(it => it.Type).SequenceEqual(["chrome", "chrome-120", "firefox", "semi-python", "go"]).Should().BeTrue();
        await AetherFingerprint.FromString("semi-python").Should().BeEqualTo(AetherFingerprint.SemiPython);
    }
}
