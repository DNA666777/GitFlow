using System.Globalization;
using GitFlow.Core.Git;
using Microsoft.VisualStudio.Extensibility;
using Microsoft.VisualStudio.Extensibility.Documents;

namespace GitFlow.VSExtension.Services;

/// <summary>
/// "GitFlow" Output pane. Every git command is written there (command, exit code, stderr).
/// </summary>
internal sealed class GitFlowOutput : IGitCommandLog, IDisposable
{
    private readonly SemaphoreSlim _createLock = new(1, 1);
    private readonly object _writeLock = new();
    private OutputChannel? _channel;

    public async Task EnsureChannelAsync(VisualStudioExtensibility extensibility, CancellationToken ct)
    {
        if (_channel is not null)
        {
            return;
        }

        await _createLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            // Creating two channels with the same name throws: create once per extension lifetime.
            if (_channel is null)
            {
                _channel = await extensibility.Views().Output.CreateOutputChannelAsync(Strings.OutputPaneTitle, ct).ConfigureAwait(false);

                // Diagnostic: the UI language drives the texts (English by default, French for "fr").
                WriteLine($"GitFlow — UI culture: {CultureInfo.CurrentUICulture.Name}");
            }
        }
        finally
        {
            _createLock.Release();
        }
    }

    public void WriteLine(string message)
    {
        var channel = _channel;
        if (channel is null)
        {
            return;
        }

        lock (_writeLock)
        {
            channel.Writer.WriteLine(message);
            channel.Writer.Flush();
        }
    }

    public void Log(GitCommandLogEntry entry)
    {
        var time = DateTime.Now.ToString("HH:mm:ss", CultureInfo.InvariantCulture);
        var line = $"[{time}] {entry.Result.CommandLine}  → {entry.Result.ExitCode} ({entry.Duration.TotalMilliseconds:0} ms)";
        var stderr = entry.Result.StandardError.Trim();
        WriteLine(stderr.Length == 0 ? line : line + Environment.NewLine + Indent(stderr));
    }

    public void Dispose()
    {
        _channel?.Dispose();
        _createLock.Dispose();
    }

    private static string Indent(string text) =>
        string.Join(Environment.NewLine, text.Split('\n').Select(l => "    " + l.TrimEnd('\r')));
}
