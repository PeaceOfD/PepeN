namespace ServiceLib.Tests.Manager;

/// <summary>
/// PattN: the Aether core as the check-update page sees it: its release archives on patterniha/Aether (the names of the
/// fork's release.yml), the systems that are offered one, and how its version is read.
/// </summary>
public class CoreInfoManagerAetherTests
{
    private const string Releases = "https://github.com/patterniha/Aether/releases";

    private static CoreInfo Aether => CoreInfoManager.Instance.GetCoreInfo(ECoreType.aether)!;

    private static OSPlatform PlatformOf(string os) => os switch
    {
        "windows" => OSPlatform.Windows,
        "linux" => OSPlatform.Linux,
        "macos" => OSPlatform.OSX,
        _ => OSPlatform.Create(os),
    };

    [Test]
    public async Task TheCoreIsTheForksAetherOutOfReachOfUpstreamValues()
    {
        await ((int)ECoreType.aether).Should().BeEqualTo(500);
        await Global.CoreUrls[ECoreType.aether].Should().BeEqualTo("patterniha/Aether");
        await Aether.Url.Should().BeEqualTo(Releases);
        await Aether.ReleaseApiUrl.Should().BeEqualTo("https://api.github.com/repos/patterniha/Aether/releases");
        await Aether.CoreExes!.SequenceEqual(["aether"]).Should().BeTrue();
        await Aether.VersionArg.Should().BeEqualTo("--version");
        await Aether.Match.Should().BeEqualTo("aether");
        await Aether.Environment.Count.Should().BeEqualTo(0);
    }

    [Test]
    [Arguments("windows", Architecture.X64, "aether-windows-x86_64.zip")]
    // The fork builds no arm64 Windows core; Windows 11 on ARM runs the x64 one (Windows 10 on ARM is offered none:
    // ASystemIsOfferedTheCoreOnlyWhereItRuns).
    [Arguments("windows", Architecture.Arm64, "aether-windows-x86_64.zip")]
    // The static musl builds, not the glibc ones.
    [Arguments("linux", Architecture.X64, "aether-linux-x86_64-musl.tar.gz")]
    [Arguments("linux", Architecture.Arm64, "aether-linux-aarch64-musl.tar.gz")]
    [Arguments("macos", Architecture.X64, "aether-macos-x86_64.tar.gz")]
    [Arguments("macos", Architecture.Arm64, "aether-macos-arm64.tar.gz")]
    public async Task EachSystemWithAReleaseArchiveDownloadsIt(string os, Architecture architecture, string archive)
    {
        var url = CoreInfoManager.GetDownloadUrl(Aether, PlatformOf(os), architecture);

        // UpdateService.ParseDownloadUrl fills in the tag as the release names it.
        await string.Format(url!, "v2.3.2").Should().BeEqualTo($"{Releases}/download/v2.3.2/{archive}");
    }

    [Test]
    [Arguments("linux", Architecture.RiscV64)]
    [Arguments("linux", Architecture.LoongArch64)]
    [Arguments("linux", Architecture.Arm)]
    [Arguments("windows", Architecture.X86)]
    [Arguments("macos", Architecture.X86)]
    [Arguments("freebsd", Architecture.X64)]
    public async Task ASystemWithoutAReleaseArchiveIsOfferedNone(string os, Architecture architecture)
    {
        await CoreInfoManager.GetDownloadUrl(Aether, PlatformOf(os), architecture).Should().BeNull();
    }

    [Test]
    [Arguments("windows", Architecture.Arm64, 22000, true)]
    [Arguments("windows", Architecture.Arm64, 26100, true)]
    // Windows 10 on ARM cannot run the x64 core.
    [Arguments("windows", Architecture.Arm64, 19045, false)]
    [Arguments("windows", Architecture.X64, 19045, true)]
    [Arguments("linux", Architecture.Arm64, 0, true)]
    [Arguments("linux", Architecture.RiscV64, 0, false)]
    [Arguments("macos", Architecture.Arm64, 0, true)]
    public async Task ASystemIsOfferedTheCoreOnlyWhereItRuns(string os, Architecture architecture, int windowsBuild, bool offered)
    {
        await CoreInfoManager.IsAetherDownloadable(Aether, PlatformOf(os), architecture, windowsBuild).Should().BeEqualTo(offered);
    }

    [Test]
    public async Task TheUpdateRowIsListedWhereAnArchiveExists()
    {
        var listed = CoreInfoManager.Instance.GetCheckUpdateCoreTypes().Contains(ECoreType.aether);
        var downloadable = CoreInfoManager.Instance.IsAetherDownloadable();
        var desktop = OperatingSystem.IsWindows() || OperatingSystem.IsLinux() || OperatingSystem.IsMacOS();
        var windows10OnArm = OperatingSystem.IsWindows() && RuntimeInformation.ProcessArchitecture == Architecture.Arm64
            && Environment.OSVersion.Version.Build < 22000;

        await downloadable.Should().BeEqualTo(desktop && !windows10OnArm && RuntimeInformation.ProcessArchitecture is Architecture.X64 or Architecture.Arm64);
        // Like the other cores, never on Windows before 10.
        await listed.Should().BeEqualTo(downloadable && !(OperatingSystem.IsWindows() && Environment.OSVersion.Version.Major < 10));
    }

    [Test]
    public async Task TheCoreChecksForUpdatesWithoutPreReleases()
    {
        await CoreInfoManager.Instance.IsCheckUpdateSupported(ECoreType.aether).Should().BeTrue();
        await CoreInfoManager.Instance.GetCheckPreRelease(ECoreType.aether, true).Should().BeFalse();
    }

    [Test]
    public async Task TheVersionTheCorePrintsIsRead()
    {
        // UpdateService reads the output of "aether --version" with its SemVerRegex.
        var semVer = (Regex)typeof(UpdateService).GetMethod("SemVerRegex", BindingFlags.NonPublic | BindingFlags.Static)!.Invoke(null, null)!;
        var version = semVer.Match("aether 2.3.2\n").Groups["version"].Value;

        await version.Should().BeEqualTo("2.3.2");
        // The installed version is compared with the tag of the latest release.
        await (new SemanticVersion(version) >= new SemanticVersion("v2.3.2")).Should().BeTrue();
        await (new SemanticVersion(version) >= new SemanticVersion("v2.3.3")).Should().BeFalse();
    }

    [Test]
    public async Task TheUpdateRowReadsAetherCoreAndIsStoredAsAether()
    {
        var row = new CheckUpdateModel { CoreType = ECoreType.aether };

        await row.CoreTypeDisplay.Should().BeEqualTo("aether-core");
        await row.CoreTypeForStorage.Should().BeEqualTo("aether");
    }
}
