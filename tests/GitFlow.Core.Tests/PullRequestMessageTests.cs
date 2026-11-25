using GitFlow.Core.Model;
using GitFlow.Core.PullRequests;
using GitFlow.Core.Resources;
using Shouldly;

namespace GitFlow.Core.Tests;

[Collection(MessagesCulture.Collection)]
public class PullRequestMessageTests
{
    private static readonly string Nl = Environment.NewLine;

    public PullRequestMessageTests() => FlowMessages.Culture = MessagesCulture.French;

    private static PullRequestContext Feature(string name, string[] commits, string messages = "", DiffSummary? diff = null) =>
        new(new FlowBranch(BranchType.Feature, name, "feature/" + name), "develop", commits, messages, diff ?? new DiffSummary(2, 10, 3), null);

    [Theory]
    [InlineData(BranchType.Feature, "user-login_page", "Feature: user login page")]
    [InlineData(BranchType.Release, "1.4.0", "Release 1.4.0")]
    [InlineData(BranchType.Hotfix, "1.4.1", "Hotfix 1.4.1")]
    public void Titles(BranchType type, string name, string expected)
    {
        PullRequestMessageGenerator.Title(new FlowBranch(type, name, "x/" + name)).ShouldBe(expected);
    }

    [Fact]
    public void Feature_with_several_commits()
    {
        var draft = PullRequestMessageGenerator.Build(Feature("login", ["Ajoute la page", "Corrige le style"]));

        draft.Source.ShouldBe("feature/login");
        draft.Target.ShouldBe("develop");
        draft.Title.ShouldBe("Feature: login");
        draft.Description.ShouldBe(string.Join(Nl,
            "**Feature** `feature/login` → `develop`",
            "",
            "### Commits (2)",
            "- Ajoute la page",
            "- Corrige le style",
            "",
            "### Fichiers",
            "2 fichier(s) modifié(s), 10 ligne(s) ajoutée(s), 3 ligne(s) supprimée(s)"));
    }

    [Fact]
    public void No_commit()
    {
        var draft = PullRequestMessageGenerator.Build(Feature("empty", [], diff: DiffSummary.Empty));

        draft.Description.ShouldContain("### Commits (0)" + Nl + "_Aucun commit._");
        draft.Description.ShouldContain("0 fichier(s) modifié(s)");
    }

    [Fact]
    public void Release_mentions_version_and_tag()
    {
        var context = new PullRequestContext(new FlowBranch(BranchType.Release, "1.4.0", "release/1.4.0"), "master", ["Bump"], "Bump", DiffSummary.Empty, "v1.4.0");

        var draft = PullRequestMessageGenerator.Build(context);

        draft.Title.ShouldBe("Release 1.4.0");
        draft.Description.ShouldContain("### Version" + Nl + "Version **1.4.0** : le tag `v1.4.0` sera créé");
    }

    [Fact]
    public void Tickets_from_branch_and_commit_messages()
    {
        var draft = PullRequestMessageGenerator.Build(
            Feature("ABC-12-login", ["Fix #42", "Suite"], messages: "Fix #42\n\nVoir aussi XY-7 et ABC-12"));

        draft.Description.ShouldEndWith("### Tickets" + Nl + "ABC-12, #42, XY-7");
    }

    [Fact]
    public void Custom_ticket_pattern_and_invalid_pattern_fallback()
    {
        var context = Feature("login", ["US1234 done"], messages: "US1234 done #42");

        PullRequestMessageGenerator.FindTickets(context, @"US\d+").ShouldBe(["US1234"]);
        PullRequestMessageGenerator.FindTickets(context, "([").ShouldBe(["#42"]);
    }

    [Fact]
    public void Special_characters_are_kept_verbatim()
    {
        var draft = PullRequestMessageGenerator.Build(Feature("x", ["Gère « l'été » & <tags> `code` *gras*"]));

        draft.Description.ShouldContain("- Gère « l'été » & <tags> `code` *gras*");
    }

    [Fact]
    public void Template_with_variables_is_filled()
    {
        var template = "## {type} {source} → {target}\n{commits}\n{stats}\nv={version} t={tickets}";

        var draft = PullRequestMessageGenerator.Build(Feature("ABC-1-x", ["One"]), template);

        draft.Description.ShouldBe("## Feature feature/ABC-1-x → develop\n- One\n2 fichier(s) modifié(s), 10 ligne(s) ajoutée(s), 3 ligne(s) supprimée(s)\nv= t=ABC-1");
    }

    [Fact]
    public void Platform_template_without_variables_is_used_as_base()
    {
        var draft = PullRequestMessageGenerator.Build(Feature("x", ["One"]), "## Checklist\n- [ ] Tests");

        draft.Description.ShouldStartWith("## Checklist\n- [ ] Tests" + Nl + Nl + "---" + Nl + Nl + "**Feature** `feature/x`");
    }

    [Theory]
    [InlineData(" 5 files changed, 120 insertions(+), 8 deletions(-)", 5, 120, 8)]
    [InlineData(" 1 file changed, 1 insertion(+)", 1, 1, 0)]
    [InlineData(" 1 file changed, 2 deletions(-)", 1, 0, 2)]
    [InlineData("", 0, 0, 0)]
    public void Parses_shortstat(string text, int files, int insertions, int deletions)
    {
        DiffSummary.Parse(text).ShouldBe(new DiffSummary(files, insertions, deletions));
    }
}
