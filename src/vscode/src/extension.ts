import * as path from 'node:path';
import * as vscode from 'vscode';
import { FlowCommands } from './flow';
import { BranchType } from './protocol';
import { GitFlowServer } from './server';
import { GitFlowPanel } from './panel';
import { SettingsEditor } from './settings';
import { VsCodeUi } from './ui';

const t = vscode.l10n.t;

/** Returned by activate(): used by the extension tests. */
export interface GitFlowApi {
  server: GitFlowServer;
  flow: FlowCommands;
  panel: GitFlowPanel;
  findRepository(): Promise<string | undefined>;
}

export function activate(context: vscode.ExtensionContext): GitFlowApi {
  const output = vscode.window.createOutputChannel('GitFlow', { log: true });
  const server = new GitFlowServer(context.asAbsolutePath(path.join('server', 'GitFlow.Server.dll')), output);
  const find = () => findRepository(server);
  let panel: GitFlowPanel | undefined;
  const flow = new FlowCommands(server, find, output, new VsCodeUi(), result => panel?.onResult(result));
  panel = new GitFlowPanel(context.extensionUri, server, flow, find, output);
  const settings = new SettingsEditor(context.extensionUri, server, flow, find, () => panel?.refresher.refresh());
  context.subscriptions.push(output, server, panel, settings, vscode.window.registerWebviewViewProvider('gitflow.branches', panel));

  context.subscriptions.push(
    // Init and Settings share the same screen, like in Visual Studio (Init when the repository is not initialized).
    vscode.commands.registerCommand('gitflow.init', () => settings.open()),
    vscode.commands.registerCommand('gitflow.settings', () => settings.open()),
    vscode.commands.registerCommand('gitflow.start', () => pickAndStart(flow)),
    vscode.commands.registerCommand('gitflow.startFeature', (name?: unknown) => flow.start('feature', typeof name === 'string' ? name : undefined)),
    vscode.commands.registerCommand('gitflow.startRelease', (name?: unknown) => flow.start('release', typeof name === 'string' ? name : undefined)),
    vscode.commands.registerCommand('gitflow.startHotfix', (name?: unknown) => flow.start('hotfix', typeof name === 'string' ? name : undefined)),
    vscode.commands.registerCommand('gitflow.finish', (branch?: unknown) => flow.finish(typeof branch === 'string' ? branch : undefined)),
    vscode.commands.registerCommand('gitflow.publish', (branch?: unknown) => flow.publish(typeof branch === 'string' ? branch : undefined)),
    vscode.commands.registerCommand('gitflow.refresh', () => panel?.refresher.refresh()),
    vscode.commands.registerCommand('gitflow.showOutput', () => output.show(true)),
    vscode.commands.registerCommand('gitflow.restartServer', async () => {
      try {
        await server.restart();
        panel?.refresher.refresh();
      } catch (error) {
        const message = error instanceof Error ? error.message : String(error);
        output.error(message);
        void vscode.window.showErrorMessage(t('GitFlow: unexpected error. {0}', message));
      }
    }),
  );

  watchRepository(context, panel);
  return { server, flow, panel, findRepository: find };
}

export function deactivate(): void {
  // The server is disposed with the subscriptions.
}

/** "Start…" from the view: choose the type, then the usual prompts. */
async function pickAndStart(flow: FlowCommands): Promise<unknown> {
  const types: { label: string; type: BranchType }[] = [
    { label: t('Feature'), type: 'feature' },
    { label: t('Release'), type: 'release' },
    { label: t('Hotfix'), type: 'hotfix' },
  ];
  const picked = await vscode.window.showQuickPick(types, { title: t('Start a branch'), placeHolder: t('Type of branch') });
  return picked ? flow.start(picked.type) : undefined;
}

/** Minimal part of the API of VS Code's built-in git extension. */
interface GitApi {
  repositories: { state: { onDidChange: vscode.Event<void> } }[];
  onDidOpenRepository: vscode.Event<{ state: { onDidChange: vscode.Event<void> } }>;
}

/**
 * Keeps the view up to date: changes seen by VS Code's git extension (branch, commits, index, merge),
 * saved files, another active editor (another repository), back to the window.
 */
function watchRepository(context: vscode.ExtensionContext, panel: GitFlowPanel): void {
  const later = () => panel.refresher.refresh(true);
  context.subscriptions.push(
    vscode.workspace.onDidSaveTextDocument(later),
    vscode.window.onDidChangeActiveTextEditor(later),
    vscode.workspace.onDidChangeWorkspaceFolders(later),
    vscode.window.onDidChangeWindowState(s => s.focused && later()),
  );

  void (async () => {
    try {
      const extension = vscode.extensions.getExtension<{ getAPI(version: 1): GitApi }>('vscode.git');
      const git = (await extension?.activate())?.getAPI(1);
      if (!git) {
        return;
      }

      const follow = (repository: { state: { onDidChange: vscode.Event<void> } }) =>
        context.subscriptions.push(repository.state.onDidChange(later));
      git.repositories.forEach(follow);
      context.subscriptions.push(git.onDidOpenRepository(repository => {
        follow(repository);
        later();
      }));
    } catch {
      // Without the git extension, the view still refreshes after each GitFlow operation and on save.
    }
  })();
}

/** True for a file inside a .git folder (e.g. the commit message being edited): not a working tree. */
export function isInsideGitFolder(file: string): boolean {
  return file.split(/[\\/]/).includes('.git');
}

/** Repository of the active file, otherwise of the first workspace folder. */
async function findRepository(server: GitFlowServer): Promise<string | undefined> {
  const editor = vscode.window.activeTextEditor?.document.uri;
  const candidates = [
    editor?.scheme === 'file' && !isInsideGitFolder(editor.fsPath) ? path.dirname(editor.fsPath) : undefined,
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
