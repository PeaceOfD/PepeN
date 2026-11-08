using ServiceLib.Tests.CoreConfig;

namespace ServiceLib.Tests.Handler;

public class NodeValidatorEchOutboundTests
{
    private const string EchConfigList = "cloudflare-ech.com+https://1.1.1.1/dns-query";

    [Test]
    [Arguments("")]
    [Arguments("  \n")]
    public async Task ValidateEchOutbound_Empty_ShouldPass(string echOutbound)
    {
        await NodeValidator.ValidateEchOutbound(CreateNode(echOutbound)).Should().BeNull();
    }

    [Test]
    [Arguments("freedom")]
    [Arguments("""[{"tag": "ech-out"}]""")]
    [Arguments("{\"tag\": \"ech-out\", \"protocol\": \"freedom\"")]
    public async Task ValidateEchOutbound_NotJsonObject_ShouldFail(string echOutbound)
    {
        await NodeValidator.ValidateEchOutbound(CreateNode(echOutbound))
            .Should().BeEqualTo(string.Format(ResUI.MsgInvalidProperty, ResUI.TbEchOutbound));
    }

    [Test]
    public async Task ValidateEchOutbound_WithoutEchConfigList_ShouldFail()
    {
        var node = CreateNode("""{"tag": "ech-out", "protocol": "freedom"}""");
        node.EchConfigList = string.Empty;

        await NodeValidator.ValidateEchOutbound(node).Should().BeEqualTo(ResUI.MsgEchOutboundNeedsEchConfigList);
    }

    [Test]
    [Arguments("""{"protocol": "freedom"}""")]
    [Arguments("""{"tag": "", "protocol": "freedom"}""")]
    [Arguments("""{"tag": " ", "protocol": "freedom"}""")]
    [Arguments("""{"tag": 1, "protocol": "freedom"}""")]
    [Arguments("""{"tag": "direct", "protocol": "freedom"}""")]
    [Arguments("""{"tag": "block", "protocol": "freedom"}""")]
    [Arguments("""{"tag": "proxy", "protocol": "freedom"}""")]
    [Arguments("""{"tag": "proxy2", "protocol": "freedom"}""")]
    [Arguments("""{"tag": "proxy-ech", "protocol": "freedom"}""")]
    public async Task ValidateEchOutbound_MissingOrReservedTag_ShouldFail(string echOutbound)
    {
        await NodeValidator.ValidateEchOutbound(CreateNode(echOutbound)).Should().BeEqualTo(ResUI.MsgEchOutboundInvalidTag);
    }

    [Test]
    [Arguments("""{"tag": "ech-proxy", "protocol": "freedom"}""")]
    [Arguments("""{"tag": "Proxy", "protocol": "freedom"}""")]
    [Arguments("""{"tag": "directly", "protocol": "freedom"}""")]
    public async Task ValidateEchOutbound_OtherTags_ShouldPass(string echOutbound)
    {
        // Only the exact direct/block tags and the "proxy" prefix that balancers select by are taken.
        await NodeValidator.ValidateEchOutbound(CreateNode(echOutbound)).Should().BeNull();
    }

    [Test]
    public async Task Validate_ShouldAcceptAValidEchOutboundAndRejectAnInvalidOne()
    {
        // The checks made when saving are made again when a config is generated, for imported profiles.
        var node = CreateNode("""
            {
              "tag": "ech-out",
              "protocol": "freedom"
            }
            """);

        await NodeValidator.ValidateEchOutbound(node).Should().BeNull();
        await NodeValidator.Validate(node, ECoreType.Xray).Success.Should().BeTrue();

        node.EchOutbound = """{"tag": "proxy", "protocol": "freedom"}""";
        var result = NodeValidator.Validate(node, ECoreType.Xray);

        await result.Success.Should().BeFalse();
        await result.Errors.Should().Contain(ResUI.MsgEchOutboundInvalidTag);
    }

    [Test]
    [Arguments("""{"tag": "ech-out", "protocol": "freedom", "protocol": "freedom"}""")]
    [Arguments("""{"tag": "ech-out", "protocol": "freedom", "settings": {"domainStrategy": "UseIP", "domainStrategy": "AsIs"}}""")]
    public async Task ValidateEchOutbound_RepeatedKey_ShouldFail(string echOutbound)
    {
        // Every read of a parsed object that repeats a key throws, so such an outbound is invalid.
        await NodeValidator.ValidateEchOutbound(CreateNode(echOutbound))
            .Should().BeEqualTo(string.Format(ResUI.MsgInvalidProperty, ResUI.TbEchOutbound));
        await NodeValidator.Validate(CreateNode(echOutbound), ECoreType.Xray).Success.Should().BeFalse();
    }

    [Test]
    public async Task ValidateEchOutbound_WithComments_ShouldGiveTheOutbound()
    {
        var node = CreateNode("""
            {
              // the ECH config query goes direct
              "tag": "ech-out",
              "protocol": "freedom"
            }
            """);

        var error = NodeValidator.ValidateEchOutbound(node, out var echOutbound);

        await error.Should().BeNull();
        await echOutbound.Should().NotBeNull();
        await NodeValidator.GetOutboundTag(echOutbound!).Should().BeEqualTo("ech-out");
    }

    [Test]
    [Arguments(EConfigType.VLESS, "reality")]
    [Arguments(EConfigType.VLESS, "")]
    [Arguments(EConfigType.WireGuard, "tls")]
    [Arguments(EConfigType.TUIC, "tls")]
    [Arguments(EConfigType.Anytls, "tls")]
    [Arguments(EConfigType.Naive, "tls")]
    public async Task ValidateEchOutbound_WhereItDoesNotApply_ShouldBeIgnored(EConfigType configType, string streamSecurity)
    {
        // The editor hides the field there and no config uses it, so an imported value must not block the profile.
        var node = CreateNode("""{"tag": "proxy", "protocol": "freedom"}""");
        node.ConfigType = configType;
        node.StreamSecurity = streamSecurity;

        var error = NodeValidator.ValidateEchOutbound(node, out var echOutbound);

        await error.Should().BeNull();
        await echOutbound.Should().BeNull();
    }

    [Test]
    public async Task GetOutboundTag_RepeatedTag_ShouldTakeTheLastLikeXray()
    {
        var outbound = JsonUtils.ParseJson("""{"tag": "first", "protocol": "freedom", "tag": "last"}""")!.AsObject();

        await NodeValidator.GetOutboundTag(outbound).Should().BeEqualTo("last");
    }

    private static ProfileItem CreateNode(string echOutbound)
    {
        var node = CoreConfigTestFactory.CreateVmessNode(ECoreType.Xray);
        node.StreamSecurity = Global.StreamSecurity;
        node.EchConfigList = EchConfigList;
        node.EchOutbound = echOutbound;
        return node;
    }
}
