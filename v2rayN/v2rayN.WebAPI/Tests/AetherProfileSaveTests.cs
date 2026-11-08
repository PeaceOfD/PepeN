using ServiceLib.Enums;
using ServiceLib.Models.Entities;
using v2rayN.WebAPI.Contracts;
using v2rayN.WebAPI.Services;

namespace v2rayN.WebAPI.Tests;

// PattN: the API has no editor for the settings of an Aether profile, so it refuses to create or change one with a code
// of its own, before anything else is read or written, instead of the misleading failure of the checks other types get.
public class AetherProfileSaveTests
{
    [Test]
    [Arguments(null)]
    [Arguments("existing-aether-profile")]
    public async Task SavingAnAetherProfileIsRefusedAsDesktopOnly(string? profileId)
    {
        var runtime = new V2rayRuntime(null!, null!, null!, null!, null!);
        var profile = new ProfileItem
        {
            ConfigType = EConfigType.Aether,
            Remarks = "WARP",
            Address = "162.159.198.1",
            Port = 443,
        };

        var result = await runtime.SaveProfileAsync(profile, profileId);

        await result.Success.Should().BeFalse();
        await result.Code.Should().BeEqualTo("profile_type_desktop_only");
        await result.MessageKey.Should().BeEqualTo(ApiMessageKeys.ProfileTypeDesktopOnly);
        await ApiMessageKeys.ProfileTypeDesktopOnly.Should().BeEqualTo("profiles.typeDesktopOnly");
    }
}
