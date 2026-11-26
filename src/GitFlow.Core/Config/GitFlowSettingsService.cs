using GitFlow.Core.Git;
using GitFlow.Core.Model;

namespace GitFlow.Core.Config;

/// <summary>
/// Loads, checks and saves the settings screen: naming (gitflow.* keys) and Finish defaults (gitflowvs.* keys),
/// for this repository (local) or for all the user's repositories (global).
/// </summary>
public sealed class GitFlowSettingsService(IGitRunner runner)
{
    private readonly GitFlowConfigStore _store = new(runner);

    public GitFlowConfigStore ConfigStore => _store;

    /// <summary>
    /// Values to show for a scope. Local: the repository configuration as it applies (local, else global, else defaults).
    /// Global: the user's defaults, else the built-in defaults.
    /// </summary>
    public async Task<GitFlowSettings> LoadAsync(string repositoryPath, ConfigScope scope, CancellationToken ct = default)
    {
        GitFlowConfig naming;
        if (scope == ConfigScope.Local)
        {
            naming = await _store.GetInitDefaultsAsync(repositoryPath, ct).ConfigureAwait(false);
        }
        else
        {
            var global = await _store.ReadRawAsync(repositoryPath, ConfigScope.Global, ct).ConfigureAwait(false);
            naming = GitFlowConfigStore.Merge(GitFlowConfig.Default, global);
        }

        return new GitFlowSettings(naming, await ReadFinishDefaultsAsync(repositoryPath, scope, ct).ConfigureAwait(false));
    }

    /// <summary>
    /// Finish defaults of a scope; for <see cref="ConfigScope.Local"/>, falls back to the global value of each option.
    /// </summary>
    public async Task<FinishDefaults> ReadFinishDefaultsAsync(string repositoryPath, ConfigScope scope, CancellationToken ct = default)
    {
        var values = new Dictionary<string, string>(
            await ReadVsKeysAsync(repositoryPath, ConfigScope.Global, ct).ConfigureAwait(false), StringComparer.Ordinal);
        if (scope == ConfigScope.Local)
        {
            var local = await ReadVsKeysAsync(repositoryPath, ConfigScope.Local, ct).ConfigureAwait(false);
            foreach (var (key, value) in local)
            {
                values[key] = value;
            }
        }

        return new FinishDefaults(
            ParseBool(values, GitFlowVsConfigKeys.FinishPush),
            ParseBool(values, GitFlowVsConfigKeys.FinishDeleteRemote),
            UsePullRequest: values.TryGetValue(GitFlowVsConfigKeys.FinishMode, out var mode) && mode.Trim().Equals("pullrequest", StringComparison.OrdinalIgnoreCase),
            PruneOnComplete: ParseBool(values, GitFlowVsConfigKeys.PruneOnComplete),
            AzureDevOpsServer: values.TryGetValue(GitFlowVsConfigKeys.Platform, out var platform)
                && platform.Trim().Equals(GitFlowVsConfigKeys.PlatformAzureDevOps, StringComparison.OrdinalIgnoreCase));
    }

    public async Task WriteFinishDefaultsAsync(string repositoryPath, FinishDefaults defaults, ConfigScope scope, CancellationToken ct = default)
    {
        var git = new GitClient(runner, repositoryPath);
        var flag = scope == ConfigScope.Global ? "--global" : "--local";
        await git.RunCheckedAsync(ct, "config", flag, GitFlowVsConfigKeys.FinishPush, defaults.PushAfterFinish ? "true" : "false").ConfigureAwait(false);
        await git.RunCheckedAsync(ct, "config", flag, GitFlowVsConfigKeys.FinishDeleteRemote, defaults.DeleteRemoteBranch ? "true" : "false").ConfigureAwait(false);
        await git.RunCheckedAsync(ct, "config", flag, GitFlowVsConfigKeys.FinishMode, defaults.UsePullRequest ? "pullrequest" : "local").ConfigureAwait(false);
        await git.RunCheckedAsync(ct, "config", flag, GitFlowVsConfigKeys.PruneOnComplete, defaults.PruneOnComplete ? "true" : "false").ConfigureAwait(false);
        await git.RunCheckedAsync(
            ct, "config", flag, GitFlowVsConfigKeys.Platform,
            defaults.AzureDevOpsServer ? GitFlowVsConfigKeys.PlatformAzureDevOps : GitFlowVsConfigKeys.PlatformAuto).ConfigureAwait(false);
    }

    /// <summary>
    /// Non-blocking consequences of a naming change in this repository: branches left with an old prefix
    /// (they are NOT renamed) and tags with the old tag prefix.
    /// </summary>
    public async Task<IReadOnlyList<SettingsWarning>> GetWarningsAsync(
        string repositoryPath, GitFlowConfig current, GitFlowConfig proposed, CancellationToken ct = default)
    {
        var git = new GitClient(runner, repositoryPath);
        var warnings = new List<SettingsWarning>();
        var branches = await git.HasCommitsAsync(ct).ConfigureAwait(false)
            ? await git.GetLocalBranchesAsync(ct).ConfigureAwait(false)
            : [];

        foreach (var type in Enum.GetValues<BranchType>())
        {
            var oldPrefix = current.GetPrefix(type);
            if (oldPrefix.Length == 0 || oldPrefix == proposed.GetPrefix(type))
            {
                continue;
            }

            // Branches still recognised with the new naming are fine.
            var orphans = branches
                .Where(b => b.StartsWith(oldPrefix, StringComparison.Ordinal) && proposed.TryParseFlowBranch(b) is null)
                .ToArray();
            if (orphans.Length > 0)
            {
                warnings.Add(new SettingsWarning(SettingsWarningKind.OrphanedBranches, oldPrefix, orphans));
            }
        }

        if (current.VersionTagPrefix != proposed.VersionTagPrefix && await git.HasCommitsAsync(ct).ConfigureAwait(false))
        {
            var tags = (await git.RunCheckedAsync(ct, "tag", "--list").ConfigureAwait(false)).StandardOutput
                .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Where(t => t.StartsWith(current.VersionTagPrefix, StringComparison.Ordinal)
                    && SemanticVersion.TryParse(t[current.VersionTagPrefix.Length..], out _)
                    && !(proposed.VersionTagPrefix.Length > 0 && t.StartsWith(proposed.VersionTagPrefix, StringComparison.Ordinal)))
                .ToArray();
            if (tags.Length > 0)
            {
                warnings.Add(new SettingsWarning(SettingsWarningKind.OldTagPrefix, current.VersionTagPrefix, tags));
            }
        }

        return warnings;
    }

    /// <summary>
    /// Saves the settings of an INITIALISED repository (local) or the user's defaults (global).
    /// Initialising a repository goes through <see cref="Flow.FlowService.InitAsync"/>.
    /// </summary>
    public async Task<FlowResult> SaveAsync(string repositoryPath, GitFlowSettings settings, ConfigScope scope, CancellationToken ct = default)
    {
        try
        {
            var errors = await _store.ValidateAsync(repositoryPath, settings.Naming, ct).ConfigureAwait(false);
            if (errors.Count > 0)
            {
                return FlowResult.InvalidConfig(errors);
            }

            if (scope == ConfigScope.Local)
            {
                if (await _store.ReadAsync(repositoryPath, ConfigScope.Local, ct).ConfigureAwait(false) is null)
                {
                    return FlowResult.Fail(FlowError.NotInitialized);
                }

                // The permanent branches must exist: nothing is created or renamed here.
                var git = new GitClient(runner, repositoryPath);
                foreach (var branch in new[] { settings.Naming.MasterBranch, settings.Naming.DevelopBranch })
                {
                    if (!await git.LocalBranchExistsAsync(branch, ct).ConfigureAwait(false))
                    {
                        return FlowResult.Fail(FlowError.BaseBranchNotFound, branch);
                    }
                }
            }

            await _store.WriteAsync(repositoryPath, settings.Naming, scope, ct).ConfigureAwait(false);
            await WriteFinishDefaultsAsync(repositoryPath, settings.Finish, scope, ct).ConfigureAwait(false);
            return FlowResult.Ok();
        }
        catch (GitCommandException ex)
        {
            return FlowResult.Fail(FlowError.GitCommandFailed, ex.Result.StandardError.Trim(), ex.Result);
        }
    }

    /// <summary>Local branch names, for the drop-down lists of the screen.</summary>
    public async Task<IReadOnlyList<string>> GetLocalBranchesAsync(string repositoryPath, CancellationToken ct = default)
    {
        var git = new GitClient(runner, repositoryPath);
        return await git.HasCommitsAsync(ct).ConfigureAwait(false) ? await git.GetLocalBranchesAsync(ct).ConfigureAwait(false) : [];
    }

    private async Task<IReadOnlyDictionary<string, string>> ReadVsKeysAsync(string repositoryPath, ConfigScope scope, CancellationToken ct)
    {
        var result = await runner.RunAsync(
            repositoryPath,
            ["config", scope == ConfigScope.Global ? "--global" : "--local", "--null", "--get-regexp", @"^gitflowvs\."],
            ct).ConfigureAwait(false);
        return result.ExitCode switch
        {
            0 => GitFlowConfigStore.ParseNullSeparated(result.StandardOutput),
            1 => new Dictionary<string, string>(),
            _ => throw new GitCommandException(result),
        };
    }

    private static bool ParseBool(IReadOnlyDictionary<string, string> values, string key) =>
        values.TryGetValue(key, out var value)
        && value.Trim().ToLowerInvariant() is "true" or "yes" or "on" or "1" or "";
}
