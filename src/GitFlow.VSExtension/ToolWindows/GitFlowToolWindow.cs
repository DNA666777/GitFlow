using GitFlow.VSExtension.Services;
using Microsoft.VisualStudio.Extensibility;
using Microsoft.VisualStudio.Extensibility.ToolWindows;
using Microsoft.VisualStudio.RpcContracts.RemoteUI;

namespace GitFlow.VSExtension.ToolWindows;

/// <summary>"GitFlow" tool window, docked as a tab next to the Git Changes window.</summary>
[VisualStudioContribution]
internal sealed class GitFlowToolWindow : ToolWindow
{
    /// <summary>Git Changes window (Microsoft.TeamFoundation.Git.Provider.GitWindow.GitToolWindow).</summary>
    private static readonly Guid GitChangesToolWindow = new("1c64b9c2-e352-428e-a56d-0ace190b99a6");

    private readonly GitFlowToolWindowViewModel _viewModel;

    public GitFlowToolWindow(VisualStudioExtensibility extensibility, GitFlowSession session)
        : base(extensibility)
    {
        Title = Strings.ToolWindowTitle;
        _viewModel = new GitFlowToolWindowViewModel(extensibility, session);
    }

    public override ToolWindowConfiguration ToolWindowConfiguration => new()
    {
        Placement = ToolWindowPlacement.DockedTo(GitChangesToolWindow),
    };

    public override Task<IRemoteUserControl> GetContentAsync(CancellationToken cancellationToken) =>
        Task.FromResult<IRemoteUserControl>(new GitFlowToolWindowControl(_viewModel));

    public override async Task OnShowAsync(CancellationToken cancellationToken)
    {
        await base.OnShowAsync(cancellationToken);
        await _viewModel.SetVisibleAsync(true, cancellationToken);
    }

    public override async Task OnHideAsync(CancellationToken cancellationToken)
    {
        await base.OnHideAsync(cancellationToken);
        await _viewModel.SetVisibleAsync(false, cancellationToken);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _viewModel.Dispose();
        }

        base.Dispose(disposing);
    }
}

internal sealed class GitFlowToolWindowControl(GitFlowToolWindowViewModel dataContext) : Microsoft.VisualStudio.Extensibility.UI.RemoteUserControl(dataContext);
