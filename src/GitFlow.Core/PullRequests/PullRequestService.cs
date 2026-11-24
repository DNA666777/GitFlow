using GitFlow.Core.Config;
using GitFlow.Core.Flow;
using GitFlow.Core.Git;
using GitFlow.Core.Model;

namespace GitFlow.Core.PullRequests;

/// <summary>A pull request opened by the extension for a work branch, with its live state when it could be read.</summary>
public sealed record TrackedPullRequest(string Source, string Target, int Id, PullRequestInfo? Info, string? Error);

/// <summary>
/// Optional Pull Request mode: prepares the messages, pushes the branch and opens one PR per merge target,
/// follows them, tags the merge commit once the PR to production is merged, and cleans up when all are merged.
/// Never approves nor merges a pull request. Links are kept in the local git config (gitflowvs-pr.&lt;branch&gt;.link).
/// </summary>
public sealed class PullRequestService
{
    private const string Remote = FlowService.RemoteName;
    private readonly IGitRunner _runner;
    private readonly IReadOnlyDictionary<HostingPlatform, IPullRequestProvider> _providers;
    private readonly GitCredentialProvider _credentials;
    private readonly GitFlowConfigStore _configStore;

    public PullRequestService(IGitRunner runner, HttpClient http)
        : this(runner, [new GitHubPullRequestProvider(http), new AzureDevOpsPullRequestProvider(http)])
    {
    }

    public PullRequestService(IGitRunner runner, IEnumerable<IPullRequestProvider> providers)
    {
        _runner = runner;
        _providers = providers.ToDictionary(p => p.Platform);
        _credentials = new GitCredentialProvider(runner);
        _configStore = new GitFlowConfigStore(runner);
    }

    /// <summary>The hosted repository behind origin, or null when PR mode is not available (no origin, unknown platform).</summary>
    public async Task<RemoteRepository?> GetRemoteAsync(string repositoryPath, CancellationToken ct = default)
    {
        // The configured URL as written (url.*.insteadOf rewrites are not applied): it names the hosting platform.
        var result = await _runner.RunAsync(repositoryPath, ["config", "--get", $"remote.{Remote}.url"], ct).ConfigureAwait(false);
        var remote = result.Success ? RemoteRepository.TryParse(result.StandardOutput.Trim()) : null;
        return remote is not null && _providers.ContainsKey(remote.Platform) ? remote : null;
    }

    /// <summary>Generated title + description for each merge target, from git only.</summary>
    public async Task<IReadOnlyList<PullRequestDraft>> PrepareAsync(
        string repositoryPath, FlowBranch branch, IReadOnlyList<string> targets, string? tagName, CancellationToken ct = default)
    {
        var git = new GitClient(_runner, repositoryPath);
        var remote = await GetRemoteAsync(repositoryPath, ct).ConfigureAwait(false);
        var template = LoadTemplate(repositoryPath, remote?.Platform);
        var pattern = (await git.RunAsync(ct, "config", "--get", "gitflowvs.pr.ticketpattern").ConfigureAwait(false)).StandardOutput.Trim();
        var source = "refs/heads/" + branch.FullName;

        var drafts = new List<PullRequestDraft>();
        foreach (var target in targets)
        {
            // Compare with what the PR will be compared with: the remote target when it is known.
            var targetRef = await git.ResolveCommitAsync($"refs/remotes/{Remote}/{target}", ct).ConfigureAwait(false) is not null
                ? $"refs/remotes/{Remote}/{target}"
                : "refs/heads/" + target;
            var subjects = await git.RunCheckedAsync(ct, "log", "--no-merges", "--reverse", "--format=%s", $"{targetRef}..{source}").ConfigureAwait(false);
            var messages = await git.RunCheckedAsync(ct, "log", "--no-merges", "--format=%B", $"{targetRef}..{source}").ConfigureAwait(false);
            var stat = await git.RunCheckedAsync(ct, "diff", "--shortstat", $"{targetRef}...{source}").ConfigureAwait(false);
            var context = new PullRequestContext(
                branch,
                target,
                GitClient.Lines(subjects.StandardOutput),
                messages.StandardOutput,
                DiffSummary.Parse(stat.StandardOutput),
                tagName);
            drafts.Add(PullRequestMessageGenerator.Build(context, template, pattern.Length == 0 ? null : pattern));
        }

        return drafts;
    }

    /// <summary>Pushes the branch and opens (or reuses the open) pull request for each draft.</summary>
    public async Task<FlowResult> OpenAsync(FinishPlan plan, CancellationToken ct = default)
    {
        var drafts = plan.PullRequests ?? [];
        var (remote, provider, credential, error) = await ConnectAsync(plan.RepositoryPath, ct).ConfigureAwait(false);
        if (error is not null)
        {
            return error;
        }

        var git = new GitClient(_runner, plan.RepositoryPath);
        var source = plan.Branch.FullName;
        var push = await git.RunAsync(ct, "push", "-u", Remote, source).ConfigureAwait(false);
        if (!push.Success)
        {
            return FlowResult.Fail(FlowError.GitCommandFailed, push.StandardError.Trim(), push);
        }

        var opened = new List<PullRequestInfo>();
        try
        {
            foreach (var draft in drafts)
            {
                var existing = await provider!.FindAsync(remote!, credential!, draft.Source, draft.Target, ct).ConfigureAwait(false);
                var pr = existing is { State: PullRequestState.Open }
                    ? existing
                    : await provider.CreateAsync(remote!, credential!, draft, ct).ConfigureAwait(false);
                opened.Add(pr);
                await SaveLinksAsync(git, source, opened, plan.TagMessage, ct).ConfigureAwait(false);
            }
        }
        catch (PullRequestApiException ex)
        {
            return FlowResult.Fail(FlowError.PullRequestApiFailed, ex.Message) with { PullRequests = opened };
        }

        return FlowResult.Ok(source) with { PullRequests = opened };
    }

    /// <summary>Pull requests opened by the extension; with <paramref name="refresh"/>, their state is read on the platform.</summary>
    public async Task<IReadOnlyList<TrackedPullRequest>> GetTrackedAsync(string repositoryPath, bool refresh, CancellationToken ct = default)
    {
        var links = await ReadLinksAsync(repositoryPath, ct).ConfigureAwait(false);
        if (!refresh || links.Count == 0)
        {
            return links.Select(l => new TrackedPullRequest(l.Source, l.Target, l.Id, null, null)).ToArray();
        }

        var (remote, provider, credential, error) = await ConnectAsync(repositoryPath, ct).ConfigureAwait(false);
        var tracked = new List<TrackedPullRequest>();
        foreach (var (source, target, id) in links)
        {
            if (error is not null)
            {
                tracked.Add(new TrackedPullRequest(source, target, id, null, Resources.FlowMessages.Describe(error)));
                continue;
            }

            try
            {
                var info = await provider!.GetAsync(remote!, credential!, id, ct).ConfigureAwait(false);
                tracked.Add(new TrackedPullRequest(source, target, id, info, info is null ? "404" : null));
            }
            catch (Exception ex) when (ex is PullRequestApiException or HttpRequestException)
            {
                tracked.Add(new TrackedPullRequest(source, target, id, null, ex.Message));
            }
        }

        return tracked;
    }

    /// <summary>True when the version tag of a release/hotfix branch exists (always false for a feature).</summary>
    public async Task<bool> IsTaggedAsync(string repositoryPath, string branchName, CancellationToken ct = default)
    {
        var config = await _configStore.ReadAsync(repositoryPath, ConfigScope.Local, ct).ConfigureAwait(false);
        return config?.TryParseFlowBranch(branchName) is { Type: not BranchType.Feature } branch
            && await new GitClient(_runner, repositoryPath).TagExistsAsync(config.GetTagName(branch.Name), ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Release/hotfix: once the PR to production is merged, tags its merge commit ({prefix}{version}, annotated) and pushes the tag.
    /// </summary>
    public async Task<FlowResult> TagMergedAsync(string repositoryPath, string branchName, CancellationToken ct = default)
    {
        try
        {
            var config = await _configStore.ReadAsync(repositoryPath, ConfigScope.Local, ct).ConfigureAwait(false);
            var branch = config?.TryParseFlowBranch(branchName);
            if (config is null || branch is null || branch.Type == BranchType.Feature)
            {
                return FlowResult.Fail(config is null ? FlowError.NotInitialized : FlowError.NotAFlowBranch, branchName);
            }

            var git = new GitClient(_runner, repositoryPath);
            var tagName = config.GetTagName(branch.Name);
            if (await git.TagExistsAsync(tagName, ct).ConfigureAwait(false))
            {
                return FlowResult.Ok(branchName, tagName);
            }

            var toMaster = (await GetTrackedAsync(repositoryPath, refresh: true, ct).ConfigureAwait(false))
                .FirstOrDefault(t => t.Source == branchName && t.Target == config.MasterBranch);
            if (toMaster?.Info is not { State: PullRequestState.Merged } merged)
            {
                return FlowResult.Fail(FlowError.PullRequestNotMerged, config.MasterBranch);
            }

            await git.RunCheckedAsync(ct, "fetch", Remote).ConfigureAwait(false);
            var remoteMaster = $"refs/remotes/{Remote}/{config.MasterBranch}";
            if (merged.MergeCommit is not { } mergeCommit
                || await git.ResolveCommitAsync(mergeCommit, ct).ConfigureAwait(false) is null
                || !await git.IsAncestorAsync(mergeCommit, remoteMaster, ct).ConfigureAwait(false))
            {
                return FlowResult.Fail(FlowError.MergeCommitNotFound, config.MasterBranch);
            }

            var message = (await git.RunAsync(ct, "config", "--local", "--get", LinkSection(branchName) + ".tagmessage").ConfigureAwait(false)).StandardOutput.Trim();
            await git.RunCheckedAsync(ct, "tag", "-a", tagName, "-m", message.Length > 0 ? message : FlowService.GetDefaultTagMessage(branch.Type, branch.Name), mergeCommit).ConfigureAwait(false);

            var push = await git.RunAsync(ct, "push", Remote, "refs/tags/" + tagName).ConfigureAwait(false);
            return push.Success
                ? FlowResult.Ok(branchName, tagName)
                : FlowResult.Ok(branchName, tagName, [new FlowWarning(FlowWarningKind.PushFailed, push.StandardError.Trim(), push)]);
        }
        catch (GitCommandException ex)
        {
            return FlowResult.Fail(FlowError.GitCommandFailed, ex.Result.StandardError.Trim(), ex.Result);
        }
    }

    /// <summary>
    /// When every PR of the branch is merged: updates the local targets (fast-forward only) and deletes the local branch
    /// with <c>git branch -d</c> (refused by git if it is not merged, e.g. after a squash merge). Release/hotfix must be tagged first.
    /// </summary>
    public async Task<FlowResult> CompleteAsync(string repositoryPath, string branchName, CancellationToken ct = default)
    {
        try
        {
            var config = await _configStore.ReadAsync(repositoryPath, ConfigScope.Local, ct).ConfigureAwait(false);
            var branch = config?.TryParseFlowBranch(branchName);
            if (config is null || branch is null)
            {
                return FlowResult.Fail(config is null ? FlowError.NotInitialized : FlowError.NotAFlowBranch, branchName);
            }

            var git = new GitClient(_runner, repositoryPath);
            var prs = (await GetTrackedAsync(repositoryPath, refresh: true, ct).ConfigureAwait(false)).Where(t => t.Source == branchName).ToArray();
            if (prs.Length == 0)
            {
                return FlowResult.Fail(FlowError.PullRequestNotMerged, config.GetBaseBranch(branch.Type));
            }

            if (prs.FirstOrDefault(p => p.Info?.State != PullRequestState.Merged) is { } pending)
            {
                return FlowResult.Fail(FlowError.PullRequestNotMerged, pending.Target);
            }

            // Release/hotfix: the tag goes on the merge commit of the PR to production (done here if not yet).
            var tagName = branch.Type == BranchType.Feature ? null : config.GetTagName(branch.Name);
            if (tagName is not null && !await git.TagExistsAsync(tagName, ct).ConfigureAwait(false))
            {
                var tagged = await TagMergedAsync(repositoryPath, branchName, ct).ConfigureAwait(false);
                if (!tagged.IsSuccess)
                {
                    return tagged;
                }
            }

            if (await git.IsWorkingTreeDirtyAsync(ct).ConfigureAwait(false))
            {
                return FlowResult.Fail(FlowError.DirtyWorkingTree);
            }

            await git.RunCheckedAsync(ct, "fetch", Remote).ConfigureAwait(false);
            var targets = prs.Select(p => p.Target).Distinct().ToArray();
            foreach (var target in targets)
            {
                var remoteRef = $"refs/remotes/{Remote}/{target}";
                if (!await git.LocalBranchExistsAsync(target, ct).ConfigureAwait(false)
                    || await git.ResolveCommitAsync(remoteRef, ct).ConfigureAwait(false) is null)
                {
                    continue;
                }

                if (!await git.IsAncestorAsync("refs/heads/" + branchName, remoteRef, ct).ConfigureAwait(false))
                {
                    return FlowResult.Fail(FlowError.BranchNotMergedOnRemote, target);
                }

                if (!await git.IsAncestorAsync(remoteRef, "refs/heads/" + target, ct).ConfigureAwait(false)
                    && await git.IsAncestorAsync("refs/heads/" + target, remoteRef, ct).ConfigureAwait(false))
                {
                    await git.RunCheckedAsync(ct, "switch", target).ConfigureAwait(false);
                    await git.RunCheckedAsync(ct, "merge", "--ff-only", remoteRef).ConfigureAwait(false);
                }
            }

            var finalBranch = targets.LastOrDefault() ?? config.DevelopBranch;
            if (await git.GetCurrentBranchAsync(ct).ConfigureAwait(false) == branchName)
            {
                await git.RunCheckedAsync(ct, "switch", finalBranch).ConfigureAwait(false);
            }

            var warnings = new List<FlowWarning>();
            var delete = await git.RunAsync(ct, "branch", "-d", branchName).ConfigureAwait(false);
            if (!delete.Success)
            {
                warnings.Add(new FlowWarning(FlowWarningKind.LocalBranchDeleteFailed, branchName, delete));
            }
            else
            {
                await git.RunAsync(ct, "config", "--local", "--remove-section", LinkSection(branchName)).ConfigureAwait(false);
            }

            return FlowResult.Ok(await git.GetCurrentBranchAsync(ct).ConfigureAwait(false), tagName, warnings);
        }
        catch (GitCommandException ex)
        {
            return FlowResult.Fail(FlowError.GitCommandFailed, ex.Result.StandardError.Trim(), ex.Result);
        }
    }

    private async Task<(RemoteRepository? Remote, IPullRequestProvider? Provider, HostCredential? Credential, FlowResult? Error)> ConnectAsync(
        string repositoryPath, CancellationToken ct)
    {
        var remote = await GetRemoteAsync(repositoryPath, ct).ConfigureAwait(false);
        if (remote is null)
        {
            return (null, null, null, FlowResult.Fail(FlowError.PullRequestNotSupported));
        }

        var credential = await _credentials.GetAsync(repositoryPath, remote, ct).ConfigureAwait(false);
        return credential is null
            ? (remote, null, null, FlowResult.Fail(FlowError.NoCredentials, remote.CredentialHost))
            : (remote, _providers[remote.Platform], credential, null);
    }

    private static string LinkSection(string branch) => "gitflowvs-pr." + branch;

    private static async Task SaveLinksAsync(GitClient git, string source, IReadOnlyList<PullRequestInfo> prs, string? tagMessage, CancellationToken ct)
    {
        var section = LinkSection(source);
        await git.RunAsync(ct, "config", "--local", "--unset-all", section + ".link").ConfigureAwait(false);
        foreach (var pr in prs)
        {
            await git.RunCheckedAsync(ct, "config", "--local", "--add", section + ".link", $"{pr.Target} {pr.Id}").ConfigureAwait(false);
        }

        if (!string.IsNullOrWhiteSpace(tagMessage))
        {
            await git.RunCheckedAsync(ct, "config", "--local", section + ".tagmessage", tagMessage).ConfigureAwait(false);
        }
    }

    private async Task<IReadOnlyList<(string Source, string Target, int Id)>> ReadLinksAsync(string repositoryPath, CancellationToken ct)
    {
        var result = await _runner.RunAsync(repositoryPath, ["config", "--local", "--null", "--get-regexp", @"^gitflowvs-pr\..*\.link$"], ct).ConfigureAwait(false);
        if (!result.Success)
        {
            return [];
        }

        var links = new List<(string, string, int)>();
        foreach (var entry in result.StandardOutput.Split('\0', StringSplitOptions.RemoveEmptyEntries))
        {
            var newline = entry.IndexOf('\n');
            if (newline < 0)
            {
                continue;
            }

            // Key: gitflowvs-pr.<source>.link (the source may contain dots and slashes).
            var key = entry[..newline];
            var source = key["gitflowvs-pr.".Length..^".link".Length];
            var value = entry[(newline + 1)..].Trim();
            var space = value.LastIndexOf(' ');
            if (space > 0 && int.TryParse(value[(space + 1)..], System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var id))
            {
                links.Add((source, value[..space], id));
            }
        }

        return links;
    }

    /// <summary>
    /// Our template (.gitflow/pull_request_template.md, may use variables), else the platform's own template (used as a base).
    /// </summary>
    internal static string? LoadTemplate(string repositoryPath, HostingPlatform? platform)
    {
        var candidates = new List<string> { ".gitflow/pull_request_template.md" };
        candidates.AddRange(platform switch
        {
            HostingPlatform.GitHub => [".github/pull_request_template.md", ".github/PULL_REQUEST_TEMPLATE.md", "docs/pull_request_template.md", "pull_request_template.md"],
            HostingPlatform.AzureDevOps => [".azuredevops/pull_request_template.md", ".vsts/pull_request_template.md", "docs/pull_request_template.md", "pull_request_template.md"],
            _ => [],
        });

        foreach (var candidate in candidates)
        {
            var path = Path.Combine(repositoryPath, candidate);
            if (File.Exists(path))
            {
                return File.ReadAllText(path).Replace("\r\n", "\n", StringComparison.Ordinal);
            }
        }

        return null;
    }
}

/// <summary>Finish in Pull Request mode: push + one PR per target, nothing merged locally.</summary>
public sealed class PullRequestStrategy(PullRequestService service) : IFinishStrategy
{
    public Task<FlowResult> ExecuteAsync(FinishPlan plan, CancellationToken cancellationToken) => service.OpenAsync(plan, cancellationToken);
}
