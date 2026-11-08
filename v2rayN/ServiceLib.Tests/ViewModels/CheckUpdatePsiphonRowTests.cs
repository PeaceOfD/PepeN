using ServiceLib.Tests.CoreConfig;
using ServiceLib.ViewModels;

namespace ServiceLib.Tests.ViewModels;

/// <summary>
/// PattN (design 3.6, critiques 16, 26 and 28): the row of the Psiphon server list in the check-update window. It has no
/// core type, as the geo files' row has none, so a flag of its own tells the two apart: each is stored, selected, shown
/// and finished by itself. It is listed only where an Aether core is offered. An update of it alone stops no core and
/// reloads nothing; the next core start unpacks the new list (design 2.11). Any other row finished alongside it still has
/// the cores stopped and reloaded.
/// </summary>
public class CheckUpdatePsiphonRowTests
{
    private static CheckUpdateModel Psiphon()
    {
        return new CheckUpdateModel { IsPsiphonServers = true, IsFinished = true };
    }

    [Test]
    public async Task APsiphonOnlyUpdateDoesNotStopTheCores()
    {
        await CheckUpdateViewModel.UpdateRestartsCores([Psiphon()]).Should().BeFalse();
        await CheckUpdateViewModel.UpdateRestartsCores([]).Should().BeFalse();
    }

    [Test]
    public async Task AnyOtherRowStillStopsAndReloadsTheCores()
    {
        var geo = new CheckUpdateModel { IsGeoFile = true, IsFinished = true };
        var aether = new CheckUpdateModel { CoreType = ECoreType.aether, IsFinished = true };
        var xray = new CheckUpdateModel { CoreType = ECoreType.Xray, IsFinished = true };

        await CheckUpdateViewModel.UpdateRestartsCores([geo]).Should().BeTrue();
        await CheckUpdateViewModel.UpdateRestartsCores([Psiphon(), aether]).Should().BeTrue();
        await CheckUpdateViewModel.UpdateRestartsCores([xray, Psiphon()]).Should().BeTrue();
        // The geo files' row and the Psiphon row both have no core type; only the flag tells them apart.
        await geo.CoreType.Should().BeNull();
        await Psiphon().CoreType.Should().BeNull();
    }

    [Test]
    public async Task ThePsiphonRowIsStoredAndShownByANameOfItsOwn()
    {
        var psiphon = Psiphon();
        var geo = new CheckUpdateModel { IsGeoFile = true };
        var aether = new CheckUpdateModel { CoreType = ECoreType.aether };

        await psiphon.CoreTypeForStorage.Should().BeEqualTo("PsiphonServers");
        await psiphon.CoreTypeDisplay.Should().BeEqualTo("psiphon_servers");
        await CheckUpdateModel.PsiphonServersStorageName.Should().BeEqualTo("PsiphonServers");
        await geo.CoreTypeForStorage.Should().BeEqualTo("GeoFiles");
        await geo.CoreTypeDisplay.Should().BeEqualTo("GeoFiles");
        await aether.CoreTypeForStorage.Should().BeEqualTo("aether");
        await aether.CoreTypeDisplay.Should().BeEqualTo("aether-core");
    }

    [Test]
    public async Task TheGeoFilesRowAndThePsiphonRowFinishApart()
    {
        var xray = new CheckUpdateModel { CoreType = ECoreType.Xray };
        var geo = new CheckUpdateModel { IsGeoFile = true };
        var psiphon = Psiphon();

        // Whichever comes first, neither of the two rows without a core type stands for the other.
        foreach (var rows in new[] { new[] { xray, geo, psiphon }, new[] { psiphon, geo, xray } })
        {
            await CheckUpdateViewModel.RowOf(rows, null).Should().BeSameReferenceAs(geo);
            await CheckUpdateViewModel.RowOf(rows, null, isPsiphonServers: true).Should().BeSameReferenceAs(psiphon);
            await CheckUpdateViewModel.RowOf(rows, ECoreType.Xray).Should().BeSameReferenceAs(xray);
        }
        await CheckUpdateViewModel.RowOf([xray, psiphon], null).Should().BeNull();
        await CheckUpdateViewModel.RowOf([xray, geo], null, isPsiphonServers: true).Should().BeNull();
    }

    [Test]
    [NotInParallel("AppManager config")]
    public async Task ThePsiphonRowIsListedWhereAnAetherCoreIsAndShowsItsOwnMessages()
    {
        var window = Window(selected: null);
        var rows = window.CheckUpdateModels.ToList();
        var geo = rows.Single(row => row.IsGeoFile);
        var psiphonRows = rows.Where(row => row.IsPsiphonServers).ToList();

        // Hidden where no Aether core is offered (critique 28): the two rows come together.
        var offersAether = rows.Any(row => row.CoreType == ECoreType.aether);
        await offersAether.Should().BeEqualTo(CoreInfoManager.Instance.GetCheckUpdateCoreTypes().Contains(ECoreType.aether));
        await psiphonRows.Count.Should().BeEqualTo(offersAether ? 1 : 0);
        if (!offersAether)
        {
            return;
        }
        var psiphon = psiphonRows[0];
        await psiphon.CoreType.Should().BeNull();
        await psiphon.IsGeoFile.Should().BeFalse();
        await psiphon.IsSelected.Should().BeEqualTo(true);
        await psiphon.ShowCheckPreRelease.Should().BeFalse();
        await psiphon.CoreTypeDisplay.Should().BeEqualTo("psiphon_servers");

        await window.UpdateViewResult(new CheckUpdateModel { IsPsiphonServers = true, Remarks = "psiphon" });
        await psiphon.Remarks.Should().BeEqualTo("psiphon");
        await geo.Remarks.Should().BeEqualTo(ResUI.menuCheckUpdate);

        await window.UpdateViewResult(new CheckUpdateModel { IsGeoFile = true, Remarks = "geo" });
        await geo.Remarks.Should().BeEqualTo("geo");
        await psiphon.Remarks.Should().BeEqualTo("psiphon");
    }

    [Test]
    [NotInParallel("AppManager config")]
    public async Task ThePsiphonRowIsSelectedByItsStorageNameAlone()
    {
        if (!CoreInfoManager.Instance.GetCheckUpdateCoreTypes().Contains(ECoreType.aether))
        {
            return;
        }

        var psiphonOnly = Window(selected: ["PsiphonServers"]).CheckUpdateModels.ToList();
        await psiphonOnly.Single(row => row.IsPsiphonServers).IsSelected.Should().BeEqualTo(true);
        await psiphonOnly.Single(row => row.IsGeoFile).IsSelected.Should().BeEqualTo(false);

        // A list saved before the row existed leaves it unselected (critique 26, accepted: no migration).
        var geoOnly = Window(selected: ["GeoFiles"]).CheckUpdateModels.ToList();
        await geoOnly.Single(row => row.IsPsiphonServers).IsSelected.Should().BeEqualTo(false);
        await geoOnly.Single(row => row.IsGeoFile).IsSelected.Should().BeEqualTo(true);
    }

    /// <summary>The check-update window on settings whose saved row list is <paramref name="selected" />.</summary>
    private static CheckUpdateViewModel Window(List<string>? selected)
    {
        var config = CoreConfigTestFactory.CreateConfig();
        config.CheckUpdateItem = new CheckUpdateItem { SelectedCoreTypes = selected };
        return ViewModelConfig.With(config, () => new CheckUpdateViewModel());
    }
}
