using System.Runtime.Serialization;
using Microsoft.VisualStudio.Extensibility.UI;

namespace GitFlow.VSExtension.ToolWindows;

/// <summary>Finish options: tag message (release/hotfix), push at the end, delete the remote branch.</summary>
[DataContract]
internal sealed class FinishDialogViewModel : NotifyPropertyChangedObject
{
    private string _tagMessage;
    private bool _pushAfterFinish;
    private bool _deleteRemoteBranch;

    public FinishDialogViewModel(string summary, bool showTagMessage, string tagLabel, string tagMessage, bool hasRemote)
    {
        Summary = summary;
        ShowTagMessage = showTagMessage;
        TagLabel = tagLabel;
        _tagMessage = tagMessage;
        HasRemote = hasRemote;
    }

    [DataMember]
    public string Summary { get; }

    [DataMember]
    public bool ShowTagMessage { get; }

    [DataMember]
    public string TagLabel { get; }

    [DataMember]
    public string TagMessage
    {
        get => _tagMessage;
        set => SetProperty(ref _tagMessage, value);
    }

    [DataMember]
    public bool HasRemote { get; }

    [DataMember]
    public bool ShowNoRemote => !HasRemote;

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

    [DataMember]
    public string PushLabel => Strings.FinishPushOption;

    [DataMember]
    public string DeleteRemoteLabel => Strings.FinishDeleteRemoteOption;

    [DataMember]
    public string NoRemoteText => Strings.FinishNoRemote;
}

internal sealed class FinishDialogControl(FinishDialogViewModel dataContext) : RemoteUserControl(dataContext);
