using GitFlow.Core.Git;
using GitFlow.Core.PullRequests;
using GitFlow.Server;

// GitFlow server for the VS Code extension: JSON-RPC on stdin/stdout.
// stdout carries the protocol only: diagnostics go to stderr.
try
{
    using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };

    // Windows credentials (NTLM / Kerberos) only for an Azure DevOps Server for which git has no secret.
    using var windowsHttp = new HttpClient(new HttpClientHandler { UseDefaultCredentials = true }) { Timeout = TimeSpan.FromSeconds(30) };

    using var rpc = RpcHost.Create(
        Console.OpenStandardOutput(),
        Console.OpenStandardInput(),
        log => new GitCliRunner(log),
        runner => new PullRequestService(runner, http, windowsHttp));
    rpc.StartListening();
    await rpc.Completion.ConfigureAwait(false);
    return 0;
}
catch (Exception ex)
{
    await Console.Error.WriteLineAsync("GitFlow.Server: " + ex).ConfigureAwait(false);
    return 1;
}
