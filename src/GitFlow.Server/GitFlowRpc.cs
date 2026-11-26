using System.Globalization;
using GitFlow.Core.Config;
using GitFlow.Core.Flow;
using GitFlow.Core.Git;
using GitFlow.Core.Model;
using GitFlow.Core.PullRequests;
using GitFlow.Core.Resources;
using StreamJsonRpc;

namespace GitFlow.Server;

/// <summary>
/// JSON-RPC surface of the Core for the VS Code extension: a thin layer, like GitFlow.VSExtension.
/// One method per operation; operations run one at a time (the repository is shared).
/// Expected failures come back as <see cref="ResultDto"/>, never as JSON-RPC errors.
/// </summary>
public sealed class GitFlowRpc
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly FlowService _flow;
    private readonly CommitService _commits;
    private readonly GitFlowSettingsService _settings;
    private readonly PullRequestService _pullRequests;
    private readonly FinishPlanner _planner;

    public GitFlowRpc(IGitRunner runner, PullRequestService pullRequests)
    {
        _pullRequests = pullRequests;
        _flow = new FlowService(runner, pullRequestStrategy: new PullRequestStrategy(pullRequests));
        _commits = new CommitService(runner);
        _settings = new GitFlowSettingsService(runner);
        _planner = new FinishPlanner(_flow, _settings, pullRequests);
    }

    /// <summary>Must be called first. <paramref name="culture"/>: VS Code's language (fr* → French, otherwise English).</summary>
    [JsonRpcMethod("initialize")]
    public ServerInfo Initialize(string? culture = null)
    {
        var info = string.IsNullOrWhiteSpace(culture) ? CultureInfo.InvariantCulture : TryCulture(culture);
        FlowMessages.Culture = info;
        CultureInfo.DefaultThreadCurrentUICulture = info;
        var version = typeof(GitFlowRpc).Assembly.GetName().Version?.ToString(3) ?? "0.0.0";
        return new ServerInfo(version, info.Name);
    }

    /// <summary>Root of the git repository containing <paramref name="path"/>, or null.</summary>
    [JsonRpcMethod("findRepository")]
    public Task<string?> FindRepositoryAsync(string path, CancellationToken ct) =>
        ReadAsync(() => _flow.FindRepositoryRootAsync(path, ct), ct);

    [JsonRpcMethod("getState")]
    public Task<StateDto?> GetStateAsync(string repository, CancellationToken ct) =>
        ReadAsync(async () => await _flow.GetStateAsync(repository, ct).ConfigureAwait(false) is { } s ? StateDto.From(s) : null, ct);

    // ---------- Settings / Init ----------

    /// <summary>Settings of a scope; for an uninitialised repository, the Init defaults (existing git-flow keys, global, built-in).</summary>
    [JsonRpcMethod("getSettings")]
    public Task<SettingsDto> GetSettingsAsync(string repository, ConfigScope scope, CancellationToken ct) =>
        ReadAsync(async () => SettingsDto.From(await _settings.LoadAsync(repository, scope, ct).ConfigureAwait(false)), ct);

    [JsonRpcMethod("getLocalBranches")]
    public Task<IReadOnlyList<string>> GetLocalBranchesAsync(string repository, CancellationToken ct) =>
        ReadAsync(() => _settings.GetLocalBranchesAsync(repository, ct), ct);

    /// <summary>Non-blocking consequences of a naming change (translated), to confirm before saving.</summary>
    [JsonRpcMethod("getSettingsWarnings")]
    public Task<IReadOnlyList<string>> GetSettingsWarningsAsync(string repository, NamingDto proposed, CancellationToken ct) =>
        ReadAsync<IReadOnlyList<string>>(async () =>
        {
            var current = await _settings.LoadAsync(repository, ConfigScope.Local, ct).ConfigureAwait(false);
            var warnings = await _settings.GetWarningsAsync(repository, current.Naming, proposed.ToConfig(), ct).ConfigureAwait(false);
            return warnings.Select(FlowMessages.Describe).ToArray();
        }, ct);

    [JsonRpcMethod("saveSettings")]
    public Task<ResultDto> SaveSettingsAsync(string repository, SettingsDto settings, ConfigScope scope, CancellationToken ct) =>
        RunAsync(async () => ResultDto.From(await _settings.SaveAsync(repository, settings.ToSettings(), scope, ct).ConfigureAwait(false)), ct);

    /// <summary>Init: creates develop if needed and writes the naming, then the Finish defaults of this repository.</summary>
    [JsonRpcMethod("init")]
    public Task<ResultDto> InitAsync(string repository, SettingsDto settings, CancellationToken ct) =>
        RunAsync(async () =>
        {
            var result = await _flow.InitAsync(repository, settings.Naming.ToConfig(), ct).ConfigureAwait(false);
            if (result.IsSuccess)
            {
                await _settings.WriteFinishDefaultsAsync(repository, settings.Finish.ToDefaults(), ConfigScope.Local, ct).ConfigureAwait(false);
            }

            return ResultDto.From(result);
        }, ct);

    // ---------- Start ----------

    /// <summary>Suggested version of a release (minor + 1) or hotfix (patch + 1); empty for a feature.</summary>
    [JsonRpcMethod("suggestVersion")]
    public Task<string> SuggestVersionAsync(string repository, BranchType type, CancellationToken ct) =>
        ReadAsync(async () => type == BranchType.Feature ? string.Empty : await _flow.SuggestVersionAsync(repository, type, ct).ConfigureAwait(false), ct);

    /// <summary>Commits on develop not yet released (0: a release would be identical to the last one).</summary>
    [JsonRpcMethod("countUnreleasedCommits")]
    public Task<int?> CountUnreleasedCommitsAsync(string repository, CancellationToken ct) =>
        ReadAsync(() => _flow.CountUnreleasedCommitsAsync(repository, ct), ct);

    [JsonRpcMethod("start")]
    public Task<ResultDto> StartAsync(string repository, BranchType type, string name, CancellationToken ct) =>
        RunAsync(async () => ResultDto.From(await _flow.StartAsync(repository, type, name, null, ct).ConfigureAwait(false)), ct);

    // ---------- Finish ----------

    /// <param name="branch">Branch to finish; null: the branch of a stopped merge, otherwise the current branch.</param>
    [JsonRpcMethod("prepareFinish")]
    public Task<PrepareFinishResponse> PrepareFinishAsync(string repository, string? branch, CancellationToken ct) =>
        RunAsync(async () =>
        {
            var (context, error) = await _planner.PrepareAsync(repository, branch, ct).ConfigureAwait(false);
            return context is null
                ? new PrepareFinishResponse(null, ResultDto.From(error!))
                : new PrepareFinishResponse(FinishContextDto.From(context), null);
        }, ct);

    /// <summary>Local merge (or resume after a conflict).</summary>
    [JsonRpcMethod("finish")]
    public Task<ResultDto> FinishAsync(string repository, string branch, string? tagMessage, bool pushAfterFinish, bool deleteRemoteBranch, CancellationToken ct) =>
        RunAsync(async () => ResultDto.From(await _flow.FinishAsync(
            repository,
            new FinishOptions
            {
                BranchName = branch,
                TagMessage = string.IsNullOrWhiteSpace(tagMessage) ? null : tagMessage.Trim(),
                PushAfterFinish = pushAfterFinish,
                DeleteRemoteBranch = deleteRemoteBranch,
            },
            ct).ConfigureAwait(false)), ct);

    /// <summary>Pull Request mode, step 1: history check (nothing pushed) and generated messages to review.</summary>
    [JsonRpcMethod("preparePullRequests")]
    public Task<PreparePullRequestsResponse> PreparePullRequestsAsync(string repository, string branch, CancellationToken ct) =>
        RunAsync(async () =>
        {
            var (context, error) = await _planner.PrepareAsync(repository, branch, ct).ConfigureAwait(false);
            if (context is null)
            {
                return new PreparePullRequestsResponse([], ResultDto.From(error!));
            }

            var history = await _pullRequests.CheckHistoryAsync(repository, context.Branch, context.Targets, ct).ConfigureAwait(false);
            if (!history.IsSuccess)
            {
                return new PreparePullRequestsResponse([], ResultDto.From(history));
            }

            var drafts = await _pullRequests.PrepareAsync(repository, context.Branch, context.Targets, context.TagName, ct).ConfigureAwait(false);
            return new PreparePullRequestsResponse(drafts.Select(PullRequestDraftDto.From).ToArray(), null);
        }, ct);

    /// <summary>Pull Request mode, step 2: push the branch and open (or reuse) one PR per reviewed draft. Nothing is merged locally.</summary>
    [JsonRpcMethod("finishWithPullRequests")]
    public Task<ResultDto> FinishWithPullRequestsAsync(string repository, string branch, string? tagMessage, IReadOnlyList<PullRequestDraftDto> drafts, CancellationToken ct) =>
        RunAsync(async () => ResultDto.From(await _flow.FinishAsync(
            repository,
            new FinishOptions
            {
                BranchName = branch,
                TagMessage = string.IsNullOrWhiteSpace(tagMessage) ? null : tagMessage.Trim(),
                PullRequests = drafts.Select(d => d.ToDraft()).ToArray(),
            },
            ct).ConfigureAwait(false)), ct);

    // ---------- Push ----------

    /// <summary>git push -u origin &lt;branch&gt; (null: the current branch).</summary>
    [JsonRpcMethod("publish")]
    public Task<ResultDto> PublishAsync(string repository, string? branch, CancellationToken ct) =>
        RunAsync(async () => ResultDto.From(await _flow.PublishAsync(repository, branch, ct).ConfigureAwait(false)), ct);

    // ---------- Commit ----------

    /// <summary>Changes that a commit would contain now (staged files only when something is staged) and the generated message.</summary>
    [JsonRpcMethod("getCommitPreview")]
    public Task<CommitPreviewDto?> GetCommitPreviewAsync(string repository, CancellationToken ct) =>
        ReadAsync(async () => await _commits.GetPreviewAsync(repository, ct).ConfigureAwait(false) is { } p ? CommitPreviewDto.From(p) : null, ct);

    /// <summary>Commit with the user's message; refused on the production branch and while a merge is in progress.</summary>
    [JsonRpcMethod("commit")]
    public Task<ResultDto> CommitAsync(string repository, string message, CancellationToken ct) =>
        RunAsync(async () => ResultDto.From(await _commits.CommitAsync(repository, message, ct).ConfigureAwait(false)), ct);

    // ---------- Pull requests ----------

    /// <param name="refresh">Read the states on the platform (network); otherwise only the tracked links.</param>
    [JsonRpcMethod("getPullRequests")]
    public Task<IReadOnlyList<PullRequestGroupDto>> GetPullRequestsAsync(string repository, bool refresh, CancellationToken ct) =>
        ReadAsync<IReadOnlyList<PullRequestGroupDto>>(async () =>
            (await _pullRequests.GetGroupsAsync(repository, refresh, ct).ConfigureAwait(false)).Select(PullRequestGroupDto.From).ToArray(), ct);

    /// <summary>Release/hotfix: tags the merge commit of the PR to production and pushes the tag.</summary>
    [JsonRpcMethod("tagPullRequest")]
    public Task<ResultDto> TagPullRequestAsync(string repository, string branch, CancellationToken ct) =>
        RunAsync(async () => ResultDto.From(await _pullRequests.TagMergedAsync(repository, branch, ct).ConfigureAwait(false)), ct);

    /// <summary>All PRs merged: tags if needed, updates the local targets, deletes the local branch (fetch --prune if enabled).</summary>
    [JsonRpcMethod("completePullRequests")]
    public Task<ResultDto> CompletePullRequestsAsync(string repository, string branch, CancellationToken ct) =>
        RunAsync(async () =>
        {
            var prune = (await _settings.ReadFinishDefaultsAsync(repository, ConfigScope.Local, ct).ConfigureAwait(false)).PruneOnComplete;
            return ResultDto.From(await _pullRequests.CompleteAsync(repository, branch, prune, ct).ConfigureAwait(false));
        }, ct);

    private static CultureInfo TryCulture(string name)
    {
        try
        {
            return CultureInfo.GetCultureInfo(name);
        }
        catch (CultureNotFoundException)
        {
            return CultureInfo.InvariantCulture;
        }
    }

    /// <summary>
    /// True while a read done for display runs (state of the view, settings, suggestions): the client logs its git
    /// commands at the "trace" level, so that the operations stay readable. Every command is still logged.
    /// </summary>
    public static bool IsReading => Reading.Value;

    private static readonly AsyncLocal<bool> Reading = new();

    /// <summary>An operation asked by the user: its git commands are logged at the "info" level.</summary>
    private Task<T> RunAsync<T>(Func<Task<T>> operation, CancellationToken ct) => ExecuteAsync(operation, reading: false, ct);

    /// <summary>A read for display (see <see cref="IsReading"/>).</summary>
    private Task<T> ReadAsync<T>(Func<Task<T>> operation, CancellationToken ct) => ExecuteAsync(operation, reading: true, ct);

    private async Task<T> ExecuteAsync<T>(Func<Task<T>> operation, bool reading, CancellationToken ct)
    {
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            Reading.Value = reading;
            return await operation().ConfigureAwait(false);
        }
        finally
        {
            Reading.Value = false;
            _gate.Release();
        }
    }
}
