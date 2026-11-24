using GitFlow.Core.Model;

namespace GitFlow.Core.Config;

/// <summary>Standard git-flow configuration keys (stored in git config).</summary>
public static class GitFlowConfigKeys
{
    public const string MasterBranch = "gitflow.branch.master";
    public const string DevelopBranch = "gitflow.branch.develop";
    public const string FeaturePrefix = "gitflow.prefix.feature";
    public const string ReleasePrefix = "gitflow.prefix.release";
    public const string HotfixPrefix = "gitflow.prefix.hotfix";
    public const string VersionTagPrefix = "gitflow.prefix.versiontag";
}

public enum ConfigScope
{
    /// <summary>This repository (git config --local).</summary>
    Local,

    /// <summary>All repositories of the user (git config --global), used as defaults for new repositories.</summary>
    Global,
}

public enum ConfigValidationErrorKind
{
    MasterBranchEmpty,
    DevelopBranchEmpty,
    SameMasterAndDevelop,
    PrefixEmpty,
    DuplicatePrefix,
    InvalidBranchName,
    InvalidPrefix,
    InvalidTagPrefix,
}

public sealed record ConfigValidationError(ConfigValidationErrorKind Kind, string? Value = null);

/// <summary>
/// Naming of the flow. Only naming is configurable; the flow behaviour is not.
/// </summary>
public sealed record GitFlowConfig(
    string MasterBranch,
    string DevelopBranch,
    string FeaturePrefix,
    string ReleasePrefix,
    string HotfixPrefix,
    string VersionTagPrefix)
{
    public static GitFlowConfig Default { get; } = new("master", "develop", "feature/", "release/", "hotfix/", string.Empty);

    public string GetPrefix(BranchType type) => type switch
    {
        BranchType.Feature => FeaturePrefix,
        BranchType.Release => ReleasePrefix,
        BranchType.Hotfix => HotfixPrefix,
        _ => throw new ArgumentOutOfRangeException(nameof(type)),
    };

    /// <summary>Branch a work branch starts from: hotfix from master, everything else from develop.</summary>
    public string GetBaseBranch(BranchType type) => type == BranchType.Hotfix ? MasterBranch : DevelopBranch;

    public string GetTagName(string version) => VersionTagPrefix + version;

    /// <summary>Recognises a work branch from its full name, or returns null.</summary>
    public FlowBranch? TryParseFlowBranch(string? fullName)
    {
        if (string.IsNullOrEmpty(fullName))
        {
            return null;
        }

        // Longest prefix first, in case one prefix starts with another.
        foreach (var type in Enum.GetValues<BranchType>().OrderByDescending(t => GetPrefix(t).Length))
        {
            var prefix = GetPrefix(type);
            if (prefix.Length > 0 && fullName.Length > prefix.Length && fullName.StartsWith(prefix, StringComparison.Ordinal))
            {
                return new FlowBranch(type, fullName[prefix.Length..], fullName);
            }
        }

        return null;
    }

    /// <summary>Checks that do not need git (see <see cref="GitFlowConfigStore.ValidateAsync"/> for the full validation).</summary>
    public IReadOnlyList<ConfigValidationError> Validate()
    {
        var errors = new List<ConfigValidationError>();
        if (string.IsNullOrWhiteSpace(MasterBranch))
        {
            errors.Add(new(ConfigValidationErrorKind.MasterBranchEmpty));
        }

        if (string.IsNullOrWhiteSpace(DevelopBranch))
        {
            errors.Add(new(ConfigValidationErrorKind.DevelopBranchEmpty));
        }

        if (!string.IsNullOrWhiteSpace(MasterBranch) && string.Equals(MasterBranch, DevelopBranch, StringComparison.Ordinal))
        {
            errors.Add(new(ConfigValidationErrorKind.SameMasterAndDevelop, MasterBranch));
        }

        foreach (var type in Enum.GetValues<BranchType>())
        {
            if (string.IsNullOrWhiteSpace(GetPrefix(type)))
            {
                errors.Add(new(ConfigValidationErrorKind.PrefixEmpty, type.ToString()));
            }
        }

        var duplicates = Enum.GetValues<BranchType>()
            .Select(GetPrefix)
            .Where(p => !string.IsNullOrWhiteSpace(p))
            .GroupBy(p => p, StringComparer.Ordinal)
            .Where(g => g.Count() > 1)
            .Select(g => g.Key);
        errors.AddRange(duplicates.Select(p => new ConfigValidationError(ConfigValidationErrorKind.DuplicatePrefix, p)));

        return errors;
    }
}
