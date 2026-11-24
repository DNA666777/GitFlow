namespace GitFlow.Core.Git;

/// <summary>
/// Decides whether a file system change in a repository can change what GitFlow displays.
/// Files written by git's own read commands (index, *.lock, logs, objects) and build output are ignored,
/// so that refreshing the state never triggers another refresh.
/// </summary>
public static class RepositoryChangeFilter
{
    private static readonly string[] IgnoredFolders = ["bin", "obj", ".vs", "node_modules", "packages"];

    public static bool IsRelevant(string repositoryRoot, string fullPath)
    {
        var relative = Path.GetRelativePath(repositoryRoot, fullPath).Replace('\\', '/');
        if (relative.StartsWith("../", StringComparison.Ordinal) || relative == "..")
        {
            return false;
        }

        var segments = relative.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length == 0 || relative == ".")
        {
            return false;
        }

        if (segments[0] == ".git")
        {
            // Only what changes branches or the merge state.
            var inGit = string.Join('/', segments.Skip(1));
            return !inGit.EndsWith(".lock", StringComparison.Ordinal)
                && (inGit is "HEAD" or "MERGE_HEAD" or "packed-refs" || inGit.StartsWith("refs/", StringComparison.Ordinal));
        }

        return !segments.Any(s => IgnoredFolders.Contains(s, StringComparer.OrdinalIgnoreCase));
    }
}
