using GitFlow.VSExtension.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.Extensibility;

namespace GitFlow.VSExtension;

/// <summary>Entry point of the out-of-process GitFlow extension.</summary>
[VisualStudioContribution]
internal sealed class ExtensionEntrypoint : Extension
{
    public override ExtensionConfiguration ExtensionConfiguration => new()
    {
        // The id never changes: Visual Studio uses it to recognise updates of an installed extension.
        Metadata = new(
            id: "GitFlowVS.3f6c2a8e-6a0e-4d0b-9a7c-1f5e2b7d4c91",
            version: this.ExtensionAssemblyVersion,
            publisherName: "DNA666777",
            displayName: "GitFlow",
            description: "Gitflow standard (feature, release, hotfix) sans quitter Visual Studio : démarrer, terminer, publier en un clic, "
                + "merge local ou Pull Request (GitHub, Azure DevOps), message de commit généré, tags de version.")
        {
            Preview = false,
            Icon = "Resources\\icon.png",
            PreviewImage = "Resources\\preview.png",
            License = "LICENSE.txt",
            MoreInfo = "https://github.com/DNA666777/GitFlow",
            ReleaseNotes = "https://github.com/DNA666777/GitFlow/releases",
            Tags = ["git", "gitflow", "git-flow", "branching", "release", "hotfix", "feature", "pull request", "azure devops", "github"],
        },
    };

    protected override void InitializeServices(IServiceCollection serviceCollection)
    {
        base.InitializeServices(serviceCollection);
        serviceCollection.AddSingleton<GitFlowOutput>();
        serviceCollection.AddSingleton<GitFlowSession>();
    }
}
