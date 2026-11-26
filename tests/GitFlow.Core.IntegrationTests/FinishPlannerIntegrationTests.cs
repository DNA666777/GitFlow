using GitFlow.Core.Config;
using GitFlow.Core.Flow;
using GitFlow.Core.Model;
using GitFlow.Core.PullRequests;
using Shouldly;

namespace GitFlow.Core.IntegrationTests;

/// <summary>Preparation of a Finish, shared by Visual Studio and VS Code.</summary>
public class FinishPlannerIntegrationTests
{
    private static async Task<(TestRepository Repo, FinishPlanner Planner)> CreateAsync()
    {
        var repo = await TestRepository.CreateAsync();
        (await repo.Flow.InitAsync(repo.WorkDir, GitFlowConfig.Default)).IsSuccess.ShouldBeTrue();
        var planner = new FinishPlanner(repo.Flow, new GitFlowSettingsService(repo.Runner), new PullRequestService(repo.Runner, new HttpClient()));
        return (repo, planner);
    }

    [Fact]
    public async Task A_feature_just_started_has_no_commit_and_is_not_reported_as_merged()
    {
        var (repo, planner) = await CreateAsync();
        await using var _ = repo;
        await repo.Flow.StartAsync(repo.WorkDir, BranchType.Feature, "fresh");
        await repo.Flow.StartAsync(repo.WorkDir, BranchType.Hotfix, "1.0.1");

        var state = (await repo.Flow.GetStateAsync(repo.WorkDir))!;
        var (context, _) = await planner.PrepareAsync(repo.WorkDir, "feature/fresh");

        state.FlowBranchesWithoutCommit.ShouldBe(["feature/fresh", "hotfix/1.0.1"], ignoreOrder: true);
        state.MergedFlowBranches.ShouldBeEmpty();
        context!.IsWithoutCommit.ShouldBeTrue();
        context.IsAlreadyMerged.ShouldBeFalse();
    }

    [Fact]
    public async Task A_release_without_commit_of_its_own_is_a_normal_release()
    {
        var (repo, planner) = await CreateAsync();
        await using var _ = repo;
        await repo.GitAsync("switch", "develop");
        await repo.CommitFileAsync("a.txt", "a\n", "Ajoute a");
        await repo.Flow.StartAsync(repo.WorkDir, BranchType.Release, "1.1.0");

        var (context, _) = await planner.PrepareAsync(repo.WorkDir, "release/1.1.0");

        context!.IsWithoutCommit.ShouldBeFalse("it brings develop into master");
        context.IsAlreadyMerged.ShouldBeFalse();
    }

    [Fact]
    public async Task A_feature_merged_by_hand_is_still_reported_as_merged()
    {
        var (repo, planner) = await CreateAsync();
        await using var _ = repo;
        await repo.Flow.StartAsync(repo.WorkDir, BranchType.Feature, "manual");
        await repo.CommitFileAsync("m.txt", "m\n", "Ajoute m");
        await repo.GitAsync("switch", "develop");
        await repo.GitAsync("merge", "--no-ff", "-m", "Merge by hand", "feature/manual");

        var (context, _) = await planner.PrepareAsync(repo.WorkDir, "feature/manual");

        context!.IsAlreadyMerged.ShouldBeTrue();
        context.IsWithoutCommit.ShouldBeFalse();
    }

    [Fact]
    public async Task Uncommitted_changes_refuse_the_finish_before_any_question()
    {
        var (repo, planner) = await CreateAsync();
        await using var _ = repo;
        await repo.Flow.StartAsync(repo.WorkDir, BranchType.Feature, "dirty");
        await repo.CommitFileAsync("d.txt", "d\n", "Ajoute d");
        await repo.WriteFileAsync("d.txt", "changed\n");

        var (context, error) = await planner.PrepareAsync(repo.WorkDir, null);

        context.ShouldBeNull();
        error!.Error.ShouldBe(FlowError.DirtyWorkingTree);
    }

    [Fact]
    public async Task Resuming_a_finish_stopped_on_a_conflict_is_not_refused()
    {
        var (repo, planner) = await CreateAsync();
        await using var _ = repo;
        await repo.Flow.StartAsync(repo.WorkDir, BranchType.Feature, "x");
        await repo.CommitFileAsync("README.md", "feature\n", "Feature");
        await repo.GitAsync("switch", "develop");
        await repo.CommitFileAsync("README.md", "develop\n", "Develop");
        (await repo.Flow.FinishAsync(repo.WorkDir, new FinishOptions { BranchName = "feature/x" })).Status.ShouldBe(FlowStatus.Conflict);

        var (context, error) = await planner.PrepareAsync(repo.WorkDir, null);

        error.ShouldBeNull();
        context!.IsResume.ShouldBeTrue();
    }
}
