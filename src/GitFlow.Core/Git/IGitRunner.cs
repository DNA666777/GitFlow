namespace GitFlow.Core.Git;

/// <summary>
/// The single gateway to git. Arguments are always passed as a list (never concatenated)
/// so that branch names cannot inject options or shell syntax.
/// </summary>
public interface IGitRunner
{
    Task<GitResult> RunAsync(string workingDirectory, IReadOnlyList<string> arguments, CancellationToken cancellationToken);
}

/// <summary>Receives every executed git command (command line, exit code, stderr).</summary>
public interface IGitCommandLog
{
    void Log(GitCommandLogEntry entry);
}

public sealed record GitCommandLogEntry(string WorkingDirectory, GitResult Result, TimeSpan Duration);

/// <summary>Thrown when a git command that is expected to succeed fails unexpectedly.</summary>
public sealed class GitCommandException(GitResult result)
    : Exception($"{result.CommandLine} failed with exit code {result.ExitCode}: {result.StandardError.Trim()}")
{
    public GitResult Result { get; } = result;
}
