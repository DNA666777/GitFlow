using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using GitFlow.Core.Git;
using GitFlow.Core.PullRequests;
using StreamJsonRpc;

namespace GitFlow.Server;

/// <summary>
/// Wires <see cref="GitFlowRpc"/> to a JSON-RPC connection with the framing of vscode-jsonrpc
/// (Content-Length headers) and camelCase JSON.
/// </summary>
public static class RpcHost
{
    public static JsonSerializerOptions JsonOptions { get; } = CreateJsonOptions();

    /// <summary>Creates the connection (not yet listening) over a duplex stream, e.g. the process stdin/stdout.</summary>
    /// <param name="createRunner">Builds the git runner from the log that forwards each command to the client.</param>
    public static JsonRpc Create(
        Stream sending,
        Stream receiving,
        Func<IGitCommandLog, IGitRunner> createRunner,
        Func<IGitRunner, PullRequestService> createPullRequests)
    {
        var formatter = new SystemTextJsonFormatter();
        formatter.JsonSerializerOptions = JsonOptions;
        var rpc = new JsonRpc(new HeaderDelimitedMessageHandler(sending, receiving, formatter));

        var log = new NotifyingLog(rpc);
        var runner = createRunner(log);
        rpc.AddLocalRpcTarget(new GitFlowRpc(runner, createPullRequests(runner)), new JsonRpcTargetOptions
        {
            // Unknown or wrong parameters must fail loudly during development of the extension.
            AllowNonPublicInvocation = false,
        });
        return rpc;
    }

    private static JsonSerializerOptions CreateJsonOptions()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web)
        {
            DefaultIgnoreCondition = JsonIgnoreCondition.Never,
        };
        options.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase));
        return options;
    }

    /// <summary>Every git command (command line, exit code, duration, stderr) goes to the client as a "log" notification.</summary>
    private sealed class NotifyingLog(JsonRpc rpc) : IGitCommandLog
    {
        public void Log(GitCommandLogEntry entry)
        {
            var time = DateTime.Now.ToString("HH:mm:ss", CultureInfo.InvariantCulture);
            var line = $"[{time}] {entry.Result.CommandLine}  → {entry.Result.ExitCode} ({entry.Duration.TotalMilliseconds:0} ms)";
            var stderr = entry.Result.StandardError.Trim();
            if (stderr.Length > 0)
            {
                line += Environment.NewLine + string.Join(Environment.NewLine, stderr.Split('\n').Select(l => "    " + l.TrimEnd('\r')));
            }

            // Fire and forget: logging must never block nor fail an operation.
            _ = rpc.NotifyWithParameterObjectAsync("log", new LogNotification(line, GitFlowRpc.IsReading ? "trace" : "info")).ContinueWith(
                t => _ = t.Exception, CancellationToken.None, TaskContinuationOptions.OnlyOnFaulted, TaskScheduler.Default);
        }
    }
}
