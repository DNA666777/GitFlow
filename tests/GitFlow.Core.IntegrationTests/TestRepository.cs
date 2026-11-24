using GitFlow.Core.Flow;
using GitFlow.Core.Git;

namespace GitFlow.Core.IntegrationTests;

/// <summary>
/// Temporary repository in %TEMP% (optionally with a bare "origin"), isolated from the user's git configuration.
/// </summary>
internal sealed class TestRepository : IAsyncDisposable
{
    private TestRepository(string root)
    {
        Root = root;
        WorkDir = Path.Combine(root, "work");
        OriginDir = Path.Combine(root, "origin.git");
        Log = new RecordingLog();
        Runner = new GitCliRunner(Log, new GitCliRunnerOptions
        {
            EnvironmentVariables = new Dictionary<string, string?>
            {
                ["GIT_CONFIG_GLOBAL"] = Path.Combine(root, "global.gitconfig"),
                ["GIT_CONFIG_NOSYSTEM"] = "1",
            },
        });
        Flow = new FlowService(Runner);
    }

    public string Root { get; }

    public string WorkDir { get; }

    public string OriginDir { get; }

    public GitCliRunner Runner { get; }

    public RecordingLog Log { get; }

    public FlowService Flow { get; }

    public static async Task<TestRepository> CreateAsync(bool withOrigin = true, string masterBranch = "master")
    {
        var repo = new TestRepository(Path.Combine(Path.GetTempPath(), "gitflow-tests", Guid.NewGuid().ToString("N")[..12]));
        Directory.CreateDirectory(repo.WorkDir);

        await repo.GitAsync("init", "-b", masterBranch);
        await repo.GitAsync("config", "user.name", "GitFlow Tests");
        await repo.GitAsync("config", "user.email", "tests@example.invalid");
        await repo.GitAsync("config", "commit.gpgsign", "false");
        await repo.GitAsync("config", "tag.gpgsign", "false");
        await repo.GitAsync("config", "core.autocrlf", "false");
        await repo.CommitFileAsync("README.md", "initial\n", "Initial commit");

        if (withOrigin)
        {
            Directory.CreateDirectory(repo.OriginDir);
            await repo.GitInAsync(repo.OriginDir, "init", "--bare", "-b", masterBranch);
            await repo.GitAsync("remote", "add", "origin", repo.OriginDir);
            await repo.GitAsync("push", "-u", "origin", masterBranch);
        }

        return repo;
    }

    public Task<string> GitAsync(params string[] args) => GitInAsync(WorkDir, args);

    public async Task<string> GitInAsync(string directory, params string[] args)
    {
        var result = await Runner.RunAsync(directory, args, CancellationToken.None);
        if (!result.Success)
        {
            throw new GitCommandException(result);
        }

        return result.StandardOutput.Trim();
    }

    public async Task<GitResult> TryGitAsync(params string[] args) =>
        await Runner.RunAsync(WorkDir, args, CancellationToken.None);

    public async Task<string> CommitFileAsync(string file, string content, string message)
    {
        await File.WriteAllTextAsync(Path.Combine(WorkDir, file), content);
        await GitAsync("add", "--", file);
        await GitAsync("commit", "-m", message);
        return await GitAsync("rev-parse", "HEAD");
    }

    public Task WriteFileAsync(string file, string content) => File.WriteAllTextAsync(Path.Combine(WorkDir, file), content);

    public Task<string> CurrentBranchAsync() => GitAsync("rev-parse", "--abbrev-ref", "HEAD");

    public async Task<IReadOnlyList<string>> LocalBranchesAsync() =>
        (await GitAsync("for-each-ref", "--format=%(refname:short)", "refs/heads/")).Split('\n', StringSplitOptions.RemoveEmptyEntries);

    public async Task<IReadOnlyList<string>> OriginBranchesAsync() =>
        (await GitInAsync(OriginDir, "for-each-ref", "--format=%(refname:short)", "refs/heads/")).Split('\n', StringSplitOptions.RemoveEmptyEntries);

    public async Task<bool> IsAncestorAsync(string ancestor, string descendant) =>
        (await TryGitAsync("merge-base", "--is-ancestor", ancestor, descendant)).Success;

    /// <summary>Parents of a commit (2 for a --no-ff merge commit).</summary>
    public async Task<string[]> ParentsAsync(string revision) =>
        (await GitAsync("rev-list", "--parents", "-n", "1", revision)).Split(' ')[1..];

    public async ValueTask DisposeAsync()
    {
        await Task.Yield();
        try
        {
            if (Directory.Exists(Root))
            {
                foreach (var file in Directory.EnumerateFiles(Root, "*", SearchOption.AllDirectories))
                {
                    File.SetAttributes(file, FileAttributes.Normal); // git objects are read-only
                }

                Directory.Delete(Root, recursive: true);
            }
        }
        catch (IOException)
        {
            // Best effort: a lingering git process may still hold a handle.
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    internal sealed class RecordingLog : IGitCommandLog
    {
        private readonly List<GitCommandLogEntry> _entries = [];

        public IReadOnlyList<GitCommandLogEntry> Entries
        {
            get
            {
                lock (_entries)
                {
                    return _entries.ToArray();
                }
            }
        }

        public void Log(GitCommandLogEntry entry)
        {
            lock (_entries)
            {
                _entries.Add(entry);
            }
        }
    }
}
