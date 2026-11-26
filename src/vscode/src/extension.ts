import * as path from 'node:path';
import * as vscode from 'vscode';
import { RepoState } from './protocol';
import { GitFlowServer } from './server';

/** Returned by activate(): used by the extension tests. */
export interface GitFlowApi {
  server: GitFlowServer;
  findRepository(): Promise<string | undefined>;
}

export function activate(context: vscode.ExtensionContext): GitFlowApi {
  const output = vscode.window.createOutputChannel('GitFlow');
  const server = new GitFlowServer(context.asAbsolutePath(path.join('server', 'GitFlow.Server.dll')), output);
  context.subscriptions.push(output, server);

  const api: GitFlowApi = {
    server,
    findRepository: () => findRepository(server),
  };

  context.subscriptions.push(
    vscode.commands.registerCommand('gitflow.showOutput', () => output.show(true)),
    vscode.commands.registerCommand('gitflow.restartServer', () => run(output, () => server.restart())),
    vscode.commands.registerCommand('gitflow.showState', () => run(output, async () => {
      const repository = await api.findRepository();
      if (!repository) {
        void vscode.window.showWarningMessage(vscode.l10n.t('No Git repository found for the open folder or file.'));
        return;
      }

      const state = await server.request<RepoState | null>('getState', { repository });
      output.appendLine(JSON.stringify(state, null, 2));
      void vscode.window.showInformationMessage(describe(state));
    })),
  );

  return api;
}

export function deactivate(): void {
  // The server is disposed with the subscriptions.
}

/** Repository of the active file, otherwise of the first workspace folder. */
async function findRepository(server: GitFlowServer): Promise<string | undefined> {
  const editor = vscode.window.activeTextEditor?.document.uri;
  const candidates = [
    editor?.scheme === 'file' ? path.dirname(editor.fsPath) : undefined,
    ...(vscode.workspace.workspaceFolders ?? []).map(f => f.uri.fsPath),
  ].filter((p): p is string => !!p);

  for (const candidate of candidates) {
    const root = await server.request<string | null>('findRepository', { path: candidate });
    if (root) {
      return root;
    }
  }

  return undefined;
}

function describe(state: RepoState | null): string {
  if (!state) {
    return vscode.l10n.t('No Git repository found for the open folder or file.');
  }

  if (!state.isInitialized) {
    return vscode.l10n.t('GitFlow is not initialized in this repository.');
  }

  const branch = state.currentBranch ?? 'HEAD';
  const type = state.currentFlowBranch?.type ?? '—';
  return vscode.l10n.t('Branch {0} ({1}), {2} work branch(es) in progress.', branch, type, state.flowBranches.length);
}

/** Never silent: an unexpected error is shown and written to the GitFlow log. */
async function run(output: vscode.OutputChannel, action: () => Promise<void>): Promise<void> {
  try {
    await action();
  } catch (error) {
    const message = error instanceof Error ? error.message : String(error);
    output.appendLine(error instanceof Error && error.stack ? error.stack : message);
    void vscode.window.showErrorMessage(vscode.l10n.t('GitFlow: unexpected error. {0}', message));
  }
}
