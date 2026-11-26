using GitFlow.Core.Model;
using GitFlow.Core.PullRequests;

namespace GitFlow.Server.Tests;

/// <summary>Pull Request mode through JSON-RPC, with an in-memory GitHub.</summary>
public class ServerPullRequestTests
{
    [Fact]
    public async Task Release_through_pull_requests()
    {
        await using var s = await ServerSession.CreateAsync(pullRequests: true);
        await s.Repo.GitAsync("switch", "develop");
        await s.Repo.CommitFileAsync("a.txt", "a\n", "Ajoute a");
        await s.CallAsync<ResultDto>("start", new { repository = s.Repo.WorkDir, type = "release", name = "1.0.0" });

        var context = (await s.CallAsync<PrepareFinishResponse>("prepareFinish", new { repository = s.Repo.WorkDir, branch = (string?)null })).Context!;
        var prepared = await s.CallAsync<PreparePullRequestsResponse>("preparePullRequests", new { repository = s.Repo.WorkDir, branch = "release/1.0.0" });
        var drafts = prepared.Drafts.Select(d => d with { Title = d.Title + " (relu)" }).ToArray();
        var result = await s.CallAsync<ResultDto>("finishWithPullRequests", new { repository = s.Repo.WorkDir, branch = "release/1.0.0", tagMessage = "Release 1.0.0", drafts });
        var groups = await s.CallAsync<IReadOnlyList<PullRequestGroupDto>>("getPullRequests", new { repository = s.Repo.WorkDir, refresh = true });

        context.PullRequestAvailable.ShouldBeTrue();
        context.Remote!.Platform.ShouldBe(HostingPlatform.GitHub);
        prepared.Error.ShouldBeNull();
        prepared.Drafts.Select(d => d.Target).ShouldBe(["master", "develop"]);
        prepared.Drafts[0].Title.ShouldBe("Release 1.0.0");
        result.Success.ShouldBeTrue(result.Message);
        result.PullRequests.Select(p => p.Target).ShouldBe(["master", "develop"]);
        s.Platform!.PullRequests.Select(p => p.Title).ShouldAllBe(t => t.EndsWith(" (relu)"));
        var group = groups.ShouldHaveSingleItem();
        group.Source.ShouldBe("release/1.0.0");
        group.Version.ShouldBe("1.0.0");
        group.PullRequests.Select(p => p.State).ShouldBe([PullRequestState.Open, PullRequestState.Open]);
        group.CanTag.ShouldBeFalse("the PR to master is not merged yet");
        group.CanComplete.ShouldBeFalse();
    }

    [Fact]
    public async Task Unrelated_history_is_reported_before_anything_is_pushed()
    {
        await using var s = await ServerSession.CreateAsync(pullRequests: true);
        await s.Repo.GitAsync("switch", "--orphan", "server-readme");
        await s.Repo.CommitFileAsync("SERVER.md", "# Server\n", "Initial commit");
        await s.Repo.GitAsync("push", "--force", "origin", "server-readme:master");
        await s.Repo.GitAsync("switch", "develop");
        await s.Repo.GitAsync("branch", "-D", "server-readme");
        await s.Repo.GitAsync("fetch", "origin");
        await s.CallAsync<ResultDto>("start", new { repository = s.Repo.WorkDir, type = "release", name = "2.0.0" });

        var prepared = await s.CallAsync<PreparePullRequestsResponse>("preparePullRequests", new { repository = s.Repo.WorkDir, branch = "release/2.0.0" });

        prepared.Drafts.ShouldBeEmpty();
        prepared.Error!.Error.ShouldBe(FlowError.NoCommonHistory);
        prepared.Error.Message.ShouldContain("git merge origin/master --allow-unrelated-histories");
        (await s.Repo.OriginBranchesAsync()).ShouldNotContain("release/2.0.0");
    }
}
