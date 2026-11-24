using GitFlow.Core.Git;
using GitFlow.Core.Model;

namespace GitFlow.Core.Config;

/// <summary>Reads and writes the gitflow.* keys in git config.</summary>
public sealed class GitFlowConfigStore(IGitRunner runner)
{
    /// <summary>Raw gitflow.* values of one scope (keys in lower case). Empty when none are set.</summary>
    public async Task<IReadOnlyDictionary<string, string>> ReadRawAsync(string repositoryPath, ConfigScope scope, CancellationToken ct = default)
    {
        var result = await runner.RunAsync(
            repositoryPath,
            ["config", ScopeFlag(scope), "--null", "--get-regexp", @"^gitflow\."],
            ct).ConfigureAwait(false);

        return result.ExitCode switch
        {
            0 => ParseNullSeparated(result.StandardOutput),
            1 => new Dictionary<string, string>(), // no matching key
            _ => throw new GitCommandException(result),
        };
    }

    /// <summary>Configuration of a scope, or null when master/develop are not defined there (not initialised).</summary>
    public async Task<GitFlowConfig?> ReadAsync(string repositoryPath, ConfigScope scope, CancellationToken ct = default)
    {
        var raw = await ReadRawAsync(repositoryPath, scope, ct).ConfigureAwait(false);
        if (!raw.ContainsKey(GitFlowConfigKeys.MasterBranch) || !raw.ContainsKey(GitFlowConfigKeys.DevelopBranch))
        {
            return null;
        }

        return Merge(GitFlowConfig.Default, raw);
    }

    /// <summary>
    /// Values to pre-fill the Init screen: the existing local configuration as is,
    /// otherwise the global defaults of the user, otherwise the built-in defaults.
    /// </summary>
    public async Task<GitFlowConfig> GetInitDefaultsAsync(string repositoryPath, CancellationToken ct = default)
    {
        var local = await ReadRawAsync(repositoryPath, ConfigScope.Local, ct).ConfigureAwait(false);
        if (local.Count > 0)
        {
            var global = await ReadRawAsync(repositoryPath, ConfigScope.Global, ct).ConfigureAwait(false);
            return Merge(Merge(GitFlowConfig.Default, global), local);
        }

        var globalOnly = await ReadRawAsync(repositoryPath, ConfigScope.Global, ct).ConfigureAwait(false);
        return Merge(GitFlowConfig.Default, globalOnly);
    }

    public async Task WriteAsync(string repositoryPath, GitFlowConfig config, ConfigScope scope, CancellationToken ct = default)
    {
        var git = new GitClient(runner, repositoryPath);
        foreach (var (key, value) in ToPairs(config))
        {
            await git.RunCheckedAsync(ct, "config", ScopeFlag(scope), key, value).ConfigureAwait(false);
        }
    }

    /// <summary>Full validation: pure rules plus git's own reference name rules.</summary>
    public async Task<IReadOnlyList<ConfigValidationError>> ValidateAsync(string repositoryPath, GitFlowConfig config, CancellationToken ct = default)
    {
        var errors = config.Validate().ToList();
        var git = new GitClient(runner, repositoryPath);

        foreach (var branch in new[] { config.MasterBranch, config.DevelopBranch })
        {
            if (!string.IsNullOrWhiteSpace(branch) && !await git.IsValidBranchNameAsync(branch, ct).ConfigureAwait(false))
            {
                errors.Add(new(ConfigValidationErrorKind.InvalidBranchName, branch));
            }
        }

        foreach (var type in Enum.GetValues<BranchType>())
        {
            var prefix = config.GetPrefix(type);
            if (!string.IsNullOrWhiteSpace(prefix) && !await git.IsValidBranchNameAsync(prefix + "name", ct).ConfigureAwait(false))
            {
                errors.Add(new(ConfigValidationErrorKind.InvalidPrefix, prefix));
            }
        }

        if (!await git.IsValidRefNameAsync("refs/tags/" + config.VersionTagPrefix + "1.0.0", ct).ConfigureAwait(false))
        {
            errors.Add(new(ConfigValidationErrorKind.InvalidTagPrefix, config.VersionTagPrefix));
        }

        return errors;
    }

    internal static IReadOnlyDictionary<string, string> ParseNullSeparated(string output)
    {
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var entry in output.Split('\0', StringSplitOptions.RemoveEmptyEntries))
        {
            var newline = entry.IndexOf('\n');
            var key = (newline < 0 ? entry : entry[..newline]).Trim().ToLowerInvariant();
            var value = newline < 0 ? string.Empty : entry[(newline + 1)..];
            if (key.Length > 0)
            {
                values[key] = value; // last one wins, as git does
            }
        }

        return values;
    }

    internal static GitFlowConfig Merge(GitFlowConfig baseConfig, IReadOnlyDictionary<string, string> raw)
    {
        string Get(string key, string fallback) => raw.TryGetValue(key, out var value) ? value : fallback;

        return new GitFlowConfig(
            Get(GitFlowConfigKeys.MasterBranch, baseConfig.MasterBranch),
            Get(GitFlowConfigKeys.DevelopBranch, baseConfig.DevelopBranch),
            Get(GitFlowConfigKeys.FeaturePrefix, baseConfig.FeaturePrefix),
            Get(GitFlowConfigKeys.ReleasePrefix, baseConfig.ReleasePrefix),
            Get(GitFlowConfigKeys.HotfixPrefix, baseConfig.HotfixPrefix),
            Get(GitFlowConfigKeys.VersionTagPrefix, baseConfig.VersionTagPrefix));
    }

    private static IEnumerable<(string Key, string Value)> ToPairs(GitFlowConfig config)
    {
        yield return (GitFlowConfigKeys.MasterBranch, config.MasterBranch);
        yield return (GitFlowConfigKeys.DevelopBranch, config.DevelopBranch);
        yield return (GitFlowConfigKeys.FeaturePrefix, config.FeaturePrefix);
        yield return (GitFlowConfigKeys.ReleasePrefix, config.ReleasePrefix);
        yield return (GitFlowConfigKeys.HotfixPrefix, config.HotfixPrefix);
        yield return (GitFlowConfigKeys.VersionTagPrefix, config.VersionTagPrefix);
    }

    private static string ScopeFlag(ConfigScope scope) => scope == ConfigScope.Global ? "--global" : "--local";
}
