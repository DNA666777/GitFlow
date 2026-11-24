namespace GitFlow.Core.Git;

/// <summary>
/// The single gateway to git. Arguments are always passed as a list (never concatenated)
/// so that branch names cannot inject options or shell syntax.
/// </summary>
public interface IGitRunner
{
    Task<GitResult> RunAsync(string workingDirectory, IReadOnlyList<string> arguments, CancellationToken cancellationToken);

    /// <summary>
    /// Same, writing <paramref name="standardInput"/> to git (e.g. <c>git credential fill</c>).
    /// Standard input and standard output are never logged: they can carry credentials.
    /// </summary>
    Task<GitResult> RunAsync(string workingDirectory, IReadOnlyList<string> arguments, string? standardInput, CancellationToken cancellationToken) =>
        standardInput is null
            ? RunAsync(workingDirectory, arguments, cancellationToken)
            : throw new NotSupportedException("This runner does not support standard input.");
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
