using GitFlow.Core.Config;
using GitFlow.Core.Flow;
using GitFlow.Core.Model;
using Shouldly;

namespace GitFlow.Core.Tests;

public class FlowServiceOtherTests
{
    private const string Repo = @"C:\repo";

    [Fact]
    public async Task Publish_pushes_with_upstream()
    {
        var git = FakeGitRunner.Initialized(withRemote: true).AddBranch("feature/x");
        git.CurrentBranch = "feature/x";

        var result = await new FlowService(git).PublishAsync(Repo);

        result.IsSuccess.ShouldBeTrue();
        git.MutatingCalls.ShouldBe(["push -u origin feature/x"]);
    }

    [Fact]
    public async Task Publish_without_remote_fails()
    {
        var git = FakeGitRunner.Initialized().AddBranch("feature/x");

        var result = await new FlowService(git).PublishAsync(Repo, "feature/x");

        result.Error.ShouldBe(FlowError.NoRemote);
        git.MutatingCalls.ShouldBeEmpty();
    }

    [Fact]
    public async Task Publish_refuses_permanent_branch()
    {
        var git = FakeGitRunner.Initialized(withRemote: true);

        var result = await new FlowService(git).PublishAsync(Repo, "develop");

        result.Error.ShouldBe(FlowError.NotAFlowBranch);
    }

    [Fact]
    public async Task Init_writes_config_and_creates_develop_from_master()
    {
        var git = new FakeGitRunner().AddBranch("master", "m1");
        git.Refs["HEAD"] = "m1";
        git.CurrentBranch = "master";

        var result = await new FlowService(git).InitAsync(Repo, GitFlowConfig.Default);

        result.IsSuccess.ShouldBeTrue();
        git.MutatingCalls.ShouldBe([
            "config --local gitflow.branch.master master",
            "config --local gitflow.branch.develop develop",
            "config --local gitflow.prefix.feature feature/",
            "config --local gitflow.prefix.release release/",
            "config --local gitflow.prefix.hotfix hotfix/",
            "config --local gitflow.prefix.versiontag ",
            "branch develop refs/heads/master",
        ]);
    }

    [Fact]
    public async Task Init_tracks_remote_develop_when_present()
    {
        var git = new FakeGitRunner().AddBranch("master", "m1");
        git.Refs["HEAD"] = "m1";
        git.Remotes.Add("origin");
        git.Refs["refs/remotes/origin/develop"] = "d9";

        var result = await new FlowService(git).InitAsync(Repo, GitFlowConfig.Default);

        result.IsSuccess.ShouldBeTrue();
        git.MutatingCalls.ShouldContain("branch --track develop refs/remotes/origin/develop");
    }

    [Fact]
    public async Task Init_refuses_invalid_config_without_writing()
    {
        var git = new FakeGitRunner().AddBranch("master");
        git.Refs["HEAD"] = "c0";

        var result = await new FlowService(git).InitAsync(Repo, GitFlowConfig.Default with { DevelopBranch = "master" });

        result.Error.ShouldBe(FlowError.InvalidConfig);
        result.ConfigErrors.Select(e => e.Kind).ShouldContain(ConfigValidationErrorKind.SameMasterAndDevelop);
        git.MutatingCalls.ShouldBeEmpty();
    }

    [Fact]
    public async Task Init_refuses_empty_repository()
    {
        var git = new FakeGitRunner();

        var result = await new FlowService(git).InitAsync(Repo, GitFlowConfig.Default);

        result.Error.ShouldBe(FlowError.NoCommits);
    }

    [Fact]
    public async Task Init_refuses_missing_master()
    {
        var git = new FakeGitRunner().AddBranch("main");
        git.Refs["HEAD"] = "c0";

        var result = await new FlowService(git).InitAsync(Repo, GitFlowConfig.Default);

        result.Error.ShouldBe(FlowError.BaseBranchNotFound);
        result.Detail.ShouldBe("master");
        git.MutatingCalls.ShouldBeEmpty();
    }

    [Theory]
    [InlineData(BranchType.Release, "1.5.0")]
    [InlineData(BranchType.Hotfix, "1.4.1")]
    public async Task SuggestVersion_increments_latest_master_tag(BranchType type, string expected)
    {
        var git = FakeGitRunner.Initialized();
        git.On("tag --list --merged refs/heads/master *", stdout: "1.3.9\n1.4.0\nnot-a-version\n1.10\n");

        (await new FlowService(git).SuggestVersionAsync(Repo, type)).ShouldBe(expected);
    }

    [Fact]
    public async Task SuggestVersion_respects_tag_prefix()
    {
        var git = FakeGitRunner.Initialized();
        git.Config["gitflow.prefix.versiontag"] = "v";
        git.On("tag --list --merged refs/heads/master v*", stdout: "v2.0.0\nv10.1.3\n");

        (await new FlowService(git).SuggestVersionAsync(Repo, BranchType.Release)).ShouldBe("10.2.0");
    }

    [Fact]
    public async Task SuggestVersion_without_tags_starts_from_zero()
    {
        var git = FakeGitRunner.Initialized();
        var service = new FlowService(git);

        (await service.SuggestVersionAsync(Repo, BranchType.Release)).ShouldBe("0.1.0");
        (await service.SuggestVersionAsync(Repo, BranchType.Hotfix)).ShouldBe("0.0.1");
    }

    [Fact]
    public async Task CountUnreleasedCommits_counts_non_merge_commits_of_develop_not_in_master()
    {
        var git = FakeGitRunner.Initialized();
        git.On("rev-list --count --no-merges refs/heads/master..refs/heads/develop", stdout: "3\n");

        (await new FlowService(git).CountUnreleasedCommitsAsync(Repo)).ShouldBe(3);
        git.MutatingCalls.ShouldBeEmpty();
    }

    [Fact]
    public async Task CountUnreleasedCommits_is_null_when_not_initialized()
    {
        var git = FakeGitRunner.Initialized();
        git.Config.Clear();

        (await new FlowService(git).CountUnreleasedCommitsAsync(Repo)).ShouldBeNull();
    }

    [Fact]
    public async Task GetState_describes_repository()
    {
        var git = FakeGitRunner.Initialized(withRemote: true)
            .AddBranch("release/1.0.0").AddBranch("feature/b").AddBranch("feature/a").AddBranch("hotfix/0.9.1");
        git.CurrentBranch = "release/1.0.0";
        git.Status = " M x\n";

        var state = await new FlowService(git).GetStateAsync(Repo);

        state.ShouldNotBeNull();
        state.IsInitialized.ShouldBeTrue();
        state.IsDirty.ShouldBeTrue();
        state.HasRemote.ShouldBeTrue();
        state.CurrentFlowBranch.ShouldBe(new FlowBranch(BranchType.Release, "1.0.0", "release/1.0.0"));
        state.ShowReleaseReminder.ShouldBeTrue();
        state.FlowBranches.Select(b => b.FullName).ShouldBe(["feature/a", "feature/b", "release/1.0.0", "hotfix/0.9.1"]);
    }

    [Fact]
    public async Task GetState_reports_branches_merged_into_all_targets()
    {
        var git = FakeGitRunner.Initialized()
            .AddBranch("feature/done").Merged("feature/done", "develop")
            .AddBranch("feature/wip")
            .AddBranch("hotfix/1.0.1").Merged("hotfix/1.0.1", "master"); // not yet in develop

        var state = await new FlowService(git).GetStateAsync(Repo);

        state!.MergedFlowBranches.ShouldBe(["feature/done"]);
    }

    [Fact]
    public async Task GetState_reports_pending_finish_during_conflict()
    {
        var git = FakeGitRunner.Initialized().AddBranch("feature/x", "f1");
        git.Refs["MERGE_HEAD"] = "f1";

        var state = await new FlowService(git).GetStateAsync(Repo);

        state!.IsMergeInProgress.ShouldBeTrue();
        state.PendingFinish!.FullName.ShouldBe("feature/x");
    }

    [Fact]
    public async Task GetState_returns_null_outside_git()
    {
        var git = new FakeGitRunner().On("rev-parse --is-inside-work-tree", exitCode: 128);

        (await new FlowService(git).GetStateAsync(Repo)).ShouldBeNull();
    }
}
