namespace GitFlow.Core.Git;

/// <summary>Outcome of a single git invocation.</summary>
public sealed record GitResult(
    IReadOnlyList<string> Arguments,
    int ExitCode,
    string StandardOutput,
    string StandardError)
{
    public bool Success => ExitCode == 0;

    /// <summary>Human readable command line, for logs only (never executed).</summary>
    public string CommandLine => "git " + string.Join(' ', Arguments.Select(Quote));

    private static string Quote(string arg) =>
        arg.Length == 0 || arg.Any(c => char.IsWhiteSpace(c) || c == '"' || c == '\'')
            ? "\"" + arg.Replace("\"", "\\\"") + "\""
            : arg;
}
