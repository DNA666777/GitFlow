using GitFlow.Core.Config;

namespace GitFlow.Core.Model;

/// <summary>Snapshot of the repository as seen by the tool window.</summary>
public sealed record RepoState
{
    public required string RepositoryPath { get; init; }

    /// <summary>Local gitflow.* configuration, null when the repository is not initialised.</summary>
    public GitFlowConfig? Config { get; init; }

    public bool IsInitialized => Config is not null;

    public bool HasCommits { get; init; }

    /// <summary>Current branch, null when HEAD is detached.</summary>
    public string? CurrentBranch { get; init; }

    public FlowBranch? CurrentFlowBranch { get; init; }

    public bool IsDirty { get; init; }

    public bool HasRemote { get; init; }

    public bool IsMergeInProgress { get; init; }

    /// <summary>Flow branch whose Finish stopped on a conflict and can be resumed.</summary>
    public FlowBranch? PendingFinish { get; init; }

    /// <summary>Commits ahead of / behind the upstream of the current branch (null without upstream).</summary>
    public int? Ahead { get; init; }

    public int? Behind { get; init; }

    public IReadOnlyList<FlowBranch> FlowBranches { get; init; } = [];

    /// <summary>
    /// Full names of the work branches already merged into all their Finish targets
    /// (e.g. merge concluded by hand): a Finish will only tag/delete them.
    /// </summary>
    public IReadOnlyCollection<string> MergedFlowBranches { get; init; } = [];

    /// <summary>
    /// Full names of the features/hotfixes that have no commit of their own yet (same commit as their base branch,
    /// e.g. just started): not reported as merged, a Finish would have nothing to merge.
    /// </summary>
    public IReadOnlyCollection<string> FlowBranchesWithoutCommit { get; init; } = [];

    /// <summary>On a release branch: show the reminder "fixes, docs and release tasks only".</summary>
    public bool ShowReleaseReminder => CurrentFlowBranch?.Type == BranchType.Release;
}
