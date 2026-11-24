using GitFlow.Core.Config;
using GitFlow.Core.Flow;
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
        Flow = new FlowService(runner);
        Commits = new CommitService(runner);
        Settings = new GitFlowSettingsService(runner);
    }

    public GitFlowSettingsService Settings { get; }

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
