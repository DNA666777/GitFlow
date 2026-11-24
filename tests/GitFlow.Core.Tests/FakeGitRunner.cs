using GitFlow.Core.Git;

namespace GitFlow.Core.Tests;

/// <summary>
/// Scripted <see cref="IGitRunner"/>: simulates refs, ancestry and command outputs, and records every call.
/// Unknown commands succeed with an empty output.
/// </summary>
internal sealed class FakeGitRunner : IGitRunner
{
    private readonly List<(Func<IReadOnlyList<string>, bool> Match, Func<IReadOnlyList<string>, GitResult> Respond)> _rules = [];

    /// <summary>Existing refs (refs/heads/x, refs/remotes/origin/x, refs/tags/x, MERGE_HEAD) → commit id.</summary>
    public Dictionary<string, string> Refs { get; } = new(StringComparer.Ordinal);

    /// <summary>(ancestor, descendant) pairs for which merge-base --is-ancestor succeeds.</summary>
    public HashSet<(string Ancestor, string Descendant)> Ancestors { get; } = [];

    public List<IReadOnlyList<string>> Calls { get; } = [];

    public string? CurrentBranch { get; set; }

    public string Status { get; set; } = string.Empty;

    public List<string> Remotes { get; } = [];

    public List<string> ConflictedFiles { get; } = [];

    public Dictionary<string, string> Config { get; } = new(StringComparer.Ordinal);

    public static readonly string[] MutatingCommands = ["switch", "merge", "tag", "branch", "commit", "push", "fetch", "config", "add"];

    /// <summary>Calls that change something (reads such as "tag --list" or "config --get-regexp" excluded).</summary>
    public IReadOnlyList<string> MutatingCalls => Calls
        .Where(c => MutatingCommands.Contains(c[0]))
        .Where(c => !(c[0] == "tag" && c.Contains("--list")))
        .Where(c => !(c[0] == "config" && c.Contains("--get-regexp")))
        .Select(c => string.Join(' ', c))
        .ToArray();

    public FakeGitRunner On(string commandLine, int exitCode = 0, string stdout = "", string stderr = "")
    {
        _rules.Add((args => string.Join(' ', args) == commandLine, args => new GitResult(args, exitCode, stdout, stderr)));
        return this;
    }

    public FakeGitRunner OnPrefix(string commandPrefix, int exitCode = 0, string stdout = "", string stderr = "")
    {
        _rules.Add((args => string.Join(' ', args).StartsWith(commandPrefix, StringComparison.Ordinal), args => new GitResult(args, exitCode, stdout, stderr)));
        return this;
    }

    public FakeGitRunner AddBranch(string name, string commit = "c0")
    {
        Refs["refs/heads/" + name] = commit;
        return this;
    }

    public FakeGitRunner Merged(string branch, string into)
    {
        Ancestors.Add(("refs/heads/" + branch, "refs/heads/" + into));
        return this;
    }

    /// <summary>Initialised repository with default naming, master/develop, on develop, clean.</summary>
    public static FakeGitRunner Initialized(bool withRemote = false)
    {
        var fake = new FakeGitRunner();
        fake.Config["gitflow.branch.master"] = "master";
        fake.Config["gitflow.branch.develop"] = "develop";
        fake.Config["gitflow.prefix.feature"] = "feature/";
        fake.Config["gitflow.prefix.release"] = "release/";
        fake.Config["gitflow.prefix.hotfix"] = "hotfix/";
        fake.Config["gitflow.prefix.versiontag"] = "";
        fake.AddBranch("master", "m1").AddBranch("develop", "d1");
        fake.Refs["HEAD"] = "d1";
        fake.CurrentBranch = "develop";
        if (withRemote)
        {
            fake.Remotes.Add("origin");
        }

        return fake;
    }

    public Task<GitResult> RunAsync(string workingDirectory, IReadOnlyList<string> arguments, CancellationToken cancellationToken)
    {
        Calls.Add(arguments.ToArray());
        for (var i = _rules.Count - 1; i >= 0; i--)
        {
            if (_rules[i].Match(arguments))
            {
                return Task.FromResult(_rules[i].Respond(arguments));
            }
        }

        return Task.FromResult(Simulate(arguments));
    }

    private GitResult Simulate(IReadOnlyList<string> a)
    {
        GitResult Ok(string stdout = "") => new(a, 0, stdout, "");
        GitResult Exit(int code) => new(a, code, "", "");

        switch (a[0])
        {
            case "rev-parse" when a.Contains("--is-inside-work-tree"):
                return Ok("true\n");
            case "rev-parse" when a.Contains("--verify"):
                var reference = a[^1].Replace("^{commit}", "", StringComparison.Ordinal);
                return Refs.TryGetValue(reference, out var sha) ? Ok(sha + "\n") : Exit(1);
            case "symbolic-ref":
                return CurrentBranch is null ? Exit(1) : Ok($"refs/heads/{CurrentBranch}\n");
            case "for-each-ref" when a.Contains("--points-at"):
                var target = a[2];
                return Ok(string.Concat(Refs.Where(r => r.Key.StartsWith("refs/heads/", StringComparison.Ordinal) && r.Value == target).Select(r => r.Key + "\n")));
            case "for-each-ref" when a.Contains("--format=%(upstream)"):
                return Ok();
            case "for-each-ref":
                return Ok(string.Concat(Refs.Keys.Where(k => k.StartsWith("refs/heads/", StringComparison.Ordinal)).Order(StringComparer.Ordinal).Select(k => k + "\n")));
            case "remote":
                return Ok(string.Concat(Remotes.Select(r => r + "\n")));
            case "status":
                return Ok(Status);
            case "diff" when a.Contains("--diff-filter=U"):
                return Ok(string.Concat(ConflictedFiles.Select(f => f + "\0")));
            case "merge-base":
                return Ancestors.Contains((a[2], a[3])) ? Exit(0) : Exit(1);
            case "config" when a.Contains("--get-regexp"):
                if (a.Contains("--global") || Config.Count == 0)
                {
                    return Exit(1);
                }

                return Ok(string.Concat(Config.Select(kv => $"{kv.Key}\n{kv.Value}\0")));
            case "switch":
                CurrentBranch = a[^1] == a[1] ? a[1] : a[2]; // "switch x" or "switch -c x base"
                return Ok();
            default:
                return Ok();
        }
    }
}
