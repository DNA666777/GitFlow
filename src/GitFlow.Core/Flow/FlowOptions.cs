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
}

/// <summary>Everything a finish strategy needs, validated by <see cref="FlowService"/>.</summary>
/// <param name="MergeTargets">Branches to merge into, in order (master first for release/hotfix).</param>
/// <param name="TagName">Tag to create on the first target (master), null for a feature.</param>
public sealed record FinishPlan(
    string RepositoryPath,
    FlowBranch Branch,
    GitFlowConfig Config,
    IReadOnlyList<string> MergeTargets,
    string? TagName,
    string? TagMessage,
    bool PushAfterFinish,
    bool DeleteRemoteBranch);

/// <summary>
/// How a branch is finished. Phase 1: <see cref="LocalMergeStrategy"/>.
/// Phase 2 will add a pull request strategy without touching <see cref="FlowService"/>.
/// </summary>
public interface IFinishStrategy
{
    Task<FlowResult> ExecuteAsync(FinishPlan plan, CancellationToken cancellationToken);
}
