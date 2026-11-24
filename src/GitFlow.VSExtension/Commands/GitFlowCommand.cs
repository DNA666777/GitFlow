using GitFlow.VSExtension.Services;
using Microsoft.VisualStudio.Extensibility;
using Microsoft.VisualStudio.Extensibility.Commands;

namespace GitFlow.VSExtension.Commands;

/// <summary>Menu command: locates the repository, then delegates to <see cref="FlowUi"/>.</summary>
internal abstract class GitFlowCommand(GitFlowSession session) : Command
{
    protected GitFlowSession Session { get; } = session;

    public sealed override async Task ExecuteCommandAsync(IClientContext context, CancellationToken cancellationToken)
    {
        var ui = new FlowUi(Extensibility, Session);
        try
        {
            await Session.Output.EnsureChannelAsync(Extensibility, cancellationToken);
            var repository = await RepositoryLocator.FindAsync(Extensibility, Session.Flow, context, cancellationToken);
            if (repository is null)
            {
                await ui.ErrorAsync(Strings.NoRepository, cancellationToken);
                return;
            }

            await ExecuteAsync(ui, repository, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            // Cancelled by Visual Studio: nothing to report.
        }
        catch (Exception ex)
        {
            await ui.ReportUnexpectedAsync(ex);
        }
    }

    protected abstract Task ExecuteAsync(FlowUi ui, string repository, CancellationToken ct);
}
