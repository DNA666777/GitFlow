using GitFlow.Core.Config;
using GitFlow.Core.Model;
using GitFlow.Core.Resources;
using Shouldly;

namespace GitFlow.Core.Tests;

[Collection(MessagesCulture.Collection)]
public class SettingsTests
{
    private const string Repo = @"C:\repo";

    public SettingsTests() => FlowMessages.Culture = MessagesCulture.French;

    private static FakeGitRunner WithVsKeys(FakeGitRunner git, string? local = null, string? global = null)
    {
        git.On(@"config --local --null --get-regexp ^gitflowvs\.", exitCode: local is null ? 1 : 0, stdout: local ?? "");
        git.On(@"config --global --null --get-regexp ^gitflowvs\.", exitCode: global is null ? 1 : 0, stdout: global ?? "");
        return git;
    }

    [Fact]
    public async Task Finish_defaults_are_off_by_default()
    {
        var git = WithVsKeys(FakeGitRunner.Initialized());

        (await new GitFlowSettingsService(git).ReadFinishDefaultsAsync(Repo, ConfigScope.Local)).ShouldBe(FinishDefaults.None);
    }

    [Fact]
    public async Task Local_finish_defaults_override_global_per_option()
    {
        var git = WithVsKeys(
            FakeGitRunner.Initialized(),
            local: "gitflowvs.finish.push\nfalse\0",
            global: "gitflowvs.finish.push\ntrue\0gitflowvs.finish.deleteremote\nyes\0");
        var service = new GitFlowSettingsService(git);

        (await service.ReadFinishDefaultsAsync(Repo, ConfigScope.Local)).ShouldBe(new FinishDefaults(false, true));
        (await service.ReadFinishDefaultsAsync(Repo, ConfigScope.Global)).ShouldBe(new FinishDefaults(true, true));
    }

    [Fact]
    public async Task Save_local_writes_naming_then_finish_defaults()
    {
        var git = WithVsKeys(FakeGitRunner.Initialized());
        var settings = new GitFlowSettings(GitFlowConfig.Default with { FeaturePrefix = "feat/", VersionTagPrefix = "v" }, new FinishDefaults(true, false));

        var result = await new GitFlowSettingsService(git).SaveAsync(Repo, settings, ConfigScope.Local);

        result.IsSuccess.ShouldBeTrue();
        git.MutatingCalls.ShouldBe([
            "config --local gitflow.branch.master master",
            "config --local gitflow.branch.develop develop",
            "config --local gitflow.prefix.feature feat/",
            "config --local gitflow.prefix.release release/",
            "config --local gitflow.prefix.hotfix hotfix/",
            "config --local gitflow.prefix.versiontag v",
            "config --local gitflowvs.finish.push true",
            "config --local gitflowvs.finish.deleteremote false",
            "config --local gitflowvs.finish.mode local",
            "config --local gitflowvs.pr.prune false",
        ]);
    }

    [Fact]
    public async Task Save_global_does_not_require_an_initialized_repository()
    {
        var git = WithVsKeys(FakeGitRunner.Initialized());
        git.Config.Clear();

        var result = await new GitFlowSettingsService(git).SaveAsync(Repo, GitFlowSettings.Default, ConfigScope.Global);

        result.IsSuccess.ShouldBeTrue();
        git.MutatingCalls.ShouldAllBe(c => c.StartsWith("config --global ", StringComparison.Ordinal));
        git.MutatingCalls.Count.ShouldBe(10);
    }

    [Fact]
    public async Task Prune_on_complete_is_optional_and_off_by_default()
    {
        var off = WithVsKeys(FakeGitRunner.Initialized());
        var on = WithVsKeys(FakeGitRunner.Initialized(), global: "gitflowvs.pr.prune\ntrue\0");

        (await new GitFlowSettingsService(off).ReadFinishDefaultsAsync(Repo, ConfigScope.Local)).PruneOnComplete.ShouldBeFalse();
        (await new GitFlowSettingsService(on).ReadFinishDefaultsAsync(Repo, ConfigScope.Local)).PruneOnComplete.ShouldBeTrue("global value applies to this repo");
    }

    [Fact]
    public async Task Pull_request_mode_is_optional_and_off_by_default()
    {
        var off = WithVsKeys(FakeGitRunner.Initialized());
        var on = WithVsKeys(FakeGitRunner.Initialized(), local: "gitflowvs.finish.mode\npullrequest\0");

        (await new GitFlowSettingsService(off).ReadFinishDefaultsAsync(Repo, ConfigScope.Local)).UsePullRequest.ShouldBeFalse();
        (await new GitFlowSettingsService(on).ReadFinishDefaultsAsync(Repo, ConfigScope.Local)).UsePullRequest.ShouldBeTrue();
    }

    [Fact]
    public async Task Save_refuses_invalid_naming_without_writing()
    {
        var git = WithVsKeys(FakeGitRunner.Initialized());
        var settings = new GitFlowSettings(GitFlowConfig.Default with { ReleasePrefix = "feature/" }, FinishDefaults.None);

        var result = await new GitFlowSettingsService(git).SaveAsync(Repo, settings, ConfigScope.Local);

        result.Error.ShouldBe(FlowError.InvalidConfig);
        result.ConfigErrors.ShouldContain(e => e.Kind == ConfigValidationErrorKind.DuplicatePrefix);
        git.MutatingCalls.ShouldBeEmpty();
    }

    [Fact]
    public async Task Save_local_refuses_missing_permanent_branch()
    {
        var git = WithVsKeys(FakeGitRunner.Initialized());
        var settings = new GitFlowSettings(GitFlowConfig.Default with { DevelopBranch = "dev" }, FinishDefaults.None);

        var result = await new GitFlowSettingsService(git).SaveAsync(Repo, settings, ConfigScope.Local);

        result.Error.ShouldBe(FlowError.BaseBranchNotFound);
        result.Detail.ShouldBe("dev");
        git.MutatingCalls.ShouldBeEmpty();
    }

    [Fact]
    public async Task Save_local_requires_initialized_repository()
    {
        var git = WithVsKeys(FakeGitRunner.Initialized());
        git.Config.Clear();

        (await new GitFlowSettingsService(git).SaveAsync(Repo, GitFlowSettings.Default, ConfigScope.Local)).Error.ShouldBe(FlowError.NotInitialized);
    }

    [Fact]
    public async Task Renaming_a_prefix_warns_about_existing_branches()
    {
        var git = FakeGitRunner.Initialized().AddBranch("feature/a").AddBranch("feature/b").AddBranch("hotfix/1.0.1");

        var warnings = await new GitFlowSettingsService(git).GetWarningsAsync(
            Repo, GitFlowConfig.Default, GitFlowConfig.Default with { FeaturePrefix = "feat/" });

        var warning = warnings.ShouldHaveSingleItem();
        warning.Kind.ShouldBe(SettingsWarningKind.OrphanedBranches);
        warning.Prefix.ShouldBe("feature/");
        warning.Items.ShouldBe(["feature/a", "feature/b"]);
        FlowMessages.Describe(warnings[0]).ShouldStartWith("2 branche(s) utilisent l'ancien préfixe « feature/ » : feature/a, feature/b.");
        git.MutatingCalls.ShouldBeEmpty();
    }

    [Fact]
    public async Task Unchanged_prefixes_give_no_warning()
    {
        var git = FakeGitRunner.Initialized().AddBranch("feature/a");

        (await new GitFlowSettingsService(git).GetWarningsAsync(Repo, GitFlowConfig.Default, GitFlowConfig.Default)).ShouldBeEmpty();
    }

    [Fact]
    public async Task Changing_the_tag_prefix_warns_about_version_tags()
    {
        var git = FakeGitRunner.Initialized();
        git.On("tag --list", stdout: "1.0.0\n1.1.0\nnot-a-version\n");

        var warnings = await new GitFlowSettingsService(git).GetWarningsAsync(
            Repo, GitFlowConfig.Default, GitFlowConfig.Default with { VersionTagPrefix = "v" });

        var warning = warnings.ShouldHaveSingleItem();
        warning.Kind.ShouldBe(SettingsWarningKind.OldTagPrefix);
        warning.Prefix.ShouldBe("");
        warning.Items.ShouldBe(["1.0.0", "1.1.0"]);
        FlowMessages.Describe(warnings[0]).ShouldContain("« ∅ »");
    }

    [Fact]
    public void Every_settings_warning_has_a_french_message()
    {
        foreach (var kind in Enum.GetValues<SettingsWarningKind>())
        {
            FlowMessages.Get("SettingsWarning_" + kind).ShouldNotBe("SettingsWarning_" + kind);
        }
    }
}
