namespace ServiceLib.Enums;

public enum EConfigType
{
    VMess = 1,
    Custom = 2,
    Shadowsocks = 3,
    SOCKS = 4,
    VLESS = 5,
    Trojan = 6,
    Hysteria2 = 7,
    TUIC = 8,
    WireGuard = 9,
    HTTP = 10,
    Anytls = 11,
    Naive = 12,
    Outbound = 13,
    MASQUE = 14,
    PolicyGroup = 101,
    ProxyChain = 102,

    // PattN: fork-only type. Its value stays clear of the values upstream v2rayN assigns, so that a type added
    // upstream later cannot collide with it on merge (as PattNG's AETHER(500)).
    Aether = 500,
}
