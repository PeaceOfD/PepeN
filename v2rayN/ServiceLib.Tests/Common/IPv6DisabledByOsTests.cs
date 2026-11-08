namespace ServiceLib.Tests.Common;

public class IPv6DisabledByOsTests
{
    [Test]
    [Arguments(0xFF, true)] // all but the loopback interface, the usual way to turn IPv6 off
    [Arguments(0x10, true)] // native (nontunnel) interfaces, which Wintun's adapter is
    [Arguments(0x11, true)]
    [Arguments(-1, true)] // 0xFFFFFFFF read back as a DWORD
    [Arguments(0x01, false)] // tunnel interfaces only: 6to4, ISATAP, Teredo
    [Arguments(0x20, false)] // IPv4 preferred, IPv6 still on
    [Arguments(0, false)]
    public async Task IsIPv6DisabledByComponents_ShouldFollowTheNontunnelBit(int disabledComponents, bool disabled)
    {
        await Utils.IsIPv6DisabledByComponents(disabledComponents).Should().BeEqualTo(disabled);
    }

    [Test]
    public async Task IsIPv6DisabledByComponents_ShouldKeepIPv6WithoutTheValue()
    {
        await Utils.IsIPv6DisabledByComponents(null).Should().BeFalse();
    }

    [Test]
    [Arguments("1\n", true)]
    [Arguments("2\n", true)] // the kernel takes any nonzero number as off
    [Arguments("0\n", false)]
    [Arguments("", false)]
    public async Task IsIPv6DisabledBySysctl_ShouldReadTheValue(string value, bool disabled)
    {
        await Utils.IsIPv6DisabledBySysctl(value).Should().BeEqualTo(disabled);
    }
}
