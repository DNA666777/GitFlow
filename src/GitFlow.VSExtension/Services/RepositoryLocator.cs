using GitFlow.Core.Flow;
using Microsoft.VisualStudio.Extensibility;
using Microsoft.VisualStudio.ProjectSystem.Query;

namespace GitFlow.VSExtension.Services;

/// <summary>Finds the git repository the user is working in.</summary>
internal static class RepositoryLocator
{
    /// <summary>Path of the open solution, or null (no solution / Open Folder mode).</summary>
    public static async Task<string?> GetSolutionPathAsync(VisualStudioExtensibility extensibility, CancellationToken ct)
    {
        var solutions = await extensibility.Workspaces().QuerySolutionAsync(s => s.With(x => x.Path), ct);
        return solutions.Select(s => s.Path).FirstOrDefault(p => !string.IsNullOrWhiteSpace(p));
    }

    /// <summary>Repository of the open solution, else of the selected item, else of the active document.</summary>
    public static async Task<string?> FindAsync(VisualStudioExtensibility extensibility, FlowService flow, IClientContext? context, CancellationToken ct)
    {
        var candidates = new List<string>();
        if (await GetSolutionPathAsync(extensibility, ct) is { } solution)
        {
            candidates.Add(solution);
        }

        if (context is not null)
        {
            // Hints only: Visual Studio throws (e.g. UriFormatException) when the selection is not a file
            // (solution node, project, folder...). A failing hint is skipped, never fatal.
            if (await TryGetAsync<Uri>(async () => await context.GetSelectedPathAsync(ct)) is { IsFile: true } selected)
            {
                candidates.Add(selected.LocalPath);
            }

            var textView = await TryGetAsync(() => context.GetActiveTextViewAsync(ct));
            if (textView?.Document.Uri is { IsFile: true } document)
            {
                candidates.Add(document.LocalPath);
            }
        }

        foreach (var candidate in candidates.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (await flow.FindRepositoryRootAsync(candidate, ct) is { } root)
            {
                return root;
            }
        }

        return null;
    }

    private static async Task<T?> TryGetAsync<T>(Func<Task<T?>> hint)
        where T : class
    {
        try
        {
            return await hint();
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return null;
        }
    }
}
