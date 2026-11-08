using TUnit.Assertions.Enums;

namespace ServiceLib.Tests.Aether;

/// <summary>From PattNG AetherIdentityManagerTest, on real files in a folder of each test's own.</summary>
public class AetherIdentityManagerTests : IDisposable
{
    private const string Quotes = "\"\"\"";

    // PattNG's @TempDir: a folder under the temp path for this test alone, removed after it.
    private readonly string _folder = Directory.CreateTempSubdirectory("pattn-aether-identity-").FullName;

    public void Dispose()
    {
        Directory.Delete(_folder, recursive: true);
    }

    private string NewFolder(string name)
    {
        var path = Path.Combine(_folder, name);
        if (Exists(path))
        {
            throw new InvalidOperationException($"could not create {path}");
        }
        return Directory.CreateDirectory(path).FullName;
    }

    private static bool Exists(string path)
    {
        return Directory.Exists(path) || File.Exists(path);
    }

    private static string KeyFile(string deviceId, string ipv4 = "172.16.0.2", string ipv6 = "2606:4700:110:8a36::1")
    {
        return string.Join("\n",
            $"device_id = \"{deviceId}\"",
            "access_token = \"secret-token\"",
            $"cert_pem = {Quotes}",
            "-----BEGIN CERTIFICATE-----",
            "device_id = \"hidden-in-pem\"",
            "-----END CERTIFICATE-----",
            Quotes,
            "cert_issued_at = 1757580000",
            $"ipv4 = \"{ipv4}\"",
            $"ipv6 = \"{ipv6}\"",
            "wg_private_key = \"c2VjcmV0\"");
    }

    private string WorkDir(params (string Name, string Text)[] files)
    {
        var dir = NewFolder("aether");
        foreach (var (name, text) in files)
        {
            File.WriteAllText(Path.Combine(dir, name), text);
        }
        return dir;
    }

    /// <summary>The device of each key in <paramref name="dir" />, in the order of <see cref="AetherPaths.KeyFiles" />; null for a key that is not there.</summary>
    private static List<string?> Devices(string dir)
    {
        return AetherPaths.KeyFiles
            .Select(name => Path.Combine(dir, name))
            .Select(path => File.Exists(path) ? AetherIdentityManager.Parse(File.ReadAllText(path))?.DeviceId : null)
            .ToList();
    }

    private static List<string?> Every(string? deviceId)
    {
        return Enumerable.Repeat(deviceId, AetherPaths.KeyFiles.Count).ToList();
    }

    private string KeysInUse()
    {
        return WorkDir(AetherPaths.KeyFiles.Select(it => (it, KeyFile("old"))).ToArray());
    }

    private static void Register(string renewalDir, IEnumerable<string>? files = null, string deviceId = "new")
    {
        foreach (var it in files ?? AetherPaths.KeyFiles)
        {
            File.WriteAllText(Path.Combine(renewalDir, it), KeyFile(deviceId));
        }
    }

    // PattNG's assertEquals on lists: the same items in the same order.
    private static async Task ShouldBe(IReadOnlyList<string?> actual, IReadOnlyList<string?> expected)
    {
        await actual.Should().BeEquivalentTo(expected, CollectionOrdering.Matching);
    }

    [Test]
    public async Task OnlyTheIdentityFieldsAreReadFromAKeyFile()
    {
        var identity = AetherIdentityManager.Parse(KeyFile("a1b2c3d4-e5f6"));
        await identity.Should().BeEqualTo(new AetherIdentity("a1b2c3d4-e5f6", "172.16.0.2", "2606:4700:110:8a36::1"));
    }

    [Test]
    public async Task AFileWithoutADeviceIsNotAKey()
    {
        await AetherIdentityManager.Parse("ipv4 = \"172.16.0.2\"").Should().BeNull();
        await AetherIdentityManager.Parse("device_id = \"\"").Should().BeNull();
        await AetherIdentityManager.Parse("").Should().BeNull();
    }

    [Test]
    public async Task EachProtocolReadsItsOwnKeyFiles()
    {
        var dir = WorkDir(
            (AetherPaths.MasqueFile, KeyFile("masque")),
            (AetherPaths.MasqueInnerFile, KeyFile("masque-inner")),
            (AetherPaths.WireGuardFile, KeyFile("outer")),
            (AetherPaths.WireGuardInnerFile, KeyFile("inner")));

        await (AetherIdentityManager.Status(dir, AetherProtocol.Masque).Primary?.DeviceId).Should().BeEqualTo("masque");
        await AetherIdentityManager.Status(dir, AetherProtocol.Masque).Secondary.Should().BeNull();
        await (AetherIdentityManager.Status(dir, AetherProtocol.WireGuard).Primary?.DeviceId).Should().BeEqualTo("outer");

        var gool = AetherIdentityManager.Status(dir, AetherProtocol.Gool);
        await (gool.Primary?.DeviceId).Should().BeEqualTo("outer");
        await (gool.Secondary?.DeviceId).Should().BeEqualTo("inner");

        var mim = AetherIdentityManager.Status(dir, AetherProtocol.Mim);
        await (mim.Primary?.DeviceId).Should().BeEqualTo("masque");
        await (mim.Secondary?.DeviceId).Should().BeEqualTo("masque-inner");

        // WireGuard over MASQUE: the MASQUE key outside, and a WireGuard key of its own, which the core registered.
        var goolOverMasque = AetherIdentityManager.Status(dir, AetherProtocol.WgOverMasque);
        await (goolOverMasque.Primary?.DeviceId).Should().BeEqualTo("masque");
        await goolOverMasque.Secondary.Should().BeNull();
        File.WriteAllText(Path.Combine(dir, AetherPaths.MasqueGoolFile), KeyFile("gool"));
        await (AetherIdentityManager.Status(dir, AetherProtocol.WgOverMasque).Secondary?.DeviceId).Should().BeEqualTo("gool");
    }

    [Test]
    public async Task AMissingKeyFileMeansNoKey()
    {
        var dir = WorkDir((AetherPaths.WireGuardFile, KeyFile("outer")));

        await AetherIdentityManager.Status(dir, AetherProtocol.Masque).Primary.Should().BeNull();
        await AetherIdentityManager.Status(dir, AetherProtocol.Gool).Secondary.Should().BeNull();
        await AetherIdentityManager.Status(dir, AetherProtocol.Mim).Primary.Should().BeNull();
        await AetherIdentityManager.Status(dir, AetherProtocol.Mim).Secondary.Should().BeNull();
        await AetherIdentityManager.Status(Path.Combine(_folder, "absent"), AetherProtocol.WireGuard).Primary.Should().BeNull();
    }

    [Test]
    public async Task TheTunnelsOverMasqueShareAKeyAndTheOthersAnother()
    {
        await AetherIdentityManager.SharesIdentity(AetherProtocol.Masque, AetherProtocol.Masque).Should().BeTrue();
        await AetherIdentityManager.SharesIdentity(AetherProtocol.Masque, AetherProtocol.Mim).Should().BeTrue();
        await AetherIdentityManager.SharesIdentity(AetherProtocol.Mim, AetherProtocol.Mim).Should().BeTrue();
        await AetherIdentityManager.SharesIdentity(AetherProtocol.WireGuard, AetherProtocol.Gool).Should().BeTrue();
        await AetherIdentityManager.SharesIdentity(AetherProtocol.Gool, AetherProtocol.Gool).Should().BeTrue();
        await AetherIdentityManager.SharesIdentity(AetherProtocol.Masque, AetherProtocol.WireGuard).Should().BeFalse();
        await AetherIdentityManager.SharesIdentity(AetherProtocol.Gool, AetherProtocol.Masque).Should().BeFalse();
        await AetherIdentityManager.SharesIdentity(AetherProtocol.Mim, AetherProtocol.Gool).Should().BeFalse();
        await AetherIdentityManager.SharesIdentity(AetherProtocol.WireGuard, AetherProtocol.Mim).Should().BeFalse();
        // WireGuard over MASQUE is a tunnel over MASQUE; its WireGuard key is no other tunnel's.
        await AetherIdentityManager.SharesIdentity(AetherProtocol.WgOverMasque, AetherProtocol.Masque).Should().BeTrue();
        await AetherIdentityManager.SharesIdentity(AetherProtocol.Mim, AetherProtocol.WgOverMasque).Should().BeTrue();
        await AetherIdentityManager.SharesIdentity(AetherProtocol.WgOverMasque, AetherProtocol.WireGuard).Should().BeFalse();
        await AetherIdentityManager.SharesIdentity(AetherProtocol.Gool, AetherProtocol.WgOverMasque).Should().BeFalse();
    }

    [Test]
    public async Task TheCoreSaysWhenEveryKeyIsRegistered()
    {
        var done = "[2026-10-01T10:00:00.000Z INFO  aether] [+] identities ready: wireguard, wireguard inner, masque, masque inner";
        await AetherIdentityManager.IsRegistered(done).Should().BeTrue();

        // Each key is announced as it is saved; only the last line ends the run.
        var one = "[2026-10-01T10:00:00.000Z INFO  aether] [+] masque identity ready: device=a1b2 ipv4=172.16.0.2 ipv6=2606::1";
        await AetherIdentityManager.IsRegistered(one).Should().BeFalse();
        await AetherIdentityManager.IsRegistered("[+] no masque identity found; provisioning dedicated masque account").Should().BeFalse();
    }

    [Test]
    public async Task ARenewalReplacesEveryKeyOnceAllTheNewOnesAreThere()
    {
        var dir = KeysInUse();
        var renewal = Path.Combine(_folder, "aether-renewal");

        var renewed = await AetherIdentityManager.RenewAsync(dir, renewal, AetherPaths.KeyFiles, _ =>
        {
            Register(renewal);
            return Task.FromResult(true);
        });

        await renewed.Should().BeTrue();
        await ShouldBe(Devices(dir), Every("new"));
        await Exists(renewal).Should().BeFalse();
    }

    [Test]
    public async Task TheKeysInUseAreNotTouchedWhileTheNewOnesAreRegistered()
    {
        var dir = KeysInUse();
        var renewal = Path.Combine(_folder, "aether-renewal");

        await AetherIdentityManager.RenewAsync(dir, renewal, AetherPaths.KeyFiles, async _ =>
        {
            Register(renewal, AetherPaths.KeyFiles.SkipLast(1));
            // The first new keys are there and the last one is still being registered.
            await ShouldBe(Devices(dir), Every("old"));
            Register(renewal, AetherPaths.KeyFiles.TakeLast(1));
            await ShouldBe(Devices(dir), Every("old"));
            return true;
        });

        await ShouldBe(Devices(dir), Every("new"));
    }

    [Test]
    public async Task AMissingNewKeyKeepsEveryOldKey()
    {
        var dir = KeysInUse();
        var renewal = Path.Combine(_folder, "aether-renewal");

        var renewed = await AetherIdentityManager.RenewAsync(dir, renewal, AetherPaths.KeyFiles, _ =>
        {
            // The runs said they were done, but the inner WireGuard key is not there.
            Register(renewal, AetherPaths.KeyFiles.Where(it => it != AetherPaths.WireGuardInnerFile));
            return Task.FromResult(true);
        });

        await renewed.Should().BeFalse();
        await ShouldBe(Devices(dir), Every("old"));
        await Exists(renewal).Should().BeFalse();
    }

    [Test]
    public async Task ANewKeyThatDoesNotReadAsAKeyKeepsEveryOldKey()
    {
        var dir = KeysInUse();
        var renewal = Path.Combine(_folder, "aether-renewal");

        var renewed = await AetherIdentityManager.RenewAsync(dir, renewal, AetherPaths.KeyFiles, _ =>
        {
            Register(renewal);
            File.WriteAllText(Path.Combine(renewal, AetherPaths.MasqueInnerFile), "device_id = \"\"");
            return Task.FromResult(true);
        });

        await renewed.Should().BeFalse();
        await ShouldBe(Devices(dir), Every("old"));
        await Exists(renewal).Should().BeFalse();
    }

    [Test]
    public async Task AFailedRegistrationKeepsEveryOldKey()
    {
        var dir = KeysInUse();
        var renewal = Path.Combine(_folder, "aether-renewal");

        var renewed = await AetherIdentityManager.RenewAsync(dir, renewal, AetherPaths.KeyFiles, _ =>
        {
            // The MASQUE keys came, then the WireGuard run failed.
            Register(renewal, [AetherPaths.MasqueFile, AetherPaths.MasqueInnerFile]);
            return Task.FromResult(false);
        });

        await renewed.Should().BeFalse();
        await ShouldBe(Devices(dir), Every("old"));
        await Exists(renewal).Should().BeFalse();
    }

    [Test]
    public async Task ACancelledRenewalKeepsEveryOldKey()
    {
        var dir = KeysInUse();
        var renewal = Path.Combine(_folder, "aether-renewal");
        using var cancellation = new CancellationTokenSource();
        var registering = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var renewing = AetherIdentityManager.RenewAsync(dir, renewal, AetherPaths.KeyFiles, async token =>
        {
            Register(renewal);
            registering.SetResult();
            await Task.Delay(Timeout.Infinite, token);
            return true;
        }, ct: cancellation.Token);
        await registering.Task;
        await cancellation.CancelAsync();
        try
        {
            await renewing;
        }
        catch (OperationCanceledException)
        {
            // PattNG's cancelAndJoin: the renewal is over, cancelled.
        }

        await ShouldBe(Devices(dir), Every("old"));
        await Exists(renewal).Should().BeFalse();
    }

    [Test]
    public async Task ACancellationLandingRightAfterTheNewKeysWereMarkedReadyStillPutsThemInPlace()
    {
        var dir = KeysInUse();
        var renewal = Path.Combine(_folder, "aether-renewal");
        using var cancellation = new CancellationTokenSource();
        var markedReady = false;
        List<string?>? devicesWhenMarked = null;
        // PattNG holds the step that marks the new keys ready on a single-threaded executor and cancels the renewal once
        // that step is over and before its result is delivered. The steps on the files run through this instead, which
        // cancels the renewal right after the step that left the ready mark, before the step's result comes back: the
        // moment a renewal must neither drop its new keys nor leave half of them.
        Func<Func<bool>, Task<bool>> steps = step =>
        {
            var result = step();
            if (!cancellation.IsCancellationRequested && File.Exists(Path.Combine(renewal, AetherPaths.ReadyMark)))
            {
                // Marked ready, and not in place yet.
                markedReady = true;
                devicesWhenMarked = Devices(dir);
                cancellation.Cancel();
            }
            return Task.FromResult(result);
        };

        try
        {
            await AetherIdentityManager.RenewAsync(dir, renewal, AetherPaths.KeyFiles, _ =>
            {
                Register(renewal);
                return Task.FromResult(true);
            }, steps, cancellation.Token);
        }
        catch (OperationCanceledException)
        {
            // PattNG's job.cancel() and join(): the renewal is over, cancelled.
        }

        await markedReady.Should().BeTrue();
        await ShouldBe(devicesWhenMarked!, Every("old"));
        await ShouldBe(Devices(dir), Every("new"));
        await Exists(renewal).Should().BeFalse();
    }

    [Test]
    public async Task ARenewalStoppedWhileItMovedTheKeysIsFinishedLater()
    {
        var dir = KeysInUse();
        var renewal = NewFolder("aether-renewal");
        Register(renewal);
        File.Create(Path.Combine(renewal, AetherPaths.ReadyMark)).Dispose();
        // PattN was killed after the first two new keys were moved.
        foreach (var it in AetherPaths.KeyFiles.Take(2))
        {
            File.Move(Path.Combine(renewal, it), Path.Combine(dir, it), overwrite: true);
        }

        await AetherIdentityManager.Settle(dir, renewal).Should().BeTrue();

        await ShouldBe(Devices(dir), Every("new"));
        await Exists(renewal).Should().BeFalse();
    }

    [Test]
    public async Task SettlingTheFilesFolderFinishesARenewalWhoseKeysWereAllReady()
    {
        var dir = KeysInUse();
        var renewal = NewFolder("aether-renewal");
        Register(renewal);
        File.Create(Path.Combine(renewal, AetherPaths.ReadyMark)).Dispose();

        await AetherIdentityManager.SettleIn(_folder).Should().BeTrue();

        await ShouldBe(Devices(dir), Every("new"));
        await ShouldBe(Directory.EnumerateFileSystemEntries(_folder).Select(Path.GetFileName).ToList(), ["aether"]);
    }

    [Test]
    public async Task NewKeysNotMarkedReadyAreNeverMovedIntoPlace()
    {
        var dir = KeysInUse();
        var renewal = NewFolder("aether-renewal");
        // A renewal is still at work here, or one was stopped before it had checked its keys.
        Register(renewal);

        await AetherIdentityManager.Settle(dir, renewal).Should().BeTrue();

        await ShouldBe(Devices(dir), Every("old"));
        await ShouldBe(Devices(renewal), Every("new"));
    }

    [Test]
    public async Task WhatAnInterruptedRenewalLeftIsNotTakenForNewKeys()
    {
        var dir = KeysInUse();
        var renewal = NewFolder("aether-renewal");
        Register(renewal, AetherPaths.KeyFiles.Take(3), deviceId: "stale");

        var renewed = await AetherIdentityManager.RenewAsync(dir, renewal, AetherPaths.KeyFiles, async _ =>
        {
            // The stale keys are gone before the core runs, which registers them all anew.
            await ShouldBe(Devices(renewal), Every(null));
            Register(renewal);
            return true;
        });

        await renewed.Should().BeTrue();
        await ShouldBe(Devices(dir), Every("new"));
    }

    [Test]
    public async Task AFirstRenewalPutsEveryKeyInPlace()
    {
        var dir = Path.Combine(_folder, "aether");
        var renewal = Path.Combine(_folder, "aether-renewal");

        var renewed = await AetherIdentityManager.RenewAsync(dir, renewal, AetherPaths.KeyFiles, _ =>
        {
            Register(renewal);
            return Task.FromResult(true);
        });

        await renewed.Should().BeTrue();
        await ShouldBe(Devices(dir), Every("new"));
    }

    [Test]
    public async Task AFailedFirstRenewalLeavesNoKeyBehind()
    {
        var dir = Path.Combine(_folder, "aether");
        var renewal = Path.Combine(_folder, "aether-renewal");

        var renewed = await AetherIdentityManager.RenewAsync(dir, renewal, AetherPaths.KeyFiles, _ =>
        {
            Register(renewal, [AetherPaths.MasqueFile]);
            return Task.FromResult(false);
        });

        await renewed.Should().BeFalse();
        await ShouldBe(Devices(dir), Every(null));
        await Exists(renewal).Should().BeFalse();
    }

    [Test]
    public async Task EachKindRegistersTheKeysOfItsProtocols()
    {
        var wireguard = AetherPaths.WireGuardFile;
        var wireguardInner = AetherPaths.WireGuardInnerFile;
        var masque = AetherPaths.MasqueFile;
        var masqueInner = AetherPaths.MasqueInnerFile;
        await ShouldBe(AetherIdentityManager.FilesOf(AetherKeyKind.AllKeys), AetherPaths.KeyFiles);
        await ShouldBe(AetherIdentityManager.FilesOf(AetherKeyKind.WireGuard), [wireguard]);
        await ShouldBe(AetherIdentityManager.FilesOf(AetherKeyKind.Masque), [masque]);
        await ShouldBe(AetherIdentityManager.FilesOf(AetherKeyKind.Gool), [wireguard, wireguardInner]);
        await ShouldBe(AetherIdentityManager.FilesOf(AetherKeyKind.Mim), [masque, masqueInner]);
        var gool = AetherPaths.MasqueGoolFile;
        await ShouldBe(AetherIdentityManager.FilesOf(AetherKeyKind.WgOverMasque), [masque, gool]);
        await ShouldBe(AetherPaths.KeyFiles, [wireguard, wireguardInner, masque, masqueInner, gool]);

        // A tunnel uses the keys its protocol registers, its outer hop the key of the one-hop protocol.
        foreach (var protocol in AetherProtocol.All)
        {
            var kind = AetherKeyKind.All.Single(it => it.Type == protocol.Type);
            await ShouldBe(AetherIdentityManager.FilesOf(protocol), AetherIdentityManager.FilesOf(kind));
        }
    }

    [Test]
    public async Task ARenewalOfTheMasqueKeyLeavesTheWireGuardKeyOfWireGuardOverMasqueAlone()
    {
        var dir = KeysInUse();
        var renewal = Path.Combine(_folder, "aether-renewal");

        var renewed = await AetherIdentityManager.RenewAsync(dir, renewal, AetherIdentityManager.FilesOf(AetherKeyKind.Masque), _ =>
        {
            // A run that left gool's WireGuard key beside the MASQUE key asked for.
            Register(renewal, [AetherPaths.MasqueFile, AetherPaths.MasqueGoolFile]);
            return Task.FromResult(true);
        });

        await renewed.Should().BeTrue();
        await ShouldBe(Devices(dir), AetherPaths.KeyFiles.Select(it => it == AetherPaths.MasqueFile ? "new" : "old").ToList());
        await (AetherIdentityManager.Status(dir, AetherProtocol.WgOverMasque).Secondary?.DeviceId).Should().BeEqualTo("old");
        await Exists(renewal).Should().BeFalse();
    }

    [Test]
    public async Task TheKeysAreReadFileByFile()
    {
        var dir = WorkDir(
            (AetherPaths.WireGuardFile, KeyFile("outer")),
            (AetherPaths.MasqueInnerFile, KeyFile("masque-inner")),
            (AetherPaths.MasqueFile, "device_id = \"\""));

        await ShouldBe(AetherIdentityManager.Keys(dir, AetherPaths.KeyFiles).Select(it => it.Identity?.DeviceId).ToList(), ["outer", null, null, "masque-inner", null]);
        await ShouldBe(AetherIdentityManager.Keys(dir, AetherPaths.KeyFiles).Select(it => it.File).ToList(), AetherPaths.KeyFiles);
    }

    [Test]
    public async Task ARenewalOfOneKindReplacesItsKeysAndLeavesTheOthersAlone()
    {
        foreach (var kind in AetherKeyKind.All)
        {
            var dir = KeysInUse();
            var renewal = Path.Combine(_folder, "aether-renewal");
            var files = AetherIdentityManager.FilesOf(kind);

            var renewed = await AetherIdentityManager.RenewAsync(dir, renewal, files, _ =>
            {
                Register(renewal, files);
                return Task.FromResult(true);
            });

            await renewed.Should().BeTrue();
            await ShouldBe(Devices(dir), AetherPaths.KeyFiles.Select(it => files.Contains(it) ? "new" : "old").ToList());
            await Exists(renewal).Should().BeFalse();
            Directory.Delete(dir, recursive: true);
        }
    }

    [Test]
    public async Task AKeyOutsideTheKindIsNeverPutInPlace()
    {
        var dir = KeysInUse();
        var renewal = Path.Combine(_folder, "aether-renewal");

        var renewed = await AetherIdentityManager.RenewAsync(dir, renewal, AetherIdentityManager.FilesOf(AetherKeyKind.WireGuard), _ =>
        {
            // The run left a MASQUE key beside the WireGuard key it was asked for.
            Register(renewal, [AetherPaths.WireGuardFile, AetherPaths.MasqueFile]);
            return Task.FromResult(true);
        });

        await renewed.Should().BeTrue();
        await ShouldBe(Devices(dir), ["new", "old", "old", "old", "old"]);
        await Exists(renewal).Should().BeFalse();
    }

    [Test]
    public async Task AMissingNewKeyOfAKindKeepsEveryOldKeyOfIt()
    {
        var dir = KeysInUse();
        var renewal = Path.Combine(_folder, "aether-renewal");

        var renewed = await AetherIdentityManager.RenewAsync(dir, renewal, AetherIdentityManager.FilesOf(AetherKeyKind.Gool), _ =>
        {
            // The outer hop key came; the inner one did not.
            Register(renewal, [AetherPaths.WireGuardFile]);
            return Task.FromResult(true);
        });

        await renewed.Should().BeFalse();
        await ShouldBe(Devices(dir), Every("old"));
        await Exists(renewal).Should().BeFalse();
    }

    [Test]
    public async Task ACoreNeedsTheKeysOfItsProtocolAndNoneWithPsiphonOrTorAlone()
    {
        static IReadOnlyList<string> Needed(params string[] arguments) => AetherIdentityManager.FilesNeededBy(arguments);
        var masque = AetherPaths.MasqueFile;
        var wireguard = AetherPaths.WireGuardFile;
        await ShouldBe(Needed("--protocol", "masque", "--h2"), [masque]);
        await ShouldBe(Needed("--protocol", "wg", "--tor"), [wireguard]);
        await ShouldBe(Needed("--wiw-outer", "162.159.192.1:2408"), [wireguard, AetherPaths.WireGuardInnerFile]);
        await ShouldBe(Needed("--protocol", "mim", "--psiphon-reverse"), [masque, AetherPaths.MasqueInnerFile]);
        // WireGuard over MASQUE needs the MASQUE key and the WireGuard key it carries, both from the WARP keys window.
        await ShouldBe(Needed("--protocol", "gool", "--gool-peer", "162.159.192.1:2408"), [masque, AetherPaths.MasqueGoolFile]);
        await ShouldBe(Needed("--protocol", "gool", "--gool-classic", "--wiw-scan"), [wireguard, AetherPaths.WireGuardInnerFile]);
        // With no protocol named the core runs MASQUE.
        await ShouldBe(Needed(), [masque]);
        await ShouldBe(Needed("--protocol", "wg", "--psiphon-only"), []);
        await ShouldBe(Needed("--tor-only"), []);
    }

    [Test]
    public async Task AKeyThatIsNotThereOrDoesNotReadAsOneIsMissing()
    {
        var dir = WorkDir(
            (AetherPaths.MasqueFile, KeyFile("masque")),
            (AetherPaths.MasqueInnerFile, "device_id = \"\""));

        await ShouldBe(AetherIdentityManager.Missing(dir, [AetherPaths.MasqueFile]), []);
        await ShouldBe(
            AetherIdentityManager.Missing(dir, [.. AetherIdentityManager.FilesOf(AetherKeyKind.Mim), AetherPaths.WireGuardFile]),
            [AetherPaths.MasqueInnerFile, AetherPaths.WireGuardFile]);
        await ShouldBe(AetherIdentityManager.Missing(dir, []), []);
    }

    [Test]
    public async Task ARenewalLeavesEverythingButTheKeysAlone()
    {
        var dir = KeysInUse();
        File.WriteAllText(Path.Combine(dir, "aether-wg-lastconn.toml"), "peer = \"162.159.192.1:2408\"");
        Directory.CreateDirectory(Path.Combine(dir, "aether-tor"));
        File.WriteAllText(Path.Combine(dir, "aether-tor", "state"), "guards");
        var renewal = Path.Combine(_folder, "aether-renewal");

        await AetherIdentityManager.RenewAsync(dir, renewal, AetherPaths.KeyFiles, _ =>
        {
            Register(renewal);
            return Task.FromResult(true);
        });

        await ShouldBe(Devices(dir), Every("new"));
        await File.ReadAllText(Path.Combine(dir, "aether-wg-lastconn.toml")).Should().BeEqualTo("peer = \"162.159.192.1:2408\"");
        await File.ReadAllText(Path.Combine(dir, "aether-tor", "state")).Should().BeEqualTo("guards");
    }
}
