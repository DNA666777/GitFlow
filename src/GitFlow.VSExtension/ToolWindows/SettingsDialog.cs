using System.Runtime.Serialization;
using GitFlow.Core.Config;
using GitFlow.Core.Resources;
using Microsoft.VisualStudio.Extensibility.UI;

namespace GitFlow.VSExtension.ToolWindows;

/// <summary>
/// "Paramètres GitFlow" screen (also used by Init): naming, tag prefix, Finish defaults, scope.
/// Live validation uses the pure rules; git rules are checked on save.
/// </summary>
[DataContract]
internal sealed class SettingsDialogViewModel : NotifyPropertyChangedObject
{
    private readonly Func<ConfigScope, Task<GitFlowSettings>>? _loadScope;
    private string _masterBranch = string.Empty;
    private string _developBranch = string.Empty;
    private string _featurePrefix = string.Empty;
    private string _releasePrefix = string.Empty;
    private string _hotfixPrefix = string.Empty;
    private string _versionTagPrefix = string.Empty;
    private bool _pushAfterFinish;
    private bool _deleteRemoteBranch;
    private bool _usePullRequest;
    private bool _pruneOnComplete;
    private bool _isGlobalScope;
    private string _validationText = string.Empty;

    public SettingsDialogViewModel(GitFlowSettings settings, IReadOnlyList<string> branches, bool initMode, Func<ConfigScope, Task<GitFlowSettings>>? loadScope)
    {
        _loadScope = loadScope;
        IsInitMode = initMode;
        Branches = new ObservableList<string>(branches);
        Apply(settings);
        ResetCommand = new AsyncCommand((_, _) =>
        {
            Apply(GitFlowSettings.Default);
            return Task.CompletedTask;
        });
    }

    [DataMember] public bool IsInitMode { get; }

    [DataMember] public bool ShowScope => !IsInitMode;

    [DataMember] public ObservableList<string> Branches { get; }

    [DataMember] public AsyncCommand ResetCommand { get; }

    [DataMember] public SettingsLabels Labels { get; } = new();

    [DataMember]
    public string MasterBranch
    {
        get => _masterBranch;
        set => SetAndValidate(ref _masterBranch, value);
    }

    [DataMember]
    public string DevelopBranch
    {
        get => _developBranch;
        set => SetAndValidate(ref _developBranch, value);
    }

    [DataMember]
    public string FeaturePrefix
    {
        get => _featurePrefix;
        set => SetAndValidate(ref _featurePrefix, value);
    }

    [DataMember]
    public string ReleasePrefix
    {
        get => _releasePrefix;
        set => SetAndValidate(ref _releasePrefix, value);
    }

    [DataMember]
    public string HotfixPrefix
    {
        get => _hotfixPrefix;
        set => SetAndValidate(ref _hotfixPrefix, value);
    }

    [DataMember]
    public string VersionTagPrefix
    {
        get => _versionTagPrefix;
        set => SetAndValidate(ref _versionTagPrefix, value);
    }

    [DataMember]
    public bool PushAfterFinish
    {
        get => _pushAfterFinish;
        set => SetProperty(ref _pushAfterFinish, value);
    }

    [DataMember]
    public bool DeleteRemoteBranch
    {
        get => _deleteRemoteBranch;
        set => SetProperty(ref _deleteRemoteBranch, value);
    }

    /// <summary>Default Finish mode: Pull Request (option) instead of the local merge (default).</summary>
    [DataMember]
    public bool UsePullRequest
    {
        get => _usePullRequest;
        set
        {
            if (SetProperty(ref _usePullRequest, value))
            {
                RaiseNotifyPropertyChangedEvent(nameof(UseLocalMerge));
            }
        }
    }

    /// <summary>"Clore la branche" (PR mode) uses git fetch --prune.</summary>
    [DataMember]
    public bool PruneOnComplete
    {
        get => _pruneOnComplete;
        set => SetProperty(ref _pruneOnComplete, value);
    }

    [DataMember]
    public bool UseLocalMerge
    {
        get => !UsePullRequest;
        set => UsePullRequest = !value;
    }

    /// <summary>"Tous mes dépôts" (git config --global); otherwise "Ce dépôt".</summary>
    [DataMember]
    public bool IsGlobalScope
    {
        get => _isGlobalScope;
        set
        {
            if (SetProperty(ref _isGlobalScope, value))
            {
                RaiseNotifyPropertyChangedEvent(nameof(IsLocalScope));
                _ = ReloadScopeAsync();
            }
        }
    }

    [DataMember]
    public bool IsLocalScope
    {
        get => !_isGlobalScope;
        set => IsGlobalScope = !value;
    }

    public ConfigScope Scope => IsGlobalScope ? ConfigScope.Global : ConfigScope.Local;

    [DataMember]
    public string ValidationText
    {
        get => _validationText;
        set
        {
            if (SetProperty(ref _validationText, value))
            {
                RaiseNotifyPropertyChangedEvent(nameof(HasValidationErrors));
            }
        }
    }

    [DataMember] public bool HasValidationErrors => ValidationText.Length > 0;

    public GitFlowSettings ToSettings() => new(
        new GitFlowConfig(MasterBranch.Trim(), DevelopBranch.Trim(), FeaturePrefix.Trim(), ReleasePrefix.Trim(), HotfixPrefix.Trim(), VersionTagPrefix.Trim()),
        new FinishDefaults(PushAfterFinish, DeleteRemoteBranch, UsePullRequest, PruneOnComplete));

    private void Apply(GitFlowSettings settings)
    {
        _masterBranch = settings.Naming.MasterBranch;
        _developBranch = settings.Naming.DevelopBranch;
        _featurePrefix = settings.Naming.FeaturePrefix;
        _releasePrefix = settings.Naming.ReleasePrefix;
        _hotfixPrefix = settings.Naming.HotfixPrefix;
        _versionTagPrefix = settings.Naming.VersionTagPrefix;
        foreach (var name in new[] { nameof(MasterBranch), nameof(DevelopBranch), nameof(FeaturePrefix), nameof(ReleasePrefix), nameof(HotfixPrefix), nameof(VersionTagPrefix) })
        {
            RaiseNotifyPropertyChangedEvent(name);
        }

        PushAfterFinish = settings.Finish.PushAfterFinish;
        DeleteRemoteBranch = settings.Finish.DeleteRemoteBranch;
        UsePullRequest = settings.Finish.UsePullRequest;
        PruneOnComplete = settings.Finish.PruneOnComplete;
        Validate();
    }

    private void SetAndValidate(ref string field, string? value)
    {
        if (SetProperty(ref field, value ?? string.Empty))
        {
            Validate();
        }
    }

    private void Validate() =>
        ValidationText = string.Join(Environment.NewLine, ToSettings().Naming.Validate().Select(FlowMessages.Describe));

    private async Task ReloadScopeAsync()
    {
        if (_loadScope is not null)
        {
            Apply(await _loadScope(Scope));
        }
    }
}

[DataContract]
internal sealed class SettingsLabels
{
    [DataMember] public string Scope => Strings.SettingsScope;
    [DataMember] public string ScopeLocal => Strings.SettingsScopeLocal;
    [DataMember] public string ScopeGlobal => Strings.SettingsScopeGlobal;
    [DataMember] public string Branches => Strings.SettingsBranches;
    [DataMember] public string Master => Strings.SettingsMaster;
    [DataMember] public string Develop => Strings.SettingsDevelop;
    [DataMember] public string Prefixes => Strings.SettingsPrefixes;
    [DataMember] public string Feature => Strings.TypeFeature;
    [DataMember] public string Release => Strings.TypeRelease;
    [DataMember] public string Hotfix => Strings.TypeHotfix;
    [DataMember] public string TagPrefix => Strings.SettingsTagPrefix;
    [DataMember] public string TagPrefixHint => Strings.SettingsTagPrefixHint;
    [DataMember] public string FinishDefaults => Strings.SettingsFinishDefaults;
    [DataMember] public string Push => Strings.FinishPushOption;
    [DataMember] public string DeleteRemote => Strings.FinishDeleteRemoteOption;
    [DataMember] public string Reset => Strings.SettingsReset;
    [DataMember] public string RenameNote => Strings.SettingsRenameNote;
    [DataMember] public string FinishMode => Strings.SettingsFinishMode;
    [DataMember] public string ModeLocal => Strings.FinishModeLocal;
    [DataMember] public string ModePullRequest => Strings.FinishModePullRequest;
    [DataMember] public string Prune => Strings.SettingsPruneOnComplete;
}

internal sealed class SettingsDialogControl(SettingsDialogViewModel dataContext) : RemoteUserControl(dataContext);
