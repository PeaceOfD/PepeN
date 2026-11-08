using ReactiveUI.Primitives;
using ReactiveUI.Primitives.Extensions;
using ServiceLib.Tests.CoreConfig;
using ServiceLib.Tests.CoreConfig.Context;
using ServiceLib.ViewModels;
using static ServiceLib.Tests.CoreConfig.V2ray.AetherConfigTests;

namespace ServiceLib.Tests.ViewModels;

/// <summary>
/// PattN (N1, N2; PattNG SubEditViewModel and RoutingEditViewModel): the subscription editor and the routing rule editor
/// look the profile names they save up as the start finds them, the spaces around them aside: a name no profile has, as
/// after a rename or a delete, or several have, is told with PattNG's words rather than saved, and the editor stays open.
/// The editors are made where no UI thread is. Every name is new to the test, as the database is shared; a subscription a
/// test saves is removed after it.
/// </summary>
[NotInParallel("AppManager config")]
public class ProfileNameEditorTests
{
    private static string NewName(string prefix)
    {
        return $"{prefix}-{Guid.NewGuid():N}";
    }

    private static async Task Store(params ProfileItem[] profiles)
    {
        SQLiteHelper.Instance.CreateTable<ProfileItem>();
        foreach (var profile in profiles)
        {
            await SQLiteHelper.Instance.ReplaceAsync(profile);
        }
    }

    /// <summary>Two profiles of <paramref name="name" />, the spaces around it aside, which it names neither of.</summary>
    private static ProfileItem[] Twins(string name)
    {
        return [Vless(NewName("twin"), name), Vless(NewName("twin"), $" {name}")];
    }

    /// <summary>What saving with <paramref name="save" /> showed, and whether the editor closed.</summary>
    private static async Task<(List<string> Shown, bool Closed)> Saved(ICloseable editor, ReactiveCommand<RxVoid, RxVoid> save)
    {
        var shown = new ConcurrentQueue<string>();
        var closed = false;
        editor.RequestClose += (_, _) => closed = true;
        using (AppEvents.SendSnackMsgRequested.AsObservable().Subscribe(shown.Enqueue))
        {
            await save.Execute().ToTask();
        }
        return ([.. shown], closed);
    }

    // ---- the subscription editor (N1)

    private static async Task<SubEditViewModel> SubEditor(string remarks, string? prev, string? next)
    {
        // The editor looks the names up among the profiles and saves into the subscriptions: tables the app's start makes,
        // which a test can be the first to use.
        SQLiteHelper.Instance.CreateTable<ProfileItem>();
        SQLiteHelper.Instance.CreateTable<SubItem>();
        var vm = await Task.Run(() => ViewModelConfig.With(CoreConfigTestFactory.CreateConfig(), () =>
            new SubEditViewModel(new SubItem { Remarks = remarks, Url = string.Empty })));
        vm.PrevProfile = prev!;
        vm.NextProfile = next!;
        return vm;
    }

    private static async Task<SubItem?> StoredSubscription(string remarks)
    {
        SQLiteHelper.Instance.CreateTable<SubItem>();
        return await SQLiteHelper.Instance.TableAsync<SubItem>().FirstOrDefaultAsync(sub => sub.Remarks == remarks);
    }

    [Test]
    [Arguments(false, false)]
    [Arguments(false, true)]
    [Arguments(true, false)]
    [Arguments(true, true)]
    public async Task TheSubscriptionEditorRefusesANameNoneOrSeveralHave(bool several, bool asNext)
    {
        var named = NewName("named");
        if (several)
        {
            await Store(Twins(named));
        }
        var remarks = NewName("sub");
        var vm = await SubEditor(remarks, asNext ? null : $" {named} ", asNext ? named : null);

        var (shown, closed) = await Saved(vm, vm.SaveCmd);

        await shown.Should().Contain(string.Format(several ? ResUI.MsgRoutingRuleTargetDuplicate : ResUI.MsgRoutingRuleTargetNotFound, named));
        await closed.Should().BeFalse();
        await (await StoredSubscription(remarks)).Should().BeNull();
    }

    [Test]
    public async Task TheSubscriptionEditorSavesNamesThatEachFindOneProfileOrNone()
    {
        var prev = Vless(NewName("prev"), NewName("prev"));
        var next = Vless(NewName("next"), NewName("next"));
        await Store(prev, next);
        var remarks = NewName("sub");
        var blank = NewName("sub");
        try
        {
            var vm = await SubEditor(remarks, $"{prev.Remarks} ", $" {next.Remarks}");
            var (shown, closed) = await Saved(vm, vm.SaveCmd);
            await closed.Should().BeTrue().Because(string.Join(" | ", shown));
            await (await StoredSubscription(remarks))!.NextProfile.Should().BeEqualTo($" {next.Remarks}");

            // A blank name names none.
            var blankVm = await SubEditor(blank, "   ", string.Empty);
            var (blankShown, blankClosed) = await Saved(blankVm, blankVm.SaveCmd);
            await blankClosed.Should().BeTrue().Because(string.Join(" | ", blankShown));
        }
        finally
        {
            foreach (var name in new[] { remarks, blank })
            {
                if (await StoredSubscription(name) is { } saved)
                {
                    await SQLiteHelper.Instance.DeleteAsync(saved);
                }
            }
        }
    }

    [Test]
    public async Task TheSubscriptionEditorCountsNoCustomConfiguration()
    {
        // A custom configuration is no hop (PattNG takesAsHop), as the editor's picker hides it: beside a profile of its
        // name it makes no second one, and a name only it has finds no profile.
        var name = NewName("landing");
        var onlyCustom = NewName("only-custom");
        await Store(SubscriptionNameTests.Custom(name), Vless(NewName("landing"), name), SubscriptionNameTests.Custom(onlyCustom));
        var refused = NewName("sub");
        var remarks = NewName("sub");
        try
        {
            var refusedVm = await SubEditor(refused, onlyCustom, null);
            var (refusedShown, refusedClosed) = await Saved(refusedVm, refusedVm.SaveCmd);
            await refusedShown.Should().Contain(string.Format(ResUI.MsgRoutingRuleTargetNotFound, onlyCustom));
            await refusedClosed.Should().BeFalse();
            await (await StoredSubscription(refused)).Should().BeNull();

            var vm = await SubEditor(remarks, null, name);
            var (shown, closed) = await Saved(vm, vm.SaveCmd);
            await closed.Should().BeTrue().Because(string.Join(" | ", shown));
            await (await StoredSubscription(remarks))!.NextProfile.Should().BeEqualTo(name);
        }
        finally
        {
            foreach (var stored in new[] { refused, remarks })
            {
                if (await StoredSubscription(stored) is { } saved)
                {
                    await SQLiteHelper.Instance.DeleteAsync(saved);
                }
            }
        }
    }

    // ---- the routing rule editor (N2)

    private const string Before = "direct";

    /// <summary>The editor of a stored rule sending example.com to <see cref="Before" />, set to send it to <paramref name="target" />.</summary>
    private static async Task<RoutingRuleDetailsViewModel> RuleEditor(string target, bool enabled = true, ERuleType? ruleType = null)
    {
        var rule = new RulesItem { Id = NewName("rule"), Remarks = "rule", OutboundTag = Before, Enabled = true, Domain = ["example.com"] };
        var vm = await Task.Run(() => ViewModelConfig.With(CoreConfigTestFactory.CreateConfig(), () => new RoutingRuleDetailsViewModel(rule)));
        vm.OutboundTag = target;
        vm.Enabled = enabled;
        vm.RuleType = ruleType?.ToString();
        return vm;
    }

    [Test]
    public async Task TheRuleEditorRefusesATargetNoProfileOrSeveralHaveAndLeavesTheRuleAsItWas()
    {
        var missing = NewName("missing");
        var shared = NewName("shared");
        // A custom configuration is no profile a rule can send to: its name finds none.
        var customName = NewName("custom");
        var custom = CoreConfigTestFactory.CreateCustomOutboundNode(ECoreType.Xray, NewName("custom"), customName);
        custom.ConfigType = EConfigType.Custom;
        await Store([custom, .. Twins(shared)]);

        foreach (var (target, problem) in new[]
                 {
                     (missing, string.Format(ResUI.MsgRoutingRuleTargetNotFound, missing)),
                     ($"{shared}  ", string.Format(ResUI.MsgRoutingRuleTargetDuplicate, shared)),
                     (customName, string.Format(ResUI.MsgRoutingRuleTargetNotFound, customName)),
                 })
        {
            var vm = await RuleEditor(target);
            var (shown, closed) = await Saved(vm, vm.SaveCmd);

            await shown.Should().Contain(problem);
            await closed.Should().BeFalse();
            // The list holds the rule being edited: a refused save does not write it.
            await vm.SelectedSource.OutboundTag.Should().BeEqualTo(Before);
        }
        // A rule of the routing alone sends traffic as one of both does.
        var routing = await RuleEditor(missing, ruleType: ERuleType.Routing);
        await (await Saved(routing, routing.SaveCmd)).Closed.Should().BeFalse();
    }

    [Test]
    public async Task TheRuleEditorSavesWhatTheStartTakes()
    {
        var name = NewName("found");
        var found = Vless(NewName("found"), name);
        await Store(found);
        var missing = NewName("missing");

        foreach (var (vm, why) in new[]
                 {
                     (await RuleEditor($" {name} "), "one profile has the name"),
                     (await RuleEditor(Global.ProxyTag), "proxy"),
                     (await RuleEditor(Global.DirectTag), "direct"),
                     (await RuleEditor(Global.BlockTag), "block"),
                     (await RuleEditor(string.Empty), "none: the start sends it to the proxy"),
                     (await RuleEditor(missing, enabled: false), "a rule turned off: the start looks at none"),
                     (await RuleEditor(missing, ruleType: ERuleType.DNS), "a rule of the DNS alone, which sends no traffic"),
                 })
        {
            var (shown, closed) = await Saved(vm, vm.SaveCmd);
            await closed.Should().BeTrue().Because($"{why}: {string.Join(" | ", shown)}");
            await vm.SelectedSource.OutboundTag.Should().BeEqualTo(vm.OutboundTag).Because(why);
        }
    }
}
