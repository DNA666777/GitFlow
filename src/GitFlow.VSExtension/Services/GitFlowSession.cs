using GitFlow.Core.Config;
using GitFlow.Core.Flow;
using GitFlow.Core.PullRequests;
using GitFlow.Core.Git;

namespace GitFlow.VSExtension.Services;

/// <summary>Shared state of the extension: the flow service (git CLI logged to the Output pane) and an operation lock.</summary>
internal sealed class GitFlowSession : IDisposable
{
    private readonly SemaphoreSlim _operationLock = new(1, 1);

    public GitFlowSession(GitFlowOutput output)
    {
        Output = output;
        var runner = new GitCliRunner(output);
        PullRequests = new PullRequestService(runner, Http, WindowsHttp);
        Flow = new FlowService(runner, pullRequestStrategy: new PullRequestStrategy(PullRequests));
        Commits = new CommitService(runner);
        Settings = new GitFlowSettingsService(runner);
        Planner = new FinishPlanner(Flow, Settings, PullRequests);
    }

    /// <summary>One HttpClient for the extension lifetime (GitHub / Azure DevOps REST APIs).</summary>
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(30) };

    /// <summary>
    /// Sends the Windows session's credentials (NTLM / Kerberos) when the server asks for them: only used for an
    /// Azure DevOps Server for which git has no secret, like git itself inside a company network.
    /// </summary>
    private static readonly HttpClient WindowsHttp = new(new HttpClientHandler { UseDefaultCredentials = true }) { Timeout = TimeSpan.FromSeconds(30) };

    public PullRequestService PullRequests { get; }

    public GitFlowSettingsService Settings { get; }

    /// <summary>Prepares a Finish (shared with VS Code through the Core).</summary>
    public FinishPlanner Planner { get; }

    public GitFlowOutput Output { get; }

    public FlowService Flow { get; }

    public CommitService Commits { get; }

    /// <summary>Only one GitFlow operation at a time. Returns null when another one is running.</summary>
    public IDisposable? TryBeginOperation() =>
        _operationLock.Wait(0) ? new Releaser(_operationLock) : null;

    public void Dispose() => _operationLock.Dispose();

    private sealed class Releaser(SemaphoreSlim semaphore) : IDisposable
    {
        private int _released;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _released, 1) == 0)
            {
                semaphore.Release();
            }
        }
    }
}
