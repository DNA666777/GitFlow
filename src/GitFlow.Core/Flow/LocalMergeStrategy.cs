using GitFlow.Core.Git;
using GitFlow.Core.Model;

namespace GitFlow.Core.Flow;

/// <summary>
/// Finishes a branch with local <c>--no-ff</c> merges. Every step is skipped when already done,
/// so running it again after a resolved conflict resumes where it stopped.
/// </summary>
public sealed class LocalMergeStrategy(IGitRunner runner) : IFinishStrategy
{
    public async Task<FlowResult> ExecuteAsync(FinishPlan plan, CancellationToken cancellationToken)
    {
        var ct = cancellationToken;
        var git = new GitClient(runner, plan.RepositoryPath);
        var source = plan.Branch.FullName;

        for (var i = 0; i < plan.MergeTargets.Count; i++)
        {
            var target = plan.MergeTargets[i];
            if (!await git.IsAncestorAsync("refs/heads/" + source, "refs/heads/" + target, ct).ConfigureAwait(false))
            {
                await git.RunCheckedAsync(ct, "switch", target).ConfigureAwait(false);
                var merge = await git.RunAsync(ct, "merge", "--no-ff", "-m", $"Merge branch '{source}' into {target}", source).ConfigureAwait(false);
                if (!merge.Success)
                {
                    var conflicts = await git.GetConflictedFilesAsync(ct).ConfigureAwait(false);
                    return conflicts.Count > 0
                        ? FlowResult.Conflicted(new MergeConflict(source, target, conflicts))
                        : FlowResult.Fail(FlowError.GitCommandFailed, merge.StandardError.Trim(), merge);
                }
            }

            // The tag goes on master right after the merge into master.
            if (i == 0 && plan.TagName is not null && !await git.TagExistsAsync(plan.TagName, ct).ConfigureAwait(false))
            {
                await git.RunCheckedAsync(ct, "tag", "-a", plan.TagName, "-m", plan.TagMessage ?? plan.TagName, target).ConfigureAwait(false);
            }
        }

        var finalBranch = plan.MergeTargets[^1];
        if (await git.GetCurrentBranchAsync(ct).ConfigureAwait(false) != finalBranch)
        {
            await git.RunCheckedAsync(ct, "switch", finalBranch).ConfigureAwait(false);
        }

        var warnings = new List<FlowWarning>();
        var pushFailed = false;
        if (plan.PushAfterFinish)
        {
            var pushArgs = new List<string> { "push", FlowService.RemoteName };
            pushArgs.AddRange(plan.MergeTargets);
            if (plan.TagName is not null)
            {
                pushArgs.Add("refs/tags/" + plan.TagName);
            }

            var push = await git.RunAsync(ct, [.. pushArgs]).ConfigureAwait(false);
            if (!push.Success)
            {
                pushFailed = true;
                warnings.Add(new FlowWarning(FlowWarningKind.PushFailed, push.StandardError.Trim(), push));
            }
        }

        // "-d" (never "-D"): git refuses to delete a branch that is not merged.
        var delete = await git.RunAsync(ct, "branch", "-d", source).ConfigureAwait(false);
        if (!delete.Success)
        {
            warnings.Add(new FlowWarning(FlowWarningKind.LocalBranchDeleteFailed, source, delete));
        }

        if (plan.DeleteRemoteBranch)
        {
            if (pushFailed)
            {
                // Keep the remote copy: the merged result has not reached origin.
                warnings.Add(new FlowWarning(FlowWarningKind.RemoteBranchKeptBecausePushFailed, source));
            }
            else if (await git.RemoteBranchExistsAsync(FlowService.RemoteName, source, ct).ConfigureAwait(false))
            {
                var deleteRemote = await git.RunAsync(ct, "push", FlowService.RemoteName, "--delete", source).ConfigureAwait(false);
                if (!deleteRemote.Success)
                {
                    warnings.Add(new FlowWarning(FlowWarningKind.RemoteBranchDeleteFailed, source, deleteRemote));
                }
            }
        }

        return FlowResult.Ok(finalBranch, plan.TagName, warnings);
    }
}
