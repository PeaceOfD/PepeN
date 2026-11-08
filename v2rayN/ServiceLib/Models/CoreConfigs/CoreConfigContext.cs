namespace ServiceLib.Models.CoreConfigs;

public record CoreConfigContext
{
    public required ProfileItem Node { get; init; }
    public required ECoreType RunCoreType { get; init; }
    public RoutingItem? RoutingItem { get; init; }
    public DNSItem? RawDnsItem { get; init; }
    public SimpleDNSItem SimpleDnsItem { get; init; } = new();
    public Dictionary<string, ProfileItem> AllProxiesMap { get; init; } = new();
    public Config AppConfig { get; init; } = new();
    public FullConfigTemplateItem? FullConfigTemplate { get; init; } = new();

    public Dictionary<string, string> CustomOutboundContent { get; init; } = new();

    // Test ServerTestItem Map
    public Dictionary<string, string> ServerTestItemMap { get; init; } = new();

    // TUN Compatibility
    public bool IsTunEnabled { get; init; } = false;
    public HashSet<string> ProtectDomainList { get; init; } = [];
    // Typically, it is the core of the outbound chain
    public HashSet<ECoreType> ProtectCoreTypeList { get; init; } = [];

    public bool IsWindows { get; init; }
    public bool IsMacOS { get; init; }
    public bool IsLinux { get; init; }

    // Defaults to true so that a context built without this flag keeps routing IPv6 into the
    // tunnel; only a positive detection of the host having no global IPv6 address turns it off.
    // PattN: the Xray TUN ignores it on Windows with strict route, where EnableIPv6Address decides (GenInbounds).
    public bool HasGlobalIPv6Address { get; init; } = true;

    // PattN: the OS keeps IPv6 off on new interfaces (Utils.IsIPv6DisabledByOs), where a TUN asked for IPv6 does not
    // start; the TUNs then take no IPv6 unless a global IPv6 address could bypass them (GenInbounds, TunTakesIPv6).
    public bool IsIPv6DisabledByOs { get; init; }

    // Generation Context
    public Dictionary<object, string> CustomOutboundMap { get; init; } = new();

    // PattN: the ECH outbounds of the profiles in this config, appended after every other outbound
    public List<EchOutboundItem> EchOutbounds { get; init; } = [];

    // PattN: what the Aether outbounds of this config need: filled while the outbounds are built, read by GenAether and
    // by whoever starts the core it settles on (Aether.Core)
    public AetherBuild Aether { get; init; } = new();
}

// PattN: an ECH outbound as a profile wrote it, and the tag it has in the generated config
public record EchOutboundItem(JsonObject Outbound, string Tag);
