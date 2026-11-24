namespace GitFlow.Core.Git;

/// <summary>
/// Typed queries and commands on one repository, built on top of <see cref="IGitRunner"/>.
/// Methods throw <see cref="GitCommandException"/> only when git fails unexpectedly.
/// </summary>
public sealed class GitClient(IGitRunner runner, string repositoryPath)
{
    private const string HeadsPrefix = "refs/heads/";

    public string RepositoryPath { get; } = repositoryPath;

    public Task<GitResult> RunAsync(CancellationToken ct, params string[] arguments) =>
        runner.RunAsync(RepositoryPath, arguments, ct);

    public async Task<GitResult> RunCheckedAsync(CancellationToken ct, params string[] arguments)
    {
        var result = await RunAsync(ct, arguments).ConfigureAwait(false);
        if (!result.Success)
        {
            throw new GitCommandException(result);
        }

        return result;
    }

    public async Task<bool> IsInsideWorkTreeAsync(CancellationToken ct)
    {
        var result = await RunAsync(ct, "rev-parse", "--is-inside-work-tree").ConfigureAwait(false);
        return result.Success && result.StandardOutput.Trim() == "true";
    }

    /// <summary>Root folder of the work tree containing <see cref="RepositoryPath"/>, or null outside a repository.</summary>
    public async Task<string?> GetTopLevelAsync(CancellationToken ct)
    {
        var result = await RunAsync(ct, "rev-parse", "--show-toplevel").ConfigureAwait(false);
        var path = result.StandardOutput.Trim();
        return result.Success && path.Length > 0 ? Path.GetFullPath(path) : null;
    }

    public async Task<bool> HasCommitsAsync(CancellationToken ct) =>
        await ResolveCommitAsync("HEAD", ct).ConfigureAwait(false) is not null;

    /// <summary>Current branch short name, or null when HEAD is detached.</summary>
    public async Task<string?> GetCurrentBranchAsync(CancellationToken ct)
    {
        var result = await RunAsync(ct, "symbolic-ref", "--quiet", "HEAD").ConfigureAwait(false);
        if (!result.Success)
        {
            return null;
        }

        var reference = result.StandardOutput.Trim();
        return reference.StartsWith(HeadsPrefix, StringComparison.Ordinal) ? reference[HeadsPrefix.Length..] : null;
    }

    public Task<bool> LocalBranchExistsAsync(string branch, CancellationToken ct) =>
        RefExistsAsync(HeadsPrefix + branch, ct);

    public Task<bool> RemoteBranchExistsAsync(string remote, string branch, CancellationToken ct) =>
        RefExistsAsync($"refs/remotes/{remote}/{branch}", ct);

    public Task<bool> TagExistsAsync(string tag, CancellationToken ct) =>
        RefExistsAsync("refs/tags/" + tag, ct);

    private async Task<bool> RefExistsAsync(string reference, CancellationToken ct) =>
        (await RunAsync(ct, "rev-parse", "--verify", "--quiet", reference).ConfigureAwait(false)).Success;

    /// <summary>Full commit id of a revision, or null if it does not resolve to a commit.</summary>
    public async Task<string?> ResolveCommitAsync(string revision, CancellationToken ct)
    {
        var result = await RunAsync(ct, "rev-parse", "--verify", "--quiet", revision + "^{commit}").ConfigureAwait(false);
        return result.Success ? result.StandardOutput.Trim() : null;
    }

    public async Task<IReadOnlyList<string>> GetLocalBranchesAsync(CancellationToken ct)
    {
        var result = await RunCheckedAsync(ct, "for-each-ref", "--format=%(refname)", HeadsPrefix).ConfigureAwait(false);
        return StripHeads(Lines(result.StandardOutput));
    }

    public async Task<IReadOnlyList<string>> GetLocalBranchesPointingAtAsync(string commit, CancellationToken ct)
    {
        var result = await RunCheckedAsync(ct, "for-each-ref", "--points-at", commit, "--format=%(refname)", HeadsPrefix).ConfigureAwait(false);
        return StripHeads(Lines(result.StandardOutput));
    }

    public async Task<bool> HasRemoteAsync(string remote, CancellationToken ct)
    {
        var result = await RunCheckedAsync(ct, "remote").ConfigureAwait(false);
        return Lines(result.StandardOutput).Contains(remote, StringComparer.Ordinal);
    }

    /// <summary>Validates a branch name with <c>git check-ref-format --branch</c>.</summary>
    public async Task<bool> IsValidBranchNameAsync(string branch, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(branch) || branch.StartsWith('-') || branch.StartsWith('@'))
        {
            return false;
        }

        return (await RunAsync(ct, "check-ref-format", "--branch", branch).ConfigureAwait(false)).Success;
    }

    /// <summary>Validates a full reference name (e.g. refs/tags/v1.0.0) with <c>git check-ref-format</c>.</summary>
    public async Task<bool> IsValidRefNameAsync(string reference, CancellationToken ct) =>
        (await RunAsync(ct, "check-ref-format", reference).ConfigureAwait(false)).Success;

    /// <summary>True when tracked files have staged or unstaged changes. Untracked files are ignored (as git-flow does).</summary>
    public async Task<bool> IsWorkingTreeDirtyAsync(CancellationToken ct)
    {
        var result = await RunCheckedAsync(ct, "status", "--porcelain=v1", "--untracked-files=no").ConfigureAwait(false);
        return result.StandardOutput.Trim().Length > 0;
    }

    /// <summary>All changes of the work tree (untracked files included, ignored files excluded).</summary>
    public async Task<IReadOnlyList<Model.FileChange>> GetChangesAsync(CancellationToken ct)
    {
        var result = await RunCheckedAsync(ct, "status", "--porcelain=v1", "-z", "--untracked-files=all").ConfigureAwait(false);
        return ParseStatus(result.StandardOutput);
    }

    /// <summary>Line statistics per path: of the index against HEAD (<paramref name="staged"/>) or of the work tree against HEAD.</summary>
    public async Task<IReadOnlyDictionary<string, Model.FileStat>> GetDiffStatsAsync(bool staged, CancellationToken ct)
    {
        if (!await HasCommitsAsync(ct).ConfigureAwait(false))
        {
            return new Dictionary<string, Model.FileStat>();
        }

        var result = staged
            ? await RunCheckedAsync(ct, "diff", "--cached", "--numstat", "-z", "-M", "HEAD").ConfigureAwait(false)
            : await RunCheckedAsync(ct, "diff", "--numstat", "-z", "-M", "HEAD").ConfigureAwait(false);
        return ParseNumstat(result.StandardOutput);
    }

    /// <summary>Parses <c>git status --porcelain=v1 -z</c>.</summary>
    internal static IReadOnlyList<Model.FileChange> ParseStatus(string output)
    {
        var changes = new List<Model.FileChange>();
        var entries = output.Split('\0');
        for (var i = 0; i < entries.Length; i++)
        {
            var entry = entries[i];
            if (entry.Length < 4)
            {
                continue;
            }

            char x = entry[0], y = entry[1];
            var path = entry[3..];
            string? oldPath = null;
            if (x is 'R' or 'C' || y is 'R' or 'C')
            {
                // Rename/copy: the next entry is the original path.
                oldPath = i + 1 < entries.Length ? entries[++i] : null;
            }

            var kind = (x, y) switch
            {
                ('?', '?') => Model.ChangeKind.Added,
                ('U', _) or (_, 'U') or ('A', 'A') or ('D', 'D') => Model.ChangeKind.Conflicted,
                ('R', _) or (_, 'R') => Model.ChangeKind.Renamed,
                ('A', _) or ('C', _) or (_, 'A') => Model.ChangeKind.Added,
                ('D', _) or (_, 'D') => Model.ChangeKind.Deleted,
                _ => Model.ChangeKind.Modified,
            };
            if (x == 'C')
            {
                oldPath = null; // a copy is a new file
            }

            var staged = x is not ' ' and not '?';
            var unstaged = y is not ' ';
            changes.Add(new Model.FileChange(kind, path, oldPath, staged, unstaged));
        }

        return changes;
    }

    /// <summary>Parses <c>git diff --numstat -z</c> (renames: "a\td\t\0old\0new\0").</summary>
    internal static IReadOnlyDictionary<string, Model.FileStat> ParseNumstat(string output)
    {
        var stats = new Dictionary<string, Model.FileStat>(StringComparer.Ordinal);
        var entries = output.Split('\0');
        for (var i = 0; i < entries.Length; i++)
        {
            var parts = entries[i].Split('\t');
            if (parts.Length < 3)
            {
                continue;
            }

            var stat = new Model.FileStat(ParseCount(parts[0]), ParseCount(parts[1]));
            var path = parts[2];
            if (path.Length == 0 && i + 2 < entries.Length)
            {
                i += 2; // rename: old path, then new path
                path = entries[i];
            }

            stats[path] = stat;
        }

        return stats;

        static int? ParseCount(string text) =>
            int.TryParse(text, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var n) ? n : null;
    }

    public async Task<IReadOnlyList<string>> GetConflictedFilesAsync(CancellationToken ct)
    {
        var result = await RunCheckedAsync(ct, "diff", "--name-only", "--diff-filter=U", "-z").ConfigureAwait(false);
        return result.StandardOutput.Split('\0', StringSplitOptions.RemoveEmptyEntries);
    }

    public Task<string?> GetMergeHeadAsync(CancellationToken ct) => ResolveCommitAsync("MERGE_HEAD", ct);

    public async Task<bool> IsAncestorAsync(string ancestor, string descendant, CancellationToken ct)
    {
        var result = await RunAsync(ct, "merge-base", "--is-ancestor", ancestor, descendant).ConfigureAwait(false);
        return result.ExitCode switch
        {
            0 => true,
            1 => false,
            _ => throw new GitCommandException(result),
        };
    }

    /// <summary>Full upstream reference of a local branch (e.g. refs/remotes/origin/develop), or null.</summary>
    public async Task<string?> GetUpstreamAsync(string branch, CancellationToken ct)
    {
        var result = await RunCheckedAsync(ct, "for-each-ref", "--format=%(upstream)", HeadsPrefix + branch).ConfigureAwait(false);
        var upstream = result.StandardOutput.Trim();
        return upstream.Length == 0 ? null : upstream;
    }

    public async Task<(int Ahead, int Behind)> GetAheadBehindAsync(string local, string upstream, CancellationToken ct)
    {
        var result = await RunCheckedAsync(ct, "rev-list", "--left-right", "--count", $"{local}...{upstream}").ConfigureAwait(false);
        var parts = result.StandardOutput.Split(['\t', ' '], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return (int.Parse(parts[0], System.Globalization.CultureInfo.InvariantCulture),
                int.Parse(parts[1], System.Globalization.CultureInfo.InvariantCulture));
    }

    public async Task<IReadOnlyList<string>> GetTagsMergedIntoAsync(string revision, string pattern, CancellationToken ct)
    {
        var result = await RunCheckedAsync(ct, "tag", "--list", "--merged", revision, pattern).ConfigureAwait(false);
        return Lines(result.StandardOutput);
    }

    internal static IReadOnlyList<string> Lines(string output) =>
        output.Split('\n', StringSplitOptions.RemoveEmptyEntries)
              .Select(l => l.TrimEnd('\r'))
              .Where(l => l.Length > 0)
              .ToArray();

    private static IReadOnlyList<string> StripHeads(IEnumerable<string> references) =>
        references.Where(r => r.StartsWith(HeadsPrefix, StringComparison.Ordinal))
                  .Select(r => r[HeadsPrefix.Length..])
                  .ToArray();
}
