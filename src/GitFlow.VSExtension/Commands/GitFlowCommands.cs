using GitFlow.Core.Model;
using GitFlow.VSExtension.Services;
using GitFlow.VSExtension.ToolWindows;
using Microsoft.VisualStudio.Extensibility;
using Microsoft.VisualStudio.Extensibility.Commands;

namespace GitFlow.VSExtension.Commands;

/// <summary>Extensions &gt; GitFlow submenu.</summary>
internal static class GitFlowMenu
{
    [VisualStudioContribution]
    public static MenuConfiguration Menu => new("%GitFlow.VSExtension.Menu.DisplayName%")
    {
        Placements = [CommandPlacement.KnownPlacements.ExtensionsMenu],
        Children =
        [
            MenuChild.Command<ShowGitFlowWindowCommand>(),
            MenuChild.Separator,
            MenuChild.Command<StartFeatureCommand>(),
            MenuChild.Command<StartReleaseCommand>(),
            MenuChild.Command<StartHotfixCommand>(),
            MenuChild.Separator,
            MenuChild.Command<FinishCommand>(),
            MenuChild.Command<PublishCommand>(),
            MenuChild.Separator,
            MenuChild.Command<SettingsCommand>(),
            MenuChild.Command<InitCommand>(),
        ],
    };
}

[VisualStudioContribution]
internal sealed class SettingsCommand(GitFlowSession session) : GitFlowCommand(session)
{
    public override CommandConfiguration CommandConfiguration => new("%GitFlow.VSExtension.SettingsCommand.DisplayName%");

    protected override Task ExecuteAsync(FlowUi ui, string repository, CancellationToken ct) => ui.SettingsAsync(repository, ct);
}

/// <summary>Opens the GitFlow tool window (also under View &gt; Other Windows).</summary>
[VisualStudioContribution]
internal sealed class ShowGitFlowWindowCommand : Command
{
    public override CommandConfiguration CommandConfiguration => new("%GitFlow.VSExtension.ShowGitFlowWindowCommand.DisplayName%")
    {
        Placements = [CommandPlacement.KnownPlacements.ViewOtherWindowsMenu],
    };

    public override Task ExecuteCommandAsync(IClientContext context, CancellationToken cancellationToken) =>
        Extensibility.Shell().ShowToolWindowAsync<GitFlowToolWindow>(activate: true, cancellationToken);
}

[VisualStudioContribution]
internal sealed class InitCommand(GitFlowSession session) : GitFlowCommand(session)
{
    public override CommandConfiguration CommandConfiguration => new("%GitFlow.VSExtension.InitCommand.DisplayName%");

    protected override Task ExecuteAsync(FlowUi ui, string repository, CancellationToken ct) => ui.InitAsync(repository, ct);
}

internal abstract class StartCommand(GitFlowSession session, BranchType type) : GitFlowCommand(session)
{
    protected override Task ExecuteAsync(FlowUi ui, string repository, CancellationToken ct) =>
        ui.StartAsync(repository, type, name: null, showSuccess: true, ct);
}

[VisualStudioContribution]
internal sealed class StartFeatureCommand(GitFlowSession session) : StartCommand(session, BranchType.Feature)
{
    public override CommandConfiguration CommandConfiguration => new("%GitFlow.VSExtension.StartFeatureCommand.DisplayName%");
}

[VisualStudioContribution]
internal sealed class StartReleaseCommand(GitFlowSession session) : StartCommand(session, BranchType.Release)
{
    public override CommandConfiguration CommandConfiguration => new("%GitFlow.VSExtension.StartReleaseCommand.DisplayName%");
}

[VisualStudioContribution]
internal sealed class StartHotfixCommand(GitFlowSession session) : StartCommand(session, BranchType.Hotfix)
{
    public override CommandConfiguration CommandConfiguration => new("%GitFlow.VSExtension.StartHotfixCommand.DisplayName%");
}

[VisualStudioContribution]
internal sealed class FinishCommand(GitFlowSession session) : GitFlowCommand(session)
{
    public override CommandConfiguration CommandConfiguration => new("%GitFlow.VSExtension.FinishCommand.DisplayName%");

    protected override Task ExecuteAsync(FlowUi ui, string repository, CancellationToken ct) =>
        ui.FinishAsync(repository, branchName: null, showSuccess: true, ct);
}

[VisualStudioContribution]
internal sealed class PublishCommand(GitFlowSession session) : GitFlowCommand(session)
{
    public override CommandConfiguration CommandConfiguration => new("%GitFlow.VSExtension.PublishCommand.DisplayName%");

    protected override Task ExecuteAsync(FlowUi ui, string repository, CancellationToken ct) =>
        ui.PublishAsync(repository, branchName: null, showSuccess: true, ct);
}
