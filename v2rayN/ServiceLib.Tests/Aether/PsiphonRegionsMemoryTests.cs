using TUnit.Assertions.Enums;

namespace ServiceLib.Tests.Aether;

/// <summary>
/// PattN (PattNG PsiphonServerList.remember, remembered, forgetRemembered; critique 33): Psiphon's last report of the
/// countries it can leave from, kept in the settings for the exit country list of the editor.
/// </summary>
public class PsiphonRegionsMemoryTests
{
    [Test]
    public async Task TheLastReportIsKeptSortedInPlaceOfTheOneBefore()
    {
        var settings = new AetherItem();
        await PsiphonServerList.Remembered(settings).Count.Should().BeEqualTo(0);
        await PsiphonServerList.Remembered(null).Count.Should().BeEqualTo(0);

        await PsiphonServerList.Remember(settings, ["US", "DE", "NL", "DE"]).Should().BeTrue();
        await settings.PsiphonRegions.Should().BeEqualTo("DE,NL,US");
        await PsiphonServerList.Remembered(settings).Should().BeEquivalentTo(["DE", "NL", "US"], CollectionOrdering.Matching);

        // The report is the whole set, so the last one is the truth.
        await PsiphonServerList.Remember(settings, ["CA"]).Should().BeTrue();
        await PsiphonServerList.Remembered(settings).Should().BeEquivalentTo(["CA"], CollectionOrdering.Matching);
    }

    [Test]
    public async Task TheSameReportAgainChangesNothingToSave()
    {
        var settings = new AetherItem();
        await PsiphonServerList.Remember(settings, ["DE", "NL"]).Should().BeTrue();

        await PsiphonServerList.Remember(settings, ["NL", "DE"]).Should().BeFalse();
    }

    [Test]
    public async Task ForgettingTheReportLeavesNoCountry()
    {
        // With the Psiphon datastore cleared, what it reported goes too.
        var settings = new AetherItem { PsiphonRegions = "DE,NL" };

        await PsiphonServerList.Remember(settings, []).Should().BeTrue();

        await PsiphonServerList.Remembered(settings).Count.Should().BeEqualTo(0);
        await PsiphonServerList.Remember(settings, []).Should().BeFalse();
    }

    [Test]
    public async Task OnlyCountryCodesAreReadBack()
    {
        var settings = new AetherItem { PsiphonRegions = "DE,,NLD,x,US" };

        await PsiphonServerList.Remembered(settings).Should().BeEquivalentTo(["DE", "US"], CollectionOrdering.Matching);
    }
}
