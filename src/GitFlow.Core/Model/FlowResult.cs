using GitFlow.Core.Config;
using GitFlow.Core.Git;

namespace GitFlow.Core.Model;

public enum FlowStatus
{
    Success,
    Failed,
    Conflict,
}

/// <summary>Typed reason of a failed flow operation. Each value has a French message in Resources/Strings.resx (key "Error_{name}").</summary>
public enum FlowError
{
    None,
    NotAGitRepository,
    NotInitialized,
    NoCommits,
    InvalidConfig,
    DirtyWorkingTree,
    MergeInProgress,
    InvalidBranchName,
    BranchAlreadyExists,
    BranchNotFound,
    BaseBranchNotFound,
    NotAFlowBranch,
    TagAlreadyExists,
    ReleaseAlreadyInProgress,
    HotfixAlreadyInProgress,
    BranchDivergedFromRemote,
    NoRemote,
    MergeConflict,
    GitCommandFailed,
    NothingToCommit,
    EmptyCommitMessage,
    CommitOnMasterRefused,
    DetachedHead,
    PullRequestNotSupported,
    NoCredentials,
    PullRequestApiFailed,
    PullRequestNotMerged,
    MergeCommitNotFound,
    BranchNotMergedOnRemote,
    NoCommonHistory,
}

/// <summary>Non blocking problem after an otherwise successful operation (key "Warning_{name}").</summary>
public enum FlowWarningKind
{
    PushFailed,
    RemoteBranchKeptBecausePushFailed,
    RemoteBranchDeleteFailed,
    LocalBranchDeleteFailed,
}

public sealed record FlowWarning(FlowWarningKind Kind, string? Detail = null, GitResult? Command = null);

/// <summary>A merge stopped on conflicts. The repository is left in the merge state.</summary>
public sealed record MergeConflict(string SourceBranch, string TargetBranch, IReadOnlyList<string> Files);

/// <summary>Result of a flow operation. Expected business failures are returned, never thrown.</summary>
public sealed record FlowResult
{
    public FlowStatus Status { get; init; }

    public FlowError Error { get; init; }

    /// <summary>Context of the error (branch name, tag...), used to format the message.</summary>
    public string? Detail { get; init; }

    public GitResult? FailedCommand { get; init; }

    public MergeConflict? Conflict { get; init; }

    /// <summary>On success: the branch created (Start) or the branch checked out at the end (Finish).</summary>
    public string? BranchName { get; init; }

    public string? TagName { get; init; }

    public IReadOnlyList<ConfigValidationError> ConfigErrors { get; init; } = [];

    public IReadOnlyList<FlowWarning> Warnings { get; init; } = [];

    /// <summary>Pull requests created or reused (Pull Request mode).</summary>
    public IReadOnlyList<PullRequests.PullRequestInfo> PullRequests { get; init; } = [];

    public bool IsSuccess => Status == FlowStatus.Success;

    public static FlowResult Ok(string? branchName = null, string? tagName = null, IReadOnlyList<FlowWarning>? warnings = null) =>
        new() { Status = FlowStatus.Success, BranchName = branchName, TagName = tagName, Warnings = warnings ?? [] };

    public static FlowResult Fail(FlowError error, string? detail = null, GitResult? failedCommand = null) =>
        new() { Status = FlowStatus.Failed, Error = error, Detail = detail, FailedCommand = failedCommand };

    public static FlowResult Conflicted(MergeConflict conflict) =>
        new() { Status = FlowStatus.Conflict, Error = FlowError.MergeConflict, Conflict = conflict, Detail = conflict.TargetBranch };

    public static FlowResult InvalidConfig(IReadOnlyList<ConfigValidationError> errors) =>
        new() { Status = FlowStatus.Failed, Error = FlowError.InvalidConfig, ConfigErrors = errors };
}
