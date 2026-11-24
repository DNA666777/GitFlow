using GitFlow.Core.Config;
using GitFlow.Core.Model;

namespace GitFlow.Core.Flow;

public sealed record StartOptions
{
    /// <summary>Run <c>git fetch origin</c> first and fast-forward the base branch if it is behind.</summary>
    public bool FetchFromRemote { get; init; }
}

public sealed record FinishOptions
{
    /// <summary>Branch to finish. When null: the branch of a merge stopped on conflicts, otherwise the current branch.</summary>
    public string? BranchName { get; init; }

    /// <summary>Annotated tag message (release/hotfix). Defaults to "Release {version}" / "Hotfix {version}".</summary>
    public string? TagMessage { get; init; }

    public bool DeleteRemoteBranch { get; init; }

    /// <summary>Push the target branches (and the tag) to origin at the end.</summary>
    public bool PushAfterFinish { get; init; }

    /// <summary>Fetch origin first and fast-forward the target branches if they are behind.</summary>
    public bool FetchFromRemote { get; init; }

    /// <summary>
    /// Pull Request mode (optional, phase 2): one reviewed draft per merge target. When set, nothing is merged locally:
    /// the branch is pushed and the pull requests are created on the hosting platform.
    /// </summary>
    public IReadOnlyList<PullRequests.PullRequestDraft>? PullRequests { get; init; }
}

/// <summary>Everything a finish strategy needs, validated by <see cref="FlowService"/>.</summary>
/// <param name="MergeTargets">Branches to merge into, in order (master first for release/hotfix).</param>
/// <param name="TagName">Tag to create on the first target (master), null for a feature.</param>
/// <param name="PullRequests">Drafts in Pull Request mode, null for a local merge.</param>
public sealed record FinishPlan(
    string RepositoryPath,
    FlowBranch Branch,
    GitFlowConfig Config,
    IReadOnlyList<string> MergeTargets,
    string? TagName,
    string? TagMessage,
    bool PushAfterFinish,
    bool DeleteRemoteBranch,
    IReadOnlyList<PullRequests.PullRequestDraft>? PullRequests = null);

/// <summary>
/// How a branch is finished: <see cref="LocalMergeStrategy"/> (default) or
/// <see cref="PullRequests.PullRequestStrategy"/> (optional Pull Request mode).
/// </summary>
public interface IFinishStrategy
{
    Task<FlowResult> ExecuteAsync(FinishPlan plan, CancellationToken cancellationToken);
}
