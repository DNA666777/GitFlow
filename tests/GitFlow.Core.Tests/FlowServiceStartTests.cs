using GitFlow.Core.Flow;
using GitFlow.Core.Model;
using Shouldly;

namespace GitFlow.Core.Tests;

public class FlowServiceStartTests
{
    private const string Repo = @"C:\repo";

    [Fact]
    public async Task Start_feature_creates_branch_from_develop()
    {
        var git = FakeGitRunner.Initialized();

        var result = await new FlowService(git).StartAsync(Repo, BranchType.Feature, "login");

        result.IsSuccess.ShouldBeTrue();
        result.BranchName.ShouldBe("feature/login");
        git.MutatingCalls.ShouldBe(["switch -c feature/login develop"]);
    }

    [Fact]
    public async Task Start_hotfix_creates_branch_from_master()
    {
        var git = FakeGitRunner.Initialized();

        var result = await new FlowService(git).StartAsync(Repo, BranchType.Hotfix, "1.0.1");

        result.IsSuccess.ShouldBeTrue();
        git.MutatingCalls.ShouldBe(["switch -c hotfix/1.0.1 master"]);
    }

    [Fact]
    public async Task Start_trims_the_name()
    {
        var git = FakeGitRunner.Initialized();

        var result = await new FlowService(git).StartAsync(Repo, BranchType.Release, "  1.2.0 ");

        result.BranchName.ShouldBe("release/1.2.0");
        git.MutatingCalls.ShouldBe(["switch -c release/1.2.0 develop"]);
    }

    [Fact]
    public async Task Start_with_fetch_fetches_then_fast_forwards_base_when_behind()
    {
        var git = FakeGitRunner.Initialized(withRemote: true);
        git.Refs["refs/remotes/origin/develop"] = "d2";
        git.Ancestors.Add(("refs/heads/develop", "refs/remotes/origin/develop"));

        var result = await new FlowService(git).StartAsync(Repo, BranchType.Feature, "x", new StartOptions { FetchFromRemote = true });

        result.IsSuccess.ShouldBeTrue();
        git.MutatingCalls.ShouldBe([
            "fetch origin",
            "switch develop",
            "merge --ff-only refs/remotes/origin/develop",
            "switch -c feature/x develop",
        ]);
    }

    [Fact]
    public async Task Start_with_fetch_refuses_when_base_diverged()
    {
        var git = FakeGitRunner.Initialized(withRemote: true);
        git.Refs["refs/remotes/origin/develop"] = "d2";

        var result = await new FlowService(git).StartAsync(Repo, BranchType.Feature, "x", new StartOptions { FetchFromRemote = true });

        result.Error.ShouldBe(FlowError.BranchDivergedFromRemote);
        result.Detail.ShouldBe("develop");
        git.MutatingCalls.ShouldBe(["fetch origin"]);
    }

    [Fact]
    public async Task Start_refuses_dirty_working_tree()
    {
        var git = FakeGitRunner.Initialized();
        git.Status = " M file.txt\n";

        var result = await new FlowService(git).StartAsync(Repo, BranchType.Feature, "x");

        result.Status.ShouldBe(FlowStatus.Failed);
        result.Error.ShouldBe(FlowError.DirtyWorkingTree);
        git.MutatingCalls.ShouldBeEmpty();
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("bad..name")]
    public async Task Start_refuses_invalid_name(string name)
    {
        var git = FakeGitRunner.Initialized();
        git.OnPrefix("check-ref-format --branch feature/bad..name", exitCode: 1);

        var result = await new FlowService(git).StartAsync(Repo, BranchType.Feature, name);

        result.Error.ShouldBe(FlowError.InvalidBranchName);
        git.MutatingCalls.ShouldBeEmpty();
    }

    [Fact]
    public async Task Start_refuses_existing_local_branch()
    {
        var git = FakeGitRunner.Initialized().AddBranch("feature/x");

        var result = await new FlowService(git).StartAsync(Repo, BranchType.Feature, "x");

        result.Error.ShouldBe(FlowError.BranchAlreadyExists);
        result.Detail.ShouldBe("feature/x");
        git.MutatingCalls.ShouldBeEmpty();
    }

    [Fact]
    public async Task Start_refuses_existing_remote_branch()
    {
        var git = FakeGitRunner.Initialized(withRemote: true);
        git.Refs["refs/remotes/origin/feature/x"] = "f1";

        var result = await new FlowService(git).StartAsync(Repo, BranchType.Feature, "x");

        result.Error.ShouldBe(FlowError.BranchAlreadyExists);
    }

    [Fact]
    public async Task Start_refuses_when_not_initialized()
    {
        var git = FakeGitRunner.Initialized();
        git.Config.Clear();

        var result = await new FlowService(git).StartAsync(Repo, BranchType.Feature, "x");

        result.Error.ShouldBe(FlowError.NotInitialized);
        git.MutatingCalls.ShouldBeEmpty();
    }

    [Fact]
    public async Task Start_refuses_missing_base_branch()
    {
        var git = FakeGitRunner.Initialized();
        git.Refs.Remove("refs/heads/develop");

        var result = await new FlowService(git).StartAsync(Repo, BranchType.Feature, "x");

        result.Error.ShouldBe(FlowError.BaseBranchNotFound);
        result.Detail.ShouldBe("develop");
    }

    [Fact]
    public async Task Start_refuses_during_merge()
    {
        var git = FakeGitRunner.Initialized();
        git.Refs["MERGE_HEAD"] = "x1";

        var result = await new FlowService(git).StartAsync(Repo, BranchType.Feature, "x");

        result.Error.ShouldBe(FlowError.MergeInProgress);
    }

    [Fact]
    public async Task Start_release_refuses_second_release()
    {
        var git = FakeGitRunner.Initialized().AddBranch("release/1.0.0");

        var result = await new FlowService(git).StartAsync(Repo, BranchType.Release, "1.1.0");

        result.Error.ShouldBe(FlowError.ReleaseAlreadyInProgress);
        result.Detail.ShouldBe("release/1.0.0");
    }

    [Fact]
    public async Task Start_hotfix_refuses_second_hotfix()
    {
        var git = FakeGitRunner.Initialized().AddBranch("hotfix/1.0.1");

        var result = await new FlowService(git).StartAsync(Repo, BranchType.Hotfix, "1.0.2");

        result.Error.ShouldBe(FlowError.HotfixAlreadyInProgress);
    }

    [Fact]
    public async Task Start_release_refuses_existing_tag()
    {
        var git = FakeGitRunner.Initialized();
        git.Config["gitflow.prefix.versiontag"] = "v";
        git.Refs["refs/tags/v1.0.0"] = "t1";

        var result = await new FlowService(git).StartAsync(Repo, BranchType.Release, "1.0.0");

        result.Error.ShouldBe(FlowError.TagAlreadyExists);
        result.Detail.ShouldBe("v1.0.0");
    }

    [Fact]
    public async Task Start_uses_custom_naming()
    {
        var git = new FakeGitRunner();
        git.Config["gitflow.branch.master"] = "main";
        git.Config["gitflow.branch.develop"] = "dev";
        git.Config["gitflow.prefix.feature"] = "feat/";
        git.AddBranch("main").AddBranch("dev");
        git.CurrentBranch = "dev";

        var result = await new FlowService(git).StartAsync(Repo, BranchType.Feature, "x");

        result.IsSuccess.ShouldBeTrue();
        git.MutatingCalls.ShouldBe(["switch -c feat/x dev"]);
    }

    [Fact]
    public async Task Start_reports_unexpected_git_failure_without_throwing()
    {
        var git = FakeGitRunner.Initialized();
        git.On("switch -c feature/x develop", exitCode: 128, stderr: "fatal: boom");

        var result = await new FlowService(git).StartAsync(Repo, BranchType.Feature, "x");

        result.Error.ShouldBe(FlowError.GitCommandFailed);
        result.Detail.ShouldBe("fatal: boom");
        result.FailedCommand.ShouldNotBeNull();
    }
}
