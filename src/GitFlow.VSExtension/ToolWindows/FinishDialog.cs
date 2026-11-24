using System.Runtime.Serialization;
using GitFlow.Core.PullRequests;
using Microsoft.VisualStudio.Extensibility.UI;

namespace GitFlow.VSExtension.ToolWindows;

/// <summary>
/// Finish options: mode (local merge by default, Pull Request as an option), tag message (release/hotfix),
/// push at the end and delete the remote branch (local merge only).
/// </summary>
[DataContract]
internal sealed class FinishDialogViewModel : NotifyPropertyChangedObject
{
    private string _tagMessage;
    private bool _pushAfterFinish;
    private bool _deleteRemoteBranch;
    private bool _usePullRequest;

    public FinishDialogViewModel(string summary, bool showTagMessage, string tagLabel, string tagMessage, bool hasRemote, string? pullRequestUnavailable = null)
    {
        Summary = summary;
        ShowTagMessage = showTagMessage;
        TagLabel = tagLabel;
        _tagMessage = tagMessage;
        HasRemote = hasRemote;
        PullRequestUnavailableText = pullRequestUnavailable ?? string.Empty;
    }

    [DataMember] public string Summary { get; }

    [DataMember] public bool ShowTagMessage { get; }

    [DataMember] public string TagLabel { get; }

    [DataMember]
    public string TagMessage
    {
        get => _tagMessage;
        set => SetProperty(ref _tagMessage, value);
    }

    [DataMember] public bool HasRemote { get; }

    [DataMember] public bool ShowNoRemote => !HasRemote && !UsePullRequest;

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

    /// <summary>Pull Request mode is possible (origin is GitHub or Azure DevOps).</summary>
    [DataMember] public bool PullRequestAvailable => PullRequestUnavailableText.Length == 0;

    [DataMember] public string PullRequestUnavailableText { get; }

    [DataMember] public bool ShowPullRequestUnavailable => !PullRequestAvailable;

    [DataMember]
    public bool UsePullRequest
    {
        get => _usePullRequest;
        set
        {
            if (SetProperty(ref _usePullRequest, value && PullRequestAvailable))
            {
                RaiseNotifyPropertyChangedEvent(nameof(UseLocalMerge));
                RaiseNotifyPropertyChangedEvent(nameof(ShowLocalOptions));
                RaiseNotifyPropertyChangedEvent(nameof(ShowNoRemote));
            }
        }
    }

    [DataMember]
    public bool UseLocalMerge
    {
        get => !UsePullRequest;
        set => UsePullRequest = !value;
    }

    [DataMember] public bool ShowLocalOptions => !UsePullRequest;

    [DataMember] public bool ShowPullRequestNote => UsePullRequest;

    [DataMember] public string ModeLabel => Strings.FinishMode;

    [DataMember] public string LocalLabel => Strings.FinishModeLocal;

    [DataMember] public string PullRequestLabel => Strings.FinishModePullRequest;

    [DataMember] public string PullRequestNote => Strings.FinishPullRequestNote;

    [DataMember] public string PushLabel => Strings.FinishPushOption;

    [DataMember] public string DeleteRemoteLabel => Strings.FinishDeleteRemoteOption;

    [DataMember] public string NoRemoteText => Strings.FinishNoRemote;
}

internal sealed class FinishDialogControl(FinishDialogViewModel dataContext) : RemoteUserControl(dataContext);

/// <summary>Generated pull request messages, editable before creation (one per merge target).</summary>
[DataContract]
internal sealed class PullRequestDraftsViewModel(string intro, IEnumerable<PullRequestDraft> drafts)
{
    [DataMember] public string Intro { get; } = intro;

    [DataMember] public ObservableList<PullRequestDraftItem> Drafts { get; } = new(drafts.Select(d => new PullRequestDraftItem(d)));

    [DataMember] public string TitleLabel => Strings.PullRequestTitleLabel;

    [DataMember] public string DescriptionLabel => Strings.PullRequestDescriptionLabel;

    public IReadOnlyList<PullRequestDraft> ToDrafts() =>
        Drafts.Select(d => new PullRequestDraft(d.Source, d.Target, d.Title.Trim(), d.Description.Trim())).ToArray();
}

[DataContract]
internal sealed class PullRequestDraftItem(PullRequestDraft draft) : NotifyPropertyChangedObject
{
    private string _title = draft.Title;
    private string _description = draft.Description;

    [DataMember] public string Source { get; } = draft.Source;

    [DataMember] public string Target { get; } = draft.Target;

    [DataMember] public string Header { get; } = $"{draft.Source} → {draft.Target}";

    [DataMember]
    public string Title
    {
        get => _title;
        set => SetProperty(ref _title, value);
    }

    [DataMember]
    public string Description
    {
        get => _description;
        set => SetProperty(ref _description, value);
    }
}

internal sealed class PullRequestDraftsControl(PullRequestDraftsViewModel dataContext) : RemoteUserControl(dataContext);
