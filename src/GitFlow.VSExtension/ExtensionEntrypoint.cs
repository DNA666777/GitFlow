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
        Metadata = new(
            id: "GitFlowVS.3f6c2a8e-6a0e-4d0b-9a7c-1f5e2b7d4c91",
            version: this.ExtensionAssemblyVersion,
            publisherName: "GitFlowVS",
            displayName: "GitFlow",
            description: "Gitflow standard (feature, release, hotfix) sans quitter Visual Studio."),
    };

    protected override void InitializeServices(IServiceCollection serviceCollection)
    {
        base.InitializeServices(serviceCollection);
        serviceCollection.AddSingleton<GitFlowOutput>();
        serviceCollection.AddSingleton<GitFlowSession>();
    }
}
