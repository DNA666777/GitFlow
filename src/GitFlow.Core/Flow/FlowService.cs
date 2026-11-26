using GitFlow.Core.Config;
using GitFlow.Core.Git;
using GitFlow.Core.Model;

namespace GitFlow.Core.Flow;

/// <summary>
/// Gitflow operations (Atlassian Gitflow Workflow). Expected failures are returned as <see cref="FlowResult"/>,
/// never thrown. Nothing is changed before every precondition has been checked.
/// </summary>
public sealed class FlowService
{
    public const string RemoteName = "origin";

    private readonly IGitRunner _runner;
    private readonly IFinishStrategy _finishStrategy;
    private readonly IFinishStrategy? _pullRequestStrategy;

    /// <param name="finishStrategy">Local merge (default: <see cref="LocalMergeStrategy"/>).</param>
    /// <param name="pullRequestStrategy">Optional Pull Request mode; when null, PR mode is unavailable.</param>
    public FlowService(IGitRunner runner, IFinishStrategy? finishStrategy = null, IFinishStrategy? pullRequestStrategy = null)
    {
        _runner = runner;
        _finishStrategy = finishStrategy ?? new LocalMergeStrategy(runner);
        _pullRequestStrategy = pullRequestStrategy;
        ConfigStore = new GitFlowConfigStore(runner);
    }

    public GitFlowConfigStore ConfigStore { get; }

    public static string GetDefaultTagMessage(BranchType type, string version) =>
        type == BranchType.Hotfix ? $"Hotfix {version}" : $"Release {version}";

    /// <summary>Root of the git work tree containing <paramref name="path"/> (file or folder), or null.</summary>
    public async Task<string?> FindRepositoryRootAsync(string path, CancellationToken ct = default)
    {
        var directory = Directory.Exists(path) ? path : Path.GetDirectoryName(path);
        if (string.IsNullOrEmpty(directory) || !Directory.Exists(directory))
        {
            return null;
        }

        return await new GitClient(_runner, directory).GetTopLevelAsync(ct).ConfigureAwait(false);
    }

    /// <summary>Branches a Finish of <paramref name="branch"/> merges into, in order (for confirmation screens).</summary>
    public Task<IReadOnlyList<string>> GetMergeTargetsAsync(string repositoryPath, GitFlowConfig config, FlowBranch branch, CancellationToken ct = default) =>
        GetMergeTargetsAsync(new GitClient(_runner, repositoryPath), config, branch, ct);

    /// <summary>State of the repository, or null when the folder is not a git work tree.</summary>
    public async Task<RepoState?> GetStateAsync(string repositoryPath, CancellationToken ct = default)
    {
        var git = new GitClient(_runner, repositoryPath);
        if (!await git.IsInsideWorkTreeAsync(ct).ConfigureAwait(false))
        {
            return null;
        }

        var config = await ConfigStore.ReadAsync(repositoryPath, ConfigScope.Local, ct).ConfigureAwait(false);
        var current = await git.GetCurrentBranchAsync(ct).ConfigureAwait(false);
        var flowBranches = config is null ? [] : await GetFlowBranchesAsync(git, config, ct).ConfigureAwait(false);
        var mergeHead = await git.GetMergeHeadAsync(ct).ConfigureAwait(false);

        var merged = new List<string>();
        var withoutCommit = new List<string>();
        if (config is not null)
        {
            foreach (var branch in flowBranches)
            {
                // Feature/hotfix just started: same commit as its base branch. Not "already merged", nothing to merge yet.
                // (A release without commit of its own is a normal release: it brings develop into master.)
                var tip = branch.Type == BranchType.Release ? null : await git.ResolveCommitAsync("refs/heads/" + branch.FullName, ct).ConfigureAwait(false);
                var baseTip = tip is null ? null : await git.ResolveCommitAsync("refs/heads/" + config.GetBaseBranch(branch.Type), ct).ConfigureAwait(false);
                if (tip is not null && tip == baseTip)
                {
                    withoutCommit.Add(branch.FullName);
                    continue;
                }

                var targets = await GetMergeTargetsAsync(git, config, branch, ct).ConfigureAwait(false);
                var mergedEverywhere = true;
                foreach (var target in targets)
                {
                    if (!await git.LocalBranchExistsAsync(target, ct).ConfigureAwait(false)
                        || !await git.IsAncestorAsync("refs/heads/" + branch.FullName, "refs/heads/" + target, ct).ConfigureAwait(false))
                    {
                        mergedEverywhere = false;
                        break;
                    }
                }

                if (mergedEverywhere)
                {
                    merged.Add(branch.FullName);
                }
            }
        }

        int? ahead = null, behind = null;
        if (current is not null)
        {
            var upstream = await git.GetUpstreamAsync(current, ct).ConfigureAwait(false);
            if (upstream is not null && await git.ResolveCommitAsync(upstream, ct).ConfigureAwait(false) is not null)
            {
                (ahead, behind) = await git.GetAheadBehindAsync("refs/heads/" + current, upstream, ct).ConfigureAwait(false);
            }
        }

        return new RepoState
        {
            RepositoryPath = repositoryPath,
            Config = config,
            HasCommits = await git.HasCommitsAsync(ct).ConfigureAwait(false),
            CurrentBranch = current,
            CurrentFlowBranch = config?.TryParseFlowBranch(current),
            IsDirty = await git.IsWorkingTreeDirtyAsync(ct).ConfigureAwait(false),
            HasRemote = await git.HasRemoteAsync(RemoteName, ct).ConfigureAwait(false),
            IsMergeInProgress = mergeHead is not null,
            PendingFinish = mergeHead is null || config is null ? null : await FindFlowBranchAtAsync(git, config, mergeHead, ct).ConfigureAwait(false),
            Ahead = ahead,
            Behind = behind,
            FlowBranches = flowBranches,
            MergedFlowBranches = merged,
            FlowBranchesWithoutCommit = withoutCommit,
        };
    }

    /// <summary>Creates develop from master if needed and writes the local gitflow.* configuration.</summary>
    public Task<FlowResult> InitAsync(string repositoryPath, GitFlowConfig config, CancellationToken ct = default) => GuardAsync(async () =>
    {
        var git = new GitClient(_runner, repositoryPath);
        if (!await git.IsInsideWorkTreeAsync(ct).ConfigureAwait(false))
        {
            return FlowResult.Fail(FlowError.NotAGitRepository, repositoryPath);
        }

        if (!await git.HasCommitsAsync(ct).ConfigureAwait(false))
        {
            return FlowResult.Fail(FlowError.NoCommits);
        }

        var errors = await ConfigStore.ValidateAsync(repositoryPath, config, ct).ConfigureAwait(false);
        if (errors.Count > 0)
        {
            return FlowResult.InvalidConfig(errors);
        }

        var hasRemote = await git.HasRemoteAsync(RemoteName, ct).ConfigureAwait(false);
        if (!await git.LocalBranchExistsAsync(config.MasterBranch, ct).ConfigureAwait(false))
        {
            if (!hasRemote || !await git.RemoteBranchExistsAsync(RemoteName, config.MasterBranch, ct).ConfigureAwait(false))
            {
                return FlowResult.Fail(FlowError.BaseBranchNotFound, config.MasterBranch);
            }

            await git.RunCheckedAsync(ct, "branch", "--track", config.MasterBranch, $"refs/remotes/{RemoteName}/{config.MasterBranch}").ConfigureAwait(false);
        }

        await ConfigStore.WriteAsync(repositoryPath, config, ConfigScope.Local, ct).ConfigureAwait(false);

        if (!await git.LocalBranchExistsAsync(config.DevelopBranch, ct).ConfigureAwait(false))
        {
            if (hasRemote && await git.RemoteBranchExistsAsync(RemoteName, config.DevelopBranch, ct).ConfigureAwait(false))
            {
                await git.RunCheckedAsync(ct, "branch", "--track", config.DevelopBranch, $"refs/remotes/{RemoteName}/{config.DevelopBranch}").ConfigureAwait(false);
            }
            else
            {
                await git.RunCheckedAsync(ct, "branch", config.DevelopBranch, "refs/heads/" + config.MasterBranch).ConfigureAwait(false);
            }
        }

        return FlowResult.Ok(config.DevelopBranch);
    });

    /// <summary>
    /// Suggested name for a new release (next minor of the latest version tag on master)
    /// or hotfix (next patch). Starts from 0.0.0 when master has no version tag.
    /// </summary>
    public async Task<string> SuggestVersionAsync(string repositoryPath, BranchType type, CancellationToken ct = default)
    {
        if (type == BranchType.Feature)
        {
            throw new ArgumentOutOfRangeException(nameof(type), "Features are not versioned.");
        }

        var config = await ConfigStore.ReadAsync(repositoryPath, ConfigScope.Local, ct).ConfigureAwait(false) ?? GitFlowConfig.Default;
        var git = new GitClient(_runner, repositoryPath);
        var latest = new SemanticVersion(0, 0, 0);

        if (await git.LocalBranchExistsAsync(config.MasterBranch, ct).ConfigureAwait(false))
        {
            var prefix = config.VersionTagPrefix;
            var tags = await git.GetTagsMergedIntoAsync("refs/heads/" + config.MasterBranch, prefix + "*", ct).ConfigureAwait(false);
            foreach (var tag in tags)
            {
                if (tag.StartsWith(prefix, StringComparison.Ordinal)
                    && SemanticVersion.TryParse(tag[prefix.Length..], out var version)
                    && version.CompareTo(latest) > 0)
                {
                    latest = version;
                }
            }
        }

        return (type == BranchType.Hotfix ? latest.NextPatch() : latest.NextMinor()).ToString();
    }

    /// <summary>
    /// Number of commits of develop that are not on master yet, merge commits excluded
    /// (the merge of the previous release back into develop does not count).
    /// 0 means a new release would be identical to the last one. Null when not initialised or a branch is missing.
    /// </summary>
    public async Task<int?> CountUnreleasedCommitsAsync(string repositoryPath, CancellationToken ct = default)
    {
        var config = await ConfigStore.ReadAsync(repositoryPath, ConfigScope.Local, ct).ConfigureAwait(false);
        var git = new GitClient(_runner, repositoryPath);
        if (config is null
            || !await git.LocalBranchExistsAsync(config.MasterBranch, ct).ConfigureAwait(false)
            || !await git.LocalBranchExistsAsync(config.DevelopBranch, ct).ConfigureAwait(false))
        {
            return null;
        }

        var result = await git.RunCheckedAsync(
            ct,
            "rev-list",
            "--count",
            "--no-merges",
            $"refs/heads/{config.MasterBranch}..refs/heads/{config.DevelopBranch}").ConfigureAwait(false);
        return int.Parse(result.StandardOutput.Trim(), System.Globalization.CultureInfo.InvariantCulture);
    }

    public Task<FlowResult> StartAsync(string repositoryPath, BranchType type, string name, StartOptions? options = null, CancellationToken ct = default) => GuardAsync(async () =>
    {
        options ??= new StartOptions();
        var git = new GitClient(_runner, repositoryPath);
        var (config, error) = await LoadInitializedConfigAsync(git, ct).ConfigureAwait(false);
        if (config is null)
        {
            return error!;
        }

        if (await git.GetMergeHeadAsync(ct).ConfigureAwait(false) is not null)
        {
            return FlowResult.Fail(FlowError.MergeInProgress);
        }

        if (await git.IsWorkingTreeDirtyAsync(ct).ConfigureAwait(false))
        {
            return FlowResult.Fail(FlowError.DirtyWorkingTree);
        }

        var shortName = name?.Trim() ?? string.Empty;
        var fullName = config.GetPrefix(type) + shortName;
        if (shortName.Length == 0 || !await git.IsValidBranchNameAsync(fullName, ct).ConfigureAwait(false))
        {
            return FlowResult.Fail(FlowError.InvalidBranchName, fullName);
        }

        var baseBranch = config.GetBaseBranch(type);
        if (!await git.LocalBranchExistsAsync(baseBranch, ct).ConfigureAwait(false))
        {
            return FlowResult.Fail(FlowError.BaseBranchNotFound, baseBranch);
        }

        var hasRemote = await git.HasRemoteAsync(RemoteName, ct).ConfigureAwait(false);
        var fetch = options.FetchFromRemote && hasRemote;
        if (fetch)
        {
            await git.RunCheckedAsync(ct, "fetch", RemoteName).ConfigureAwait(false);
        }

        if (await git.LocalBranchExistsAsync(fullName, ct).ConfigureAwait(false)
            || (hasRemote && await git.RemoteBranchExistsAsync(RemoteName, fullName, ct).ConfigureAwait(false)))
        {
            return FlowResult.Fail(FlowError.BranchAlreadyExists, fullName);
        }

        if (type != BranchType.Feature)
        {
            var inProgress = (await GetFlowBranchesAsync(git, config, ct).ConfigureAwait(false)).FirstOrDefault(b => b.Type == type);
            if (inProgress is not null)
            {
                return FlowResult.Fail(
                    type == BranchType.Release ? FlowError.ReleaseAlreadyInProgress : FlowError.HotfixAlreadyInProgress,
                    inProgress.FullName);
            }

            var tag = config.GetTagName(shortName);
            if (await git.TagExistsAsync(tag, ct).ConfigureAwait(false))
            {
                return FlowResult.Fail(FlowError.TagAlreadyExists, tag);
            }
        }

        if (fetch)
        {
            var syncError = await SyncWithRemoteAsync(git, baseBranch, ct).ConfigureAwait(false);
            if (syncError is not null)
            {
                return syncError;
            }
        }

        await git.RunCheckedAsync(ct, "switch", "-c", fullName, baseBranch).ConfigureAwait(false);
        return FlowResult.Ok(fullName);
    });

    /// <summary>
    /// Finishes a work branch. When a previous Finish stopped on a conflict that is now resolved
    /// (files staged), the merge is concluded and the remaining steps are run.
    /// </summary>
    public Task<FlowResult> FinishAsync(string repositoryPath, FinishOptions? options = null, CancellationToken ct = default) => GuardAsync(async () =>
    {
        options ??= new FinishOptions();
        var git = new GitClient(_runner, repositoryPath);
        var (config, error) = await LoadInitializedConfigAsync(git, ct).ConfigureAwait(false);
        if (config is null)
        {
            return error!;
        }

        var mergeHead = await git.GetMergeHeadAsync(ct).ConfigureAwait(false);
        FlowBranch? branch;
        if (options.BranchName is { } requested)
        {
            branch = config.TryParseFlowBranch(requested);
            if (branch is null)
            {
                return FlowResult.Fail(FlowError.NotAFlowBranch, requested);
            }
        }
        else if (mergeHead is not null)
        {
            branch = await FindFlowBranchAtAsync(git, config, mergeHead, ct).ConfigureAwait(false);
            if (branch is null)
            {
                return FlowResult.Fail(FlowError.MergeInProgress);
            }
        }
        else
        {
            var current = await git.GetCurrentBranchAsync(ct).ConfigureAwait(false);
            branch = config.TryParseFlowBranch(current);
            if (branch is null)
            {
                return FlowResult.Fail(FlowError.NotAFlowBranch, current ?? "HEAD");
            }
        }

        if (!await git.LocalBranchExistsAsync(branch.FullName, ct).ConfigureAwait(false))
        {
            return FlowResult.Fail(FlowError.BranchNotFound, branch.FullName);
        }

        var targets = await GetMergeTargetsAsync(git, config, branch, ct).ConfigureAwait(false);
        foreach (var target in targets)
        {
            if (!await git.LocalBranchExistsAsync(target, ct).ConfigureAwait(false))
            {
                return FlowResult.Fail(FlowError.BaseBranchNotFound, target);
            }
        }

        if (mergeHead is not null)
        {
            var resumeResult = await ConcludeStoppedMergeAsync(git, branch, targets, mergeHead, ct).ConfigureAwait(false);
            if (resumeResult is not null)
            {
                return resumeResult;
            }
        }

        if (await git.IsWorkingTreeDirtyAsync(ct).ConfigureAwait(false))
        {
            return FlowResult.Fail(FlowError.DirtyWorkingTree);
        }

        var pullRequestMode = options.PullRequests is not null;
        if (pullRequestMode && _pullRequestStrategy is null)
        {
            return FlowResult.Fail(FlowError.PullRequestNotSupported);
        }

        if ((options.PushAfterFinish || options.DeleteRemoteBranch || options.FetchFromRemote || pullRequestMode)
            && !await git.HasRemoteAsync(RemoteName, ct).ConfigureAwait(false))
        {
            return FlowResult.Fail(FlowError.NoRemote);
        }

        string? tagName = null, tagMessage = null;
        if (branch.Type != BranchType.Feature)
        {
            tagName = config.GetTagName(branch.Name);
            var alreadyOnMaster = await git.IsAncestorAsync("refs/heads/" + branch.FullName, "refs/heads/" + config.MasterBranch, ct).ConfigureAwait(false);
            if (!alreadyOnMaster && await git.TagExistsAsync(tagName, ct).ConfigureAwait(false))
            {
                return FlowResult.Fail(FlowError.TagAlreadyExists, tagName);
            }

            tagMessage = string.IsNullOrWhiteSpace(options.TagMessage) ? GetDefaultTagMessage(branch.Type, branch.Name) : options.TagMessage;
        }

        if (options.FetchFromRemote)
        {
            await git.RunCheckedAsync(ct, "fetch", RemoteName).ConfigureAwait(false);
            foreach (var target in targets)
            {
                var syncError = await SyncWithRemoteAsync(git, target, ct).ConfigureAwait(false);
                if (syncError is not null)
                {
                    return syncError;
                }
            }
        }

        var plan = new FinishPlan(repositoryPath, branch, config, targets, tagName, tagMessage, options.PushAfterFinish, options.DeleteRemoteBranch, options.PullRequests);
        return pullRequestMode
            ? await _pullRequestStrategy!.ExecuteAsync(plan, ct).ConfigureAwait(false)
            : await _finishStrategy.ExecuteAsync(plan, ct).ConfigureAwait(false);
    });

    /// <summary><c>git push -u origin &lt;branch&gt;</c> for a work branch (current branch by default).</summary>
    public Task<FlowResult> PublishAsync(string repositoryPath, string? branchName = null, CancellationToken ct = default) => GuardAsync(async () =>
    {
        var git = new GitClient(_runner, repositoryPath);
        var (config, error) = await LoadInitializedConfigAsync(git, ct).ConfigureAwait(false);
        if (config is null)
        {
            return error!;
        }

        branchName ??= await git.GetCurrentBranchAsync(ct).ConfigureAwait(false);
        var branch = config.TryParseFlowBranch(branchName);
        if (branch is null)
        {
            return FlowResult.Fail(FlowError.NotAFlowBranch, branchName ?? "HEAD");
        }

        if (!await git.LocalBranchExistsAsync(branch.FullName, ct).ConfigureAwait(false))
        {
            return FlowResult.Fail(FlowError.BranchNotFound, branch.FullName);
        }

        if (!await git.HasRemoteAsync(RemoteName, ct).ConfigureAwait(false))
        {
            return FlowResult.Fail(FlowError.NoRemote);
        }

        var push = await git.RunAsync(ct, "push", "-u", RemoteName, branch.FullName).ConfigureAwait(false);
        return push.Success
            ? FlowResult.Ok(branch.FullName)
            : FlowResult.Fail(FlowError.GitCommandFailed, push.StandardError.Trim(), push);
    });

    private async Task<(GitFlowConfig? Config, FlowResult? Error)> LoadInitializedConfigAsync(GitClient git, CancellationToken ct)
    {
        if (!await git.IsInsideWorkTreeAsync(ct).ConfigureAwait(false))
        {
            return (null, FlowResult.Fail(FlowError.NotAGitRepository, git.RepositoryPath));
        }

        var config = await ConfigStore.ReadAsync(git.RepositoryPath, ConfigScope.Local, ct).ConfigureAwait(false);
        return config is null ? (null, FlowResult.Fail(FlowError.NotInitialized)) : (config, null);
    }

    /// <summary>
    /// Resume after a conflict: the stopped merge must be ours (MERGE_HEAD = tip of the branch,
    /// current branch = one of its targets) and fully resolved. Returns null when the merge was concluded.
    /// </summary>
    private static async Task<FlowResult?> ConcludeStoppedMergeAsync(
        GitClient git, FlowBranch branch, IReadOnlyList<string> targets, string mergeHead, CancellationToken ct)
    {
        var tip = await git.ResolveCommitAsync("refs/heads/" + branch.FullName, ct).ConfigureAwait(false);
        var current = await git.GetCurrentBranchAsync(ct).ConfigureAwait(false);
        if (tip != mergeHead || current is null || !targets.Contains(current, StringComparer.Ordinal))
        {
            return FlowResult.Fail(FlowError.MergeInProgress);
        }

        var conflicts = await git.GetConflictedFilesAsync(ct).ConfigureAwait(false);
        if (conflicts.Count > 0)
        {
            return FlowResult.Conflicted(new MergeConflict(branch.FullName, current, conflicts));
        }

        await git.RunCheckedAsync(ct, "commit", "--no-edit").ConfigureAwait(false);
        return null;
    }

    /// <summary>feature → develop; release → master, develop; hotfix → master, then the release in progress or develop.</summary>
    private static async Task<IReadOnlyList<string>> GetMergeTargetsAsync(GitClient git, GitFlowConfig config, FlowBranch branch, CancellationToken ct)
    {
        switch (branch.Type)
        {
            case BranchType.Feature:
                return [config.DevelopBranch];
            case BranchType.Release:
                return [config.MasterBranch, config.DevelopBranch];
            default:
                var release = (await GetFlowBranchesAsync(git, config, ct).ConfigureAwait(false)).FirstOrDefault(b => b.Type == BranchType.Release);
                return [config.MasterBranch, release?.FullName ?? config.DevelopBranch];
        }
    }

    private static async Task<IReadOnlyList<FlowBranch>> GetFlowBranchesAsync(GitClient git, GitFlowConfig config, CancellationToken ct)
    {
        var branches = await git.GetLocalBranchesAsync(ct).ConfigureAwait(false);
        return branches.Select(config.TryParseFlowBranch)
                       .OfType<FlowBranch>()
                       .OrderBy(b => b.Type)
                       .ThenBy(b => b.Name, StringComparer.Ordinal)
                       .ToArray();
    }

    private static async Task<FlowBranch?> FindFlowBranchAtAsync(GitClient git, GitFlowConfig config, string commit, CancellationToken ct)
    {
        var branches = await git.GetLocalBranchesPointingAtAsync(commit, ct).ConfigureAwait(false);
        return branches.Select(config.TryParseFlowBranch).OfType<FlowBranch>().FirstOrDefault();
    }

    /// <summary>After a fetch: fast-forward a local branch that is behind origin, refuse when it has diverged.</summary>
    private static async Task<FlowResult?> SyncWithRemoteAsync(GitClient git, string branch, CancellationToken ct)
    {
        var remoteRef = $"refs/remotes/{RemoteName}/{branch}";
        var localRef = "refs/heads/" + branch;
        if (await git.ResolveCommitAsync(remoteRef, ct).ConfigureAwait(false) is null
            || await git.IsAncestorAsync(remoteRef, localRef, ct).ConfigureAwait(false))
        {
            return null; // nothing on origin, or local already contains it
        }

        if (!await git.IsAncestorAsync(localRef, remoteRef, ct).ConfigureAwait(false))
        {
            return FlowResult.Fail(FlowError.BranchDivergedFromRemote, branch);
        }

        await git.RunCheckedAsync(ct, "switch", branch).ConfigureAwait(false);
        await git.RunCheckedAsync(ct, "merge", "--ff-only", remoteRef).ConfigureAwait(false);
        return null;
    }

    private static async Task<FlowResult> GuardAsync(Func<Task<FlowResult>> operation)
    {
        try
        {
            return await operation().ConfigureAwait(false);
        }
        catch (GitCommandException ex)
        {
            return FlowResult.Fail(FlowError.GitCommandFailed, ex.Result.StandardError.Trim(), ex.Result);
        }
    }
}
