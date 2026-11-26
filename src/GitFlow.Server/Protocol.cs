using GitFlow.Core.Config;
using GitFlow.Core.Flow;
using GitFlow.Core.Model;
using GitFlow.Core.PullRequests;
using GitFlow.Core.Resources;

namespace GitFlow.Server;

// Messages exchanged with the VS Code extension (JSON, camelCase, enums as camelCase strings).
// Texts are already translated by the Core (FlowMessages) in the culture given to "initialize".

public sealed record ServerInfo(string Version, string Culture);

public sealed record BranchDto(BranchType Type, string Name, string FullName)
{
    public static BranchDto? From(FlowBranch? branch) => branch is null ? null : new(branch.Type, branch.Name, branch.FullName);
}

public sealed record NamingDto(string MasterBranch, string DevelopBranch, string FeaturePrefix, string ReleasePrefix, string HotfixPrefix, string VersionTagPrefix)
{
    public static NamingDto From(GitFlowConfig config) =>
        new(config.MasterBranch, config.DevelopBranch, config.FeaturePrefix, config.ReleasePrefix, config.HotfixPrefix, config.VersionTagPrefix);

    public GitFlowConfig ToConfig() =>
        new(MasterBranch.Trim(), DevelopBranch.Trim(), FeaturePrefix.Trim(), ReleasePrefix.Trim(), HotfixPrefix.Trim(), VersionTagPrefix.Trim());
}

public sealed record FinishDefaultsDto(bool PushAfterFinish, bool DeleteRemoteBranch, bool UsePullRequest, bool PruneOnComplete, bool AzureDevOpsServer)
{
    public static FinishDefaultsDto From(FinishDefaults d) => new(d.PushAfterFinish, d.DeleteRemoteBranch, d.UsePullRequest, d.PruneOnComplete, d.AzureDevOpsServer);

    public FinishDefaults ToDefaults() => new(PushAfterFinish, DeleteRemoteBranch, UsePullRequest, PruneOnComplete, AzureDevOpsServer);
}

public sealed record SettingsDto(NamingDto Naming, FinishDefaultsDto Finish)
{
    public static SettingsDto From(GitFlowSettings s) => new(NamingDto.From(s.Naming), FinishDefaultsDto.From(s.Finish));

    public GitFlowSettings ToSettings() => new(Naming.ToConfig(), Finish.ToDefaults());
}

public sealed record StateDto(
    string RepositoryPath,
    bool IsInitialized,
    NamingDto? Naming,
    bool HasCommits,
    string? CurrentBranch,
    BranchDto? CurrentFlowBranch,
    bool IsDirty,
    bool HasRemote,
    bool IsMergeInProgress,
    BranchDto? PendingFinish,
    int? Ahead,
    int? Behind,
    IReadOnlyList<BranchDto> FlowBranches,
    IReadOnlyList<string> MergedFlowBranches,
    string? ReleaseReminder)
{
    public static StateDto From(RepoState s) => new(
        s.RepositoryPath,
        s.IsInitialized,
        s.Config is null ? null : NamingDto.From(s.Config),
        s.HasCommits,
        s.CurrentBranch,
        BranchDto.From(s.CurrentFlowBranch),
        s.IsDirty,
        s.HasRemote,
        s.IsMergeInProgress,
        BranchDto.From(s.PendingFinish),
        s.Ahead,
        s.Behind,
        s.FlowBranches.Select(b => BranchDto.From(b)!).ToArray(),
        s.MergedFlowBranches.ToArray(),
        s.ShowReleaseReminder ? FlowMessages.ReleaseReminder : null);
}

public sealed record PullRequestDto(int Id, string Source, string Target, PullRequestState? State, string? WebUrl, string? Title, string? Error)
{
    public static PullRequestDto From(PullRequestInfo pr) => new(pr.Id, pr.Source, pr.Target, pr.State, pr.WebUrl.AbsoluteUri, pr.Title, null);

    public static PullRequestDto From(TrackedPullRequest pr) =>
        new(pr.Id, pr.Source, pr.Target, pr.Info?.State, pr.Info?.WebUrl.AbsoluteUri, pr.Info?.Title, pr.Error);
}

public sealed record PullRequestGroupDto(string Source, string? Version, IReadOnlyList<PullRequestDto> PullRequests, bool CanTag, bool CanComplete)
{
    public static PullRequestGroupDto From(PullRequestGroup g) =>
        new(g.Source, g.Version, g.PullRequests.Select(PullRequestDto.From).ToArray(), g.CanTag, g.CanComplete);
}

/// <summary>Outcome of an operation. <see cref="Message"/> is the translated error (empty on success).</summary>
public sealed record ResultDto(
    bool Success,
    FlowStatus Status,
    FlowError Error,
    string Message,
    string? Detail,
    string? BranchName,
    string? TagName,
    IReadOnlyList<string> Warnings,
    IReadOnlyList<string> ConflictedFiles,
    IReadOnlyList<PullRequestDto> PullRequests)
{
    public static ResultDto From(FlowResult r) => new(
        r.IsSuccess,
        r.Status,
        r.Error,
        r.IsSuccess ? string.Empty : FlowMessages.Describe(r),
        r.Detail,
        r.BranchName,
        r.TagName,
        r.Warnings.Select(FlowMessages.Describe).ToArray(),
        r.Conflict?.Files ?? [],
        r.PullRequests.Select(PullRequestDto.From).ToArray());
}

public sealed record RemoteDto(HostingPlatform Platform, string WebUrl, bool OnPremises);

public sealed record FinishContextDto(
    BranchDto Branch,
    IReadOnlyList<string> Targets,
    string? TagName,
    string DefaultTagMessage,
    bool IsResume,
    bool IsAlreadyMerged,
    bool AsksTagMessage,
    bool HasRemote,
    FinishDefaultsDto Defaults,
    RemoteDto? Remote,
    bool PullRequestAvailable,
    string PullRequestUnavailableMessage)
{
    public static FinishContextDto From(FinishContext c) => new(
        BranchDto.From(c.Branch)!,
        c.Targets,
        c.TagName,
        c.DefaultTagMessage,
        c.IsResume,
        c.IsAlreadyMerged,
        c.AsksTagMessage,
        c.HasRemote,
        FinishDefaultsDto.From(c.Defaults),
        c.Remote is null ? null : new RemoteDto(c.Remote.Platform, c.Remote.WebUrl.AbsoluteUri, c.Remote.OnPremises),
        c.PullRequestAvailable,
        c.PullRequestUnavailableMessage);
}

/// <summary>Either the context of the Finish, or why it cannot start.</summary>
public sealed record PrepareFinishResponse(FinishContextDto? Context, ResultDto? Error);

public sealed record PullRequestDraftDto(string Source, string Target, string Title, string Description)
{
    public static PullRequestDraftDto From(PullRequestDraft d) => new(d.Source, d.Target, d.Title, d.Description);

    public PullRequestDraft ToDraft() => new(Source, Target, Title, Description);
}

/// <summary>Generated PR messages, or why the PRs cannot be opened (e.g. unrelated histories), checked before anything is pushed.</summary>
public sealed record PreparePullRequestsResponse(IReadOnlyList<PullRequestDraftDto> Drafts, ResultDto? Error);

/// <summary>A line of the git log (command, exit code, stderr), sent as the "log" notification.</summary>
public sealed record LogNotification(string Line);
