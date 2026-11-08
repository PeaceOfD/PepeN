using TUnit.Assertions.Enums;

namespace ServiceLib.Tests.Aether;

// From PattNG AetherCoreTest: the core of a profile or of a command line. Its exit-node tests are in AetherExitTests.
// PattNG reads the Aether listen port from the settings where a test leaves it out (onListenPort swaps the setting);
// AetherCore.Of and OfCommand take it, so the tests pass the port the setting would hold: 10819 unless one is named.
public class AetherCoreTests
{
    private const int ListenPort = AetherPorts.DefaultListenPort;

    // PattNG's profile { ... }: a WireGuard profile with the changes of extra and set.
    private static ProfileItem Profile(Func<ProtocolExtraItem, ProtocolExtraItem>? extra = null, Action<ProfileItem>? set = null)
    {
        var profile = new ProfileItem { ConfigType = EConfigType.Aether };
        var protocolExtra = new ProtocolExtraItem { AetherProtocol = AetherProtocol.WireGuard.Type };
        profile.SetProtocolExtra(extra == null ? protocolExtra : extra(protocolExtra));
        set?.Invoke(profile);
        return profile;
    }

    // PattNG's pinned: the endpoint is Address/Port (PattNG: server/serverPort).
    private static ProfileItem Pinned()
    {
        return Profile(set: profile =>
        {
            profile.Address = "188.114.96.77";
            profile.Port = 443;
        });
    }

    // PattNG's profile.copy(...): a new profile with the changes, the one copied left as it was.
    private static ProfileItem Copy(ProfileItem profile, Func<ProtocolExtraItem, ProtocolExtraItem>? extra = null, Action<ProfileItem>? set = null)
    {
        var copy = JsonUtils.DeepCopy(profile)!;
        if (extra != null)
        {
            copy.SetProtocolExtra(extra(copy.GetProtocolExtra()));
        }
        set?.Invoke(copy);
        return copy;
    }

    // As PattNG's test helper: the value after the first flag, unlike AetherArguments.ValueAfter.
    private static string? ValueAfter(IReadOnlyList<string> arguments, string flag)
    {
        var at = arguments.ToList().IndexOf(flag);
        return at >= 0 && at + 1 < arguments.Count ? arguments[at + 1] : null;
    }

    // PattNG's assertEquals on lists: the same items in the same order.
    private static async Task ShouldBe<T>(IReadOnlyList<T> actual, params T[] expected)
    {
        await actual.Should().BeEquivalentTo(expected, CollectionOrdering.Matching);
    }

    private static AetherCore OfCommand(string command, int listenPort = ListenPort)
    {
        return AetherCore.OfCommand(command, listenPort)!;
    }

    [Test]
    public async Task TheCoreOfAProfileIsItsSettingsOnTheAetherListenPortWithoutALogLevel()
    {
        var core = AetherCore.Of(Pinned(), 20808);
        // Obfuscation left automatic is the core's own choice, so the arguments say nothing about it.
        await ShouldBe(core.Arguments,
            "--bind", "127.0.0.1:20808", "--protocol", "wg", "--scan", "balanced", "--ip", "v4",
            "--peer", "188.114.96.77:443", "--quick-reconnect");
        await core.Port.Should().BeEqualTo(20808);
        await core.Protocol.Should().BeEqualTo(AetherProtocol.WireGuard);
        await AetherCore.Of(Profile(), ListenPort).Port.Should().BeEqualTo(ListenPort);
    }

    [Test]
    public async Task AListenPortAProfileStoredOfItsOwnCountsNoMore()
    {
        // Profiles stored while each profile had a listen port of its own may carry one still. PattN has no such field:
        // the stored JSON of such a profile carries the key, which nothing reads.
        var legacy = new ProfileItem
        {
            ConfigType = EConfigType.Aether,
            Address = "188.114.96.77",
            Port = 443,
            ProtoExtra = """{"AetherProtocol":"wg","AetherListenPort":"20808"}""",
        };
        await legacy.GetProtocolExtra().AetherProtocol.Should().BeEqualTo("wg");
        await AetherCore.Of(legacy, ListenPort).Should().BeEqualTo(AetherCore.Of(Pinned(), ListenPort));
        await AetherCore.Of(legacy, ListenPort).Port.Should().BeEqualTo(ListenPort);
    }

    [Test]
    public async Task TheCommandOfAProfileReadsBackAsTheSameCore()
    {
        var core = AetherCore.Of(Pinned(), ListenPort);
        await core.Command.Should().BeEqualTo(
            "aether --bind 127.0.0.1:10819 --protocol wg --scan balanced --ip v4 --peer 188.114.96.77:443 --quick-reconnect");
        await AetherCore.OfCommand(core.Command, ListenPort).Should().BeEqualTo(core);

        var gool = AetherCore.Of(Profile(extra => extra with { AetherProtocol = AetherProtocol.Gool.Type, AetherWiwOuter = "162.159.192.1:2408" }), ListenPort);
        await AetherCore.OfCommand(gool.Command, ListenPort).Should().BeEqualTo(gool);
        await OfCommand(gool.Command).Protocol.Should().BeEqualTo(AetherProtocol.Gool);

        var mim = AetherCore.Of(Profile(extra => extra with { AetherProtocol = AetherProtocol.Mim.Type, AetherWiwInner = "188.114.96.1:443" }), ListenPort);
        await AetherCore.OfCommand(mim.Command, ListenPort).Should().BeEqualTo(mim);
        await OfCommand(mim.Command).Protocol.Should().BeEqualTo(AetherProtocol.Mim);

        var goolOverMasque = AetherCore.Of(
            Profile(extra => extra with { AetherProtocol = AetherProtocol.WgOverMasque.Type, AetherWiwInner = "162.159.192.1:2408" }), ListenPort);
        await AetherCore.OfCommand(goolOverMasque.Command, ListenPort).Should().BeEqualTo(goolOverMasque);
        await OfCommand(goolOverMasque.Command).Protocol.Should().BeEqualTo(AetherProtocol.WgOverMasque);
    }

    [Test]
    public async Task ACommandIsReadAsWritten()
    {
        // Whatever the words mean is for the core to say; the app reads the listener and the protocol.
        var core = OfCommand("aether --gool --scan balanced --bind 127.0.0.1:20808 --dns 1.1.1.1");
        await ShouldBe(core.Arguments, "--gool", "--scan", "balanced", "--bind", "127.0.0.1:20808", "--dns", "1.1.1.1");
        await core.Port.Should().BeEqualTo(20808);
        // Gool is WireGuard over MASQUE to the core, unless something makes it the classic gool.
        await core.Protocol.Should().BeEqualTo(AetherProtocol.WgOverMasque);
        await OfCommand("aether --gool --gool-classic --bind 127.0.0.1:20808").Protocol.Should().BeEqualTo(AetherProtocol.Gool);
    }

    [Test]
    public async Task TheProgramNameInFrontIsDroppedWhateverItIs()
    {
        string[] arguments = ["--wg", "--bind", "127.0.0.1:10819"];
        await ShouldBe(OfCommand("aether --wg --bind 127.0.0.1:10819").Arguments, arguments);
        await ShouldBe(OfCommand("/data/app/lib/libaether.so --wg --bind 127.0.0.1:10819").Arguments, arguments);
        await ShouldBe(OfCommand("--wg --bind 127.0.0.1:10819").Arguments, arguments);
        await ShouldBe(OfCommand("  aether   --wg\t--bind 127.0.0.1:10819\n").Arguments, arguments);
    }

    [Test]
    public async Task ACommandWithoutABindListensOnTheDefaultPortOfTheApp()
    {
        // The core's own default is another port, which no outbound of the app dials.
        var core = OfCommand("aether --wg --turbo");
        await ShouldBe(core.Arguments, "--wg", "--turbo", "--bind", "127.0.0.1:10819");
        await core.Port.Should().BeEqualTo(ListenPort);
    }

    [Test]
    public async Task ACommandThatNamesNothingTheAppCanRunIsNoCore()
    {
        await AetherCore.OfCommand("", ListenPort).Should().BeNull();
        await AetherCore.OfCommand("   ", ListenPort).Should().BeNull();
        await AetherCore.OfCommand("aether", ListenPort).Should().BeNull();
        // A listener whose port cannot be read would otherwise be replaced without a word.
        await AetherCore.OfCommand("aether --wg --bind 10819", ListenPort).Should().BeNull();
        await AetherCore.OfCommand("aether --wg --bind", ListenPort).Should().BeNull();
    }

    [Test]
    public async Task QuotesKeepAWordTogether()
    {
        const string bridge = "obfs4 1.2.3.4:443 FINGERPRINT cert=abc iat-mode=0";
        var core = OfCommand($"aether --tor-only --tor-bridge \"{bridge}\" --bind 127.0.0.1:10819");
        await ShouldBe(core.Arguments, "--tor-only", "--tor-bridge", bridge, "--bind", "127.0.0.1:10819");
        await core.Command.Should().BeEqualTo($"aether --tor-only --tor-bridge \"{bridge}\" --bind 127.0.0.1:10819");

        await ShouldBe(AetherCore.Words("--x 'a b'"), "--x", "a b");
        await ShouldBe(AetherCore.Words("--x \"\""), "--x", "");
        await ShouldBe(AetherCore.Words("--x \"a b"), "--x", "a b");
        await ShouldBe(AetherCore.Words("--x a\"\"b"), "--x", "ab");
    }

    [Test]
    public async Task AProcessRunsTheCoreOnWhateverPortsAndWhateverItLogs()
    {
        var core = OfCommand("aether --wg --bind 127.0.0.1:20808 --scan turbo");

        await core.RunsAs(core.Arguments).Should().BeTrue();
        await core.RunsAs([.. core.Arguments, "--log-level", "debug"]).Should().BeTrue();
        // The port is for the caller to compare; the tunnel is the same.
        await core.RunsAs(OfCommand("aether --wg --bind 127.0.0.1:41234 --scan turbo").Arguments).Should().BeTrue();
        await core.RunsAs(OfCommand("aether --wg --bind 127.0.0.1:20808 --scan thorough").Arguments).Should().BeFalse();
        await core.RunsAs([]).Should().BeFalse();
    }

    [Test]
    public async Task AProfileWithACommandOfItsOwnRunsThatCommand()
    {
        var core = AetherCore.Of(Copy(Pinned(), extra => extra with { AetherCommand = "aether --wg --dns 1.1.1.1 --bind 127.0.0.1:20808" }), ListenPort);
        await ShouldBe(core.Arguments, "--wg", "--dns", "1.1.1.1", "--bind", "127.0.0.1:20808");
        await core.Port.Should().BeEqualTo(20808);
        // A command the app cannot read is left aside for the settings; the editor refuses to store one.
        await AetherCore.Of(Copy(Pinned(), extra => extra with { AetherCommand = "aether" }), ListenPort)
            .Should().BeEqualTo(AetherCore.Of(Pinned(), ListenPort));
        await AetherCore.Of(Copy(Pinned(), extra => extra with { AetherCommand = "   " }), ListenPort)
            .Should().BeEqualTo(AetherCore.Of(Pinned(), ListenPort));
    }

    [Test]
    public async Task WithPsiphonInsideTheTunnelTheAppDialsPsiphon()
    {
        var chain = AetherCore.Of(Copy(Pinned(), extra => extra with { AetherPsiphon = "chain" }), 20808);
        await chain.Port.Should().BeEqualTo(20808);
        await ShouldBe(chain.Ports, 20809, 20808);
        await ValueAfter(chain.Arguments, "--bind").Should().BeEqualTo("127.0.0.1:20809");
        await ValueAfter(chain.Arguments, "--psiphon-bind").Should().BeEqualTo("127.0.0.1:20808");

        // A hand-written command with Psiphon inside gets the app's port for Psiphon when it names none, and keeps its own otherwise.
        await ShouldBe(OfCommand("aether --psiphon --wg").Arguments, "--psiphon", "--wg", "--psiphon-bind", "127.0.0.1:10819");
        await OfCommand("aether --psiphon --bind 127.0.0.1:10819 --psiphon-bind 127.0.0.1:1821").Port.Should().BeEqualTo(1821);
        await AetherCore.OfCommand("aether --psiphon --psiphon-bind 1821", ListenPort).Should().BeNull();
    }

    [Test]
    public async Task WithPsiphonAroundTheTunnelTheAppDialsTheTunnel()
    {
        var reverse = AetherCore.Of(Copy(Pinned(), extra => extra with { AetherProtocol = "masque", AetherPsiphon = "reverse" }), 20808);
        await reverse.Port.Should().BeEqualTo(20808);
        await ShouldBe(reverse.Ports, 20808);
        await ValueAfter(reverse.Arguments, "--psiphon-bind").Should().BeEqualTo("127.0.0.1:0");

        var only = AetherCore.Of(Copy(Pinned(), extra => extra with { AetherPsiphon = "only" }), 20808);
        await only.Port.Should().BeEqualTo(20808);
        await ShouldBe(only.Ports, 20808);
    }

    [Test]
    public async Task WithTorInsideTheTunnelTheAppDialsTor()
    {
        var chain = AetherCore.Of(Copy(Pinned(), extra => extra with { AetherTor = "chain" }), 20808);
        await chain.Port.Should().BeEqualTo(20808);
        await ShouldBe(chain.Ports, 20809, 20808);
        await ValueAfter(chain.Arguments, "--tor-bind").Should().BeEqualTo("127.0.0.1:20808");
        await ValueAfter(chain.Arguments, "--bind").Should().BeEqualTo("127.0.0.1:20809");

        // A hand-written command with Tor inside gets the app's port for Tor when it names none, and keeps its own otherwise.
        await ShouldBe(OfCommand("aether --tor --wg").Arguments, "--tor", "--wg", "--tor-bind", "127.0.0.1:10819");
        await OfCommand("aether --tor --bind 127.0.0.1:10819 --tor-bind 127.0.0.1:1820").Port.Should().BeEqualTo(1820);
    }

    [Test]
    public async Task WithTorAroundTheTunnelTorsOwnListenerFollowsTheTunnel()
    {
        var reverse = AetherCore.Of(Copy(Pinned(), extra => extra with { AetherProtocol = "masque", AetherTor = "reverse" }), 20808);
        await reverse.Port.Should().BeEqualTo(20808);
        await ShouldBe(reverse.Ports, 20808, 20809);
        await ValueAfter(reverse.Arguments, "--bind").Should().BeEqualTo("127.0.0.1:20808");
        await ValueAfter(reverse.Arguments, "--tor-bind").Should().BeEqualTo("127.0.0.1:20809");

        // Nested carriers take the ports after it in the order the profile hands them out; Psiphon around the tunnel keeps an ephemeral port.
        var nested = AetherCore.Of(Copy(Pinned(), extra => extra with { AetherProtocol = "masque", AetherPsiphon = "chain", AetherTor = "reverse" }), 20808);
        await nested.Port.Should().BeEqualTo(20808);
        await ShouldBe(nested.Ports, 20809, 20810, 20808);
        await ValueAfter(nested.Arguments, "--psiphon-bind").Should().BeEqualTo("127.0.0.1:20808");
        await ValueAfter(nested.Arguments, "--bind").Should().BeEqualTo("127.0.0.1:20809");
        await ValueAfter(nested.Arguments, "--tor-bind").Should().BeEqualTo("127.0.0.1:20810");

        var torInside = AetherCore.Of(Copy(Pinned(), extra => extra with { AetherProtocol = "masque", AetherPsiphon = "reverse", AetherTor = "chain" }), 20808);
        await ValueAfter(torInside.Arguments, "--tor-bind").Should().BeEqualTo("127.0.0.1:20808");
        await ValueAfter(torInside.Arguments, "--bind").Should().BeEqualTo("127.0.0.1:20809");
        await ValueAfter(torInside.Arguments, "--psiphon-bind").Should().BeEqualTo("127.0.0.1:0");
    }

    [Test]
    public async Task ACoreNamesItsTunnelFromTheOutsideIn()
    {
        await ShouldBe(AetherCore.Of(Pinned(), ListenPort).Path, "WIREGUARD");
        await ShouldBe(AetherCore.Of(Copy(Pinned(), extra => extra with { AetherPsiphon = "chain" }), ListenPort).Path, "WIREGUARD", "PSIPHON");
        await ShouldBe(AetherCore.Of(Copy(Pinned(), extra => extra with { AetherPsiphon = "only" }), ListenPort).Path, "PSIPHON");
        await ShouldBe(AetherCore.Of(Copy(Pinned(), extra => extra with { AetherProtocol = "masque", AetherTor = "reverse" }), ListenPort).Path, "TOR", "MASQUE");
        await ShouldBe(OfCommand("aether --gool --tor").Path, "WG_OVER_MASQUE", "TOR");
        await ShouldBe(OfCommand("aether --gool-classic --tor").Path, "GOOL", "TOR");
    }

    // PattN: the path as the profile list names it, PattNG's row joining the parts by arrows.
    [Test]
    public async Task ThePathLabelJoinsThePartsOfTheTunnelByArrows()
    {
        var reverse = AetherCore.Of(Copy(Pinned(), extra => extra with { AetherProtocol = "masque", AetherTor = "reverse" }), ListenPort);
        await reverse.PathLabel().Should().BeEqualTo("TOR → MASQUE");
        await reverse.PathLabel(protocol => protocol.Type).Should().BeEqualTo("TOR → masque");
        await ShouldBe(reverse.LabeledPath(protocol => protocol.Type), "TOR", "masque");
    }

    [Test]
    public async Task TheSameArgumentsAreTheSameCore()
    {
        await OfCommand("aether --wg --bind 127.0.0.1:20808").Should().BeEqualTo(OfCommand("aether --wg --bind 127.0.0.1:20808"));
        await OfCommand("aether --wg --bind 127.0.0.1:20808").Should().NotBeEqualTo(OfCommand("aether --wg --bind 127.0.0.1:20809"));
        await OfCommand("aether --wg --bind 127.0.0.1:20808").Should().NotBeEqualTo(OfCommand("aether --bind 127.0.0.1:20808 --wg"));
    }

    [Test]
    public async Task ACoreDialsOutThroughXrayAndStaysTheSameTunnel()
    {
        var core = OfCommand("aether --bind 127.0.0.1:10819 --protocol wg --scan balanced");
        var routed = core.Through(10821);
        await core.HasUpstream.Should().BeFalse();
        await routed.HasUpstream.Should().BeTrue();
        await ValueAfter(routed.Arguments, "--upstream").Should().BeEqualTo("socks5://127.0.0.1:10821");
        // The session's core is the profile's tunnel still: a process running one runs the other.
        await core.RunsAs(routed.Arguments).Should().BeTrue();
        await routed.RunsAs(core.Arguments).Should().BeTrue();
        await ShouldBe(routed.Ports, core.Ports.ToArray());

        // A command that names an upstream of its own keeps it.
        var own = OfCommand("aether --bind 127.0.0.1:10819 --upstream socks5://127.0.0.1:1080");
        await own.HasUpstream.Should().BeTrue();
        await own.Through(10821).Should().BeEqualTo(own);
    }

    [Test]
    public async Task TheSecondarySocksPortIsThreeAboveTheListenPortWhereNoCoreListens()
    {
        // Psiphon inside the tunnel and Tor around it take the most ports: the listen port and the two above it.
        var most = Profile(extra => extra with
        {
            AetherProtocol = AetherProtocol.Masque.Type,
            AetherPsiphon = AetherPsiphon.Chain.Type,
            AetherTor = AetherTor.Reverse.Type,
        });
        await AetherPorts.DefaultListenPort.Should().BeEqualTo(10819);
        await AetherPorts.SecondarySocksPortOf(AetherPorts.DefaultListenPort).Should().BeEqualTo(10822);
        await ShouldBe(AetherCore.Of(most, ListenPort).Ports.Order().ToList(), 10819, 10820, 10821);
        await AetherPorts.SecondarySocksPortOf(20808).Should().BeEqualTo(20811);
        await ShouldBe(AetherCore.Of(most, 20808).Ports.Order().ToList(), 20808, 20809, 20810);
    }

    [Test]
    public async Task TheHttpProxiesACommandWrittenByHandOpensAreItsPortsToo()
    {
        var tor = OfCommand("aether --wg --tor --bind 127.0.0.1:10819 --tor-bind 127.0.0.1:10820 "
            + "--http-proxy 127.0.0.1:8118 --tor-http 127.0.0.1:8119");
        await ShouldBe(tor.Ports, 10819, 10820, 8118, 8119);
        var psiphon = OfCommand("aether --wg --psiphon --bind 127.0.0.1:10819 --psiphon-bind 127.0.0.1:10821 --psiphon-http 127.0.0.1:8120");
        await ShouldBe(psiphon.Ports, 10819, 10821, 8120);
    }

    [Test]
    public async Task TwoListenersOfACommandOnOnePortAreFound()
    {
        // An HTTP proxy on the port the core listens on, which a command naming no listener gets there.
        await OfCommand("aether --wg --http-proxy 127.0.0.1:10819").SharedPort.Should().BeEqualTo(10819);
        // Tor inside the tunnel on the core's own port.
        await OfCommand("aether --masque --tor --tor-bind 127.0.0.1:10820 --bind 127.0.0.1:10820").SharedPort.Should().BeEqualTo(10820);
        // A carrier alone serves the core's own listener; the core's own HTTP proxy, bound as it starts, and the carrier's,
        // bound once that one is let go, may share a port. The core runs nothing else.
        var torAlone = OfCommand("aether --tor-only --bind 127.0.0.1:10819 --http-proxy 127.0.0.1:8118 --tor-http 127.0.0.1:8118 "
                                 + "--psiphon --psiphon-bind 127.0.0.1:10819 --psiphon-http 127.0.0.1:8120");
        await torAlone.SharedPort.Should().BeNull();
        await ShouldBe(torAlone.Ports, 10819, 8118);
        // Tor's listener of a command that runs no Tor binds nothing: the command names one listener, not two.
        var torOff = OfCommand("aether --masque --tor-bind 127.0.0.1:10819 --tor-http 127.0.0.1:10820");
        await torOff.SharedPort.Should().BeNull();
        await ShouldBe(torOff.Ports, 10819);
        // Each with a port of its own; Psiphon's 0 around the tunnel is none.
        await OfCommand("aether --wg --bind 127.0.0.1:10819 --http-proxy 127.0.0.1:8118").SharedPort.Should().BeNull();
        await OfCommand("aether --masque --psiphon-reverse --psiphon-bind 127.0.0.1:0 --psiphon-http 127.0.0.1:0 --bind 127.0.0.1:10819")
            .SharedPort.Should().BeNull();
        // The settings give each listener a port of its own.
        var most = Profile(extra => extra with
        {
            AetherProtocol = AetherProtocol.Masque.Type,
            AetherPsiphon = AetherPsiphon.Chain.Type,
            AetherTor = AetherTor.Reverse.Type,
        });
        await AetherCore.Of(most, ListenPort).SharedPort.Should().BeNull();
    }

    [Test]
    public async Task AListenerACommandDoesNotNameListensOnTheCoresDefaultPort()
    {
        // Tor inside the tunnel gets the app's port; the core's own listener, left out, is on 1819 all the same.
        await ShouldBe(OfCommand("aether --wg --tor").Ports, 1819, 10819);
        // Tor's and Psiphon's listeners around the tunnel, left out, are on 1820 and 1821.
        await ShouldBe(OfCommand("aether --masque --tor-reverse --bind 127.0.0.1:10819").Ports, 10819, 1820);
        await ShouldBe(OfCommand("aether --masque --psiphon-reverse --bind 127.0.0.1:10819").Ports, 10819, 1821);
        // A default the command does not see can be the port it names for another listener.
        await OfCommand("aether --wg --tor --tor-bind 127.0.0.1:1819").SharedPort.Should().BeEqualTo(1819);
        await OfCommand("aether --masque --tor-reverse --bind 127.0.0.1:1820").SharedPort.Should().BeEqualTo(1820);
        // An HTTP proxy left out is off, and a listener of a mode that does not run binds nothing.
        await ShouldBe(OfCommand("aether --wg --bind 127.0.0.1:10819").Ports, 10819);
        // The commands of the app name every listener they run.
        await ShouldBe(AetherCore.Of(Copy(Pinned(), extra => extra with { AetherTor = "chain" }), ListenPort).Ports, 10820, 10819);
    }

    [Test]
    public async Task AloneTheCoreHoldsItsOwnHttpProxyAsItStartsAndThenTheCarriersOne()
    {
        // The core binds its own HTTP proxy as it starts, beside its own listener: it counts, and cannot share that one.
        await ShouldBe(OfCommand("aether --tor-only --bind 127.0.0.1:10819 --http-proxy 127.0.0.1:8118").Ports, 10819, 8118);
        await OfCommand("aether --tor-only --bind 127.0.0.1:10819 --http-proxy 127.0.0.1:10819").SharedPort.Should().BeEqualTo(10819);
        // It lets it go before Tor's HTTP proxy is bound, so those two may share a port; Tor's and the core's own listener may not.
        await OfCommand("aether --tor-only --bind 127.0.0.1:10819 --http-proxy 127.0.0.1:8118 --tor-http 127.0.0.1:8118")
            .SharedPort.Should().BeNull();
        await OfCommand("aether --psiphon-only --bind 127.0.0.1:10819 --psiphon-http 127.0.0.1:10819").SharedPort.Should().BeEqualTo(10819);
    }

    [Test]
    public async Task AListenerWhoseAddressTheCoreDoesNotTakeIsFound()
    {
        await OfCommand("aether --wg --bind 127.0.0.1:10819 --http-proxy [::1]:8118").InvalidListener.Should().BeNull();
        // The core would quietly listen on its default instead, where the app would dial 20808.
        await OfCommand("aether --wg --bind localhost:20808").InvalidListener.Should().BeEqualTo("--bind");
        await OfCommand("aether --masque --tor-reverse --bind 127.0.0.1:10819 --tor-bind 1830").InvalidListener
            .Should().BeEqualTo(AetherArguments.TorBind);
        // The core reads its own listener's address as written, and the others trimmed.
        await OfCommand("aether --wg --bind ' 127.0.0.1:10819'").InvalidListener.Should().BeEqualTo("--bind");
        await OfCommand("aether --wg --bind 127.0.0.1:10819 --http-proxy ' 127.0.0.1:8118 '").InvalidListener.Should().BeNull();
        // An HTTP proxy written blank or "off" is off, and a listener of a mode that does not run binds nothing.
        await OfCommand("aether --wg --tor --bind 127.0.0.1:10820 --tor-bind 127.0.0.1:10819 --tor-http off").InvalidListener.Should().BeNull();
        await OfCommand("aether --wg --bind 127.0.0.1:10819 --http-proxy ''").InvalidListener.Should().BeNull();
        await OfCommand("aether --wg --bind 127.0.0.1:10819 --tor-bind nowhere").InvalidListener.Should().BeNull();
        // Digits of another script make a port for the app, not for the core.
        await OfCommand("aether --wg --bind 127.0.0.1:۲۰۸۰۸").InvalidListener.Should().BeEqualTo("--bind");
        // The app's own commands write addresses the core takes.
        await AetherCore.Of(Copy(Pinned(), extra => extra with { AetherTor = "chain" }), ListenPort).InvalidListener.Should().BeNull();
    }

    [Test]
    public async Task TheListenerTheAppDialsIsWhereTheAppReachesIt()
    {
        // The app dials the core on 127.0.0.1, which a listener on 0.0.0.0 answers as well.
        await OfCommand("aether --wg --bind 127.0.0.1:10819").UnreachableListener.Should().BeNull();
        await OfCommand("aether --wg --bind 0.0.0.0:10819").UnreachableListener.Should().BeNull();
        // :: outside Windows, which opens an IPv6 socket to IPv4 as well; Windows does not.
        await OfCommand("aether --wg --bind [::]:10819").UnreachableListener.Should().BeEqualTo(OperatingSystem.IsWindows() ? "--bind" : null);
        // Another loopback address, IPv6's, or a network's: the session would stay connecting.
        await OfCommand("aether --wg --bind 127.0.0.2:10819").UnreachableListener.Should().BeEqualTo("--bind");
        await OfCommand("aether --wg --bind [::1]:10819").UnreachableListener.Should().BeEqualTo("--bind");
        await OfCommand("aether --wg --bind 192.168.1.5:10819").UnreachableListener.Should().BeEqualTo("--bind");
        // With Tor or Psiphon inside the tunnel the app dials theirs, and the core's own may listen anywhere.
        await OfCommand("aether --wg --tor --bind 192.168.1.5:10820 --tor-bind 127.0.0.1:10819").UnreachableListener.Should().BeNull();
        await OfCommand("aether --wg --tor --tor-bind [::1]:10819").UnreachableListener.Should().BeEqualTo(AetherArguments.TorBind);
        await OfCommand("aether --wg --psiphon --psiphon-bind 10.0.0.1:10819").UnreachableListener.Should().BeEqualTo(AetherArguments.PsiphonBind);
        // An HTTP proxy may listen on a network's address; nobody in the app dials it.
        await OfCommand("aether --wg --bind 127.0.0.1:10819 --http-proxy 192.168.1.5:8118").UnreachableListener.Should().BeNull();
        // An address the core does not take is another problem, and one the command leaves out the app gives on 127.0.0.1.
        await OfCommand("aether --wg --bind localhost:20808").UnreachableListener.Should().BeNull();
        await OfCommand("aether --wg --http-proxy 192.168.1.5:8118").UnreachableListener.Should().BeNull();
        await AetherCore.Of(Copy(Pinned(), extra => extra with { AetherTor = "chain" }), ListenPort).UnreachableListener.Should().BeNull();
    }

    [Test]
    public async Task TheListenerTheAppDialsIsReadTrimmedAsTheCoreReadsIt()
    {
        // Tor inside the tunnel, dialled on its listener, which the core reads trimmed: the app dials the same port.
        var tor = OfCommand("aether --masque --tor --tor-bind '127.0.0.1:10820 '");
        await tor.Port.Should().BeEqualTo(10820);
        await ShouldBe(tor.Ports, 1819, 10820);
        await tor.InvalidListener.Should().BeNull();
        await tor.UnreachableListener.Should().BeNull();
        // The core's own listener is read as written, so a space keeps the app from reading its port at all.
        await AetherCore.OfCommand("aether --wg --bind '127.0.0.1:10819 '", ListenPort).Should().BeNull();
    }

    [Test]
    public async Task TheListenersOtherThanItsOwnAreCountedTrimmedAsTheCoreReadsThem()
    {
        await ShouldBe(OfCommand("aether --wg --bind 127.0.0.1:10819 --http-proxy '127.0.0.1:8118 '").Ports, 10819, 8118);
        await OfCommand("aether --wg --bind 127.0.0.1:8118 --http-proxy '127.0.0.1:8118 '").SharedPort.Should().BeEqualTo(8118);
        // By Rust's whitespace, which is .NET's: a next line (U+0085) is trimmed, an information separator (U+001C) is not.
        await OfCommand("aether --wg --bind 127.0.0.1:10819 --http-proxy '127.0.0.1:8118\u0085'").InvalidListener.Should().BeNull();
        await OfCommand("aether --masque --tor-reverse --bind 127.0.0.1:10819 --tor-bind '127.0.0.1:9050\u001C'").InvalidListener
            .Should().BeEqualTo(AetherArguments.TorBind);
    }

    [Test]
    public async Task ARegistrationOfKeysListensOnlyWhereACarrierAroundTheTunnelDoes()
    {
        // The core registers and ends: no listener of its own, though the command names none to leave to a default.
        await ShouldBe(new AetherCore(["--register", "wg", "--wg"]).Ports);
        await ShouldBe(new AetherCore(["--register", "wg", "--wg", "--tor"]).Ports);
        // A carrier around the tunnel carries its requests on its listener, the default where none is named.
        await ShouldBe(new AetherCore(["--register", "masque", "--masque", "--tor-reverse"]).Ports, 1820);
        await ShouldBe(new AetherCore(["--register", "masque", "--masque", "--psiphon-reverse", "--psiphon-bind", "127.0.0.1:20808"]).Ports, 20808);
        // That carrier's HTTP proxy too, which it binds as it serves; the core's own is not bound.
        await ShouldBe(new AetherCore(["--register", "masque", "--masque", "--tor-reverse", "--tor-http", "127.0.0.1:9000", "--http-proxy", "127.0.0.1:9001"]).Ports,
            1820, 9000);
        await new AetherCore(["--register", "masque", "--masque", "--tor-reverse", "--tor-bind", "127.0.0.1:9000", "--tor-http", "127.0.0.1:9000"])
            .SharedPort.Should().BeEqualTo(9000);
        await ShouldBe(new AetherCore(["--register", "masque", "--masque", "--psiphon-reverse", "--psiphon-http", "127.0.0.1:9002"]).Ports, 1821, 9002);
    }

    [Test]
    public async Task TheCoreOfAProfileDialsOutThroughTheExitNodeOfItsProfile()
    {
        var masked = Copy(Pinned(), set: profile =>
        {
            profile.Finalmask = """{"tcp": [{"type": "fragment"}]}""";
            profile.DialMode = "custom";
        });
        var core = AetherCore.Of(masked, ListenPort);
        await core.Exit.Should().BeEqualTo(new AetherExit("""{"tcp": [{"type": "fragment"}]}""", "custom"));
        // The exit-node is no part of the command line; the same arguments with another exit-node are another core.
        await ShouldBe(core.Arguments, AetherCore.Of(Pinned(), ListenPort).Arguments.ToArray());
        await core.Should().NotBeEqualTo(AetherCore.Of(Pinned(), ListenPort));
        var blank = Copy(Pinned(), set: profile =>
        {
            profile.Finalmask = " ";
            profile.DialMode = "";
        });
        await AetherCore.Of(blank, ListenPort).Exit.Should().BeEqualTo(AetherExit.Plain);
        // A command written by hand dials out through the profile's exit-node as well; a command alone, through a plain one.
        await AetherCore.Of(Copy(masked, extra => extra with { AetherCommand = core.Command }), ListenPort).Exit.Should().BeEqualTo(core.Exit);
        await OfCommand(core.Command).Exit.Should().BeEqualTo(AetherExit.Plain);
        // Dialling out through Xray keeps its exit-node.
        await core.Through(41236).Exit.Should().BeEqualTo(core.Exit);
    }

    [Test]
    public async Task ACommandNamingNoListenerGetsOneOnTheListenPortGiven()
    {
        var command = Profile(extra => extra with { AetherCommand = "aether --wg" });
        await OfCommand("aether --wg", 20808).Port.Should().BeEqualTo(20808);
        await AetherCore.Of(command, 20808).Port.Should().BeEqualTo(20808);
        // On the port of the settings, as before.
        await OfCommand("aether --wg", 10819).Port.Should().BeEqualTo(10819);
        await AetherCore.Of(command, 10819).Port.Should().BeEqualTo(10819);
        // A listener the command names stays.
        await AetherCore.Of(Profile(extra => extra with { AetherCommand = "aether --wg --bind 127.0.0.1:30808" }), 20808).Port.Should().BeEqualTo(30808);
    }
}
