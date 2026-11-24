using GitFlow.Core.Config;
using GitFlow.Core.Git;
using GitFlow.Core.Model;

namespace GitFlow.Core.Flow;

/// <summary>
/// Commit from the GitFlow window: preview with a generated message, then a guarded commit.
/// Staged files only when something is staged, otherwise every change (git add -A).
/// Refused on master (never commit directly on production) and while a merge is in progress
/// (that merge belongs to a Finish to resume).
/// </summary>
public sealed class CommitService(IGitRunner runner)
{
    private readonly GitFlowConfigStore _configStore = new(runner);

    /// <summary>What a commit would contain now, or null outside a git work tree.</summary>
    public async Task<CommitPreview?> GetPreviewAsync(string repositoryPath, CancellationToken ct = default)
    {
        var git = new GitClient(runner, repositoryPath);
        if (!await git.IsInsideWorkTreeAsync(ct).ConfigureAwait(false))
        {
            return null;
        }

        var changes = await git.GetChangesAsync(ct).ConfigureAwait(false);
        var branch = await git.GetCurrentBranchAsync(ct).ConfigureAwait(false);
        var config = await _configStore.ReadAsync(repositoryPath, ConfigScope.Local, ct).ConfigureAwait(false);

        var blockedBy = FlowError.None;
        string? detail = null;
        if (await git.GetMergeHeadAsync(ct).ConfigureAwait(false) is not null || changes.Any(c => c.Kind == ChangeKind.Conflicted))
        {
            blockedBy = FlowError.MergeInProgress;
        }
        else if (branch is null)
        {
            blockedBy = FlowError.DetachedHead;
        }
        else if (config is not null && branch == config.MasterBranch)
        {
            (blockedBy, detail) = (FlowError.CommitOnMasterRefused, branch);
        }

        var mode = changes.Any(c => c.IsStaged) ? CommitMode.StagedOnly : CommitMode.AllChanges;
        var selected = mode == CommitMode.StagedOnly ? changes.Where(c => c.IsStaged).ToArray() : changes;
        var stats = selected.Count == 0
            ? new Dictionary<string, FileStat>()
            : await git.GetDiffStatsAsync(staged: mode == CommitMode.StagedOnly, ct).ConfigureAwait(false);

        return new CommitPreview
        {
            Mode = mode,
            Changes = selected,
            SuggestedMessage = CommitMessageGenerator.Generate(selected, stats, branch),
            BlockedBy = blockedBy,
            BlockedDetail = detail,
        };
    }

    public async Task<FlowResult> CommitAsync(string repositoryPath, string message, CancellationToken ct = default)
    {
        try
        {
            var preview = await GetPreviewAsync(repositoryPath, ct).ConfigureAwait(false);
            if (preview is null)
            {
                return FlowResult.Fail(FlowError.NotAGitRepository, repositoryPath);
            }

            if (preview.BlockedBy != FlowError.None)
            {
                return FlowResult.Fail(preview.BlockedBy, preview.BlockedDetail);
            }

            if (preview.Changes.Count == 0)
            {
                return FlowResult.Fail(FlowError.NothingToCommit);
            }

            if (string.IsNullOrWhiteSpace(message))
            {
                return FlowResult.Fail(FlowError.EmptyCommitMessage);
            }

            var git = new GitClient(runner, repositoryPath);
            if (preview.Mode == CommitMode.AllChanges)
            {
                await git.RunCheckedAsync(ct, "add", "--all").ConfigureAwait(false);
            }

            // --cleanup=whitespace: keep lines starting with '#' (e.g. "#42"), only trim blank lines and spaces.
            var commit = await git.RunAsync(ct, "commit", "--cleanup=whitespace", "-m", message.Trim()).ConfigureAwait(false);
            if (!commit.Success)
            {
                return FlowResult.Fail(FlowError.GitCommandFailed, (commit.StandardError + commit.StandardOutput).Trim(), commit);
            }

            return FlowResult.Ok(await git.GetCurrentBranchAsync(ct).ConfigureAwait(false));
        }
        catch (GitCommandException ex)
        {
            return FlowResult.Fail(FlowError.GitCommandFailed, ex.Result.StandardError.Trim(), ex.Result);
        }
    }
}
