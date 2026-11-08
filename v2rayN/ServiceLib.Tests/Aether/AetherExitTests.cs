using TUnit.Assertions.Enums;

namespace ServiceLib.Tests.Aether;

// From PattNG AetherCoreTest (its exit-node tests), AetherKeysTest.anExitNodeTakesABlankFinalMaskOrAJsonObject and
// dto/ByNameTest: the exit-node of a core, its digests, and the lookup of a profile by name.
public class AetherExitTests
{
    private const int ListenPort = AetherPorts.DefaultListenPort;

    // PattNG's profile { ... } of AetherCoreTest: a WireGuard profile with the changes of extra and set.
    private static ProfileItem Profile(Func<ProtocolExtraItem, ProtocolExtraItem>? extra = null, Action<ProfileItem>? set = null)
    {
        var profile = new ProfileItem { ConfigType = EConfigType.Aether };
        var protocolExtra = new ProtocolExtraItem { AetherProtocol = AetherProtocol.WireGuard.Type };
        profile.SetProtocolExtra(extra == null ? protocolExtra : extra(protocolExtra));
        set?.Invoke(profile);
        return profile;
    }

    private static ProfileItem Pinned()
    {
        return Profile(set: profile =>
        {
            profile.Address = "188.114.96.77";
            profile.Port = 443;
        });
    }

    // PattNG's ProfileItem.create(type) with a name, a server and a password (the uuid of VLESS).
    private static ProfileItem Server(EConfigType type, string remarks, string address = "", int port = 0, string password = "")
    {
        return new ProfileItem
        {
            ConfigType = type,
            Remarks = remarks,
            Address = address,
            Port = port,
            Password = password,
        };
    }

    // PattNG's profile.copy(...): a new profile with the changes, the one copied left as it was.
    private static ProfileItem Copy(ProfileItem profile, Func<ProtocolExtraItem, ProtocolExtraItem>? extra = null, Action<ProfileItem>? set = null)
    {
        var copy = JsonUtils.DeepCopy(profile)!;
        if (extra != null)
        {
            copy.SetProtocolExtra(extra(copy.GetProtocolExtra()));
        }
        set?.Invoke(copy);
        return copy;
    }

    private static string Digest(string text)
    {
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(text)));
    }

    private static ByName<string> Find(string? name, params string[] names)
    {
        return ByName.Find(name, names, it => it);
    }

    [Test]
    public async Task AProfileNamedAsTheExitNodeTakesThePlaceOfFreedom()
    {
        var masked = Profile(set: profile =>
        {
            profile.Finalmask = """{"tcp": []}""";
            profile.DialMode = "code-1";
        });
        await AetherExit.Of(masked).Should().BeEqualTo(new AetherExit("""{"tcp": []}""", "code-1"));
        // With a node, the finalMask and the dialMode are out of use.
        var noded = Copy(masked, extra => extra with { AetherExitNode = " germany " });
        await AetherExit.Of(noded).Should().BeEqualTo(new AetherExit(Node: "germany"));
        await AetherCore.Of(noded, ListenPort).Exit.Should().BeEqualTo(new AetherExit(Node: "germany"));
        await AetherCore.Of(noded, ListenPort).Arguments
            .Should().BeEquivalentTo(AetherCore.Of(masked, ListenPort).Arguments, CollectionOrdering.Matching);
        await AetherExit.Of(Copy(masked, extra => extra with { AetherExitNode = " " })).Should().BeEqualTo(AetherExit.Of(masked));
        // The node tells exits apart.
        await new AetherExit(Node: "germany").Key.Should().NotBeEqualTo(AetherExit.Plain.Key);
        await new AetherExit(Node: "france").Key.Should().NotBeEqualTo(new AetherExit(Node: "germany").Key);
        // The key of an exit without one is what it was before nodes.
        await new AetherExit(DialMode: "code-1").Key.Should().BeEqualTo(Digest("\u0000code-1\u0000"));
        await new AetherExit(Hops: "hops").Key.Should().BeEqualTo(Digest("\u0000\u0000hops"));
    }

    [Test]
    public async Task TheKeyOfAnExitNodeTellsAChangedProfileOfTheSameName()
    {
        var germany = Server(EConfigType.VLESS, "germany", "203.0.113.7", 443);
        germany.IndexId = "guid-1";
        // A subscription's update gives the profile of the name another server, and a new id the content does not hold.
        var updated = Copy(germany, set: profile =>
        {
            profile.Address = "203.0.113.8";
            profile.IndexId = "guid-2";
        });
        var node = new AetherExit(Node: "germany");
        Func<string, ByName<ProfileItem>> Found(ProfileItem profile)
        {
            return name => name == "germany" ? new ByName<ProfileItem>.One(profile) : ByName<ProfileItem>.None.Instance;
        }

        var atStart = node.WithNodeContent(Found(germany));
        await atStart.NodeContent.Should().BeEqualTo(AetherExit.ContentOf(germany));
        await node.WithNodeContent(Found(Copy(germany, set: profile => profile.IndexId = "guid-3"))).Key.Should().BeEqualTo(atStart.Key);
        await node.WithNodeContent(Found(updated)).Key.Should().NotBeEqualTo(atStart.Key);
        await node.Key.Should().NotBeEqualTo(atStart.Key);
        // A name no profile, or several, have any more gives none, and the key without it differs as well.
        await node.WithNodeContent(_ => ByName<ProfileItem>.None.Instance).Should().BeEqualTo(node);
        await node.WithNodeContent(_ => ByName<ProfileItem>.Several.Instance).Should().BeEqualTo(node);
        // Without a node there is nothing to look up, and the key stays what it was.
        await AetherExit.Plain.WithNodeContent(_ => throw new InvalidOperationException("no node to look up")).Should().BeEqualTo(AetherExit.Plain);
        // The digest holds no secret of the profile.
        await atStart.Key.Contains("203.0.113.7").Should().BeFalse();
        await atStart.NodeContent!.Contains("203.0.113.7").Should().BeFalse();
    }

    [Test]
    public async Task AnyProfileAChainTakesForAHopCanBeTheExitNodeButAnAetherOne()
    {
        EConfigType[] taken =
        [
            EConfigType.VMess, EConfigType.VLESS, EConfigType.Trojan, EConfigType.Shadowsocks, EConfigType.SOCKS,
            EConfigType.HTTP, EConfigType.WireGuard, EConfigType.Hysteria2,
        ];
        foreach (var type in taken)
        {
            await AetherExit.TakesAsNode(new ProfileItem { ConfigType = type }).Should().BeTrue();
        }
        foreach (var type in new[] { EConfigType.Aether, EConfigType.Custom, EConfigType.PolicyGroup, EConfigType.ProxyChain })
        {
            await AetherExit.TakesAsNode(new ProfileItem { ConfigType = type }).Should().BeFalse();
        }
    }

    // PattN: only what Xray builds gives the exit-node an outbound; sing-box alone runs TUIC, AnyTLS, Naive and the custom
    // outbounds made for it.
    [Test]
    public async Task OnlyAProfileXrayCanBuildCanBeTheExitNode()
    {
        await AetherExit.TakesAsNode(new ProfileItem { ConfigType = EConfigType.MASQUE }).Should().BeTrue();
        foreach (var type in new[] { EConfigType.TUIC, EConfigType.Anytls, EConfigType.Naive })
        {
            await AetherExit.TakesAsNode(new ProfileItem { ConfigType = type }).Should().BeFalse();
        }
        await AetherExit.TakesAsNode(new ProfileItem { ConfigType = EConfigType.Outbound, CoreType = ECoreType.Xray }).Should().BeTrue();
        await AetherExit.TakesAsNode(new ProfileItem { ConfigType = EConfigType.Outbound, CoreType = ECoreType.sing_box }).Should().BeFalse();
        await AetherExit.TakesAsNode(new ProfileItem { ConfigType = EConfigType.Outbound }).Should().BeFalse();
    }

    [Test]
    public async Task TheNamesOfTheProfilesThatCanBeTheExitNodeAreListedOnceWithHowManyHaveThem()
    {
        ProfileItem[] profiles =
        [
            Server(EConfigType.VLESS, " germany "),
            Server(EConfigType.Trojan, "france"),
            Server(EConfigType.VMess, "germany"),
            // An Aether profile, or a group, has a name no exit-node is found by.
            Server(EConfigType.Aether, "france"),
            Server(EConfigType.PolicyGroup, "spain"),
            // Nothing names a profile without a name.
            Server(EConfigType.SOCKS, "  "),
            Server(EConfigType.HTTP, "italy"),
        ];
        await AetherExit.NodesOf(profiles).Should().BeEquivalentTo(
            [new AetherExitNode("germany", 2), new AetherExitNode("france", 1), new AetherExitNode("italy", 1)],
            CollectionOrdering.Matching);
        await AetherExit.NodesOf([]).Should().BeEmpty();
    }

    // PattN: the lookup of an exit-node by name (PattNG AetherExit.nodeProfile over the profile list, and the problems of
    // ExitNodeOutbound), with names that several profiles have.
    [Test]
    public async Task AnExitNodeNameFindsTheOneProfileThatCanBeTheExitNode()
    {
        var germany = Server(EConfigType.VLESS, " germany ", "203.0.113.7", 443);
        ProfileItem[] profiles =
        [
            germany,
            // An Aether profile, or a group, of the same name is no exit-node, so the name still finds one.
            Server(EConfigType.Aether, "germany"),
            Server(EConfigType.PolicyGroup, "germany"),
            Server(EConfigType.Trojan, "france"),
            Server(EConfigType.VMess, "france "),
            Server(EConfigType.ProxyChain, "spain"),
        ];
        await AetherExit.NodeProfile("germany", profiles).Should().BeEqualTo(new ByName<ProfileItem>.One(germany));
        await AetherExit.NodeProfile("france", profiles).Should().BeEqualTo(ByName<ProfileItem>.Several.Instance);
        await AetherExit.NodeProfile("spain", profiles).Should().BeEqualTo(ByName<ProfileItem>.None.Instance);

        var found = ExitNodeLookup.Of(AetherExit.NodeProfile("germany", profiles));
        await (found is ExitNodeLookup.One one && ReferenceEquals(one.Profile, germany) && one.Content == AetherExit.ContentOf(germany))
            .Should().BeTrue();
        await ExitNodeLookup.Of(AetherExit.NodeProfile("france", profiles)).Should().BeEqualTo(ExitNodeLookup.SameName.Instance);
        await ExitNodeLookup.Of(AetherExit.NodeProfile("spain", profiles)).Should().BeEqualTo(ExitNodeLookup.NotFound.Instance);
        await ExitNodeLookup.NotFound.Instance.MessageKey.Should().BeEqualTo("MsgAetherExitNodeNotFound");
        await ExitNodeLookup.SameName.Instance.MessageKey.Should().BeEqualTo("MsgAetherExitNodeDuplicate");
        await ExitNodeLookup.NoOutbound.Instance.MessageKey.Should().BeEqualTo("MsgAetherExitNodeUnusable");
        await ExitNodeLookup.EchUnusable.Instance.MessageKey.Should().BeEqualTo("MsgAetherExitNodeEchUnusable");
    }

    [Test]
    public async Task AnUpdateThatLeavesAProfileAsItWasLeavesItsDigestAsItWas()
    {
        // A subscription's update builds every profile again, added anew, and may move it to another subscription; what
        // it connects with is what tells an exit-node, or the hops of a chain, apart. (PattNG's addedTime and
        // description are PattN's IndexId and nothing.)
        var germany = Server(EConfigType.VLESS, "germany", "203.0.113.7", 443, "uuid");
        var renewed = Copy(germany, set: profile =>
        {
            profile.IndexId = "added-anew";
            profile.Subid = "sub";
            profile.ConfigVersion = 5;
            // PattN: a subscription's import also marks the row and sets its pre-socks port, the list its log switch,
            // and the name is no part of what a profile connects with.
            profile.IsSub = false;
            profile.PreSocksPort = 1234;
            profile.DisplayLog = false;
            profile.Remarks = " germany renewed ";
        });

        await AetherExit.ContentOf(renewed).Should().BeEqualTo(AetherExit.ContentOf(germany));
        await AetherExit.Through([renewed]).Should().BeEqualTo(AetherExit.Through([germany]));
        await AetherExit.ContentOf(Copy(germany, set: profile => profile.Port = 8443)).Should().NotBeEqualTo(AetherExit.ContentOf(germany));
        await AetherExit.Through([Copy(germany, set: profile => profile.Password = "another")]).Should().NotBeEqualTo(AetherExit.Through([germany]));
    }

    // PattN: a custom outbound profile holds the name of the file its outbound is in, and every import of a subscription
    // copies the file under a new name. What it connects with is the outbound, read as the configuration reads it, without
    // the tag the configuration gives it and in whatever layout, so that an update that leaves it as it was leaves the
    // same node, and a file edited in place makes another.
    [Test]
    public async Task ACustomOutboundIsToldByItsOutboundNotByItsFileName()
    {
        const string germany = """{"protocol": "vless", "tag": "proxy", "settings": {"vnext": [{"address": "203.0.113.7", "port": 443}]}}""";
        var files = new Dictionary<string, string>
        {
            ["1111.json"] = germany,
            // The same outbound under another tag, laid out otherwise, with a comment.
            ["2222.json"] = """
                            {
                              "tag": "another", // set anew by the configuration
                              "protocol": "vless",
                              "settings": {"vnext": [{"address": "203.0.113.7", "port": 443}]}
                            }
                            """,
            ["3333.json"] = germany.Replace("203.0.113.7", "203.0.113.8"),
        };
        string? Read(ProfileItem profile)
        {
            return files.GetValueOrDefault(profile.Address);
        }
        ProfileItem Outbound(string file)
        {
            return new ProfileItem { ConfigType = EConfigType.Outbound, CoreType = ECoreType.Xray, Remarks = "out", Address = file };
        }

        var first = AetherExit.ContentOf(Outbound("1111.json"), Read);
        await AetherExit.ContentOf(Outbound("2222.json"), Read).Should().BeEqualTo(first);
        await AetherExit.Through([Outbound("2222.json")], Read).Should().BeEqualTo(AetherExit.Through([Outbound("1111.json")], Read));
        await AetherExit.ContentOf(Outbound("3333.json"), Read).Should().NotBeEqualTo(first);
        await AetherExit.Through([Outbound("3333.json")], Read).Should().NotBeEqualTo(AetherExit.Through([Outbound("1111.json")], Read));
        await first.Contains("203.0.113.7").Should().BeFalse();

        // The file edited in place: another node under the same file name.
        files["1111.json"] = files["3333.json"];
        await AetherExit.ContentOf(Outbound("1111.json"), Read).Should().NotBeEqualTo(first);
        files["1111.json"] = germany;

        // The node a name finds, and the lookup of it, are digested the same way.
        var node = new AetherExit(Node: "out");
        var atStart = node.WithNodeContent(_ => new ByName<ProfileItem>.One(Outbound("1111.json")), Read);
        await node.WithNodeContent(_ => new ByName<ProfileItem>.One(Outbound("2222.json")), Read).Key.Should().BeEqualTo(atStart.Key);
        await node.WithNodeContent(_ => new ByName<ProfileItem>.One(Outbound("3333.json")), Read).Key.Should().NotBeEqualTo(atStart.Key);
        var found = ExitNodeLookup.Of(new ByName<ProfileItem>.One(Outbound("2222.json")), Read);
        await (found is ExitNodeLookup.One one && one.Content == first).Should().BeTrue();

        // Without the file, the file name is all there is to tell it by; no other profile is read so.
        await AetherExit.ContentOf(Outbound("4444.json"), Read).Should().NotBeEqualTo(AetherExit.ContentOf(Outbound("5555.json"), Read));
        var vless = Server(EConfigType.VLESS, "v", "203.0.113.7", 443, "uuid");
        await AetherExit.ContentOf(vless, _ => throw new InvalidOperationException("only a custom outbound is read"))
            .Should().BeEqualTo(AetherExit.ContentOf(vless));
    }

    [Test]
    public async Task TheHopsOfAChainAreAnExitNodeOfTheirOwn()
    {
        var vless = Server(EConfigType.VLESS, "v", "1.2.3.4", 443, "secret-uuid");
        var trojan = Server(EConfigType.Trojan, "t", "5.6.7.8", 443, "secret-password");
        var through = AetherExit.Through([vless, trojan]);
        await AetherExit.Through([Copy(vless), Copy(trojan)]).Should().BeEqualTo(through);
        await AetherExit.Through([trojan, vless]).Should().NotBeEqualTo(through);
        await AetherExit.Through([vless]).Should().NotBeEqualTo(through);
        await AetherExit.Through([Copy(vless, set: profile => profile.Password = "another"), trojan]).Should().NotBeEqualTo(through);
        await through.FinalMask.Should().BeNull();
        await through.DialMode.Should().BeNull();
        // Neither what tells the hops apart nor the key of the exit-node holds what the hops are made of.
        await through.ToString().Contains("secret").Should().BeFalse();
        await through.Key.Contains("secret").Should().BeFalse();
    }

    [Test]
    public async Task EveryExitNodeHasAKeyOfItsOwn()
    {
        await new AetherExit().Key.Should().BeEqualTo(AetherExit.Plain.Key);
        await AetherExit.Plain.Key.Length.Should().BeEqualTo(64);
        await AetherExit.Of(Copy(Pinned(), set: profile => profile.DialMode = "code-1")).Key.Should().BeEqualTo(new AetherExit(DialMode: "code-1").Key);
        var keys = new[]
        {
            AetherExit.Plain,
            new AetherExit(DialMode: "code-1"),
            new AetherExit(FinalMask: "code-1"),
            new AetherExit(FinalMask: """{"tcp": []}""", DialMode: "code-1"),
            AetherExit.Through([Pinned()]),
        }.Select(exit => exit.Key).ToList();
        await keys.Distinct().Count().Should().BeEqualTo(keys.Count);
    }

    [Test]
    public async Task AnExitNodeTakesABlankFinalMaskOrAJsonObject()
    {
        await AetherExit.TakesFinalMask(null).Should().BeTrue();
        await AetherExit.TakesFinalMask(" ").Should().BeTrue();
        await AetherExit.TakesFinalMask("""{"tcp": []}""").Should().BeTrue();
        await AetherExit.TakesFinalMask("[]").Should().BeFalse();
        await AetherExit.TakesFinalMask("{not json").Should().BeFalse();
        await AetherExit.TakesFinalMask("ForceIP").Should().BeFalse();
    }

    [Test]
    public async Task ANameFindsTheOneProfileThatHasIt()
    {
        await Find("germany", "france", "germany", "germany 2").Should().BeEqualTo(new ByName<string>.One("germany"));
    }

    [Test]
    public async Task NamesAreToldApartWithoutTheSpacesAroundThem()
    {
        await Find("germany", " germany ").Should().BeEqualTo(new ByName<string>.One(" germany "));
        await Find(" germany  ", "germany").Should().BeEqualTo(new ByName<string>.One("germany"));
        // Inside a name, and in the case of its letters, they count.
        await Find("Germany", "germany").Should().BeEqualTo(ByName<string>.None.Instance);
        await Find("ger many", "germany").Should().BeEqualTo(ByName<string>.None.Instance);
    }

    [Test]
    public async Task ANameNoProfileHasAnyMoreFindsNone()
    {
        await Find("germany", "france", "germany-renamed").Should().BeEqualTo(ByName<string>.None.Instance);
        await Find("germany").Should().BeEqualTo(ByName<string>.None.Instance);
    }

    [Test]
    public async Task ANameSeveralProfilesHaveFindsSeveral()
    {
        await Find("germany", "germany", "france", " germany").Should().BeEqualTo(ByName<string>.Several.Instance);
    }

    [Test]
    public async Task ABlankNameFindsNoneEvenWhereProfilesHaveNoName()
    {
        await Find("", "", "france").Should().BeEqualTo(ByName<string>.None.Instance);
        await Find("  ", " ").Should().BeEqualTo(ByName<string>.None.Instance);
        await Find(null, "").Should().BeEqualTo(ByName<string>.None.Instance);
    }

    // PattN: names are trimmed by Kotlin's whitespace wherever PattNG trims them, so that the WARP keys window, an Aether
    // profile and the list of names find the same profile: the separators U+001C to U+001F are spaces, U+0085 is not.
    [Test]
    public async Task TheKeysWindowAProfileAndTheListOfNamesFindTheSameNode()
    {
        var named = Server(EConfigType.VLESS, "X\u001F", "203.0.113.7", 443);
        ProfileItem[] profiles = [named];
        await AetherExit.NodesOf(profiles).Should().BeEquivalentTo([new AetherExitNode("X", 1)], CollectionOrdering.Matching);

        var fromWindow = new AetherKeysSettings { ExitNode = "X\u001F" }.Exit;
        await fromWindow.Should().BeEqualTo(new AetherExit(Node: "X"));
        await AetherExit.NodeProfile(fromWindow.Node, profiles).Should().BeEqualTo(new ByName<ProfileItem>.One(named));

        var fromProfile = AetherExit.Of(Profile(extra => extra with { AetherExitNode = "\u001FX\u001F" }));
        await fromProfile.Should().BeEqualTo(new AetherExit(Node: "X"));
        await AetherExit.NodeProfile(fromProfile.Node, profiles).Should().BeEqualTo(new ByName<ProfileItem>.One(named));

        await Find("\u001Egermany", "germany\u001C").Should().BeEqualTo(new ByName<string>.One("germany\u001C"));
        await Find("germany\u0085", "germany").Should().BeEqualTo(ByName<string>.None.Instance);
        await Find("\u001F", "").Should().BeEqualTo(ByName<string>.None.Instance);
        // A finalMask and a dialMode of separators alone are none, as in PattNG.
        await AetherExit.Of(Profile(set: profile =>
        {
            profile.Finalmask = "\u001F";
            profile.DialMode = "\u001C";
        })).Should().BeEqualTo(AetherExit.Plain);
        await AetherExit.TakesFinalMask("\u001F").Should().BeTrue();
        await AetherExit.TakesFinalMask("\u0085").Should().BeFalse();
    }

    [Test]
    public async Task TheSearchEndsAtTheSecondProfileWithTheName()
    {
        // The profiles of a long list are read as the search goes; those after the second are not.
        var read = new List<string>();
        var names = new[] { "germany", "france", "germany", "spain" }.Select(name =>
        {
            read.Add(name);
            return name;
        });
        await ByName.Find("germany", names, it => it).Should().BeEqualTo(ByName<string>.Several.Instance);
        await read.Should().BeEquivalentTo(["germany", "france", "germany"], CollectionOrdering.Matching);
    }
}
