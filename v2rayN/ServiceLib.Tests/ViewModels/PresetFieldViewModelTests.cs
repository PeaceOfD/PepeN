using ServiceLib.Tests.CoreConfig;
using ServiceLib.ViewModels;
using TUnit.Assertions.Enums;

namespace ServiceLib.Tests.ViewModels;

/// <summary>
/// PattN (design 3.3, stage 8): the preset field of the editors (PattNG PresetField): "default" first, the name of the
/// preset the field holds under it, matched as JSON whatever its key order or as cipher names whatever the spaces, and a
/// pick that asks first only when it would replace a value of the user's own. FieldPresetsTests covers the matching
/// itself; these the field around it.
/// </summary>
[NotInParallel("AppManager config")]
public class PresetFieldViewModelTests
{
    private static async Task<PresetFieldViewModel> Field(FieldPresets presets, string? text)
    {
        var field = ViewModelConfig.With(CoreConfigTestFactory.CreateConfig(), () => new PresetFieldViewModel(presets, text));
        await field.Matched;
        return field;
    }

    /// <summary>Answers the replace question with <paramref name="answer" /> and gives the questions asked.</summary>
    private static List<(string Setting, string Pick)> Answering(PresetFieldViewModel field, bool answer)
    {
        var asked = new List<(string Setting, string Pick)>();
        field.ReplaceInteraction.RegisterHandler(interaction =>
        {
            asked.Add(interaction.Input);
            interaction.SetOutput(answer);
        });
        return asked;
    }

    private static string FinalMask(int index) => Global.FinalMaskPresets[index].Value;

    private static string FinalMaskName(int index) => Global.FinalMaskPresets[index].Name;

    /// <summary><paramref name="node" /> with the keys of every object in the reverse order.</summary>
    private static JsonNode? Reversed(JsonNode? node)
    {
        switch (node)
        {
            case JsonObject obj:
                var reversed = new JsonObject();
                foreach (var (key, value) in obj.ToList().AsEnumerable().Reverse())
                {
                    reversed[key] = Reversed(value);
                }
                return reversed;

            case JsonArray array:
                return new JsonArray(array.Select(Reversed).ToArray());

            default:
                return node?.DeepClone();
        }
    }

    [Test]
    public async Task TheListShowsDefaultFirstThenThePresetsByName()
    {
        var field = await Field(FieldPresets.FinalMask, null);

        await field.PickNames.Should().BeEquivalentTo(
            [ResUI.TbPresetDefault, .. Global.FinalMaskPresets.Select(preset => preset.Name)], CollectionOrdering.Matching);
        await field.Setting.Should().BeEqualTo("finalMask");
        await field.ChooseTip.Should().BeEqualTo(string.Format(ResUI.TbPresetChoose, "finalMask"));
        await PresetFieldViewModel.PickOf(0).Should().BeEqualTo(FieldPresets.Default);
        await PresetFieldViewModel.PickOf(1).Should().BeEqualTo(0);
    }

    [Test]
    public async Task TheNameUnderTheFieldIsTheDefaultsThePresetsItHoldsOrNone()
    {
        var field = await Field(FieldPresets.FinalMask, "  ");
        await field.HeldName.Should().BeEqualTo(ResUI.TbPresetDefault);

        // JSON in another layout and key order, at every depth, holds the preset all the same.
        field.Text = Reversed(JsonNode.Parse(FinalMask(0)))!.ToJsonString();
        await field.Matched;
        await field.HeldName.Should().BeEqualTo(FinalMaskName(0));

        field.Text = """{"tcp": []}""";
        await field.Matched;
        await field.HeldName.Should().BeNull();

        // Cipher names whatever the spaces around each, and an empty name counts for none.
        var (name, value) = Global.CipherSuitesPresets[0];
        var suites = await Field(FieldPresets.CipherSuites, string.Join(" : ", value.Split(':')) + " :");
        await suites.HeldName.Should().BeEqualTo(name);
    }

    [Test]
    public async Task APickOverABlankOrAPresetValueReplacesItWithoutAsking()
    {
        var field = await Field(FieldPresets.FinalMask, string.Empty);
        var asked = Answering(field, answer: false);

        await field.ChooseAtAsync(1);
        await field.Text.Should().BeEqualTo(FinalMask(0));
        await field.Matched;
        await field.HeldName.Should().BeEqualTo(FinalMaskName(0));

        await field.ChooseAtAsync(2);
        await field.Text.Should().BeEqualTo(FinalMask(1));
        await asked.Should().BeEmpty();
    }

    [Test]
    public async Task APickOverAValueOfTheUsersOwnAsksFirst()
    {
        const string own = """{"udp": [{"type": "noise"}]}""";
        var field = await Field(FieldPresets.FinalMask, own);
        var asked = Answering(field, answer: false);

        await field.ChooseAtAsync(1);
        await field.Text.Should().BeEqualTo(own);
        await asked.Should().BeEquivalentTo([("finalMask", FinalMaskName(0))], CollectionOrdering.Matching);

        var replacing = await Field(FieldPresets.FinalMask, own);
        var yes = Answering(replacing, answer: true);
        await replacing.ChooseAtAsync(1);
        await replacing.Text.Should().BeEqualTo(FinalMask(0));
        await yes.Count.Should().BeEqualTo(1);
    }

    [Test]
    public async Task DefaultBlanksTheFieldAskingFirstOverAValueOfTheUsersOwn()
    {
        var preset = await Field(FieldPresets.FinalMask, FinalMask(1));
        var none = Answering(preset, answer: false);
        await preset.ChooseAtAsync(0);
        await preset.Text.Should().BeEqualTo(string.Empty);
        await preset.HeldName.Should().BeEqualTo(ResUI.TbPresetDefault);
        await none.Should().BeEmpty();

        var own = await Field(FieldPresets.CipherSuites, "TLS_AES_128_GCM_SHA256");
        var asked = Answering(own, answer: true);
        await own.ChooseAtAsync(0);
        await own.Text.Should().BeEqualTo(string.Empty);
        await asked.Should().BeEquivalentTo([("cipherSuites", ResUI.TbPresetDefault)], CollectionOrdering.Matching);
    }

    [Test]
    public async Task UnansweredTheQuestionReplacesNothing()
    {
        const string own = "TLS_AES_128_GCM_SHA256";
        var field = await Field(FieldPresets.CipherSuites, own);

        await field.ChooseAtAsync(1);

        await field.Text.Should().BeEqualTo(own);
    }
}
