using System.Runtime.Serialization;
using GitFlow.Core.Model;
using GitFlow.Core.Resources;
using GitFlow.VSExtension.Services;
using Microsoft.VisualStudio.Extensibility;
using Microsoft.VisualStudio.Extensibility.UI;

namespace GitFlow.VSExtension.ToolWindows;

/// <summary>
/// State of the GitFlow tool window. Orchestration only: the flow logic is in GitFlow.Core,
/// user interaction in <see cref="FlowUi"/>.
/// </summary>
[DataContract]
internal sealed class GitFlowToolWindowViewModel : NotifyPropertyChangedObject, IDisposable
{
    private static readonly BranchType[] StartTypeOrder = [BranchType.Feature, BranchType.Release, BranchType.Hotfix];

    private readonly VisualStudioExtensibility _extensibility;
    private readonly GitFlowSession _session;
    private readonly SemaphoreSlim _refreshLock = new(1, 1);
    private readonly RepositoryWatcher _watcher;
    private readonly Timer _solutionPoll;
    private string? _repository;
    private string? _solutionPath;
    private bool _visible;
    private bool _refreshRequested;

    private bool _isIdle = true;
    private string _statusText = string.Empty;
    private bool _hasRepository;
    private string _repositoryName = string.Empty;
    private bool _isInitialized;
    private string _currentBranch = string.Empty;
    private string _currentBranchType = string.Empty;
    private string _workingTreeState = string.Empty;
    private bool _isDirty;
    private string _syncState = string.Empty;
    private bool _showReleaseReminder;
    private bool _hasPendingFinish;
    private string _pendingFinishText = string.Empty;
    private int _selectedStartTypeIndex;
    private string _startPrefix = string.Empty;
    private string _newBranchName = string.Empty;
    private bool _hasNoBranches = true;
    private string _commitMessage = string.Empty;
    private bool _commitMessageEdited;
    private bool _settingCommitMessage;
    private string _changesHeader = string.Empty;
    private string _commitModeText = string.Empty;
    private string _commitBlockedText = string.Empty;
    private bool _canCommit;
    private bool _hasChanges;

    public GitFlowToolWindowViewModel(VisualStudioExtensibility extensibility, GitFlowSession session)
    {
        _extensibility = extensibility;
        _session = session;
        _watcher = new RepositoryWatcher(() => _ = OnRepositoryChangedAsync());
        _solutionPoll = new Timer(_ => _ = PollSolutionAsync(), null, Timeout.Infinite, Timeout.Infinite);

        StartTypes = new ObservableList<string>([Strings.TypeFeature, Strings.TypeRelease, Strings.TypeHotfix]);
        RefreshCommand = new AsyncCommand((_, context, ct) => RefreshAsync(context, ct));
        InitCommand = new AsyncCommand((_, context, ct) => RunAsync(context, (ui, repo, c) => ui.InitAsync(repo, c), ct));
        StartCommand = new AsyncCommand((_, context, ct) => RunAsync(
            context,
            (ui, repo, c) => ui.StartAsync(repo, StartTypeOrder[Math.Clamp(SelectedStartTypeIndex, 0, 2)], NewBranchName, showSuccess: false, c),
            ct,
            onSuccess: () => NewBranchName = string.Empty));
        ResumeFinishCommand = new AsyncCommand((_, context, ct) => RunAsync(context, (ui, repo, c) => ui.FinishAsync(repo, null, showSuccess: false, c), ct));
        CommitCommand = new AsyncCommand((_, context, ct) => RunAsync(
            context,
            (ui, repo, c) => ui.CommitAsync(repo, CommitMessage, c),
            ct,
            onSuccess: () => SetGeneratedCommitMessage(string.Empty)));
        RegenerateMessageCommand = new AsyncCommand((_, context, ct) =>
        {
            _commitMessageEdited = false;
            return RefreshAsync(context, ct);
        });
    }

    [DataMember] public AsyncCommand CommitCommand { get; }

    [DataMember] public AsyncCommand RegenerateMessageCommand { get; }

    [DataMember] public ObservableList<ChangeItemViewModel> Changes { get; } = [];

    /// <summary>Commit message. Generated from the changes until the user edits it, then left alone.</summary>
    [DataMember]
    public string CommitMessage
    {
        get => _commitMessage;
        set
        {
            if (SetProperty(ref _commitMessage, value) && !_settingCommitMessage)
            {
                _commitMessageEdited = true;
            }
        }
    }

    [DataMember]
    public string ChangesHeader
    {
        get => _changesHeader;
        set => SetProperty(ref _changesHeader, value);
    }

    [DataMember]
    public string CommitModeText
    {
        get => _commitModeText;
        set => SetProperty(ref _commitModeText, value);
    }

    [DataMember]
    public string CommitBlockedText
    {
        get => _commitBlockedText;
        set
        {
            if (SetProperty(ref _commitBlockedText, value))
            {
                RaiseNotifyPropertyChangedEvent(nameof(IsCommitBlocked));
            }
        }
    }

    [DataMember] public bool IsCommitBlocked => CommitBlockedText.Length > 0;

    [DataMember]
    public bool CanCommit
    {
        get => _canCommit;
        set => SetProperty(ref _canCommit, value);
    }

    [DataMember]
    public bool HasChanges
    {
        get => _hasChanges;
        set
        {
            if (SetProperty(ref _hasChanges, value))
            {
                RaiseNotifyPropertyChangedEvent(nameof(HasNoChanges));
            }
        }
    }

    [DataMember] public bool HasNoChanges => !HasChanges;

    [DataMember] public ToolWindowLabels Labels { get; } = new();

    [DataMember] public AsyncCommand RefreshCommand { get; }

    [DataMember] public AsyncCommand InitCommand { get; }

    [DataMember] public AsyncCommand StartCommand { get; }

    [DataMember] public AsyncCommand ResumeFinishCommand { get; }

    [DataMember] public ObservableList<string> StartTypes { get; }

    [DataMember] public ObservableList<BranchItemViewModel> Features { get; } = [];

    [DataMember] public ObservableList<BranchItemViewModel> Releases { get; } = [];

    [DataMember] public ObservableList<BranchItemViewModel> Hotfixes { get; } = [];

    [DataMember]
    public bool IsIdle
    {
        get => _isIdle;
        set => SetProperty(ref _isIdle, value);
    }

    [DataMember]
    public string StatusText
    {
        get => _statusText;
        set => SetProperty(ref _statusText, value);
    }

    [DataMember]
    public bool HasRepository
    {
        get => _hasRepository;
        set
        {
            if (SetProperty(ref _hasRepository, value))
            {
                RaiseNotifyPropertyChangedEvent(nameof(ShowNoRepository));
                RaiseNotifyPropertyChangedEvent(nameof(ShowInitPanel));
                RaiseNotifyPropertyChangedEvent(nameof(ShowFlowPanel));
            }
        }
    }

    [DataMember] public bool ShowNoRepository => !HasRepository;

    [DataMember]
    public string RepositoryName
    {
        get => _repositoryName;
        set => SetProperty(ref _repositoryName, value);
    }

    [DataMember]
    public bool IsInitialized
    {
        get => _isInitialized;
        set
        {
            if (SetProperty(ref _isInitialized, value))
            {
                RaiseNotifyPropertyChangedEvent(nameof(ShowInitPanel));
                RaiseNotifyPropertyChangedEvent(nameof(ShowFlowPanel));
            }
        }
    }

    [DataMember] public bool ShowInitPanel => HasRepository && !IsInitialized;

    [DataMember] public bool ShowFlowPanel => HasRepository && IsInitialized;

    [DataMember]
    public string CurrentBranch
    {
        get => _currentBranch;
        set => SetProperty(ref _currentBranch, value);
    }

    [DataMember]
    public string CurrentBranchType
    {
        get => _currentBranchType;
        set => SetProperty(ref _currentBranchType, value);
    }

    [DataMember]
    public string WorkingTreeState
    {
        get => _workingTreeState;
        set => SetProperty(ref _workingTreeState, value);
    }

    [DataMember]
    public bool IsDirty
    {
        get => _isDirty;
        set => SetProperty(ref _isDirty, value);
    }

    [DataMember]
    public string SyncState
    {
        get => _syncState;
        set => SetProperty(ref _syncState, value);
    }

    [DataMember]
    public bool ShowReleaseReminder
    {
        get => _showReleaseReminder;
        set => SetProperty(ref _showReleaseReminder, value);
    }

    [DataMember]
    public bool HasPendingFinish
    {
        get => _hasPendingFinish;
        set => SetProperty(ref _hasPendingFinish, value);
    }

    [DataMember]
    public string PendingFinishText
    {
        get => _pendingFinishText;
        set => SetProperty(ref _pendingFinishText, value);
    }

    [DataMember]
    public int SelectedStartTypeIndex
    {
        get => _selectedStartTypeIndex;
        set
        {
            if (SetProperty(ref _selectedStartTypeIndex, value))
            {
                _ = UpdateStartSuggestionAsync();
            }
        }
    }

    [DataMember]
    public string StartPrefix
    {
        get => _startPrefix;
        set => SetProperty(ref _startPrefix, value);
    }

    [DataMember]
    public string NewBranchName
    {
        get => _newBranchName;
        set => SetProperty(ref _newBranchName, value);
    }

    [DataMember]
    public bool HasNoBranches
    {
        get => _hasNoBranches;
        set => SetProperty(ref _hasNoBranches, value);
    }

    public async Task SetVisibleAsync(bool visible, CancellationToken ct)
    {
        _visible = visible;
        if (visible)
        {
            await _session.Output.EnsureChannelAsync(_extensibility, ct);
            _solutionPoll.Change(TimeSpan.FromSeconds(3), TimeSpan.FromSeconds(3));
            await RefreshAsync(null, ct);
        }
        else
        {
            _solutionPoll.Change(Timeout.Infinite, Timeout.Infinite);
        }
    }

    /// <summary>Reloads the repository state. Concurrent requests are coalesced.</summary>
    public async Task RefreshAsync(IClientContext? context, CancellationToken ct)
    {
        if (!await _refreshLock.WaitAsync(0, ct))
        {
            _refreshRequested = true;
            return;
        }

        try
        {
            do
            {
                _refreshRequested = false;
                await RefreshCoreAsync(context, ct);
            }
            while (_refreshRequested);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _session.Output.WriteLine(ex.ToString());
            StatusText = Strings.Format(Strings.UnexpectedError, ex.Message);
        }
        finally
        {
            _refreshLock.Release();
        }
    }

    public void Dispose()
    {
        _solutionPoll.Dispose();
        _watcher.Dispose();
        _refreshLock.Dispose();
    }

    private async Task RefreshCoreAsync(IClientContext? context, CancellationToken ct)
    {
        _solutionPath = await RepositoryLocator.GetSolutionPathAsync(_extensibility, ct);
        var repository = await RepositoryLocator.FindAsync(_extensibility, _session.Flow, context, ct) ?? _repository;
        if (repository is not null && !Directory.Exists(repository))
        {
            repository = null;
        }

        if (!string.Equals(repository, _repository, StringComparison.OrdinalIgnoreCase))
        {
            _repository = repository;
            _watcher.Watch(repository);
        }

        var state = repository is null ? null : await _session.Flow.GetStateAsync(repository, ct);
        if (state is null)
        {
            HasRepository = false;
            ClearBranches();
            return;
        }

        HasRepository = true;
        RepositoryName = Path.GetFileName(state.RepositoryPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        IsInitialized = state.IsInitialized;
        CurrentBranch = state.CurrentBranch ?? Strings.DetachedHead;
        CurrentBranchType = state.CurrentFlowBranch is { } current ? TypeLabel(current.Type) : Strings.TypeOther;
        IsDirty = state.IsDirty;
        WorkingTreeState = state.IsMergeInProgress ? Strings.StateMerging : state.IsDirty ? Strings.StateDirty : Strings.StateClean;
        SyncState = state.Ahead is { } ahead && state.Behind is { } behind
            ? Strings.Format(Strings.SyncFormat, ahead, behind)
            : state.HasRemote ? Strings.SyncNoUpstream : Strings.SyncNoRemote;
        ShowReleaseReminder = state.ShowReleaseReminder;
        HasPendingFinish = state.PendingFinish is not null;
        PendingFinishText = state.PendingFinish is { } pending ? Strings.Format(Strings.PendingFinish, pending.FullName) : string.Empty;

        if (state.Config is { } config)
        {
            StartPrefix = config.GetPrefix(StartTypeOrder[Math.Clamp(SelectedStartTypeIndex, 0, 2)]);
        }

        ClearBranches();
        foreach (var branch in state.FlowBranches)
        {
            var item = new BranchItemViewModel(
                branch,
                isCurrent: branch.FullName == state.CurrentBranch,
                isMerged: state.MergedFlowBranches.Contains(branch.FullName),
                finish: new AsyncCommand((_, c, t) => RunAsync(c, (ui, repo, x) => ui.FinishAsync(repo, branch.FullName, showSuccess: false, x), t)),
                publish: new AsyncCommand((_, c, t) => RunAsync(c, (ui, repo, x) => ui.PublishAsync(repo, branch.FullName, showSuccess: false, x), t)));
            (branch.Type switch
            {
                BranchType.Feature => Features,
                BranchType.Release => Releases,
                _ => Hotfixes,
            }).Add(item);
        }

        HasNoBranches = state.FlowBranches.Count == 0;

        await RefreshCommitAsync(state.RepositoryPath, ct);
    }

    private async Task RefreshCommitAsync(string repository, CancellationToken ct)
    {
        var preview = await _session.Commits.GetPreviewAsync(repository, ct);
        Changes.Clear();
        if (preview is null || preview.Changes.Count == 0)
        {
            HasChanges = false;
            CanCommit = false;
            ChangesHeader = Strings.Format(Strings.ChangesHeader, 0);
            CommitModeText = string.Empty;
            CommitBlockedText = string.Empty;
            if (!_commitMessageEdited)
            {
                // Keep a message typed before the files were saved.
                SetGeneratedCommitMessage(string.Empty);
            }

            return;
        }

        foreach (var change in preview.Changes)
        {
            Changes.Add(new ChangeItemViewModel(change));
        }

        HasChanges = true;
        ChangesHeader = Strings.Format(Strings.ChangesHeader, preview.Changes.Count);
        CommitModeText = preview.Mode == CommitMode.StagedOnly ? Strings.CommitModeStaged : Strings.CommitModeAll;
        CommitBlockedText = preview.BlockedBy == FlowError.None
            ? string.Empty
            : FlowMessages.Describe(FlowResult.Fail(preview.BlockedBy, preview.BlockedDetail));
        CanCommit = preview.CanCommit;
        if (!_commitMessageEdited)
        {
            SetGeneratedCommitMessage(preview.SuggestedMessage);
        }
    }

    private void SetGeneratedCommitMessage(string message)
    {
        _settingCommitMessage = true;
        try
        {
            CommitMessage = message;
            _commitMessageEdited = false;
        }
        finally
        {
            _settingCommitMessage = false;
        }
    }

    private void ClearBranches()
    {
        Features.Clear();
        Releases.Clear();
        Hotfixes.Clear();
        HasNoBranches = true;
    }

    /// <summary>Runs one flow operation from the window, then refreshes. Errors are shown by <see cref="FlowUi"/>.</summary>
    private async Task RunAsync(
        IClientContext? context,
        Func<FlowUi, string, CancellationToken, Task<FlowResult?>> operation,
        CancellationToken ct,
        Action? onSuccess = null)
    {
        var ui = new FlowUi(_extensibility, _session);
        try
        {
            var repository = _repository ?? await RepositoryLocator.FindAsync(_extensibility, _session.Flow, context, ct);
            if (repository is null)
            {
                await ui.ErrorAsync(Strings.NoRepository, ct);
                return;
            }

            IsIdle = false;
            StatusText = Strings.StatusWorking;
            var result = await operation(ui, repository, ct);
            StatusText = result is null ? string.Empty : FlowUi.Summarize(result);
            if (result?.IsSuccess == true)
            {
                onSuccess?.Invoke();
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            await ui.ReportUnexpectedAsync(ex);
        }
        finally
        {
            IsIdle = true;
            await RefreshAsync(context, CancellationToken.None);
            await UpdateStartSuggestionAsync();
        }
    }

    /// <summary>Pre-fills the next version for a release / hotfix (empty for a feature).</summary>
    private async Task UpdateStartSuggestionAsync()
    {
        try
        {
            var type = StartTypeOrder[Math.Clamp(SelectedStartTypeIndex, 0, 2)];
            var repository = _repository;
            var config = repository is null ? null : await _session.Flow.ConfigStore.ReadAsync(repository, Core.Config.ConfigScope.Local);
            StartPrefix = config?.GetPrefix(type) ?? string.Empty;
            NewBranchName = type == BranchType.Feature || repository is null || config is null
                ? string.Empty
                : await _session.Flow.SuggestVersionAsync(repository, type);
        }
        catch (Exception ex)
        {
            _session.Output.WriteLine(ex.ToString());
        }
    }

    private async Task OnRepositoryChangedAsync()
    {
        if (_visible && IsIdle)
        {
            await RefreshAsync(null, CancellationToken.None);
        }
    }

    /// <summary>Detects a solution switch (no git command involved, so the Output pane stays quiet).</summary>
    private async Task PollSolutionAsync()
    {
        try
        {
            if (!_visible || !IsIdle)
            {
                return;
            }

            var solution = await RepositoryLocator.GetSolutionPathAsync(_extensibility, CancellationToken.None);
            if (!string.Equals(solution, _solutionPath, StringComparison.OrdinalIgnoreCase))
            {
                await RefreshAsync(null, CancellationToken.None);
            }
        }
        catch (Exception ex)
        {
            _session.Output.WriteLine(ex.ToString());
        }
    }

    private static string TypeLabel(BranchType type) => type switch
    {
        BranchType.Feature => Strings.TypeFeature,
        BranchType.Release => Strings.TypeRelease,
        _ => Strings.TypeHotfix,
    };
}

/// <summary>One work branch in the list.</summary>
[DataContract]
internal sealed class BranchItemViewModel(FlowBranch branch, bool isCurrent, bool isMerged, AsyncCommand finish, AsyncCommand publish)
{
    [DataMember] public string Name { get; } = branch.Name;

    [DataMember] public string FullName { get; } = branch.FullName;

    [DataMember] public bool IsCurrent { get; } = isCurrent;

    [DataMember] public bool IsMerged { get; } = isMerged;

    [DataMember] public string Badge { get; } = string.Join("  ·  ", new[]
    {
        isCurrent ? Strings.BadgeCurrent : null,
        isMerged ? Strings.BadgeMerged : null,
    }.OfType<string>());

    [DataMember] public AsyncCommand FinishCommand { get; } = finish;

    [DataMember] public AsyncCommand PublishCommand { get; } = publish;

    [DataMember] public string FinishLabel => Strings.ButtonFinish;

    [DataMember] public string PublishLabel => Strings.ButtonPublish;
}

/// <summary>One changed file in the Commit section.</summary>
[DataContract]
internal sealed class ChangeItemViewModel(FileChange change)
{
    [DataMember] public string Status { get; } = change.Kind switch
    {
        ChangeKind.Added => "A",
        ChangeKind.Deleted => "D",
        ChangeKind.Renamed => "R",
        ChangeKind.Conflicted => "!",
        _ => "M",
    };

    [DataMember] public string Path { get; } = change.OldPath is null ? change.Path : $"{change.OldPath} → {change.Path}";

    [DataMember] public string KindLabel { get; } = change.Kind switch
    {
        ChangeKind.Added => Strings.ChangeAdded,
        ChangeKind.Deleted => Strings.ChangeDeleted,
        ChangeKind.Renamed => Strings.ChangeRenamed,
        ChangeKind.Conflicted => Strings.ChangeConflicted,
        _ => Strings.ChangeModified,
    };
}

/// <summary>Static texts of the window, from Resources/Strings.resx.</summary>
[DataContract]
internal sealed class ToolWindowLabels
{
    [DataMember] public string Refresh => Strings.ButtonRefresh;
    [DataMember] public string Branch => Strings.LabelBranch;
    [DataMember] public string Type => Strings.LabelType;
    [DataMember] public string State => Strings.LabelState;
    [DataMember] public string Sync => Strings.LabelSync;
    [DataMember] public string NoRepository => Strings.NoRepositoryHint;
    [DataMember] public string NotInitialized => Strings.NotInitializedHint;
    [DataMember] public string Init => Strings.ButtonInit;
    [DataMember] public string ReleaseReminder => FlowMessages.ReleaseReminder;
    [DataMember] public string Resume => Strings.ButtonResume;
    [DataMember] public string StartSection => Strings.SectionStart;
    [DataMember] public string Start => Strings.ButtonStart;
    [DataMember] public string BranchesSection => Strings.SectionBranches;
    [DataMember] public string Features => Strings.GroupFeatures;
    [DataMember] public string Releases => Strings.GroupReleases;
    [DataMember] public string Hotfixes => Strings.GroupHotfixes;
    [DataMember] public string NoBranches => Strings.NoBranches;
    [DataMember] public string CommitSection => Strings.SectionCommit;
    [DataMember] public string NoChanges => Strings.NoChanges;
    [DataMember] public string Commit => Strings.ButtonCommit;
    [DataMember] public string Regenerate => Strings.ButtonRegenerate;
}
