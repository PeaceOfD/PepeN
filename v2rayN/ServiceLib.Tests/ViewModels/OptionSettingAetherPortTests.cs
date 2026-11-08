using ReactiveUI.Primitives;
using ReactiveUI.Primitives.Extensions;
using ServiceLib.Tests.CoreConfig;
using ServiceLib.ViewModels;

namespace ServiceLib.Tests.ViewModels;

/// <summary>
/// PattN (design 2.5.1 and 3.7, PattNG SettingsViewModel.validateAetherListenPort): the Aether listen port in "Core: basic
/// settings". It has to be a port from 1024 to 65532, and neither it nor the three ports after it, which the core takes as
/// well, may be one of the local ports of the app, the mixed port L and the six after it (critique 10). The same check runs
/// when the mixed port moves, the new L against the Aether listen port of the dialog. A refused value is not stored. An
/// overlap the settings already hold is refused only by a save that moves one of the two ports (E3).
/// </summary>
public class OptionSettingAetherPortTests
{
    private const int L = 10808;

    [Test]
    public async Task TheAetherListenPortIsAPortFrom1024To65532()
    {
        foreach (var text in new[] { "1023", "65533", "abc", "", " ", "0", "-1", "10819.5", "70000", "99999999999" })
        {
            await OptionSettingViewModel.AetherPortsProblem(L, text).Should().BeEqualTo(ResUI.FillAetherListenPort).Because($"\"{text}\"");
        }
        await OptionSettingViewModel.AetherPortsProblem(L, null).Should().BeEqualTo(ResUI.FillAetherListenPort);
        await OptionSettingViewModel.AetherPortsProblem(L, "1024").Should().BeNull();
        await OptionSettingViewModel.AetherPortsProblem(L, "65532").Should().BeNull();
    }

    [Test]
    public async Task TheAetherListenPortAndTheThreeAfterItStayClearOfTheMixedPortAndTheSixAfterIt()
    {
        // P..P+3 reaching L from below, and P inside L..L+6 up to L+6 itself.
        foreach (var port in new[] { L - 3, L - 1, L, L + 3, L + 6 })
        {
            await OptionSettingViewModel.AetherPortsProblem(L, port.ToString()).Should().BeEqualTo(ResUI.MsgAetherListenPortLocalProxy).Because($"P = {port}");
        }
        await OptionSettingViewModel.AetherPortsProblem(L, (L - 4).ToString()).Should().BeNull();
        await OptionSettingViewModel.AetherPortsProblem(L, (L + 7).ToString()).Should().BeNull();
    }

    [Test]
    public async Task AMixedPortMovedOntoTheAetherPortsIsRefusedToo()
    {
        const int p = AetherPorts.DefaultListenPort;
        // The new L against the Aether listen port of the dialog: L+6 reaching P from below, and L inside P..P+3.
        foreach (var localPort in new[] { p - 6, p - 3, p, p + 3 })
        {
            await OptionSettingViewModel.AetherPortsProblem(localPort, p.ToString()).Should().BeEqualTo(ResUI.MsgAetherListenPortLocalProxy).Because($"L = {localPort}");
        }
        await OptionSettingViewModel.AetherPortsProblem(p - 7, p.ToString()).Should().BeNull();
        await OptionSettingViewModel.AetherPortsProblem(p + 4, p.ToString()).Should().BeNull();
    }

    [Test]
    public async Task AValidChangeHasNoProblem()
    {
        await OptionSettingViewModel.AetherPortsProblem(L, "20808").Should().BeNull();
        await OptionSettingViewModel.AetherPortsProblem(L, " 20808 ").Should().BeNull();
        await OptionSettingViewModel.AetherPortsProblem(20000, AetherPorts.DefaultListenPort.ToString()).Should().BeNull();
    }

    [Test]
    public async Task AValidPortIsStoredAsTheNumberItNames()
    {
        // What SaveCmd writes once the port passed the check: blanks around it and the digits of another script read as
        // PattNG reads them, the ends of the range kept.
        await OptionSettingViewModel.StoredAetherListenPort(" 20808 ").Should().BeEqualTo(20808);
        await OptionSettingViewModel.StoredAetherListenPort("２０８０８").Should().BeEqualTo(20808);
        await OptionSettingViewModel.StoredAetherListenPort("۲۰۸۰۸").Should().BeEqualTo(20808);
        await OptionSettingViewModel.StoredAetherListenPort("1024").Should().BeEqualTo(1024);
        await OptionSettingViewModel.StoredAetherListenPort("65532").Should().BeEqualTo(65532);
        foreach (var text in new[] { "20808", " 20808 ", "２０８０８", "1024", "65532" })
        {
            await OptionSettingViewModel.AetherPortsProblem(L, text).Should().BeNull().Because($"\"{text}\"");
        }
    }

    [Test]
    [NotInParallel("AppManager config")]
    public async Task TheDialogShowsThePortTheCoresUse()
    {
        foreach (var (stored, shown) in new (int?, string)[] { (20808, "20808"), (null, "10819"), (80, "10819"), (65533, "10819") })
        {
            var config = CoreConfigTestFactory.CreateConfig();
            config.CoreBasicItem.AetherListenPort = stored;
            var vm = ViewModelConfig.With(config, () => new OptionSettingViewModel());
            await vm.AetherListenPort.Should().BeEqualTo(shown).Because($"stored {stored}");
        }
    }

    [Test]
    [NotInParallel("AppManager config")]
    public async Task ARefusedPortIsNotStoredAndSaysWhy()
    {
        // (the Aether listen port of the dialog, its mixed port, the message)
        var refused = new (string Port, int LocalPort, string Message)[]
        {
            ("1023", L, ResUI.FillAetherListenPort),
            ("65533", L, ResUI.FillAetherListenPort),
            ("abc", L, ResUI.FillAetherListenPort),
            ((L + 6).ToString(), L, ResUI.MsgAetherListenPortLocalProxy),
            ("20808", 20808 - 6, ResUI.MsgAetherListenPortLocalProxy),
            ("20808", 20808 + 3, ResUI.MsgAetherListenPortLocalProxy),
        };
        foreach (var (port, localPort, message) in refused)
        {
            var config = CoreConfigTestFactory.CreateConfig();
            config.CoreBasicItem.AetherListenPort = 20808;
            var vm = ViewModelConfig.With(config, () => new OptionSettingViewModel());
            vm.AetherListenPort = port;
            vm.LocalPort = localPort;
            // Other tests may show messages meanwhile, from other threads.
            var shown = new ConcurrentQueue<string>();
            using (AppEvents.SendSnackMsgRequested.AsObservable().Subscribe(shown.Enqueue))
            {
                await vm.SaveCmd.Execute().ToTask().WaitAsync(TimeSpan.FromSeconds(10));
            }

            await shown.Should().Contain(message).Because($"P = {port}, L = {localPort}");
            await shown.Should().NotContain(ResUI.OperationSuccess);
            await config.CoreBasicItem.AetherListenPort.Should().BeEqualTo(20808);
            await config.Inbound[0].LocalPort.Should().BeEqualTo(L);
        }
    }

    // ---- E3: an overlap the settings already hold

    /// <summary>A stored Aether listen port whose ports, P..P+3 = L+2..L+5, lie among the app's local ports L..L+6.</summary>
    private const int StoredOverlap = L + 2;

    [Test]
    public async Task AnOverlapStoredEarlierIsRefusedOnlyWhenTheSaveMovesAPortIntoOne()
    {
        // Both ports as they were: the save goes through; the start of an Aether session refuses the overlap.
        await OptionSettingViewModel.AetherPortsSaveProblem(L, StoredOverlap.ToString(), L, StoredOverlap).Should().BeNull();
        await OptionSettingViewModel.AetherPortsSaveProblem(L, $" {StoredOverlap} ", L, StoredOverlap).Should().BeNull();
        // Either port moved into another overlap: refused.
        await OptionSettingViewModel.AetherPortsSaveProblem(L, (StoredOverlap + 1).ToString(), L, StoredOverlap)
            .Should().BeEqualTo(ResUI.MsgAetherListenPortLocalProxy);
        await OptionSettingViewModel.AetherPortsSaveProblem(L + 1, StoredOverlap.ToString(), L, StoredOverlap)
            .Should().BeEqualTo(ResUI.MsgAetherListenPortLocalProxy);
        await OptionSettingViewModel.AetherPortsSaveProblem(L - 1, (StoredOverlap + 1).ToString(), L, StoredOverlap)
            .Should().BeEqualTo(ResUI.MsgAetherListenPortLocalProxy);
        // Either port moved out of it: nothing wrong.
        await OptionSettingViewModel.AetherPortsSaveProblem(L, "20808", L, StoredOverlap).Should().BeNull();
        await OptionSettingViewModel.AetherPortsSaveProblem(StoredOverlap + 4, StoredOverlap.ToString(), L, StoredOverlap).Should().BeNull();
        // A value that is no port is refused whatever was stored, the port it falls back to among them.
        foreach (var stored in new[] { StoredOverlap, AetherPorts.DefaultListenPort })
        {
            await OptionSettingViewModel.AetherPortsSaveProblem(L, "abc", L, stored).Should().BeEqualTo(ResUI.FillAetherListenPort);
        }
    }

    [Test]
    public async Task WithNoOverlapStoredASaveThatMovesAPortIntoOneIsRefused()
    {
        const int p = AetherPorts.DefaultListenPort;
        await OptionSettingViewModel.AetherPortsSaveProblem(L, p.ToString(), L, p).Should().BeNull();
        await OptionSettingViewModel.AetherPortsSaveProblem(L, (L + 6).ToString(), L, p).Should().BeEqualTo(ResUI.MsgAetherListenPortLocalProxy);
        await OptionSettingViewModel.AetherPortsSaveProblem(p - 3, p.ToString(), L, p).Should().BeEqualTo(ResUI.MsgAetherListenPortLocalProxy);
        await OptionSettingViewModel.AetherPortsSaveProblem(p, p.ToString(), L, p).Should().BeEqualTo(ResUI.MsgAetherListenPortLocalProxy);
    }

    [Test]
    [NotInParallel("AppManager config")]
    public async Task AnOverlapStoredEarlierStopsOnlyTheSaveThatMovesAPort()
    {
        // A fragment length that is no number stops the save at the check after the ports, before anything is stored (a
        // save that goes through writes the settings file and the autostart task): a save that gets there passed the
        // port check.
        foreach (var (port, localPort, message) in new (string Port, int LocalPort, string Message)[]
                 {
                     (StoredOverlap.ToString(), L, ResUI.FillFragmentParameterError),
                     ((StoredOverlap + 1).ToString(), L, ResUI.MsgAetherListenPortLocalProxy),
                     (StoredOverlap.ToString(), L + 1, ResUI.MsgAetherListenPortLocalProxy),
                 })
        {
            var config = CoreConfigTestFactory.CreateConfig();
            config.CoreBasicItem.AetherListenPort = StoredOverlap;
            var vm = ViewModelConfig.With(config, () => new OptionSettingViewModel());
            await vm.AetherListenPort.Should().BeEqualTo(StoredOverlap.ToString());
            vm.AetherListenPort = port;
            vm.LocalPort = localPort;
            vm.FragmentLengths = "not a length";
            // Other tests may show messages meanwhile, from other threads.
            var shown = new ConcurrentQueue<string>();
            using (AppEvents.SendSnackMsgRequested.AsObservable().Subscribe(shown.Enqueue))
            {
                await vm.SaveCmd.Execute().ToTask().WaitAsync(TimeSpan.FromSeconds(10));
            }

            await shown.Should().Contain(message).Because($"P = {port}, L = {localPort}");
            if (message != ResUI.MsgAetherListenPortLocalProxy)
            {
                await shown.Should().NotContain(ResUI.MsgAetherListenPortLocalProxy);
            }
            await shown.Should().NotContain(ResUI.OperationSuccess);
            await config.CoreBasicItem.AetherListenPort.Should().BeEqualTo(StoredOverlap);
            await config.Inbound[0].LocalPort.Should().BeEqualTo(L);
        }
    }
}
