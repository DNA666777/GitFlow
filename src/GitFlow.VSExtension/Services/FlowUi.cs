using System.Text;
using GitFlow.Core.Config;
using GitFlow.Core.Flow;
using GitFlow.Core.Model;
using GitFlow.Core.PullRequests;
using GitFlow.Core.Resources;
using GitFlow.VSExtension.ToolWindows;
using Microsoft.VisualStudio.Extensibility;
using Microsoft.VisualStudio.Extensibility.Shell;
using Microsoft.VisualStudio.RpcContracts.Notifications;

namespace GitFlow.VSExtension.Services;

/// <summary>
/// User interaction around the flow operations, shared by the menu commands and the tool window:
/// one operation at a time, prompts, Finish options dialog, progress, French messages.
/// Returns null when the user cancelled or the operation could not start.
/// </summary>
internal sealed class FlowUi(VisualStudioExtensibility extensibility, GitFlowSession session)
{
    private FlowService Flow => session.Flow;

    /// <summary>
    /// Init opens the settings screen pre-filled (existing git-flow keys, else the user's global defaults,
    /// else the built-in defaults), then creates develop if needed and writes the configuration.
    /// </summary>
    public Task<FlowResult?> InitAsync(string repository, CancellationToken ct) => ExclusiveAsync(async () =>
    {
        var state = await Flow.GetStateAsync(repository, ct);
        if (state?.Config is { } existing)
        {
            await InfoAsync(Strings.Format(Strings.InitAlreadyInitialized, existing.MasterBranch, existing.DevelopBranch), ct);
            return null;
        }

        return await EditSettingsAsync(repository, initMode: true, ct);
    }, ct);

    /// <summary>"Paramètres GitFlow". On a repository that is not initialised yet, this is the Init screen.</summary>
    public Task<FlowResult?> SettingsAsync(string repository, CancellationToken ct) => ExclusiveAsync(async () =>
    {
        var initialized = await Flow.ConfigStore.ReadAsync(repository, ConfigScope.Local, ct) is not null;
        return await EditSettingsAsync(repository, initMode: !initialized, ct);
    }, ct);

    private async Task<FlowResult?> EditSettingsAsync(string repository, bool initMode, CancellationToken ct)
    {
        var settingsService = session.Settings;
        var current = await settingsService.LoadAsync(repository, ConfigScope.Local, ct);
        var dialog = new SettingsDialogViewModel(
            current,
            await settingsService.GetLocalBranchesAsync(repository, ct),
            initMode,
            scope => settingsService.LoadAsync(repository, scope, CancellationToken.None));
        var title = initMode ? Strings.InitDialogTitle : Strings.SettingsDialogTitle;

        // The dialog closes on OK: when something is wrong, explain and show it again with the same values.
        while (true)
        {
            using (var control = new SettingsDialogControl(dialog))
            {
                if (await extensibility.Shell().ShowDialogAsync(control, title, DialogOption.OKCancel, ct) != DialogResult.OK)
                {
                    return null;
                }
            }

            var settings = dialog.ToSettings();
            FlowResult result;
            if (initMode)
            {
                result = await RunAsync(Strings.ProgressInit, c => Flow.InitAsync(repository, settings.Naming, c), ct);
                if (result.IsSuccess)
                {
                    await settingsService.WriteFinishDefaultsAsync(repository, settings.Finish, ConfigScope.Local, ct);
                    await InfoAsync(Strings.Format(Strings.InitSuccess, settings.Naming.DevelopBranch), ct);
                    return result;
                }
            }
            else
            {
                if (dialog.Scope == ConfigScope.Local)
                {
                    var warnings = await settingsService.GetWarningsAsync(repository, current.Naming, settings.Naming, ct);
                    if (warnings.Count > 0)
                    {
                        var text = string.Join(Environment.NewLine + Environment.NewLine, warnings.Select(FlowMessages.Describe));
                        if (!await ConfirmAsync(text + Environment.NewLine + Environment.NewLine + Strings.SettingsConfirmWarnings, ct))
                        {
                            continue;
                        }
                    }
                }

                result = await RunAsync(Strings.ProgressSettings, c => settingsService.SaveAsync(repository, settings, dialog.Scope, c), ct);
                if (result.IsSuccess)
                {
                    await InfoAsync(dialog.Scope == ConfigScope.Global ? Strings.SettingsSavedGlobal : Strings.SettingsSavedLocal, ct);
                    return result;
                }
            }

            await ShowResultAsync(result, string.Empty, showSuccess: false, ct);
        }
    }

    /// <summary>Start a branch. When <paramref name="name"/> is null the user is asked (version pre-filled).</summary>
    public Task<FlowResult?> StartAsync(string repository, BranchType type, string? name, bool showSuccess, CancellationToken ct) => ExclusiveAsync(async () =>
    {
        var config = await Flow.ConfigStore.ReadAsync(repository, ConfigScope.Local, ct);
        if (config is null)
        {
            await ErrorAsync(FlowMessages.Describe(FlowResult.Fail(FlowError.NotInitialized)), ct);
            return null;
        }

        // Reminder, not a block: a release with nothing new since master would be identical to the last version.
        if (type == BranchType.Release
            && await Flow.CountUnreleasedCommitsAsync(repository, ct) == 0
            && !await ConfirmAsync(Strings.Format(Strings.StartReleaseNothingNew, config.DevelopBranch, config.MasterBranch), ct))
        {
            return null;
        }

        if (string.IsNullOrWhiteSpace(name))
        {
            var suggestion = type == BranchType.Feature ? string.Empty : await Flow.SuggestVersionAsync(repository, type, ct);
            var question = type switch
            {
                BranchType.Feature => Strings.Format(Strings.StartAskFeature, config.FeaturePrefix, config.DevelopBranch),
                BranchType.Release => Strings.Format(Strings.StartAskRelease, config.ReleasePrefix, config.DevelopBranch),
                _ => Strings.Format(Strings.StartAskHotfix, config.HotfixPrefix, config.MasterBranch),
            };
            name = await AskTextAsync(question, suggestion, ct);
            if (string.IsNullOrEmpty(name))
            {
                return null;
            }
        }

        var branch = config.GetPrefix(type) + name.Trim();
        var result = await RunAsync(Strings.Format(Strings.ProgressStart, branch), c => Flow.StartAsync(repository, type, name, null, c), ct);

        var success = Strings.Format(Strings.StartSuccess, result.BranchName ?? branch, config.GetBaseBranch(type));
        if (type == BranchType.Release)
        {
            success += Environment.NewLine + Environment.NewLine + FlowMessages.ReleaseReminder;
        }

        await ShowResultAsync(result, success, showSuccess, ct);
        return result;
    }, ct);

    /// <summary>
    /// Finish a branch (null: the branch of a stopped merge, else the current branch) after the options dialog.
    /// </summary>
    public Task<FlowResult?> FinishAsync(string repository, string? branchName, bool showSuccess, CancellationToken ct) => ExclusiveAsync(async () =>
    {
        var state = await Flow.GetStateAsync(repository, ct);
        if (state?.Config is not { } config)
        {
            await ErrorAsync(FlowMessages.Describe(FlowResult.Fail(FlowError.NotInitialized)), ct);
            return null;
        }

        var branch = branchName is not null ? config.TryParseFlowBranch(branchName) : state.PendingFinish ?? state.CurrentFlowBranch;
        if (branch is null)
        {
            await ErrorAsync(FlowMessages.Describe(FlowResult.Fail(FlowError.NotAFlowBranch, branchName ?? state.CurrentBranch ?? "HEAD")), ct);
            return null;
        }

        var resume = state.PendingFinish?.FullName == branch.FullName;
        var alreadyMerged = state.MergedFlowBranches.Contains(branch.FullName);
        var targets = await Flow.GetMergeTargetsAsync(repository, config, branch, ct);

        string summary;
        if (resume)
        {
            summary = Strings.Format(Strings.FinishConfirmResume, branch.FullName);
        }
        else if (branch.Type == BranchType.Feature)
        {
            summary = Strings.Format(Strings.FinishConfirmFeature, branch.FullName, targets[0]);
        }
        else
        {
            summary = Strings.Format(Strings.FinishConfirmVersioned, branch.FullName, targets[0], config.GetTagName(branch.Name), targets[^1]);
        }

        if (alreadyMerged && !resume)
        {
            summary += Environment.NewLine + Environment.NewLine + Strings.FinishAlreadyMerged;
        }

        var defaults = await session.Settings.ReadFinishDefaultsAsync(repository, ConfigScope.Local, ct);
        var remote = await session.PullRequests.GetRemoteAsync(repository, ct);
        var pullRequestUnavailable = resume || alreadyMerged
            ? Strings.FinishPullRequestNotNow
            : remote is null ? FlowMessages.Describe(FlowResult.Fail(FlowError.PullRequestNotSupported)) : null;
        var dialog = new FinishDialogViewModel(
            summary,
            showTagMessage: branch.Type != BranchType.Feature && !resume && !alreadyMerged,
            tagLabel: Strings.Format(Strings.FinishAskTagMessage, config.GetTagName(branch.Name)),
            tagMessage: FlowService.GetDefaultTagMessage(branch.Type, branch.Name),
            hasRemote: state.HasRemote,
            pullRequestUnavailable)
        {
            PushAfterFinish = state.HasRemote && defaults.PushAfterFinish,
            DeleteRemoteBranch = state.HasRemote && defaults.DeleteRemoteBranch,
            UsePullRequest = defaults.UsePullRequest,
        };

        using (var control = new FinishDialogControl(dialog))
        {
            var answer = await extensibility.Shell().ShowDialogAsync(control, Strings.Format(Strings.FinishDialogTitle, branch.FullName), DialogOption.OKCancel, ct);
            if (answer != DialogResult.OK)
            {
                return null;
            }
        }

        var tagMessage = dialog.ShowTagMessage && !string.IsNullOrWhiteSpace(dialog.TagMessage) ? dialog.TagMessage.Trim() : null;
        if (dialog.UsePullRequest && remote is not null)
        {
            return await FinishWithPullRequestsAsync(repository, config, branch, targets, tagMessage, remote, ct);
        }

        var options = new FinishOptions
        {
            BranchName = branch.FullName,
            TagMessage = tagMessage,
            PushAfterFinish = state.HasRemote && dialog.PushAfterFinish,
            DeleteRemoteBranch = state.HasRemote && dialog.DeleteRemoteBranch,
        };

        var result = await RunAsync(Strings.Format(Strings.ProgressFinish, branch.FullName), c => Flow.FinishAsync(repository, options, c), ct);
        var success = result.TagName is null
            ? Strings.Format(Strings.FinishSuccess, branch.FullName, result.BranchName)
            : Strings.Format(Strings.FinishSuccessTagged, branch.FullName, result.BranchName, result.TagName);
        await ShowResultAsync(result, success, showSuccess, ct);
        return result;
    }, ct);

    /// <summary>Pull Request mode: generated messages reviewed by the user, then push + one PR per target.</summary>
    private async Task<FlowResult?> FinishWithPullRequestsAsync(
        string repository, GitFlowConfig config, FlowBranch branch, IReadOnlyList<string> targets, string? tagMessage, RemoteRepository remote, CancellationToken ct)
    {
        var tagName = branch.Type == BranchType.Feature ? null : config.GetTagName(branch.Name);
        var drafts = await session.PullRequests.PrepareAsync(repository, branch, targets, tagName, ct);
        var review = new PullRequestDraftsViewModel(Strings.Format(Strings.PullRequestDraftsIntro, remote.WebUrl), drafts);
        using (var control = new PullRequestDraftsControl(review))
        {
            if (await extensibility.Shell().ShowDialogAsync(control, Strings.Format(Strings.PullRequestDraftsTitle, branch.FullName), DialogOption.OKCancel, ct) != DialogResult.OK)
            {
                return null;
            }
        }

        var options = new FinishOptions { BranchName = branch.FullName, TagMessage = tagMessage, PullRequests = review.ToDrafts() };
        var result = await RunAsync(Strings.Format(Strings.ProgressPullRequests, branch.FullName), c => Flow.FinishAsync(repository, options, c), ct);
        if (!result.IsSuccess)
        {
            await ShowResultAsync(result, string.Empty, showSuccess: false, ct);
            return result;
        }

        var text = new StringBuilder(Strings.Format(Strings.PullRequestsCreated, branch.FullName)).AppendLine();
        foreach (var pr in result.PullRequests)
        {
            text.AppendLine().Append($"• #{pr.Id} → {pr.Target} : {pr.WebUrl}");
        }

        if (tagName is not null)
        {
            text.AppendLine().AppendLine().Append(Strings.Format(Strings.PullRequestsTagLater, tagName, config.MasterBranch));
        }

        text.AppendLine().AppendLine().Append(Strings.PullRequestsOpenAsk);
        if (await ConfirmAsync(text.ToString(), ct))
        {
            foreach (var pr in result.PullRequests)
            {
                OpenInBrowser(pr.WebUrl);
            }
        }

        return result;
    }

    /// <summary>Release/hotfix in PR mode: tags the merge commit of the PR to production and pushes the tag.</summary>
    public Task<FlowResult?> TagMergedPullRequestAsync(string repository, string branchName, CancellationToken ct) => ExclusiveAsync(async () =>
    {
        var result = await RunAsync(Strings.Format(Strings.ProgressTag, branchName), c => session.PullRequests.TagMergedAsync(repository, branchName, c), ct);
        await ShowResultAsync(result, Strings.Format(Strings.TagDone, result.TagName), showSuccess: true, ct);
        return result;
    }, ct);

    /// <summary>
    /// PR mode: once every PR is merged, updates the local targets and deletes the local branch
    /// (with <c>fetch --prune</c> when the option is enabled in the settings).
    /// </summary>
    public Task<FlowResult?> CompletePullRequestsAsync(string repository, string branchName, CancellationToken ct) => ExclusiveAsync(async () =>
    {
        var prune = (await session.Settings.ReadFinishDefaultsAsync(repository, ConfigScope.Local, ct)).PruneOnComplete;
        var result = await RunAsync(Strings.Format(Strings.ProgressComplete, branchName), c => session.PullRequests.CompleteAsync(repository, branchName, prune, c), ct);
        await ShowResultAsync(result, Strings.Format(Strings.CompleteDone, branchName), showSuccess: true, ct);
        return result;
    }, ct);

    /// <summary>Opens a pull request page in the default browser (https only).</summary>
    public static void OpenInBrowser(Uri url)
    {
        if (url.Scheme == Uri.UriSchemeHttps)
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(url.AbsoluteUri) { UseShellExecute = true })?.Dispose();
        }
    }

    public Task<FlowResult?> PublishAsync(string repository, string? branchName, bool showSuccess, CancellationToken ct) => ExclusiveAsync(async () =>
    {
        branchName ??= (await Flow.GetStateAsync(repository, ct))?.CurrentBranch;
        var label = branchName ?? "HEAD";
        var result = await RunAsync(Strings.Format(Strings.ProgressPublish, label), c => Flow.PublishAsync(repository, branchName, c), ct);
        await ShowResultAsync(result, Strings.Format(Strings.PublishSuccess, result.BranchName ?? label), showSuccess, ct);
        return result;
    }, ct);

    public Task<FlowResult?> CommitAsync(string repository, string message, CancellationToken ct) => ExclusiveAsync(async () =>
    {
        var result = await RunAsync(Strings.ProgressCommit, c => session.Commits.CommitAsync(repository, message, c), ct);
        await ShowResultAsync(result, string.Empty, showSuccess: false, ct);
        return result;
    }, ct);

    /// <summary>Message for the status line of the tool window.</summary>
    public static string Summarize(FlowResult result) =>
        result.IsSuccess
            ? (result.Warnings.Count == 0 ? Strings.StatusSucceeded : Strings.StatusSucceededWithWarnings)
            : FlowMessages.Describe(result);

    public Task InfoAsync(string message, CancellationToken ct) =>
        extensibility.Shell().ShowPromptAsync(message, PromptOptions.OK with { Title = Strings.Title }, ct);

    public Task ErrorAsync(string message, CancellationToken ct) =>
        extensibility.Shell().ShowPromptAsync(message, PromptOptions.ErrorConfirm with { Title = Strings.Title }, ct);

    public Task<bool> ConfirmAsync(string message, CancellationToken ct) =>
        extensibility.Shell().ShowPromptAsync(message, PromptOptions.OKCancel with { Title = Strings.Title }, ct);

    public async Task<string?> AskTextAsync(string question, string defaultText, CancellationToken ct)
    {
        var answer = await extensibility.Shell().ShowPromptAsync(
            question,
            InputPromptOptions.Default with { DefaultText = defaultText, Title = Strings.Title },
            ct);
        return answer?.Trim();
    }

    /// <summary>Never throws: a failure must never be silent.</summary>
    public async Task ReportUnexpectedAsync(Exception ex)
    {
        session.Output.WriteLine(ex.ToString());
        try
        {
            await ErrorAsync(Strings.Format(Strings.UnexpectedError, ex.Message), CancellationToken.None);
        }
        catch (Exception displayError)
        {
            session.Output.WriteLine(displayError.ToString());
            await extensibility.Shell().ShowPromptAsync("GitFlow : erreur inattendue. " + ex.Message, PromptOptions.OK, CancellationToken.None);
        }
    }

    private async Task<FlowResult?> ExclusiveAsync(Func<Task<FlowResult?>> action, CancellationToken ct)
    {
        using var operation = session.TryBeginOperation();
        if (operation is null)
        {
            await InfoAsync(Strings.OperationInProgress, ct);
            return null;
        }

        try
        {
            await session.Output.EnsureChannelAsync(extensibility, ct);
            return await action();
        }
        catch (OperationCanceledException)
        {
            return null;
        }
        catch (Exception ex)
        {
            await ReportUnexpectedAsync(ex);
            return null;
        }
    }

    private async Task<FlowResult> RunAsync(string progressTitle, Func<CancellationToken, Task<FlowResult>> operation, CancellationToken ct)
    {
        session.Output.WriteLine($"=== {progressTitle} ===");
        using var progress = await extensibility.Shell().StartProgressReportingAsync(progressTitle, ct);
        var result = await operation(ct);
        session.Output.WriteLine(result.IsSuccess ? "OK" : FlowMessages.Describe(result));
        return result;
    }

    private async Task ShowResultAsync(FlowResult result, string successMessage, bool showSuccess, CancellationToken ct)
    {
        if (result.IsSuccess)
        {
            if (!showSuccess && result.Warnings.Count == 0)
            {
                return;
            }

            var text = new StringBuilder(successMessage);
            if (result.Warnings.Count > 0)
            {
                text.AppendLine().AppendLine().AppendLine(Strings.WarningsHeader);
                foreach (var warning in result.Warnings)
                {
                    text.Append("• ").AppendLine(FlowMessages.Describe(warning));
                }
            }

            await InfoAsync(text.ToString().TrimEnd(), ct);
            return;
        }

        var message = new StringBuilder(FlowMessages.Describe(result));
        if (result.Conflict is { } conflict)
        {
            message.AppendLine().AppendLine().AppendLine(Strings.ConflictedFiles);
            foreach (var file in conflict.Files.Take(15))
            {
                message.Append("• ").AppendLine(file);
            }

            if (conflict.Files.Count > 15)
            {
                message.AppendLine("…");
            }
        }

        await ErrorAsync(message.ToString().TrimEnd(), ct);
    }
}
