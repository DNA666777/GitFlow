namespace GitFlow.Core.Model;

/// <summary>The three work branch types of the standard Gitflow model. No other type exists.</summary>
public enum BranchType
{
    Feature,
    Release,
    Hotfix,
}

/// <summary>A work branch recognised by its prefix.</summary>
/// <param name="Type">Detected branch type.</param>
/// <param name="Name">Name without prefix (for release/hotfix: the version).</param>
/// <param name="FullName">Full local branch name, prefix included.</param>
public sealed record FlowBranch(BranchType Type, string Name, string FullName);
