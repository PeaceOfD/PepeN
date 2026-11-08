using ServiceLib.Tests.CoreConfig;
using ServiceLib.ViewModels;
using TUnit.Assertions.Enums;

namespace ServiceLib.Tests.ViewModels;

/// <summary>
/// PattN (design 3.2.2 row 15, stage 8): the exit-node list of the Aether editor and the WARP keys window, from PattNG
/// ExitNodesTest and ExitNodeField: freedom first, each name once and unique, and under the field the hint, or why the
/// name chosen cannot be used: no profile has it any more, or several have it. A name is never replaced by a guess.
/// </summary>
[NotInParallel("AppManager config")]
public class AetherExitNodeFieldTests
{
    /// <summary>As ResUI.TbLabelNumbered writes it in English.</summary>
    private static string Numbered(string name, int number) => $"{name} ({number})";

    private static readonly List<AetherExitNode> Nodes = [new("germany", 1), new("france", 2)];

    private static ExitNodeFieldViewModel Field(string? name, IReadOnlyList<AetherExitNode>? nodes)
    {
        var field = ViewModelConfig.With(CoreConfigTestFactory.CreateConfig(), () => new ExitNodeFieldViewModel(name));
        field.SetNodes(nodes);
        return field;
    }

    [Test]
    public async Task EachLabelIsTheNameMadeUniqueWhereItIsTakenAlready()
    {
        await AetherExitNodes.DistinctLabels(
                ["germany", "freedom (default)", "france", "freedom (default) (2)"],
                ["freedom (default)"],
                Numbered)
            .Should().BeEquivalentTo(["germany", "freedom (default) (2)", "france", "freedom (default) (2) (2)"], CollectionOrdering.Matching);
        await AetherExitNodes.DistinctLabels([], ["freedom (default)"], Numbered).Should().BeEmpty();
        // The number is written as the language of the app writes it.
        await AetherExitNodes.DistinctLabels(["freedom（默认）"], ["freedom（默认）"], (name, number) => $"{name}（{number}）")
            .Should().BeEquivalentTo(["freedom（默认）（2）"], CollectionOrdering.Matching);
    }

    [Test]
    public async Task ANameNoProfileHasAnyMoreOrSeveralHaveIsTold()
    {
        await AetherExitNodes.ProblemOf("germany", Nodes).Should().BeNull();
        await AetherExitNodes.ProblemOf(" germany ", Nodes).Should().BeNull();
        await AetherExitNodes.ProblemOf("france", Nodes).Should().BeEqualTo(ExitNodeLookup.SameName.Instance);
        await AetherExitNodes.ProblemOf("spain", Nodes).Should().BeEqualTo(ExitNodeLookup.NotFound.Instance);
        await AetherExitNodes.ProblemOf("germany", []).Should().BeEqualTo(ExitNodeLookup.NotFound.Instance);
        // Freedom is no name, and nothing is told before the names are read.
        await AetherExitNodes.ProblemOf(string.Empty, Nodes).Should().BeNull();
        await AetherExitNodes.ProblemOf("  ", Nodes).Should().BeNull();
        await AetherExitNodes.ProblemOf("spain", null).Should().BeNull();
        await AetherExitNodes.MessageOf(ExitNodeLookup.SameName.Instance, "france")
            .Should().BeEqualTo(string.Format(ResUI.MsgAetherExitNodeDuplicate, "france"));
    }

    [Test]
    public async Task FreedomComesFirstAndShowsNothingUnderTheField()
    {
        var field = Field(null, Nodes);

        await field.Labels.Should().BeEquivalentTo([ResUI.TbAetherExitNodeFreedom, "germany", "france"], CollectionOrdering.Matching);
        await field.SelectedIndex.Should().BeEqualTo(0);
        await field.IsFreedom.Should().BeTrue();
        await field.SupportingText.Should().BeNull();
        await field.HasProblem.Should().BeFalse();
    }

    [Test]
    public async Task APickSetsTheNameAndTheHintOrTheProblemShowsUnderIt()
    {
        var field = Field(string.Empty, Nodes);

        field.SelectedIndex = 1;
        await field.Name.Should().BeEqualTo("germany");
        await field.IsFreedom.Should().BeFalse();
        await field.SupportingText.Should().BeEqualTo(ResUI.TbAetherExitNodeHint);
        await field.HasProblem.Should().BeFalse();

        // Several profiles have the name: the field says so, and keeps it.
        field.SelectedIndex = 2;
        await field.Name.Should().BeEqualTo("france");
        await field.SupportingText.Should().BeEqualTo(string.Format(ResUI.MsgAetherExitNodeDuplicate, "france"));
        await field.HasProblem.Should().BeTrue();

        // A list that is replaced picks nothing for a moment, which changes nothing.
        field.SelectedIndex = -1;
        await field.Name.Should().BeEqualTo("france");

        field.SelectedIndex = 0;
        await field.Name.Should().BeEqualTo(string.Empty);
        await field.SupportingText.Should().BeNull();
    }

    [Test]
    public async Task ANameNoProfileHasAnyMoreStaysOnShowWithWhyItCannotBeUsed()
    {
        var field = Field("spain", Nodes);

        await field.Labels.Should().BeEquivalentTo([ResUI.TbAetherExitNodeFreedom, "germany", "france", "spain"], CollectionOrdering.Matching);
        await field.SelectedIndex.Should().BeEqualTo(3);
        await field.SupportingText.Should().BeEqualTo(string.Format(ResUI.MsgAetherExitNodeNotFound, "spain"));
        await field.HasProblem.Should().BeTrue();

        // Once another is chosen, it is no longer offered.
        field.SelectedIndex = 1;
        await field.Labels.Should().BeEquivalentTo([ResUI.TbAetherExitNodeFreedom, "germany", "france"], CollectionOrdering.Matching);
        await field.SelectedIndex.Should().BeEqualTo(1);
    }

    [Test]
    public async Task BeforeTheNamesAreReadANameShowsAsItIsWithTheHint()
    {
        var field = Field(" germany ", null);

        await field.Labels.Should().BeEquivalentTo([ResUI.TbAetherExitNodeFreedom, "germany"], CollectionOrdering.Matching);
        await field.SelectedIndex.Should().BeEqualTo(1);
        await field.SupportingText.Should().BeEqualTo(ResUI.TbAetherExitNodeHint);
        // The name is stored as it was written.
        await field.Name.Should().BeEqualTo(" germany ");

        field.SetNodes(Nodes);
        await field.Labels.Should().BeEquivalentTo([ResUI.TbAetherExitNodeFreedom, "germany", "france"], CollectionOrdering.Matching);
        await field.SelectedIndex.Should().BeEqualTo(1);
    }

    [Test]
    public async Task ANameLikeTheFreedomLabelIsToldApartFromIt()
    {
        var freedom = ResUI.TbAetherExitNodeFreedom;
        var field = Field(freedom, [new AetherExitNode(freedom, 1)]);

        await field.Labels.Should().BeEquivalentTo([freedom, string.Format(ResUI.TbLabelNumbered, freedom, 2)], CollectionOrdering.Matching);
        await field.SelectedIndex.Should().BeEqualTo(1);
        await field.IsFreedom.Should().BeFalse();
    }
}
