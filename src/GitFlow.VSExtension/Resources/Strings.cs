using System.Globalization;
using System.Resources;

namespace GitFlow.VSExtension;

/// <summary>
/// User facing texts of the extension: English by default (Resources/Strings.resx), French translation (Resources/Strings.fr.resx),
/// chosen from the UI culture of the extension process.
/// </summary>
internal static class Strings
{
    // Strings.resx sits next to this class, so MSBuild embeds it under the class full name (GitFlow.VSExtension.Strings).
    private static readonly ResourceManager Resources = new(typeof(Strings));

    public static CultureInfo Culture => CultureInfo.CurrentUICulture;

    public static string Title => Get(nameof(Title));
    public static string OutputPaneTitle => Get(nameof(OutputPaneTitle));
    public static string OperationInProgress => Get(nameof(OperationInProgress));
    public static string NoRepository => Get(nameof(NoRepository));
    public static string UnexpectedError => Get(nameof(UnexpectedError));
    public static string WarningsHeader => Get(nameof(WarningsHeader));
    public static string ConflictedFiles => Get(nameof(ConflictedFiles));
    public static string ProgressInit => Get(nameof(ProgressInit));
    public static string ProgressStart => Get(nameof(ProgressStart));
    public static string ProgressFinish => Get(nameof(ProgressFinish));
    public static string ProgressPublish => Get(nameof(ProgressPublish));
    public static string InitAlreadyInitialized => Get(nameof(InitAlreadyInitialized));
    public static string InitSuccess => Get(nameof(InitSuccess));
    public static string StartAskFeature => Get(nameof(StartAskFeature));
    public static string StartAskRelease => Get(nameof(StartAskRelease));
    public static string StartAskHotfix => Get(nameof(StartAskHotfix));
    public static string StartSuccess => Get(nameof(StartSuccess));
    public static string StartReleaseNothingNew => Get(nameof(StartReleaseNothingNew));
    public static string FinishConfirmFeature => Get(nameof(FinishConfirmFeature));
    public static string FinishConfirmVersioned => Get(nameof(FinishConfirmVersioned));
    public static string FinishConfirmResume => Get(nameof(FinishConfirmResume));
    public static string FinishAskTagMessage => Get(nameof(FinishAskTagMessage));
    public static string FinishSuccess => Get(nameof(FinishSuccess));
    public static string FinishSuccessTagged => Get(nameof(FinishSuccessTagged));
    public static string PublishSuccess => Get(nameof(PublishSuccess));
    public static string FinishDialogTitle => Get(nameof(FinishDialogTitle));
    public static string FinishAlreadyMerged => Get(nameof(FinishAlreadyMerged));
    public static string FinishPushOption => Get(nameof(FinishPushOption));
    public static string FinishDeleteRemoteOption => Get(nameof(FinishDeleteRemoteOption));
    public static string FinishNoRemote => Get(nameof(FinishNoRemote));
    public static string ToolWindowTitle => Get(nameof(ToolWindowTitle));
    public static string ButtonRefresh => Get(nameof(ButtonRefresh));
    public static string ButtonInit => Get(nameof(ButtonInit));
    public static string ButtonStart => Get(nameof(ButtonStart));
    public static string ButtonFinish => Get(nameof(ButtonFinish));
    public static string ButtonPublish => Get(nameof(ButtonPublish));
    public static string ButtonResume => Get(nameof(ButtonResume));
    public static string LabelBranch => Get(nameof(LabelBranch));
    public static string LabelType => Get(nameof(LabelType));
    public static string LabelState => Get(nameof(LabelState));
    public static string LabelSync => Get(nameof(LabelSync));
    public static string NoRepositoryHint => Get(nameof(NoRepositoryHint));
    public static string NotInitializedHint => Get(nameof(NotInitializedHint));
    public static string SectionStart => Get(nameof(SectionStart));
    public static string SectionBranches => Get(nameof(SectionBranches));
    public static string GroupFeatures => Get(nameof(GroupFeatures));
    public static string GroupReleases => Get(nameof(GroupReleases));
    public static string GroupHotfixes => Get(nameof(GroupHotfixes));
    public static string NoBranches => Get(nameof(NoBranches));
    public static string TypeFeature => Get(nameof(TypeFeature));
    public static string TypeRelease => Get(nameof(TypeRelease));
    public static string TypeHotfix => Get(nameof(TypeHotfix));
    public static string TypeOther => Get(nameof(TypeOther));
    public static string DetachedHead => Get(nameof(DetachedHead));
    public static string StateClean => Get(nameof(StateClean));
    public static string StateDirty => Get(nameof(StateDirty));
    public static string StateMerging => Get(nameof(StateMerging));
    public static string SyncFormat => Get(nameof(SyncFormat));
    public static string SyncNoUpstream => Get(nameof(SyncNoUpstream));
    public static string SyncNoRemote => Get(nameof(SyncNoRemote));
    public static string PendingFinish => Get(nameof(PendingFinish));
    public static string BadgeCurrent => Get(nameof(BadgeCurrent));
    public static string BadgeMerged => Get(nameof(BadgeMerged));

    public static string BadgeNoCommit => Get(nameof(BadgeNoCommit));

    public static string FinishWithoutCommit => Get(nameof(FinishWithoutCommit));
    public static string StatusWorking => Get(nameof(StatusWorking));
    public static string FinishMode => Get(nameof(FinishMode));
    public static string FinishModeLocal => Get(nameof(FinishModeLocal));
    public static string FinishModePullRequest => Get(nameof(FinishModePullRequest));
    public static string FinishPullRequestNote => Get(nameof(FinishPullRequestNote));
    public static string FinishPullRequestNotNow => Get(nameof(FinishPullRequestNotNow));
    public static string PullRequestDraftsTitle => Get(nameof(PullRequestDraftsTitle));
    public static string PullRequestDraftsIntro => Get(nameof(PullRequestDraftsIntro));
    public static string PullRequestTitleLabel => Get(nameof(PullRequestTitleLabel));
    public static string PullRequestDescriptionLabel => Get(nameof(PullRequestDescriptionLabel));
    public static string ProgressPullRequests => Get(nameof(ProgressPullRequests));
    public static string PullRequestsCreated => Get(nameof(PullRequestsCreated));
    public static string PullRequestsTagLater => Get(nameof(PullRequestsTagLater));
    public static string PullRequestsOpenAsk => Get(nameof(PullRequestsOpenAsk));
    public static string ProgressTag => Get(nameof(ProgressTag));
    public static string TagDone => Get(nameof(TagDone));
    public static string ProgressComplete => Get(nameof(ProgressComplete));
    public static string CompleteDone => Get(nameof(CompleteDone));
    public static string SectionPullRequests => Get(nameof(SectionPullRequests));
    public static string NoPullRequests => Get(nameof(NoPullRequests));
    public static string ButtonRefreshPullRequests => Get(nameof(ButtonRefreshPullRequests));
    public static string ButtonOpen => Get(nameof(ButtonOpen));
    public static string ButtonTag => Get(nameof(ButtonTag));
    public static string ButtonComplete => Get(nameof(ButtonComplete));
    public static string PrStateOpen => Get(nameof(PrStateOpen));
    public static string PrStateMerged => Get(nameof(PrStateMerged));
    public static string PrStateClosed => Get(nameof(PrStateClosed));
    public static string PrStateUnknown => Get(nameof(PrStateUnknown));
    public static string SettingsFinishMode => Get(nameof(SettingsFinishMode));
    public static string SettingsPruneOnComplete => Get(nameof(SettingsPruneOnComplete));

    public static string SettingsAzureDevOpsServer => Get(nameof(SettingsAzureDevOpsServer));

    public static string SettingsAzureDevOpsServerHint => Get(nameof(SettingsAzureDevOpsServerHint));
    public static string ButtonSettings => Get(nameof(ButtonSettings));
    public static string SettingsDialogTitle => Get(nameof(SettingsDialogTitle));
    public static string InitDialogTitle => Get(nameof(InitDialogTitle));
    public static string ProgressSettings => Get(nameof(ProgressSettings));
    public static string SettingsScope => Get(nameof(SettingsScope));
    public static string SettingsScopeLocal => Get(nameof(SettingsScopeLocal));
    public static string SettingsScopeGlobal => Get(nameof(SettingsScopeGlobal));
    public static string SettingsBranches => Get(nameof(SettingsBranches));
    public static string SettingsMaster => Get(nameof(SettingsMaster));
    public static string SettingsDevelop => Get(nameof(SettingsDevelop));
    public static string SettingsPrefixes => Get(nameof(SettingsPrefixes));
    public static string SettingsTagPrefix => Get(nameof(SettingsTagPrefix));
    public static string SettingsTagPrefixHint => Get(nameof(SettingsTagPrefixHint));
    public static string SettingsRenameNote => Get(nameof(SettingsRenameNote));
    public static string SettingsFinishDefaults => Get(nameof(SettingsFinishDefaults));
    public static string SettingsReset => Get(nameof(SettingsReset));
    public static string SettingsConfirmWarnings => Get(nameof(SettingsConfirmWarnings));
    public static string SettingsSavedLocal => Get(nameof(SettingsSavedLocal));
    public static string SettingsSavedGlobal => Get(nameof(SettingsSavedGlobal));
    public static string SectionCommit => Get(nameof(SectionCommit));
    public static string ChangesHeader => Get(nameof(ChangesHeader));
    public static string NoChanges => Get(nameof(NoChanges));
    public static string CommitModeStaged => Get(nameof(CommitModeStaged));
    public static string CommitModeAll => Get(nameof(CommitModeAll));
    public static string ButtonCommit => Get(nameof(ButtonCommit));
    public static string ButtonRegenerate => Get(nameof(ButtonRegenerate));
    public static string ProgressCommit => Get(nameof(ProgressCommit));
    public static string ChangeAdded => Get(nameof(ChangeAdded));
    public static string ChangeModified => Get(nameof(ChangeModified));
    public static string ChangeDeleted => Get(nameof(ChangeDeleted));
    public static string ChangeRenamed => Get(nameof(ChangeRenamed));
    public static string ChangeConflicted => Get(nameof(ChangeConflicted));
    public static string StatusSucceeded => Get(nameof(StatusSucceeded));
    public static string StatusSucceededWithWarnings => Get(nameof(StatusSucceededWithWarnings));

    public static string Format(string format, params object?[] args) => string.Format(Culture, format, args);

    /// <summary>Never throws: a missing resource must not prevent an error from being shown.</summary>
    private static string Get(string key)
    {
        try
        {
            return Resources.GetString(key, Culture) ?? key;
        }
        catch (MissingManifestResourceException)
        {
            return key;
        }
    }
}
