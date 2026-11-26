using GitFlow.Core.Config;
using GitFlow.Core.Flow;
using GitFlow.Core.Model;
using GitFlow.Core.PullRequests;
using Shouldly;

namespace GitFlow.Core.IntegrationTests;

/// <summary>
/// Pull Request mode end to end, without network: "origin" is a local bare repository presented as
/// https://github.com/test/repo.git (url.insteadOf), credentials come from an isolated credential store,
/// and the platform is simulated in memory; "merging" a PR really merges on origin through another clone.
/// </summary>
public class PullRequestIntegrationTests
{
    private const string GitHubUrl = "https://github.com/test/repo.git";

    // Generated texts are asserted in French: fixed language, whatever the machine's (CI runs in English).
    public PullRequestIntegrationTests() => Resources.FlowMessages.Culture = new System.Globalization.CultureInfo("fr-FR");

    private sealed class Setup : IAsyncDisposable
    {
        public required TestRepository Repo { get; init; }

        public required FakePlatform Platform { get; init; }

        public required PullRequestService PullRequests { get; init; }

        public required FlowService Flow { get; init; }

        public ValueTask DisposeAsync() => Repo.DisposeAsync();
    }

    private static async Task<Setup> CreateAsync()
    {
        var repo = await TestRepository.CreateAsync();
        (await repo.Flow.InitAsync(repo.WorkDir, GitFlowConfig.Default)).IsSuccess.ShouldBeTrue();
        await repo.GitAsync("push", "origin", "develop");

        // origin "is" GitHub for the extension, but git really talks to the local bare repository.
        await repo.GitAsync("config", "--global", $"url.{repo.OriginDir.Replace('\\', '/')}.insteadOf", GitHubUrl);
        await repo.GitAsync("remote", "set-url", "origin", GitHubUrl);
        var store = Path.Combine(repo.Root, "credentials-store");
        await File.WriteAllTextAsync(store, "https://tester:fake-token@github.com\n");
        await repo.GitAsync("config", "--global", "credential.helper", $"store --file={store.Replace('\\', '/')}");

        var platform = new FakePlatform(repo);
        var pullRequests = new PullRequestService(repo.Runner, [platform]);
        return new Setup
        {
            Repo = repo,
            Platform = platform,
            PullRequests = pullRequests,
            Flow = new FlowService(repo.Runner, pullRequestStrategy: new PullRequestStrategy(pullRequests)),
        };
    }

    private static async Task<FlowResult> FinishWithPullRequestsAsync(Setup s, string branchName)
    {
        var config = (await s.Repo.Flow.ConfigStore.ReadAsync(s.Repo.WorkDir, ConfigScope.Local))!;
        var branch = config.TryParseFlowBranch(branchName)!;
        var targets = await s.Flow.GetMergeTargetsAsync(s.Repo.WorkDir, config, branch);
        var tag = branch.Type == BranchType.Feature ? null : config.GetTagName(branch.Name);
        var drafts = await s.PullRequests.PrepareAsync(s.Repo.WorkDir, branch, targets, tag);
        return await s.Flow.FinishAsync(s.Repo.WorkDir, new FinishOptions { BranchName = branchName, PullRequests = drafts, TagMessage = "Livraison " + branch.Name });
    }

    [Fact]
    public async Task Remote_is_recognised_from_its_configured_url()
    {
        await using var s = await CreateAsync();

        var remote = await s.PullRequests.GetRemoteAsync(s.Repo.WorkDir);

        remote.ShouldNotBeNull();
        remote.Platform.ShouldBe(HostingPlatform.GitHub);
        remote.Name.ShouldBe("repo");
    }

    [Fact]
    public async Task Drafts_are_generated_from_git()
    {
        await using var s = await CreateAsync();
        await s.Flow.StartAsync(s.Repo.WorkDir, BranchType.Feature, "ABC-9-login");
        await s.Repo.CommitFileAsync("a.txt", "a\n", "Ajoute a");
        await s.Repo.CommitFileAsync("b.txt", "b\nb\n", "Ajoute b (#42)");
        var config = (await s.Repo.Flow.ConfigStore.ReadAsync(s.Repo.WorkDir, ConfigScope.Local))!;

        var draft = (await s.PullRequests.PrepareAsync(s.Repo.WorkDir, config.TryParseFlowBranch("feature/ABC-9-login")!, ["develop"], null)).ShouldHaveSingleItem();

        draft.Title.ShouldBe("Feature: ABC 9 login");
        draft.Target.ShouldBe("develop");
        draft.Description.ShouldContain("### Commits (2)" + Environment.NewLine + "- Ajoute a" + Environment.NewLine + "- Ajoute b (#42)");
        draft.Description.ShouldContain("2 fichier(s) modifié(s), 3 ligne(s) ajoutée(s), 0 ligne(s) supprimée(s)");
        draft.Description.ShouldContain("ABC-9, #42");
    }

    [Fact]
    public async Task Drafts_do_not_fail_without_a_merge_base()
    {
        // "git diff develop...feature/x" is fatal when the two histories share no commit (seen on a company server).
        await using var s = await CreateAsync();
        await s.Repo.GitAsync("switch", "--orphan", "feature/unrelated");
        await s.Repo.CommitFileAsync("only.txt", "x\n", "Ajoute only");
        var config = (await s.Repo.Flow.ConfigStore.ReadAsync(s.Repo.WorkDir, ConfigScope.Local))!;

        var draft = (await s.PullRequests.PrepareAsync(s.Repo.WorkDir, config.TryParseFlowBranch("feature/unrelated")!, ["develop"], null)).ShouldHaveSingleItem();

        draft.Title.ShouldBe("Feature: unrelated");
        draft.Description.ShouldContain("- Ajoute only");
    }

    [Fact]
    public async Task Release_is_refused_before_any_push_when_origin_master_is_unrelated()
    {
        // Repository created on the server with a README, local history pushed beside it: origin/master shares no commit.
        await using var s = await CreateAsync();
        await s.Repo.GitAsync("switch", "--orphan", "server-readme");
        await s.Repo.CommitFileAsync("README.md", "# Server\n", "Initial commit");
        await s.Repo.GitAsync("push", "--force", "origin", "server-readme:master");
        await s.Repo.GitAsync("switch", "develop");
        await s.Repo.GitAsync("branch", "-D", "server-readme");
        await s.Repo.GitAsync("fetch", "origin");
        await s.Flow.StartAsync(s.Repo.WorkDir, BranchType.Release, "2.0.0");
        await s.Repo.CommitFileAsync("version.txt", "2.0.0\n", "Version 2.0.0");

        var finish = await FinishWithPullRequestsAsync(s, "release/2.0.0");

        finish.Error.ShouldBe(FlowError.NoCommonHistory);
        finish.Detail.ShouldBe("master");
        GitFlow.Core.Resources.FlowMessages.Describe(finish).ShouldContain("git merge origin/master --allow-unrelated-histories");
        (await s.Repo.OriginBranchesAsync()).ShouldNotContain("release/2.0.0", "nothing is pushed");
        s.Platform.Created.ShouldBe(0);

        // The advice of the message is enough: join the histories of master once, then finish again.
        await s.Repo.GitAsync("switch", "master");
        // Both histories have a README.md: the usual add/add conflict, resolved by keeping the local file.
        await s.Repo.TryGitAsync("merge", "origin/master", "--allow-unrelated-histories", "-m", "Join server history");
        await s.Repo.GitAsync("checkout", "--ours", "--", ".");
        await s.Repo.GitAsync("add", "-A");
        await s.Repo.GitAsync("commit", "--no-edit");
        await s.Repo.GitAsync("push", "origin", "master");
        await s.Repo.GitAsync("switch", "release/2.0.0");

        var retry = await FinishWithPullRequestsAsync(s, "release/2.0.0");

        retry.IsSuccess.ShouldBeTrue(retry.Detail);
        retry.PullRequests.Select(p => p.Target).ShouldBe(["master", "develop"]);
    }

    [Fact]
    public async Task Feature_through_a_pull_request()
    {
        await using var s = await CreateAsync();
        await s.Flow.StartAsync(s.Repo.WorkDir, BranchType.Feature, "login");
        var work = await s.Repo.CommitFileAsync("login.txt", "login\n", "Ajoute login");
        var developBefore = await s.Repo.GitAsync("rev-parse", "develop");

        var finish = await FinishWithPullRequestsAsync(s, "feature/login");

        finish.IsSuccess.ShouldBeTrue(finish.Detail);
        var pr = finish.PullRequests.ShouldHaveSingleItem();
        pr.Target.ShouldBe("develop");
        pr.State.ShouldBe(PullRequestState.Open);
        (await s.Repo.OriginBranchesAsync()).ShouldContain("feature/login", "the branch is pushed for the PR");
        (await s.Repo.GitAsync("rev-parse", "develop")).ShouldBe(developBefore, "nothing is merged locally");
        (await s.Repo.LocalBranchesAsync()).ShouldContain("feature/login");
        (await s.PullRequests.GetTrackedAsync(s.Repo.WorkDir, refresh: true)).ShouldHaveSingleItem().Info!.State.ShouldBe(PullRequestState.Open);

        // Not merged yet: nothing is deleted.
        (await s.PullRequests.CompleteAsync(s.Repo.WorkDir, "feature/login")).Error.ShouldBe(FlowError.PullRequestNotMerged);

        await s.Platform.MergeAsync(pr.Id);
        var complete = await s.PullRequests.CompleteAsync(s.Repo.WorkDir, "feature/login");

        complete.IsSuccess.ShouldBeTrue(complete.Detail);
        (await s.Repo.LocalBranchesAsync()).ShouldNotContain("feature/login");
        (await s.Repo.IsAncestorAsync(work, "develop")).ShouldBeTrue("local develop fast-forwarded to the merged PR");
        (await s.PullRequests.GetTrackedAsync(s.Repo.WorkDir, refresh: false)).ShouldBeEmpty();
    }

    [Fact]
    public async Task Finishing_again_reuses_the_open_pull_request()
    {
        await using var s = await CreateAsync();
        await s.Flow.StartAsync(s.Repo.WorkDir, BranchType.Feature, "x");
        await s.Repo.CommitFileAsync("x.txt", "x\n", "X");

        var first = await FinishWithPullRequestsAsync(s, "feature/x");
        await s.Repo.CommitFileAsync("y.txt", "y\n", "Y");
        var second = await FinishWithPullRequestsAsync(s, "feature/x");

        second.PullRequests.ShouldHaveSingleItem().Id.ShouldBe(first.PullRequests[0].Id);
        s.Platform.Created.ShouldBe(1);
        (await s.Repo.GitInAsync(s.Repo.OriginDir, "rev-parse", "feature/x")).ShouldBe(await s.Repo.GitAsync("rev-parse", "feature/x"), "new commits pushed to the PR");
    }

    [Fact]
    public async Task Release_through_pull_requests_is_tagged_on_the_merge_commit()
    {
        await using var s = await CreateAsync();
        await s.Flow.StartAsync(s.Repo.WorkDir, BranchType.Release, "1.0.0");
        var bump = await s.Repo.CommitFileAsync("version.txt", "1.0.0\n", "Version 1.0.0");

        var finish = await FinishWithPullRequestsAsync(s, "release/1.0.0");

        finish.IsSuccess.ShouldBeTrue(finish.Detail);
        finish.PullRequests.Select(p => p.Target).ShouldBe(["master", "develop"]);
        finish.TagName.ShouldBeNull("the tag waits for the merge into master");
        (await s.Repo.GitAsync("tag", "--list")).ShouldBeEmpty();

        (await s.PullRequests.TagMergedAsync(s.Repo.WorkDir, "release/1.0.0")).Error.ShouldBe(FlowError.PullRequestNotMerged);

        var mergeCommit = await s.Platform.MergeAsync(finish.PullRequests[0].Id);
        var tag = await s.PullRequests.TagMergedAsync(s.Repo.WorkDir, "release/1.0.0");

        tag.IsSuccess.ShouldBeTrue(tag.Detail);
        tag.TagName.ShouldBe("1.0.0");
        (await s.Repo.GitAsync("rev-parse", "1.0.0^{commit}")).ShouldBe(mergeCommit);
        (await s.Repo.GitAsync("cat-file", "-t", "1.0.0")).ShouldBe("tag");
        (await s.Repo.GitAsync("tag", "-l", "--format=%(contents:subject)", "1.0.0")).ShouldBe("Livraison 1.0.0");
        (await s.Repo.GitInAsync(s.Repo.OriginDir, "tag", "--list")).ShouldBe("1.0.0", "tag pushed");

        // The PR to develop is still open: the branch is kept.
        (await s.PullRequests.CompleteAsync(s.Repo.WorkDir, "release/1.0.0")).Error.ShouldBe(FlowError.PullRequestNotMerged);
        await s.Platform.MergeAsync(finish.PullRequests[1].Id);
        (await s.PullRequests.CompleteAsync(s.Repo.WorkDir, "release/1.0.0")).IsSuccess.ShouldBeTrue();

        (await s.Repo.CurrentBranchAsync()).ShouldBe("develop", "ends on develop even after fast-forwarding master");
        (await s.Repo.LocalBranchesAsync()).ShouldNotContain("release/1.0.0");
        (await s.Repo.IsAncestorAsync(bump, "master")).ShouldBeTrue();
        (await s.Repo.IsAncestorAsync(bump, "develop")).ShouldBeTrue();
    }

    [Fact]
    public async Task Completing_a_release_tags_it_when_needed()
    {
        await using var s = await CreateAsync();
        await s.Flow.StartAsync(s.Repo.WorkDir, BranchType.Hotfix, "0.0.1");
        await s.Repo.CommitFileAsync("fix.txt", "fix\n", "Fix");
        var finish = await FinishWithPullRequestsAsync(s, "hotfix/0.0.1");
        (await s.PullRequests.IsTaggedAsync(s.Repo.WorkDir, "hotfix/0.0.1")).ShouldBeFalse();

        var mergeCommit = await s.Platform.MergeAsync(finish.PullRequests[0].Id);
        await s.Platform.MergeAsync(finish.PullRequests[1].Id);
        var complete = await s.PullRequests.CompleteAsync(s.Repo.WorkDir, "hotfix/0.0.1");

        complete.IsSuccess.ShouldBeTrue(complete.Detail);
        complete.TagName.ShouldBe("0.0.1");
        (await s.Repo.CurrentBranchAsync()).ShouldBe("develop");
        (await s.Repo.GitAsync("rev-parse", "0.0.1^{commit}")).ShouldBe(mergeCommit);
        (await s.Repo.LocalBranchesAsync()).ShouldNotContain("hotfix/0.0.1");
    }

    [Fact]
    public async Task Release_without_own_commits_ends_on_develop()
    {
        // Real case seen on Azure DevOps: the PR to develop has nothing to merge, only master moves.
        await using var s = await CreateAsync();
        await s.Flow.StartAsync(s.Repo.WorkDir, BranchType.Feature, "x");
        await s.Repo.CommitFileAsync("x.txt", "x\n", "X");
        await s.Repo.Flow.FinishAsync(s.Repo.WorkDir, new FinishOptions { PushAfterFinish = true });
        await s.Flow.StartAsync(s.Repo.WorkDir, BranchType.Release, "2.0.0");
        var finish = await FinishWithPullRequestsAsync(s, "release/2.0.0");
        await s.Platform.MergeAsync(finish.PullRequests[0].Id);
        await s.Platform.MergeAsync(finish.PullRequests[1].Id);
        var developBefore = await s.Repo.GitAsync("rev-parse", "develop");

        var complete = await s.PullRequests.CompleteAsync(s.Repo.WorkDir, "release/2.0.0");

        complete.IsSuccess.ShouldBeTrue(complete.Detail);
        (await s.Repo.GitAsync("rev-parse", "develop")).ShouldBe(developBefore, "nothing to merge into develop");
        (await s.Repo.CurrentBranchAsync()).ShouldBe("develop", "not left on master");
        (await s.Repo.GitAsync("rev-parse", "2.0.0^{commit}")).ShouldBe(await s.Repo.GitAsync("rev-parse", "master"));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Complete_prunes_deleted_remote_branches_only_when_asked(bool prune)
    {
        await using var s = await CreateAsync();
        await s.Flow.StartAsync(s.Repo.WorkDir, BranchType.Feature, "old");
        await s.Repo.CommitFileAsync("o.txt", "o\n", "O");
        var finish = await FinishWithPullRequestsAsync(s, "feature/old");
        await s.Platform.MergeAsync(finish.PullRequests[0].Id);
        // The platform deletes the source branch after the merge ("delete branch after merging").
        await s.Repo.GitInAsync(s.Repo.OriginDir, "branch", "-D", "feature/old");
        // An unrelated local branch, never touched by --prune.
        await s.Repo.GitAsync("branch", "keep-me", "develop");

        var complete = await s.PullRequests.CompleteAsync(s.Repo.WorkDir, "feature/old", prune);

        complete.IsSuccess.ShouldBeTrue(complete.Detail);
        var remoteRefs = await s.Repo.GitAsync("for-each-ref", "--format=%(refname:short)", "refs/remotes/origin/");
        if (prune)
        {
            remoteRefs.ShouldNotContain("origin/feature/old");
        }
        else
        {
            remoteRefs.ShouldContain("origin/feature/old", Case.Sensitive, "without the option the stale reference stays");
        }

        (await s.Repo.LocalBranchesAsync()).ShouldContain("keep-me");
        (await s.Repo.LocalBranchesAsync()).ShouldNotContain("feature/old");
    }

    [Fact]
    public async Task Squash_merged_branch_is_never_deleted()
    {
        await using var s = await CreateAsync();
        await s.Flow.StartAsync(s.Repo.WorkDir, BranchType.Feature, "squashed");
        await s.Repo.CommitFileAsync("s.txt", "s\n", "S");
        var finish = await FinishWithPullRequestsAsync(s, "feature/squashed");

        await s.Platform.MergeAsync(finish.PullRequests[0].Id, squash: true);
        var complete = await s.PullRequests.CompleteAsync(s.Repo.WorkDir, "feature/squashed");

        complete.Error.ShouldBe(FlowError.BranchNotMergedOnRemote);
        (await s.Repo.LocalBranchesAsync()).ShouldContain("feature/squashed");
    }

    [Fact]
    public async Task Unknown_platform_keeps_the_local_finish_available()
    {
        await using var repo = await TestRepository.CreateAsync();
        (await repo.Flow.InitAsync(repo.WorkDir, GitFlowConfig.Default)).IsSuccess.ShouldBeTrue();
        var service = new PullRequestService(repo.Runner, [new FakePlatform(repo)]);
        var flow = new FlowService(repo.Runner, pullRequestStrategy: new PullRequestStrategy(service));
        await flow.StartAsync(repo.WorkDir, BranchType.Feature, "x");
        await repo.CommitFileAsync("x.txt", "x\n", "X");

        (await service.GetRemoteAsync(repo.WorkDir)).ShouldBeNull("origin is a plain local path");
        var prMode = await flow.FinishAsync(repo.WorkDir, new FinishOptions { PullRequests = [new PullRequestDraft("feature/x", "develop", "t", "d")] });
        prMode.Error.ShouldBe(FlowError.PullRequestNotSupported);
        (await repo.LocalBranchesAsync()).ShouldContain("feature/x");

        (await flow.FinishAsync(repo.WorkDir)).IsSuccess.ShouldBeTrue("local merge still works");
    }

    [Fact]
    public async Task Missing_credentials_are_reported()
    {
        await using var s = await CreateAsync();
        await s.Repo.GitAsync("config", "--global", "--unset-all", "credential.helper");
        await s.Flow.StartAsync(s.Repo.WorkDir, BranchType.Feature, "x");
        await s.Repo.CommitFileAsync("x.txt", "x\n", "X");

        var result = await FinishWithPullRequestsAsync(s, "feature/x");

        result.Error.ShouldBe(FlowError.NoCredentials);
        result.Detail.ShouldBe("github.com");
        s.Platform.Created.ShouldBe(0);
    }

    /// <summary>In-memory hosting platform; a merge really merges on the bare origin through a scratch clone.</summary>
    private sealed class FakePlatform(TestRepository repo) : IPullRequestProvider
    {
        private readonly List<PullRequestInfo> _prs = [];

        public int Created { get; private set; }

        public HostingPlatform Platform => HostingPlatform.GitHub;

        public Task<PullRequestInfo> CreateAsync(RemoteRepository repository, HostCredential credential, PullRequestDraft draft, CancellationToken ct)
        {
            credential.Secret.ShouldBe("fake-token");
            var pr = new PullRequestInfo(_prs.Count + 1, new Uri($"https://github.com/test/repo/pull/{_prs.Count + 1}"), PullRequestState.Open, draft.Source, draft.Target, draft.Title, null);
            _prs.Add(pr);
            Created++;
            return Task.FromResult(pr);
        }

        public Task<PullRequestInfo?> FindAsync(RemoteRepository repository, HostCredential credential, string source, string target, CancellationToken ct) =>
            Task.FromResult(_prs.LastOrDefault(p => p.Source == source && p.Target == target));

        public Task<PullRequestInfo?> GetAsync(RemoteRepository repository, HostCredential credential, int id, CancellationToken ct) =>
            Task.FromResult(_prs.FirstOrDefault(p => p.Id == id));

        /// <summary>Merges the PR on origin (--no-ff, or squash) and returns the merge commit.</summary>
        public async Task<string> MergeAsync(int id, bool squash = false)
        {
            var pr = _prs.Single(p => p.Id == id);
            var clone = Path.Combine(repo.Root, "platform-" + Guid.NewGuid().ToString("N")[..6]);
            await repo.GitInAsync(repo.Root, "clone", "-q", "-b", pr.Target, repo.OriginDir, clone);
            await repo.GitInAsync(clone, "config", "user.name", "Platform");
            await repo.GitInAsync(clone, "config", "user.email", "platform@example.invalid");
            if (squash)
            {
                await repo.GitInAsync(clone, "merge", "--squash", "origin/" + pr.Source);
                await repo.GitInAsync(clone, "commit", "-m", $"{pr.Title} (#{id})");
            }
            else
            {
                await repo.GitInAsync(clone, "merge", "--no-ff", "-m", $"Merge pull request #{id}", "origin/" + pr.Source);
            }

            await repo.GitInAsync(clone, "push", "-q", "origin", pr.Target);
            var commit = await repo.GitInAsync(clone, "rev-parse", "HEAD");
            _prs[_prs.IndexOf(pr)] = pr with { State = PullRequestState.Merged, MergeCommit = commit };
            return commit;
        }
    }
}
