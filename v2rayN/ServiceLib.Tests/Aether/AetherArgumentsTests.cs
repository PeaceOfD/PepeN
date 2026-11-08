using TUnit.Assertions.Enums;

namespace ServiceLib.Tests.Aether;

// From PattNG AetherCoreManagerTest: the tests of the arguments a core is started with and of their reading. The tests
// of processes, exits, readiness and leftover cores go with the stages that run cores.
public class AetherArgumentsTests
{
    // PattNG AetherCoreManagerTest.profile(): an Aether profile with every option the tests do not change set. The
    // endpoint is Address/Port, Port 0 meaning none (PattNG: server/serverPort, null meaning none).
    private static ProfileItem Profile(
        AetherProtocol? protocol = null,
        AetherTransport? transport = null,
        string? server = null,
        int port = 0,
        string? outer = null,
        string? inner = null,
        bool? fragment = null,
        Func<ProtocolExtraItem, ProtocolExtraItem>? copy = null)
    {
        var extra = new ProtocolExtraItem
        {
            AetherProtocol = (protocol ?? AetherProtocol.Masque).Type,
            AetherTransport = (transport ?? AetherTransport.Http3).Type,
            AetherScanMode = AetherScanMode.Verified.Type,
            AetherObfuscation = AetherObfuscation.Aggressive.Type,
            AetherIpVersion = AetherIpVersion.Dual.Type,
            AetherWiwOuter = outer,
            AetherWiwInner = inner,
            AetherFragment = fragment,
        };
        var profile = new ProfileItem
        {
            ConfigType = EConfigType.Aether,
            Remarks = "test",
            Address = server ?? string.Empty,
            Port = port,
        };
        profile.SetProtocolExtra(copy == null ? extra : copy(extra));
        return profile;
    }

    // PattNG's profile.copy(...): a new profile, the one copied left as it was.
    private static ProfileItem Copy(ProfileItem profile, Func<ProtocolExtraItem, ProtocolExtraItem> change)
    {
        var copy = new ProfileItem
        {
            ConfigType = profile.ConfigType,
            Remarks = profile.Remarks,
            Address = profile.Address,
            Port = profile.Port,
        };
        copy.SetProtocolExtra(change(profile.GetProtocolExtra()));
        return copy;
    }

    // As PattNG's test helper: the value after the first flag, unlike AetherArguments.ValueAfter.
    private static string? ValueAfter(IReadOnlyList<string> arguments, string flag)
    {
        var at = arguments.ToList().IndexOf(flag);
        return at >= 0 && at + 1 < arguments.Count ? arguments[at + 1] : null;
    }

    private static List<string> ValuesAfter(IReadOnlyList<string> arguments, string flag)
    {
        var values = new List<string>();
        for (var index = 0; index + 1 < arguments.Count; index++)
        {
            if (arguments[index] == flag)
            {
                values.Add(arguments[index + 1]);
            }
        }
        return values;
    }

    // PattNG's assertEquals on lists: the same words in the same order.
    private static async Task ShouldBe(IReadOnlyList<string> actual, params string[] expected)
    {
        await actual.Should().BeEquivalentTo(expected, CollectionOrdering.Matching);
    }

    private static List<string> Build(ProfileItem profile, int port, bool scan = false, string logLevel = AetherArguments.DefaultLogLevel)
    {
        return AetherArguments.Build(profile, port, scan, logLevel);
    }

    [Test]
    public async Task BindsToTheLoopbackPortItIsGiven()
    {
        await ValueAfter(Build(Profile(), 10819), "--bind").Should().BeEqualTo("127.0.0.1:10819");
        await ValueAfter(Build(Profile(), 0, scan: true), "--bind").Should().BeEqualTo("127.0.0.1:0");
        await AetherArguments.ListenerPortOf(Build(Profile(), 10819)).Should().BeEqualTo(10819);
    }

    // PattN: the three example commands of the Aether editor (design stage 1, report 04 section 6.2), word for word in
    // PattNG's order, as the editor's Command field shows them without the log level.
    [Test]
    public async Task TheEditorsExampleCommandsAreBuiltWordForWord()
    {
        ProfileItem Of(ProtocolExtraItem extra)
        {
            var profile = new ProfileItem { ConfigType = EConfigType.Aether, Remarks = "example" };
            profile.SetProtocolExtra(extra);
            return profile;
        }
        List<string> Command(ProfileItem profile)
        {
            return AetherArguments.WithoutOption(Build(profile, 10819), "--log-level");
        }

        // A new profile: WireGuard, with every setting at its default.
        await ShouldBe(Command(Of(new ProtocolExtraItem())),
            "--bind", "127.0.0.1:10819", "--protocol", "wg", "--scan", "balanced", "--ip", "v4", "--quick-reconnect");
        await ShouldBe(Command(Of(new ProtocolExtraItem { AetherProtocol = AetherProtocol.Masque.Type })),
            [
                "--bind", "127.0.0.1:10819", "--protocol", "masque", "--scan", "balanced", "--ip", "v4",
                "--masque-sni", "www.cloudflare.com", .. AetherFingerprint.Chrome.Arguments, "--quick-reconnect",
            ]);
        // WARP-in-WARP without hops.
        await ShouldBe(Command(Of(new ProtocolExtraItem { AetherProtocol = AetherProtocol.Gool.Type })),
            "--bind", "127.0.0.1:10819", "--protocol", "gool", "--gool-classic", "--scan", "balanced", "--ip", "v4", "--wiw-scan",
            "--quick-reconnect");
    }

    [Test]
    public async Task TheChosenModesAreForwarded()
    {
        var arguments = Build(Profile(), 10819);
        await ValueAfter(arguments, "--protocol").Should().BeEqualTo("masque");
        await ValueAfter(arguments, "--scan").Should().BeEqualTo("verified");
        await ValueAfter(arguments, "--noize").Should().BeEqualTo("aggressive");
        await ValueAfter(arguments, "--ip").Should().BeEqualTo("both");
        await ValueAfter(arguments, "--log-level").Should().BeEqualTo("info");
    }

    [Test]
    public async Task EachProtocolIsNamedToTheCore()
    {
        await ValueAfter(Build(Profile(AetherProtocol.WireGuard), 10819), "--protocol").Should().BeEqualTo("wg");
        await ValueAfter(Build(Profile(AetherProtocol.Gool), 10819), "--protocol").Should().BeEqualTo("gool");
        await ValueAfter(Build(Profile(AetherProtocol.Mim), 10819), "--protocol").Should().BeEqualTo("mim");
        // Gool is WireGuard over MASQUE to the core; WARP-in-WARP is its classic gool, asked for by name.
        await ValueAfter(Build(Profile(AetherProtocol.WgOverMasque), 10819), "--protocol").Should().BeEqualTo("gool");
        await Build(Profile(AetherProtocol.Gool), 10819).Contains("--gool-classic").Should().BeTrue();
        await Build(Profile(AetherProtocol.Gool), 0, scan: true).Contains("--gool-classic").Should().BeTrue();
        foreach (var protocol in AetherProtocol.All.Where(it => it != AetherProtocol.Gool))
        {
            await Build(Profile(protocol), 10819).Contains("--gool-classic").Should().BeFalse();
        }
    }

    [Test]
    public async Task TheProtocolOfTheArgumentsOfAProfileIsTheProfiles()
    {
        foreach (var protocol in AetherProtocol.All)
        {
            foreach (var scan in new[] { false, true })
            {
                var hopless = Build(Profile(protocol), 10819, scan);
                await AetherArguments.ProtocolOf(hopless).Should().BeEqualTo(protocol);
                var hops = Build(Profile(protocol, outer: "162.159.192.1:443", inner: "188.114.96.1:2408"), 10819, scan);
                await AetherArguments.ProtocolOf(hops).Should().BeEqualTo(protocol);
            }
        }
    }

    [Test]
    public async Task Http2AndFragmentationOnlyApplyToTunnelsOverMasque()
    {
        await Build(Profile(fragment: true), 10819).Contains("--h2").Should().BeFalse();
        await Build(Profile(fragment: true), 10819).Contains("--fragment").Should().BeFalse();

        var http2 = Build(Profile(transport: AetherTransport.Http2), 10819);
        await http2.Contains("--h2").Should().BeTrue();
        // Off is no flag at all: the core fragments nothing unless told to, and has no flag for off.
        await http2.Contains("--fragment").Should().BeFalse();
        await http2.Contains("--no-fragment").Should().BeFalse();

        var fragmented = Build(Profile(transport: AetherTransport.Http2, fragment: true), 10819);
        await fragmented.Contains("--fragment").Should().BeTrue();

        var wireguard = Build(Profile(AetherProtocol.WireGuard, AetherTransport.Http2, fragment: true), 10819);
        await wireguard.Contains("--h2").Should().BeFalse();
        await wireguard.Contains("--fragment").Should().BeFalse();

        // WireGuard over MASQUE rides the MASQUE carrier chosen here, as the WireGuard inside it does.
        var goolOverMasque = Build(Profile(AetherProtocol.WgOverMasque, AetherTransport.Http2, fragment: true), 10819);
        await goolOverMasque.Contains("--h2").Should().BeTrue();
        await goolOverMasque.Contains("--fragment").Should().BeTrue();
        await Build(Profile(AetherProtocol.WgOverMasque, AetherTransport.Http2), 10819).Contains("--fragment").Should().BeFalse();

        // Both masque-in-masque hops ride on the carrier chosen here.
        var mim = Build(Profile(AetherProtocol.Mim, AetherTransport.Http2, fragment: true), 10819);
        await mim.Contains("--h2").Should().BeTrue();
        await mim.Contains("--fragment").Should().BeTrue();
        await Build(Profile(AetherProtocol.Mim, fragment: true), 10819).Contains("--h2").Should().BeFalse();
    }

    [Test]
    public async Task FragmentValuesReachTheCoreOnlyWhenFragmentingIsOn()
    {
        var tuned = Profile(
            transport: AetherTransport.Http2,
            fragment: true,
            copy: it => it with { AetherFragmentSize = "32-16", AetherFragmentDelay = "5" });
        var arguments = Build(tuned, 10819);
        await ValueAfter(arguments, "--fragment-size").Should().BeEqualTo("16-32");
        await ValueAfter(arguments, "--fragment-delay").Should().BeEqualTo("5");

        var off = Build(Copy(tuned, it => it with { AetherFragment = false }), 10819);
        await off.Contains("--fragment").Should().BeFalse();
        await off.Contains("--fragment-size").Should().BeFalse();
        await off.Contains("--fragment-delay").Should().BeFalse();

        var invalid = Build(Copy(tuned, it => it with { AetherFragmentSize = "0", AetherFragmentDelay = "x" }), 10819);
        await invalid.Contains("--fragment").Should().BeTrue();
        await invalid.Contains("--fragment-size").Should().BeFalse();
        await invalid.Contains("--fragment-delay").Should().BeFalse();
    }

    [Test]
    public async Task APinnedEndpointIsForwardedInTheCoreFormat()
    {
        await ValueAfter(Build(Profile(server: "162.159.198.1", port: 443), 10819), "--peer").Should().BeEqualTo("162.159.198.1:443");
        await ValueAfter(Build(Profile(AetherProtocol.WireGuard, server: "2606:4700:d0::a29f:c001", port: 2408), 10819), "--peer")
            .Should().BeEqualTo("[2606:4700:d0::a29f:c001]:2408");
    }

    [Test]
    public async Task AnEndpointTheCoreCannotReadIsLeftToTheScan()
    {
        await ValueAfter(Build(Profile(), 10819), "--peer").Should().BeNull();
        await ValueAfter(Build(Profile(server: "162.159.198.1"), 10819), "--peer").Should().BeNull();
        await ValueAfter(Build(Profile(port: 443), 10819), "--peer").Should().BeNull();
        await ValueAfter(Build(Profile(server: "162.159.198.1", port: 0), 10819), "--peer").Should().BeNull();
        await ValueAfter(Build(Profile(server: "engage.cloudflareclient.com", port: 2408), 10819), "--peer").Should().BeNull();
    }

    [Test]
    public async Task BothGoolHopsReachTheCoreWhenNamedByHand()
    {
        var arguments = Build(Profile(AetherProtocol.Gool, outer: "162.159.192.1:2408", inner: "188.114.96.1:894"), 10819);
        await ValueAfter(arguments, "--wiw-outer").Should().BeEqualTo("162.159.192.1:2408");
        await ValueAfter(arguments, "--wiw-inner").Should().BeEqualTo("188.114.96.1:894");
        await arguments.Contains("--wiw-scan").Should().BeFalse();
        await arguments.Contains("--peer").Should().BeFalse();
        await arguments.Contains("--gool-classic").Should().BeTrue();
        await arguments.Contains("--gool-peer").Should().BeFalse();
    }

    [Test]
    public async Task WireGuardOverMasqueDialsItsGatewayAndItsWireGuardEndpointByTheirOwnFlags()
    {
        var named = Build(Profile(AetherProtocol.WgOverMasque, outer: "162.159.198.1:443", inner: "162.159.192.1:2408"), 10819);
        await ValueAfter(named, "--peer").Should().BeEqualTo("162.159.198.1:443");
        await ValueAfter(named, "--gool-peer").Should().BeEqualTo("162.159.192.1:2408");
        // No classic gool word: any of them would turn the core to WireGuard in WireGuard.
        foreach (var flag in new[] { "--gool-classic", "--wiw-outer", "--wiw-inner", "--wiw-scan", "--mim-outer", "--mim-inner", "--mim-scan" })
        {
            await named.Contains(flag).Should().BeFalse();
        }
        // The same address on both hops is no problem for the core here.
        var shared = Build(Profile(AetherProtocol.WgOverMasque, outer: "162.159.192.1:443", inner: "162.159.192.1:2408"), 10819);
        await ValueAfter(shared, "--peer").Should().BeEqualTo("162.159.192.1:443");
        await ValueAfter(shared, "--gool-peer").Should().BeEqualTo("162.159.192.1:2408");

        // Left blank, the gateway is scanned for and the WireGuard endpoint is the one WARP assigns.
        var blank = Build(Profile(AetherProtocol.WgOverMasque, server: "162.159.198.1", port: 443), 10819);
        await blank.Contains("--peer").Should().BeFalse();
        await blank.Contains("--gool-peer").Should().BeFalse();
        await blank.Contains("--wiw-scan").Should().BeFalse();
        var malformed = Build(Profile(AetherProtocol.WgOverMasque, outer: "162.159.198.1", inner: "x:2408"), 10819);
        await malformed.Contains("--peer").Should().BeFalse();
        await malformed.Contains("--gool-peer").Should().BeFalse();

        // A scan looks for the gateway afresh and keeps the WireGuard endpoint the session dials.
        var scan = Build(Profile(AetherProtocol.WgOverMasque, outer: "162.159.198.1:443", inner: "162.159.192.1:2408"), 0, scan: true);
        await scan.Contains("--peer").Should().BeFalse();
        await ValueAfter(scan, "--gool-peer").Should().BeEqualTo("162.159.192.1:2408");
        await scan.Contains("--no-quick-reconnect").Should().BeTrue();
    }

    [Test]
    public async Task NamingOneGoolHopLeavesTheOtherToTheScan()
    {
        var arguments = Build(Profile(AetherProtocol.Gool, inner: "188.114.96.1:894"), 10819);
        await ValueAfter(arguments, "--wiw-outer").Should().BeNull();
        await ValueAfter(arguments, "--wiw-inner").Should().BeEqualTo("188.114.96.1:894");
        await arguments.Contains("--wiw-scan").Should().BeFalse();
    }

    [Test]
    public async Task GoolScansForHopsItCannotUse()
    {
        await Build(Profile(AetherProtocol.Gool), 10819).Contains("--wiw-scan").Should().BeTrue();
        await Build(Profile(AetherProtocol.Gool), 10819).Contains("--gool-classic").Should().BeTrue();

        var malformed = Build(Profile(AetherProtocol.Gool, outer: "162.159.192.1"), 10819);
        await ValueAfter(malformed, "--wiw-outer").Should().BeNull();
        await malformed.Contains("--wiw-scan").Should().BeTrue();

        var pinnedEndpoint = Build(Profile(AetherProtocol.Gool, server: "162.159.198.1", port: 443), 10819);
        await pinnedEndpoint.Contains("--peer").Should().BeFalse();
        await pinnedEndpoint.Contains("--wiw-scan").Should().BeTrue();
    }

    [Test]
    public async Task ARunReusesTheLastGatewayButAScanLooksAfresh()
    {
        var run = Build(Profile(server: "162.159.198.1", port: 443), 10819);
        await run.Contains("--quick-reconnect").Should().BeTrue();
        await run.Contains("--no-quick-reconnect").Should().BeFalse();

        var scan = Build(Profile(server: "162.159.198.1", port: 443), 0, scan: true);
        await scan.Contains("--no-quick-reconnect").Should().BeTrue();
        await scan.Contains("--quick-reconnect").Should().BeFalse();
        await scan.Contains("--peer").Should().BeFalse();
    }

    [Test]
    public async Task MimHopsReachTheCoreUnderTheirOwnFlags()
    {
        var both = Build(Profile(AetherProtocol.Mim, outer: "162.159.192.1:443", inner: "188.114.96.1:443"), 10819);
        await ValueAfter(both, "--mim-outer").Should().BeEqualTo("162.159.192.1:443");
        await ValueAfter(both, "--mim-inner").Should().BeEqualTo("188.114.96.1:443");
        await both.Contains("--mim-scan").Should().BeFalse();
        await both.Contains("--peer").Should().BeFalse();
        await both.Contains("--wiw-outer").Should().BeFalse();

        var innerOnly = Build(Profile(AetherProtocol.Mim, inner: "188.114.96.1:443"), 10819);
        await ValueAfter(innerOnly, "--mim-outer").Should().BeNull();
        await ValueAfter(innerOnly, "--mim-inner").Should().BeEqualTo("188.114.96.1:443");
        await innerOnly.Contains("--mim-scan").Should().BeFalse();
    }

    [Test]
    public async Task MimScansForHopsItCannotUse()
    {
        await Build(Profile(AetherProtocol.Mim), 10819).Contains("--mim-scan").Should().BeTrue();

        var malformed = Build(Profile(AetherProtocol.Mim, outer: "162.159.192.1"), 10819);
        await ValueAfter(malformed, "--mim-outer").Should().BeNull();
        await malformed.Contains("--mim-scan").Should().BeTrue();

        var pinnedEndpoint = Build(Profile(AetherProtocol.Mim, server: "162.159.198.1", port: 443), 10819);
        await pinnedEndpoint.Contains("--peer").Should().BeFalse();
        await pinnedEndpoint.Contains("--mim-scan").Should().BeTrue();

        var scan = Build(Profile(AetherProtocol.Mim, outer: "162.159.192.1:443", inner: "188.114.96.1:443"), 0, scan: true);
        await scan.Contains("--mim-outer").Should().BeFalse();
        await scan.Contains("--mim-inner").Should().BeFalse();
        await scan.Contains("--mim-scan").Should().BeTrue();
    }

    [Test]
    public async Task AGoolScanIgnoresTheHopsItWasGiven()
    {
        var scan = Build(Profile(AetherProtocol.Gool, outer: "162.159.192.1:2408", inner: "188.114.96.1:894"), 0, scan: true);
        await scan.Contains("--wiw-outer").Should().BeFalse();
        await scan.Contains("--wiw-inner").Should().BeFalse();
        await scan.Contains("--wiw-scan").Should().BeTrue();
        await scan.Contains("--gool-classic").Should().BeTrue();
    }

    [Test]
    public async Task TheSessionLogLevelFollowsTheAppSetting()
    {
        await AetherArguments.CoreLogLevel("debug").Should().BeEqualTo("debug");
        await AetherArguments.CoreLogLevel("info").Should().BeEqualTo("info");
        await AetherArguments.CoreLogLevel("warning").Should().BeEqualTo("warn");
        await AetherArguments.CoreLogLevel("Warn").Should().BeEqualTo("warn");
        await AetherArguments.CoreLogLevel("error").Should().BeEqualTo("error");
        await AetherArguments.CoreLogLevel("none").Should().BeEqualTo("error");
        await AetherArguments.CoreLogLevel(null).Should().BeEqualTo("info");
        await AetherArguments.CoreLogLevel("verbose").Should().BeEqualTo("info");

        await ValueAfter(Build(Profile(), 10819, logLevel: "warn"), "--log-level").Should().BeEqualTo("warn");
        await ValueAfter(Build(Profile(), 0, scan: true), "--log-level").Should().BeEqualTo("info");
    }

    // PattNG's test also checks isSession, which tells the session's core apart in /proc; PattN knows its session in
    // memory (design 2.6.6). Only the port of the listener is here.
    [Test]
    public async Task TheSessionOfACustomConfigurationIsRecognisedOnWhateverPortItListens()
    {
        string[] session = ["/data/app/lib/libaether.so", "--bind", "127.0.0.1:20808", "--protocol", "wg"];
        await AetherArguments.BindPortOf(session).Should().BeEqualTo(20808);
        await AetherArguments.BindPortOf(["/data/app/lib/libaether.so", "--bind"]).Should().BeNull();
        await AetherArguments.BindPortOf(["/data/app/lib/libaether.so", "--bind", "20808"]).Should().BeNull();
    }

    [Test]
    public async Task TheSessionProtocolIsReadFromItsArguments()
    {
        string[] gool = ["/data/app/lib/libaether.so", "--bind", "127.0.0.1:10819", "--protocol", "gool", "--scan", "balanced"];
        await AetherArguments.ProtocolOf(gool).Should().BeEqualTo(AetherProtocol.WgOverMasque);
        await AetherArguments.ProtocolOf([.. gool, "--gool-classic"]).Should().BeEqualTo(AetherProtocol.Gool);
        await AetherArguments.ProtocolOf(["/data/app/lib/libaether.so", "--protocol", "wg"]).Should().BeEqualTo(AetherProtocol.WireGuard);
        await AetherArguments.ProtocolOf(["/data/app/lib/libaether.so", "--protocol"]).Should().BeEqualTo(AetherProtocol.Masque);
        await AetherArguments.ProtocolOf([]).Should().BeEqualTo(AetherProtocol.Masque);
    }

    [Test]
    public async Task TheProtocolOfAHandWrittenCommandIsReadTheWayTheCoreReadsIt()
    {
        const string bin = "/data/app/lib/libaether.so";
        await AetherArguments.ProtocolOf([bin, "--wg"]).Should().BeEqualTo(AetherProtocol.WireGuard);
        await AetherArguments.ProtocolOf([bin, "--warp"]).Should().BeEqualTo(AetherProtocol.WireGuard);
        // Gool is WireGuard over MASQUE, as aether 2.3.0 reads it.
        await AetherArguments.ProtocolOf([bin, "--wiw"]).Should().BeEqualTo(AetherProtocol.WgOverMasque);
        await AetherArguments.ProtocolOf([bin, "--gool"]).Should().BeEqualTo(AetherProtocol.WgOverMasque);
        await AetherArguments.ProtocolOf([bin, "--mim"]).Should().BeEqualTo(AetherProtocol.Mim);
        await AetherArguments.ProtocolOf([bin, "--masque-in-masque"]).Should().BeEqualTo(AetherProtocol.Mim);
        await AetherArguments.ProtocolOf([bin, "--protocol", "mim"]).Should().BeEqualTo(AetherProtocol.Mim);
        // After --protocol the core takes other names as well, in any case; an unknown one is masque.
        await AetherArguments.ProtocolOf([bin, "--protocol", "M2"]).Should().BeEqualTo(AetherProtocol.Mim);
        await AetherArguments.ProtocolOf([bin, "--protocol", "warp-in-warp"]).Should().BeEqualTo(AetherProtocol.WgOverMasque);
        // The app's own word for it is no word of the core's, which runs MASQUE on it.
        await AetherArguments.ProtocolOf([bin, "--protocol", "wg-over-masque"]).Should().BeEqualTo(AetherProtocol.Masque);
        await AetherArguments.ProtocolOf([bin, "--protocol", "WireGuard"]).Should().BeEqualTo(AetherProtocol.WireGuard);
        await AetherArguments.ProtocolOf([bin, "--wg", "--protocol", "something-else"]).Should().BeEqualTo(AetherProtocol.Masque);
        // The last word wins, as it does for the core.
        await AetherArguments.ProtocolOf([bin, "--wg", "--protocol", "gool"]).Should().BeEqualTo(AetherProtocol.WgOverMasque);
        await AetherArguments.ProtocolOf([bin, "--protocol", "gool", "--wg"]).Should().BeEqualTo(AetherProtocol.WireGuard);
        await AetherArguments.ProtocolOf([bin, "--gool-classic", "--masque"]).Should().BeEqualTo(AetherProtocol.Masque);
        await AetherArguments.ProtocolOf([bin, "--wg", "--gool-classic"]).Should().BeEqualTo(AetherProtocol.Gool);
        // --gool-peer selects gool as well, WireGuard over MASQUE unless something makes it the classic one.
        await AetherArguments.ProtocolOf([bin, "--gool-peer", "162.159.192.1:2408"]).Should().BeEqualTo(AetherProtocol.WgOverMasque);
        // --gool-classic, a warp-in-warp hop or a scan of the hops makes it the classic gool, wherever they stand.
        await AetherArguments.ProtocolOf([bin, "--gool-classic"]).Should().BeEqualTo(AetherProtocol.Gool);
        await AetherArguments.ProtocolOf([bin, "--gool-classic", "--protocol", "gool"]).Should().BeEqualTo(AetherProtocol.Gool);
        await AetherArguments.ProtocolOf([bin, "--gool", "--wiw-scan"]).Should().BeEqualTo(AetherProtocol.Gool);
        await AetherArguments.ProtocolOf([bin, "--gool-scan", "--protocol", "gool"]).Should().BeEqualTo(AetherProtocol.Gool);
        await AetherArguments.ProtocolOf([bin, "--protocol", "gool", "--gool-peers", "auto"]).Should().BeEqualTo(AetherProtocol.Gool);
        await AetherArguments.ProtocolOf([bin, "--gool", "--wiw-inner", "188.114.96.1:894"]).Should().BeEqualTo(AetherProtocol.Gool);
        await AetherArguments.ProtocolOf([bin, "--gool-peer", "162.159.192.1:2408", "--wiw-scan"]).Should().BeEqualTo(AetherProtocol.Gool);
        // A blank value sets nothing for the core.
        await AetherArguments.ProtocolOf([bin, "--gool", "--wiw-outer", " "]).Should().BeEqualTo(AetherProtocol.WgOverMasque);
        await AetherArguments.ProtocolOf([bin, "--wiw-peers", ""]).Should().BeEqualTo(AetherProtocol.Masque);
        // A warp-in-warp hop named without a protocol selects gool; asking for a scan of the hops does not.
        await AetherArguments.ProtocolOf([bin, "--wiw-outer", "162.159.192.1:2408"]).Should().BeEqualTo(AetherProtocol.Gool);
        await AetherArguments.ProtocolOf([bin, "--wiw-peers", "162.159.192.1:2408,188.114.96.1:2408"]).Should().BeEqualTo(AetherProtocol.Gool);
        await AetherArguments.ProtocolOf([bin, "--wiw-peers", "auto"]).Should().BeEqualTo(AetherProtocol.Masque);
        await AetherArguments.ProtocolOf([bin, "--wiw-scan"]).Should().BeEqualTo(AetherProtocol.Masque);
        await AetherArguments.ProtocolOf([bin, "--wg", "--wiw-outer", "162.159.192.1:2408"]).Should().BeEqualTo(AetherProtocol.WireGuard);
        // A masque-in-masque hop selects mim the same way, and a warp-in-warp hop comes first when both are named.
        await AetherArguments.ProtocolOf([bin, "--mim-outer", "162.159.192.1:443"]).Should().BeEqualTo(AetherProtocol.Mim);
        await AetherArguments.ProtocolOf([bin, "--mim-peers", "162.159.192.1:443,188.114.96.1:443"]).Should().BeEqualTo(AetherProtocol.Mim);
        await AetherArguments.ProtocolOf([bin, "--mim-peers", "auto"]).Should().BeEqualTo(AetherProtocol.Masque);
        await AetherArguments.ProtocolOf([bin, "--mim-scan"]).Should().BeEqualTo(AetherProtocol.Masque);
        await AetherArguments.ProtocolOf([bin, "--mim-outer", "162.159.192.1:443", "--wiw-inner", "188.114.96.1:894"]).Should().BeEqualTo(AetherProtocol.Gool);
        await AetherArguments.ProtocolOf([bin, "--masque", "--mim-outer", "162.159.192.1:443"]).Should().BeEqualTo(AetherProtocol.Masque);
        // A blank masque-in-masque hop sets nothing either, and the core runs MASQUE.
        await AetherArguments.ProtocolOf([bin, "--mim-outer", ""]).Should().BeEqualTo(AetherProtocol.Masque);
        await AetherArguments.ProtocolOf([bin, "--mim-peers", " "]).Should().BeEqualTo(AetherProtocol.Masque);
        await AetherArguments.ProtocolOf([bin, "--mim-peers", " auto "]).Should().BeEqualTo(AetherProtocol.Masque);
        // Each hop setting is the last value given for it, as the variable the core reads: a blank one clears it.
        await AetherArguments.ProtocolOf([bin, "--gool", "--wiw-outer", "162.159.192.1:2408", "--wiw-outer", ""]).Should().BeEqualTo(AetherProtocol.WgOverMasque);
        await AetherArguments.ProtocolOf([bin, "--gool", "--wiw-outer", "", "--gool-outer", "162.159.192.1:2408"]).Should().BeEqualTo(AetherProtocol.Gool);
        await AetherArguments.ProtocolOf([bin, "--mim-peers", "162.159.192.1:443,188.114.96.1:443", "--mim-scan"]).Should().BeEqualTo(AetherProtocol.Masque);
    }

    // PattNG's test reads the words from /proc (nulSeparated), which PattN never reads (design 2.6.6); only the
    // reading of those words, an empty argument among them, is here.
    [Test]
    public async Task TheWordsOfACommandLineFromProcKeepItsEmptyArguments()
    {
        // Read as the core runs: WireGuard over MASQUE, the blank hop setting nothing.
        await AetherArguments.ProtocolOf(["aether", "--gool", "--wiw-outer", "", "--bind", "127.0.0.1:10819"]).Should().BeEqualTo(AetherProtocol.WgOverMasque);
    }

    [Test]
    public async Task TheSessionTakesTheAppLogLevelUnlessItsCommandNamesOne()
    {
        await ShouldBe(AetherArguments.WithLogLevel(["--wg"], "warn"), "--wg", "--log-level", "warn");
        await ShouldBe(AetherArguments.WithLogLevel(["--wg", "--log-level", "debug"], "warn"), "--wg", "--log-level", "debug");
        await ShouldBe(AetherArguments.WithLogLevel(["--wg", "--verbose"], "warn"), "--wg", "--verbose");
    }

    [Test]
    public async Task TheListenerIsReplacedWhereverItStands()
    {
        await ShouldBe(AetherArguments.WithListener(["--bind", "127.0.0.1:10819", "--wg"], "--bind", 41234), "--wg", "--bind", "127.0.0.1:41234");
        await ShouldBe(AetherArguments.WithListener(["--wg"], "--bind", 41234), "--wg", "--bind", "127.0.0.1:41234");
        await ShouldBe(
            AetherArguments.WithListener(["--wg", "--bind", "127.0.0.1:1", "--bind", "127.0.0.1:2"], "--bind", 41234),
            "--wg", "--bind", "127.0.0.1:41234");
        await AetherArguments.BindPortOf(AetherArguments.WithListener(["--bind", "127.0.0.1:10819", "--wg"], "--bind", 41234)).Should().BeEqualTo(41234);
    }

    [Test]
    public async Task PsiphonInsideTheTunnelIsWhatTheAppDialsAndTheTunnelTakesThePortAfterIt()
    {
        var chain = Build(
            Profile(copy: it => it with { AetherPsiphon = "chain", AetherPsiphonMode = "cdn", AetherPsiphonCdnIps = "1.1.1.1,1.0.0.1", AetherPsiphonRegion = "DE" }),
            10819);
        await ValueAfter(chain, "--bind").Should().BeEqualTo("127.0.0.1:10820");
        await chain.Contains("--psiphon").Should().BeTrue();
        await ValueAfter(chain, "--psiphon-bind").Should().BeEqualTo("127.0.0.1:10819");
        await ValueAfter(chain, "--psiphon-mode").Should().BeEqualTo("cdn");
        await ValueAfter(chain, "--psiphon-cdn-ips").Should().BeEqualTo("1.1.1.1,1.0.0.1");
        await ValueAfter(chain, "--psiphon-region").Should().BeEqualTo("DE");
        await AetherArguments.ListenerPortOf(chain).Should().BeEqualTo(10819);
        await AetherArguments.ListenerAddressOf(chain).Should().BeEqualTo("127.0.0.1:10819");
        // WARP is still set up underneath.
        await ValueAfter(chain, "--protocol").Should().BeEqualTo("masque");
        await chain.Contains("--quick-reconnect").Should().BeTrue();
    }

    [Test]
    public async Task TheCdnListsReachTheCoreOnlyWhereAFrontedTransportCanReadThem()
    {
        var fronted = Profile(copy: it => it with { AetherPsiphon = "only", AetherPsiphonCdnIps = "1.1.1.1", AetherPsiphonCdnSni = "a.example" });
        await ValueAfter(Build(fronted, 10819), "--psiphon-cdn-ips").Should().BeEqualTo("1.1.1.1");
        await ValueAfter(Build(fronted, 10819), "--psiphon-cdn-sni").Should().BeEqualTo("a.example");
        await ValueAfter(Build(Copy(fronted, it => it with { AetherPsiphonMode = "cdn" }), 10819), "--psiphon-cdn-ips").Should().BeEqualTo("1.1.1.1");

        // The direct shape never fronts, so the lists would only be carried for nothing.
        var direct = Build(Copy(fronted, it => it with { AetherPsiphonMode = "direct" }), 10819);
        await ValueAfter(direct, "--psiphon-mode").Should().BeEqualTo("direct");
        await ValueAfter(direct, "--psiphon-cdn-ips").Should().BeNull();
        await ValueAfter(direct, "--psiphon-cdn-sni").Should().BeNull();

        // Without an IP list of one's own the built-in list comes whole, names included.
        var namesAlone = Build(Copy(fronted, it => it with { AetherPsiphonCdnIps = null }), 10819);
        await ValueAfter(namesAlone, "--psiphon-cdn-ips").Should().BeNull();
        await ValueAfter(namesAlone, "--psiphon-cdn-sni").Should().BeNull();
    }

    [Test]
    public async Task PsiphonAroundTheTunnelKeepsTheTunnelOnTheListenPortAndLetsPsiphonPickItsOwn()
    {
        var reverse = Build(Profile(copy: it => it with { AetherPsiphon = "reverse" }), 10819);
        await ValueAfter(reverse, "--bind").Should().BeEqualTo("127.0.0.1:10819");
        await reverse.Contains("--psiphon-reverse").Should().BeTrue();
        await ValueAfter(reverse, "--psiphon-bind").Should().BeEqualTo("127.0.0.1:0");
        await ValueAfter(reverse, "--psiphon-mode").Should().BeEqualTo("auto");
        await AetherArguments.ListenerPortOf(reverse).Should().BeEqualTo(10819);
    }

    [Test]
    public async Task PsiphonAloneLeavesWarpOut()
    {
        var only = Build(Profile(server: "162.159.198.1", port: 443, copy: it => it with { AetherPsiphon = "only" }), 10819);
        await ValueAfter(only, "--bind").Should().BeEqualTo("127.0.0.1:10819");
        await only.Contains("--psiphon-only").Should().BeTrue();
        await only.Contains("--protocol").Should().BeFalse();
        await only.Contains("--peer").Should().BeFalse();
        await only.Contains("--quick-reconnect").Should().BeFalse();
        await ValueAfter(only, "--log-level").Should().BeEqualTo("info");
        // PattN: the port table of design 2.5.2: Psiphon alone listens on P, under --bind, and on nothing else.
        await ValueAfter(only, "--psiphon-bind").Should().BeNull();
        await AetherArguments.ListenerPortOf(only).Should().BeEqualTo(10819);
    }

    [Test]
    public async Task TorInsideTheTunnelIsWhatTheAppDialsAndTheTunnelTakesThePortAfterIt()
    {
        var chain = Build(Profile(copy: it => it with { AetherTor = "chain" }), 10819);
        await chain.Contains("--tor").Should().BeTrue();
        await ValueAfter(chain, "--tor-bind").Should().BeEqualTo("127.0.0.1:10819");
        await ValueAfter(chain, "--bind").Should().BeEqualTo("127.0.0.1:10820");
        await AetherArguments.ListenerPortOf(chain).Should().BeEqualTo(10819);
        await AetherArguments.ListenerAddressOf(chain).Should().BeEqualTo("127.0.0.1:10819");
        // WARP is still set up underneath, and without a word about bridges Tor tries plainly first.
        await ValueAfter(chain, "--protocol").Should().BeEqualTo("masque");
        await chain.Contains("--quick-reconnect").Should().BeTrue();
        await chain.Contains("--tor-bridges").Should().BeFalse();
        await chain.Contains("--no-tor-bridges").Should().BeFalse();
        await chain.Contains("--tor-bridge").Should().BeFalse();
    }

    [Test]
    public async Task TorAroundTheTunnelKeepsTheTunnelOnTheListenPortAndGivesTorThePortAfterIt()
    {
        var reverse = Build(Profile(copy: it => it with { AetherTor = "reverse" }), 10819);
        await reverse.Contains("--tor-reverse").Should().BeTrue();
        await ValueAfter(reverse, "--bind").Should().BeEqualTo("127.0.0.1:10819");
        // Unlike Psiphon's, Tor's listener gets a real port, which cores before 2.3.5 needed, see AetherArguments.Build.
        await ValueAfter(reverse, "--tor-bind").Should().BeEqualTo("127.0.0.1:10820");
        await AetherArguments.ListenerPortOf(reverse).Should().BeEqualTo(10819);
    }

    [Test]
    public async Task TorAloneLeavesWarpOut()
    {
        var only = Build(Profile(server: "162.159.198.1", port: 443, copy: it => it with { AetherTor = "only" }), 10819);
        await only.Contains("--tor-only").Should().BeTrue();
        await ValueAfter(only, "--bind").Should().BeEqualTo("127.0.0.1:10819");
        await ValueAfter(only, "--tor-bind").Should().BeNull();
        await ValueAfter(only, "--protocol").Should().BeNull();
        await ValueAfter(only, "--peer").Should().BeNull();
        await only.Contains("--quick-reconnect").Should().BeFalse();
        await AetherArguments.ListenerPortOf(only).Should().BeEqualTo(10819);
    }

    [Test]
    public async Task TheBridgeSettingReachesTheCoreAsItsFlags()
    {
        await Build(Profile(copy: it => it with { AetherTor = "chain", AetherTorBridges = "first" }), 10819).Contains("--tor-bridges").Should().BeTrue();
        await Build(Profile(copy: it => it with { AetherTor = "only", AetherTorBridges = "never" }), 10819).Contains("--no-tor-bridges").Should().BeTrue();

        const string lines = "obfs4 192.0.2.55:38114 316E64 cert=abc iat-mode=0\n\n# mine\nBridge webtunnel [2001:db8::1]:443 7DD627 url=https://example.com/x ver=0.0.1\n";
        var own = Build(Profile(copy: it => it with { AetherTor = "only", AetherTorBridges = "own", AetherTorBridgeLines = lines }), 10819);
        await ShouldBe(
            ValuesAfter(own, "--tor-bridge"),
            "obfs4 192.0.2.55:38114 316E64 cert=abc iat-mode=0",
            "webtunnel [2001:db8::1]:443 7DD627 url=https://example.com/x ver=0.0.1");
        await own.Contains("--tor-bridges").Should().BeFalse();
        // With Tor off, the bridge setting says nothing.
        await Build(Profile(copy: it => it with { AetherTorBridges = "first" }), 10819).Contains("--tor-bridges").Should().BeFalse();
    }

    // PattN: the settings and the bridge lines are trimmed by Kotlin's whitespace, as PattNG trims them before the core
    // reads them: the separators U+001C to U+001F go, which the core would keep, and U+0085 stays, which the core trims
    // itself.
    [Test]
    public async Task SettingsAndBridgeLinesAreTrimmedAsPattNGTrimsThem()
    {
        await AetherArguments.SettingValue("\u001Fde\u001F").Should().BeEqualTo("de");
        await AetherArguments.SettingValue("\u001F--upstream").Should().BeNull();
        await AetherArguments.SettingValue(" \u001C\u001D ").Should().BeNull();
        await AetherArguments.SettingValue("\u0085x").Should().BeEqualTo("\u0085x");

        await ShouldBe(AetherFmt.BridgeLines("\u001F# a comment\nobfs4 192.0.2.1:443 FP cert=x iat-mode=0\u001F"), "obfs4 192.0.2.1:443 FP cert=x iat-mode=0");
        await ShouldBe(AetherFmt.BridgeLines("\u001FBridge obfs4 192.0.2.1:443 FP\u0085"), "obfs4 192.0.2.1:443 FP\u0085");
        var own = Build(Profile(copy: it => it with
        {
            AetherTor = "only",
            AetherTorBridges = "own",
            AetherTorBridgeLines = "\u001F# a comment\n\u001Eobfs4 192.0.2.1:443 FP cert=x iat-mode=0",
        }), 10819);
        await ShouldBe(ValuesAfter(own, "--tor-bridge"), "obfs4 192.0.2.1:443 FP cert=x iat-mode=0");

        var cdn = Build(Profile(copy: it => it with
        {
            AetherPsiphon = AetherPsiphon.Chain.Type,
            AetherPsiphonMode = "cdn",
            AetherPsiphonCdnIps = "\u001F1.2.3.4",
        }), 10819);
        await ValueAfter(cdn, "--psiphon-cdn-ips").Should().BeEqualTo("1.2.3.4");
    }

    [Test]
    public async Task TorAndPsiphonNestWithTheInnerOneOnTheListenPort()
    {
        // Psiphon inside, Tor around: the app dials Psiphon, the tunnel takes the next port, Tor the one after.
        var psiphonInside = Build(Profile(copy: it => it with { AetherPsiphon = "chain", AetherTor = "reverse" }), 10819);
        await ValueAfter(psiphonInside, "--psiphon-bind").Should().BeEqualTo("127.0.0.1:10819");
        await ValueAfter(psiphonInside, "--bind").Should().BeEqualTo("127.0.0.1:10820");
        await ValueAfter(psiphonInside, "--tor-bind").Should().BeEqualTo("127.0.0.1:10821");
        await AetherArguments.ListenerPortOf(psiphonInside).Should().BeEqualTo(10819);
        await psiphonInside.Contains("--psiphon").Should().BeTrue();
        await psiphonInside.Contains("--tor-reverse").Should().BeTrue();

        // Tor inside, Psiphon around: the app dials Tor, and Psiphon picks its own port as the core reads it.
        var torInside = Build(Profile(copy: it => it with { AetherPsiphon = "reverse", AetherTor = "chain" }), 10819);
        await ValueAfter(torInside, "--tor-bind").Should().BeEqualTo("127.0.0.1:10819");
        await ValueAfter(torInside, "--bind").Should().BeEqualTo("127.0.0.1:10820");
        await ValueAfter(torInside, "--psiphon-bind").Should().BeEqualTo("127.0.0.1:0");
        await AetherArguments.ListenerPortOf(torInside).Should().BeEqualTo(10819);
    }

    [Test]
    public async Task AScanLooksForWarpEndpointsWithoutTor()
    {
        var scan = Build(Profile(copy: it => it with { AetherTor = "only", AetherTorBridges = "first" }), 0, scan: true);
        await scan.Contains("--tor-only").Should().BeFalse();
        await scan.Contains("--tor-bridges").Should().BeFalse();
        await ValueAfter(scan, "--tor-bind").Should().BeNull();
        await ValueAfter(scan, "--protocol").Should().BeEqualTo("masque");
    }

    [Test]
    public async Task TheTunnelIsToldApartWithoutItsListenersOrLogLevel()
    {
        await ShouldBe(
            AetherArguments.TunnelArguments(["--tor", "--tor-bind", "127.0.0.1:1820", "--wg", "--bind", "127.0.0.1:1819", "--log-level", "info"]),
            "--tor", "--wg");
    }

    [Test]
    public async Task MasqueOverHttp2TakesNoObfuscation()
    {
        await ValueAfter(Build(Profile(transport: AetherTransport.Http2), 10819), "--noize").Should().BeNull();
        await ValueAfter(Build(Profile(AetherProtocol.Mim, AetherTransport.Http2), 10819), "--noize").Should().BeNull();
        await ValueAfter(Build(Profile(transport: AetherTransport.Http2), 0, scan: true), "--noize").Should().BeNull();
        // Tor or Psiphon around the tunnel carry TCP alone, so the core takes HTTP/2 whatever the transport says.
        await ValueAfter(Build(Profile(copy: it => it with { AetherTor = "reverse" }), 10819), "--noize").Should().BeNull();
        await ValueAfter(Build(Profile(copy: it => it with { AetherPsiphon = "reverse" }), 10819), "--noize").Should().BeNull();
        // Over HTTP/3, with a carrier inside the tunnel, and on WireGuard whatever the transport says, it stays.
        await ValueAfter(Build(Profile(), 10819), "--noize").Should().BeEqualTo("aggressive");
        await ValueAfter(Build(Profile(copy: it => it with { AetherTor = "chain" }), 10819), "--noize").Should().BeEqualTo("aggressive");
        await ValueAfter(Build(Profile(AetherProtocol.WireGuard, AetherTransport.Http2), 10819), "--noize").Should().BeEqualTo("aggressive");

        await AetherArguments.MasqueOverHttp2(AetherProtocol.Mim, AetherTransport.Http2, AetherTor.Off, AetherPsiphon.Off).Should().BeTrue();
        await AetherArguments.MasqueOverHttp2(AetherProtocol.Masque, AetherTransport.Http3, AetherTor.Reverse, AetherPsiphon.Off).Should().BeTrue();
        await AetherArguments.MasqueOverHttp2(AetherProtocol.Masque, AetherTransport.Http3, AetherTor.Chain, AetherPsiphon.Chain).Should().BeFalse();
        await AetherArguments.MasqueOverHttp2(AetherProtocol.Gool, AetherTransport.Http2, AetherTor.Off, AetherPsiphon.Off).Should().BeFalse();
        await AetherArguments.MasqueOverHttp2(AetherProtocol.WgOverMasque, AetherTransport.Http2, AetherTor.Off, AetherPsiphon.Off).Should().BeTrue();
        // Tor or Psiphon around WireGuard over MASQUE carry its MASQUE over HTTP/2.
        await AetherArguments.MasqueOverHttp2(AetherProtocol.WgOverMasque, AetherTransport.Http3, AetherTor.Reverse, AetherPsiphon.Off).Should().BeTrue();
        var throughTor = Build(Profile(AetherProtocol.WgOverMasque, copy: it => it with { AetherTor = "reverse" }), 10819);
        await throughTor.Contains("--tor-reverse").Should().BeTrue();
        await ValueAfter(throughTor, "--protocol").Should().BeEqualTo("gool");
        await ValueAfter(throughTor, "--noize").Should().BeNull();
        await ValueAfter(Build(Profile(AetherProtocol.WgOverMasque, AetherTransport.Http2), 10819), "--noize").Should().BeNull();
        await ValueAfter(Build(Profile(AetherProtocol.WgOverMasque), 10819), "--noize").Should().BeEqualTo("aggressive");
    }

    [Test]
    public async Task TheFingerprintShapesEveryMasqueTunnelAndNoOther()
    {
        foreach (var protocol in new[] { AetherProtocol.Masque, AetherProtocol.Mim, AetherProtocol.WgOverMasque })
        {
            foreach (var transport in AetherTransport.All)
            {
                // Chrome's rule is named, whatever the core's default; BoringSSL orders it by the machine's AES
                // instructions as it does the TLS 1.3 suites. So are Chrome's groups, the post-quantum one first. Chrome
                // sends GREASE.
                var chrome = Build(Profile(protocol, transport), 10819);
                await ValueAfter(chrome, "--tls-ciphers").Should().BeEqualTo("ALL:!aPSK:!ECDSA+SHA1:!3DES");
                await ValueAfter(chrome, "--tls-groups").Should().BeEqualTo("X25519MLKEM768:X25519:P-256:P-384");
                await chrome.Contains("--disable-grease").Should().BeFalse();
                // BoringSSL's TLS 1.3 order and key shares are Chrome's.
                await chrome.Contains("--tls13-ciphers").Should().BeFalse();
                await chrome.Contains("--tls-key-shares").Should().BeFalse();
                // And GREASE among the signature algorithms, which Chrome alone sends.
                await chrome.Contains("--enable-grease-sigalgs").Should().BeTrue();
                // Chrome 120's: the same rule, groups without a post-quantum one, GREASE but none among the signature
                // algorithms.
                var chrome120 = Build(Profile(protocol, transport, copy: it => it with { AetherFingerprint = "chrome-120" }), 10819);
                await ValueAfter(chrome120, "--tls-ciphers").Should().BeEqualTo("ALL:!aPSK:!ECDSA+SHA1:!3DES");
                await ValueAfter(chrome120, "--tls-groups").Should().BeEqualTo("X25519:P-256:P-384");
                await chrome120.Contains("--disable-grease").Should().BeFalse();
                await chrome120.Contains("--enable-grease-sigalgs").Should().BeFalse();
                await chrome120.Contains("--tls13-ciphers").Should().BeFalse();
                await chrome120.Contains("--tls-key-shares").Should().BeFalse();
                foreach (var fingerprint in new[] { AetherFingerprint.Firefox, AetherFingerprint.SemiPython, AetherFingerprint.Go })
                {
                    var arguments = Build(Profile(protocol, transport, copy: it => it with { AetherFingerprint = fingerprint.Type }), 10819);
                    await ValueAfter(arguments, "--tls-ciphers").Should().BeEqualTo(fingerprint.Ciphers);
                    await (ValueAfter(arguments, "--tls13-ciphers") == fingerprint.Tls13Ciphers).Should().BeTrue();
                    await ValueAfter(arguments, "--tls-groups").Should().BeEqualTo(fingerprint.Groups);
                    await (ValueAfter(arguments, "--tls-key-shares") == fingerprint.KeyShares).Should().BeTrue();
                    await arguments.Contains("--disable-grease").Should().BeTrue();
                    await arguments.Contains("--enable-grease-sigalgs").Should().BeFalse();
                    // One order of the extensions, as these clients keep.
                    await arguments.Contains("--disable-extension-permutation").Should().BeTrue();
                }
                // Every option of every fingerprint goes to the core, in the fingerprint's order: the signature
                // algorithms, the extensions and the rest.
                foreach (var fingerprint in AetherFingerprint.All)
                {
                    var arguments = Build(Profile(protocol, transport, copy: it => it with { AetherFingerprint = fingerprint.Type }), 10819);
                    var expected = fingerprint.Arguments;
                    var found = Enumerable.Range(0, arguments.Count - expected.Count + 1)
                        .Any(at => arguments.Skip(at).Take(expected.Count).SequenceEqual(expected));
                    await found.Should().BeTrue();
                }
            }
        }
        // A profile from before the setting is Chrome's.
        var older = Build(Profile(copy: it => it with { AetherFingerprint = null }), 10819);
        await ShouldBe(older, [.. Build(Profile(copy: it => it with { AetherFingerprint = "chrome" }), 10819)]);
        await ValueAfter(older, "--tls-ciphers").Should().BeEqualTo(AetherFingerprint.Chrome.Ciphers);
        // WireGuard has no TLS handshake of its own to shape.
        foreach (var protocol in new[] { AetherProtocol.WireGuard, AetherProtocol.Gool })
        {
            var arguments = Build(Profile(protocol, AetherTransport.Http2, copy: it => it with { AetherFingerprint = "go" }), 10819);
            await arguments.Contains("--tls-ciphers").Should().BeFalse();
            await arguments.Contains("--disable-grease").Should().BeFalse();
            await Build(Profile(protocol, AetherTransport.Http2), 10819).Contains("--tls-groups").Should().BeFalse();
            var firefox = Build(Profile(protocol, AetherTransport.Http2, copy: it => it with { AetherFingerprint = "firefox" }), 10819);
            await firefox.Contains("--tls13-ciphers").Should().BeFalse();
            await firefox.Contains("--tls-key-shares").Should().BeFalse();
            await Build(Profile(protocol, AetherTransport.Http2), 10819).Contains("--enable-grease-sigalgs").Should().BeFalse();
            foreach (var fingerprint in AetherFingerprint.All)
            {
                var unshaped = Build(Profile(protocol, AetherTransport.Http2, copy: it => it with { AetherFingerprint = fingerprint.Type }), 10819);
                var flags = fingerprint.Arguments.Where(word => word.StartsWith("--", StringComparison.Ordinal));
                await flags.Any(unshaped.Contains).Should().BeFalse();
            }
        }
        // A scan's probes send the ClientHello the tunnel will.
        await Build(Profile(copy: it => it with { AetherFingerprint = "firefox" }), 0, scan: true).Contains("--disable-grease").Should().BeTrue();
    }

    [Test]
    public async Task ObfuscationIsTheCoresOwnChoiceUnlessAProfileNamesIt()
    {
        // The core takes firewall for MASQUE and balanced for WireGuard and gool; automatic says nothing.
        await ValueAfter(Build(Profile(copy: it => it with { AetherObfuscation = "auto" }), 10819), "--noize").Should().BeNull();
        await ValueAfter(Build(Profile(copy: it => it with { AetherObfuscation = null }), 10819), "--noize").Should().BeNull();
        await ValueAfter(Build(Profile(copy: it => it with { AetherObfuscation = "firewall" }), 10819), "--noize").Should().BeEqualTo("firewall");
        await ValueAfter(Build(Profile(copy: it => it with { AetherObfuscation = "gfw" }), 10819), "--noize").Should().BeEqualTo("gfw");
        await ValueAfter(Build(Profile(copy: it => it with { AetherObfuscation = "off" }), 10819), "--noize").Should().BeEqualTo("off");
    }

    [Test]
    public async Task ASettingThatReadsAsAnOptionNeverReachesTheCoreWhichStillDialsOutThroughXray()
    {
        var crafted = Profile(copy: it => it with
        {
            AetherDns = "--upstream",
            AetherExitLoc = " --upstream",
            AetherEch = true,
            AetherEchDns = "--upstream",
            AetherEchDomain = "--bind",
            AetherMasqueSni = "--upstream",
        });
        var arguments = Build(crafted, 10819);
        await arguments.Contains("--upstream").Should().BeFalse();
        await ValueAfter(arguments, "--dns").Should().BeNull();
        await ValueAfter(arguments, "--exit-loc").Should().BeNull();
        await ValueAfter(arguments, "--ech-dns").Should().BeEqualTo("udp://1.1.1.1");
        await ValueAfter(arguments, "--ech-domain").Should().BeEqualTo("cloudflare-ech.com");
        await ValueAfter(arguments, "--masque-sni").Should().BeEqualTo("www.cloudflare.com");
        await ValueAfter(arguments, "--bind").Should().BeEqualTo("127.0.0.1:10819");
        // The core of the session is still told to dial out through Xray.
        var session = AetherCore.Of(crafted, 10819).Through(10822);
        await ValueAfter(session.Arguments, "--upstream").Should().BeEqualTo("socks5://127.0.0.1:10822");

        var psiphon = Profile(copy: it => it with
        {
            AetherPsiphon = AetherPsiphon.Chain.Type,
            AetherPsiphonMode = "cdn",
            AetherPsiphonCdnIps = "--upstream",
            AetherPsiphonCdnSni = "-x",
            AetherPsiphonRegion = "--upstream",
        });
        var carried = Build(psiphon, 10819);
        await carried.Contains("--upstream").Should().BeFalse();
        await ValueAfter(carried, "--psiphon-cdn-ips").Should().BeNull();
        await ValueAfter(carried, "--psiphon-cdn-sni").Should().BeNull();
        await ValueAfter(carried, "--psiphon-region").Should().BeNull();

        var tor = Profile(copy: it => it with
        {
            AetherTor = AetherTor.Chain.Type,
            AetherTorBridges = "own",
            AetherTorBridgeLines = "--upstream\nobfs4 192.0.2.1:443 FP cert=x iat-mode=0",
        });
        var bridged = Build(tor, 10819);
        await bridged.Contains("--upstream").Should().BeFalse();
        await ShouldBe(ValuesAfter(bridged, "--tor-bridge"), "obfs4 192.0.2.1:443 FP cert=x iat-mode=0");
    }

    [Test]
    public async Task TheServerNameOfTheMasqueHandshakesReachesTheCoreOverMasqueAlone()
    {
        // The default is named as well, so that the command shows what is sent.
        await ValueAfter(Build(Profile(), 10819), "--masque-sni").Should().BeEqualTo("www.cloudflare.com");
        var blank = Profile(copy: it => it with { AetherMasqueSni = " " });
        await ValueAfter(Build(blank, 10819), "--masque-sni").Should().BeEqualTo("www.cloudflare.com");

        // A name of the profile's own, trimmed, on either carrier, on every protocol over MASQUE, and in a scan.
        var named = Profile(copy: it => it with { AetherMasqueSni = " consumer-masque.cloudflareclient.com " });
        var cases = new[]
        {
            named,
            Copy(named, it => it with { AetherTransport = AetherTransport.Http2.Type }),
            Copy(named, it => it with { AetherProtocol = AetherProtocol.Mim.Type }),
            Copy(named, it => it with { AetherProtocol = AetherProtocol.WgOverMasque.Type }),
            Copy(named, it => it with { AetherTor = AetherTor.Reverse.Type }),
            Copy(named, it => it with { AetherPsiphon = AetherPsiphon.Chain.Type }),
        };
        foreach (var profile in cases)
        {
            foreach (var scan in new[] { false, true })
            {
                var arguments = Build(profile, 10819, scan: scan);
                await ValueAfter(arguments, "--masque-sni").Should().BeEqualTo("consumer-masque.cloudflareclient.com");
                await arguments.Count(it => it == "--masque-sni").Should().BeEqualTo(1);
            }
        }

        // WireGuard takes none, nor does a core without a WARP tunnel.
        var without = new[]
        {
            Copy(named, it => it with { AetherProtocol = AetherProtocol.WireGuard.Type }),
            Copy(named, it => it with { AetherProtocol = AetherProtocol.Gool.Type }),
            Copy(named, it => it with { AetherPsiphon = AetherPsiphon.Only.Type }),
            Copy(named, it => it with { AetherTor = AetherTor.Only.Type }),
        };
        foreach (var profile in without)
        {
            await Build(profile, 10819).Contains("--masque-sni").Should().BeFalse();
        }
    }

    [Test]
    public async Task EncryptedClientHelloTheResolversAndTheExitRuleReachTheCore()
    {
        var tuned = Profile(copy: it => it with { AetherEch = true, AetherDns = "1.1.1.1,10.0.0.1:5353", AetherExitLoc = "!IR,RU" });
        var arguments = Build(tuned, 10819);
        await ValueAfter(arguments, "--ech").Should().BeEqualTo("auto");
        await ValueAfter(arguments, "--dns").Should().BeEqualTo("1.1.1.1,10.0.0.1:5353");
        await ValueAfter(arguments, "--exit-loc").Should().BeEqualTo("!IR,RU");

        // The key is asked of the default resolver for the default domain, unless the profile names others.
        await ValueAfter(arguments, "--ech-dns").Should().BeEqualTo("udp://1.1.1.1");
        await ValueAfter(arguments, "--ech-domain").Should().BeEqualTo("cloudflare-ech.com");
        var named = Build(Copy(tuned, it => it with { AetherEchDns = " https://doq.dns4all.eu/dns-query ", AetherEchDomain = "ip.gs" }), 10819);
        await ValueAfter(named, "--ech-dns").Should().BeEqualTo("https://doq.dns4all.eu/dns-query");
        await ValueAfter(named, "--ech-domain").Should().BeEqualTo("ip.gs");
        var blank = Build(Copy(tuned, it => it with { AetherEchDns = " ", AetherEchDomain = "" }), 10819);
        await ValueAfter(blank, "--ech-dns").Should().BeEqualTo("udp://1.1.1.1");
        await ValueAfter(blank, "--ech-domain").Should().BeEqualTo("cloudflare-ech.com");
        var off = Build(Profile(copy: it => it with { AetherEchDns = "tcp://1.1.1.1", AetherEchDomain = "ip.gs" }), 10819);
        await ValueAfter(off, "--ech-dns").Should().BeNull();
        await ValueAfter(off, "--ech-domain").Should().BeNull();

        // ECH belongs to the MASQUE handshake, on either carrier and both hops.
        await ValueAfter(Build(Copy(tuned, it => it with { AetherProtocol = "mim" }), 10819), "--ech").Should().BeEqualTo("auto");
        await ValueAfter(Build(Copy(tuned, it => it with { AetherProtocol = "wg" }), 10819), "--ech").Should().BeNull();
        await ValueAfter(Build(Profile(), 10819), "--ech").Should().BeNull();

        // A scan runs under the same conditions, the exit rule included, so it ends on an endpoint the session will accept.
        var scan = Build(tuned, 0, scan: true);
        await ValueAfter(scan, "--ech").Should().BeEqualTo("auto");
        await ValueAfter(scan, "--ech-dns").Should().BeEqualTo("udp://1.1.1.1");
        await ValueAfter(scan, "--ech-domain").Should().BeEqualTo("cloudflare-ech.com");
        await ValueAfter(scan, "--dns").Should().BeEqualTo("1.1.1.1,10.0.0.1:5353");
        await ValueAfter(scan, "--exit-loc").Should().BeEqualTo("!IR,RU");

        // Without a WARP tunnel there is nothing for them to apply to.
        var alone = Build(Copy(tuned, it => it with { AetherPsiphon = "only" }), 10819);
        await ValueAfter(alone, "--dns").Should().BeNull();
        await ValueAfter(alone, "--exit-loc").Should().BeNull();
        await ValueAfter(alone, "--ech").Should().BeNull();
    }

    [Test]
    public async Task TheBridgePoolReachesTheCoreOnlyWhereBridgesAreFetched()
    {
        await ValueAfter(Build(Profile(copy: it => it with { AetherTor = "chain", AetherTorRelays = "only" }), 10819), "--tor-relays").Should().BeEqualTo("only");
        await ValueAfter(Build(Profile(copy: it => it with { AetherTor = "only", AetherTorBridges = "first", AetherTorRelays = "off" }), 10819), "--tor-relays")
            .Should().BeEqualTo("off");
        await ValueAfter(Build(Profile(copy: it => it with { AetherTor = "chain", AetherTorRelays = "auto" }), 10819), "--tor-relays").Should().BeNull();
        // With the profile's own lines, or no bridges at all, nothing is fetched.
        await ValueAfter(Build(Profile(copy: it => it with { AetherTor = "only", AetherTorBridges = "never", AetherTorRelays = "only" }), 10819), "--tor-relays")
            .Should().BeNull();
        var own = Profile(copy: it => it with
        {
            AetherTor = "only",
            AetherTorBridges = "own",
            AetherTorBridgeLines = "obfs4 192.0.2.55:38114 316E64 cert=abc iat-mode=0",
            AetherTorRelays = "only",
        });
        await ValueAfter(Build(own, 10819), "--tor-relays").Should().BeNull();
        await ValueAfter(Build(Profile(copy: it => it with { AetherTorRelays = "only" }), 10819), "--tor-relays").Should().BeNull();
    }

    [Test]
    public async Task TheTunnelIsNamedFromTheOutsideIn()
    {
        static List<string> Path(params string[] words)
        {
            return AetherArguments.PathOf(words);
        }

        await ShouldBe(Path("--bind", "127.0.0.1:10819"), "MASQUE");
        await ShouldBe(Path("--wg", "--psiphon"), "WIREGUARD", "PSIPHON");
        await ShouldBe(Path("--psiphon-reverse"), "PSIPHON", "MASQUE");
        await ShouldBe(Path("--tor"), "MASQUE", "TOR");
        await ShouldBe(Path("--mim", "--tor-reverse"), "TOR", "MIM");
        await ShouldBe(Path("--gool", "--psiphon", "--tor-reverse"), "TOR", "WG_OVER_MASQUE", "PSIPHON");
        await ShouldBe(Path("--protocol", "gool", "--gool-classic", "--tor"), "GOOL", "TOR");
        await ShouldBe(Path("--wg", "--tor", "--psiphon-reverse"), "PSIPHON", "WIREGUARD", "TOR");
        // A carrier alone is the whole tunnel, whatever else is written; Tor alone comes first, as it does for the core.
        await ShouldBe(Path("--wg", "--psiphon-only"), "PSIPHON");
        await ShouldBe(Path("--tor-only", "--psiphon"), "TOR");
        await ShouldBe(Path("--tor-only", "--psiphon-only"), "TOR");
        // The last mode flag wins, as it does for the core.
        await ShouldBe(Path("--tor", "--tor-only"), "TOR");
        await AetherArguments.TorModeOf(["--tor", "--tor-only"]).Should().BeEqualTo(AetherTor.Only);
        await AetherArguments.PsiphonModeOf(["--psiphon-reverse", "--psiphon"]).Should().BeEqualTo(AetherPsiphon.Chain);
        await AetherArguments.ListenerFlagOf(["--psiphon", "--psiphon-only"]).Should().BeEqualTo("--bind");
        // A carrier alone is dialled on the core's own listener, whatever the other is told: the core starts only it.
        await AetherArguments.ListenerFlagOf(["--tor-only", "--psiphon"]).Should().BeEqualTo("--bind");
        await AetherArguments.ListenerFlagOf(["--psiphon-only", "--tor"]).Should().BeEqualTo("--bind");
        await AetherArguments.AloneOf(["--tor-only", "--psiphon-only"]).Should().BeEqualTo("TOR");
        await AetherArguments.AloneOf(["--psiphon-only", "--tor"]).Should().BeEqualTo("PSIPHON");
        await AetherArguments.AloneOf(["--tor", "--psiphon-reverse"]).Should().BeNull();
    }

    // The ready word itself is checked in AetherLogLineTests, which ports the rest of PattNG's test.
    [Test]
    public async Task WhereTheAppDialsPsiphonReadinessNeedsTheCoresWord()
    {
        await AetherArguments.ReadyNeedsWord(["--wg", "--psiphon", "--psiphon-bind", "127.0.0.1:10819"]).Should().BeTrue();
        await AetherArguments.ReadyNeedsWord(["--psiphon-only", "--bind", "127.0.0.1:10819"]).Should().BeTrue();
        // Around the tunnel the app dials WARP, whose listener comes up only once the tunnel stands.
        await AetherArguments.ReadyNeedsWord(["--masque", "--psiphon-reverse"]).Should().BeFalse();
        await AetherArguments.ReadyNeedsWord(["--wg", "--tor"]).Should().BeFalse();
        await AetherArguments.ReadyNeedsWord(["--wg"]).Should().BeFalse();
        // Tor alone runs no Psiphon, whatever Psiphon is told; Psiphon alone does say it.
        await AetherArguments.ReadyNeedsWord(["--tor-only", "--psiphon"]).Should().BeFalse();
        await AetherArguments.ReadyNeedsWord(["--psiphon-only", "--tor"]).Should().BeTrue();

        // The word is an info line: a quieter core never writes it, and the listener alone decides then.
        await AetherArguments.ShowsInfo(["--psiphon"]).Should().BeTrue();
        await AetherArguments.ShowsInfo(["--psiphon", "--log-level", "debug"]).Should().BeTrue();
        await AetherArguments.ShowsInfo(["--psiphon", "--verbose"]).Should().BeTrue();
        await AetherArguments.ShowsInfo(["--psiphon", "--log-level", "warn"]).Should().BeFalse();
        await AetherArguments.ShowsInfo(["--psiphon", "--log-level", "error"]).Should().BeFalse();
    }

    [Test]
    public async Task TheCdnSetsGoToTheCoreWhereverFrontingIsInPlay()
    {
        var sets = Profile(copy: it => it with { AetherPsiphon = "chain", AetherPsiphonCdnSets = "fastly,cloudflare" });
        await ValueAfter(Build(sets, 0), "--psiphon-cdn-sets").Should().BeEqualTo("cloudflare,fastly");
        // Beside addresses of one's own as well: the core scans both.
        var own = Copy(sets, it => it with { AetherPsiphonMode = "cdn", AetherPsiphonCdnIps = "203.0.113.7" });
        await ValueAfter(Build(own, 0), "--psiphon-cdn-sets").Should().BeEqualTo("cloudflare,fastly");
        // Not where nothing is fronted, not for a stranger, not without Psiphon.
        await ValueAfter(Build(Copy(sets, it => it with { AetherPsiphonMode = "direct" }), 0), "--psiphon-cdn-sets").Should().BeNull();
        await ValueAfter(Build(Copy(sets, it => it with { AetherPsiphonCdnSets = "nowhere" }), 0), "--psiphon-cdn-sets").Should().BeNull();
        await ValueAfter(Build(Profile(copy: it => it with { AetherPsiphonCdnSets = "fastly" }), 0), "--psiphon-cdn-sets").Should().BeNull();
    }

    [Test]
    public async Task PsiphonStartsFromTheShippedListUnlessTheProfileSaysNo()
    {
        var chain = Build(Profile(copy: it => it with { AetherPsiphon = "chain" }), 0);
        await ValueAfter(chain, "--psiphon-server-entries").Should().BeEqualTo("shipped-list");
        await ValueAfter(Build(Profile(copy: it => it with { AetherPsiphon = "only" }), 0), AetherArguments.PsiphonServerEntries)
            .Should().BeEqualTo(AetherArguments.ShippedList);
        await Build(Profile(copy: it => it with { AetherPsiphon = "chain", AetherPsiphonBundledList = false }), 0)
            .Contains(AetherArguments.PsiphonServerEntries).Should().BeFalse();
        await Build(Profile(), 0).Contains(AetherArguments.PsiphonServerEntries).Should().BeFalse();
        // A scan keeps a carrier around the tunnel, list and all, and drops one inside it, list and all.
        await ValueAfter(Build(Profile(copy: it => it with { AetherPsiphon = "reverse" }), 0, scan: true), "--psiphon-server-entries").Should().BeEqualTo("shipped-list");
        await Build(Profile(copy: it => it with { AetherPsiphon = "chain" }), 0, scan: true).Contains(AetherArguments.PsiphonServerEntries).Should().BeFalse();

        // At start the word gives way to the app's file, or goes when there is none; a file of one's own stays.
        const string entries = "/data/app/psiphon-servers.txt";
        await ShouldBe(
            AetherArguments.WithShippedList(["--psiphon", "--psiphon-server-entries", "shipped-list"], entries),
            "--psiphon", "--psiphon-server-entries", System.IO.Path.GetFullPath(entries));
        await ShouldBe(AetherArguments.WithShippedList(["--psiphon", "--psiphon-server-entries", "shipped-list"], null), "--psiphon");
        await ShouldBe(
            AetherArguments.WithShippedList(["--psiphon", "--psiphon-server-entries", "/sdcard/mine.txt"], entries),
            "--psiphon", "--psiphon-server-entries", "/sdcard/mine.txt");
        await ShouldBe(AetherArguments.WithShippedList(["--psiphon"], entries), "--psiphon");
    }

    [Test]
    public async Task AProfileThatNamesNoIpVersionConnectsOverIPv4()
    {
        // IPv4 works on an IPv4-only network and on a dual-stack one; a profile says so when it wants more.
        await ValueAfter(Build(Profile(copy: it => it with { AetherIpVersion = null }), 0), "--ip").Should().BeEqualTo("v4");
        await ValueAfter(Build(Profile(copy: it => it with { AetherIpVersion = null }), 0, scan: true), "--ip").Should().BeEqualTo("v4");
        await ValueAfter(Build(Profile(copy: it => it with { AetherIpVersion = "both" }), 0), "--ip").Should().BeEqualTo("both");
    }

    [Test]
    public async Task AScanLeavesOutACarrierInsideTheTunnelAndKeepsOneAroundIt()
    {
        var inside = Build(Profile(copy: it => it with { AetherPsiphon = "chain", AetherPsiphonRegion = "DE" }), 0, scan: true);
        await inside.Any(it => it.StartsWith("--psiphon", StringComparison.Ordinal)).Should().BeFalse();
        await ValueAfter(inside, "--bind").Should().BeEqualTo("127.0.0.1:0");
        await Build(Profile(copy: it => it with { AetherTor = "chain" }), 0, scan: true).Contains("--tor").Should().BeFalse();

        // Around the tunnel the carrier is where the session looks from, so the scan looks from there as well.
        var around = Build(Profile(copy: it => it with { AetherPsiphon = "reverse", AetherPsiphonMode = "cdn" }), 0, scan: true);
        await around.Contains("--psiphon-reverse").Should().BeTrue();
        await ValueAfter(around, "--psiphon-mode").Should().BeEqualTo("cdn");
        await ValueAfter(around, "--psiphon-bind").Should().BeEqualTo("127.0.0.1:0");
        await ValueAfter(around, "--bind").Should().BeEqualTo("127.0.0.1:0");
        var torAround = Build(Profile(copy: it => it with { AetherTor = "reverse", AetherTorBridges = "first" }), 41234, scan: true);
        await torAround.Contains("--tor-reverse").Should().BeTrue();
        await torAround.Contains("--tor-bridges").Should().BeTrue();
        await ValueAfter(torAround, "--bind").Should().BeEqualTo("127.0.0.1:41234");
        await ValueAfter(torAround, "--tor-bind").Should().BeEqualTo("127.0.0.1:41235");
    }

    [Test]
    public async Task ACoreReachesWarpThroughACarrierAroundTheTunnelOnly()
    {
        static List<string> ScanOf(ProfileItem profile)
        {
            return AetherArguments.Build(profile, 0, scan: true);
        }

        await AetherArguments.ReachesWarpThroughCarrier(ScanOf(Profile())).Should().BeFalse();
        await AetherArguments.ReachesWarpThroughCarrier(ScanOf(Profile(copy: it => it with { AetherPsiphon = "chain", AetherTor = "chain" }))).Should().BeFalse();
        await AetherArguments.ReachesWarpThroughCarrier(ScanOf(Profile(copy: it => it with { AetherPsiphon = "reverse" }))).Should().BeTrue();
        await AetherArguments.ReachesWarpThroughCarrier(ScanOf(Profile(copy: it => it with { AetherTor = "reverse" }))).Should().BeTrue();

        // A command written by hand, as the WARP keys window may run, is read the way the core reads it.
        await AetherArguments.ReachesWarpThroughCarrier(["--register", "all", "--tor-reverse"]).Should().BeTrue();
        await AetherArguments.ReachesWarpThroughCarrier(["--register", "all", "--psiphon-reverse", "--psiphon"]).Should().BeFalse();
        await AetherArguments.ReachesWarpThroughCarrier(["--register", "all"]).Should().BeFalse();
    }

    [Test]
    public async Task ARunThatWaitsForAnInfoLineIsNeverQuieterThanInfo()
    {
        string[] run = ["--register", "all"];
        // The core writes its info lines unless told otherwise.
        await ShouldBe(AetherArguments.WithInfoLines(run), run);
        await ShouldBe(AetherArguments.WithInfoLines([.. run, "--log-level", "debug"]), [.. run, "--log-level", "debug"]);
        await ShouldBe(AetherArguments.WithInfoLines([.. run, "--verbose"]), [.. run, "--verbose"]);
        // A quieter level gives way, wherever it stands.
        await ShouldBe(AetherArguments.WithInfoLines(["--log-level", "warn", .. run]), [.. run, "--log-level", "info"]);
        await ShouldBe(AetherArguments.WithInfoLines([.. run, "--log-level", "error"]), [.. run, "--log-level", "info"]);
    }

    [Test]
    public async Task TheListenersAreLeftOutWhenTunnelsAreCompared()
    {
        var session = Build(Profile(copy: it => it with { AetherPsiphon = "chain" }), 10819);
        var elsewhere = Build(Profile(copy: it => it with { AetherPsiphon = "chain" }), 20808);
        await ShouldBe(AetherArguments.TunnelArguments(session), [.. AetherArguments.TunnelArguments(elsewhere)]);
        await AetherArguments.TunnelArguments(session).Contains("--psiphon-bind").Should().BeFalse();
    }

    [Test]
    public async Task TheRunningProfileIsToldByTheArgumentsOfItsSession()
    {
        // PattNG AetherCoreManager.runsProfile, on the listen port the session was given.
        static bool RunsProfile(IReadOnlyList<string> arguments, ProfileItem profile) =>
            AetherCore.Of(profile, 10819).RunsAs(arguments);

        var pinned = Profile(server: "162.159.198.1", port: 443);
        var renamed = Profile(server: "162.159.198.1", port: 443);
        renamed.Remarks = "another name";
        var otherPort = Profile(server: "162.159.198.1", port: 2408);
        var session = Build(pinned, 10819, logLevel: "warn");
        await RunsProfile(session, pinned).Should().BeTrue();
        await RunsProfile(session, renamed).Should().BeTrue();
        await RunsProfile(session, otherPort).Should().BeFalse();
        await RunsProfile(session, Profile(AetherProtocol.WireGuard, server: "162.159.198.1", port: 443)).Should().BeFalse();
        await RunsProfile(Build(pinned, 0, scan: true), pinned).Should().BeFalse();
        await RunsProfile([], pinned).Should().BeFalse();
        // A custom configuration runs the same tunnel behind a port of its own.
        await RunsProfile(Build(pinned, 20808), pinned).Should().BeTrue();
        await RunsProfile(Build(pinned, 20808), otherPort).Should().BeFalse();
    }

    [Test]
    public async Task ACoreOfItsOwnDialsOutThroughTheExit()
    {
        var scan = Build(Profile(), 0, scan: true);
        await ShouldBe(AetherArguments.StandaloneArguments(scan, 41234), [.. scan, AetherArguments.Upstream, "socks5://127.0.0.1:41234"]);
        // A command exported from a session names the session's Xray, which a core of its own does not dial.
        string[] exported = ["--wg", AetherArguments.Upstream, "socks5://127.0.0.1:10822", "--bind", "127.0.0.1:10820"];
        await ShouldBe(
            AetherArguments.StandaloneArguments(exported, 41234),
            "--wg", "--bind", "127.0.0.1:10820", AetherArguments.Upstream, "socks5://127.0.0.1:41234");
    }

    // PattN only: the session's log level as PattNG's AetherCoreManager.start sets it (design 2.6.2).
    [Test]
    public async Task TheSessionLevelIsRaisedToInfoWhereReadinessNeedsTheWord()
    {
        await AetherArguments.SessionLogLevel(["--wg"], "warning").Should().BeEqualTo("warn");
        await AetherArguments.SessionLogLevel(["--wg"], "debug").Should().BeEqualTo("debug");
        await AetherArguments.SessionLogLevel(["--wg", "--psiphon"], "warning").Should().BeEqualTo("info");
        await AetherArguments.SessionLogLevel(["--psiphon-only"], "none").Should().BeEqualTo("info");
        await AetherArguments.SessionLogLevel(["--psiphon-only"], "debug").Should().BeEqualTo("debug");
        await AetherArguments.SessionLogLevel(["--wg", "--psiphon-reverse"], "error").Should().BeEqualTo("error");
    }

    // PattNG AetherCoreManagerTest.anAddressTheCoreTakesIsAnIpAddressAndAPort: Rust's SocketAddr, as the core reads a
    // listener's address.
    [Test]
    public async Task AnAddressTheCoreTakesIsAnIpAddressAndAPort()
    {
        string[] taken =
        [
            "127.0.0.1:10819", "0.0.0.0:0", "255.255.255.255:65535", "[::1]:10819", "[::]:1", "[fe80::1%2]:10819",
            "[::ffff:127.0.0.1]:1", "[1:2:3:4:5:6:7:8]:1", "[1:2:3:4:5:6:1.2.3.4]:1", "[1::]:1", "[1:2:3:4:5:6:7::]:1",
            // Rust reads a port, and a scope, with any number of leading zeros.
            "127.0.0.1:000080", "127.0.0.1:0000000000000065535", "[::1%0004294967295]:1",
        ];
        foreach (var address in taken)
        {
            await AetherArguments.IsSocketAddress(address).Should().BeTrue().Because(address);
        }
        string[] refused =
        [
            "localhost:10819", "10819", "127.0.0.1", "127.0.0.1:", "127.0.0.1:65536", "127.0.0.1:-1", "127.0.0.01:1", "1.2.3:1",
            "256.0.0.1:1", "::1:10819", "[::1]", "[:::1]:1", "[1:2:3:4:5:6:7:8:9]:1", "[1:2:3:4:5:6:7:8::]:1", "[1.2.3.4::]:1",
            "[1::2::3]:1", "[fe80::1%]:1", "[gggg::1]:1", "[12345::1]:1", " 127.0.0.1:1", "127.0.0.1:1 ",
            "127.0.0.1:0065536", "127.0.0.1:+80", "[::1%4294967296]:1", "[::1%eth0]:1", "127.0.0.1:۱۰۸۱۹", "[]:1", "]:1",
        ];
        foreach (var address in refused)
        {
            await AetherArguments.IsSocketAddress(address).Should().BeFalse().Because(address);
        }
    }

    // PattNG AetherCoreManagerTest.aListenerAddressIsReadAsTheCoreReadsIt.
    [Test]
    public async Task AListenerAddressIsReadAsTheCoreReadsIt()
    {
        // The core's own listener as written, every other trimmed, the one the app dials among them.
        string[] argv = ["--tor", "--bind", " 127.0.0.1:10820 ", "--tor-bind", " 127.0.0.1:10819 ", "--http-proxy", "127.0.0.1:8118 "];
        await AetherArguments.ListenerAddressAfter(argv, "--bind").Should().BeEqualTo(" 127.0.0.1:10820 ");
        await AetherArguments.ListenerAddressAfter(argv, AetherArguments.TorBind).Should().BeEqualTo("127.0.0.1:10819");
        await AetherArguments.ListenerAddressOf(argv).Should().BeEqualTo("127.0.0.1:10819");
        await AetherArguments.ListenerPortOf(argv).Should().BeEqualTo(10819);
        await AetherArguments.PortAfter(argv, AetherArguments.HttpProxy).Should().BeEqualTo(8118);
        await AetherArguments.PortAfter(argv, "--bind").Should().BeNull();
        await AetherArguments.ListenerAddressAfter(argv, AetherArguments.PsiphonBind).Should().BeNull();
        // By Rust's whitespace, which is .NET's: a next line (U+0085) is trimmed.
        await AetherArguments.PortAfter(["--tor-bind", "127.0.0.1:10819\u0085"], AetherArguments.TorBind).Should().BeEqualTo(10819);
    }

    // PattNG AetherCoreManagerTest.aListenerAnswersTheAppOnLoopbackOrOnEveryAddress, with the system as a parameter.
    [Test]
    public async Task AListenerAnswersTheAppOnLoopbackOrOnEveryAddress()
    {
        string[] everywhere = ["127.0.0.1", "0.0.0.0"];
        string[] dualStackOnly = ["[::]", "[0:0:0:0:0:0:0:0]", "[::0]", "[::ffff:127.0.0.1]", "[::FFFF:7f00:1]", "[0:0:0:0:0:ffff:127.0.0.1]", "[::ffff:0.0.0.0]"];
        string[] nowhere = ["127.0.0.2", "192.168.1.5", "[::1]", "[::ffff:127.0.0.2]", "[::ffff:10.0.0.1]", "[1::]", "[fe80::1%2]", "[::ffff:0:1]"];
        foreach (var host in everywhere)
        {
            await AetherArguments.ReachesLoopback(host, dualStack: true).Should().BeTrue().Because(host);
            await AetherArguments.ReachesLoopback(host, dualStack: false).Should().BeTrue().Because(host);
        }
        foreach (var host in dualStackOnly)
        {
            await AetherArguments.ReachesLoopback(host, dualStack: true).Should().BeTrue().Because(host);
            await AetherArguments.ReachesLoopback(host, dualStack: false).Should().BeFalse().Because(host);
        }
        foreach (var host in nowhere)
        {
            await AetherArguments.ReachesLoopback(host, dualStack: true).Should().BeFalse().Because(host);
        }
    }

    // PattNG AetherCoreManager.defaultListenerPorts: where the core listens when a command leaves a listener out.
    [Test]
    public async Task TheCoreListensOnItsDefaultsWhereACommandNamesNoListener()
    {
        await AetherArguments.DefaultListenerPorts["--bind"].Should().BeEqualTo(1819);
        await AetherArguments.DefaultListenerPorts[AetherArguments.TorBind].Should().BeEqualTo(1820);
        await AetherArguments.DefaultListenerPorts[AetherArguments.PsiphonBind].Should().BeEqualTo(1821);
        // An HTTP proxy left out is off.
        await AetherArguments.DefaultListenerPorts.ContainsKey(AetherArguments.HttpProxy).Should().BeFalse();
    }
}
