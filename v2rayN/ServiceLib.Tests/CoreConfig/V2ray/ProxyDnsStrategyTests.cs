namespace ServiceLib.Tests.CoreConfig.V2ray;

// PattN: the domain strategies the DNS settings give proxies reach every outbound of a type Xray runs, which they find
// by its name in Global.ProtocolTypes. A type Xray runs without a name there would leave the configuration of every
// profile without its DNS servers and DNS routing, as GenDns gives up at the missing name.
public class ProxyDnsStrategyTests
{
    [Test]
    public async Task EveryTypeXrayRunsHasAProtocolName()
    {
        var missing = string.Join(", ", Global.XraySupportConfigType.Where(type => !Global.ProtocolTypes.ContainsKey(type)));

        await missing.Should().BeEqualTo(string.Empty);
        // The outbound of an Aether profile is a SOCKS one to its core, and its name is there before Xray runs it.
        await Global.ProtocolTypes[EConfigType.Aether].Should().BeEqualTo("socks");
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task AProxyDomainStrategyKeepsTheDnsServersAndTheirRouting(bool dial)
    {
        var config = CoreConfigTestFactory.CreateConfig(ECoreType.Xray);
        config.SimpleDNSItem.DirectDNS = "1.1.1.1";
        config.SimpleDNSItem.RemoteDNS = "https://8.8.8.8/dns-query";
        if (dial)
        {
            config.SimpleDNSItem.Strategy4ProxyDial = "UseIPv4";
        }
        else
        {
            config.SimpleDNSItem.Strategy4Proxy = "UseIPv4";
        }
        CoreConfigTestFactory.BindAppManagerConfig(config);
        var node = new ProfileItem
        {
            IndexId = "vless-main",
            ConfigType = EConfigType.VLESS,
            CoreType = ECoreType.Xray,
            Remarks = "main",
            Address = "vless.example",
            Port = 443,
            Password = Guid.NewGuid().ToString(),
            Network = nameof(ETransport.raw),
            StreamSecurity = string.Empty,
            Subid = string.Empty,
        };
        node.SetProtocolExtra(new ProtocolExtraItem { VlessEncryption = Global.None, Flow = string.Empty });
        var context = CoreConfigTestFactory.CreateContext(config, node, ECoreType.Xray);

        var result = new CoreConfigV2rayService(context).GenerateClientConfigContent();

        await result.Success.Should().BeTrue();
        var cfg = JsonUtils.Deserialize<V2rayConfig>(result.Data!.ToString())!;
        var dns = JsonUtils.Deserialize<Dns4Ray>(JsonUtils.Serialize(cfg.dns))!;
        await dns.servers.Count.Should().BeGreaterThanOrEqualTo(1);
        await dns.tag.Should().BeEqualTo(Global.DnsTag);
        await cfg.routing.rules.Any(rule => rule.inboundTag?.Contains(Global.DnsTag) == true).Should().BeTrue();

        var proxy = cfg.outbounds.Single(outbound => outbound.tag == Global.ProxyTag);
        if (dial)
        {
            await (proxy.streamSettings?.sockopt?.domainStrategy).Should().BeEqualTo("UseIPv4");
        }
        else
        {
            await proxy.targetStrategy.Should().BeEqualTo("UseIPv4");
        }
    }
}
