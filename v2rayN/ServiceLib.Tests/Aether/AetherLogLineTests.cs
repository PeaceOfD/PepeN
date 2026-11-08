namespace ServiceLib.Tests.Aether;

public class AetherLogLineTests
{
    [Test]
    public async Task CoreOutputKeepsItsLogLevel()
    {
        await AetherLogLine.OutputPriority("[2026-09-11T10:00:00.000Z ERROR aether] config parse failed").Should().BeEqualTo(AetherLogPriority.Error);
        await AetherLogLine.OutputPriority("[2026-09-11T10:00:00.000Z WARN  aether] [-] tunnel ended").Should().BeEqualTo(AetherLogPriority.Warn);
        await AetherLogLine.OutputPriority("[2026-09-11T10:00:00.000Z INFO  aether] [+] identity ready").Should().BeEqualTo(AetherLogPriority.Info);
        await AetherLogLine.OutputPriority("[2026-09-11T10:00:00.000Z DEBUG aether::quic] packet").Should().BeEqualTo(AetherLogPriority.Debug);
        await AetherLogLine.OutputPriority("[2026-09-11T10:00:00.000Z TRACE aether] packet").Should().BeEqualTo(AetherLogPriority.Debug);
    }

    [Test]
    public async Task AFatalErrorFromTheCoreIsAnError()
    {
        await AetherLogLine.OutputPriority("Error: Api(\"too many registrations\")").Should().BeEqualTo(AetherLogPriority.Error);
    }

    [Test]
    public async Task TheLogHeaderIsStrippedFromCoreOutput()
    {
        await AetherLogLine.OutputMessage("[2026-09-11T10:00:00.000Z INFO  aether::prober] [+] candidate ok 162.159.197.3:443 rtt=84ms")
            .Should().BeEqualTo("[+] candidate ok 162.159.197.3:443 rtt=84ms");
        await AetherLogLine.OutputMessage("[+] selected protocol: MASQUE").Should().BeEqualTo("[+] selected protocol: MASQUE");
        await AetherLogLine.OutputMessage("  fallback over tcp 443  ").Should().BeEqualTo("fallback over tcp 443");
        await AetherLogLine.OutputMessage("   ").Should().BeEqualTo("");
    }

    [Test]
    public async Task OutputWithoutALogHeaderIsInformational()
    {
        await AetherLogLine.OutputPriority("  fallback over tcp 443 (--masque-http2) on this network.").Should().BeEqualTo(AetherLogPriority.Info);
        await AetherLogLine.OutputPriority("[unterminated header").Should().BeEqualTo(AetherLogPriority.Info);
        await AetherLogLine.OutputPriority("[]").Should().BeEqualTo(AetherLogPriority.Info);
    }

    // PattNG's test also checks AetherCoreManager.readyNeedsWord and showsInfo, which AetherArguments ports; only the
    // ready word is here.
    [Test]
    public async Task WhereTheAppDialsPsiphonReadinessNeedsTheCoresWord()
    {
        await AetherLogLine.IsReadyWord("[2026-09-24T10:00:00.000Z INFO  aether] [+] psiphon is ready; 127.0.0.1:10819 leaves through psiphon, carried by the tunnel").Should().BeTrue();
        await AetherLogLine.IsReadyWord("[2026-09-24T10:00:00.000Z INFO  aether] [+] psiphon is ready; 127.0.0.1:10819 leaves through psiphon").Should().BeTrue();
        await AetherLogLine.IsReadyWord("[2026-09-24T10:00:00.000Z INFO  aether] [*] starting psiphon through the tunnel at 127.0.0.1:10820").Should().BeFalse();
        await AetherLogLine.IsReadyWord("[2026-09-24T10:00:00.000Z INFO  aether] [+] psiphon reached a server at 203.0.113.9").Should().BeFalse();
    }

    // PattNG's test also checks AetherCoreManager.stoppedMessage, a string resource of a later stage; only the word and
    // the level of the line are here.
    [Test]
    public async Task ACoreThatStoppedForWantOfAnEchKeyIsReportedSo()
    {
        // The core's last line as its main prints the error it ends with (fork aether/src/lib.rs, session_ech_key).
        const string stopped = "Error: Ech(\"ECH is on but there is no ECH key to offer (udp://1.1.1.1:53 did not answer for "
                               + "cloudflare-ech.com); stopping rather than send the server name in the clear\")";
        await AetherLogLine.IsNoEchKeyWord(stopped).Should().BeTrue();
        await AetherLogLine.OutputPriority(stopped).Should().BeEqualTo(AetherLogPriority.Error);
        await AetherLogLine.IsNoEchKeyWord("[2026-10-01T10:00:00.000Z INFO  aether] [+] fetched ECHConfigList automatically (71 bytes)").Should().BeFalse();
        await AetherLogLine.IsNoEchKeyWord("[2026-10-01T10:00:00.000Z INFO  aether] [+] ECH off; the server name goes out in cleartext").Should().BeFalse();
        await AetherLogLine.IsNoEchKeyWord("Error: Api(\"too many registrations\")").Should().BeFalse();
    }

    [Test]
    public async Task TheEndOfTorOrPsiphonInsideTheTunnelIsToldFromTheirOtherLines()
    {
        string[] tor = ["--masque", "--tor", "--tor-bind", "127.0.0.1:10819", "--bind", "127.0.0.1:10820"];
        string[] psiphon = ["--masque", "--psiphon", "--psiphon-bind", "127.0.0.1:10819", "--bind", "127.0.0.1:10820"];
        // The core's lines as an aether-core before 2.3.4 writes them (aether lib.rs): an error's words start with the label
        // of its kind, as AetherError::Other shows it.
        const string torText = "the tor socks5 listener cannot use 127.0.0.1:10819: Address already in use (os error 98); "
                               + "another program already listens there, so stop it or choose another port with --bind";
        const string torEnded = "[2026-10-23T10:00:00.000Z ERROR aether] [-] tor: other: " + torText;
        await AetherLogLine.ChainEndWords(torEnded, tor).Should().BeEqualTo("tor: " + torText);
        await AetherLogLine.ChainEndWords("[2026-10-23T10:00:00.000Z ERROR aether] [-] tor: io: Connection reset by peer (os error 104)", tor)
            .Should().BeEqualTo("tor: Connection reset by peer (os error 104)");
        // As a PattNG device logged it on 2026-09-24.
        await AetherLogLine.ChainEndWords(
                "[2026-09-24T16:12:36.142Z ERROR aether] [-] psiphon: other: psiphon did not come up in time; it may be blocked on this network", psiphon)
            .Should().BeEqualTo("psiphon: psiphon did not come up in time; it may be blocked on this network");
        await AetherLogLine.ChainEndWords("[2026-10-23T10:00:00.000Z ERROR aether] [-] psiphon: other: psiphon stopped: exit status: 1", psiphon)
            .Should().BeEqualTo("psiphon: psiphon stopped: exit status: 1");
        // The notices of the Psiphon client come from the core's psiphon module, under the same prefix, at any level.
        await AetherLogLine.ChainEndWords(
                "[2026-10-23T10:00:00.000Z ERROR aether::psiphon] [-] psiphon: error initializing local SOCKS proxy: address already in use", psiphon)
            .Should().BeNull();
        await AetherLogLine.ChainEndWords("[2026-10-23T10:00:00.000Z ERROR aether::psiphon] [-] psiphon: psiphon.(*Controller).Run#412: tunnel failed", psiphon)
            .Should().BeNull();
        await AetherLogLine.ChainEndWords("[2026-10-23T10:00:00.000Z WARN  aether::psiphon] [-] psiphon lost its tunnel; it is reconnecting", psiphon)
            .Should().BeNull();
        // Not at the error level, Tor's word for a session on Psiphon's listener, a session on the core's own listener.
        await AetherLogLine.ChainEndWords(torEnded.Replace("ERROR", "INFO "), tor).Should().BeNull();
        await AetherLogLine.ChainEndWords(torEnded, psiphon).Should().BeNull();
        await AetherLogLine.ChainEndWords(torEnded, ["--masque", "--bind", "127.0.0.1:10819"]).Should().BeNull();
        // Psiphon around the tunnel ends the tunnel with it, and the core's own listener answers no more: no leak to look for.
        string[] reverse = ["--masque", "--psiphon-reverse", "--bind", "127.0.0.1:10819"];
        await AetherLogLine.ChainEndWords("[2026-10-23T10:00:00.000Z ERROR aether::psiphon] [-] psiphon: other: psiphon stopped: exit status: 1", reverse)
            .Should().BeNull();
        // The error the core stops on is its own exit, not a chain that ended under a running core.
        await AetherLogLine.ChainEndWords("Error: Other(\"[-] tor: ...\")", tor).Should().BeNull();
    }

    // PattN only (design 2.6.1): the Tor logs of the core can carry colour codes even when piped.
    [Test]
    public async Task AnsiColourCodesAreStrippedFromCoreOutput()
    {
        const string coloured = "\u001b[2m2026-10-20T10:00:00.123456Z\u001b[0m \u001b[32m INFO\u001b[0m \u001b[2marti_client\u001b[0m: bootstrapped";
        await AetherLogLine.StripAnsi(coloured).Should().BeEqualTo("2026-10-20T10:00:00.123456Z  INFO arti_client: bootstrapped");
        await AetherLogLine.StripAnsi("[+] psiphon is ready").Should().BeEqualTo("[+] psiphon is ready");
    }
}
