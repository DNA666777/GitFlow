import * as vscode from 'vscode';

export const EXTENSION_ID = 'DNA666777.gitflow-for-vscode';

interface AcquireResult {
  dotnetPath: string;
}

/**
 * The `dotnet` executable that runs GitFlow.Server: the "gitflow.dotnetPath" setting when set,
 * otherwise a .NET 8 runtime provided (and downloaded once if needed) by the .NET Install Tool extension.
 */
export async function resolveDotnet(): Promise<string> {
  const configured = vscode.workspace.getConfiguration('gitflow').get<string>('dotnetPath')?.trim();
  if (configured) {
    return configured;
  }

  const result = await vscode.commands.executeCommand<AcquireResult | undefined>('dotnet.acquire', {
    version: '8.0',
    requestingExtensionId: EXTENSION_ID,
  });
  if (!result?.dotnetPath) {
    throw new Error(vscode.l10n.t('The .NET 8 runtime could not be obtained. See the ".NET Install Tool" output, or set "gitflow.dotnetPath".'));
  }

  return result.dotnetPath;
}
