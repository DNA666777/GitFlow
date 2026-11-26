using GitFlow.Core.Config;
using GitFlow.Core.Model;
using GitFlow.Core.PullRequests;
using GitFlow.Core.Resources;

namespace GitFlow.Core.Flow;

/// <summary>Why the Pull Request mode cannot be chosen for this Finish.</summary>
public enum PullRequestUnavailableReason
{
    None,

    /// <summary>A local merge is in progress or already done for this branch.</summary>
    LocalMergeStarted,

    /// <summary>origin is not recognised as GitHub or Azure DevOps (and not declared as an Azure DevOps server).</summary>
    PlatformNotSupported,
}

/// <summary>Everything the Finish dialog needs, whatever the editor (Visual Studio or VS Code).</summary>
/// <param name="Targets">Branches to merge into, in order (production first for a release/hotfix).</param>
/// <param name="TagName">Tag of a release/hotfix, null for a feature.</param>
/// <param name="IsResume">The Finish of this branch stopped on a conflict and resumes.</param>
/// <param name="IsAlreadyMerged">Already merged into all its targets by hand: the Finish only tags and deletes.</param>
/// <param name="IsWithoutCommit">No commit of its own yet (just started): there is nothing to merge.</param>
/// <param name="AsksTagMessage">A tag message is asked (release/hotfix not resumed and not already merged).</param>
/// <param name="PullRequestUnavailableMessage">Translated reason, empty when the Pull Request mode is available.</param>
public sealed record FinishContext(
    FlowBranch Branch,
    IReadOnlyList<string> Targets,
    string? TagName,
    string DefaultTagMessage,
    bool IsResume,
    bool IsAlreadyMerged,
    bool IsWithoutCommit,
    bool AsksTagMessage,
    bool HasRemote,
    FinishDefaults Defaults,
    RemoteRepository? Remote,
    PullRequestUnavailableReason PullRequestUnavailable,
    string PullRequestUnavailableMessage)
{
    public bool PullRequestAvailable => PullRequestUnavailable == PullRequestUnavailableReason.None;
}

/// <summary>Prepares a Finish: which branch, its targets and tag, the default options and whether a PR is possible.</summary>
public sealed class FinishPlanner(FlowService flow, GitFlowSettingsService settings, PullRequestService pullRequests)
{
    /// <param name="branchName">Branch to finish; null: the branch of a stopped merge, otherwise the current branch.</param>
    /// <returns>
    /// The context, or a failed result (not initialised, not a work branch, uncommitted changes): refused before any
    /// question is asked, rather than after the options of the Finish.
    /// </returns>
    public async Task<(FinishContext? Context, FlowResult? Error)> PrepareAsync(string repositoryPath, string? branchName, CancellationToken ct = default)
    {
        var state = await flow.GetStateAsync(repositoryPath, ct).ConfigureAwait(false);
        if (state?.Config is not { } config)
        {
            return (null, FlowResult.Fail(FlowError.NotInitialized));
        }

        var branch = branchName is not null ? config.TryParseFlowBranch(branchName) : state.PendingFinish ?? state.CurrentFlowBranch;
        if (branch is null)
        {
            return (null, FlowResult.Fail(FlowError.NotAFlowBranch, branchName ?? state.CurrentBranch ?? "HEAD"));
        }

        var resume = state.PendingFinish?.FullName == branch.FullName;
        if (state.IsDirty && !resume)
        {
            return (null, FlowResult.Fail(FlowError.DirtyWorkingTree));
        }

        var alreadyMerged = state.MergedFlowBranches.Contains(branch.FullName);
        var targets = await flow.GetMergeTargetsAsync(repositoryPath, config, branch, ct).ConfigureAwait(false);
        var defaults = await settings.ReadFinishDefaultsAsync(repositoryPath, ConfigScope.Local, ct).ConfigureAwait(false);
        var remote = await pullRequests.GetRemoteAsync(repositoryPath, ct).ConfigureAwait(false);

        var unavailable = resume || alreadyMerged
            ? PullRequestUnavailableReason.LocalMergeStarted
            : remote is null ? PullRequestUnavailableReason.PlatformNotSupported : PullRequestUnavailableReason.None;
        var unavailableMessage = unavailable switch
        {
            PullRequestUnavailableReason.LocalMergeStarted => FlowMessages.Get("Info_PullRequestNotNow"),
            PullRequestUnavailableReason.PlatformNotSupported => FlowMessages.Describe(FlowResult.Fail(FlowError.PullRequestNotSupported)),
            _ => string.Empty,
        };

        var versioned = branch.Type != BranchType.Feature;
        return (new FinishContext(
            branch,
            targets,
            versioned ? config.GetTagName(branch.Name) : null,
            FlowService.GetDefaultTagMessage(branch.Type, branch.Name),
            resume,
            alreadyMerged,
            state.FlowBranchesWithoutCommit.Contains(branch.FullName),
            AsksTagMessage: versioned && !resume && !alreadyMerged,
            state.HasRemote,
            defaults with
            {
                PushAfterFinish = state.HasRemote && defaults.PushAfterFinish,
                DeleteRemoteBranch = state.HasRemote && defaults.DeleteRemoteBranch,
            },
            remote,
            unavailable,
            unavailableMessage), null);
    }
}
