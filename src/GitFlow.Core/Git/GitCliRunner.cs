using System.ComponentModel;
using System.Diagnostics;
using System.Text;

namespace GitFlow.Core.Git;

public sealed class GitCliRunnerOptions
{
    /// <summary>Path to the git executable. Defaults to "git" resolved from PATH.</summary>
    public string GitExecutable { get; init; } = "git";

    /// <summary>Extra environment variables (applied last, so they can override the defaults).</summary>
    public IReadOnlyDictionary<string, string?> EnvironmentVariables { get; init; } = new Dictionary<string, string?>();
}

/// <summary>Runs the git CLI (Git for Windows) as a child process.</summary>
public sealed class GitCliRunner(IGitCommandLog? log = null, GitCliRunnerOptions? options = null) : IGitRunner
{
    private readonly GitCliRunnerOptions _options = options ?? new GitCliRunnerOptions();

    public async Task<GitResult> RunAsync(string workingDirectory, IReadOnlyList<string> arguments, CancellationToken cancellationToken)
    {
        var startInfo = new ProcessStartInfo(_options.GitExecutable)
        {
            WorkingDirectory = workingDirectory,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };

        // Stable, parseable output whatever the user's locale and config.
        startInfo.ArgumentList.Add("-c");
        startInfo.ArgumentList.Add("core.quotepath=false");
        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        startInfo.Environment["LC_ALL"] = "C";
        startInfo.Environment["LANG"] = "C";
        startInfo.Environment["GIT_TERMINAL_PROMPT"] = "0"; // never hang waiting for console input
        startInfo.Environment["GIT_MERGE_AUTOEDIT"] = "no"; // never open an editor on merge
        foreach (var (key, value) in _options.EnvironmentVariables)
        {
            startInfo.Environment[key] = value;
        }

        var stopwatch = Stopwatch.StartNew();
        GitResult result;
        using (var process = new Process { StartInfo = startInfo })
        {
            try
            {
                process.Start();
            }
            catch (Win32Exception ex)
            {
                result = new GitResult(arguments, -1, string.Empty, $"Unable to start '{_options.GitExecutable}': {ex.Message}");
                log?.Log(new GitCommandLogEntry(workingDirectory, result, stopwatch.Elapsed));
                return result;
            }

            process.StandardInput.Close();
            var stdoutTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
            var stderrTask = process.StandardError.ReadToEndAsync(cancellationToken);

            try
            {
                await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                TryKill(process);
                throw;
            }

            var stdout = await stdoutTask.ConfigureAwait(false);
            var stderr = await stderrTask.ConfigureAwait(false);
            result = new GitResult(arguments, process.ExitCode, stdout, stderr);
        }

        log?.Log(new GitCommandLogEntry(workingDirectory, result, stopwatch.Elapsed));
        return result;
    }

    private static void TryKill(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch (InvalidOperationException)
        {
            // Already exited.
        }
    }
}
