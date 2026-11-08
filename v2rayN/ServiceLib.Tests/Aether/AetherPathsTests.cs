using TUnit.Assertions.Enums;

namespace ServiceLib.Tests.Aether;

// PattN: the names of the folders and files PattN and the core share, against the rules the core names them by
// (aether/src/psiphon.rs, tor.rs) and PattNG's names. Literals, never the constants, so that a renamed constant fails
// here. The paths themselves are left alone: reading them creates guiConfigs. The moving and clearing of these folders
// are tested with the code that does it (PattNG AetherCoreManagerTest's Psiphon datastore tests).
public class AetherPathsTests
{
    [Test]
    public async Task TheCoreDerivesItsOwnStateFoldersFromTheBaseFile()
    {
        await AetherPaths.BaseFile.Should().BeEqualTo("aether.toml");
        // psiphon.rs: format!("{stem}-psiphon") of the base config path, the whole file name.
        await AetherPaths.PsiphonInsideDirName.Should().BeEqualTo("aether.toml-psiphon");
        // tor.rs: "{stem}-tor" of the file stem.
        await AetherPaths.TorStateDirName.Should().BeEqualTo("aether-tor");
    }

    [Test]
    public async Task ThePsiphonDatastoresOfTheSessionAndOfTheProbesAreApart()
    {
        // Both are folders of guiConfigs beside the working folder; a second client on the session's datastore would
        // give up after a second.
        await AetherPaths.PsiphonStateDirName.Should().BeEqualTo("psiphon");
        await AetherPaths.PsiphonProbeDirName.Should().BeEqualTo("psiphon-probe");
    }

    [Test]
    public async Task TheKeyFilesAreNamedAsTheCoreNamesThemInTheOrderItRegistersThem()
    {
        await AetherPaths.KeyFiles.Should().BeEquivalentTo(
            ["aether-wg.toml", "aether-wg-secondary.toml", "aether-masque.toml", "aether-masque-secondary.toml", "aether-masque-gool.toml"],
            CollectionOrdering.Matching);
        await AetherPaths.WorkDirName.Should().BeEqualTo("aether");
        await AetherPaths.RenewalDirName.Should().BeEqualTo("aether-renewal");
        await AetherPaths.ReadyMark.Should().BeEqualTo("ready");
        await AetherPaths.WireGuardLastConnectionFile.Should().BeEqualTo("aether-wg-lastconn.toml");
        await AetherPaths.MasqueLastConnectionFile.Should().BeEqualTo("aether-masque-lastconn.toml");
    }

    [Test]
    public async Task ThePsiphonServerListIsKeptUnderPsiphonsNames()
    {
        await AetherPaths.PsiphonServersFileName.Should().BeEqualTo("psiphon_servers.dat");
        await AetherPaths.PsiphonServersStampFileName.Should().BeEqualTo("psiphon_servers.stamp");
        await AetherPaths.PsiphonEntriesFileName.Should().BeEqualTo("psiphon-servers.txt");
        await AetherPaths.PsiphonEntriesSourceFileName.Should().BeEqualTo("psiphon-servers.source");
    }
}
