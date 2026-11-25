using GitFlow.Core.Config;
using GitFlow.Core.Model;
using GitFlow.Core.Resources;
using Shouldly;

namespace GitFlow.Core.Tests;

[Collection(MessagesCulture.Collection)]
public class ConfigAndModelTests
{
    public ConfigAndModelTests() => FlowMessages.Culture = MessagesCulture.French;

    [Fact]
    public void Defaults_follow_git_flow()
    {
        GitFlowConfig.Default.ShouldBe(new GitFlowConfig("master", "develop", "feature/", "release/", "hotfix/", ""));
        GitFlowConfig.Default.Validate().ShouldBeEmpty();
    }

    [Theory]
    [InlineData("feature/login", BranchType.Feature, "login")]
    [InlineData("feature/team/login", BranchType.Feature, "team/login")]
    [InlineData("release/1.4.0", BranchType.Release, "1.4.0")]
    [InlineData("hotfix/1.4.1", BranchType.Hotfix, "1.4.1")]
    public void Parses_flow_branches(string fullName, BranchType type, string name)
    {
        GitFlowConfig.Default.TryParseFlowBranch(fullName).ShouldBe(new FlowBranch(type, name, fullName));
    }

    [Theory]
    [InlineData("develop")]
    [InlineData("master")]
    [InlineData("feature/")]
    [InlineData("bugfix/x")]
    [InlineData("support/1.0")]
    [InlineData(null)]
    public void Ignores_other_branches(string? fullName)
    {
        GitFlowConfig.Default.TryParseFlowBranch(fullName).ShouldBeNull();
    }

    [Fact]
    public void Base_branches()
    {
        var config = GitFlowConfig.Default with { MasterBranch = "main", DevelopBranch = "dev" };
        config.GetBaseBranch(BranchType.Feature).ShouldBe("dev");
        config.GetBaseBranch(BranchType.Release).ShouldBe("dev");
        config.GetBaseBranch(BranchType.Hotfix).ShouldBe("main");
    }

    [Fact]
    public void Validation_detects_errors()
    {
        var config = new GitFlowConfig("main", "main", "x/", "x/", "", "v");

        config.Validate().Select(e => e.Kind).ShouldBe(
            [ConfigValidationErrorKind.SameMasterAndDevelop, ConfigValidationErrorKind.PrefixEmpty, ConfigValidationErrorKind.DuplicatePrefix],
            ignoreOrder: true);
    }

    [Fact]
    public void Parses_null_separated_git_config()
    {
        var raw = GitFlowConfigStore.ParseNullSeparated(
            "gitflow.branch.master\nmain\0gitflow.prefix.versiontag\n\0gitflow.Prefix.Feature\nfeat/\0gitflow.flag\0");

        raw["gitflow.branch.master"].ShouldBe("main");
        raw["gitflow.prefix.versiontag"].ShouldBe("");
        raw["gitflow.prefix.feature"].ShouldBe("feat/");
        raw["gitflow.flag"].ShouldBe("");
    }

    [Fact]
    public async Task Reads_partial_config_with_defaults()
    {
        var git = new FakeGitRunner();
        git.Config["gitflow.branch.master"] = "main";
        git.Config["gitflow.branch.develop"] = "dev";

        var config = await new GitFlowConfigStore(git).ReadAsync(@"C:\repo", ConfigScope.Local);

        config.ShouldBe(new GitFlowConfig("main", "dev", "feature/", "release/", "hotfix/", ""));
    }

    [Fact]
    public async Task Missing_master_or_develop_means_not_initialized()
    {
        var git = new FakeGitRunner();
        git.Config["gitflow.prefix.feature"] = "feat/";
        var store = new GitFlowConfigStore(git);

        (await store.ReadAsync(@"C:\repo", ConfigScope.Local)).ShouldBeNull();
        (await store.GetInitDefaultsAsync(@"C:\repo")).FeaturePrefix.ShouldBe("feat/");
    }

    [Theory]
    [InlineData("1.4.0", true)]
    [InlineData("0.0.1", true)]
    [InlineData("1.4", false)]
    [InlineData("1.4.0-rc1", false)]
    [InlineData("v1.4.0", false)]
    [InlineData("1..0", false)]
    public void Parses_versions(string text, bool valid)
    {
        SemanticVersion.TryParse(text, out _).ShouldBe(valid);
    }

    [Fact]
    public void Version_increments()
    {
        new SemanticVersion(1, 4, 0).NextPatch().ToString().ShouldBe("1.4.1");
        new SemanticVersion(1, 4, 1).NextMinor().ToString().ShouldBe("1.5.0");
    }

    [Fact]
    public void Every_error_warning_and_config_error_has_a_french_message()
    {
        FlowMessages.Culture = new System.Globalization.CultureInfo("fr-FR");
        foreach (var error in Enum.GetValues<FlowError>())
        {
            FlowMessages.Get("Error_" + error).ShouldNotBe("Error_" + error);
        }

        foreach (var kind in Enum.GetValues<FlowWarningKind>())
        {
            FlowMessages.Get("Warning_" + kind).ShouldNotBe("Warning_" + kind);
        }

        foreach (var kind in Enum.GetValues<ConfigValidationErrorKind>())
        {
            FlowMessages.Get("Config_" + kind).ShouldNotBe("Config_" + kind);
        }

        FlowMessages.ReleaseReminder.ShouldBe("Release en cours : uniquement des corrections, de la doc et les tâches de release.");
    }

    [Fact]
    public void Describes_results_in_french()
    {
        FlowMessages.Describe(FlowResult.Fail(FlowError.BranchAlreadyExists, "feature/x"))
            .ShouldBe("La branche « feature/x » existe déjà.");
        FlowMessages.Describe(FlowResult.Conflicted(new MergeConflict("feature/x", "develop", ["a", "b"])))
            .ShouldStartWith("Conflits pendant le merge de « feature/x » dans « develop » (2 fichier(s))");
        FlowMessages.Describe(FlowResult.Ok()).ShouldBeEmpty();
    }

    [Theory]
    [InlineData(@"src\Program.cs", true)]
    [InlineData(@"README.md", true)]
    [InlineData(@".git\HEAD", true)]
    [InlineData(@".git\MERGE_HEAD", true)]
    [InlineData(@".git\packed-refs", true)]
    [InlineData(@".git\refs\heads\feature\x", true)]
    [InlineData(@".git\refs\heads\feature\x.lock", false)]
    [InlineData(@".git\index", false)]
    [InlineData(@".git\index.lock", false)]
    [InlineData(@".git\logs\HEAD", false)]
    [InlineData(@".git\objects\ab\cdef", false)]
    [InlineData(@"src\App\bin\Debug\App.dll", false)]
    [InlineData(@"src\App\obj\project.assets.json", false)]
    [InlineData(@".vs\GitFlow\v18\.suo", false)]
    public void Repository_change_filter(string relativePath, bool relevant)
    {
        var root = Path.Combine(Path.GetTempPath(), "repo");

        Git.RepositoryChangeFilter.IsRelevant(root, Path.Combine(root, relativePath)).ShouldBe(relevant);
    }

    [Fact]
    public void Repository_change_filter_ignores_paths_outside_the_repository()
    {
        var root = Path.Combine(Path.GetTempPath(), "repo");

        Git.RepositoryChangeFilter.IsRelevant(root, Path.Combine(Path.GetTempPath(), "other", "a.cs")).ShouldBeFalse();
        Git.RepositoryChangeFilter.IsRelevant(root, root).ShouldBeFalse();
    }

    [Fact]
    public void GitResult_command_line_quotes_arguments()
    {
        new Git.GitResult(["commit", "-m", "Hello world", ""], 0, "", "").CommandLine
            .ShouldBe("git commit -m \"Hello world\" \"\"");
    }
}
