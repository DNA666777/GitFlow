using GitFlow.Core.Flow;
using GitFlow.Core.Model;
using Shouldly;

namespace GitFlow.Core.Tests;

public class FlowServiceFinishTests
{
    private const string Repo = @"C:\repo";

    private static FakeGitRunner OnBranch(string branch, bool withRemote = false)
    {
        var git = FakeGitRunner.Initialized(withRemote).AddBranch(branch, "b1");
        git.CurrentBranch = branch;
        return git;
    }

    [Fact]
    public async Task Finish_feature_merges_no_ff_into_develop_and_deletes_branch()
    {
        var git = OnBranch("feature/login");

        var result = await new FlowService(git).FinishAsync(Repo);

        result.IsSuccess.ShouldBeTrue();
        result.BranchName.ShouldBe("develop");
        result.TagName.ShouldBeNull();
        git.MutatingCalls.ShouldBe([
            "switch develop",
            "merge --no-ff -m Merge branch 'feature/login' into develop feature/login",
            "branch -d feature/login",
        ]);
    }

    [Fact]
    public async Task Finish_release_merges_master_tags_then_merges_develop()
    {
        var git = OnBranch("release/1.4.0");

        var result = await new FlowService(git).FinishAsync(Repo);

        result.IsSuccess.ShouldBeTrue();
        result.TagName.ShouldBe("1.4.0");
        git.MutatingCalls.ShouldBe([
            "switch master",
            "merge --no-ff -m Merge branch 'release/1.4.0' into master release/1.4.0",
            "tag -a 1.4.0 -m Release 1.4.0 master",
            "switch develop",
            "merge --no-ff -m Merge branch 'release/1.4.0' into develop release/1.4.0",
            "branch -d release/1.4.0",
        ]);
    }

    [Fact]
    public async Task Finish_hotfix_uses_tag_prefix_and_custom_message()
    {
        var git = OnBranch("hotfix/1.4.1");
        git.Config["gitflow.prefix.versiontag"] = "v";

        var result = await new FlowService(git).FinishAsync(Repo, new FinishOptions { TagMessage = "Correctif urgent" });

        result.TagName.ShouldBe("v1.4.1");
        git.MutatingCalls.ShouldBe([
            "switch master",
            "merge --no-ff -m Merge branch 'hotfix/1.4.1' into master hotfix/1.4.1",
            "tag -a v1.4.1 -m Correctif urgent master",
            "switch develop",
            "merge --no-ff -m Merge branch 'hotfix/1.4.1' into develop hotfix/1.4.1",
            "branch -d hotfix/1.4.1",
        ]);
    }

    [Fact]
    public async Task Finish_hotfix_merges_into_release_in_progress_instead_of_develop()
    {
        var git = OnBranch("hotfix/1.4.1").AddBranch("release/1.5.0", "r1");

        var result = await new FlowService(git).FinishAsync(Repo);

        result.IsSuccess.ShouldBeTrue();
        result.BranchName.ShouldBe("release/1.5.0");
        git.MutatingCalls.ShouldContain("merge --no-ff -m Merge branch 'hotfix/1.4.1' into release/1.5.0 hotfix/1.4.1");
        git.MutatingCalls.ShouldNotContain(c => c.Contains("into develop"));
    }

    [Fact]
    public async Task Finish_stops_on_conflict_and_deletes_nothing()
    {
        var git = OnBranch("feature/x");
        git.On("merge --no-ff -m Merge branch 'feature/x' into develop feature/x", exitCode: 1, stdout: "CONFLICT");
        git.ConflictedFiles.Add("a.txt");

        var result = await new FlowService(git).FinishAsync(Repo);

        result.Status.ShouldBe(FlowStatus.Conflict);
        result.Conflict.ShouldNotBeNull();
        result.Conflict.SourceBranch.ShouldBe("feature/x");
        result.Conflict.TargetBranch.ShouldBe("develop");
        result.Conflict.Files.ShouldBe(["a.txt"]);
        git.MutatingCalls.ShouldNotContain(c => c.StartsWith("branch") || c.StartsWith("push") || c.Contains("--abort"));
    }

    [Fact]
    public async Task Finish_merge_failure_without_conflict_is_reported()
    {
        var git = OnBranch("feature/x");
        git.On("merge --no-ff -m Merge branch 'feature/x' into develop feature/x", exitCode: 1, stderr: "untracked files would be overwritten");

        var result = await new FlowService(git).FinishAsync(Repo);

        result.Error.ShouldBe(FlowError.GitCommandFailed);
        git.MutatingCalls.ShouldNotContain(c => c.StartsWith("branch"));
    }

    [Fact]
    public async Task Finish_resumes_after_resolved_conflict()
    {
        // Merge of feature/x into develop stopped; the user resolved and staged the files.
        var git = FakeGitRunner.Initialized().AddBranch("feature/x", "f1");
        git.Refs["MERGE_HEAD"] = "f1";
        git.CurrentBranch = "develop";
        git.Merged("feature/x", "develop"); // true once the merge commit is made

        var result = await new FlowService(git).FinishAsync(Repo);

        result.IsSuccess.ShouldBeTrue();
        git.MutatingCalls.ShouldBe(["commit --no-edit", "branch -d feature/x"]);
    }

    [Fact]
    public async Task Finish_resume_with_unresolved_conflicts_returns_conflict_again()
    {
        var git = FakeGitRunner.Initialized().AddBranch("feature/x", "f1");
        git.Refs["MERGE_HEAD"] = "f1";
        git.ConflictedFiles.Add("a.txt");

        var result = await new FlowService(git).FinishAsync(Repo);

        result.Status.ShouldBe(FlowStatus.Conflict);
        git.MutatingCalls.ShouldBeEmpty();
    }

    [Fact]
    public async Task Finish_refuses_foreign_merge_in_progress()
    {
        var git = OnBranch("feature/x");
        git.Refs["MERGE_HEAD"] = "zz";

        var result = await new FlowService(git).FinishAsync(Repo);

        result.Error.ShouldBe(FlowError.MergeInProgress);
        git.MutatingCalls.ShouldBeEmpty();
    }

    [Fact]
    public async Task Finish_release_resume_skips_master_merge_and_existing_tag()
    {
        var git = FakeGitRunner.Initialized().AddBranch("release/1.0.0", "r1").Merged("release/1.0.0", "master");
        git.Refs["refs/tags/1.0.0"] = "t1";
        git.CurrentBranch = "release/1.0.0";

        var result = await new FlowService(git).FinishAsync(Repo);

        result.IsSuccess.ShouldBeTrue();
        git.MutatingCalls.ShouldBe([
            "switch develop",
            "merge --no-ff -m Merge branch 'release/1.0.0' into develop release/1.0.0",
            "branch -d release/1.0.0",
        ]);
    }

    [Fact]
    public async Task Finish_release_refuses_existing_tag_when_not_merged()
    {
        var git = OnBranch("release/1.0.0");
        git.Refs["refs/tags/1.0.0"] = "t1";

        var result = await new FlowService(git).FinishAsync(Repo);

        result.Error.ShouldBe(FlowError.TagAlreadyExists);
        git.MutatingCalls.ShouldBeEmpty();
    }

    [Fact]
    public async Task Finish_refuses_dirty_tree()
    {
        var git = OnBranch("feature/x");
        git.Status = " M a.txt\n";

        var result = await new FlowService(git).FinishAsync(Repo);

        result.Error.ShouldBe(FlowError.DirtyWorkingTree);
        git.MutatingCalls.ShouldBeEmpty();
    }

    [Fact]
    public async Task Finish_refuses_non_flow_branch()
    {
        var git = FakeGitRunner.Initialized();

        var result = await new FlowService(git).FinishAsync(Repo);

        result.Error.ShouldBe(FlowError.NotAFlowBranch);
        result.Detail.ShouldBe("develop");
    }

    [Fact]
    public async Task Finish_explicit_branch_from_another_branch()
    {
        var git = FakeGitRunner.Initialized().AddBranch("feature/x", "f1");

        var result = await new FlowService(git).FinishAsync(Repo, new FinishOptions { BranchName = "feature/x" });

        result.IsSuccess.ShouldBeTrue();
        git.MutatingCalls.ShouldContain("branch -d feature/x");
    }

    [Fact]
    public async Task Finish_with_push_pushes_targets_and_tag_then_deletes_remote_branch()
    {
        var git = OnBranch("release/2.0.0", withRemote: true);
        git.Refs["refs/remotes/origin/release/2.0.0"] = "b1";

        var result = await new FlowService(git).FinishAsync(Repo, new FinishOptions { PushAfterFinish = true, DeleteRemoteBranch = true });

        result.IsSuccess.ShouldBeTrue();
        result.Warnings.ShouldBeEmpty();
        git.MutatingCalls.ShouldBe([
            "switch master",
            "merge --no-ff -m Merge branch 'release/2.0.0' into master release/2.0.0",
            "tag -a 2.0.0 -m Release 2.0.0 master",
            "switch develop",
            "merge --no-ff -m Merge branch 'release/2.0.0' into develop release/2.0.0",
            "push origin master develop refs/tags/2.0.0",
            "branch -d release/2.0.0",
            "push origin --delete release/2.0.0",
        ]);
    }

    [Fact]
    public async Task Finish_push_failure_is_a_warning_and_keeps_remote_branch()
    {
        var git = OnBranch("feature/x", withRemote: true);
        git.Refs["refs/remotes/origin/feature/x"] = "b1";
        git.On("push origin develop", exitCode: 1, stderr: "rejected");

        var result = await new FlowService(git).FinishAsync(Repo, new FinishOptions { PushAfterFinish = true, DeleteRemoteBranch = true });

        result.IsSuccess.ShouldBeTrue();
        result.Warnings.Select(w => w.Kind).ShouldBe([FlowWarningKind.PushFailed, FlowWarningKind.RemoteBranchKeptBecausePushFailed]);
        git.MutatingCalls.ShouldNotContain("push origin --delete feature/x");
    }

    [Fact]
    public async Task Finish_with_push_requires_remote()
    {
        var git = OnBranch("feature/x");

        var result = await new FlowService(git).FinishAsync(Repo, new FinishOptions { PushAfterFinish = true });

        result.Error.ShouldBe(FlowError.NoRemote);
        git.MutatingCalls.ShouldBeEmpty();
    }

    [Fact]
    public async Task Finish_never_force_deletes_or_force_pushes()
    {
        var git = OnBranch("feature/x", withRemote: true);
        git.On("branch -d feature/x", exitCode: 1, stderr: "not fully merged");

        var result = await new FlowService(git).FinishAsync(Repo, new FinishOptions { PushAfterFinish = true });

        result.Warnings.Select(w => w.Kind).ShouldContain(FlowWarningKind.LocalBranchDeleteFailed);
        git.Calls.ShouldNotContain(c => c.Contains("-D") || c.Contains("--force") || c.Contains("-f") || c.Contains("--hard"));
    }

    [Fact]
    public async Task Finish_uses_strategy_abstraction()
    {
        var git = OnBranch("release/1.0.0");
        var strategy = new RecordingStrategy();

        await new FlowService(git, strategy).FinishAsync(Repo, new FinishOptions { TagMessage = "  " });

        strategy.Plan.ShouldNotBeNull();
        strategy.Plan.MergeTargets.ShouldBe(["master", "develop"]);
        strategy.Plan.TagName.ShouldBe("1.0.0");
        strategy.Plan.TagMessage.ShouldBe("Release 1.0.0");
        git.MutatingCalls.ShouldBeEmpty();
    }

    private sealed class RecordingStrategy : IFinishStrategy
    {
        public FinishPlan? Plan { get; private set; }

        public Task<FlowResult> ExecuteAsync(FinishPlan plan, CancellationToken cancellationToken)
        {
            Plan = plan;
            return Task.FromResult(FlowResult.Ok());
        }
    }
}
