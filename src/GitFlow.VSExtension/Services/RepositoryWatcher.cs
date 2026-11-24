using GitFlow.Core.Git;

namespace GitFlow.VSExtension.Services;

/// <summary>
/// Watches a repository (work tree + branch refs) and calls back, debounced, when something changed.
/// Files written by git's own read commands (index, *.lock, logs) and build output are ignored,
/// so a refresh never triggers another refresh.
/// </summary>
internal sealed class RepositoryWatcher(Action changed) : IDisposable
{
    private static readonly TimeSpan Debounce = TimeSpan.FromMilliseconds(700);

    private readonly object _lock = new();
    private FileSystemWatcher? _watcher;
    private Timer? _timer;

    public void Watch(string? repository)
    {
        lock (_lock)
        {
            _watcher?.Dispose();
            _watcher = null;
            if (repository is null || !Directory.Exists(repository))
            {
                return;
            }

            _timer ??= new Timer(_ => changed(), null, Timeout.Infinite, Timeout.Infinite);
            var watcher = new FileSystemWatcher(repository)
            {
                IncludeSubdirectories = true,
                NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName | NotifyFilters.LastWrite,
                InternalBufferSize = 64 * 1024,
            };
            watcher.Changed += (_, e) => OnEvent(repository, e.FullPath);
            watcher.Created += (_, e) => OnEvent(repository, e.FullPath);
            watcher.Deleted += (_, e) => OnEvent(repository, e.FullPath);
            watcher.Renamed += (_, e) => OnEvent(repository, e.FullPath);
            watcher.Error += (_, _) => Schedule();
            watcher.EnableRaisingEvents = true;
            _watcher = watcher;
        }
    }

    public void Dispose()
    {
        lock (_lock)
        {
            _watcher?.Dispose();
            _timer?.Dispose();
            _watcher = null;
            _timer = null;
        }
    }

    private void OnEvent(string repository, string fullPath)
    {
        if (RepositoryChangeFilter.IsRelevant(repository, fullPath))
        {
            Schedule();
        }
    }

    private void Schedule()
    {
        lock (_lock)
        {
            _timer?.Change(Debounce, Timeout.InfiniteTimeSpan);
        }
    }
}
