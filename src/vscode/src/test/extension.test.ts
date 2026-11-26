import * as assert from 'node:assert';
import * as fs from 'node:fs';
import * as path from 'node:path';
import * as vscode from 'vscode';
import type { GitFlowApi } from '../extension';
import type { RepoState } from '../protocol';

const EXTENSION_ID = 'DNA666777.gitflow-vscode';

async function api(): Promise<GitFlowApi> {
  const extension = vscode.extensions.getExtension<GitFlowApi>(EXTENSION_ID);
  assert.ok(extension, 'extension installed in the test instance');
  return extension.activate();
}

suite('GitFlow extension', () => {
  test('activates on a workspace containing a git repository', async () => {
    await api();
    assert.ok(vscode.extensions.getExtension(EXTENSION_ID)?.isActive);
  });

  test('registers its commands', async () => {
    await api();
    const commands = await vscode.commands.getCommands(true);
    for (const id of ['gitflow.showOutput', 'gitflow.showState', 'gitflow.restartServer']) {
      assert.ok(commands.includes(id), id);
    }
  });

  test('starts the server and talks to it', async () => {
    const gitflow = await api();
    await gitflow.server.start();

    assert.ok(gitflow.server.info?.version, 'server version');
    const repository = await gitflow.findRepository();
    const workspace = vscode.workspace.workspaceFolders![0].uri.fsPath;
    // Windows: VS Code writes the drive letter in lower case (c:\), git in upper case (C:\).
    const same = (p: string) => (process.platform === 'win32' ? fs.realpathSync(p).toLowerCase() : fs.realpathSync(p));
    assert.strictEqual(same(repository!), same(workspace));

    // Independent of the order of the suites (the command tests initialize this repository).
    const state = await gitflow.server.request<RepoState | null>('getState', { repository });
    assert.ok(state);
    assert.strictEqual(same(state.repositoryPath), same(workspace));
    assert.ok(state.hasCommits);
  });

  test('restarts the server', async () => {
    const gitflow = await api();
    await vscode.commands.executeCommand('gitflow.restartServer');
    const root = await gitflow.server.request<string | null>('findRepository', { path: path.dirname(vscode.workspace.workspaceFolders![0].uri.fsPath) });
    assert.strictEqual(root, null, 'the temporary folder above the repository is not a repository');
  });
});
