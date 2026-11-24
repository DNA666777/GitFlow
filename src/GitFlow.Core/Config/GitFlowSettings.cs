namespace GitFlow.Core.Config;

/// <summary>Default state of the Finish options (the user can still change them in the Finish dialog).</summary>
/// <param name="UsePullRequest">Pull Request mode pre-selected in the Finish dialog (optional mode; local merge by default).</param>
public sealed record FinishDefaults(bool PushAfterFinish, bool DeleteRemoteBranch, bool UsePullRequest = false)
{
    public static FinishDefaults None { get; } = new(false, false);
}

/// <summary>Everything the "Paramètres GitFlow" screen edits.</summary>
public sealed record GitFlowSettings(GitFlowConfig Naming, FinishDefaults Finish)
{
    public static GitFlowSettings Default { get; } = new(GitFlowConfig.Default, FinishDefaults.None);
}

/// <summary>Extension keys (not part of git-flow), kept apart from the gitflow.* namespace.</summary>
public static class GitFlowVsConfigKeys
{
    public const string FinishPush = "gitflowvs.finish.push";
    public const string FinishDeleteRemote = "gitflowvs.finish.deleteremote";

    /// <summary>"local" (default) or "pullrequest".</summary>
    public const string FinishMode = "gitflowvs.finish.mode";

    /// <summary>Regular expression for ticket references in PR descriptions (default: ABC-123 and #42).</summary>
    public const string TicketPattern = "gitflowvs.pr.ticketpattern";
}

public enum SettingsWarningKind
{
    /// <summary>Branches still use a prefix that is being changed: they are not renamed and will no longer be recognised.</summary>
    OrphanedBranches,

    /// <summary>Tags use the previous tag prefix: they will no longer be used to suggest versions.</summary>
    OldTagPrefix,
}

/// <param name="Prefix">The previous prefix.</param>
/// <param name="Items">The branches or tags concerned.</param>
public sealed record SettingsWarning(SettingsWarningKind Kind, string Prefix, IReadOnlyList<string> Items);
