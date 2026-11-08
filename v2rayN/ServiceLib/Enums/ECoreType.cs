namespace ServiceLib.Enums;

public enum ECoreType
{
    v2fly = 1,
    Xray = 2,
    v2fly_v5 = 4,
    mihomo = 13,
    hysteria = 21,
    naiveproxy = 22,
    tuic = 23,
    sing_box = 24,
    juicity = 25,
    hysteria2 = 26,
    brook = 27,
    overtls = 28,
    shadowquic = 29,
    mieru = 30,
    v2rayN = 99,

    // PattN: fork-only core, the Aether WARP client (patterniha/Aether). Its value stays clear of the values upstream
    // v2rayN assigns, so that a core added upstream later cannot collide with it on merge (as EConfigType.Aether).
    aether = 500,
}
